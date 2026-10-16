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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A server whose durable subscription restore fails does not start. It
    /// must release the session and subscription managers it already created
    /// without storing a partial restore, so that starting again restores
    /// every stored subscription.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class SubscriptionRestoreStartupTests
    {
        private static readonly uint[] s_restoredIds = [41u, 42u];

        [Test]
        public async Task FailedRestoreReleasesTheManagersAndARetryRestoresEverySubscriptionAsync()
        {
            var state = new StartupState();
            state.Store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new RestoreSubscriptionResult(
                    true,
                    [
                        SubscriptionRestoreFailureTests.CreateStored(41),
                        SubscriptionRestoreFailureTests.CreateStored(42)
                    ]));
            var fixture = new ServerFixture<RestoreRetryServer>(t => state.CreateServer(t))
            {
                DurableSubscriptionsEnabled = true
            };
            try
            {
                // 41 fails and 42 is restored, so the restore and the startup fail.
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await fixture.StartAsync().ConfigureAwait(false));

                Assert.That(state.SessionManagers, Has.Count.EqualTo(1));
                Assert.That(state.SubscriptionManagers, Has.Count.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(state.SessionManagers[0].ShutDown, Is.True,
                        "the session monitor of the failed startup is stopped");
                    Assert.That(state.SessionManagers[0].Disposed, Is.True,
                        "the session manager of the failed startup is released");
                    Assert.That(state.SubscriptionManagers[0].Disposed, Is.True,
                        "the subscription manager of the failed startup is released");
                });
                state.Store.Verify(
                    value => value.StoreSubscriptionsAsync(
                        It.IsAny<IEnumerable<IStoredSubscription>>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                state.Store.Verify(
                    value => value.OnSubscriptionRestoreCompleteAsync(
                        It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);

                state.FailRestoreOf41 = false;
                RestoreRetryServer server = await fixture.StartAsync().ConfigureAwait(false);

                Assert.That(
                    server.CurrentInstance.SubscriptionManager.GetSubscriptions()
                        .Select(subscription => subscription.Id),
                    Is.EquivalentTo(s_restoredIds));
                Assert.That(state.SessionManagers[1].Disposed, Is.False);
                state.Store.Verify(
                    value => value.OnSubscriptionRestoreCompleteAsync(
                        It.Is<Dictionary<uint, ArrayOf<uint>>>(created =>
                            created.Count == 2 && created.ContainsKey(41u) && created.ContainsKey(42u)),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                state.Servers[0].Dispose();
            }
        }

        private sealed class StartupState
        {
            public Mock<ISubscriptionStore> Store { get; } = new Mock<ISubscriptionStore>();

            public bool FailRestoreOf41 { get; set; } = true;

            public List<RestoreRetryServer> Servers { get; } = [];

            public List<TrackingSessionManager> SessionManagers { get; } = [];

            public List<TrackingSubscriptionManager> SubscriptionManagers { get; } = [];

            public RestoreRetryServer CreateServer(ITelemetryContext telemetry)
            {
                var server = new RestoreRetryServer(telemetry, this);
                Servers.Add(server);
                return server;
            }
        }

        /// <summary>
        /// Server that records the managers it creates and restores from a
        /// shared subscription store.
        /// </summary>
        private sealed class RestoreRetryServer : StandardServer
        {
            private readonly StartupState m_state;

            public RestoreRetryServer(ITelemetryContext telemetry, StartupState state)
                : base(telemetry)
            {
                m_state = state;
            }

            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var manager = new TrackingSessionManager(server, configuration);
                m_state.SessionManagers.Add(manager);
                return manager;
            }

            protected override ISubscriptionManager CreateSubscriptionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var manager = new TrackingSubscriptionManager(server, configuration, m_state);
                m_state.SubscriptionManagers.Add(manager);
                return manager;
            }

            protected override ISubscriptionStore? CreateSubscriptionStore(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return m_state.Store.Object;
            }
        }

        private sealed class TrackingSessionManager : SessionManager
        {
            public TrackingSessionManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public bool Disposed { get; private set; }

            public bool ShutDown { get; private set; }

            public override ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
            {
                ShutDown = true;
                return base.ShutdownAsync(cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class TrackingSubscriptionManager : SubscriptionManager
        {
            private readonly StartupState m_state;

            public TrackingSubscriptionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                StartupState state)
                : base(server, configuration)
            {
                m_state = state;
            }

            public bool Disposed { get; private set; }

            protected override ValueTask<ISubscription> RestoreSubscriptionAsync(
                IStoredSubscription storedSubscription,
                CancellationToken cancellationToken = default)
            {
                if (m_state.FailRestoreOf41 && storedSubscription.Id == 41)
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }

                return base.RestoreSubscriptionAsync(storedSubscription, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
