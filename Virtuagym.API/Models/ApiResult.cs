using System.Collections.Generic;

namespace Virtuagym.API.Models
{
    public class ApiResult<T>
    {
        public ApiStatus status { get; set; }
        public T result { get; set; }
    }

    public class ApiResultV1<T>
    {
        public ApiStatus status { get; set; }
        public List<T> result { get; set; }
    }

    /// <summary>
    /// v0 API Antwort mit Ergebnis-Liste (result ist ein Array, statuscode auf oberster Ebene).
    /// </summary>
    public class ApiResultV0List<T>
    {
        public int statuscode { get; set; }
        public string statusmessage { get; set; }
        public long timestamp { get; set; }
        public int result_count { get; set; }
        public List<T> result { get; set; }
    }

    /// <summary>
    /// v0 API Antwort mit Einzelobjekt als result (statuscode auf oberster Ebene).
    /// </summary>
    public class ApiResultV0Single<T>
    {
        public int statuscode { get; set; }
        public string statusmessage { get; set; }
        public long timestamp { get; set; }
        public int result_count { get; set; }
        public T result { get; set; }
    }

    public class ApiStatus
    {
        public int statuscode { get; set; }
        public string statusmessage { get; set; }
        public int result_count { get; set; }
        public long timestamp { get; set; }
        public int results_remaining { get; set; }
        public string next_page { get; set; }
    }

    public class ApiTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ClubSubdomain { get; set; }
        public string ClubName { get; set; }
    }

    public class ApiError
    {
        public string type { get; set; }
        public List<ApiErrorInfo> information { get; set; }
    }

    public class ApiErrorInfo
    {
        public string type { get; set; }
        public string value { get; set; }
    }

    public class ApiErrorResult
    {
        public ApiStatus status { get; set; }
        public List<ApiError> errors { get; set; }
    }
}
