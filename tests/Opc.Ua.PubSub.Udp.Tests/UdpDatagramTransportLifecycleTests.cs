// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Lifecycle and state-event tests for
    /// <see cref="UdpDatagramTransport"/>: open, close, disposal,
    /// re-open semantics, and the <c>StateChanged</c> event firing.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [CancelAfter(10000)]
    public sealed class UdpDatagramTransportLifecycleTests
    {
        private static UdpDatagramTransport NewSendTransport(int port)
        {
            string url = $"opc.udp://127.0.0.1:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            return new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
        }

        [Test]
        public async Task OpenCloseCycle_Succeeds()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);
            await transport.CloseAsync().ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task OpenAsync_TwiceIsIdempotent()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task DoubleClose_IsIdempotent()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }

            await transport.CloseAsync().ConfigureAwait(false);
            await transport.CloseAsync().ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeAfterClose_IsIdempotent()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            UdpDatagramTransport transport = NewSendTransport(port);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }
            await transport.CloseAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task OpenAsync_AfterDispose_Throws()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            UdpDatagramTransport transport = NewSendTransport(port);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await transport.OpenAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task SendAsync_AfterDispose_Throws()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            UdpDatagramTransport transport = NewSendTransport(port);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await transport.SendAsync(new byte[] { 1 }).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task SendAsync_BeforeOpen_Throws()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);

            Assert.That(
                async () => await transport.SendAsync(new byte[] { 1 }).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task SendAsync_FrameLargerThanMaxFrameSize_Throws()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            string url = $"opc.udp://127.0.0.1:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            var options = new UdpTransportOptions
            {
                MaxFrameSize = 16,
                ReceiveQueueCapacity = 4
            };
            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                options);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }

            Assert.That(
                async () => await transport.SendAsync(new byte[32]).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task StateChanged_FiresOnOpenAndClose()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            var events = new List<bool>();
            transport.StateChanged += (_, args) => events.Add(args.IsConnected);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }
            await transport.CloseAsync().ConfigureAwait(false);

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0], Is.True);
            Assert.That(events[1], Is.False);
        }

        [Test]
        public async Task ReceiveAsync_WithoutReceiveDirection_YieldsBreak()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket open failed: {ex.Message}");
                return;
            }

            int seen = 0;
            await foreach (PubSubTransportFrame _ in transport.ReceiveAsync())
            {
                seen++;
                break;
            }

            Assert.That(seen, Is.Zero);
        }

        [Test]
        public async Task OpenAsync_WithCancelledToken_Throws()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            await using UdpDatagramTransport transport = NewSendTransport(port);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await transport.OpenAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Constructor_NullConnection_Throws()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    null!,
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System,
                    new UdpTransportOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_NullTelemetry_Throws()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    null!,
                    TimeProvider.System,
                    new UdpTransportOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_NullTimeProvider_Throws()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    null!,
                    new UdpTransportOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_NullOptions_Throws()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System,
                    null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_InvalidEndpoint_Throws()
        {
            var endpoint = new UdpEndpoint(null!, 4840, UdpAddressType.Unicast, null);
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System,
                    new UdpTransportOptions()),
                Throws.TypeOf<ArgumentException>());
        }
    }
}
