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
using System.Linq;
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
    public sealed class EcmPowerIntegrationTests : IAsyncDisposable
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
        public Task TearDownAsync()
        {
            return DisposeAsync().AsTask();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
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
                        try
                        {
                            if (m_manager != null)
                            {
                                await m_manager.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (m_binding != null)
                                {
                                    await m_binding.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                try
                                {
                                    if (m_wol != null)
                                    {
                                        await m_wol.DisposeAsync().ConfigureAwait(false);
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
            Assert.That(m_session.TryGetSubscriptionManager(
                out global::Opc.Ua.Client.Subscriptions.ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!);
            int stage = 0;
            var observed = new List<byte>();
            await foreach (var value in m_client.Standby.StandbyManagementStatusVariable()
                .ObserveAsync(streaming, cancellationToken: timeout.Token).ConfigureAwait(false))
            {
                Assert.That(value.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
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
                            Assert.That(write, Is.EqualTo(StatusCodes.Good));
                            break;
                        case 1:
                            m_clock.Advance(2000);
                            await m_binding.RefreshAsync(timeout.Token).ConfigureAwait(false);
                            break;
                        case 2:
                            EcmControlResult end = await m_client.EndPauseAsync(timeout.Token).ConfigureAwait(false);
                            Assert.That(end.Status, Is.EqualTo(StatusCodes.Good));
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
            Assert.That(rejected.Status, Is.EqualTo(StatusCodes.Uncertain));
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
            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadUserAccessDenied));
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
            var composed = new List<EcmWolClient>();
            await foreach (EcmWolClient client in EcmWolClient.DiscoverAsync(
                m_session, m_binding.Standby.NodeId, NUnitTelemetryContext.Create()).ConfigureAwait(false))
            {
                composed.Add(client);
            }
            Assert.That(composed, Has.Count.EqualTo(1));
            Assert.That(composed[0].PowerOff.ObjectId, Is.EqualTo(m_binding.PowerOff!.NodeId));
            var mac = await found[0].PowerOff.WOLMagicPacketVariable().ReadAsync().ConfigureAwait(false);
            Assert.That(mac.HasValue, Is.True);
            Assert.That(mac.Value.ToArray(), Is.EqualTo(new byte[] { 2, 0, 0, 0, 0, 1 }));
            EcmControlResult sleep = await found[0].SwitchOffWolAsync().ConfigureAwait(false);
            Assert.That(sleep.Status, Is.EqualTo(StatusCodes.Good));
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

        /// <summary>
        /// E0, E1 and D0 values, accuracy metadata and writable ApplicationTag cross a live UA session.
        /// </summary>
        [Test]
        public async Task StandaloneMeasurementsDiscoverReadAndWriteThroughGeneratedProxy()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var acquired = new DateTime(2026, 8, 14, 9, 10, 11, DateTimeKind.Utc);
            AccuracyDomainState accuracyDomain = await m_manager.CreateAccuracyDomainAsync(
                new QualifiedName("IntegrationAccuracy", m_manager.InstanceNamespaceIndex),
                [
                    new EnumValueType
                    {
                        Value = 0,
                        DisplayName = new LocalizedText("Class 0"),
                        Description = new LocalizedText("Integration test accuracy class")
                    }
                ],
                timeout.Token).ConfigureAwait(false);
            var provider = new TestMeasurementProvider(accuracyDomain.NodeId, acquired);
            EcmMeasurementBinding binding = await m_manager.CreateMeasurementAsync(
                new QualifiedName("IntegrationMeter", m_manager.InstanceNamespaceIndex),
                provider,
                authorize: static (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(true);
                },
                cancellationToken: timeout.Token).ConfigureAwait(false);
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, m_session.NamespaceUris);
            EcmMeasurementClient? discovered = null;
            await foreach (EcmMeasurementClient candidate in EcmMeasurementClient.DiscoverAsync(
                m_session, deviceSet, NUnitTelemetryContext.Create(), timeout.Token).ConfigureAwait(false))
            {
                discovered = candidate;
                break;
            }
            Assert.That(discovered, Is.Not.Null);
            Assert.That(discovered!.MeasurementPoint.ObjectId, Is.EqualTo(binding.MeasurementPoint.NodeId));

            var phaseCurrent = await discovered.AcCurrentPe.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(phaseCurrent.HasValue, Is.True);
            Assert.That(phaseCurrent.Value.L1, Is.EqualTo(1.25f));
            Assert.That(phaseCurrent.Value.L2, Is.EqualTo(2.5f));
            Assert.That(phaseCurrent.Value.L3, Is.EqualTo(3.75f));
            Assert.That(phaseCurrent.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(phaseCurrent.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)acquired));

            var activePower = await discovered.AcActivePowerTotal.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(activePower.HasValue, Is.True);
            Assert.That(activePower.Value, Is.EqualTo(15.5f));
            Assert.That(activePower.DataValue.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(activePower.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)acquired));

            var dcCurrent = await discovered.DcCurrent.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(dcCurrent.HasValue, Is.True);
            Assert.That(dcCurrent.Value, Is.EqualTo(24.75f));
            Assert.That(dcCurrent.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dcCurrent.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)acquired));

            Assert.That(await discovered.ApplicationTag.WriteWithStatusAsync("integration/updated", timeout.Token)
                .ConfigureAwait(false), Is.EqualTo(StatusCodes.Good));
            await binding.RefreshAsync(timeout.Token).ConfigureAwait(false);
            var applicationTag = await discovered.ApplicationTag.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(applicationTag.HasValue, Is.True);
            Assert.That(applicationTag.Value, Is.EqualTo("integration/updated"));
            Assert.That(applicationTag.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// E2 energy statistics, StartTime and the grouped reset cross a live UA session through generated proxies.
        /// </summary>
        [Test]
        public async Task E2StatisticsReadAndResetThroughGeneratedProxy()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var started = new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc);
            var restarted = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
            var provider = new StatisticsMeasurementProvider(
                started,
                restarted,
                Reading(BrowseNames.AcActivePowerTotal, Variant.From(15.5f), FloatType(), started),
                Reading(BrowseNames.AcActiveEnergyTotalExportLp, Variant.From(100f), FloatType(), started),
                Reading(BrowseNames.AcActiveEnergyTotalImportLp, Variant.From(200f), FloatType(), started));
            EcmMeasurementClient client = await CreateMeasurementAsync(provider, timeout.Token).ConfigureAwait(false);

            var export = await client.AcActiveEnergyTotalExportLp.ReadAsync(timeout.Token).ConfigureAwait(false);
            var import = await client.AcActiveEnergyTotalImportLp.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(export.HasValue, Is.True);
            Assert.That(export.Value, Is.EqualTo(100f));
            Assert.That(import.Value, Is.EqualTo(200f));
            var start = await client.ReadStatisticsStartTimeAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(start.Value, Is.EqualTo((DateTimeUtc)started));

            await client.ResetStatisticsAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(provider.ResetCount, Is.EqualTo(1));
            start = await client.ReadStatisticsStartTimeAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(start.Value, Is.EqualTo((DateTimeUtc)restarted));
            export = await client.AcActiveEnergyTotalExportLp.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(export.Value, Is.Zero);
            var power = await client.AcActivePowerTotal.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(power.Value, Is.EqualTo(15.5f));
            var tag = await client.ApplicationTag.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(tag.Value, Is.EqualTo("statistics/initial"));
        }

        /// <summary>
        /// Phase structures and high-precision statistics keep distinct values on a live session.
        /// </summary>
        [Test]
        public async Task E3TypedPhaseValuesReadThroughGeneratedProxy()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var acquired = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);
            ExpandedNodeId pe = DataTypeIds.AcPeDataType;
            var provider = new StatisticsMeasurementProvider(
                acquired,
                acquired,
                Reading(BrowseNames.AcActivePowerPe, Phase(1f, 2f, 3f), pe, acquired),
                Reading(BrowseNames.AcCurrentPe, Phase(4f, 5f, 6f), pe, acquired),
                Reading(BrowseNames.AcPowerFactorPe, Phase(0.7f, 0.8f, 0.9f), pe, acquired),
                Reading(BrowseNames.AcReactivePowerPe, Phase(7f, 8f, 9f), pe, acquired),
                Reading(BrowseNames.AcVoltagePe, Phase(230f, 231f, 232f), pe, acquired),
                Reading(BrowseNames.AcVoltagePp,
                    Variant.From(new AcPpDataType { L1L2 = 400f, L2L3 = 401f, L3L1 = 402f }),
                    DataTypeIds.AcPpDataType, acquired),
                Reading(BrowseNames.AcActiveEnergyTotalExportHp, Variant.From(1.0000001d), DoubleType(), acquired),
                Reading(BrowseNames.AcActiveEnergyTotalImportHp, Variant.From(2.0000002d), DoubleType(), acquired),
                Reading(BrowseNames.AcReactiveEnergyTotalExportHp, Variant.From(3.0000003d), DoubleType(), acquired),
                Reading(BrowseNames.AcReactiveEnergyTotalImportHp, Variant.From(4.0000004d), DoubleType(), acquired));
            EcmMeasurementClient client = await CreateMeasurementAsync(provider, timeout.Token).ConfigureAwait(false);

            var voltage = await client.AcVoltagePe.ReadAsync(timeout.Token).ConfigureAwait(false);
            var lines = await client.AcVoltagePp.ReadAsync(timeout.Token).ConfigureAwait(false);
            var factor = await client.AcPowerFactorPe.ReadAsync(timeout.Token).ConfigureAwait(false);
            var exported = await client.AcActiveEnergyTotalExportHp.ReadAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(voltage.Value.L1, Is.EqualTo(230f));
            Assert.That(voltage.Value.L3, Is.EqualTo(232f));
            Assert.That(lines.Value.L1L2, Is.EqualTo(400f));
            Assert.That(lines.Value.L3L1, Is.EqualTo(402f));
            Assert.That(factor.Value.L2, Is.EqualTo(0.8f));
            Assert.That(exported.Value, Is.EqualTo(1.0000001d));
        }

        /// <summary>
        /// D1 values and statistics read through generated proxies and the grouped reset zeroes every statistic.
        /// </summary>
        [Test]
        public async Task D1ValuesReadAndResetThroughGeneratedProxy()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var started = new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc);
            var restarted = new DateTime(2026, 8, 14, 11, 0, 0, DateTimeKind.Utc);
            var provider = new StatisticsMeasurementProvider(
                started,
                restarted,
                Reading(BrowseNames.DcActivePower, Variant.From(480f), FloatType(), started),
                Reading(BrowseNames.DcCurrent, Variant.From(10f), FloatType(), started),
                Reading(BrowseNames.DcVoltage, Variant.From(48f), FloatType(), started),
                Reading(BrowseNames.DcElectricalCharge, Variant.From(5f), FloatType(), started),
                Reading(BrowseNames.DcEnergyTotalExportLp, Variant.From(6f), FloatType(), started),
                Reading(BrowseNames.DcEnergyTotalImportLp, Variant.From(7f), FloatType(), started),
                Reading(BrowseNames.DcRelativeCharge, Variant.From(80f), FloatType(), started));
            EcmMeasurementClient client = await CreateMeasurementAsync(provider, timeout.Token).ConfigureAwait(false);

            var power = await client.DcActivePower.ReadAsync(timeout.Token).ConfigureAwait(false);
            var charge = await client.DcRelativeCharge.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(power.Value, Is.EqualTo(480f));
            Assert.That(charge.Value, Is.EqualTo(80f));

            await client.ResetStatisticsAsync(timeout.Token).ConfigureAwait(false);

            charge = await client.DcRelativeCharge.ReadAsync(timeout.Token).ConfigureAwait(false);
            var imported = await client.DcEnergyTotalImportLp.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(charge.Value, Is.Zero);
            Assert.That(imported.Value, Is.Zero);
            power = await client.DcActivePower.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(power.Value, Is.EqualTo(480f));
        }

        /// <summary>
        /// The measurement instance lives in a namespace that differs from the ECM model namespace, and the
        /// client resolves type members through the session's own namespace table.
        /// </summary>
        [Test]
        public async Task MeasurementInstanceNamespaceDiffersFromModelNamespace()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var acquired = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);
            var provider = new StatisticsMeasurementProvider(
                acquired,
                acquired,
                Reading(BrowseNames.DcActivePower, Variant.From(1f), FloatType(), acquired),
                Reading(BrowseNames.DcCurrent, Variant.From(2f), FloatType(), acquired),
                Reading(BrowseNames.DcVoltage, Variant.From(3f), FloatType(), acquired),
                Reading(BrowseNames.DcElectricalCharge, Variant.From(0f), FloatType(), acquired),
                Reading(BrowseNames.DcEnergyTotalExportLp, Variant.From(0f), FloatType(), acquired),
                Reading(BrowseNames.DcEnergyTotalImportLp, Variant.From(0f), FloatType(), acquired),
                Reading(BrowseNames.DcRelativeCharge, Variant.From(0f), FloatType(), acquired));
            EcmMeasurementClient client = await CreateMeasurementAsync(provider, timeout.Token).ConfigureAwait(false);

            int modelIndex = m_session.NamespaceUris.GetIndex(Namespaces.ECM);
            Assert.That(modelIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(client.MeasurementPoint.ObjectId.NamespaceIndex, Is.Not.EqualTo((ushort)modelIndex));
            Assert.That(client.MeasurementPoint.ObjectId.NamespaceIndex, Is.EqualTo(m_manager.InstanceNamespaceIndex));
            var voltage = await client.DcVoltage.ReadAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(voltage.Value, Is.EqualTo(3f));
        }

        /// <summary>
        /// Creates and refreshes a statistics measurement, then discovers its typed client.
        /// </summary>
        private async Task<EcmMeasurementClient> CreateMeasurementAsync(
            StatisticsMeasurementProvider provider,
            CancellationToken cancellationToken)
        {
            m_measurement = await m_manager.CreateMeasurementAsync(
                new QualifiedName("StatisticsMeter", m_manager.InstanceNamespaceIndex),
                provider,
                authorize: static (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<bool>(true);
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Di.ObjectIds.DeviceSet, m_session.NamespaceUris);
            await foreach (EcmMeasurementClient candidate in EcmMeasurementClient.DiscoverAsync(
                m_session, deviceSet, NUnitTelemetryContext.Create(), cancellationToken).ConfigureAwait(false))
            {
                if (candidate.MeasurementPoint.ObjectId == m_measurement.MeasurementPoint.NodeId)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("The statistics measurement point was not discovered.");
        }

        /// <summary>
        /// Creates a Good reading.
        /// </summary>
        private static EcmMeasurementReading Reading(
            string name,
            Variant value,
            ExpandedNodeId dataType,
            DateTime timestamp)
        {
            return new EcmMeasurementReading(name, value, dataType, StatusCodes.Good, timestamp);
        }

        /// <summary>
        /// Creates a phase-structure value.
        /// </summary>
        private static Variant Phase(float l1, float l2, float l3)
        {
            return Variant.From(new AcPeDataType { L1 = l1, L2 = l2, L3 = l3 });
        }

        /// <summary>
        /// Returns the Float DataType.
        /// </summary>
        private static ExpandedNodeId FloatType()
        {
            return Opc.Ua.DataTypeIds.Float;
        }

        /// <summary>
        /// Returns the Double DataType.
        /// </summary>
        private static ExpandedNodeId DoubleType()
        {
            return Opc.Ua.DataTypeIds.Double;
        }

        /// <summary>The measurement created by the current test.</summary>
        private EcmMeasurementBinding m_measurement = null!;
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
        private global::Opc.Ua.Client.ISession m_session = null!;

        /// <summary>The typed client borrowing the fixture's session.</summary>
        private EcmClient m_client = null!;

        /// <summary>
        /// Provides fixed typed readings and retains the writable tag for this integration test.
        /// </summary>
        private sealed class TestMeasurementProvider : IEcmMeasurementProvider
        {
            /// <summary>
            /// Creates a provider with one published accuracy domain and acquisition time.
            /// </summary>
            public TestMeasurementProvider(NodeId accuracyDomain, DateTime acquired)
            {
                m_accuracyDomain = accuracyDomain;
                m_acquired = acquired;
            }

            /// <inheritdoc/>
            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_lock)
                {
                    return new ValueTask<EcmMeasurementSnapshot>(new EcmMeasurementSnapshot(
                        m_applicationTag,
                        [
                            new(
                                BrowseNames.AcCurrentPe,
                                Variant.From(new AcPeDataType { L1 = 1.25f, L2 = 2.5f, L3 = 3.75f }),
                                DataTypeIds.AcPeDataType,
                                StatusCodes.Good,
                                m_acquired,
                                AccuracyClass: 0,
                                AccuracyDomain: m_accuracyDomain),
                            new(
                                BrowseNames.AcActivePowerTotal,
                                Variant.From(15.5f),
                                Opc.Ua.DataTypeIds.Float,
                                StatusCodes.UncertainLastUsableValue,
                                m_acquired),
                            new(
                                BrowseNames.DcCurrent,
                                Variant.From(24.75f),
                                Opc.Ua.DataTypeIds.Float,
                                StatusCodes.Good,
                                m_acquired)
                        ]));
                }
            }

            /// <inheritdoc/>
            public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_lock)
                {
                    m_applicationTag = applicationTag;
                }
                return default;
            }

            /// <summary>The synchronization gate for tag state.</summary>
            private readonly System.Threading.Lock m_lock = new();

            /// <summary>The source timestamp for the provider's fixed readings.</summary>
            private readonly DateTime m_acquired;

            /// <summary>The published accuracy domain.</summary>
            private readonly NodeId m_accuracyDomain;

            /// <summary>The application-owned tag value.</summary>
            private string m_applicationTag = "integration/initial";
        }

        /// <summary>
        /// Supplies fixed readings with a StartTime and resets every statistic member to zero.
        /// </summary>
        private sealed class StatisticsMeasurementProvider : IEcmStatisticsProvider
        {
            /// <summary>
            /// Creates a provider with the initial readings, collection StartTime and reset StartTime.
            /// </summary>
            public StatisticsMeasurementProvider(
                DateTime started,
                DateTime restarted,
                params EcmMeasurementReading[] readings)
            {
                m_started = started;
                m_restarted = restarted;
                m_readings = readings;
            }

            /// <summary>Gets the number of committed resets.</summary>
            public int ResetCount { get; private set; }

            /// <inheritdoc/>
            public ValueTask<EcmMeasurementSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<EcmMeasurementSnapshot>(new EcmMeasurementSnapshot(
                    "statistics/initial",
                    m_readings.ToArrayOf())
                {
                    StatisticsStartTime = (DateTimeUtc)(ResetCount == 0 ? m_started : m_restarted)
                });
            }

            /// <inheritdoc/>
            public ValueTask WriteApplicationTagAsync(string applicationTag, CancellationToken cancellationToken)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> AuthorizeStatisticsResetAsync(
                ISystemContext requestContext,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<EcmStatisticsSnapshot> ResetStatisticsAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EcmMeasurementReading[] zeroed = m_readings
                    .Where(reading => IsStatistic(reading.BrowseName))
                    .Select(reading => reading with
                    {
                        Value = reading.DataType.Equals(DoubleType()) ? Variant.From(0d) : Variant.From(0f)
                    })
                    .ToArray();
                m_readings = m_readings
                    .Select(reading => zeroed.FirstOrDefault(z => z.BrowseName == reading.BrowseName) ?? reading)
                    .ToArray();
                ResetCount++;
                return new ValueTask<EcmStatisticsSnapshot>(
                    new EcmStatisticsSnapshot((DateTimeUtc)m_restarted, zeroed.ToArrayOf()));
            }

            /// <summary>
            /// Returns whether a member is a resettable statistic.
            /// </summary>
            private static bool IsStatistic(string name)
            {
                return name.Contains("Energy", StringComparison.Ordinal) ||
                    name == BrowseNames.DcElectricalCharge || name == BrowseNames.DcRelativeCharge;
            }

            /// <summary>The StartTime before the first reset.</summary>
            private readonly DateTime m_started;

            /// <summary>The StartTime after a reset.</summary>
            private readonly DateTime m_restarted;

            /// <summary>The current readings.</summary>
            private EcmMeasurementReading[] m_readings;
        }
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
