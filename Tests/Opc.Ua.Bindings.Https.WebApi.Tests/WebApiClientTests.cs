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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    namespace Tests
    {
        /// <summary>
        /// Unit tests for <see cref="WebApiClient"/> — constructor options,
        /// <see cref="WebApiClient.Create"/> factory, the
        /// <see cref="WebApiClient.InvokeAsync{TRequest,TResponse}"/> core dispatch
        /// path, <see cref="WebApiClient.InvokeRouteAsync"/> (non-generic), and a
        /// representative selection of the 29 typed service-method wrappers.
        /// Uses a custom <see cref="HttpMessageHandler"/> stub; no live network.
        /// </summary>
        [TestFixture]
        [Category("WebApiClient")]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        [Parallelizable]
        public sealed class WebApiClientTests
        {
            private static readonly Uri s_baseAddress = new("https://test.example.com/");
            private ServiceMessageContext m_messageContext = null!;

            [SetUp]
            public void SetUp()
            {
                m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
                m_messageContext.Factory.Builder
                    .AddEncodeableTypes(typeof(ReadRequest).Assembly)
                    .Commit();
            }

            // ─────────────────────────── Constructor ────────────────────────────

            [Test]
            public void ConstructorThrowsForNullHttpClient()
            {
                Assert.That(
                    () => new WebApiClient(null!, options: null),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("httpClient"));
            }

            [Test]
            public void ConstructorThrowsWhenBearerAndBasicBothSet()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var options = new WebApiClientOptions
                {
                    BearerToken = "tok",
                    BasicCredentials = ("user", "pass")
                };

                Assert.That(
                    () => new WebApiClient(httpClient, options),
                    Throws.InvalidOperationException);
            }

            [Test]
            public void ConstructorSetsBearerAuthorizationHeader()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var options = new WebApiClientOptions { BearerToken = "my-token" };

                using var client = new WebApiClient(httpClient, options);

                Assert.That(
                    httpClient.DefaultRequestHeaders.Authorization?.Scheme,
                    Is.EqualTo("Bearer"));
                Assert.That(
                    httpClient.DefaultRequestHeaders.Authorization?.Parameter,
                    Is.EqualTo("my-token"));
            }

            [Test]
            public void ConstructorSetsBasicAuthorizationHeader()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var options = new WebApiClientOptions { BasicCredentials = ("alice", "s3cret") };

                using var client = new WebApiClient(httpClient, options);

                Assert.That(
                    httpClient.DefaultRequestHeaders.Authorization?.Scheme,
                    Is.EqualTo("Basic"));
                string decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        httpClient.DefaultRequestHeaders.Authorization!.Parameter!));
                Assert.That(decoded, Is.EqualTo("alice:s3cret"));
            }

            [Test]
            public void ConstructorSetsAcceptHeaderForCompactEncoding()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var options = new WebApiClientOptions { Encoding = WebApiEncoding.Compact };

                using var client = new WebApiClient(httpClient, options);

                bool hasApplicationJson = false;
                foreach (MediaTypeWithQualityHeaderValue h in httpClient.DefaultRequestHeaders.Accept)
                {
                    if (h.MediaType == "application/json")
                    {
                        hasApplicationJson = true;
                    }
                }
                Assert.That(hasApplicationJson, Is.True,
                    "Compact encoding must advertise application/json in Accept.");
            }

            [Test]
            public void ConstructorSetsRequestTimeoutWhenSpecified()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var timeout = TimeSpan.FromSeconds(42);
                var options = new WebApiClientOptions { RequestTimeout = timeout };

                using var client = new WebApiClient(httpClient, options);

                Assert.That(httpClient.Timeout, Is.EqualTo(timeout));
            }

            [Test]
            public void ConstructorWithNullOptionsUsesDefaults()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };

                using var client = new WebApiClient(httpClient, options: null);

                Assert.That(client.Encoding, Is.EqualTo(WebApiEncoding.Compact));
            }

            // ─────────────────────────── Create factory ─────────────────────────

            [Test]
            public void CreateThrowsForNullBaseAddress()
            {
                Assert.That(
                    () => WebApiClient.Create(null!, options: null),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("baseAddress"));
            }

            [Test]
            public void CreateNormalizesOpcHttpsWebApiScheme()
            {
                var opcUri = new Uri("opc.https+webapi://server.example.com/");
                using var client = WebApiClient.Create(opcUri);

                Assert.That(client.BaseAddress.Scheme, Is.EqualTo("https"),
                    "Create must convert opc.https+webapi:// to https://.");
            }

            [Test]
            public void CreateNormalizesOpcHttpsScheme()
            {
                var opcUri = new Uri("opc.https://server.example.com/");
                using var client = WebApiClient.Create(opcUri);

                Assert.That(client.BaseAddress.Scheme, Is.EqualTo("https"),
                    "Create must convert opc.https:// to https://.");
            }

            [Test]
            public void CreateWithCustomHandlerDoesNotDisposeHandlerByDefault()
            {
                var handler = new DisposeTrackingHandler();
                var options = new WebApiClientOptions
                {
                    HttpMessageHandler = handler,
                    DisposeHandler = false
                };

                using (WebApiClient client = WebApiClient.Create(s_baseAddress, options))
                {
                    // Dispose the client — handler is borrowed, must NOT be disposed.
                }

                Assert.That(handler.Disposed, Is.False,
                    "When DisposeHandler=false, Create must not dispose the injected handler.");
            }

            [Test]
            public void EncodingPropertyReturnsOptionValue()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var options = new WebApiClientOptions { Encoding = WebApiEncoding.Verbose };

                using var client = new WebApiClient(httpClient, options);

                Assert.That(client.Encoding, Is.EqualTo(WebApiEncoding.Verbose));
            }

            [Test]
            public void BaseAddressPropertyReturnsHttpClientBaseAddress()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };

                using var client = new WebApiClient(httpClient);

                Assert.That(client.BaseAddress, Is.EqualTo(s_baseAddress));
            }

            // ────────────────────────── InvokeAsync<,> ──────────────────────────

            [Test]
            public async Task InvokeAsyncReturnsDecodedResponseAsync()
            {
                var expectedResponse = new ReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = 7,
                        ServiceResult = StatusCodes.Good,
                        Timestamp = DateTime.UtcNow,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    },
                    Results = new ArrayOf<DataValue>()
                };

                using WebApiClient client = BuildClientWithResponse(expectedResponse);

                var request = new ReadRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 7 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                };

                ReadResponse response = await client
                    .InvokeAsync<ReadRequest, ReadResponse>(request)
                    .ConfigureAwait(false);

                Assert.That(response, Is.Not.Null);
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(7u));
            }

            [Test]
            public void InvokeAsyncThrowsForNullRequest()
            {
                using WebApiClient client = BuildClientWithResponse(new ReadResponse());

                Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await client.InvokeAsync<ReadRequest, ReadResponse>(null!).ConfigureAwait(false));
            }

            [Test]
            public void InvokeAsyncThrowsHttpRequestExceptionOnNon2xxStatus()
            {
                var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
#pragma warning disable CA2000  // handler ownership transferred to HttpClient; disposed with client
                var handler = new FixedResponseHandler(errorResponse);
                var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
#pragma warning restore CA2000
                using var client = new WebApiClient(httpClient,
                    new WebApiClientOptions { MessageContext = m_messageContext });

                var request = new ReadRequest
                {
                    RequestHeader = new RequestHeader(),
                    NodesToRead = new ArrayOf<ReadValueId>()
                };

                Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await client
                        .InvokeAsync<ReadRequest, ReadResponse>(request)
                        .ConfigureAwait(false));
            }

            [Test]
            public void InvokeAsyncPropagatesCancellationToken()
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();

