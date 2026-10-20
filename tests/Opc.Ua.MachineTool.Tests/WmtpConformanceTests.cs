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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Model = Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies per-instance conformance computation and the real ServerProfileArray publication lifecycle.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpConformanceTests
    {
        /// <summary>
        /// Namespace loading and pending/cancelled setup do not advertise any WMTP instance capability.
        /// </summary>
        [Test]
        public async Task WmtpNamespaceAndUnpublishedBuildDoNotAdvertiseCapabilitiesAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            Assert.That(fixture.Context.Context.NamespaceUris.GetIndex(Model.Namespaces.WMTP), Is.GreaterThan(0));
            Assert.That(Units(fixture.Context.Manager), Is.Empty);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);

            var equipment = new WmtpTestEquipment();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            equipment.Device.Setup(provider => provider.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return equipment.Snapshot;
                });
            using var cancellation = new CancellationTokenSource();
            Task<WmtpPeripheral> building = equipment.Builder(fixture.Context).BuildAsync(cancellation.Token).AsTask();
            await entered.Task.ConfigureAwait(false);
            try
            {
                Assert.That(Units(fixture.Context.Manager), Is.Empty);
                Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
            }
            finally
            {
                cancellation.Cancel();
                release.TrySetResult(true);
            }
            await Assert.CatchAsync<OperationCanceledException>(async () => await building.ConfigureAwait(false));
            Assert.That(Units(fixture.Context.Manager), Is.Empty);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
        }

        /// <summary>
        /// A mandatory-only peripheral contributes exactly Basic and automatically withdraws after disposal.
        /// </summary>
        [Test]
        public async Task MinimalPeripheralPublishesOnlyBasicAndWithdrawsOnDisposalAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            WmtpPeripheral peripheral = await equipment.Builder(fixture.Context).BuildAsync().ConfigureAwait(false);
            await using (peripheral.ConfigureAwait(false))
            {
                Assert.That(Units(peripheral), Is.EqualTo(s_basicUnits));
                Assert.That(peripheral.ServerProfiles.ToArray(), Is.EqualTo(s_basicProfiles));
                (string[] units, string[] profiles) = await connection.ReadAsync().ConfigureAwait(false);
                Assert.That(units, Is.EqualTo(s_basicUnits));
                Assert.That(profiles, Is.EqualTo(s_basicProfiles));
                Assert.That(peripheral.ConformanceUnits[0].NamespaceIndex,
                    Is.EqualTo(fixture.Context.Context.NamespaceUris.GetIndex(Model.Namespaces.WMTP)));
            }
            Assert.That(peripheral.ConformanceUnits.IsEmpty, Is.True);
            Assert.That(peripheral.ServerProfiles.IsEmpty, Is.True);
            Assert.That(Units(fixture.Context.Manager), Is.Empty);
            (string[] removedUnits, string[] removedProfiles) = await connection.ReadAsync().ConfigureAwait(false);
            Assert.That(removedUnits, Is.Empty);
            Assert.That(removedProfiles, Is.Empty);
        }

        /// <summary>
        /// A fully wired peripheral advertises exactly all 33 units and the 13 normative facets, never secure variants.
        /// </summary>
        [TestCase(MachineToolParts.Wmtp, false)]
        [TestCase(MachineToolParts.All, true)]
        public async Task FullPeripheralAdvertisesExactReleasedUnitsAndFacetUrisAsync(
            MachineToolParts parts, bool reordered)
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync(parts, reordered).ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            await using var work = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using var service = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            WmtpPeripheralBuilder builder = equipment.Builder(fixture.Context)
                .WithFeatures(WmtpFeatures.All).WithWorkRecords(work, WmtpRecordFeatures.All)
                .WithServiceRecords(service, WmtpRecordFeatures.All);
            for (uint kind = 0; kind < 8; kind++)
            {
                WmtpMeasurementDefinition definition =
                    WmtpTestEquipment.Definition((WmtpMeasurementKind)kind, WmtpMeasurementFeatures.All);
                equipment.Samples[definition.Name] = equipment.Sample(Variant.From(5d));
                builder.AddMeasurement(definition);
            }
            await using WmtpPeripheral peripheral = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(Units(peripheral), Is.EquivalentTo(ExpectedUnits()));
            Assert.That(peripheral.ConformanceUnits.Count, Is.EqualTo(33));
            Assert.That(peripheral.ServerProfiles.ToArray(), Is.EquivalentTo(ExpectedProfiles()));
            Assert.That(peripheral.ServerProfiles.Count, Is.EqualTo(13));
            Assert.That(Units(fixture.Context.Manager), Is.EquivalentTo(ExpectedUnits()));
            (string[] units, string[] profiles) = await connection.ReadAsync().ConfigureAwait(false);
            Assert.That(units, Is.EquivalentTo(ExpectedUnits()));
            Assert.That(profiles, Is.EquivalentTo(ExpectedProfiles()));
            equipment.Samples["Temperature"] = new WmtpMeasurementSample
            {
                Value = new DataValue(Variant.Null, StatusCodes.BadNoCommunication)
            };
            await peripheral.RefreshAsync().ConfigureAwait(false);
            Assert.That(peripheral.ServerProfiles.ToArray(), Is.EquivalentTo(ExpectedProfiles()));
            Assert.That(Units(peripheral), Is.EquivalentTo(ExpectedUnits()));
        }

        /// <summary>
        /// Separate partial instances cannot collectively invent an advanced CU, and disposal preserves the survivor.
        /// </summary>
        [Test]
        public async Task TwoPeripheralContributionsRemainIsolatedAndDeduplicatedAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var firstEquipment = new WmtpTestEquipment();
            var secondEquipment = new WmtpTestEquipment();
            firstEquipment.Samples["Temperature"] = firstEquipment.Sample(Variant.From(5d));
            secondEquipment.Samples["Force"] = secondEquipment.Sample(Variant.From(6d));
            await using var firstStore = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using var secondStore = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            WmtpPeripheral first = await firstEquipment.Builder(fixture.Context, "First")
                .AddMeasurement(WmtpTestEquipment.Definition())
                .WithWorkRecords(firstStore, WmtpRecordFeatures.DeleteAll | WmtpRecordFeatures.ReportIndex)
                .BuildAsync().ConfigureAwait(false);
            await using WmtpPeripheral second = await secondEquipment.Builder(fixture.Context, "Second")
                .AddMeasurement(WmtpTestEquipment.Definition(WmtpMeasurementKind.Force))
                .WithWorkRecords(secondStore, WmtpRecordFeatures.DeleteIndex | WmtpRecordFeatures.DeleteTime |
                    WmtpRecordFeatures.ReportTime | WmtpRecordFeatures.ReportFirst | WmtpRecordFeatures.ReportLast)
                .BuildAsync().ConfigureAwait(false);
            await using (first.ConfigureAwait(false))
            {
                (string[] units, string[] profiles) = await connection.ReadAsync().ConfigureAwait(false);
                Assert.That(profiles, Is.EquivalentTo(new[]
                {
                    kPrefix + "WMTPBasicServerFacet", kPrefix + "WMTPTemperatureFacet",
                    kPrefix + "WMTPForceFacet", kPrefix + "WMTPWorkCycleDataFacet"
                }));
                Assert.That(units, Does.Contain("WMTP WorkCycleData Deleting All"));
                Assert.That(units, Does.Contain("WMTP WorkCycleData CombinedReport Index"));
                Assert.That(units, Does.Contain("WMTP WorkCycleData CombinedReport Time"));
                Assert.That(units, Does.Not.Contain("WMTP WorkCycleData Deleting Advanced"));
                Assert.That(units, Does.Not.Contain("WMTP WorkCycleData CombinedReport Advanced"));
                Assert.That(units.Count(name => name == "WMTP Basic"), Is.EqualTo(1));
            }
            (string[] remainingUnits, string[] remainingProfiles) = await connection.ReadAsync().ConfigureAwait(false);
            Assert.That(remainingProfiles, Is.EquivalentTo(new[]
            {
                kPrefix + "WMTPBasicServerFacet", kPrefix + "WMTPForceFacet", kPrefix + "WMTPWorkCycleDataFacet"
            }));
            Assert.That(remainingUnits, Does.Not.Contain("WMTP Temperature Measurement"));
            Assert.That(remainingUnits, Does.Not.Contain("WMTP WorkCycleData Deleting All"));
            Assert.That(remainingUnits, Does.Not.Contain("WMTP WorkCycleData CombinedReport Index"));
            Assert.That(Units(second), Does.Contain("WMTP Force Measurement"));
            await second.DisposeAsync().ConfigureAwait(false);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
        }

        /// <summary>
        /// Raw flags and namespace presence cannot retain a CU after its actual node or handler disappears.
        /// </summary>
        [Test]
        public async Task MissingNodesAndHandlersWithdrawOnlyTheAffectedCapabilitiesAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.FullSnapshot();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(5d));
            await using var work = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .WithFeatures(WmtpFeatures.Firmware | WmtpFeatures.License | WmtpFeatures.AdvancedServiceInformation)
                .AddMeasurement(WmtpTestEquipment.Definition(features: WmtpMeasurementFeatures.All))
                .WithWorkRecords(work, WmtpRecordFeatures.All).BuildAsync().ConfigureAwait(false);
            WMTPMeasurementState measurement = peripheral.Measurements[0].State;
            var firmware = peripheral.Nodes.UpdateFirmware!.OnCallAsync;
            peripheral.Nodes.UpdateFirmware.OnCallAsync = null;
            Assert.That(Units(peripheral), Does.Not.Contain("WMTP Firmware Services"));
            Assert.That(Units(peripheral), Does.Contain("WMTP License Services"));
            peripheral.Nodes.UpdateFirmware.OnCallAsync = firmware;
            var report = peripheral.State.WMTPWorkCycleData!.CombinedReportAll!.OnCallAsync;
            peripheral.State.WMTPWorkCycleData.CombinedReportAll.OnCallAsync = null;
            Assert.That(Units(peripheral).Any(unit => unit.StartsWith("WMTP WorkCycleData", StringComparison.Ordinal)),
                Is.False);
            Assert.That(peripheral.ServerProfiles.ToArray(), Does.Not.Contain(kPrefix + "WMTPWorkCycleDataFacet"));
            peripheral.State.WMTPWorkCycleData.CombinedReportAll.OnCallAsync = report;
            var deletion = peripheral.State.WMTPWorkCycleData.DeleteStoredRecordsTime!.OnCallAsync;
            peripheral.State.WMTPWorkCycleData.DeleteStoredRecordsTime.OnCallAsync = null;
            Assert.That(Units(peripheral), Does.Not.Contain("WMTP WorkCycleData Deleting Advanced"));
            Assert.That(Units(peripheral), Does.Contain("WMTP WorkCycleData Deleting All"));
            peripheral.State.WMTPWorkCycleData.DeleteStoredRecordsTime.OnCallAsync = deletion;
            var trigger = measurement.SetTriggerSettings!.OnCallAsync;
            measurement.SetTriggerSettings.OnCallAsync = null;
            Assert.That(Units(peripheral), Does.Not.Contain("WMTP Temperature Measurement"));
            Assert.That(Units(peripheral), Does.Not.Contain("WMTP Measurement LimitCounters"));
            Assert.That(peripheral.ServerProfiles.ToArray(), Does.Not.Contain(kPrefix + "WMTPTemperatureFacet"));
            measurement.SetTriggerSettings.OnCallAsync = trigger;
            PropertyState<uint>? index = measurement.Index;
            measurement.Index = null;
            Assert.That(Units(peripheral), Does.Not.Contain("WMTP Measurement Information for WorkCycleData"));
            Assert.That(Units(peripheral), Does.Contain("WMTP Temperature Measurement"));
            measurement.Index = index;
            var lifetimeStart = peripheral.Nodes.NextService!.StartValue;
            peripheral.Nodes.NextService.StartValue = null;
            Assert.That(peripheral.ServerProfiles.ToArray(),
                Does.Not.Contain(kPrefix + "WMTPServiceAndLifeCycleAdvancedFacet"));
            Assert.That(peripheral.ServerProfiles.ToArray(),
                Does.Contain(kPrefix + "WMTPServiceAndLifeCycleBasicFacet"));
            peripheral.Nodes.NextService.StartValue = lifetimeStart;
            string serial = peripheral.Nodes.Identification.SerialNumber!.Value;
            peripheral.Nodes.Identification.SerialNumber.Value = string.Empty;
            await PublishAsync(fixture).ConfigureAwait(false);
            Assert.That(Units(peripheral), Is.Empty);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
            peripheral.Nodes.Identification.SerialNumber.Value = serial;
            await PublishAsync(fixture).ConfigureAwait(false);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Does.Contain(
                kPrefix + "WMTPServiceAndLifeCycleAdvancedFacet"));
        }

        /// <summary>
        /// Missing providers and failure after graph publication but before final commitment never advertise WMTP.
        /// </summary>
        [Test]
        public async Task FailedProviderSelectionAndLateSetupDoNotPublishFacetsAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Context.AddWirelessPeripheral("MissingProvider").WithProvider(equipment.Device.Object)
                    .WithFeatures(WmtpFeatures.Firmware).BuildAsync().ConfigureAwait(false));
            Assert.That(Units(fixture.Context.Manager), Is.Empty);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(5d));
            bool observedPublishedGraph = false;
            var clock = new ThrowingTimerClock(() =>
            {
                BaseInstanceState? root = fixture.Context.MachinesFolder.FindChild(fixture.Context.Context,
                    new QualifiedName("LateFailure", fixture.Context.InstanceNamespaceIndex));
                observedPublishedGraph = root != null &&
                    ReferenceEquals(fixture.Context.Manager.FindPredefinedNode(root.NodeId), root);
                Assert.That(Units(fixture.Context.Manager), Is.Empty);
            });
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await equipment.Builder(fixture.Context, "LateFailure").WithClock(clock)
                    .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false));
            Assert.That(observedPublishedGraph, Is.True);
            Assert.That(Units(fixture.Context.Manager), Is.Empty);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
            await using WmtpPeripheral retried = await equipment.Builder(fixture.Context, "LateFailure")
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles,
                Does.Contain(kPrefix + "WMTPTemperatureFacet"));
        }

        /// <summary>
        /// Removing a runtime through its namespace owner also withdraws conformance, without explicit handle disposal.
        /// </summary>
        [Test]
        public async Task NamespaceOwnerDeletionWithdrawsPeripheralConformanceAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(fixture).ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context).BuildAsync()
                .ConfigureAwait(false);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles,
                Does.Contain(kPrefix + "WMTPBasicServerFacet"));
            await fixture.Context.Manager.DeleteNodeAsync(
                fixture.Context.Manager.SystemContext, peripheral.NodeId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(peripheral.ServerProfiles.IsEmpty, Is.True);
            Assert.That((await connection.ReadAsync().ConfigureAwait(false)).Profiles, Is.Empty);
        }

        /// <summary>
        /// Filters only WMTP contributions, preserving unrelated server/base capabilities.
        /// </summary>
        private static string[] Units(IConformanceContributor contributor)
        {
            return contributor.ConformanceUnits.ToArray()!
                .Where(unit => unit.Name?.StartsWith("WMTP ", StringComparison.Ordinal) == true)
                .Select(unit => unit.Name!).ToArray();
        }

        /// <summary>
        /// Requests a fresh shared catalog publish after a deliberate advanced-node mutation.
        /// </summary>
        private static ValueTask PublishAsync(WmtpTestFixture fixture)
        {
            return fixture.Context.Manager.Server is ServerInternalData { ConformanceUnitsManager: { } catalog }
                ? catalog.PublishAsync()
                : throw new InvalidOperationException("The fixture requires the standard conformance catalog.");
        }

        /// <summary>
        /// Provides the independent exact table 58 URI oracle from the curated first-party specification evidence.
        /// </summary>
        private static string[] ExpectedProfiles()
        {
            return
            [
                kPrefix + "WMTPBasicServerFacet",
                kPrefix + "WMTPServiceAndLifeCycleBasicFacet",
                kPrefix + "WMTPServiceAndLifeCycleAdvancedFacet",
                kPrefix + "WMTPTemperatureFacet",
                kPrefix + "WMTPRotationSpeedFacet",
                kPrefix + "WMTPHumidityFacet",
                kPrefix + "WMTPAccelerationFacet",
                kPrefix + "WMTPForceFacet",
                kPrefix + "WMTPPositionFacet",
                kPrefix + "WMTPTorqueFacet",
                kPrefix + "WMTPDimensionFacet",
                kPrefix + "WMTPWorkCycleDataFacet",
                kPrefix + "WMTPServiceCycleDataFacet"
            ];
        }

        /// <summary>
        /// Provides the independent 33-row released CU inventory.
        /// </summary>
        private static string[] ExpectedUnits()
        {
            var units = new List<string>
            {
                "WMTP Basic",
                "WMTP DeviceConfiguration Calibration Capabilities",
                "WMTP DeviceConfiguration Time Capabilities",
                "WMTP Firmware Services",
                "WMTP License Services",
                "WMTP BatteryLevel",
                "WMTP Service and LifeCycle Information Basic",
                "WMTP Service and LifeCycle Information Advanced",
                "WMTP Temperature Measurement",
                "WMTP RotationSpeed Measurement",
                "WMTP Humidity Measurement",
                "WMTP Acceleration Measurement",
                "WMTP Force Measurement",
                "WMTP Torque Measurement",
                "WMTP Position Measurement",
                "WMTP Dimension Measurement",
                "WMTP Measurement LimitCounters",
                "WMTP Measurement InternalUpdateInterval",
                "WMTP Measurement Information for WorkCycleData"
            };
            foreach (string dataset in new[] { "WMTP WorkCycleData", "WMTP ServiceCycleData" })
            {
                units.Add(dataset);
                units.Add(dataset + " Deleting All");
                units.Add(dataset + " Deleting Advanced");
                units.Add(dataset + " ReportNumberOfStoredRecordsTime");
                units.Add(dataset + " CombinedReport Index");
                units.Add(dataset + " CombinedReport Time");
                units.Add(dataset + " CombinedReport Advanced");
            }
            return units.ToArray();
        }

        /// <summary>
        /// Owns a real client connection and reads the standard diagnostics variables without forcing publication.
        /// </summary>
        private sealed class Connection : IAsyncDisposable
        {
            /// <summary>
            /// Creates an owned connection.
            /// </summary>
            private Connection(ClientFixture transport, Opc.Ua.Client.ISession session)
            {
                m_transport = transport;
                m_session = session;
            }

            /// <summary>
            /// Opens the actual fixture endpoint.
            /// </summary>
            internal static async ValueTask<Connection> OpenAsync(WmtpTestFixture fixture)
            {
                var transport = new ClientFixture(NUnitTelemetryContext.Create());
                try
                {
                    await transport.LoadClientConfigurationAsync(fixture.Root, "Conformance").ConfigureAwait(false);
                    Opc.Ua.Client.ISession session = await transport.ConnectAsync(
                        fixture.Endpoint, SecurityPolicies.None).ConfigureAwait(false);
                    return new Connection(transport, session);
                }
                catch
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            /// <summary>
            /// Reads the actual wire arrays and filters only WMTP entries.
            /// </summary>
            internal async ValueTask<(string[] Units, string[] Profiles)> ReadAsync()
            {
                ReadResponse response = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId
                        {
                            NodeId = Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits,
                            AttributeId = Attributes.Value
                        },
                        new ReadValueId
                        {
                            NodeId = Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray,
                            AttributeId = Attributes.Value
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out ArrayOf<string> profiles), Is.True);
                return (units.ToArray()!.Where(unit =>
                        unit.Name?.StartsWith("WMTP ", StringComparison.Ordinal) == true)
                    .Select(unit => unit.Name!).ToArray(),
                    profiles.ToArray()!.Where(profile => profile.StartsWith(kPrefix, StringComparison.Ordinal))
                        .ToArray());
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    m_session.Dispose();
                    await m_transport.DisposeAsync().ConfigureAwait(false);
                }
            }

            /// <summary>The real transport fixture.</summary>
            private readonly ClientFixture m_transport;

            /// <summary>The actual connected session.</summary>
            private readonly Opc.Ua.Client.ISession m_session;
        }

        /// <summary>
        /// Fails the final timer initialization after the root has been indexed but before WMTP commitment.
        /// </summary>
        private sealed class ThrowingTimerClock : TimeProvider
        {
            /// <summary>
            /// Creates the failure point.
            /// </summary>
            internal ThrowingTimerClock(Action observe)
            {
                m_observe = observe;
            }

            /// <inheritdoc/>
            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                m_observe();
                throw new InvalidOperationException("Injected timer initialization failure.");
            }

            /// <summary>The pre-failure observation.</summary>
            private readonly Action m_observe;
        }

        /// <summary>The normative WMTP URI prefix; the released specification has no secure variants.</summary>
        private const string kPrefix = "http://opcfoundation.org/UA-Profile/WMTP/Server/";

        /// <summary>The exact mandatory-only CU set.</summary>
        private static readonly string[] s_basicUnits = ["WMTP Basic"];

        /// <summary>The exact mandatory-only standard facet set.</summary>
        private static readonly string[] s_basicProfiles = [kPrefix + "WMTPBasicServerFacet"];
    }
}
