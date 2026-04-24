using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jablotron.API;
using Jablotron.API.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Api
{
    [TestClass]
    public class JablotronCloudServiceTests
    {
        [TestMethod]
        public void Ctor_EmptyApiBaseUrl_ThrowsArgumentException()
        {
            try
            {
                new JablotronCloudService("", "user@example.com", "secret");
                Assert.Fail("Expected ArgumentException was not thrown.");
            }
            catch (ArgumentException)
            {
            }
        }

        [TestMethod]
        public void GetServices_OnSessionExpired_PerformsLoginAndRetries()
        {
            var handler = new SequenceHttpMessageHandler(new List<HttpResponseMessage>
            {
                CreateJsonResponse(HttpStatusCode.Unauthorized,
                    "{\"errors\":[{\"code\":\"USER.SESSION.EXPIRED\",\"message\":\"Login expired. Please login again.\"}],\"http-code\":401}"),
                CreateJsonResponse(HttpStatusCode.OK, "{\"data\":{},\"http-code\":200}"),
                CreateJsonResponse(HttpStatusCode.OK, "{\"data\":{\"services\":[{\"service-id\":1339277,\"name\":\"Fitnessstudio\"}]},\"http-code\":200}")
            });

            var httpClient = new HttpClient(handler);
            var service = new JablotronCloudService("https://example.test", "user@example.com", "secret", httpClient: httpClient);

            var services = service.GetServices();

            Assert.AreEqual(1, services.Count);
            Assert.AreEqual(3, handler.RequestUris.Count);
            Assert.IsTrue(handler.RequestUris[0].AbsoluteUri.EndsWith("/serviceListGet.json"));
            Assert.IsTrue(handler.RequestUris[1].AbsoluteUri.EndsWith("/userAuthorize.json"));
            Assert.IsTrue(handler.RequestUris[2].AbsoluteUri.EndsWith("/serviceListGet.json"));
        }

        private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private sealed class SequenceHttpMessageHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public List<Uri> RequestUris { get; }

            public SequenceHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
                RequestUris = new List<Uri>();
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUris.Add(request.RequestUri);

                if (_responses.Count == 0)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
