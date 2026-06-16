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
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.WebApi.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WebApiWssTransportChannel"/> — the
    /// WSS <c>opcua+openapi</c> sub-protocol channel (OPC UA Part 6
    /// §7.5.2; OPC Foundation
    /// <see href="https://profiles.opcfoundation.org/profile/2339">profile/2339</see>).
    /// Verifies wire-shape round-trip, sub-protocol negotiation
    /// (plain <c>opcua+openapi</c> and bearer variant
    /// <c>opcua+openapi+&lt;accesstoken&gt;</c>), URI normalisation,
    /// the no-multiplex feature flag, and lifecycle (close / dispose /
    /// reconnect) contract.
    /// </summary>
    /// <remarks>
    /// The stub server is a minimal Kestrel host listening on
    /// <c>http://127.0.0.1:0</c> with <see cref="WebSocketOptions"/>
    /// enabled — plain <c>ws://</c> sidesteps the TLS / certificate
    /// management overhead unit tests don't need. The same wire
    /// envelope (<c>{TypeId, Body}</c> standard OPC UA JSON message)
    /// is reused, so the channel exercises the same encode/decode path
    /// it does against the real <c>HttpsTransportListener.AcceptWebSocketOpenApiAsync</c>.
    /// </remarks>
    [TestFixture]
    [Category("WebApiWssTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WebApiWssTransportChannelTests
    {
        private IHost? m_host;
        private Uri m_baseUri = null!;
        private ServiceMessageContext? m_messageContext;
        private string? m_lastNegotiatedSubProtocol;
        private IServiceRequest? m_lastRequest;
        private Func<IServiceRequest, IServiceResponse>? m_responder;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            // Populate the encodeable factory with the standard
            // OPC UA Core types so the client-side
            // JsonDecoder.DecodeMessage<IServiceResponse> can resolve
            // ReadResponse / BrowseResponse / etc. from the wire
            // envelope's TypeId.
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();
            m_lastNegotiatedSubProtocol = null;
            m_lastRequest = null;
            m_responder = DefaultResponder;

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts =>
                    {
                        opts.Listen(IPAddress.Loopback, 0);
                    });
                    webHost.ConfigureServices(s => { });
                    webHost.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(HandleWebSocketAsync);
                    });
                });

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            IServer server = m_host.Services.GetRequiredService<IServer>();
            string baseAddress = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            m_baseUri = new Uri(baseAddress.Replace("http://", "ws://", StringComparison.Ordinal));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_host != null)
            {
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
                m_host = null;
            }
        }

        [Test]
        public async Task SendRequestAsyncRoundTripsReadRequestAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 4242 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(m_lastRequest, Is.InstanceOf<ReadRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(4242u));
        }

        [Test]
        public async Task SendRequestAsyncRoundTripsBrowseRequestAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

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
            Assert.That(m_lastRequest, Is.InstanceOf<BrowseRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(17u));
        }

        [Test]
        public async Task OpenAsyncNegotiatesPlainSubProtocolWhenNoBearerAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            // Send any request to force the handshake to land server-side.
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };
            _ = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(m_lastNegotiatedSubProtocol, Is.EqualTo(Profiles.OpcUaWsSubProtocolOpenApi),
                "Without a bearer token, the channel must negotiate plain " +
                "'opcua+openapi'.");
        }

        [Test]
        public async Task OpenAsyncRejectsBearerTokenOverPlainWebSocketAsync()
        {
            // sec-3 fix: the bearer-prefix sub-protocol leaks the token
            // through every TCP intermediary in the 101 handshake. The
            // client now refuses to send the token over plain ws://.
            // Over wss:// (real TLS) the credential is at least hidden
            // from network observers; that path is covered by the
            // integration tests against the reference server.
            const string token = "abc.def.ghi";
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using WebApiWssTransportChannel channel = await OpenChannelAsync(
                    new WebApiClientOptions { BearerToken = token })
                    .ConfigureAwait(false);
            })!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                "Bearer-prefix sub-protocol over plain ws:// must be rejected with " +
                "BadSecurityChecksFailed (sec-3 fix).");
        }

        [Test]
        public void SupportedFeaturesIsNone()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.None),
                "The WSS channel is single-threaded and does not advertise " +
                "any optional transport features (no multiplexing).");
        }

        [Test]
        public void UriSchemeIsOpcWssOpenApi()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            Assert.That(channel.UriScheme, Is.EqualTo(Utils.UriSchemeOpcWssOpenApi));
        }

        [Test]
        public async Task ReconnectAsyncThrowsBadNotSupportedAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .ReconnectAsync(connection: null, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "Reconnect over WSS requires re-running CreateSession/Activate; " +
                "the channel surfaces BadNotSupported so ManagedSession's " +
                "reconnect policy can rebuild the session.");
        }

        [Test]
        public void SendRequestAsyncThrowsBadNotConnectedBeforeOpen()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());

            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task DisposedChannelRejectsSendAsync()
        {
            WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);
            channel.Dispose();

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void OpenAsyncRejectsBadUriScheme()
        {
            // Smoke check: an unrecognised URI scheme is allowed through
            // NormalizeUrl unchanged but ClientWebSocket itself rejects
            // anything outside ws/wss. The channel surfaces the
            // underlying connect failure rather than swallowing it.
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = "http://127.0.0.1:1/",
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable()
            };
            // ClientWebSocket rejects 'http' scheme outright.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await channel
                    .OpenAsync(new Uri("http://127.0.0.1:1/"), settings, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task CloseAsyncReleasesWebSocketAsync()
        {
            WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            // After Close the channel reports as not connected on next send.
            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));

            channel.Dispose();
        }

        [Test]
        public async Task ServerClosedMidRequestMapsToBadConnectionClosedAsync()
        {
            // Replace the responder with one that closes the socket
            // before sending a response.
            m_responder = req =>
            {
                throw new CloseConnectionSentinel();
            };

            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 99 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed),
                "When the server closes the WebSocket while a request is " +
                "in flight, the channel surfaces BadConnectionClosed.");
        }
        private async Task<WebApiWssTransportChannel> OpenChannelAsync(
            WebApiClientOptions? options = null)
        {
            var channel = new WebApiWssTransportChannel(new TelemetryStub(), options);
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    // Non-empty server cert short-circuits
                    // HydrateEndpointFromServerAsync so the unit tests
                    // don't have to stub GetEndpoints.
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable()
            };
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);
            return channel;
        }

        private async Task HandleWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }

            string? subProtocol = context.WebSockets.WebSocketRequestedProtocols
                .FirstOrDefault();
            if (subProtocol == null ||
                (!string.Equals(subProtocol, Profiles.OpcUaWsSubProtocolOpenApi, StringComparison.Ordinal) &&
                 !subProtocol.StartsWith(Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix, StringComparison.Ordinal)))
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }
            m_lastNegotiatedSubProtocol = subProtocol;

            using WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(subProtocol)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;
            ServiceMessageContext messageContext = m_messageContext!;
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                byte[]? requestBytes;
                try
                {
                    requestBytes = await ReceiveMessageAsync(ws, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (requestBytes == null)
                {
                    return;
                }

                IServiceRequest decoded = JsonDecoder.DecodeMessage<IServiceRequest>(
                    requestBytes,
                    messageContext);
                m_lastRequest = decoded;

                IServiceResponse response;
                try
                {
                    response = m_responder!(decoded);
                }
                catch (CloseConnectionSentinel)
                {
                    // Abort the socket without an OPC UA response —
                    // exercises the BadConnectionClosed path in the
                    // client receive loop.
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "test-close",
                        ct).ConfigureAwait(false);
                    return;
                }

                byte[] responseBytes;
                using (var stream = new MemoryStream())
                {
                    using (var encoder = new JsonEncoder(
                        stream,
                        messageContext,
                        JsonEncoderOptions.Compact))
                    {
                        encoder.EncodeMessage(response, response.TypeId);
                        encoder.Close();
                    }
                    responseBytes = stream.ToArray();
                }
                await ws.SendAsync(
                    new ArraySegment<byte>(responseBytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]?> ReceiveMessageAsync(
            WebSocket ws,
            CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            byte[] receive = new byte[8192];
            while (true)
            {
                WebSocketReceiveResult result = await ws
                    .ReceiveAsync(new ArraySegment<byte>(receive), ct)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client-close",
                        ct).ConfigureAwait(false);
                    return null;
                }
                buffer.Write(receive, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return buffer.ToArray();
                }
            }
        }

        private static IServiceResponse DefaultResponder(IServiceRequest request)
        {
            if (!WebApiServiceRoutes.TryGetByRequestType(request.GetType(), out WebApiServiceRoute route))
            {
                throw new InvalidOperationException(
                    $"No matching route for {request.GetType().Name}.");
            }
            var response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
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
            return response;
        }

        private sealed class CloseConnectionSentinel : Exception
        {
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
