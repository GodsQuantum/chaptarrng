using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Indexers.DirectDownload
{
    public sealed class DirectDownloadResponseReader
    {
        private const int MaxRedirects = 5;
        private readonly IHttpClient _httpClient;

        public DirectDownloadResponseReader(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<DirectDownloadFetchedResponse> GetAsync(Uri uri, DirectDownloadProbeRequest request)
        {
            var currentUri = uri;
            for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
            {
                DirectDownloadUrlSafety.ValidateAbsoluteHttpOrHttpsUri(currentUri, currentUri.AbsoluteUri, request.RequestTimeout);

                using var buffer = new CappedMemoryStream(request.MaxResponseBytes);
                var httpRequest = new HttpRequest(currentUri.AbsoluteUri)
                {
                    AllowPrivateNetworkTargets = false,
                    AllowAutoRedirect = false,
                    RequestTimeout = request.RequestTimeout,
                    ResponseStream = buffer
                };
                httpRequest.Headers.Accept = "text/html, application/json;q=0.9, */*;q=0.1";
                httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable };

                try
                {
                    var response = await _httpClient.ExecuteAsync(httpRequest);
                    if ((response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                        !string.IsNullOrWhiteSpace(request.FlareSolverrUrl))
                    {
                        return await GetViaFlareSolverrAsync(currentUri, request);
                    }

                    if (response.HasHttpRedirect)
                    {
                        var location = response.Headers.GetSingleValue("Location");
                        if (string.IsNullOrWhiteSpace(location))
                        {
                            throw new DirectDownloadProbeException($"Redirect response from '{CleanseLogMessage.Cleanse(currentUri.AbsoluteUri)}' is missing a Location header.");
                        }

                        currentUri = new Uri(currentUri, location);
                        DirectDownloadUrlSafety.ValidateAbsoluteHttpOrHttpsUri(currentUri, currentUri.AbsoluteUri, request.RequestTimeout);
                        continue;
                    }

                    if ((int)response.StatusCode >= (int)HttpStatusCode.BadRequest)
                    {
                        throw new DirectDownloadProbeException(
                            $"Source '{CleanseLogMessage.Cleanse(currentUri.AbsoluteUri)}' returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
                    }

                    var responseBytes = response.ResponseData ?? buffer.ToArray();
                    var responseText = response.ResponseData == null
                        ? HttpHeader.GetEncodingFromContentType(response.Headers.ContentType).GetString(responseBytes)
                        : response.Content;

                    if (LooksLikeBrowserChallenge(responseText) && !string.IsNullOrWhiteSpace(request.FlareSolverrUrl))
                    {
                        return await GetViaFlareSolverrAsync(currentUri, request);
                    }

                    return new DirectDownloadFetchedResponse(response.StatusCode, response.Headers.ContentType, responseText);
                }
                catch (WebException ex) when (ex.InnerException is IOException ioException && ioException.Message.Contains("maximum response size", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DirectDownloadProbeException($"Response from '{CleanseLogMessage.Cleanse(currentUri.AbsoluteUri)}' exceeded the maximum response size.", ex);
                }
                catch (Exception ex) when (!(ex is DirectDownloadProbeException))
                {
                    throw new DirectDownloadProbeException(CleanseLogMessage.Cleanse(ex.Message), ex);
                }
            }

            throw new DirectDownloadProbeException($"Too many redirects were attempted for '{CleanseLogMessage.Cleanse(uri.AbsoluteUri)}'.");
        }

        private async Task<DirectDownloadFetchedResponse> GetViaFlareSolverrAsync(Uri targetUri, DirectDownloadProbeRequest request)
        {
            if (!Uri.TryCreate(request.FlareSolverrUrl, UriKind.Absolute, out var solverUri) ||
                (solverUri.Scheme != Uri.UriSchemeHttp && solverUri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(solverUri.UserInfo) || !string.IsNullOrEmpty(solverUri.Fragment) ||
                !string.IsNullOrEmpty(solverUri.Query))
            {
                throw new DirectDownloadProbeException("The configured FlareSolverr URL is invalid.");
            }

            DirectDownloadUrlSafety.ValidateAbsoluteHttpOrHttpsUri(targetUri, targetUri.AbsoluteUri, request.RequestTimeout);
            var maxTimeout = (int)Math.Max(1000, Math.Min(request.RequestTimeout.TotalMilliseconds, 60000));
            var body = JsonSerializer.Serialize(new { cmd = "request.get", url = targetUri.AbsoluteUri, maxTimeout });
            using var buffer = new CappedMemoryStream(request.MaxResponseBytes);
            var httpRequest = new HttpRequest(solverUri.AbsoluteUri)
            {
                Method = HttpMethod.Post,
                AllowPrivateNetworkTargets = true,
                AllowAutoRedirect = false,
                RequestTimeout = TimeSpan.FromMilliseconds(maxTimeout),
                ResponseStream = buffer
            };
            httpRequest.Headers.ContentType = "application/json";
            httpRequest.Headers.Accept = "application/json";
            httpRequest.SetContent(body);
            var response = await _httpClient.ExecuteAsync(httpRequest);
            if (response.HasHttpError)
            {
                throw new DirectDownloadProbeException($"FlareSolverr returned HTTP {(int)response.StatusCode} {response.StatusCode}.");
            }

            try
            {
                using var document = JsonDocument.Parse(response.Content);
                var root = document.RootElement;
                if (!root.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase) ||
                    !root.TryGetProperty("solution", out var solution) ||
                    !solution.TryGetProperty("response", out var content) || content.ValueKind != JsonValueKind.String)
                {
                    throw new DirectDownloadProbeException("FlareSolverr did not return a successful page response.");
                }

                var statusCode = solution.TryGetProperty("status", out var code) && code.TryGetInt32(out var value)
                    ? (HttpStatusCode)value
                    : HttpStatusCode.OK;
                if ((int)statusCode >= 400)
                {
                    throw new DirectDownloadProbeException($"FlareSolverr target returned HTTP {(int)statusCode} {statusCode}.");
                }

                return new DirectDownloadFetchedResponse(statusCode, "text/html; charset=utf-8", content.GetString());
            }
            catch (JsonException ex)
            {
                throw new DirectDownloadProbeException("FlareSolverr returned an invalid response.", ex);
            }
        }

        private static bool LooksLikeBrowserChallenge(string content)
        {
            return content?.IndexOf("DDoS-Guard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   content?.IndexOf("Checking your browser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   content?.IndexOf("Just a moment...", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public sealed class DirectDownloadFetchedResponse
        {
            public DirectDownloadFetchedResponse(HttpStatusCode statusCode, string contentType, string content)
            {
                StatusCode = statusCode;
                ContentType = contentType ?? string.Empty;
                Content = content ?? string.Empty;
            }

            public HttpStatusCode StatusCode { get; }

            public string ContentType { get; }

            public string Content { get; }
        }

        private sealed class CappedMemoryStream : MemoryStream
        {
            private readonly int _maxBytes;

            public CappedMemoryStream(int maxBytes)
            {
                _maxBytes = maxBytes > 0 ? maxBytes : 256 * 1024;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                EnsureCapacityWithinLimit(count);
                base.Write(buffer, offset, count);
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
            {
                EnsureCapacityWithinLimit(buffer.Length);
                return base.WriteAsync(buffer, cancellationToken);
            }

            private void EnsureCapacityWithinLimit(int incomingBytes)
            {
                if (Length + incomingBytes > _maxBytes)
                {
                    throw new IOException("maximum response size exceeded");
                }
            }
        }
    }
}
