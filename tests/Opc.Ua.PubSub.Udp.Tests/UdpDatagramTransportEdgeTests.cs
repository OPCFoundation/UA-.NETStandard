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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Edge-case coverage for <see cref="UdpDatagramTransport"/> guard
    /// rails: argument validation, lifecycle errors, payload-size
    /// enforcement and dispose semantics per Part 14 §7.3.2.
    /// </summary>
    [TestFixture]
    [TestSpec("7.3.2", Summary = "UDP datagram transport guard rails")]
    [CancelAfter(15000)]
    public sealed class UdpDatagramTransportEdgeTests
    {
        [Test]
        public void ConstructorRejectsNullConnection()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    connection: null!,
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System,
                    UdpIntegrationTestHelpers.LoopbackOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsInvalidEndpoint()
        {
            // Default-constructed UdpEndpoint has a null address ⇒ IsValid is false.
            var endpoint = default(UdpEndpoint);

            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System,
                    UdpIntegrationTestHelpers.LoopbackOptions()),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    telemetry: null!,
                    TimeProvider.System,
                    UdpIntegrationTestHelpers.LoopbackOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullTimeProvider()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            Assert.That(
                () => new UdpDatagramTransport(
                    UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                    endpoint,
                    PubSubTransportDirection.Send,
                    networkInterface: null,
                    NUnitTelemetryContext.Create(),
                    timeProvider: null!,
                    UdpIntegrationTestHelpers.LoopbackOptions()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullOptions()
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
                    options: null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task SendBeforeOpenThrowsInvalidOperationException()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task SendAfterDisposeThrowsObjectDisposedException()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            await transport.DisposeAsync().ConfigureAwait(false);

            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task OpenAfterDisposeThrowsObjectDisposedException()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await transport.OpenAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task SendOversizePayloadThrowsArgumentException()
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
            UdpTransportOptions options = UdpIntegrationTestHelpers.LoopbackOptions();

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
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            byte[] tooLarge = new byte[options.MaxFrameSize + 1];

            Assert.That(
                async () => await transport.SendAsync(tooLarge).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendHonoursAlreadyCancelledToken()
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

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, cancellationToken: cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task ReceiveCancelsCleanly()
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

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                transport,
                TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

            Assert.That(frame, Is.Null);
            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task DoubleOpenIsIdempotent()
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

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.SendReceive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task CloseAfterOpenSetsDisconnected()
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

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.SendReceive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);

            await transport.CloseAsync().ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task DoubleCloseIsIdempotent()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            await transport.CloseAsync().ConfigureAwait(false);
            await transport.CloseAsync().ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeWithoutOpenIsSafe()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            await transport.DisposeAsync().ConfigureAwait(false);
            // Second dispose must not throw.
            await transport.DisposeAsync().ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task EnforceDiscoveryLimit_ZeroCap_DoesNotThrowAsync()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            PubSubConnectionDataType connection = UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840");

            await using var transport = new UdpDatagramTransport(
                connection,
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            Assert.That(() => transport.EnforceDiscoveryLimit(new byte[1024]), Throws.Nothing);
        }

        [Test]
        public async Task EnforceDiscoveryLimit_OverCap_ThrowsServiceResultExceptionAsync()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4840");
            PubSubConnectionDataType connection = UdpIntegrationTestHelpers.NewConnection("opc.udp://127.0.0.1:4840");
            connection.TransportSettings = new ExtensionObject(new DatagramConnectionTransport2DataType
            {
                DiscoveryMaxMessageSize = 8
            });

            await using var transport = new UdpDatagramTransport(
                connection,
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            Assert.That(
                () => transport.EnforceDiscoveryLimit(new byte[9]),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void MapQosCategoryToTos_ReturnsExpectedValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That(UdpDatagramTransport.MapQosCategoryToTos("Reliable"), Is.EqualTo(0x48));
                Assert.That(UdpDatagramTransport.MapQosCategoryToTos("BestEffort"), Is.Zero);
                Assert.That(UdpDatagramTransport.MapQosCategoryToTos("ExpeditedForwarding"), Is.EqualTo(0xB8));
                Assert.That(UdpDatagramTransport.MapQosCategoryToTos("Unknown"), Is.Zero);
            });
        }

        [Test]
        public async Task StateChanged_HandlerThrows_DoesNotEscapeLifecycleAsync()
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

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            transport.StateChanged += (_, _) => throw new InvalidOperationException("boom");

            try
            {
                Assert.That(async () => await transport.OpenAsync().ConfigureAwait(false), Throws.Nothing);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open failed: {ex.Message}");
                return;
            }

            Assert.That(async () => await transport.CloseAsync().ConfigureAwait(false), Throws.Nothing);
        }
    }
}
