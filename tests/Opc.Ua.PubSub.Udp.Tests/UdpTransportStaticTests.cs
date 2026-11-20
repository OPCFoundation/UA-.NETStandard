// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Unit tests for the internal static helper
    /// <see cref="UdpDatagramTransport.MapQosCategoryToTos"/> and for
    /// <see cref="UdpDatagramTransport.CloseAsync"/> called on a transport
    /// that has never been opened, verifying the idempotent close guard per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2</see>.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    [CancelAfter(10000)]
    public sealed class UdpTransportStaticTests
    {
        [TestCase("Reliable", 0x48)]
        [TestCase("BestEffort", 0x00)]
        [TestCase("ExpeditedForwarding", 0xB8)]
        public void MapQosCategoryToTos_KnownCategory_ReturnsExpectedTosValue(
            string category, int expectedTos)
        {
            int tos = UdpDatagramTransport.MapQosCategoryToTos(category);
            Assert.That(tos, Is.EqualTo(expectedTos));
        }

        [TestCase("Unknown")]
        [TestCase("")]
        [TestCase("reliable")]   // case-sensitive — not matched
        [TestCase("best_effort")]
        public void MapQosCategoryToTos_UnrecognisedCategory_ReturnsZero(string category)
        {
            int tos = UdpDatagramTransport.MapQosCategoryToTos(category);
            Assert.That(tos, Is.Zero);
        }

        [Test]
        public async Task CloseAsync_OnUnopenedSendTransport_CompletesWithoutException(
            CancellationToken cancellationToken)
        {
            await using UdpDatagramTransport transport = NewSendTransport("opc.udp://127.0.0.1:4841");
            Assert.That(transport.IsConnected, Is.False);

            await transport.CloseAsync(cancellationToken).ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task CloseAsync_CalledTwiceOnUnopenedTransport_IsIdempotent(
            CancellationToken cancellationToken)
        {
            await using UdpDatagramTransport transport = NewSendTransport("opc.udp://127.0.0.1:4842");

            await transport.CloseAsync(cancellationToken).ConfigureAwait(false);
            await transport.CloseAsync(cancellationToken).ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeAsync_Twice_IsIdempotent(CancellationToken cancellationToken)
        {
            UdpDatagramTransport transport = NewSendTransport("opc.udp://127.0.0.1:4843");
            await transport.DisposeAsync().ConfigureAwait(false);
            // Second DisposeAsync must not throw.
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        [Category("Integration")]
        [CancelAfter(8000)]
        public async Task StateChanged_FiredOnOpenAndClose_WhenUnicastTransportIsUsed(
            CancellationToken cancellationToken)
        {
            int port = UdpIntegrationTestHelpers.ReserveEphemeralPort(
                System.Net.IPAddress.Loopback);
            string url = $"opc.udp://127.0.0.1:{port}";

            await using UdpDatagramTransport transport = NewReceiveTransport(url);

            int stateChanges = 0;
            transport.StateChanged += (_, _) => Interlocked.Increment(ref stateChanges);

            await transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            await transport.CloseAsync(cancellationToken).ConfigureAwait(false);

            Assert.That(stateChanges, Is.GreaterThanOrEqualTo(2),
                "Expected at least one StateChanged event for open and one for close.");
        }

        private static UdpDatagramTransport NewSendTransport(string url)
        {
            return new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                UdpEndpointParser.Parse(url),
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
        }

        private static UdpDatagramTransport NewReceiveTransport(string url)
        {
            return new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url),
                UdpEndpointParser.Parse(url),
                PubSubTransportDirection.Receive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
        }
    }
}
