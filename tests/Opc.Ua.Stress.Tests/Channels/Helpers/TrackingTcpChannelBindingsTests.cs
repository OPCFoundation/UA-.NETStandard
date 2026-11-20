// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// Verifies that transport lifetime observation does not hide repeated disposal.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Parallelizable]
    public sealed class TrackingTcpChannelBindingsTests
    {
        /// <summary>
        /// Verifies lifecycle instrumentation records repeated physical disposal instead of hiding duplicates.
        /// </summary>
        [Test]
        public void CountsEveryDisposeCallIncludingDuplicates()
        {
            var bindings = new TrackingTcpChannelBindings();
            ITransportChannel channel = bindings.Create(Utils.UriSchemeOpcTcp, NUnitTelemetryContext.Create())
                ?? throw new AssertionException("The TCP binding must create a transport.");
            try
            {
                Assert.That(channel, Is.InstanceOf<TcpTransportChannel>());
                channel.Dispose();
            }
            finally
            {
                channel.Dispose();
            }

            Assert.That(bindings.Channels.Count, Is.EqualTo(1));
            TrackingTcpChannelBindings.TrackingTcpTransportChannel tracked = bindings.Channels[0];
            Assert.Multiple(() =>
            {
                Assert.That(tracked.OpenCount, Is.Zero);
                Assert.That(tracked.CloseCount, Is.Zero);
                Assert.That(tracked.DisposeCount, Is.EqualTo(2));
                Assert.That(tracked.DisposeCompletedCount, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Verifies unsupported schemes neither return a transport nor add an observation entry.
        /// </summary>
        [Test]
        public void UnsupportedSchemeDoesNotCreateTransport()
        {
            var bindings = new TrackingTcpChannelBindings();
            ITransportChannel? channel = bindings.Create("unsupported", NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(channel, Is.Null);
                Assert.That(bindings.Channels.Count, Is.Zero);
            });
        }
    }
}
