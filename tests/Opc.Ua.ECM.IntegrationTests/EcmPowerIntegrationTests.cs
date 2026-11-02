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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.ECM.Client;
using Opc.Ua.ECM.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.ECM.IntegrationTests
{
    /// <summary>
    /// Exercises generated ECM clients through a loopback UA-TCP endpoint with simulated device state only.
    /// </summary>
    [TestFixture]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [NonParallelizable]
    [Category("ECM")]
    [Category("Integration")]
    public sealed class EcmPowerIntegrationTests
    {
        /// <summary>
        /// Starts a real ECM owner and client using isolated temporary PKI stores.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(Path.GetTempPath(), "opcua-ecm-" + Guid.NewGuid().ToString("N"));
            m_clock = new DeviceClock();
            m_power = new SimulatedEcmPowerProvider(
                [new EcmEnergySavingMode(1, "Pause", 7000, 2000, 2000, 60000, 3000, 1, 0.1f, 0.2f)],
                ByteString.From(new byte[] { 2, 0, 0, 0, 0, 1 }.AsSpan()),
                timeProvider: m_clock);
            m_server = new ServerFixture<StandardServer>(context =>
            {
                var server = new StandardServer(context);
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.SetupGet(value => value.NamespacesUris)
                    .Returns(new EcmNodeManagerFactory().NamespacesUris);
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .Returns((IServerInternal instance, ApplicationConfiguration config, CancellationToken _) =>
                    {
                        m_manager = new EcmNodeManager(instance, config);
                        return new ValueTask<IAsyncNodeManager>(m_manager);
                    });
                server.AddNodeManager(factory.Object);
                return server;
            })
            {
                SecurityNone = true,
                AutoAccept = true
            };
            await m_server.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_binding = await m_manager.CreatePowerAsync(
                new QualifiedName("Power", m_manager.InstanceNamespaceIndex), m_power, wolProvider: m_power,
                authorize: static (_, _) => new ValueTask<bool>(true)).ConfigureAwait(false);
            m_wol = await m_manager.CreateWolAsync(
                new QualifiedName("StandaloneSleep", m_manager.InstanceNamespaceIndex), m_power,
                authorize: static (_, _) => new ValueTask<bool>(true)).ConfigureAwait(false);
            m_clientFixture = new ClientFixture(telemetry);
            m_clientFixture.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await m_clientFixture.GetEndpointAsync(
                new Uri("opc.tcp://localhost:" + m_server.Port.ToString(CultureInfo.InvariantCulture)),
                SecurityPolicies.None).ConfigureAwait(false);
            m_session = await m_clientFixture.ConnectAsync(endpoint).ConfigureAwait(false);
            m_client = new EcmClient(m_session, m_binding.Standby.NodeId, telemetry, m_binding.PowerOff!.NodeId);
        }

        /// <summary>
        /// Releases subscriptions, client/server resources and the specific fixture's temporary stores.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    try
                    {
                        await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        await m_session.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                try
                {
                    if (m_clientFixture != null)
                    {
                        await m_clientFixture.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (m_server != null)
                        {
                            await m_server.StopAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(m_pkiRoot))
                        {
                            Directory.Delete(m_pkiRoot, recursive: true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// PauseTime writes and EndPause produce ordered monitored transitions through the generated accessor.
        /// </summary>
        [Test]
        public async Task PauseWriteAndEndPausePublishMovingAndArrivedStates()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.That(m_session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!);
            int stage = 0;
            var observed = new List<byte>();
            await foreach (var value in m_client.Standby.StandbyManagementStatusVariable()
                .ObserveAsync(streaming, cancellationToken: timeout.Token).ConfigureAwait(false))
            {
                Assert.That(value.DataValue.StatusCode, Is.EqualTo(new StatusCode(StatusCodes.Good)));
                Assert.That(value.HasValue, Is.True);
                byte state = value.Value;
                if ((stage == 0 && state == (byte)EcmStandbyStatus.ReadyToOperate) ||
                    (stage == 1 && state == (byte)EcmStandbyStatus.MovingToEnergySavingMode) ||
                    (stage == 2 && state == (byte)EcmStandbyStatus.EnergySavingMode) ||
                    (stage == 3 && state == (byte)EcmStandbyStatus.MovingToReadyToOperate) ||
                    (stage == 4 && state == (byte)EcmStandbyStatus.ReadyToOperate))
                {
                    observed.Add(state);
                    switch (stage++)
                    {
                        case 0:
                            StatusCode write = await m_client.Standby.PauseTimeVariable()
                                .WriteWithStatusAsync(10000, timeout.Token).ConfigureAwait(false);
                            Assert.That(write, Is.EqualTo(new StatusCode(StatusCodes.Good)));
                            break;
                        case 1:
                            m_clock.Advance(2000);
                            await m_binding.RefreshAsync(timeout.Token).ConfigureAwait(false);
                            break;
                        case 2:
                            EcmControlResult end = await m_client.EndPauseAsync(timeout.Token).ConfigureAwait(false);
                            Assert.That(end.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
                            Assert.That(end.ReturnCode, Is.Zero);
                            Assert.That(end.CurrentTimeToOperate, Is.EqualTo(5000));
                            break;
                        case 3:
                            m_clock.Advance(5000);
                            await m_binding.RefreshAsync(timeout.Token).ConfigureAwait(false);
                            break;
                        default:
                            break;
                    }
                    if (stage == 5)
                    {
                        break;
                    }
                }
            }
            Assert.That(observed, Is.EqualTo(new byte[] { 2, 3, 4, 5, 2 }));
            var pause = await m_client.Standby.PauseTimeVariable().ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(pause.HasValue, Is.True);
            Assert.That(pause.Value, Is.Zero);
        }

        /// <summary>
        /// Real Call responses retain device rejection and default-role denial without success-shaped outputs.
        /// </summary>
        [Test]
        public async Task DeviceRejectionAndRoleDenialRemainDistinctAcrossTheWire()
        {
            EcmControlResult rejected = await m_client.StartPauseAsync(1).ConfigureAwait(false);
            Assert.That(rejected.Status, Is.EqualTo(new StatusCode(StatusCodes.Uncertain)));
            Assert.That(rejected.ReturnCode, Is.EqualTo(0x50));
            Assert.That(rejected.ModeId, Is.Zero);
            Assert.That(rejected.CurrentTimeToDestination, Is.Zero);
            Assert.That(rejected.RegularTimeToOperate, Is.Zero);
            Assert.That(rejected.TimeMinLengthOfStay, Is.Zero);

            EcmWolBinding denied = await m_manager.CreateWolAsync(
                new QualifiedName("RoleProtectedSleep", m_manager.InstanceNamespaceIndex), m_power)
                .ConfigureAwait(false);
            var client = new EcmWolClient(m_session, denied.PowerOff.NodeId, NUnitTelemetryContext.Create());
            EcmControlResult result = await client.SwitchOffWolAsync().ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(new StatusCode(StatusCodes.BadUserAccessDenied)));
            Assert.That(result.ReturnCode.HasValue, Is.False);
            Assert.That((await m_power.ReadAsync().ConfigureAwait(false)).Status,
                Is.EqualTo(EcmStandbyStatus.ReadyToOperate));
        }

        /// <summary>
        /// WOL-only discovery and explicit sleep preserve the MAC and reserved destination on a live session.
        /// </summary>
        [Test]
        public async Task StandaloneWolDiscoveryAndSleepUseGeneratedProxy()
        {
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, m_session.NamespaceUris);
            var found = new List<EcmWolClient>();
            await foreach (EcmWolClient client in EcmWolClient.DiscoverAsync(
                m_session, deviceSet, NUnitTelemetryContext.Create()).ConfigureAwait(false))
            {
                found.Add(client);
            }
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].PowerOff.ObjectId, Is.EqualTo(m_wol.PowerOff.NodeId));
            var mac = await found[0].PowerOff.WOLMagicPacketVariable().ReadAsync().ConfigureAwait(false);
            Assert.That(mac.HasValue, Is.True);
            Assert.That(mac.Value.ToArray(), Is.EqualTo(new byte[] { 2, 0, 0, 0, 0, 1 }));
            EcmControlResult sleep = await found[0].SwitchOffWolAsync().ConfigureAwait(false);
            Assert.That(sleep.Status, Is.EqualTo(new StatusCode(StatusCodes.Good)));
            Assert.That(sleep.ReturnCode, Is.Zero);
            Assert.That(sleep.ModeId, Is.EqualTo(0xFE));
            Assert.That(sleep.CurrentTimeToDestination, Is.EqualTo(1000));
            Assert.That(sleep.RegularTimeToOperate, Is.EqualTo(2000));
            Assert.That(sleep.TimeMinLengthOfStay, Is.EqualTo(1000));
            m_clock.Advance(100000);
            await m_binding.RefreshAsync().ConfigureAwait(false);
            var sleeping = await m_client.Standby.StandbyManagementStatusVariable().ReadAsync().ConfigureAwait(false);
            Assert.That(sleeping.HasValue, Is.True);
            Assert.That(sleeping.Value, Is.EqualTo((byte)EcmStandbyStatus.SleepWol));
            Assert.That(m_session.Connected, Is.True);
        }

        /// <summary>The fixture's dedicated PKI directory.</summary>
        private string m_pkiRoot = string.Empty;

        /// <summary>The loopback server fixture.</summary>
        private ServerFixture<StandardServer> m_server = null!;

        /// <summary>The real ECM owner.</summary>
        private EcmNodeManager m_manager = null!;

        /// <summary>The simulated device.</summary>
        private SimulatedEcmPowerProvider m_power = null!;

        /// <summary>The controlled device clock.</summary>
        private DeviceClock m_clock = null!;

        /// <summary>The combined standby and WOL graph.</summary>
        private EcmPowerBinding m_binding = null!;

        /// <summary>The independent WOL graph.</summary>
        private EcmWolBinding m_wol = null!;

        /// <summary>The client configuration and certificate-store owner.</summary>
        private ClientFixture m_clientFixture = null!;

        /// <summary>The live session owned by this fixture.</summary>
        private ISession m_session = null!;

        /// <summary>The typed client borrowing the fixture's session.</summary>
        private EcmClient m_client = null!;

        /// <summary>
        /// Advances provider time without timing-dependent physical transitions.
        /// </summary>
        private sealed class DeviceClock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddMilliseconds(Interlocked.Read(ref m_milliseconds));
            }

            /// <summary>
            /// Advances the simulated clock by a nonnegative test interval.
            /// </summary>
            public void Advance(long milliseconds)
            {
                Interlocked.Add(ref m_milliseconds, milliseconds);
            }

            /// <summary>The elapsed simulated milliseconds.</summary>
            private long m_milliseconds;
        }
    }
}
