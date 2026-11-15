using System;
using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public class BookInfoException : NzbDroneClientException
    {
        public BookInfoException(string message)
            : base(HttpStatusCode.ServiceUnavailable, message)
        {
        }

        public BookInfoException(string message, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, args)
        {
        }

        public BookInfoException(string message, Exception innerException, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, innerException, args)
        {
        }
    }

    /// <summary>
    /// The metadata server circuit is open, so the request was refused without calling it.
    /// </summary>
    public class MetadataServerUnavailableException : BookInfoException
    {
        public MetadataServerUnavailableException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
