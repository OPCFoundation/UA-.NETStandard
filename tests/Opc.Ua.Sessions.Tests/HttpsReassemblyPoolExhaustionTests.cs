/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#if NET5_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Idle HTTPS peers without credentials must not drain the shared
    /// <c>ResourceIsolationStage.ReassemblyBytes</c> pool. Every HTTPS request
    /// and JSON WebSocket upgrade used to reserve
    /// <c>3 * MaxMessageSize + MaxBufferSize</c> bytes before reading a single
    /// byte, and the <c>opcua+uajson</c> WebSocket held that reservation for its
    /// whole life, so a handful of idle sockets or stalled POSTs answered every
    /// further HTTPS request with HTTP 503. Bodies are now charged as their bytes
    /// arrive and released once decoded.
    /// </summary>
    [TestFixture]
    [Category("HttpsReassemblyPoolExhaustion")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HttpsReassemblyPoolExhaustionTests
    {
        // Secure expectation: this many idle opcua+uajson sockets must not
        // exhaust the shared pool and block an unrelated HTTPS request.
        private const int kSecureIdleTarget = 16;

        // Hard cap on idle sockets opened, so a fixed (non-defective) server
        // does not loop forever.
        private const int kMaxIdleSockets = 18;

        private const int kConnectTimeoutMs = 15_000;

        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ReferenceServer m_server;
        private string m_pkiRoot;
        private Uri m_wssUrl;
        private Uri m_httpsUrl;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // Same fixture the WSS/JSON integration tests use: a single
            // opc.wss base address, SecurityMode None, mutual TLS off so the
            // anonymous opcua+uajson sub-protocol is reachable.
            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                UriScheme = Utils.UriSchemeOpcWss,
                HttpsMutualTls = false,
                // Plenty of physical connections so the connection-count
                // admission never masks the reassembly-pool exhaustion.
                MaxChannelCount = 203,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            string port = m_serverFixture.Port.ToString(CultureInfo.InvariantCulture);
            m_wssUrl = new Uri(
                Utils.ReplaceLocalhost($"wss://localhost:{port}/" + nameof(ReferenceServer)));
            m_httpsUrl = new Uri(
                Utils.ReplaceLocalhost($"https://localhost:{port}/" + nameof(ReferenceServer)));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public async Task IdleUaJsonWebSocketsMustNotExhaustSharedReassemblyPoolAsync()
        {
            int maxMessageSize = m_serverFixture.Config.TransportQuotas!.MaxMessageSize;
            int maxBufferSize = m_serverFixture.Config.TransportQuotas!.MaxBufferSize;

            TestContext.Out.WriteLine(
                "Configuration: MaxMessageSize={0} bytes, MaxBufferSize={1} bytes, isolation mode=Balanced (default).",
                maxMessageSize, maxBufferSize);

            var openSockets = new List<ClientWebSocket>();
            int refusalAtIdleCount = -1;
            HttpStatusCode refusalStatus = default;

            try
            {
                int idleOpen = 0;
                while (true)
                {
                    // Probe whether an unrelated HTTPS request is still
                    // admitted while 'idleOpen' idle sockets are held.
                    HttpStatusCode probe = await ProbeHttpsPostAsync().ConfigureAwait(false);
                    TestContext.Out.WriteLine(
                        "idle opcua+uajson sockets held = {0,2}  ->  HTTPS POST probe = {1} ({2})",
                        idleOpen, (int)probe, probe);

                    if (probe == HttpStatusCode.ServiceUnavailable)
                    {
                        refusalAtIdleCount = idleOpen;
                        refusalStatus = probe;
                        break;
                    }

                    if (idleOpen >= kMaxIdleSockets)
                    {
                        break;
                    }

                    ClientWebSocket ws = await OpenIdleUaJsonWebSocketAsync().ConfigureAwait(false);
                    openSockets.Add(ws);
                    idleOpen++;
                }

                TestContext.Out.WriteLine(
                    "RESULT: an unrelated HTTPS POST was first refused (503) after {0} idle " +
                    "opcua+uajson WebSocket(s) were held open (secure expectation: at least {1}).",
                    refusalAtIdleCount, kSecureIdleTarget);
            }
            finally
            {
                // Close every idle socket to release the leases.
                foreach (ClientWebSocket ws in openSockets)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(2_000);
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure, "done", cts.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore
                    }
                    ws.Dispose();
                }
            }

            // ---- recovery check ----
            // After the leases are released the pool must recover and HTTPS
            // requests must be served again.
            HttpStatusCode recovery = HttpStatusCode.ServiceUnavailable;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                recovery = await ProbeHttpsPostAsync().ConfigureAwait(false);
                if (recovery != HttpStatusCode.ServiceUnavailable)
                {
                    break;
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            TestContext.Out.WriteLine(
                "RECOVERY: after closing all idle sockets, HTTPS POST probe = {0} ({1}).",
                (int)recovery, recovery);
            Assert.That(
                recovery,
                Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable),
                "Service did not recover after the idle WebSockets were closed.");

            // Asserted last so the recovery behaviour above is always measured
            // and reported.
            Assert.That(
                refusalAtIdleCount,
                Is.EqualTo(-1).Or.GreaterThanOrEqualTo(kSecureIdleTarget),
                $"Idle unauthenticated opcua+uajson WebSockets exhausted the shared " +
                $"HTTPS reassembly pool: an unrelated HTTPS POST was refused with " +
                $"{(int)refusalStatus} after only {refusalAtIdleCount} idle socket(s). " +
                $"A few idle sockets must not block unrelated HTTPS requests.");
        }

        /// <summary>
        /// POSTs that announce a MaxMessageSize body and never send it must not
        /// hold reassembly capacity for bytes that never arrived.
        /// </summary>
        [Test]
        public async Task StalledHttpsPostsMustNotExhaustSharedReassemblyPoolAsync()
        {
            int maxMessageSize = m_serverFixture.Config.TransportQuotas!.MaxMessageSize;
            var stalled = new List<(TcpClient Tcp, SslStream Ssl)>();
            HttpStatusCode probe;
            try
            {
                for (int ii = 0; ii < kSecureIdleTarget; ii++)
                {
                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, m_httpsUrl.Port).ConfigureAwait(false);
#pragma warning disable CA5359 // Accept the test server's self-signed certificate (test-only probe).
                    var ssl = new SslStream(
                        tcp.GetStream(),
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: static (_, _, _, _) => true);
#pragma warning restore CA5359
                    stalled.Add((tcp, ssl));
                    await ssl.AuthenticateAsClientAsync("localhost").ConfigureAwait(false);
                    byte[] head = Encoding.ASCII.GetBytes(
                        $"POST {m_httpsUrl.AbsolutePath} HTTP/1.1\r\n" +
                        "Host: localhost\r\n" +
                        "Content-Type: application/octet-stream\r\n" +
                        $"Content-Length: {maxMessageSize.ToString(CultureInfo.InvariantCulture)}\r\n" +
                        "\r\n");
                    await ssl.WriteAsync(head).ConfigureAwait(false);
                    await ssl.FlushAsync().ConfigureAwait(false);
                }

                // Let the server admit every stalled head before probing.
                await Task.Delay(500).ConfigureAwait(false);
                probe = await ProbeHttpsPostAsync().ConfigureAwait(false);
                TestContext.Out.WriteLine(
                    "stalled POSTs held = {0} (Content-Length {1}) -> HTTPS POST probe = {2} ({3})",
                    stalled.Count, maxMessageSize, (int)probe, probe);
            }
            finally
            {
                foreach ((TcpClient tcp, SslStream ssl) in stalled)
                {
                    ssl.Dispose();
                    tcp.Dispose();
                }
            }

            Assert.That(
                probe,
                Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable),
                $"{kSecureIdleTarget} stalled HTTPS POSTs exhausted the shared reassembly pool.");
        }

        /// <summary>
        /// Opens a single idle opcua+uajson WebSocket and leaves it open
        /// (no data sent, no close).
        /// </summary>
        private async Task<ClientWebSocket> OpenIdleUaJsonWebSocketAsync()
        {
            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("opcua+uajson");
#pragma warning disable CA5359 // Accept the test server's self-signed certificate (test-only probe).
            ws.Options.RemoteCertificateValidationCallback =
                (sender, certificate, chain, errors) => true;
#pragma warning restore CA5359
            using var cts = new CancellationTokenSource(kConnectTimeoutMs);
            await ws.ConnectAsync(m_wssUrl, cts.Token).ConfigureAwait(false);
            return ws;
        }

        /// <summary>
        /// Sends a plain HTTPS binary POST and returns the status code. A
        /// 503 means the request was refused at the reassembly-pool
        /// admission stage; any other status means it was admitted (and
        /// then rejected/served further down the pipeline).
        /// </summary>
        private async Task<HttpStatusCode> ProbeHttpsPostAsync()
        {
#pragma warning disable CA5359 // Accept the test server's self-signed certificate (test-only probe).
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true
            };
#pragma warning restore CA5359
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            // One-byte body with the UA binary content type so the request
            // routes to SendBinaryAsync, whose first step is the reassembly
            // admission check (TryAdmitHttpBody).
            using var content = new ByteArrayContent(new byte[1]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using HttpResponseMessage response = await client
                .PostAsync(m_httpsUrl, content)
                .ConfigureAwait(false);
            return response.StatusCode;
        }
    }
}

#endif // NET5_0_OR_GREATER
