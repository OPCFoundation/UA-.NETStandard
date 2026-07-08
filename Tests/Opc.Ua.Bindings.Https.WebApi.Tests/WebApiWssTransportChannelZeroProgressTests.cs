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
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Regression test for the WebApi WSS zero-progress continuation-
    /// frame guard. Without it, the client's
    /// <c>ReceiveMessageAsync</c> would spin forever when the server
    /// sends empty (count == 0) WebSocket continuation frames with
    /// <c>EndOfMessage == false</c> (CPU DoS). Mirrors the
    /// <c>WebSocketByteTransport</c> guard for the <c>opcua+uacp</c>
    /// sub-protocol.
    /// </summary>
    [TestFixture]
    [Category("WebApiWssTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WebApiWssTransportChannelZeroProgressTests
    {
        private IHost? m_host;
        private Uri m_baseUri = null!;
        private ServiceMessageContext? m_messageContext;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts => opts.Listen(IPAddress.Loopback, 0));
                    webHost.ConfigureServices(_ => { });
                    webHost.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(HandleZeroProgressWebSocketAsync);
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
        public async Task ReceiveLoopRejectsEmptyContinuationFrameAsync()
        {
            // After accepting the request, the server sends a single
            // empty (count == 0) continuation frame with
            // EndOfMessage == false. Without the guard, the client's
            // ReceiveMessageAsync would spin reading more empty frames
            // forever (CPU DoS). The guard raises BadEncodingLimitsExceeded
            // on the first zero-progress frame.
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable()
            };

            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded),
                "Zero-progress continuation frame must surface as " +
                "BadEncodingLimitsExceeded.");
        }

        private static async Task HandleZeroProgressWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }
            string? sub = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
            if (sub == null)
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }
            using WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(sub)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;
            // Drain whatever the client sends (one request).
            byte[] receive = new byte[8192];
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r = await ws
                    .ReceiveAsync(new ArraySegment<byte>(receive), ct)
                    .ConfigureAwait(false);
                if (r.EndOfMessage || r.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }

            // Send a single empty continuation frame with EndOfMessage=false.
            // This is what triggers the client's zero-progress guard.
            await ws.SendAsync(
                new ArraySegment<byte>([]),
                WebSocketMessageType.Text,
                endOfMessage: false,
                ct).ConfigureAwait(false);

            // The client should drop the connection on the empty frame;
            // we keep the server side alive briefly so the close handshake
            // completes cleanly.
            try
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on test teardown
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