#pragma warning disable CA2000  // handler ownership transferred to HttpClient; disposed with client
                var handler = new CancelOnSendHandler();
                var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
#pragma warning restore CA2000
                using var client = new WebApiClient(httpClient,
                    new WebApiClientOptions { MessageContext = m_messageContext });

                var request = new ReadRequest
                {
                    RequestHeader = new RequestHeader(),
                    NodesToRead = new ArrayOf<ReadValueId>()
                };

                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await client
                        .InvokeAsync<ReadRequest, ReadResponse>(request, cts.Token)
                        .ConfigureAwait(false));
            }

            [Test]
            public void InvokeAsyncThrowsObjectDisposedExceptionAfterDispose()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var client = new WebApiClient(httpClient,
                    new WebApiClientOptions { MessageContext = m_messageContext });
                client.Dispose();

                var request = new ReadRequest { RequestHeader = new RequestHeader() };

                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await client
                        .InvokeAsync<ReadRequest, ReadResponse>(request)
                        .ConfigureAwait(false));
            }

            // ────────────────────────── InvokeRouteAsync ────────────────────────

#pragma warning disable IL2026  // RequiresUnreferencedCode: test exercises the non-generic path intentionally
            [Test]
            public async Task InvokeRouteAsyncReturnsDecodedResponseAsync()
            {
                var expectedResponse = new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = 42,
                        ServiceResult = StatusCodes.Good,
                        Timestamp = DateTime.UtcNow,
                        StringTable = new ArrayOf<string>(),
                        AdditionalHeader = new ExtensionObject()
                    },
                    Results = new ArrayOf<BrowseResult>()
                };

                using WebApiClient client = BuildClientWithResponse(expectedResponse);

                WebApiServiceRoutes.TryGetByRequestType(typeof(BrowseRequest), out WebApiServiceRoute route);

                var request = new BrowseRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 42 },
                    View = new ViewDescription(),
                    NodesToBrowse = new ArrayOf<BrowseDescription>()
                };

                IServiceResponse response = await client
                    .InvokeRouteAsync(route, request)
                    .ConfigureAwait(false);

                Assert.That(response, Is.InstanceOf<BrowseResponse>());
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42u));
            }

            [Test]
            public void InvokeRouteAsyncThrowsForMismatchedRequestType()
            {
                using WebApiClient client = BuildClientWithResponse(new ReadResponse());

                WebApiServiceRoutes.TryGetByRequestType(typeof(BrowseRequest), out WebApiServiceRoute route);

                var wrongRequest = new ReadRequest { RequestHeader = new RequestHeader() };

                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client
                        .InvokeRouteAsync(route, wrongRequest)
                        .ConfigureAwait(false));
            }

            [Test]
            public void InvokeRouteAsyncThrowsForNullRequest()
            {
                using WebApiClient client = BuildClientWithResponse(new ReadResponse());

                WebApiServiceRoutes.TryGetByRequestType(typeof(ReadRequest), out WebApiServiceRoute route);

                Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await client
                        .InvokeRouteAsync(route, null!)
                        .ConfigureAwait(false));
            }
