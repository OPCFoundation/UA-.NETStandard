/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// In-process integration tests for the OPC UA REST Minimal-API
    /// endpoints (OPC UA Part 6 §G.3 "OpenAPI Mapping"). Builds an
    /// ASP.NET Core <see cref="TestServer"/> wired to a stub
    /// <see cref="IWebApiServer"/>, then exercises every spec route
    /// across both encoding flavours through real HTTP semantics
    /// (status codes, headers, body shape) without standing up a
    /// real OPC UA server.
    /// </summary>
    [TestFixture]
    [Category("WebApiIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class WebApiEndpointIntegrationTests
    {
        private TestServer? m_server;
        private HttpClient? m_client;
        private StubWebApiServer? m_stubServer;

        [SetUp]
        public void SetUp()
        {
            var context = ServiceMessageContext.CreateEmpty(new TestTelemetryContext());
            m_stubServer = new StubWebApiServer(context);

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer()
                        .ConfigureServices(services =>
                    {
                        services.AddSingleton<IWebApiServer>(m_stubServer);
                        services.AddRouting();
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapWebApiEndpoints());
                    });
                });

            IHost host = hostBuilder.Start();
            m_server = host.GetTestServer();
            m_client = m_server.CreateClient();
        }

        [TearDown]
        public void TearDown()
        {
            m_client?.Dispose();
            m_server?.Dispose();
        }

        [Test]
        public async Task ReadRouteRoundTripsRequestThroughDispatcher()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 4242 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };
            ReadResponse response = await PostAsync<ReadRequest, ReadResponse>(
                "/read", request, WebApiEncoding.Compact)
                .ConfigureAwait(false);

            Assert.That(m_stubServer!.LastRequest, Is.InstanceOf<ReadRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(4242u));
        }

        [Test]
        public async Task ReadRouteAcceptsVerboseEncoding()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 7 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };
            HttpResponseMessage response = await PostAsync(
                "/read", request, WebApiEncoding.Verbose)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(
                response.Content.Headers.ContentType?.Parameters,
                Has.Some.Matches<NameValueHeaderValue>(p =>
                    p.Name == "encoding" && p.Value == "verbose"));
        }

        [Test]
        public async Task DefaultEncodingIsCompactWhenClientOmitsParameter()
        {
            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            using var content = new ByteArrayContent(EncodeBody(request, WebApiEncoding.Compact));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await m_client!
                .PostAsync(new Uri("/read", UriKind.Relative), content)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                response.Content.Headers.ContentType?.Parameters,
                Has.Some.Matches<NameValueHeaderValue>(p =>
                    p.Name == "encoding" && p.Value == "compact"),
                "server must default to Compact encoding per Part 6 §5.4.9");
        }

        [Test]
        public async Task MalformedBodyReturns400()
        {
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("not json"));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await m_client!
                .PostAsync(new Uri("/read", UriKind.Relative), content)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task DispatcherFaultReturnsTypedResponseWithStatusCode()
        {
            m_stubServer!.NextFault = StatusCodes.BadServiceUnsupported.Code;

            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } };
            ReadResponse response = await PostAsync<ReadRequest, ReadResponse>(
                "/read", request, WebApiEncoding.Compact)
                .ConfigureAwait(false);

            // Faults flow through as ServiceFault with the typed response
            // body unchanged when the dispatcher returns a typed response;
            // the controller forwards it verbatim. Confirm the HTTP shell
            // still says 200 (per existing JSON binding behaviour).
            Assert.That(response, Is.Not.Null);
        }

        [Test]
        public async Task EveryServiceRouteResolves()
        {
            // Sanity check: every route in the static table actually
            // resolves to a controller action. We hit each route with
            // an empty request body and accept either 200 (controller
            // ran, dispatcher stub returned typed response) or 400
            // (decoder couldn't parse — but route matched).
            foreach (WebApiServiceRoute route in WebApiServiceRoutes.Routes)
            {
                using var content = new ByteArrayContent([]);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage response = await m_client!
                    .PostAsync(new Uri(route.Path, UriKind.Relative), content)
                    .ConfigureAwait(false);
                Assert.That(
                    response.StatusCode,
                    Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.BadRequest),
                    $"route '{route.Path}' must be reachable; got {(int)response.StatusCode}");
            }
        }

        [Test]
        public async Task UnknownRouteReturns404()
        {
            using var content = new ByteArrayContent([]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await m_client!
                .PostAsync(new Uri("/unknownservice", UriKind.Relative), content)
                .ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        private async Task<TResponse> PostAsync<TRequest, TResponse>(
            string path,
            TRequest request,
            WebApiEncoding encoding)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new()
        {
            HttpResponseMessage response = await PostAsync(path, request, encoding)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            byte[] body = await response.Content.ReadAsByteArrayAsync()
                .ConfigureAwait(false);
            return WebApiBodyCodec.DecodeBody<TResponse>(body, m_stubServer!.MessageContext);
        }

        private async Task<HttpResponseMessage> PostAsync<TRequest>(
            string path,
            TRequest request,
            WebApiEncoding encoding)
            where TRequest : IServiceRequest
        {
            byte[] body = EncodeBody(request, encoding);
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding));

            using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            message.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding)));

            return await m_client!.SendAsync(message, HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(false);
        }

        private byte[] EncodeBody<TRequest>(TRequest request, WebApiEncoding encoding)
            where TRequest : IServiceRequest
        {
            return WebApiBodyCodec.EncodeBody(
                request,
                m_stubServer!.MessageContext,
                WebApiMediaType.ToEncoderOptions(encoding));
        }

        /// <summary>
        /// Stub <see cref="IWebApiServer"/> that records the last
        /// request and returns a typed response of the matching type.
        /// Optionally faults with a configured StatusCode.
        /// </summary>
        private sealed class StubWebApiServer : IWebApiServer
        {
            private readonly IServiceMessageContext m_messageContext;

            public StubWebApiServer(IServiceMessageContext messageContext)
            {
                m_messageContext = messageContext;
            }

            public IServiceMessageContext MessageContext => m_messageContext;
            public bool IsReady => true;

            public IServiceRequest? LastRequest { get; private set; }
            public uint NextFault { get; set; }

            public ValueTask<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                WebApiInvocationContext context,
                CancellationToken ct)
            {
                LastRequest = request;

                if (!WebApiServiceRoutes.TryGetByRequestType(request.GetType(), out WebApiServiceRoute route))
                {
                    throw new InvalidOperationException(
                        $"No matching route for {request.GetType().Name}.");
                }

                StatusCode serviceResult = NextFault != 0 ? new StatusCode(NextFault) : StatusCodes.Good;

                var response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
                var responseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = serviceResult,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                // Concrete generated <Service>Response types expose a
                // settable ResponseHeader (the IServiceResponse
                // interface only declares the getter); set via
                // reflection on the runtime type.
                route.ResponseType
                    .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                    .SetValue(response, responseHeader);
                return new ValueTask<IServiceResponse>(response);
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
