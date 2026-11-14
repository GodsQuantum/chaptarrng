using System;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Http
{
    public class HttpException : Exception
    {
        public HttpRequest Request { get; private set; }
        public HttpResponse Response { get; private set; }

        public HttpException(HttpRequest request, HttpResponse response, string message)
            : base(GetMessage(request, response, message))
        {
            Request = request;
            Response = response;
        }

        public HttpException(HttpRequest request, HttpResponse response)
            : this(request, response, null)
        {
        }

        public HttpException(HttpResponse response)
            : this(response.Request, response)
        {
        }

        public override string ToString()
        {
            if (Request?.LogRequest != false && Response != null && Response.ResponseData != null)
            {
                return base.ToString() + Environment.NewLine + Response.Content;
            }

            return base.ToString();
        }

        private static string GetMessage(HttpRequest request, HttpResponse response, string message)
        {
            if (request?.LogRequest == false)
            {
                // A request can opt out of URL logging because its query contains user data.
                // Keep the exception safe even if a caller later logs the exception object.
                return string.Format(
                    "HTTP request failed: [{0}:{1}] [{2}]",
                    response == null ? 0 : (int)response.StatusCode,
                    response?.StatusCode,
                    request.Method);
            }

            if (message != null)
            {
                return message;
            }

            return string.Format(
                "HTTP request failed: [{0}:{1}] [{2}] at [{3}]",
                response == null ? 0 : (int)response.StatusCode,
                response?.StatusCode,
                request?.Method,
                request?.Url == null ? "unknown target" : CleanseLogMessage.Cleanse(request.Url.ToString()));
        }
    }
}