#pragma warning restore IL2026

            // ────────────────────── Service method wrappers ─────────────────────

            private static IEnumerable<TestCaseData> ServiceMethodTestCases()
            {
                yield return new TestCaseData(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 }, NodesToRead = new ArrayOf<ReadValueId>() },
                    new ReadResponse { ResponseHeader = MakeHeader(1), Results = new ArrayOf<DataValue>() })
                    .SetName("ReadAsync");

                yield return new TestCaseData(
                    new WriteRequest { RequestHeader = new RequestHeader { RequestHandle = 2 }, NodesToWrite = new ArrayOf<WriteValue>() },
                    new WriteResponse { ResponseHeader = MakeHeader(2), Results = new ArrayOf<StatusCode>() })
                    .SetName("WriteAsync");

                yield return new TestCaseData(
                    new BrowseRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 3 },
                        View = new ViewDescription(),
                        NodesToBrowse = new ArrayOf<BrowseDescription>()
                    },
                    new BrowseResponse
                    {
                        ResponseHeader = MakeHeader(3),
                        Results = new ArrayOf<BrowseResult>()
                    })
                    .SetName("BrowseAsync");

                yield return new TestCaseData(
                    new CallRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 4 },
                        MethodsToCall = new ArrayOf<CallMethodRequest>()
                    },
                    new CallResponse
                    {
                        ResponseHeader = MakeHeader(4),
                        Results = new ArrayOf<CallMethodResult>()
                    })
                    .SetName("CallAsync");

                yield return new TestCaseData(
                    new GetEndpointsRequest { RequestHeader = new RequestHeader { RequestHandle = 5 } },
                    new GetEndpointsResponse
                    {
                        ResponseHeader = MakeHeader(5),
                        Endpoints = new ArrayOf<EndpointDescription>()
                    })
                    .SetName("GetEndpointsAsync");

                yield return new TestCaseData(
                    new CreateSessionRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 6 },
                        ClientDescription = new ApplicationDescription(),
                        ClientNonce = new ByteString(),
                        ClientCertificate = new ByteString()
                    },
                    new CreateSessionResponse
                    {
                        ResponseHeader = MakeHeader(6),
                        SessionId = NodeId.Null,
                        AuthenticationToken = NodeId.Null,
                        ServerNonce = new ByteString(),
                        ServerCertificate = new ByteString(),
                        ServerEndpoints = new ArrayOf<EndpointDescription>(),
                        ServerSoftwareCertificates = new ArrayOf<SignedSoftwareCertificate>(),
                        ServerSignature = new SignatureData()
                    })
                    .SetName("CreateSessionAsync");
            }

            [Test, TestCaseSource(nameof(ServiceMethodTestCases))]
            public async Task ServiceMethodDispatchesViaInvokeAsyncAsync(
                IServiceRequest request,
                IServiceResponse expectedResponse)
            {
                using WebApiClient client = BuildClientWithAnyResponseOfType(
                    expectedResponse.GetType(),
                    expectedResponse);

                IServiceResponse? actual = request switch
                {
                    ReadRequest r => await client.ReadAsync((ReadRequest)r).ConfigureAwait(false),
                    WriteRequest r => await client.WriteAsync((WriteRequest)r).ConfigureAwait(false),
                    BrowseRequest r => await client.BrowseAsync((BrowseRequest)r).ConfigureAwait(false),
                    CallRequest r => await client.CallAsync((CallRequest)r).ConfigureAwait(false),
                    GetEndpointsRequest r => await client.GetEndpointsAsync((GetEndpointsRequest)r).ConfigureAwait(false),
                    CreateSessionRequest r => await client.CreateSessionAsync((CreateSessionRequest)r).ConfigureAwait(false),
                    _ => null
                };

                Assert.That(actual, Is.Not.Null,
                    $"Service method for {request.GetType().Name} must return a non-null response.");
                Assert.That(actual, Is.InstanceOf(expectedResponse.GetType()));
                Assert.That(actual!.ResponseHeader.RequestHandle,
                    Is.EqualTo(request.RequestHeader.RequestHandle));
            }

            // ─────────────────────────── Dispose ────────────────────────────────

            [Test]
            public void DisposeIsIdempotent()
            {
                using var httpClient = new HttpClient { BaseAddress = s_baseAddress };
                var client = new WebApiClient(httpClient);

                client.Dispose();

                Assert.DoesNotThrow(client.Dispose, "Dispose must be idempotent.");
            }

            // ─────────────────────────── Helpers ────────────────────────────────

            private WebApiClient BuildClientWithResponse<TResponse>(TResponse response)
                where TResponse : IServiceResponse
            {
                return BuildClientWithAnyResponseOfType(typeof(TResponse), response);
            }

            private WebApiClient BuildClientWithAnyResponseOfType(
                Type responseType,
                IServiceResponse response)
            {
                byte[] body = WebApiBodyCodec.EncodeBody(
                    response,
                    m_messageContext,
                    WebApiMediaType.ToEncoderOptions(WebApiEncoding.Compact));

                var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body)
                };
                responseMessage.Content.Headers.ContentType =
                    MediaTypeHeaderValue.Parse("application/json");

