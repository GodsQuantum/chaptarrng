using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Services;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.BookImport.Services;
using NUnit.Framework;

namespace Chaptarr.Core.Test.MediaFiles
{
    [TestFixture]
    public class TrackedMultipartAudioRepairHelperFixture
    {
        private class EditionServiceProxy : DispatchProxy
        {
            public List<Edition> Editions { get; set; } = new();
            public int MissingEditionId { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IEditionService.GetEditionsByBook))
                {
                    return Editions;
                }

                if (targetMethod?.Name == nameof(IEditionService.GetEdition))
                {
                    var id = (int)args[0];
                    if (id == MissingEditionId)
                    {
                        throw new ModelNotFoundException(typeof(Edition), id);
                    }

                    return Editions.FirstOrDefault(edition => edition.Id == id)
                        ?? throw new ModelNotFoundException(typeof(Edition), id);
                }

                throw new NotImplementedException($"Test proxy does not implement IEditionService.{targetMethod?.Name}");
            }
        }

        private class HistoryServiceProxy : DispatchProxy
        {
            public List<EntityHistory> HistoryItems { get; set; } = new();

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == nameof(IHistoryService.FindByDownloadId))
                {
                    return HistoryItems;
                }

                throw new NotImplementedException($"Test proxy does not implement IHistoryService.{targetMethod?.Name}");
            }
        }

        [Test]
        public void should_fall_back_to_the_current_monitored_edition_when_grabbed_edition_was_removed()
        {
            var book = new Book { Id = 100 };
            var currentEdition = new Edition { Id = 102, BookId = book.Id, Monitored = true };
            var editionService = DispatchProxy.Create<IEditionService, EditionServiceProxy>();
            var editionProxy = (EditionServiceProxy)(object)editionService;
            editionProxy.Editions = new List<Edition> { currentEdition };
            editionProxy.MissingEditionId = 101;

            var historyService = DispatchProxy.Create<IHistoryService, HistoryServiceProxy>();
            ((HistoryServiceProxy)(object)historyService).HistoryItems = new List<EntityHistory>
            {
                new() { EventType = EntityHistoryEventType.Grabbed, BookId = book.Id, EditionId = 101, DownloadId = "download-1" }
            };

            var expected = TrackedMultipartAudioRepairHelper.ResolveExpectedTrackedEdition(
                book, "download-1", historyService, editionService);

            Assert.That(expected, Is.SameAs(currentEdition));
        }

        [Test]
        public void should_return_no_pinned_edition_when_grabbed_edition_was_removed_and_no_current_edition_is_monitored()
        {
            var book = new Book { Id = 100 };
            var currentEdition = new Edition { Id = 102, BookId = book.Id, Monitored = false };
            var editionService = DispatchProxy.Create<IEditionService, EditionServiceProxy>();
            var editionProxy = (EditionServiceProxy)(object)editionService;
            editionProxy.Editions = new List<Edition> { currentEdition };
            editionProxy.MissingEditionId = 101;

            var historyService = DispatchProxy.Create<IHistoryService, HistoryServiceProxy>();
            ((HistoryServiceProxy)(object)historyService).HistoryItems = new List<EntityHistory>
            {
                new() { EventType = EntityHistoryEventType.Grabbed, BookId = book.Id, EditionId = 101, DownloadId = "download-1" }
            };

            var expected = TrackedMultipartAudioRepairHelper.ResolveExpectedTrackedEdition(
                book, "download-1", historyService, editionService);

            Assert.That(expected, Is.Null);
        }
    }
}
