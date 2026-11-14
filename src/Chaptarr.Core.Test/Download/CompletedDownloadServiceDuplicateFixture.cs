using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace Chaptarr.Core.Test.Download
{
    [TestFixture]
    public class CompletedDownloadServiceDuplicateFixture
    {
        private const string OccupiedReason = ImportApprovedBooks.AdditionalCopyDestinationOccupiedRejectionPrefix + ": /books/Author/Title.epub. Current naming settings cannot represent a separate copy.";
        private string _testDirectory;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "chaptarr-download-duplicate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        private sealed class RecordingEventAggregator : IEventAggregator
        {
            public List<IEvent> Events { get; } = new();

            public void PublishEvent<TEvent>(TEvent @event)
                where TEvent : class, IEvent
            {
                Events.Add(@event);
            }
        }

        private sealed class StubProvideImportItemService : IProvideImportItemService
        {
            public DownloadClientItem ProvideImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt)
            {
                var clone = item.Clone();
                clone.OutputPath = new OsPath("/tmp/chaptarr-duplicate");
                return clone;
            }
        }

        private sealed class StubImportService : IDownloadedBooksImportService
        {
            public List<ImportResult> Results { get; set; } = new();

            public List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null, RemoteBook remoteBook = null, bool requireDefaultRootFolderForMissingAuthors = false)
                => Results;

            public List<ImportResult> ProcessFolder(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null, RemoteBook remoteBook = null, bool requireDefaultRootFolderForMissingAuthors = false)
                => throw new NotImplementedException();

            public List<ImportResult> ProcessFile(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null, RemoteBook remoteBook = null, bool requireDefaultRootFolderForMissingAuthors = false)
                => throw new NotImplementedException();
        }

        private sealed class PassthroughDownloadImportModeResolver : IDownloadImportModeResolver
        {
            public ImportMode Resolve(ImportMode requestedMode, DownloadClientItem downloadClientItem) => requestedMode;
            public DownloadImportPolicy ResolvePolicy(ImportMode requestedMode, DownloadClientItem downloadClientItem) => new(requestedMode, false);
            public bool ShouldPreserveDownloadClientItem(DownloadClientItem downloadClientItem) => false;
        }

        private sealed class StubAlreadyImported : ITrackedDownloadAlreadyImported
        {
            public bool IsImported(TrackedDownload trackedDownload, List<EntityHistory> historyItems) => false;
        }

        private ImportResult Rejected(int bookId, params string[] errors)
        {
            return CreateRejected(bookId, null, errors);
        }

        private ImportResult RejectedWithPath(int bookId, string path, params string[] errors)
        {
            return CreateRejected(bookId, path, errors);
        }

        private static ImportResult CreateRejected(int bookId, string path, string[] errors)
        {
            var author = new Author { Id = 7, Name = "Test Author" };
            var book = new Book { Id = bookId, AuthorId = 7, Author = author, Title = "Title " + bookId, MediaType = BookMediaType.Ebook };
            var localBook = new LocalBook
            {
                Author = author,
                Book = book,
                Edition = new Edition { Id = 20 + bookId, BookId = bookId },
                Path = path ?? $"/tmp/chaptarr-duplicate/{bookId}.epub",
                Size = path != null && File.Exists(path) ? new FileInfo(path).Length : 0,
                SceneName = book.Title,
                Quality = new QualityModel(Quality.EPUB)
            };

            return new ImportResult(new ImportDecision<LocalBook>(localBook), errors);
        }

        private static EntityHistory Imported(int bookId, string downloadId, string path = null, long size = 0)
        {
            var book = new Book { Id = bookId, Title = "Title " + bookId, AuthorId = 7 };
            return new EntityHistory
            {
                EventType = EntityHistoryEventType.BookFileImported,
                BookId = bookId,
                EditionId = 20 + bookId,
                DownloadId = downloadId,
                Date = DateTime.UtcNow,
                SourceTitle = book.Title,
                Quality = new QualityModel(Quality.EPUB),
                Data = path == null ? new Dictionary<string, string>() : new Dictionary<string, string>
                {
                    ["ImportedPath"] = path,
                    ["Size"] = size.ToString()
                }
            };
        }

        private string WritePayload(string fileName, string contents)
        {
            var path = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static (CompletedDownloadService Subject, TrackedDownload Download, RecordingEventAggregator Events) Create(
            List<ImportResult> results,
            List<EntityHistory> bookHistory,
            DownloadProtocol protocol = DownloadProtocol.Usenet)
        {
            var events = new RecordingEventAggregator();
            var history = new Mock<IHistoryService>();
            history.Setup(h => h.FindByDownloadId(It.IsAny<string>())).Returns(new List<EntityHistory>());
            history.Setup(h => h.GetByBook(It.IsAny<int>(), EntityHistoryEventType.BookFileImported))
                   .Returns((int id, EntityHistoryEventType? _) => bookHistory.Where(h => h.BookId == id).ToList());

            var download = new TrackedDownload
            {
                DownloadClient = 1,
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = "dup-download",
                    Title = "Duplicate.Release",
                    Status = DownloadItemStatus.Completed,
                    DownloadClientInfo = new DownloadClientItemClientInfo
                    {
                        Id = 1,
                        Name = "client",
                        Protocol = protocol
                    }
                },
                RemoteBook = new RemoteBook
                {
                    Author = new Author { Id = 7, Name = "Test Author" },
                    Books = results.Select(r => r.ImportDecision.Item.Book).ToList()
                },
                State = TrackedDownloadState.ImportPending
            };

            var subject = new CompletedDownloadService(
                events,
                history.Object,
                new StubProvideImportItemService(),
                new StubImportService { Results = results },
                new PassthroughDownloadImportModeResolver(),
                new StubAlreadyImported(),
                NoopFailedDownloadService.Instance,
                LogManager.GetCurrentClassLogger(),
                NoopDownloadClientFileSnapshotService.Instance);

            return (subject, download, events);
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_mark_duplicate_of_book_imported_from_another_download_as_imported()
        {
            var incomingPath = WritePayload("incoming.epub", "same payload");
            var importedPath = WritePayload("imported.epub", "same payload");
            var size = new FileInfo(incomingPath).Length;
            var (subject, download, events) = Create(
                new List<ImportResult> { RejectedWithPath(10, incomingPath, OccupiedReason) },
                new List<EntityHistory> { Imported(10, "other-download", importedPath, size) });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(download.StatusMessages, Is.Empty);
            Assert.That(events.Events, Has.One.InstanceOf<DownloadCompletedEvent>());
            Assert.That(events.Events, Has.None.InstanceOf<BookImportIncompleteEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_complete_duplicate_torrent_through_normal_completion_without_removing_it()
        {
            var incomingPath = WritePayload("incoming-torrent.epub", "same torrent payload");
            var importedPath = WritePayload("imported-torrent.epub", "same torrent payload");
            var size = new FileInfo(incomingPath).Length;
            var (subject, download, events) = Create(
                new List<ImportResult> { RejectedWithPath(10, incomingPath, OccupiedReason) },
                new List<EntityHistory> { Imported(10, "other-download", importedPath, size) },
                DownloadProtocol.Torrent);

            subject.Import(download);

            // Removal is left to the existing DownloadCompletedEvent handling, which honours CanBeRemoved
            // (still seeding) and the client's RemoveCompletedDownloads setting.
            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCanBeRemovedEvent>());
            Assert.That(events.Events, Has.None.InstanceOf<DownloadFailedEvent>());
            Assert.That(events.Events.OfType<DownloadCompletedEvent>().Single().TrackedDownload, Is.SameAs(download));
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_duplicate_rejection_when_book_has_no_file_imported_from_another_download()
        {
            var (subject, download, events) = Create(
                new List<ImportResult> { Rejected(10, OccupiedReason) },
                new List<EntityHistory>());

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
            Assert.That(events.Events, Has.Some.InstanceOf<BookImportIncompleteEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_duplicate_rejection_when_only_import_is_from_the_same_download()
        {
            var (subject, download, events) = Create(
                new List<ImportResult> { Rejected(10, OccupiedReason) },
                new List<EntityHistory> { Imported(10, "DUP-DOWNLOAD") });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_when_another_download_has_same_book_but_a_different_payload()
        {
            var incomingPath = WritePayload("incoming-different.epub", "new edition payload");
            var importedPath = WritePayload("imported-different.epub", "previous edition payload");
            var size = new FileInfo(incomingPath).Length;
            File.WriteAllText(importedPath, new string('x', (int)size));
            var (subject, download, events) = Create(
                new List<ImportResult> { RejectedWithPath(10, incomingPath, OccupiedReason) },
                new List<EntityHistory> { Imported(10, "other-download", importedPath, size) });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_group_collision_when_only_one_multipart_file_was_previously_imported()
        {
            // ImportApprovedBooks reports the first occupied destination for the group on every result.
            // A previous import of part 1 must not make a new, distinct part 2 look redundant.
            var incomingPartOne = WritePayload("incoming-part-1.mp3", "part one");
            var incomingPartTwo = WritePayload("incoming-part-2.mp3", "part two");
            var importedPartOne = WritePayload("imported-part-1.mp3", "part one");
            var size = new FileInfo(incomingPartOne).Length;
            var (subject, download, events) = Create(
                new List<ImportResult>
                {
                    RejectedWithPath(10, incomingPartOne, OccupiedReason),
                    RejectedWithPath(10, incomingPartTwo, OccupiedReason)
                },
                new List<EntityHistory> { Imported(10, "other-download", importedPartOne, size) });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_complete_multipart_duplicate_only_when_every_file_matches_a_prior_import()
        {
            var incomingPartOne = WritePayload("incoming-all-1.mp3", "part one");
            var incomingPartTwo = WritePayload("incoming-all-2.mp3", "part two");
            var importedPartOne = WritePayload("imported-all-1.mp3", "part one");
            var importedPartTwo = WritePayload("imported-all-2.mp3", "part two");
            var size = new FileInfo(incomingPartOne).Length;
            var (subject, download, events) = Create(
                new List<ImportResult>
                {
                    RejectedWithPath(10, incomingPartOne, OccupiedReason),
                    RejectedWithPath(10, incomingPartTwo, OccupiedReason)
                },
                new List<EntityHistory>
                {
                    Imported(10, "other-download-1", importedPartOne, size),
                    Imported(10, "other-download-2", importedPartTwo, size)
                });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(events.Events, Has.One.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_when_prior_import_is_for_a_different_edition()
        {
            var incomingPath = WritePayload("incoming-edition.epub", "same bytes");
            var importedPath = WritePayload("imported-edition.epub", "same bytes");
            var size = new FileInfo(incomingPath).Length;
            var priorImport = Imported(10, "other-download", importedPath, size);
            priorImport.EditionId++;
            var (subject, download, events) = Create(
                new List<ImportResult> { RejectedWithPath(10, incomingPath, OccupiedReason) },
                new List<EntityHistory> { priorImport });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_when_duplicate_rejection_is_mixed_with_another_rejection()
        {
            var (subject, download, events) = Create(
                new List<ImportResult>
                {
                    Rejected(10, OccupiedReason),
                    Rejected(11, "TAG_EXTRACTION_FAILED")
                },
                new List<EntityHistory> { Imported(10, "other-download"), Imported(11, "other-download") });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
            Assert.That(events.Events, Has.Some.InstanceOf<BookImportIncompleteEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_when_one_error_on_a_file_is_not_the_duplicate_reason()
        {
            var (subject, download, events) = Create(
                new List<ImportResult> { Rejected(10, OccupiedReason, "Import failed: boom") },
                new List<EntityHistory> { Imported(10, "other-download") });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }

        [Test, Platform(Exclude = "Win", Reason = "Test uses Unix paths")]
        public void should_block_when_second_book_has_no_import_from_another_download()
        {
            var (subject, download, events) = Create(
                new List<ImportResult>
                {
                    Rejected(10, OccupiedReason),
                    Rejected(11, OccupiedReason)
                },
                new List<EntityHistory> { Imported(10, "other-download") });

            subject.Import(download);

            Assert.That(download.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(events.Events, Has.None.InstanceOf<DownloadCompletedEvent>());
        }
    }
}
