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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.WebApi.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WebApiTransportChannel"/> — verifies
    /// the channel correctly adapts the Web API binding to
    /// <see cref="ITransportChannel"/>, dispatches IServiceRequest →
    /// route → POST → decode, and maps HTTP errors to
    /// <see cref="ServiceResultException"/>.
    /// </summary>
    [TestFixture]
    [Category("WebApiTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class WebApiTransportChannelTests
    {
        private TestServer? m_server;
        private HttpMessageHandler? m_handler;
        private StubServer? m_stubServer;
        private static readonly Uri s_baseAddress = new("https://localhost/");

        [SetUp]
        public void SetUp()
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_stubServer = new StubServer(context);

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
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
            m_handler = m_server.CreateHandler();
        }

        [TearDown]
        public void TearDown()
        {
            m_handler?.Dispose();
            m_server?.Dispose();
        }

        [Test]
        public async Task SendRequestAsyncDispatchesReadAndDecodesResponse()
        {
            using WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 4242 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(m_stubServer!.LastRequest, Is.InstanceOf<ReadRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(4242u));
        }

        [Test]
        public async Task SendRequestAsyncDispatchesBrowseAndDecodesResponse()
        {
            using WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);

            var request = new BrowseRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 17 },
                View = new ViewDescription(),
                NodesToBrowse = new ArrayOf<BrowseDescription>()
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<BrowseResponse>());
            Assert.That(m_stubServer!.LastRequest, Is.InstanceOf<BrowseRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(17u));
        }

        [Test]
        public async Task SendRequestAsyncRejectsUnknownRequestType()
        {
            using WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);

            var bogus = new UnknownRequest();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(bogus, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public void SendRequestAsyncThrowsBadNotConnectedBeforeOpen()
        {
            using var channel = new WebApiTransportChannel(new TelemetryStub());

            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task CloseAsyncReleasesInnerClient()
        {
            WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));

            channel.Dispose();
        }

        [Test]
        public async Task DisposedChannelRejectsSend()
        {
            WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            channel.Dispose();

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void OpenAsyncNormalizesSyntheticUriScheme()
        {
            using var channel = new WebApiTransportChannel(new TelemetryStub(),
                new WebApiClientOptions { HttpMessageHandler = m_handler, DisposeHandler = false });

            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = "opc.https+webapi://localhost/",
                    TransportProfileUri = Profiles.HttpsOpenApiTransport
                },
                Configuration = new EndpointConfiguration(),
                Factory = m_stubServer!.MessageContext.Factory,
                NamespaceUris = new NamespaceTable()
            };
            var syntheticUrl = new Uri("opc.https+webapi://localhost/");

            // Must not throw — the channel normalises the synthetic scheme
            // to plain https:// before handing it to HttpClient.
            Assert.DoesNotThrowAsync(async () =>
                await channel.OpenAsync(syntheticUrl, settings, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public void SupportedFeaturesIsNone()
        {
            using var channel = new WebApiTransportChannel(new TelemetryStub());
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None));
        }

        [Test]
        public async Task ReconnectAsyncIsNoOpAndDoesNotThrow()
        {
            using WebApiTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);

            await channel.ReconnectAsync(connection: null, CancellationToken.None).ConfigureAwait(false);

            // Channel still works after the no-op reconnect.
            var request = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } };
            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response, Is.InstanceOf<ReadResponse>());
        }

        // === Helpers ====================================================

        private async Task<WebApiTransportChannel> OpenChannelAsync()
        {
            var channel = new WebApiTransportChannel(
                new TelemetryStub(),
                new WebApiClientOptions
                {
                    HttpMessageHandler = m_handler,
                    DisposeHandler = false
                });
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = s_baseAddress.AbsoluteUri,
                    TransportProfileUri = Profiles.HttpsOpenApiTransport
                },
                Configuration = new EndpointConfiguration(),
                Factory = m_stubServer!.MessageContext.Factory,
                NamespaceUris = new NamespaceTable()
            };
            await channel.OpenAsync(s_baseAddress, settings, CancellationToken.None)
                .ConfigureAwait(false);
            return channel;
        }

        // === Stub fixtures ==============================================

        private sealed class UnknownRequest : IServiceRequest
        {
            public RequestHeader RequestHeader { get; set; } = new();
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId JsonEncodingId => ExpandedNodeId.Null;
            public void Decode(IDecoder decoder) { }
            public void Encode(IEncoder encoder) { }
            public bool IsEqual(IEncodeable? encodeable) => false;
            public object Clone() => new UnknownRequest();
        }

        private sealed class StubServer : IWebApiServer
        {
            public StubServer(IServiceMessageContext messageContext)
            {
                MessageContext = messageContext;
            }

            public IServiceMessageContext MessageContext { get; }
            public bool IsReady => true;
            public IServiceRequest? LastRequest { get; private set; }

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

                IServiceResponse response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
                var header = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                route.ResponseType
                    .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                    .SetValue(response, header);
                return new ValueTask<IServiceResponse>(response);
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