#pragma warning disable CA2000  // handler ownership transferred to HttpClient; disposed with client
                var handler = new FixedResponseHandler(responseMessage);
                var httpClient = new HttpClient(handler) { BaseAddress = s_baseAddress };
#pragma warning restore CA2000

                return new WebApiClient(
                    httpClient,
                    new WebApiClientOptions { MessageContext = m_messageContext });
            }

            private static ResponseHeader MakeHeader(uint handle)
            {
                return new ResponseHeader
                {
                    RequestHandle = handle,
                    ServiceResult = StatusCodes.Good,
                    Timestamp = DateTime.UtcNow,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
            }

            // ─────────────────────────── Inner stubs ────────────────────────────

            private sealed class FixedResponseHandler : HttpMessageHandler
            {
                private readonly HttpResponseMessage m_response;

                public FixedResponseHandler(HttpResponseMessage response)
                {
                    m_response = response;
                }

                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(m_response);
                }
            }

            private sealed class CancelOnSendHandler : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromCanceled<HttpResponseMessage>(ct);
                }
            }

            private sealed class DisposeTrackingHandler : HttpMessageHandler
            {
                public bool Disposed { get; private set; }

                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken ct)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    });
                }

                protected override void Dispose(bool disposing)
                {
                    Disposed = true;
                    base.Dispose(disposing);
                }
            }

            private sealed class TelemetryStub : TelemetryContextBase
            {
                public TelemetryStub()
                    : base(NullLoggerFactory.Instance)
                {
                }
            }
        }
    }
}
