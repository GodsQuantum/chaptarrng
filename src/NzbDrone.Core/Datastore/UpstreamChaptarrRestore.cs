using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NzbDrone.Core.Datastore
{
    internal static class UpstreamChaptarrRestore
    {
        // After migration 107, the two products assigned the same numbers to different changes.
        private static readonly Dictionary<long, string> UpstreamHistory = new()
        {
            [108] = "normalize_comparison_keys",
            [109] = "preserve_unknown_series_primary",
            [110] = "repair_edition_fts_deletion",
            [111] = "add_import_list_monitoring_options",
            [112] = "move_file_tag_cache",
            [113] = "rebuild_shared_comparison_keys",
            [114] = "rebuild_shared_comparison_keys_casefold",
            [115] = "rebuild_shared_comparison_keys_totality",
            [116] = "add_pending_author_sync_options",
            [117] = "enforce_single_monitored_edition",
            [118] = "add_library_title_search_index",
            [119] = "add_pending_unmonitored_books",
            [120] = "fold_header_author_index",
            [124] = "add_upgrade_confirmation_and_recording_override",
            [125] = "pending_import_format_origins",
            [126] = "provider_identity_projection",
            [127] = "unique_book_slugs_by_format",
            [128] = "retain_book_level_extra_files"
        };

        internal static bool TryConvert(string sourcePath, string destinationPath, Action<string> migrateCopy)
        {
            var sourceString = DatabaseLineageGuard.CreateSafeReadOnlyConnectionString(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            using var source = new SqliteConnection(sourceString);
            source.Open();
            if (!RequiresConversion(source))
            {
                return false;
            }

            using (new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            var targetString = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            using (var target = new SqliteConnection(targetString))
            {
                target.Open();
                source.BackupDatabase(target);
                PrepareCopy(target);
            }

            migrateCopy(targetString);
            using var converted = new SqliteConnection(targetString);
            converted.Open();
            using var check = converted.CreateCommand();
            check.CommandText = "PRAGMA integrity_check(1)";
            if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The converted Chaptarr backup failed SQLite integrity validation.");
            }

            check.CommandText = "PRAGMA foreign_key_check";
            if (check.ExecuteScalar() != null)
            {
                throw new InvalidDataException("The converted Chaptarr backup contains broken foreign keys.");
            }

            // Stage a self-contained main database, including commits from WAL-mode sources.
            check.CommandText = "PRAGMA journal_mode=DELETE";
            if (!string.Equals(check.ExecuteScalar()?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Could not finalize the converted Chaptarr backup.");
            }

            return true;
        }

        private static bool RequiresConversion(SqliteConnection source)
        {
            if (DatabaseLineageGuard.Detect(source, DatabaseType.SQLite) != DatabaseLineage.Chaptarr ||
                source.ExecuteScalar<int>("SELECT count(*) FROM pragma_table_info('VersionInfo') WHERE name = 'Description'") == 0)
            {
                return false;
            }

            var history = source.Query<(long Version, string Description)>(
                "SELECT Version, Description FROM VersionInfo WHERE Version >= 108 ORDER BY Version").ToList();
            if (!history.Any(row => UpstreamHistory.TryGetValue(row.Version, out var name) && row.Description == name))
            {
                return false;
            }

            var expected = UpstreamHistory.Where(row => row.Key <= history[^1].Version)
                                          .OrderBy(row => row.Key)
                                          .Select(row => (row.Key, row.Value));
            if (!expected.SequenceEqual(history))
            {
                throw new InvalidDataException("This upstream Chaptarr migration history is newer, incomplete, or mixed with ChaptarrNG migrations. Use an original backup from a supported upstream release (through v0.9.1000).");
            }

            return true;
        }

        // Only called on a private SQLite snapshot. Preserve library rows, IDs, sequences,
        // settings and upstream-only columns. Replace the source's derived search structures
        // and constraints that conflict with native edition and provider-alias writes.
        private static void PrepareCopy(SqliteConnection target)
        {
            using var tx = target.BeginTransaction();
            target.Execute("""
                DROP TRIGGER IF EXISTS edition_header_fts_ai;
                DROP TRIGGER IF EXISTS edition_header_fts_ad;
                DROP TRIGGER IF EXISTS edition_header_fts_au;
                DROP TABLE IF EXISTS edition_header_fts;
                DROP TRIGGER IF EXISTS edition_book_reference_insert;
                DROP TRIGGER IF EXISTS edition_book_reference_update;
                DROP TRIGGER IF EXISTS book_edition_reference_delete;
                DROP TRIGGER IF EXISTS book_edition_reference_update;
                DROP INDEX IF EXISTS IX_Editions_OneMonitoredPerBook;
                DROP INDEX IF EXISTS IX_Books_MediaType_TitleSlug;
                DROP INDEX IF EXISTS IX_ImportListExclusions_ForeignId_MediaType;
                CREATE TABLE IF NOT EXISTS book_trigrams (Id INTEGER PRIMARY KEY AUTOINCREMENT, BookId INTEGER NOT NULL, Trigram TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS author_trigrams (Id INTEGER PRIMARY KEY AUTOINCREMENT, AuthorId INTEGER NOT NULL, Trigram TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_book_trigrams_BookId ON book_trigrams(BookId);
                CREATE INDEX IF NOT EXISTS IX_book_trigrams_Trigram ON book_trigrams(Trigram);
                CREATE INDEX IF NOT EXISTS IX_author_trigrams_AuthorId ON author_trigrams(AuthorId);
                CREATE INDEX IF NOT EXISTS IX_author_trigrams_Trigram ON author_trigrams(Trigram);
                """, transaction: tx);

            foreach (var table in new[] { "Authors", "Books", "Editions" })
            {
                foreach (var operation in new[] { "insert", "update", "delete" })
                {
                    target.Execute($"DROP TRIGGER IF EXISTS {table}_provider_{operation}", transaction: tx);
                }
            }

            // Native migrations 3, 16 and 50 rebuild the author and edition FTS tables,
            // populate them from the library and install the matching native triggers.
            target.Execute("DELETE FROM VersionInfo WHERE Version >= 108 OR Version IN (3, 16, 50)", transaction: tx);
            tx.Commit();
        }
    }
}
