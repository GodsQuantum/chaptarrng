using System.Collections.Generic;
using System.Linq;
using Chaptarr.Api.V1.History;
using Chaptarr.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;

namespace Chaptarr.Core.Test.Api
{
    [TestFixture]
    public class HistoryControllerDownloadIdFixture
    {
        private PagingSpec<EntityHistory> GetHistoryFilterFor(string downloadId)
        {
            PagingSpec<EntityHistory> captured = null;
            var historyService = new Mock<IHistoryService>();
            historyService
                .Setup(s => s.Paged(It.IsAny<PagingSpec<EntityHistory>>(), It.IsAny<BookMediaType?>()))
                .Callback<PagingSpec<EntityHistory>, BookMediaType?>((spec, _) => captured = spec)
                .Returns<PagingSpec<EntityHistory>, BookMediaType?>((spec, _) =>
                {
                    spec.Records = new List<EntityHistory>();
                    return spec;
                });

            var controller = new HistoryController(
                historyService.Object,
                Mock.Of<ICustomFormatCalculationService>(),
                Mock.Of<IUpgradableSpecification>(),
                Mock.Of<IFailedDownloadService>(),
                Mock.Of<IAuthorService>());

            controller.GetHistory(new PagingRequestResource { Page = 1, PageSize = 10 }, false, false, null, null, downloadId);

            Assert.That(captured, Is.Not.Null);
            return captured;
        }

        private static bool Matches(PagingSpec<EntityHistory> spec, string storedDownloadId)
        {
            var row = new EntityHistory { DownloadId = storedDownloadId };
            return spec.FilterExpressions.All(filter => filter.Compile()(row));
        }

        [Test]
        public void lower_case_download_id_should_find_the_upper_case_history_row()
        {
            var spec = GetHistoryFilterFor("729bfa6f-5778-4e63-8074-0a1b2c3d4e5f");

            Assert.That(Matches(spec, "729BFA6F-5778-4E63-8074-0A1B2C3D4E5F"), Is.True);
        }

        [Test]
        public void download_id_filter_should_still_exclude_other_downloads()
        {
            var spec = GetHistoryFilterFor("729bfa6f-5778-4e63-8074-0a1b2c3d4e5f");

            Assert.That(Matches(spec, "1EACBC44-9AAA-4807-AFBD-000000000000"), Is.False);
        }
    }
}
