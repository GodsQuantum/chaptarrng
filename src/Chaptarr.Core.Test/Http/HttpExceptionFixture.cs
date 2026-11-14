using System.Net;
using NUnit.Framework;
using NzbDrone.Common.Http;

namespace Chaptarr.Core.Test.Http
{
    [TestFixture]
    public class HttpExceptionFixture
    {
        [Test]
        public void should_sanitize_request_url_in_default_message()
        {
            var request = new HttpRequest("https://example.com/api?apikey=real-secret&query=value");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.Unauthorized);

            var exception = new HttpException(request, response);

            Assert.That(exception.Message, Does.Not.Contain("real-secret"));
            Assert.That(exception.Message, Does.Contain("apikey=(removed)"));
        }

        [Test]
        public void should_not_expose_query_or_response_content_when_request_logging_is_disabled()
        {
            const string sensitiveValue = "Native%20Book%0D%0Apassword%3Dtopsecret";
            var request = new HttpRequest($"https://example.com/api/search?q={sensitiveValue}")
            {
                LogRequest = false
            };
            const string responseBody = "upstream reflected Native Book password=topsecret";
            var response = new HttpResponse(request, new HttpHeader(), responseBody, HttpStatusCode.BadRequest);

            var exception = new HttpException(request, response, $"provider echoed {sensitiveValue}");

            Assert.That(exception.Message, Does.Not.Contain(sensitiveValue));
            Assert.That(exception.Message, Does.Not.Contain("topsecret"));
            Assert.That(exception.Message, Does.Not.Contain("example.com"));
            Assert.That(exception.Message, Does.Not.Contain("/api/search"));
            Assert.That(exception.ToString(), Does.Not.Contain(sensitiveValue));
            Assert.That(exception.ToString(), Does.Not.Contain("topsecret"));
            Assert.That(exception.ToString(), Does.Not.Contain(responseBody));
        }
    }
}
