using System;

namespace Jablotron.API
{
    public class JablotronApiException : Exception
    {
        public int HttpStatusCode { get; private set; }
        public string RawResponse { get; private set; }

        public JablotronApiException(string message, int httpStatusCode, string rawResponse)
            : base(string.Format("{0} (HTTP {1})", message, httpStatusCode))
        {
            HttpStatusCode = httpStatusCode;
            RawResponse = rawResponse;
        }

        public JablotronApiException(string message)
            : base(message)
        {
        }
    }

    public class BadRequestException : JablotronApiException
    {
        public BadRequestException(string message, string rawResponse) : base(message, 400, rawResponse) { }
    }

    public class UnauthorizedException : JablotronApiException
    {
        public UnauthorizedException(string message, string rawResponse) : base(message, 401, rawResponse) { }
    }

    public class SessionExpiredException : JablotronApiException
    {
        public SessionExpiredException(string message, string rawResponse) : base(message, 408, rawResponse) { }
    }

    public class InvalidSessionIdException : JablotronApiException
    {
        public InvalidSessionIdException(string message) : base(message) { }
    }

    public class NoPinCodeException : JablotronApiException
    {
        public NoPinCodeException(string message) : base(message) { }
    }

    public class IncorrectPinCodeException : JablotronApiException
    {
        public IncorrectPinCodeException(string message) : base(message) { }
    }

    public class ControlActionException : JablotronApiException
    {
        public object Details { get; private set; }

        public ControlActionException(string message, object details = null)
            : base(message)
        {
            Details = details;
        }
    }
}
