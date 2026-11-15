using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport;

namespace Chaptarr.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class ImportOrchestratorExistingLinkFixture
    {
        private static string GetExistingLinkConflict(Edition trackedEdition, Book canonicalBook, Edition canonicalEdition)
        {
            var method = typeof(ImportOrchestratorV2).GetMethod("GetExistingLinkConflict", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            return (string)method.Invoke(null, new object[] { trackedEdition, canonicalBook, canonicalEdition });
        }

        [Test]
        public void file_linked_to_another_book_keeps_its_link()
        {
            var tracked = new Edition { Id = 14044, BookId = 2332 };
            var book = new Book { Id = 4249 };
            var edition = new Edition { Id = 17720, BookId = 4249 };

            Assert.That(GetExistingLinkConflict(tracked, book, edition), Is.EqualTo("ALREADY_LINKED_DIFFERENT_BOOK"));
        }

        [Test]
        public void file_linked_to_another_edition_of_the_matched_book_keeps_its_link()
        {
            var tracked = new Edition { Id = 14044, BookId = 2332 };
            var book = new Book { Id = 2332 };
            var edition = new Edition { Id = 15001, BookId = 2332 };

            Assert.That(GetExistingLinkConflict(tracked, book, edition), Is.EqualTo("ALREADY_LINKED_DIFFERENT_EDITION"));
        }

        [Test]
        public void file_linked_to_the_matched_edition_is_resolved_as_before()
        {
            var tracked = new Edition { Id = 14044, BookId = 2332 };
            var book = new Book { Id = 2332 };
            var edition = new Edition { Id = 14044, BookId = 2332 };

            Assert.That(GetExistingLinkConflict(tracked, book, edition), Is.Null);
        }

        [Test]
        public void untracked_edition_is_resolved_as_before()
        {
            Assert.That(GetExistingLinkConflict(null, new Book { Id = 2332 }, new Edition { Id = 14044, BookId = 2332 }), Is.Null);
        }
    }
}
