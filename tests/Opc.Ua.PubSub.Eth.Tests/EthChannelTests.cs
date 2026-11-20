// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Tests for the frame channel backends: the in-memory loopback bus
    /// and the default platform-dispatch factory.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class EthChannelTests
    {
        [Test]
        public async Task InMemoryBusDeliversBetweenChannels()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using IEthernetFrameChannel sender = factory.Create(
                EthTestHelpers.LoopbackParameters(), NUnitTelemetryContext.Create(), TimeProvider.System);
            await using IEthernetFrameChannel receiver = factory.Create(
                EthTestHelpers.LoopbackParameters(), NUnitTelemetryContext.Create(), TimeProvider.System);

            await sender.OpenAsync().ConfigureAwait(false);
            await receiver.OpenAsync().ConfigureAwait(false);

            byte[] frame = EthTestHelpers.MakePayload(40);
            await sender.SendFrameAsync(frame).ConfigureAwait(false);

            byte[]? received = await ReceiveOneAsync(receiver, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(received, Is.Not.Null);
            Assert.That(received, Is.EqualTo(frame));
        }

        [Test]
        public async Task InMemorySenderDoesNotReceiveOwnFrame()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using IEthernetFrameChannel sender = factory.Create(
                EthTestHelpers.LoopbackParameters(), NUnitTelemetryContext.Create(), TimeProvider.System);

            await sender.OpenAsync().ConfigureAwait(false);
            await sender.SendFrameAsync(EthTestHelpers.MakePayload(40)).ConfigureAwait(false);

            byte[]? received = await ReceiveOneAsync(sender, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

            Assert.That(received, Is.Null);
        }

        [Test]
        public async Task InMemorySendBeforeOpenThrows()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using IEthernetFrameChannel channel = factory.Create(
                EthTestHelpers.LoopbackParameters(), NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.That(
                async () => await channel.SendFrameAsync(EthTestHelpers.MakePayload(10)).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task InMemoryInterfaceAddressIsDeterministic()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using IEthernetFrameChannel a = factory.Create(
                EthTestHelpers.LoopbackParameters("nicA"), NUnitTelemetryContext.Create(), TimeProvider.System);
            await using IEthernetFrameChannel b = factory.Create(
                EthTestHelpers.LoopbackParameters("nicA"), NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.That(a.InterfaceAddress, Is.EqualTo(b.InterfaceAddress));
            Assert.That(a.InterfaceAddress.GetAddressBytes(), Has.Length.EqualTo(6));
        }

        [Test]
        public void DefaultFactoryNullParametersThrows()
        {
            var factory = new DefaultEthernetFrameChannelFactory();
            Assert.That(
                () => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DefaultFactoryThrowsOnUnsupportedConfiguration()
        {
            var factory = new DefaultEthernetFrameChannelFactory();
            EthChannelParameters parameters = EthTestHelpers.LoopbackParameters();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.That(
                    () => factory.Create(parameters, NUnitTelemetryContext.Create(), TimeProvider.System),
                    Throws.TypeOf<PlatformNotSupportedException>());
            }
            else
            {
                // The Linux / macOS backends require a resolved network interface.
                Assert.That(
                    () => factory.Create(parameters, NUnitTelemetryContext.Create(), TimeProvider.System),
                    Throws.ArgumentException);
            }
        }

        private static async Task<byte[]?> ReceiveOneAsync(
            IEthernetFrameChannel channel,
            TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (ReadOnlyMemory<byte> frame in channel.ReceiveFramesAsync(cts.Token)
                    .ConfigureAwait(false))
                {
                    return frame.ToArray();
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }
    }
}
