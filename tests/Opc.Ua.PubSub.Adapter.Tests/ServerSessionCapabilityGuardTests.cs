/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for the V2 subscription-manager capability guards in
    /// <see cref="ServerSession"/> when the bound <see cref="ISession"/> does
    /// not expose a manager (e.g. a classic-engine session). Uses the internal
    /// session-injecting constructor so the guards are exercised without a
    /// real server.
    /// </summary>
    [TestFixture]
    public sealed class ServerSessionCapabilityGuardTests
    {
        private static ServerSession CreateSessionWithoutManager()
        {
            var mockSession = new Mock<ISession>();
            ISubscriptionManager? manager = null;
            mockSession
                .Setup(s => s.TryGetSubscriptionManager(out manager))
                .Returns(false);
            mockSession.SetupGet(s => s.Connected).Returns(true);

            var options = new ServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            };
            return new ServerSession(
                options, AdapterTestHelpers.Telemetry(), mockSession.Object);
        }

        [Test]
        public void CreateDataChangeSubscriptionThrowsWhenNoV2Manager()
        {
            ServerSession session = CreateSessionWithoutManager();

            Assert.That(
                async () => await session.CreateDataChangeSubscriptionAsync(1000).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void StartModelChangeMonitoringNoOpsWhenNoV2Manager()
        {
            ServerSession session = CreateSessionWithoutManager();

            // Model-change monitoring is optional/best-effort: a session without a
            // V2 manager must skip it silently rather than fault.
            Assert.That(
                async () => await session.StartModelChangeMonitoringAsync().ConfigureAwait(false),
                Throws.Nothing);
        }
    }
}
