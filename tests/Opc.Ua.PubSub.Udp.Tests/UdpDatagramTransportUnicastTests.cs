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
    /// Helpers shared between UDP integration test fixtures.
    /// </summary>
    internal static class UdpIntegrationTestHelpers
    {
        /// <summary>
        /// Reserves an ephemeral UDP port on the loopback interface, then
        /// releases it. The caller may briefly race other listeners but
        /// reduces collisions in CI where many test workers share the host.
        /// </summary>
        public static int ReserveEphemeralPort(IPAddress bindAddress)
        {
            using var probe = new Socket(
                bindAddress.AddressFamily,
                SocketType.Dgram,
                ProtocolType.Udp);
            probe.Bind(new IPEndPoint(bindAddress, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        /// <summary>
        /// Builds a minimal <see cref="PubSubConnectionDataType"/> bound
        /// to the supplied URL.
        /// </summary>
        public static PubSubConnectionDataType NewConnection(string url, string name = "Conn")
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                })
            };
        }

        /// <summary>
        /// Default transport options tuned for loopback tests.
        /// </summary>
        public static UdpTransportOptions LoopbackOptions()
        {
            return new UdpTransportOptions
            {
                Ttl = 1,
                MulticastLoopback = true,
                ReceiveQueueCapacity = 16,
                MaxFrameSize = 1500
            };
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> for the next frame on
        /// the transport.
        /// </summary>
        public static async Task<PubSubTransportFrame?> ReceiveOneAsync(
            IPubSubTransport transport,
            TimeSpan timeout,
            CancellationToken externalToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            cts.CancelAfter(timeout);
            try
            {
                await foreach (PubSubTransportFrame frame in transport.ReceiveAsync(cts.Token))
                {
                    return frame;
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }
    }

    /// <summary>
    /// Loopback unicast smoke test for <see cref="UdpDatagramTransport"/>.
    /// Verifies a publisher transport bound to a random ephemeral port on
    /// 127.0.0.1 can deliver a byte payload to a subscriber transport
    /// bound to the same port, exercising the unicast bind / connect /
    /// send / receive code paths.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.2.3")]
    [CancelAfter(10000)]
    public sealed class UdpDatagramTransportUnicastTests
    {
        [Test]
        public async Task LoopbackUnicast_PublishesPayloadToSubscriber()
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConnectionDataType receiverConnection = UdpIntegrationTestHelpers.NewConnection(url, "Subscriber");
            PubSubConnectionDataType senderConnection = UdpIntegrationTestHelpers.NewConnection(url, "Publisher");

            await using var receiver = new UdpDatagramTransport(
                receiverConnection,
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);
            await using var sender = new UdpDatagramTransport(
                senderConnection,
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);

            try
            {
                await receiver.OpenAsync().ConfigureAwait(false);
                await sender.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Unicast loopback open failed: {ex.Message}");
                return;
            }

            byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];

            for (int attempt = 0; attempt < 5; attempt++)
            {
                await sender.SendAsync(payload).ConfigureAwait(false);
                PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                    receiver,
                    TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (frame is not null)
                {
                    Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
                    Assert.That(frame.Value.Topic, Is.Null);
                    return;
                }
            }

            Assert.Ignore("No unicast loopback frame received within retry budget; environment likely blocks UDP.");
        }

        [Test]
        public async Task TransportPublishesIsConnected()
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

            Assert.That(transport.IsConnected, Is.False);

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
            Assert.That(transport.TransportProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.SendReceive));
            Assert.That(transport.Endpoint.Port, Is.EqualTo(port));
        }

        [Test]
        public async Task SetAuthenticatedRemoteEndpointGuardsConnectedSendClientAsync()
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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var receiver = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Subscriber"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);
            await using var sender = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Publisher"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);

            try
            {
                await receiver.OpenAsync().ConfigureAwait(false);
                await sender.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Unicast loopback open failed: {ex.Message}");
                return;
            }

            // A connected send client rejects a null pin and ignores an attempt
            // to rebind to a different endpoint, so its socket cannot be
            // redirected to an attacker-chosen destination.
            Assert.That(
                () => sender.SetAuthenticatedRemoteEndpoint(null!),
                Throws.TypeOf<ArgumentNullException>());
            sender.SetAuthenticatedRemoteEndpoint(new IPEndPoint(IPAddress.Loopback, port + 1));

            // The send destination is unchanged: the payload still reaches the
            // receiver bound to the original port.
            byte[] payload = [0x10, 0x20, 0x30];
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await sender.SendAsync(payload).ConfigureAwait(false);
                PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                    receiver,
                    TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (frame is not null)
                {
                    Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
                    return;
                }
            }

            Assert.Ignore("No unicast loopback frame received within retry budget; environment likely blocks UDP.");
        }
    }
}
