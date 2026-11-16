using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NzbDrone.Core.Indexers.DirectDownload
{
    public sealed class DirectDownloadSearchUrlTemplate
    {
        private const string QueryToken = "{query}";
        private static readonly HashSet<string> SensitiveQueryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api_key", "apikey", "authorization", "auth", "key", "password", "secret", "token"
        };

        private DirectDownloadSearchUrlTemplate(DirectDownloadSourceFamily family, string template, Uri uri)
        {
            Family = family;
            Template = template;
            TemplateUri = uri;
        }

        public DirectDownloadSourceFamily Family { get; }

        public string Template { get; }

        private Uri TemplateUri { get; }

        public static IReadOnlyList<DirectDownloadSearchUrlTemplate> Parse(string value)
        {
            var templates = new List<DirectDownloadSearchUrlTemplate>();
            foreach (var line in (value ?? string.Empty)
                         .Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Replace('\r', '\n')
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParse(line, out var template))
                {
                    throw new DirectDownloadProbeException("A custom search URL template is invalid.");
                }

                templates.Add(template);
            }

            return templates;
        }

        public static bool TryParse(string line, out DirectDownloadSearchUrlTemplate template)
        {
            template = null;
            var separator = line?.IndexOf('|') ?? -1;
            if (separator <= 0 || separator == line.Length - 1 || line.IndexOf('|', separator + 1) >= 0)
            {
                return false;
            }

            var familyText = line.Substring(0, separator).Trim();
            var family = familyText.Equals("catalog", StringComparison.OrdinalIgnoreCase)
                ? DirectDownloadSourceFamily.CatalogPage
                : familyText.Equals("mirror", StringComparison.OrdinalIgnoreCase)
                    ? DirectDownloadSourceFamily.MirrorIndex
                    : (DirectDownloadSourceFamily?)null;
            if (family == null)
            {
                return false;
            }

            var urlTemplate = line.Substring(separator + 1).Trim();
            if (urlTemplate.IndexOf(QueryToken, StringComparison.Ordinal) < 0 ||
                urlTemplate.IndexOf(QueryToken, StringComparison.Ordinal) != urlTemplate.LastIndexOf(QueryToken, StringComparison.Ordinal))
            {
                return false;
            }

            var sampleUrl = urlTemplate.Replace(QueryToken, "search-term", StringComparison.Ordinal);
            if (sampleUrl.Contains('{') || sampleUrl.Contains('}') ||
                !Uri.TryCreate(sampleUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            var queryNames = HttpUtility.ParseQueryString(uri.Query).AllKeys;
            if (queryNames.Any(name => name != null && SensitiveQueryNames.Contains(name)))
            {
                return false;
            }

            template = new DirectDownloadSearchUrlTemplate(family.Value, urlTemplate, uri);
            return true;
        }

        public bool MatchesSource(Uri sourceUri)
        {
            return SameOrigin(TemplateUri, sourceUri);
        }

        public Uri BuildUri(Uri sourceUri, string searchTerm, TimeSpan? timeout)
        {
            var resolvedUri = new Uri(Template.Replace(QueryToken, HttpUtility.UrlEncode(searchTerm), StringComparison.Ordinal), UriKind.Absolute);
            if (!MatchesSource(sourceUri))
            {
                throw new DirectDownloadProbeException("The custom search URL must use the same origin as its configured source URL.");
            }

            DirectDownloadUrlSafety.ValidateAbsoluteHttpOrHttpsUri(resolvedUri, resolvedUri.AbsoluteUri, timeout);
            if (!SameOrigin(resolvedUri, sourceUri))
            {
                throw new DirectDownloadProbeException("The custom search URL must use the same origin as its configured source URL.");
            }

            return resolvedUri;
        }

        private static bool SameOrigin(Uri first, Uri second)
        {
            return first != null && second != null &&
                   first.Scheme.Equals(second.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   first.IdnHost.Equals(second.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                   first.Port == second.Port;
        }
    }
}
