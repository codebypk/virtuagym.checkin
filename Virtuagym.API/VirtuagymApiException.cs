using System;

namespace Virtuagym.API
{
    /// <summary>
    /// Exception for Virtuagym API errors.
    /// Contains the Virtuagym-specific status code, status message and HTTP response details.
    /// 
    /// Virtuagym Status-Codes:
    ///   200 - OK
    ///   401 - Authentication Failed
    ///   403 - Access Disabled
    ///   404 - Resource not found
    ///   406 - Email address already in use
    ///   420 - Invalid Input
    ///   421 - Too many access attempts
    ///   427 - Missing Oauth Token
    ///   428 - Invalid access token
    ///   430 - Event full
    ///   431 - Can not cancel event booking
    ///   432 - Not enough credits
    ///   450 - Too many failed login attempts
    ///   500 - Internal server error
    ///   503 - Server unavailable
    /// </summary>
    public class VirtuagymApiException : Exception
    {
        /// <summary>
        /// Virtuagym API Statuscode (z.B. 401, 420, 430).
        /// </summary>
        public int ApiStatusCode { get; }

        /// <summary>
        /// Virtuagym API Statusmeldung (z.B. "Authentication Failed", "Invalid Input").
        /// </summary>
        public string ApiStatusMessage { get; }

        /// <summary>
        /// HTTP Statuscode der Antwort (z.B. 200, 401, 500).
        /// </summary>
        public int HttpStatusCode { get; }

        /// <summary>
        /// Rohe JSON-Antwort des Servers.
        /// </summary>
        public string RawResponse { get; }

        public VirtuagymApiException(int httpStatusCode, int apiStatusCode, string apiStatusMessage, string rawResponse)
            : base(FormatMessage(httpStatusCode, apiStatusCode, apiStatusMessage))
        {
            HttpStatusCode = httpStatusCode;
            ApiStatusCode = apiStatusCode;
            ApiStatusMessage = apiStatusMessage;
            RawResponse = rawResponse;
        }

        public VirtuagymApiException(int httpStatusCode, string rawResponse)
            : base($"API error: HTTP {httpStatusCode}")
        {
            HttpStatusCode = httpStatusCode;
            RawResponse = rawResponse;
        }

        private static string FormatMessage(int httpStatusCode, int apiStatusCode, string apiStatusMessage)
        {
            if (!string.IsNullOrWhiteSpace(apiStatusMessage))
                return $"API error {apiStatusCode}: {apiStatusMessage} (HTTP {httpStatusCode})";

            return $"API error: HTTP {httpStatusCode} (Statuscode {apiStatusCode})";
        }
    }
}
