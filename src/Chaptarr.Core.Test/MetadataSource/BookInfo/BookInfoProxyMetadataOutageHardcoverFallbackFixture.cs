using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using System.IO;
using System.Threading.Tasks;
using Chaptarr.Http.ErrorManagement;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using DefaultHttpContext = Microsoft.AspNetCore.Http.DefaultHttpContext;
using Endpoint = Microsoft.AspNetCore.Http.Endpoint;
using Moq;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.Hardcover;
using GoodreadsException = NzbDrone.Core.MetadataSource.Goodreads.GoodreadsException;
using IGoodreadsSearchProxy = NzbDrone.Core.MetadataSource.Goodreads.IGoodreadsSearchProxy;
using SearchJsonResource = NzbDrone.Core.MetadataSource.Goodreads.SearchJsonResource;

namespace Chaptarr.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class BookInfoProxyMetadataOutageHardcoverFallbackFixture
    {
        private class RecordingHttpClient : IHttpClient
        {
            private readonly Func<HttpRequest, HttpResponse> _handler;

            public RecordingHttpClient(Func<HttpRequest, HttpResponse> handler)
            {
                _handler = handler;
            }

            public List<HttpRequest> Requests { get; } = new();

            public HttpResponse Get(HttpRequest request)
            {
                Requests.Add(request);
                return _handler(request);
            }

            public HttpResponse Execute(HttpRequest request) => throw new NotImplementedException();
            public void DownloadFile(string url, string fileName, string userAgent = null) => throw new NotImplementedException();
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => throw new NotImplementedException();
            public HttpResponse Post(HttpRequest request) => throw new NotImplementedException();
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse> ExecuteAsync(HttpRequest request) => throw new NotImplementedException();
            public System.Threading.Tasks.Task DownloadFileAsync(string url, string fileName, string userAgent = null) => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse> GetAsync(HttpRequest request) => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse> HeadAsync(HttpRequest request) => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse> PostAsync(HttpRequest request) => throw new NotImplementedException();
            public System.Threading.Tasks.Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        }

        private class ConfigServiceProxy : DispatchProxy
        {
            public string MetadataServerUrl { get; set; } = "http://metadata";

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod?.Name == "get_MetadataServerUrl")
                {
                    return MetadataServerUrl;
                }

                if (targetMethod?.Name == "IsDefined")
                {
                    return false;
                }

                if (targetMethod?.Name.StartsWith("get_", StringComparison.Ordinal) == true)
                {
                    return targetMethod.ReturnType.IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null;
                }

                throw new NotImplementedException($"Test proxy does not implement {targetMethod?.Name}");
            }
        }

        private sealed class StubHardcoverMetadataProxy : IHardcoverMetadataProxy
        {
            public bool IsConfigured { get; set; } = true;
            public bool IsNativeEnabled => false;
            public List<string> Searches { get; } = new();

            public List<SearchJsonResource> Search(string query, bool interactiveSearch = false)
            {
                Searches.Add(query);
                return new List<SearchJsonResource>
                {
                    new SearchJsonResource { WorkId = 101, BookId = 201, Title = "Native Book" }
                };
            }

            public Newtonsoft.Json.Linq.JObject ExecuteSearchGraphQl(
                string operationName,
                string query,
                Newtonsoft.Json.Linq.JObject variables,
                bool interactiveSearch = false)
            {
                throw new NotSupportedException();
            }

            public AuthorResource GetAuthor(string foreignAuthorId, bool interactiveSearch = false) => throw new NotSupportedException();

            public List<string> WorkLookups { get; } = new();

            public WorkResource GetWork(string foreignWorkId, bool interactiveSearch = false) => Record(foreignWorkId, new()
            {
                ForeignId = 101,
                Title = "Native Book",
                Authors = new List<AuthorResource> { new() { ForeignId = 42, Name = "Native Author" } },
                Books = new List<BookResource>
                {
                    new()
                    {
                        ForeignId = 201,
                        Title = "Native Book Edition",
                        Isbn13 = "9780000000001",
                        Format = "Audiobook",
                        Contributors = new List<ContributorResource> { new() { ForeignId = 42 } }
                    },
                    new()
                    {
                        ForeignId = 202,
                        Title = "Native Book Alternate Edition",
                        Isbn13 = "9780000000002",
                        Format = "Audiobook",
                        Contributors = new List<ContributorResource> { new() { ForeignId = 42 } }
                    }
                },
                Series = new List<SeriesResource>()
            });

            private WorkResource Record(string foreignWorkId, WorkResource resource)
            {
                WorkLookups.Add(foreignWorkId);
                return resource;
            }

            public WorkResource GetEdition(string foreignEditionId, bool interactiveSearch = false) => throw new NotSupportedException();
        }

        // Library services for a book that is not in the library yet: lookups find nothing.
        private class EmptyLibraryProxy : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                var type = targetMethod?.ReturnType;
                if (type == null || type == typeof(void))
                {
                    return null;
                }

                if (type.IsGenericType &&
                    (type.GetGenericTypeDefinition() == typeof(List<>) ||
                     type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                     type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                     type.GetGenericTypeDefinition() == typeof(IList<>)))
                {
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    return Activator.CreateInstance(type);
                }

                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }

        private static (BookInfoProxy Proxy, RecordingHttpClient Http, StubHardcoverMetadataProxy Hardcover) CreateProxy(bool circuitOpen, bool hardcoverConfigured)
        {
            var httpClient = new RecordingHttpClient(_ => throw new AssertionException("metadata server must not be called while its circuit is open"));
            var configService = DispatchProxy.Create<IConfigService, ConfigServiceProxy>();
            var logger = LogManager.GetCurrentClassLogger();
            var healthService = new MetadataServerHealthService(logger);
            var healthGate = new MetadataServerHealthGate(configService, healthService, logger);
            if (circuitOpen)
            {
                healthService.ReportFailure(healthGate.SourceName, new WebException("Http request timed out"));
            }

            var hardcover = new StubHardcoverMetadataProxy { IsConfigured = hardcoverConfigured };
            var proxy = new BookInfoProxy(httpClient,
                cachedHttpClient: null,
                goodreadsSearchProxy: null,
                hardcoverSearchClient: null,
                audibleCatalogProxy: null,
                authorService: DispatchProxy.Create<IAuthorService, EmptyLibraryProxy>(),
                bookService: DispatchProxy.Create<IBookService, EmptyLibraryProxy>(),
                editionService: DispatchProxy.Create<IEditionService, EmptyLibraryProxy>(),
                configService: configService,
                requestBuilder: new MetadataRequestBuilder(configService),
                logger: logger,
                cacheManager: new CacheManager(),
                metadataServerHealthGate: healthGate,
                hardcoverMetadataProxy: hardcover);

            return (proxy, httpClient, hardcover);
        }

        [Test]
        public void book_info_should_come_from_hardcover_while_the_metadata_server_circuit_is_open()
        {
            var (proxy, http, hardcover) = CreateProxy(circuitOpen: true, hardcoverConfigured: true);

            var info = proxy.GetBookInfo("hc:101", BookMediaType.Audiobook, useCache: false);

            Assert.Multiple(() =>
            {
                Assert.That(info.Item2.Title, Is.EqualTo("Native Book"));
                Assert.That(info.Item2.MediaType, Is.EqualTo(BookMediaType.Audiobook));
                Assert.That(hardcover.WorkLookups, Is.EqualTo(new[] { "101" }));
                Assert.That(http.Requests, Is.Empty);
            });
        }

        [Test]
        public void work_id_lookup_should_return_both_formats_from_hardcover_while_the_circuit_is_open()
        {
            var (proxy, http, hardcover) = CreateProxy(circuitOpen: true, hardcoverConfigured: true);

            var books = proxy.SearchForNewBook("hc:101", null);

            Assert.Multiple(() =>
            {
                Assert.That(books.Select(book => book.MediaType), Is.EquivalentTo(new[] { BookMediaType.Audiobook, BookMediaType.Ebook }));
                Assert.That(books.All(book => book.Title == "Native Book" && book.AnyEditionOk), Is.True);
                Assert.That(hardcover.WorkLookups, Is.Not.Empty.And.All.EqualTo("101"));
                Assert.That(http.Requests, Is.Empty);
            });
        }

        [Test]
        public void without_a_hardcover_token_the_outage_is_still_reported()
        {
            var (proxy, http, hardcover) = CreateProxy(circuitOpen: true, hardcoverConfigured: false);

            Assert.Throws<BookNotFoundException>(() => proxy.GetBookInfo("hc:101", BookMediaType.Audiobook, useCache: false));
            Assert.That(hardcover.WorkLookups, Is.Empty);
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void non_hardcover_ids_are_not_answered_from_hardcover()
        {
            var (proxy, _, hardcover) = CreateProxy(circuitOpen: true, hardcoverConfigured: true);

            Assert.Throws<BookNotFoundException>(() => proxy.GetBookInfo("gr:12345", BookMediaType.Audiobook, useCache: false));
            Assert.That(hardcover.WorkLookups, Is.Empty);
        }
    }
}
