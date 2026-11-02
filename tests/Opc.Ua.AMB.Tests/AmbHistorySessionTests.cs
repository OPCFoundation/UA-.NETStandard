/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Exercises opt-in history through the asset owner and real Part 11 sessions.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbHistorySessionTests
    {
        /// <summary>
        /// Gets archived states that must not restore the current health timestamp.
        /// </summary>
        private static IEnumerable<TestCaseData> UnrelatedArchivedStates =>
        [
            new TestCaseData((uint)StatusCodes.Good, DeviceHealthEnumeration.FAILURE),
            new TestCaseData((uint)StatusCodes.BadNoData, DeviceHealthEnumeration.NORMAL)
        ];

        [Test]
        public async Task DisabledHistoryPreservesLiveDefaultsAndRejectsHistoricalReadsAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(DisabledHistoryPreservesLiveDefaultsAndRejectsHistoricalReadsAsync),
                    builder => builder.WithDeviceHealth()).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            AssetHistoryCapabilities capabilities = await client.GetHistoryCapabilitiesAsync(asset.NodeId)
                .ConfigureAwait(false);
            Assert.That(capabilities.HealthValues, Is.False);
            Assert.That(capabilities.Events, Is.False);
            Assert.That(device.DeviceHealth!.Historizing, Is.False);
            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName(ConformanceUnitNames.AssetHealthTrackingOverallAssetStatus)));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task RawHealthHistoryPagesInBothDirectionsWithOriginalTimestampsAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                MaxSamplesPerNode = 3
            });
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(RawHealthHistoryPagesInBothDirectionsWithOriginalTimestampsAsync),
                    builder => builder.WithDeviceHealth(),
                    options => options.UseHistory(provider, AssetHistoryKinds.DeviceHealth)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            DateTime start = DateTime.UtcNow.AddHours(-1);
            await WaitForAsync(async () =>
                (await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false)).Count == 1).ConfigureAwait(false);
            await asset.Health!.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE).ConfigureAwait(false);
            DateTimeUtc failureTime = device.DeviceHealth!.Timestamp;
            await WaitForAsync(async () =>
                (await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false)).Count == 2).ConfigureAwait(false);
            await asset.Health.SetDeviceHealthAsync(DeviceHealthEnumeration.NORMAL).ConfigureAwait(false);
            DateTimeUtc normalTime = device.DeviceHealth.Timestamp;
            await WaitForAsync(async () =>
                (await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false)).Count == 3).ConfigureAwait(false);
            DateTime end = DateTime.UtcNow;
            List<AssetHealthHistoryValue> forward = await CollectAsync(
                client.ReadHealthHistoryAsync(asset.NodeId, start, end, maxValuesPerNode: 1)).ConfigureAwait(false);
            List<AssetHealthHistoryValue> reverse = await CollectAsync(
                client.ReadHealthHistoryAsync(asset.NodeId, end, start, maxValuesPerNode: 1)).ConfigureAwait(false);
            AssetHistoryCapabilities capabilities = await client.GetHistoryCapabilitiesAsync(asset.NodeId)
                .ConfigureAwait(false);
            Assert.That(forward.Select(value => value.DeviceHealth), Is.EqualTo(new[]
            {
                DeviceHealthEnumeration.NORMAL, DeviceHealthEnumeration.FAILURE, DeviceHealthEnumeration.NORMAL
            }));
            Assert.That(
                reverse.Select(value => value.Value.SourceTimestamp),
                Is.EqualTo(forward.Select(value => value.Value.SourceTimestamp).Reverse()));
            Assert.That(forward[1].Value.SourceTimestamp, Is.EqualTo(failureTime));
            Assert.That(forward[2].Value.SourceTimestamp, Is.EqualTo(normalTime));
            Assert.That(forward.All(value => StatusCode.IsGood(value.Value.StatusCode)), Is.True);
            Assert.That(capabilities, Is.EqualTo(new AssetHistoryCapabilities(true, false)));
            Assert.That(device.DeviceHealth.Historizing, Is.True);
            Assert.That(device.DeviceHealth.AccessLevel & AccessLevels.HistoryWrite, Is.Zero);
            Assert.That(
                await server.BrowseAsync(device.DeviceHealth.NodeId, Ua.ReferenceTypeIds.HasHistoricalConfiguration)
                    .ConfigureAwait(false),
                Has.Count.EqualTo(1));
            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName(ConformanceUnitNames.AssetHealthTrackingOverallAssetStatus)));
            Assert.That(await CollectAsync(client.ReadHealthHistoryAsync(
                asset.NodeId, end.AddHours(1), end.AddHours(2))).ConfigureAwait(false), Is.Empty);

            await using (IAsyncEnumerator<AssetHealthHistoryValue> partial = client.ReadHealthHistoryAsync(
                asset.NodeId, start, end, maxValuesPerNode: 1).GetAsyncEnumerator())
            {
                Assert.That(await partial.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(partial.Current.Value.SourceTimestamp, Is.EqualTo(forward[0].Value.SourceTimestamp));
            }
            Assert.That(await CollectAsync(client.ReadHealthHistoryAsync(
                asset.NodeId, start, end, maxValuesPerNode: 1)).ConfigureAwait(false), Has.Count.EqualTo(3));

            byte originalUserAccess = device.DeviceHealth.UserAccessLevel;
            device.DeviceHealth.UserAccessLevel = (byte)(originalUserAccess & ~AccessLevels.HistoryRead);
            try
            {
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await CollectAsync(client.ReadHealthHistoryAsync(
                        asset.NodeId, start, end)).ConfigureAwait(false));
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                device.DeviceHealth.UserAccessLevel = originalUserAccess;
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, start, end, cancellationToken: cancelled.Token)).ConfigureAwait(false));

            await asset.Health.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE).ConfigureAwait(false);
            DateTimeUtc lastTime = device.DeviceHealth.Timestamp;
            List<AssetHealthHistoryValue> retained = [];
            await WaitForAsync(async () =>
            {
                retained = await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false);
                return retained.Count > 0 && retained[retained.Count - 1].Value.SourceTimestamp == lastTime;
            }).ConfigureAwait(false);
            Assert.That(retained, Has.Count.EqualTo(3), "the provider's capacity policy controls retention");
            Assert.That(retained[0].Value.SourceTimestamp, Is.EqualTo(failureTime));
            Assert.That(retained[2].Value.SourceTimestamp, Is.EqualTo(lastTime));
        }

        [Test]
        public async Task EventHistoryPreservesRootCausesAndRecurringPlannedAndActualDetailsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            DateTime planned = DateTime.UtcNow.AddDays(1);
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(EventHistoryPreservesRootCausesAndRecurringPlannedAndActualDetailsAsync),
                    builder => builder
                        .WithDeviceHealth(deriveFromAlarms: true)
                        .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure)
                        .WithMaintenance("Inspection", AmbConditionClass.Inspection, details =>
                        {
                            details.PlannedDate = planned;
                            details.EstimatedDowntime = TimeSpan.FromHours(2);
                            details.ConfigurationChanged = false;
                        }),
                    options => options.UseHistory(provider)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            DateTime start = DateTime.UtcNow.AddHours(-1);
            var handle = (AssetHandle)asset;
            var vendorClass = new NodeId("VendorInspectionConditionClass", device.NodeId.NamespaceIndex);
            handle.Owner.AddNode(new BaseObjectTypeState(null)
            {
                NodeId = vendorClass,
                BrowseName = new QualifiedName("VendorInspectionConditionClass", device.NodeId.NamespaceIndex),
                DisplayName = new LocalizedText("Vendor inspection"),
                SuperTypeId = AmbConditionClass.Inspection.GetTypeId(server.Server.NamespaceUris)
            });
            ConditionState maintenanceCondition = await server.FindNodeAsync<ConditionState>(
                asset.Maintenance!.Activities[0].NodeId).ConfigureAwait(false);
            maintenanceCondition.ConditionClassId!.Value = vendorClass;
            var cause = new NodeId("Cable", device.NodeId.NamespaceIndex);
            await asset.Health!.Alarms[0].RaiseAsync(
                700, new LocalizedText("failure"), [AssetRootCauses.Of(cause, new LocalizedText("cut"))])
                .ConfigureAwait(false);
            await asset.Health.Alarms[0].ClearAsync(new LocalizedText("cleared")).ConfigureAwait(false);
            IMaintenanceActivity activity = asset.Maintenance!.Activities[0];
            await activity.StartAsync(new LocalizedText("started")).ConfigureAwait(false);
            await activity.UpdateAsync(
                details =>
                {
                    details.ConfigurationChanged = true;
                    details.EstimatedDowntime = TimeSpan.FromMinutes(15);
                },
                new LocalizedText("actual")).ConfigureAwait(false);
            await activity.FinishAsync(new LocalizedText("finished")).ConfigureAwait(false);
            await activity.ReplanAsync(planned.AddDays(1), new LocalizedText("next")).ConfigureAwait(false);
            List<MaintenanceActivityRecord> maintenance = [];
            List<AssetAlarmRecord> alarms = [];
            await WaitForAsync(async () =>
            {
                maintenance = await CollectAsync(client.ReadPastMaintenanceAsync(
                    asset.NodeId, start, DateTime.UtcNow, maxValuesPerNode: 1)).ConfigureAwait(false);
                alarms = await CollectAsync(client.ReadHealthEventsAsync(
                    asset.NodeId, start, DateTime.UtcNow, maxValuesPerNode: 1)).ConfigureAwait(false);
                return maintenance.Any(value => value.Message.Text == "next") &&
                    alarms.Any(value => value.Message.Text == "cleared");
            }).ConfigureAwait(false);
            AssetAlarmRecord failure = alarms.Single(value => value.Message.Text == "failure");
            MaintenanceActivityRecord initial = maintenance.First(
                value => value.State == MaintenanceStateKind.Planned);
            MaintenanceActivityRecord actual = maintenance.Single(value => value.Message.Text == "actual");
            MaintenanceActivityRecord next = maintenance.Single(value => value.Message.Text == "next");
            Assert.That(failure.SourceNode, Is.EqualTo(asset.NodeId));
            Assert.That(failure.PotentialRootCauses[0].RootCauseId, Is.EqualTo(cause));
            Assert.That(failure.PotentialRootCauses[0].RootCause.Text, Is.EqualTo("cut"));
            Assert.That(failure.IsActive, Is.True);
            Assert.That(alarms.Single(value => value.Message.Text == "cleared").IsActive, Is.False);
            Assert.That(failure.HistoryFields.Count, Is.EqualTo(client.GetHistoryEventFields().Count));
            Assert.That(initial.ConfigurationChanged, Is.False);
            Assert.That(initial.EstimatedDowntime, Is.EqualTo(TimeSpan.FromHours(2)));
            Assert.That(initial.PlannedDate, Is.EqualTo((DateTimeUtc)planned));
            Assert.That(actual.ConfigurationChanged, Is.True);
            Assert.That(
                actual.ConditionClassId, Is.EqualTo(vendorClass), "vendor maintenance subtypes remain selected");
            Assert.That(actual.EstimatedDowntime, Is.EqualTo(TimeSpan.FromMinutes(15)));
            Assert.That(next.PlannedDate, Is.EqualTo((DateTimeUtc)planned.AddDays(1)));
            Assert.That(next.ConditionId, Is.EqualTo(initial.ConditionId));
            Assert.That(next.EventId, Is.Not.EqualTo(initial.EventId));
            Assert.That(next.EventId.IsEmpty, Is.False);
            Assert.That(maintenance.Any(value => value.State == MaintenanceStateKind.Finished), Is.True);
            Assert.That(alarms.All(value => value.ConditionId != activity.NodeId), Is.True);
            Assert.That(
                maintenance.All(value => value.ConditionId == activity.NodeId && value.SourceNode == asset.NodeId),
                Is.True);
            Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(new[]
            {
                new QualifiedName(ConformanceUnitNames.AssetHealthTrackingOverallAssetStatus),
                new QualifiedName(ConformanceUnitNames.AssetHealthTrackingEvents),
                new QualifiedName(ConformanceUnitNames.PastMaintenanceActivities)
            }));
            Assert.That(device.EventNotifier & EventNotifiers.HistoryRead, Is.Not.Zero);
            Assert.That(device.EventNotifier & EventNotifiers.HistoryWrite, Is.Zero);
        }

        [Test]
        public async Task PerAssetDisableSuppressesGlobalCaptureAndCoverageUnitsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(PerAssetDisableSuppressesGlobalCaptureAndCoverageUnitsAsync),
                    builder => builder.WithDeviceHealth().WithHistory(null),
                    options => options.UseHistory(provider)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            Assert.That(device.DeviceHealth!.Historizing, Is.False);
            Assert.That(((AssetHandle)asset).History, Is.Null);
            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName(ConformanceUnitNames.AssetHealthTrackingOverallAssetStatus)));
            Assert.That(await provider.IsHistorizingAsync(device.DeviceHealth.NodeId, CancellationToken.None),
                Is.False);
        }

        [Test]
        public async Task EqualTimestampEventsRemainDistinctAndFilterByAssetIdentityAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(EqualTimestampEventsRemainDistinctAndFilterByAssetIdentityAsync),
                    builder => builder.WithHealthAlarm(
                        "Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure),
                    options => options.UseHistory(provider, AssetHistoryKinds.HealthEvents)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            var handle = (AssetHandle)asset;
            ConditionState condition = await server.FindNodeAsync<ConditionState>(asset.Health!.Alarms[0].NodeId)
                .ConfigureAwait(false);
            DateTime timestamp = DateTime.UtcNow;
            ByteString first = Uuid.NewUuid().ToByteString();
            ByteString second = Uuid.NewUuid().ToByteString();
            ReportSnapshot(condition, handle.Context, asset.NodeId, first, timestamp, "same-time-one");
            ReportSnapshot(condition, handle.Context, asset.NodeId, second, timestamp, "same-time-two");
            ReportSnapshot(condition, handle.Context, new NodeId("other", device.NodeId.NamespaceIndex),
                Uuid.NewUuid().ToByteString(), timestamp, "other-asset");
            ReportSnapshot(condition, handle.Context, asset.NodeId,
                Uuid.NewUuid().ToByteString(), timestamp.AddMilliseconds(1), "after-other");
            List<AssetAlarmRecord> values = [];
            await WaitForAsync(async () =>
            {
                values = await CollectAsync(client.ReadHealthEventsAsync(
                    asset.NodeId, timestamp.AddSeconds(-1), timestamp.AddSeconds(1),
                    maxValuesPerNode: 1)).ConfigureAwait(false);
                return values.Any(value => value.Message.Text == "after-other");
            }).ConfigureAwait(false);
            List<AssetAlarmRecord> equal = values.Where(value => value.Time == (DateTimeUtc)timestamp).ToList();
            Assert.That(equal.Select(value => value.EventId), Is.EqualTo(new[] { first, second }));
            Assert.That(
                equal.Select(value => value.Message.Text), Is.EqualTo(new[] { "same-time-one", "same-time-two" }));
            Assert.That(values.Any(value => value.Message.Text == "other-asset"), Is.False);
            List<AssetAlarmRecord> reverse = await CollectAsync(client.ReadHealthEventsAsync(
                asset.NodeId, timestamp.AddSeconds(1), timestamp.AddSeconds(-1),
                maxValuesPerNode: 1)).ConfigureAwait(false);
            Assert.That(
                reverse.Where(value => value.Time == (DateTimeUtc)timestamp).Select(value => value.EventId),
                Is.EqualTo(new[] { second, first }));
        }

        [TestCase(AssetHistoryKinds.DeviceHealth, false)]
        [TestCase(AssetHistoryKinds.HealthEvents, false)]
        [TestCase(AssetHistoryKinds.MaintenanceEvents, false)]
        [TestCase(AssetHistoryKinds.DeviceHealth, true)]
        [TestCase(AssetHistoryKinds.HealthEvents, true)]
        [TestCase(AssetHistoryKinds.MaintenanceEvents, true)]
        public async Task UnsupportedProviderAbortsBindingWithoutChangingLiveAttributesAsync(
            AssetHistoryKinds kind,
            bool hasProviderInterface)
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(UnsupportedProviderAbortsBindingWithoutChangingLiveAttributesAsync),
                    builder => builder.WithDeviceHealth()
                        .WithHealthAlarm("Failure", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure)
                        .WithMaintenance("Inspection", AmbConditionClass.Inspection)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            var provider = new Mock<IHistorianProvider>(MockBehavior.Strict);
            if (hasProviderInterface)
            {
                if (kind == AssetHistoryKinds.DeviceHealth)
                {
                    provider.As<IHistorianDataProvider>();
                }
                else
                {
                    provider.As<IHistorianEventProvider>();
                }
            }
            provider.Setup(value => value.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(kind == AssetHistoryKinds.DeviceHealth
                    ? HistorianNodeCapabilities.DataReadWrite with { ReadRawData = !hasProviderInterface }
                    : HistorianNodeCapabilities.EventReadWrite with { ReadEventHistory = !hasProviderInterface });
            byte original = device.DeviceHealth!.AccessLevel;
            byte originalNotifier = device.EventNotifier;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await AssetHistoryBinding.CreateAsync(
                    (AssetHandle)asset,
                    server.Server,
                    new AssetHistoryOptions(provider.Object, kind),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(device.DeviceHealth.Historizing, Is.False);
            Assert.That(device.DeviceHealth.AccessLevel, Is.EqualTo(original));
            Assert.That(device.EventNotifier, Is.EqualTo(originalNotifier));
            provider.VerifyAll();
        }

        [TestCase(AssetHistoryKinds.DeviceHealth)]
        [TestCase(AssetHistoryKinds.HealthEvents)]
        public async Task OwnerInstalledHistoryIsRejectedWithoutChangingItsAttributesAsync(AssetHistoryKinds kind)
        {
            using var provider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(OwnerInstalledHistoryIsRejectedWithoutChangingItsAttributesAsync),
                    builder => builder.WithDeviceHealth().WithHealthAlarm(
                        "Failure", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure))
                    .ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            device.DeviceHealth!.Historizing = kind == AssetHistoryKinds.DeviceHealth;
            device.EventNotifier |= kind == AssetHistoryKinds.HealthEvents ? EventNotifiers.HistoryRead : (byte)0;
            byte originalNotifier = device.EventNotifier;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await AssetHistoryBinding.CreateAsync(
                    (AssetHandle)asset, server.Server, new AssetHistoryOptions(provider, kind),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(device.DeviceHealth.Historizing, Is.EqualTo(kind == AssetHistoryKinds.DeviceHealth));
            Assert.That(device.EventNotifier, Is.EqualTo(originalNotifier));
            Assert.That(
                ((IHistorianRegistryProvider)server.Server).HistorianRegistry.Resolve(asset.NodeId),
                Is.Null);
        }

        [Test]
        public async Task PartialEventSelectionRejectsOverlappingBaseTypesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(PartialEventSelectionRejectsOverlappingBaseTypesAsync),
                    builder => builder
                        .WithHealthAlarm(
                            "HealthMaintenance", AssetHealthAlarmKind.MaintenanceRequired,
                            AmbConditionClass.ConnectionFailure)
                        .WithMaintenance("Inspection", AmbConditionClass.Inspection),
                    options => options.UseServerDefinedAlarmTypes = false).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await AssetHistoryBinding.CreateAsync(
                    (AssetHandle)asset, server.Server,
                    new AssetHistoryOptions(provider, AssetHistoryKinds.MaintenanceEvents),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(device.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
        }

        [Test]
        public async Task AnEmptyRegistryDoesNotAdvertiseHistoryFromConfigurationAloneAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnEmptyRegistryDoesNotAdvertiseHistoryFromConfigurationAloneAsync),
                builder => builder.AddOpcUaDi().AddAssetManagement(options => options.UseHistory(provider)))
                .ConfigureAwait(false);
            Assert.That(server.AssetManagement.Assets.Count, Is.Zero);
            Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DiConfigurationResolvesTheExistingProviderAsync(bool secondHasHealth)
        {
            using var provider = new InMemoryHistorianProvider();
            IAssetHandle? asset = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(DiConfigurationResolvesTheExistingProviderAsync),
                builder =>
                {
                    builder.Services.AddSingleton<IHistorianProvider>(provider);
                    builder.AddOpcUaDi().AddAssetManagement((services, options) =>
                        options.UseHistory(services.GetRequiredService<IHistorianProvider>()));
                    builder.ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context, "Sensor", "urn:acme:history-di").ConfigureAwait(false);
                        asset = await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            configure => configure.WithDeviceHealth(),
                            context.CancellationToken).ConfigureAwait(false);
                        IDeviceBuilder<DeviceState> other = await AssetRegistrationTests.CreateDeviceAsync(
                            context, "Other", "urn:acme:history-di:other").ConfigureAwait(false);
                        await other.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            configure =>
                            {
                                configure.WithHistory(null);
                                if (secondHasHealth)
                                {
                                    configure.WithDeviceHealth();
                                }
                            },
                            context.CancellationToken).ConfigureAwait(false);
                    });
                }).ConfigureAwait(false);
            Assert.That(server.AssetManagement.Options.History!.Provider, Is.SameAs(provider));
            Assert.That(((AssetHandle)asset!).History!.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
            Assert.That(
                server.Manager.ConformanceUnits.ToList().Contains(
                    new QualifiedName(ConformanceUnitNames.AssetHealthTrackingOverallAssetStatus)),
                Is.EqualTo(!secondHasHealth),
                "every asset having DeviceHealth must be covered, but assets without it need no raw archive");
        }

        [Test]
        public async Task MaintenanceOnlyAssetOverrideExcludesHealthCaptureAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            using var unusedGlobalProvider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(MaintenanceOnlyAssetOverrideExcludesHealthCaptureAsync),
                    builder => builder.WithDeviceHealth(deriveFromAlarms: true)
                        .WithHealthAlarm("Failure", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure)
                        .WithMaintenance("Inspection", AmbConditionClass.Inspection)
                        .WithHistory(new AssetHistoryOptions(provider, AssetHistoryKinds.MaintenanceEvents)),
                    options => options.UseHistory(unusedGlobalProvider)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            DateTime start = DateTime.UtcNow.AddHours(-1);
            await asset.Health!.Alarms[0].RaiseAsync(700, new LocalizedText("excluded")).ConfigureAwait(false);
            await asset.Maintenance!.Activities[0].StartAsync(new LocalizedText("included")).ConfigureAwait(false);
            List<MaintenanceActivityRecord> activities = [];
            await WaitForAsync(async () =>
            {
                activities = await CollectAsync(client.ReadPastMaintenanceAsync(
                    asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false);
                return activities.Any(value => value.Message.Text == "included");
            }).ConfigureAwait(false);
            Assert.That(device.DeviceHealth!.Historizing, Is.False);
            Assert.That(((AssetHandle)asset).History!.Kinds, Is.EqualTo(AssetHistoryKinds.MaintenanceEvents));
            Assert.That(await CollectAsync(client.ReadHealthEventsAsync(
                asset.NodeId, start, DateTime.UtcNow)).ConfigureAwait(false), Is.Empty);
            Assert.That(await unusedGlobalProvider.IsHistorizingAsync(asset.NodeId, CancellationToken.None), Is.False);
            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Contain(new QualifiedName(ConformanceUnitNames.PastMaintenanceActivities)));
            Assert.That(
                server.Manager.ConformanceUnits.ToArray(),
                Does.Not.Contain(new QualifiedName(ConformanceUnitNames.AssetHealthTrackingEvents)));
        }

        [Test]
        public async Task FailedEventBindingUndoesAlreadyInstalledRawCaptureAsync()
        {
            using var storage = new InMemoryHistorianProvider();
            Mock<IHistorianProvider> provider = RetainedRawProvider(storage);
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(FailedEventBindingUndoesAlreadyInstalledRawCaptureAsync),
                    builder => builder.WithDeviceHealth()
                        .WithMaintenance("Inspection", AmbConditionClass.Inspection)).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            byte original = device.DeviceHealth!.AccessLevel;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await AssetHistoryBinding.CreateAsync(
                    (AssetHandle)asset, server.Server, new AssetHistoryOptions(provider.Object),
                    CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(device.DeviceHealth.Historizing, Is.False);
            Assert.That(device.DeviceHealth.AccessLevel, Is.EqualTo(original));
            Assert.That(device.EventNotifier & EventNotifiers.HistoryRead, Is.Zero);
            Assert.That(
                ((IHistorianRegistryProvider)server.Server).HistorianRegistry.Resolve(device.DeviceHealth.NodeId),
                Is.Null);
        }

        [Test]
        public async Task RepeatedDisposalDoesNotRemoveAReplacementBindingAsync()
        {
            using var firstProvider = new InMemoryHistorianProvider();
            using var secondProvider = new InMemoryHistorianProvider();
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(RepeatedDisposalDoesNotRemoveAReplacementBindingAsync),
                    builder => builder.WithDeviceHealth()).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            AssetHistoryBinding first = (await AssetHistoryBinding.CreateAsync(
                (AssetHandle)asset, server.Server,
                new AssetHistoryOptions(firstProvider, AssetHistoryKinds.DeviceHealth),
                CancellationToken.None).ConfigureAwait(false))!;
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That(device.DeviceHealth!.Historizing, Is.False);
            await using AssetHistoryBinding second = (await AssetHistoryBinding.CreateAsync(
                (AssetHandle)asset, server.Server,
                new AssetHistoryOptions(secondProvider, AssetHistoryKinds.DeviceHealth),
                CancellationToken.None).ConfigureAwait(false))!;
            await first.DisposeAsync().ConfigureAwait(false);
            Assert.That(device.DeviceHealth.Historizing, Is.True);
            Assert.That(
                ((IHistorianRegistryProvider)server.Server).HistorianRegistry.Resolve(device.DeviceHealth.NodeId),
                Is.SameAs(secondProvider));
            Assert.That(first.Kinds, Is.EqualTo(AssetHistoryKinds.None));
            Assert.That(second.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
        }

        [Test]
        public async Task RestartKeepsTheArchivedStateEntryTimestampWithStableNodeIdsAsync()
        {
            using var storage = new InMemoryHistorianProvider();
            Mock<IHistorianProvider> provider = RetainedRawProvider(storage);
            NodeId nodeId;
            DateTimeUtc timestamp;
            (AmbHostedServer first, IAssetHandle firstAsset, DeviceState firstDevice) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(RestartKeepsTheArchivedStateEntryTimestampWithStableNodeIdsAsync),
                    builder => builder.WithDeviceHealth(),
                    options => options.UseHistory(provider.Object, AssetHistoryKinds.DeviceHealth))
                    .ConfigureAwait(false);
            await using (first.ConfigureAwait(false))
            {
                using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
                using ISession session = await ConnectAsync(fixture, first).ConfigureAwait(false);
                var client = new AmbClient(session, NUnitTelemetryContext.Create());
                List<AssetHealthHistoryValue> values = [];
                await WaitForAsync(async () =>
                {
                    values = await CollectAsync(client.ReadHealthHistoryAsync(
                        firstAsset.NodeId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow)).ConfigureAwait(false);
                    return values.Count == 1;
                }).ConfigureAwait(false);
                nodeId = firstDevice.DeviceHealth!.NodeId;
                timestamp = values[0].Value.SourceTimestamp;
            }
            (AmbHostedServer second, IAssetHandle secondAsset, DeviceState secondDevice) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(RestartKeepsTheArchivedStateEntryTimestampWithStableNodeIdsAsync),
                    builder => builder.WithDeviceHealth(),
                    options => options.UseHistory(provider.Object, AssetHistoryKinds.DeviceHealth))
                    .ConfigureAwait(false);
            await using AmbHostedServer secondScope = second;
            Assert.That(secondDevice.DeviceHealth!.NodeId, Is.EqualTo(nodeId));
            Assert.That(secondDevice.DeviceHealth.Timestamp, Is.EqualTo(timestamp));
            Assert.That(((AssetHandle)secondAsset).History!.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
        }

        [Test]
        public async Task ProviderReadFailuresRemainErrorsInsteadOfAnEmptyArchiveAsync()
        {
            using var storage = new InMemoryHistorianProvider();
            Mock<IHistorianProvider> provider = RetainedRawProvider(storage);
            (AmbHostedServer server, IAssetHandle asset, _) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(ProviderReadFailuresRemainErrorsInsteadOfAnEmptyArchiveAsync),
                    builder => builder.WithDeviceHealth(),
                    options => options.UseHistory(provider.Object, AssetHistoryKinds.DeviceHealth))
                    .ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            using var fixture = new ClientFixture(NUnitTelemetryContext.Create());
            using ISession session = await ConnectAsync(fixture, server).ConfigureAwait(false);
            var client = new AmbClient(session, NUnitTelemetryContext.Create());
            provider.As<IHistorianDataProvider>()
                .Setup(value => value.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadResourceUnavailable));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CollectAsync(client.ReadHealthHistoryAsync(
                    asset.NodeId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        [TestCaseSource(nameof(UnrelatedArchivedStates))]
        public async Task UnrelatedOrBadArchivedStateDoesNotReplaceTheCurrentEntryTimestampAsync(
            uint status,
            DeviceHealthEnumeration archivedHealth)
        {
            using var storage = new InMemoryHistorianProvider();
            Mock<IHistorianProvider> provider = RetainedRawProvider(storage);
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) =
                await HealthAlarmTests.StartWithSensorAsync(
                    nameof(UnrelatedOrBadArchivedStateDoesNotReplaceTheCurrentEntryTimestampAsync),
                    builder => builder.WithDeviceHealth()).ConfigureAwait(false);
            await using AmbHostedServer scope = server;
            var handle = (AssetHandle)asset;
            DateTimeUtc currentTimestamp = device.DeviceHealth!.Timestamp;
            DateTime archivedTimestamp = DateTime.UtcNow.AddDays(-1);
            storage.Register(device.DeviceHealth.NodeId);
            using var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            HistorianUpdateOutcome<DataValue> inserted = await storage.InsertAsync(
                new HistorianOperationContext(
                    (ServerSystemContext)handle.Context, operation, device.DeviceHealth, HistoryUpdateType.Insert),
                device.DeviceHealth.NodeId,
                [new DataValue(Variant.From((int)archivedHealth), status, archivedTimestamp)],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(inserted.OperationResults.ToArray(), Is.EqualTo(new[] { (StatusCode)StatusCodes.Good }));
            await using AssetHistoryBinding binding = (await AssetHistoryBinding.CreateAsync(
                handle, server.Server, new AssetHistoryOptions(provider.Object, AssetHistoryKinds.DeviceHealth),
                CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(device.DeviceHealth.Timestamp, Is.EqualTo(currentTimestamp));
            Assert.That(device.DeviceHealth.Timestamp, Is.Not.EqualTo((DateTimeUtc)archivedTimestamp));
            Assert.That(binding.Kinds, Is.EqualTo(AssetHistoryKinds.DeviceHealth));
        }

        /// <summary>
        /// Keeps the test archive outside either server's provider-disposal boundary.
        /// This models a backend that remains available after the server host restarts.
        /// </summary>
        private static Mock<IHistorianProvider> RetainedRawProvider(InMemoryHistorianProvider storage)
        {
            var provider = new Mock<IHistorianProvider>(MockBehavior.Strict);
            provider.Setup(value => value.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId node, CancellationToken ct) =>
                {
                    if (!node.IsNull)
                    {
                        storage.Register(node);
                    }
                    return storage.GetCapabilitiesAsync(node, ct);
                });
            provider.Setup(value => value.IsHistorizingAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId node, CancellationToken ct) => storage.IsHistorizingAsync(node, ct));
            Mock<IHistorianDataProvider> data = provider.As<IHistorianDataProvider>();
            data.Setup(value => value.ReadRawAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<HistorianRawReadRequest>(),
                It.IsAny<HistorianResumeToken>(), It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext context, HistorianRawReadRequest request,
                    HistorianResumeToken token, CancellationToken ct) =>
                    storage.ReadRawAsync(context, request, token, ct));
            data.Setup(value => value.InsertAsync(
                It.IsAny<HistorianOperationContext>(), It.IsAny<NodeId>(),
                It.IsAny<ArrayOf<DataValue>>(), It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext context, NodeId node,
                    ArrayOf<DataValue> values, CancellationToken ct) =>
                    storage.InsertAsync(context, node, values, ct));
            return provider;
        }

        /// <summary>
        /// Reports an immutable event with a controlled timestamp and identity.
        /// </summary>
        private static void ReportSnapshot(
            ConditionState condition,
            ISystemContext context,
            NodeId source,
            ByteString eventId,
            DateTime timestamp,
            string message)
        {
            condition.SourceNode!.Value = source;
            condition.EventId!.Value = eventId;
            condition.Time!.Value = timestamp;
            condition.Message!.Value = new LocalizedText(message);
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, condition);
            condition.ReportEvent(context, snapshot);
        }

        /// <summary>
        /// Opens a real session with the AMB model registered.
        /// </summary>
        private static async Task<ISession> ConnectAsync(ClientFixture fixture, AmbHostedServer server)
        {
            await fixture.LoadClientConfigurationAsync(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbHistorySessionTests),
                Guid.NewGuid().ToString("N"),
                "client-pki")).ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync(
                new Uri(server.EndpointUrl), SecurityPolicies.None).ConfigureAwait(false);
            session.Factory.Builder.AddOpcUaAMB().Commit();
            return session;
        }

        /// <summary>
        /// Consumes the public stream, including its paging and disposal path.
        /// </summary>
        private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> stream)
        {
            var values = new List<T>();
            await foreach (T value in stream.ConfigureAwait(false))
            {
                values.Add(value);
            }
            return values;
        }

        /// <summary>
        /// Waits for asynchronous capture, failing with a bounded deadline.
        /// </summary>
        private static async Task WaitForAsync(Func<Task<bool>> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!await condition().ConfigureAwait(false))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("The historian capture queue did not publish the expected samples.");
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
        }
    }
}
