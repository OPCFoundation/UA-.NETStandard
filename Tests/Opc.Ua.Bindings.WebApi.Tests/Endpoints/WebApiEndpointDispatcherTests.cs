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

#nullable enable

using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Endpoints;

namespace Opc.Ua.Bindings.WebApi.Tests.Endpoints
{
    /// <summary>
    /// Unit tests for the AOT-friendly Minimal-API dispatcher
    /// (<see cref="WebApiEndpointDispatcher"/>). Verifies the decode →
    /// dispatch → encode loop, fault mapping (400 on bad body, 500 on
    /// unexpected response type), Accept-header negotiation, and the
    /// <see cref="ISessionlessIdentityProvider"/> hook. Mirrors the
    /// coverage that previously lived on
    /// <c>WebApiControllerBase.ExecuteAsync</c>.
    /// </summary>
    [TestFixture]
    [Category("WebApiEndpoints")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiEndpointDispatcherTests
    {
        [Test]
        public async Task HandleAsyncRoundTripsReadRequestAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext("/read", server);
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(server.LastRequest, Is.InstanceOf<ReadRequest>());
            Assert.That(context.Response.ContentType,
                Does.StartWith("application/json"));
            Assert.That(ReadResponseBody(context.Response), Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsyncMapsDecodeFailureToBadRequestAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext("/read", server);
            // Truncated JSON object → JsonDecoder fails.
            WriteJsonBody(context.Request, "{ \"NodesToRead\": [");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.StatusCode,
                Is.EqualTo((int)HttpStatusCode.BadRequest),
                "Malformed request bodies must surface as HTTP 400 — " +
                "the dispatcher cannot build a typed response without a " +
                "usable request, so it short-circuits.");
            Assert.That(server.LastRequest, Is.Null,
                "Decode failures must not reach the dispatcher.");
        }

        [Test]
        public async Task HandleAsyncReturnsServiceFaultPayloadAsync()
        {
            var server = new StubServer
            {
                ResponseFactory = req => new ServiceFault
                {
                    ResponseHeader = new ResponseHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        RequestHandle = req.RequestHeader?.RequestHandle ?? 0,
                        ServiceResult = StatusCodes.BadInternalError,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    }
                }
            };
            DefaultHttpContext context = BuildContext("/read", server);
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK),
                "ServiceFault is a legitimate substitute for a typed " +
                "response; HTTP must stay 200 (status carried in the OPC " +
                "UA ResponseHeader.ServiceResult per Part 6 §G.3).");
            Assert.That(ReadResponseBody(context.Response), Is.Not.Empty);
        }

        [Test]
        public async Task HandleAsyncRejectsUnexpectedResponseTypeWith500Async()
        {
            // Dispatcher returns BrowseResponse for a ReadRequest — the
            // type-checked TResponse path falls through to the 500 branch
            // (this is a server-side wiring bug, surfaced loud).
            var server = new StubServer
            {
                ResponseFactory = req => new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        ServiceResult = StatusCodes.Good,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    }
                }
            };
            DefaultHttpContext context = BuildContext("/read", server);
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.StatusCode,
                Is.EqualTo((int)HttpStatusCode.InternalServerError),
                "Type mismatches between dispatcher output and route " +
                "expectation must surface as HTTP 500.");
        }

        [Test]
        public async Task HandleAsyncHonorsCompactAcceptHeaderAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext("/read", server);
            WriteJsonBody(context.Request, "{}");
            context.Request.Headers["Accept"] =
                WebApiMediaType.FormatContentType(WebApiEncoding.Compact);

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.ContentType,
                Is.EqualTo(WebApiMediaType.FormatContentType(WebApiEncoding.Compact)),
                "Accept-header negotiation must echo the requested " +
                "encoding on the response Content-Type.");
        }

        [Test]
        public async Task HandleAsyncHonorsVerboseAcceptHeaderAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext("/read", server);
            WriteJsonBody(context.Request, "{}");
            context.Request.Headers["Accept"] =
                WebApiMediaType.FormatContentType(WebApiEncoding.Verbose);

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.ContentType,
                Is.EqualTo(WebApiMediaType.FormatContentType(WebApiEncoding.Verbose)));
        }

        [Test]
        public async Task HandleAsyncResolvesIdentityFromProviderAsync()
        {
            var server = new StubServer();
            var stubIdentity = new UserIdentity();
            var provider = new StubIdentityProvider(stubIdentity);
            DefaultHttpContext context = BuildContext("/read", server, provider);
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(provider.ResolveCalls, Is.EqualTo(1));
            Assert.That(server.LastInvocation!.Identity, Is.SameAs(stubIdentity),
                "The dispatcher must forward the provider's resolved " +
                "identity onto WebApiInvocationContext for downstream " +
                "role-based-access checks.");
        }

        [Test]
        public async Task HandleAsyncSurfacesAnonymousWhenNoProviderRegisteredAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext(
                "/read",
                server,
                identityProvider: null);
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(context.Response.StatusCode, Is.EqualTo((int)HttpStatusCode.OK));
            Assert.That(server.LastInvocation!.Identity, Is.Null,
                "Without a registered provider the dispatcher must leave " +
                "Identity null so downstream code applies the binding's " +
                "anonymous-default policy.");
        }

        [Test]
        public async Task HandleAsyncPropagatesClientCertificateRawDataAsync()
        {
            var server = new StubServer();
            DefaultHttpContext context = BuildContext("/read", server);
            // DefaultHttpContext.Connection.ClientCertificate is null by
            // default — the dispatcher must surface that as null on the
            // invocation context, not throw.
            WriteJsonBody(context.Request, "{}");

            await WebApiEndpointDispatcher
                .HandleAsync<ReadRequest, ReadResponse>(context)
                .ConfigureAwait(false);

            Assert.That(server.LastInvocation!.ClientCertificate, Is.Null);
            Assert.That(server.LastInvocation!.SecureChannelId,
                Is.EqualTo(context.TraceIdentifier),
                "SecureChannelId must default to the HTTP request's trace " +
                "identifier so server-side diagnostics can correlate.");
        }

        [Test]
        public void HandleAsyncThrowsOnNullContext()
        {
            Assert.That(
                async () => await WebApiEndpointDispatcher
                    .HandleAsync<ReadRequest, ReadResponse>(null!)
                    .ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }
        private static DefaultHttpContext BuildContext(
            string path,
            StubServer server,
            ISessionlessIdentityProvider? identityProvider = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IWebApiServer>(server);
            services.AddSingleton(NullLoggerFactory.Instance);
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(
                NullLoggerFactory.Instance);
            if (identityProvider != null)
            {
                services.AddSingleton(identityProvider);
            }

            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };
            context.Request.Method = "POST";
            context.Request.Path = path;
            context.Request.ContentType =
                WebApiMediaType.FormatContentType(WebApiEncoding.Compact);
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static void WriteJsonBody(HttpRequest request, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            request.Body = new MemoryStream(payload);
            request.ContentLength = payload.Length;
        }

        private static string ReadResponseBody(HttpResponse response)
        {
            response.Body.Position = 0;
            using var reader = new StreamReader(response.Body, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private sealed class StubServer : IWebApiServer
        {
            public StubServer()
            {
                MessageContext = ServiceMessageContext.CreateEmpty(new TestTelemetryContext());
            }

            public IServiceMessageContext MessageContext { get; }
            public bool IsReady => true;
            public IServiceRequest? LastRequest { get; private set; }
            public WebApiInvocationContext? LastInvocation { get; private set; }
            public Func<IServiceRequest, IServiceResponse>? ResponseFactory { get; set; }

            public ValueTask<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                WebApiInvocationContext context,
                CancellationToken ct)
            {
                LastRequest = request;
                LastInvocation = context;

                IServiceResponse response = ResponseFactory?.Invoke(request)
                    ?? BuildTypedResponseForReadOrBrowse(request);
                return new ValueTask<IServiceResponse>(response);
            }

            private static IServiceResponse BuildTypedResponseForReadOrBrowse(IServiceRequest request)
            {
                var header = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                return request switch
                {
                    ReadRequest => new ReadResponse { ResponseHeader = header },
                    BrowseRequest => new BrowseResponse { ResponseHeader = header },
                    _ => new ServiceFault { ResponseHeader = header }
                };
            }
        }

        private sealed class StubIdentityProvider : ISessionlessIdentityProvider
        {
            private readonly IUserIdentity m_identity;

            public StubIdentityProvider(IUserIdentity identity)
            {
                m_identity = identity;
            }

            public int ResolveCalls { get; private set; }

            public IUserIdentity? Resolve(HttpContext context)
            {
                ResolveCalls++;
                return m_identity;
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
