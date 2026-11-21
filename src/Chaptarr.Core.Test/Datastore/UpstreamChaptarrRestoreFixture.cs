using System;
using System.IO;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace Chaptarr.Core.Test.Datastore
{
    [TestFixture]
    public class UpstreamChaptarrRestoreFixture
    {
        [Test]
        public void should_convert_a_divergent_history_on_a_separate_database_and_only_once()
        {
            using var work = new TempDatabaseDirectory();
            var migrations = new MigrationController(LogManager.GetCurrentClassLogger(), null);
            var source = Path.Combine(work.Path, "source.db");
            var target = Path.Combine(work.Path, "target.db");
            var connectionString = new SqliteConnectionStringBuilder { DataSource = source, Pooling = false }.ToString();
            migrations.Migrate(connectionString, new MigrationContext(MigrationType.Main, 107), DatabaseType.SQLite);
            using (var db = new SqliteConnection(connectionString))
            {
                db.Open();
                db.Execute("""
                    INSERT INTO VersionInfo (Version, Description) VALUES (108,'normalize_comparison_keys'), (109,'preserve_unknown_series_primary');
                    INSERT INTO Tags (Id, Label) VALUES (800, 'retained');
                    INSERT INTO Tags (Id, Label) VALUES (999, 'deleted');
                    DELETE FROM Tags WHERE Id = 999;
                    DROP TABLE book_trigrams;
                    """);
            }

            var digest = SHA256.HashData(File.ReadAllBytes(source));
            Assert.That(migrations.ConvertUpstreamBackup(source, target), Is.True);
            Assert.That(SHA256.HashData(File.ReadAllBytes(source)), Is.EqualTo(digest));
            using var converted = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            converted.Open();
            Assert.That(converted.ExecuteScalar<string>("SELECT Label FROM Tags WHERE Id=800"), Is.EqualTo("retained"));
            Assert.That(converted.ExecuteScalar<int>("SELECT seq FROM sqlite_sequence WHERE name='Tags'"), Is.EqualTo(999));
            Assert.That(converted.ExecuteScalar<string>("SELECT Description FROM VersionInfo WHERE Version=109"), Is.EqualTo("add_query_performance_indexes"));
            Assert.That(converted.ExecuteScalar<int>("SELECT count(*) FROM book_trigrams"), Is.Zero);
            var repeat = Path.Combine(work.Path, "repeat.db");
            Assert.That(migrations.ConvertUpstreamBackup(target, repeat), Is.False);
            Assert.That(File.Exists(repeat), Is.False);

            using var unknown = new SqliteConnection(connectionString);
            unknown.Open();
            unknown.Execute("INSERT INTO VersionInfo (Version, Description) VALUES (130, 'unknown_upstream')");
            Assert.That(() => migrations.ConvertUpstreamBackup(source, repeat), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.Exists(repeat), Is.False);
        }

        private sealed class TempDatabaseDirectory : IDisposable
        {
            public string Path { get; } = Directory.CreateTempSubdirectory("chaptarr-restore-").FullName;

            public void Dispose() => Directory.Delete(Path, true);
        }
    }
}
