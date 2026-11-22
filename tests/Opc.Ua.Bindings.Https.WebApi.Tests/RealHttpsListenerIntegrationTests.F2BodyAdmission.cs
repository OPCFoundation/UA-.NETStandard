// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// REST request bodies are admitted like every other HTTPS body.
    ///
    /// <para>
    /// The WebApi body read used to allocate <c>new byte[Content-Length]</c>
    /// before a single body byte arrived, with no resource admission, and
    /// discovery routes are anonymous: a client that announced
    /// Content-Length = MaxMessageSize and never sent the body made the server
    /// allocate and hold a MaxMessageSize array per request.
    /// </para>
    ///
    /// <para>
    /// The request heads are written on a raw TLS socket (not HttpClient, which
    /// buffers the request body and so never flushes the head until the body is
    /// produced). Client and server share this process, so GC counters observe
    /// both; the control (the same number of connections with a tiny
    /// Content-Length head) subtracts the fixed per-connection / TLS overhead,
    /// isolating the Content-Length-proportional server allocation.
    /// </para>
    /// </summary>
    public sealed partial class RealHttpsListenerIntegrationTests
    {
        // EndpointConfiguration.Create() => MaxMessageSize = TcpMessageLimits.DefaultMaxMessageSize
        // = MinBufferSize (8192) * 256 = 2 MiB. The fixture uses EndpointConfiguration.Create().
        private const int kMaxMessageSize = 8192 * 256;
        private const int kSmallContentLength = 1024;

        [Test]
        [CancelAfter(120000)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(32)]
        public async Task StalledBodyDoesNotPreAllocateFromContentLengthAsync(int concurrency)
        {
            int port = m_client!.BaseAddress!.Port;

            // Big: announce Content-Length = MaxMessageSize, send no body.
            BodyAdmissionMeasurement big = await MeasureStalledHeadsAsync(
                port, "/read", concurrency, kMaxMessageSize).ConfigureAwait(false);

            // Control: identical number of stalled TLS connections, tiny Content-Length head.
            BodyAdmissionMeasurement small = await MeasureStalledHeadsAsync(
                port, "/read", concurrency, kSmallContentLength).ConfigureAwait(false);

            long heldBig = big.HeapAfterHeads - big.HeapBaseline;
            long heldSmall = small.HeapAfterHeads - small.HeapBaseline;
            long allocBig = big.AllocatedDuringHeads;
            long allocSmall = small.AllocatedDuringHeads;

            double perRequestHeap = (heldBig - heldSmall) / (double)concurrency;
            double perRequestAlloc = (allocBig - allocSmall) / (double)concurrency;

            TestContext.Out.WriteLine(
                $"K={concurrency} MaxMessageSize={kMaxMessageSize} HTTP/1.1 (raw TLS)");
            TestContext.Out.WriteLine(
                $"  BIG  (CL={kMaxMessageSize}): heapHeldAfterHeads={heldBig} allocDuringHeads={allocBig} " +
                $"heapBaseline={big.HeapBaseline} heapAfterHeads={big.HeapAfterHeads} " +
                $"heapStillHeld(+{big.HoldMsObserved}ms)={big.HeapStillHeld} heapAfterClose={big.HeapAfterRelease}");
            TestContext.Out.WriteLine(
                $"  CTRL (CL={kSmallContentLength}): heapHeldAfterHeads={heldSmall} allocDuringHeads={allocSmall} " +
                $"heapBaseline={small.HeapBaseline} heapAfterHeads={small.HeapAfterHeads}");
            TestContext.Out.WriteLine(
                $"  => perRequestHeapDelta={perRequestHeap:N0} B  perRequestAllocDelta={perRequestAlloc:N0} B");

            // Memory allocated before any body byte arrives is
            // not proportional to the announced Content-Length (< 1 MB per request).
            Assert.That(
                perRequestHeap,
                Is.LessThan(1024 * 1024),
                $"Server held ~{perRequestHeap:N0} B of managed heap per stalled request before any " +
                $"body byte arrived, scaling with the announced Content-Length ({kMaxMessageSize} B). " +
                "The WebApi body read must not allocate from the announced Content-Length.");
        }

        /// <summary>
        /// Anonymous discovery routes accept bodies only up to the discovery message
        /// size, as a discovery-only SecureChannel does: a larger announced body is
        /// refused at once instead of being awaited and buffered.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        [TestCase("/getendpoints")]
        [TestCase("/findservers")]
        public async Task DiscoveryRouteRefusesBodyAboveDiscoveryMessageSizeAsync(string path)
        {
            int port = m_client!.BaseAddress!.Port;
            int contentLength = TcpMessageLimits.DefaultDiscoveryMaxMessageSize + 1;
            Assert.That(contentLength, Is.LessThan(kMaxMessageSize));

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
#pragma warning disable CA5359 // loopback test server uses a self-signed certificate
            using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: static (_, _, _, _) => true);
#pragma warning restore CA5359
            await ssl.AuthenticateAsClientAsync("localhost").ConfigureAwait(false);
            byte[] head = Encoding.ASCII.GetBytes(
                $"POST {path} HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                "\r\n");
            await ssl.WriteAsync(head).ConfigureAwait(false);
            await ssl.FlushAsync().ConfigureAwait(false);

            // No body is sent: a response proves the route did not wait for it.
            byte[] response = new byte[64];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            int read = await ssl.ReadAsync(response, timeout.Token).ConfigureAwait(false);
            string status = Encoding.ASCII.GetString(response, 0, read);

            Assert.That(status, Does.StartWith("HTTP/1.1 400"));
        }

        /// <summary>
        /// A body that does not arrive within the message assembly lifetime is answered
        /// with 408 on the open connection: the deadline ends the pending body read
        /// without aborting the connection the answer is written to.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        [TestCase("/", "application/octet-stream")]
        [TestCase("/", "application/opcua+uajson")]
        [TestCase("/read", "application/json")]
        public async Task LateBodyIsAnsweredWithRequestTimeoutAsync(string path, string contentType)
        {
            const int kAssemblyLifetimeMs = 1000;
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(m_telemetry!);
            var restServer = new WebApiServer(messageContext, "real-rest-api-late-body");
            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(new WebApiHttpsStartupContributor(restServer));
            InMemoryCertificateRegistry certificateRegistry = m_certificateRegistry!;
            (HttpsTransportListener listener, int port) = await OpenListenerOnFreePortAsync(
                () => (HttpsTransportListener)factory.Create(m_telemetry!),
                p =>
                {
                    TransportListenerSettings settings = CreateListenerSettings(certificateRegistry, p);
                    settings.Configuration!.ChannelLifetime = kAssemblyLifetimeMs;
                    return settings;
                },
                m_callback!).ConfigureAwait(false);
            try
            {
                await WaitForListenerReadyAsync(port).ConfigureAwait(false);

                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
#pragma warning disable CA5359 // loopback test server uses a self-signed certificate
                using var ssl = new SslStream(
                    tcp.GetStream(),
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: static (_, _, _, _) => true);
#pragma warning restore CA5359
                await ssl.AuthenticateAsClientAsync("localhost").ConfigureAwait(false);
                byte[] head = Encoding.ASCII.GetBytes(
                    $"POST {path} HTTP/1.1\r\n" +
                    "Host: localhost\r\n" +
                    $"Content-Type: {contentType}\r\n" +
                    $"Content-Length: {kSmallContentLength}\r\n" +
                    "\r\n");
                await ssl.WriteAsync(head).ConfigureAwait(false);
                await ssl.FlushAsync().ConfigureAwait(false);

                // Only the head is sent. Kestrel's own minimum body data rate allows
                // 5 s before it ends a stalled body, so an answer within 4 s comes
                // from the assembly deadline.
                byte[] response = new byte[64];
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                int read = await ssl.ReadAsync(response, timeout.Token).ConfigureAwait(false);
                string status = Encoding.ASCII.GetString(response, 0, read);

                Assert.That(status, Does.StartWith("HTTP/1.1 408"));
            }
            finally
            {
                await listener.CloseAsync().ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed record BodyAdmissionMeasurement(
            long HeapBaseline,
            long HeapAfterHeads,
            long AllocatedDuringHeads,
            long HeapStillHeld,
            long HeapAfterRelease,
            long HoldMsObserved);

        /// <summary>
        /// Opens <paramref name="concurrency"/> raw TLS connections, writes an
        /// HTTP/1.1 request head that announces <paramref name="contentLength"/>
        /// but sends no body, waits for the server to process the heads, and
        /// samples the managed heap and allocation counters. Then holds a little
        /// longer (to show the memory stays held), closes the connections, and
        /// samples the heap again.
        /// </summary>
        private async Task<BodyAdmissionMeasurement> MeasureStalledHeadsAsync(
            int port,
            string path,
            int concurrency,
            int contentLength)
        {
            var sockets = new TcpClient[concurrency];
            var streams = new SslStream[concurrency];
            try
            {
                // Establish all TLS connections first (TLS handshake allocations happen now,
                // not during the measurement window).
                for (int i = 0; i < concurrency; i++)
                {
                    var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                    sockets[i] = tcp;
#pragma warning disable CA5359 // loopback test server uses a self-signed certificate
                    var ssl = new SslStream(
                        tcp.GetStream(),
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: static (_, _, _, _) => true);
#pragma warning restore CA5359
                    await ssl.AuthenticateAsClientAsync("localhost").ConfigureAwait(false);
                    streams[i] = ssl;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long heapBaseline = GC.GetTotalMemory(forceFullCollection: false);
                long allocBaseline = GC.GetTotalAllocatedBytes(precise: true);

                // Write only the request head: announces Content-Length, sends no body.
                byte[] head = Encoding.ASCII.GetBytes(
                    $"POST {path} HTTP/1.1\r\n" +
                    "Host: localhost\r\n" +
                    "Content-Type: application/opcua+uabinary\r\n" +
                    $"Content-Length: {contentLength}\r\n" +
                    "\r\n");
                for (int i = 0; i < concurrency; i++)
                {
                    await streams[i].WriteAsync(head).ConfigureAwait(false);
                    await streams[i].FlushAsync().ConfigureAwait(false);
                }

                // Let the server route each head and run ReadAllBoundedAsync's
                // new byte[Content-Length] before it blocks on the absent body.
                await Task.Delay(1000).ConfigureAwait(false);

                long heapAfterHeads = GC.GetTotalMemory(forceFullCollection: false);
                long allocatedDuringHeads = GC.GetTotalAllocatedBytes(precise: true) - allocBaseline;

                // Confirm the memory is still held a moment later (within Kestrel's
                // ~5 s minimum-body-data-rate grace for a header-only request).
                await Task.Delay(1500).ConfigureAwait(false);
                long heapStillHeld = GC.GetTotalMemory(forceFullCollection: false);
                long holdMs = 1500;

                // Release: close the connections; the server aborts the stalled reads and frees.
                for (int i = 0; i < concurrency; i++)
                {
                    try
                    {
                        streams[i].Close();
                        sockets[i].Close();
                    }
                    catch
                    {
                        // ignore
                    }
                }

                await Task.Delay(500).ConfigureAwait(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long heapAfterRelease = GC.GetTotalMemory(forceFullCollection: true);

                return new BodyAdmissionMeasurement(
                    heapBaseline, heapAfterHeads, allocatedDuringHeads, heapStillHeld, heapAfterRelease, holdMs);
            }
            finally
            {
                for (int i = 0; i < concurrency; i++)
                {
                    streams[i]?.Dispose();
                    sockets[i]?.Dispose();
                }
            }
        }
    }
}
