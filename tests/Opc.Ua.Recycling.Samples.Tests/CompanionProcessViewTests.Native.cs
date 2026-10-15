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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using NUnit.Framework;
using Microsoft.Extensions.Hosting;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Extrusion;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Filter;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.Pelletizer;
using Opc.Ua.GranulateDrying;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Recycling.Samples.Tests
{
    public sealed partial class CompanionProcessViewTests
    {
        /// <summary>
        /// Reuses the managed protocol assertions against the actually published NativeAOT equipment graph.
        /// </summary>
        internal static async Task AssertNativeProfileAsync(
            ManagedSession session,
            RecyclingProcess process,
            string lineId,
            CommittedSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            int index = process switch
            {
                RecyclingProcess.Drying => 14,
                RecyclingProcess.Extrusion => 16,
                RecyclingProcess.MeltFiltration => 17,
                RecyclingProcess.Pelletizing => 18,
                _ => -1
            };
            if (index < 0)
            {
                return;
            }
            RegisterNativeModelTypes(session);
            NodeId view = NodeId.Create(
                Model.Objects.ProcessView, RecyclingModelMapping.NamespaceUri, session.NamespaceUris);
            StageSnapshot stage = snapshot.Stages[index];
            ArrayOf<ReferenceDescription> stages = await ChildrenAsync(session, view, cancellationToken)
                .ConfigureAwait(false);
            NodeId stageId = ExpandedNodeId.ToNodeId(stages.Span.ToArray().Single(
                item => item.BrowseName.Name == stage.Name).NodeId, session.NamespaceUris);
            NodeId equipmentId = await ChildAsync(
                session, stageId, "Equipment", RecyclingModelMapping.NamespaceUri, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<ReferenceDescription> equipment = await ChildrenAsync(session, equipmentId, cancellationToken)
                .ConfigureAwait(false);
            var amb = new AmbClient(session, session.MessageContext.Telemetry);
            ArrayOf<AssetAlias> assets = await amb.FindAssetsAsync(
                "urn:recycling-sample:" + lineId + ":%", AssetAliasCategory.ByProductInstanceUri, cancellationToken)
                .ConfigureAwait(false);
            await AssertNativeProcessAsync(session, stageId, stage, snapshot, equipment, assets, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Genuine native variables notify from commits and retain absolute values and source time across recovery.
        /// </summary>
        [TestCase(RecyclingProcess.Drying, "desiccant-thermal-drying", 14)]
        [TestCase(RecyclingProcess.Extrusion, "extrusion", 16)]
        [TestCase(RecyclingProcess.MeltFiltration, "melt-filtration", 17)]
        [TestCase(RecyclingProcess.Pelletizing, "pelletizing", 18)]
        public async Task NativeProcessValuesSubscribeAndRecoverWithoutReplay(
            RecyclingProcess process, string stageName, int stageIndex)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct)
                .ConfigureAwait(false);
            (IHost host, ManagedSession session, NodeId view, NodeId sampleStage) =
                await StartTelemetryViewAsync(environment, process, stageName, ct).ConfigureAwait(false);
            NodeId equipmentId = await ChildAsync(
                session, sampleStage, "Equipment", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> equipment = await ChildrenAsync(session, equipmentId, ct)
                .ConfigureAwait(false);
            ExpandedNodeId type = stageIndex switch
            {
                14 => GranulateDrying.ObjectTypeIds.GranulateDryingDeviceType,
                16 => Extrusion.Extruder.ObjectTypeIds.Extruder_InterfaceType,
                17 => Extrusion.Filter.ObjectTypeIds.Filter_InterfaceType,
                _ => Extrusion.Pelletizer.ObjectTypeIds.Pelletizer_InterfaceType
            };
            string path = stageIndex switch
            {
                14 => "Dryer/Bins/Bin_001/AirFlow/ActualValue",
                16 => "MeltPressureZones/MeltPressureZone_001/ActualValue",
                17 => "FilterPackage_001/Status",
                _ => "Drive/Speed/ActualValue"
            };
            NodeId device = DeviceId(equipment, type, session);
            NodeId value = await NativePathAsync(session, device, path, ct).ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> changes = ObserveSignal(session, value, ct);
            Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);
            CommittedSnapshot active;
            do
            {
                active = await AdvanceTelemetryAsync(
                    environment, session, view, environment.Coordinator.Current.Tick + 1, ct).ConfigureAwait(false);
                Assert.That(active.Tick, Is.LessThan(70), "This configured process must execute.");
            }
            while (active.Stages[stageIndex].State != EquipmentState.Executing);
            DataValue changed = await NextSignalAsync(changes, active.SourceTimestampUtc, ct).ConfigureAwait(false);
            if (stageIndex == 17)
            {
                Assert.That(changed.WrappedValue.TryGetValue(out FilterPackageStatusEnumeration status), Is.True);
                Assert.That(status, Is.EqualTo(FilterPackageStatusEnumeration.ACTIVE));
            }
            else
            {
                Assert.That(changed.WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(stageIndex == 14 ? 60 : stageIndex == 16 ? 120 : 300));
            }
            CommittedSnapshot final = await AdvanceTelemetryAsync(environment, session, view, 75, ct)
                .ConfigureAwait(false);
            await NextSignalAsync(changes, final.SourceTimestampUtc, ct).ConfigureAwait(false);
            await changes.DisposeAsync().ConfigureAwait(false);
            ArrayOf<NodeId> retained = stageIndex == 14
                ? [value, await NativePathAsync(session, device, "Operation/TotalEnergy/PowerConsumption", ct)
                    .ConfigureAwait(false)]
                : [value];
            await AssertTelemetryReconnectAsync(environment, host, session, view, retained, ct).ConfigureAwait(false);
        }

        private static void RegisterNativeModelTypes(ManagedSession session)
        {
            session.MessageContext.Factory.Builder.AddOpcUaGranulateDrying().AddOpcUaPlasticsRubber()
                .AddOpcUaExtrusion().AddOpcUaExtrusionLine().AddOpcUaExtrusionExtruder()
                .AddOpcUaExtrusionFilter().Commit();
            if (!ReferenceEquals(session.Factory, session.MessageContext.Factory))
            {
                session.Factory.Builder.AddOpcUaGranulateDrying().AddOpcUaPlasticsRubber()
                    .AddOpcUaExtrusion().AddOpcUaExtrusionLine().AddOpcUaExtrusionExtruder()
                    .AddOpcUaExtrusionFilter().Commit();
            }
        }

        private static async Task AssertNativeProcessAsync(
            ManagedSession session,
            NodeId sampleStage,
            StageSnapshot stage,
            CommittedSnapshot snapshot,
            ArrayOf<ReferenceDescription> equipment,
            ArrayOf<AssetAlias> assets,
            CancellationToken ct)
        {
            if (stage.Index is not (14 or 16 or 17 or 18))
            {
                return;
            }
            Assert.That(equipment.Count, Is.EqualTo(stage.Index == 16 ? 2 : 1));
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, session.NamespaceUris);
            ArrayOf<ReferenceDescription> discovered = await ChildrenAsync(session, machines, ct).ConfigureAwait(false);
            foreach (ReferenceDescription device in equipment.Span.ToArray())
            {
                NodeId id = ExpandedNodeId.ToNodeId(device.NodeId, session.NamespaceUris);
                Assert.That(id.IdType, Is.EqualTo(IdType.String));
                Assert.That(discovered.Contains(item =>
                    ExpandedNodeId.ToNodeId(item.NodeId, session.NamespaceUris) == id), Is.True);
                Assert.That(ExpandedNodeId.ToNodeId(device.TypeDefinition, session.NamespaceUris),
                    Is.Not.EqualTo(ExpandedNodeId.ToNodeId(
                        Extrusion.ObjectTypeIds.ExtrusionDeviceType, session.NamespaceUris)));
            }
            if (stage.Index == 14)
            {
                NodeId dryer = DeviceId(equipment, GranulateDrying.ObjectTypeIds.GranulateDryingDeviceType, session);
                NodeId bin = await NativePathAsync(session, dryer, "Dryer/Bins/Bin_001", ct).ConfigureAwait(false);
                NodeId bed = await NativePathAsync(session, dryer, "Dryer/DesiccantBeds/DesiccantBed_001", ct)
                    .ConfigureAwait(false);
                await AssertNativeTypeAsync(session, bin, GranulateDrying.ObjectTypeIds.BinType, ct)
                    .ConfigureAwait(false);
                await AssertNativeTypeAsync(session, bed, GranulateDrying.ObjectTypeIds.DesiccantBedType, ct)
                    .ConfigureAwait(false);
                Assert.That(assets.Contains(asset => asset.ReferencedNodes.Contains(
                    target => ExpandedNodeId.ToNodeId(target, session.NamespaceUris) == bin)), Is.True);
                Assert.That(assets.Contains(asset => asset.ReferencedNodes.Contains(
                    target => ExpandedNodeId.ToNodeId(target, session.NamespaceUris) == bed)), Is.True);
                bool powered = stage.ElectricalDemandWatts > 0 && stage.State is not (
                    EquipmentState.Unavailable or EquipmentState.Stopped or EquipmentState.ElectricalConstraint or
                    EquipmentState.ThermalConstraint);
                await AssertNativeNumberAsync(session, bin, "AirFlow/ActualValue",
                    powered ? 60 : 0, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, bed, "AirFlow/ActualValue",
                    powered ? 60 / 3.6 : 0, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, bin, "DewPoint",
                    powered ? -40 : 10, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, bed, "DewPoint/ActualValue",
                    powered ? -40 : 10, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, bin, "MaterialDryingTime",
                    120000, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, dryer, "Operation/TotalEnergy/PowerConsumption",
                    stage.ElectricalJoules / 3_600_000.0, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                DataValue remote = await ReadNativePathAsync(session, dryer, "Operation/RemoteControlOpcUa", ct)
                    .ConfigureAwait(false);
                Assert.That(remote.WrappedValue.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.False);
                await AssertNativeUnitAsync(session, bin, "AirFlow/ActualValue", "MQH", ct).ConfigureAwait(false);
                await AssertNativeUnitAsync(session, bed, "AirFlow/ActualValue", "G51", ct).ConfigureAwait(false);
            }
            else if (stage.Index == 16)
            {
                NodeId extruder = DeviceId(equipment, Extrusion.Extruder.ObjectTypeIds.Extruder_InterfaceType, session);
                NodeId line = DeviceId(equipment, Extrusion.Line.ObjectTypeIds.ExtrusionLine_InterfaceType, session);
                ArrayOf<ReferenceDescription> lineMembers = await ChildrenAsync(session, line, ct)
                    .ConfigureAwait(false);
                Assert.That(lineMembers.Contains(item =>
                    ExpandedNodeId.ToNodeId(item.NodeId, session.NamespaceUris) == extruder), Is.True);
                DataValue sampleTemperature = await ReadChildAsync(
                    session, sampleStage, "MeltTempC", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(sampleTemperature.WrappedValue.TryGetValue(out double temperature), Is.True);
                DataValue samplePressure = await ReadChildAsync(
                    session, sampleStage, "MeltPressureBar", RecyclingModelMapping.NamespaceUri, ct)
                    .ConfigureAwait(false);
                Assert.That(samplePressure.WrappedValue.TryGetValue(out double pressure), Is.True);
                await AssertNativeNumberAsync(
                    session, extruder, "MeltTemperatureZones/MeltTemperatureZone_001/ActualValue",
                    temperature, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, extruder, "MeltPressureZones/MeltPressureZone_001/ActualValue",
                    pressure, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                double flow = stage.Transferred.Total * 3600.0 / (1_000_000 * snapshot.LogicalSeconds);
                await AssertNativeNumberAsync(session, extruder, "Throughput/ActualValue",
                    flow, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeNumberAsync(session, line, "ProductionParameters/Throughput",
                    flow, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                await AssertNativeUnitAsync(
                    session, extruder, "MeltPressureZones/MeltPressureZone_001/ActualValue", "BAR", ct)
                    .ConfigureAwait(false);
            }
            else if (stage.Index == 17)
            {
                NodeId filter = DeviceId(equipment, Extrusion.Filter.ObjectTypeIds.Filter_InterfaceType, session);
                NodeId package = await NativePathAsync(session, filter, "FilterPackage_001", ct).ConfigureAwait(false);
                NodeId screen = await NativePathAsync(session, package, "ScreenPackage_001", ct).ConfigureAwait(false);
                await AssertNativeTypeAsync(session, package, Extrusion.Filter.ObjectTypeIds.FilterPackageType, ct)
                    .ConfigureAwait(false);
                await AssertNativeTypeAsync(session, screen, Extrusion.Filter.ObjectTypeIds.ScreenPackageType, ct)
                    .ConfigureAwait(false);
                await AssertNativeNumberAsync(session, filter, "FiltrationFineness",
                    100, snapshot.SourceTimestampUtc, ct).ConfigureAwait(false);
                DataValue status = await ReadNativePathAsync(session, package, "Status", ct).ConfigureAwait(false);
                Assert.That(status.WrappedValue.TryGetValue(out FilterPackageStatusEnumeration packageStatus), Is.True);
                double loading = (stage.Rejected.Labels + stage.Rejected.Dirt) / 1_000_000.0;
                Assert.That(packageStatus, Is.EqualTo(loading >= 1
                    ? FilterPackageStatusEnumeration.CHANGE_REQUIRED
                    : stage.State == EquipmentState.Executing
                        ? FilterPackageStatusEnumeration.ACTIVE : FilterPackageStatusEnumeration.NOT_ACTIVE));
                ArrayOf<ReferenceDescription> screenMembers = await ChildrenAsync(session, screen, ct)
                    .ConfigureAwait(false);
                Assert.That(screenMembers.Contains(item => item.BrowseName.Name == "DiffPressureBar"), Is.False,
                    "The standard ScreenPackage has no differential-pressure member; the sample owns that signal.");
            }
            else
            {
                NodeId pelletizer = DeviceId(
                    equipment, Extrusion.Pelletizer.ObjectTypeIds.Pelletizer_InterfaceType, session);
                await AssertNativeNumberAsync(session, pelletizer, "Drive/Speed/ActualValue",
                    stage.State == EquipmentState.Executing ? 300 : 0, snapshot.SourceTimestampUtc, ct)
                    .ConfigureAwait(false);
                await AssertNativeUnitAsync(session, pelletizer, "Drive/Speed/ActualValue", "RPM", ct)
                    .ConfigureAwait(false);
                await AssertNativeTypeAsync(session,
                    await NativePathAsync(session, pelletizer, "KnifePackage_001", ct).ConfigureAwait(false),
                    Extrusion.Pelletizer.ObjectTypeIds.KnifePackageType, ct).ConfigureAwait(false);
                await AssertNativeTypeAsync(session,
                    await NativePathAsync(session, pelletizer, "DiePlate_001", ct).ConfigureAwait(false),
                    Extrusion.Pelletizer.ObjectTypeIds.DiePlateType, ct).ConfigureAwait(false);
            }
        }

        private static NodeId DeviceId(
            ArrayOf<ReferenceDescription> devices, ExpandedNodeId type, ManagedSession session)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(type, session.NamespaceUris);
            ReferenceDescription device = devices.Span.ToArray().Single(item =>
                ExpandedNodeId.ToNodeId(item.TypeDefinition, session.NamespaceUris) == expected);
            return ExpandedNodeId.ToNodeId(device.NodeId, session.NamespaceUris);
        }

        private static async Task<NodeId> NativePathAsync(
            ManagedSession session, NodeId parent, string path, CancellationToken ct)
        {
            foreach (string segment in path.Split('/'))
            {
                ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, parent, ct).ConfigureAwait(false);
                ReferenceDescription child = children.Span.ToArray().Single(item => item.BrowseName.Name == segment);
                parent = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
            }
            return parent;
        }

        private static async Task<DataValue> ReadNativePathAsync(
            ManagedSession session, NodeId parent, string path, CancellationToken ct)
        {
            NodeId node = await NativePathAsync(session, parent, path, ct).ConfigureAwait(false);
            return (await ReadSignalsAsync(session, [node], ct).ConfigureAwait(false))[0];
        }

        private static async Task AssertNativeTypeAsync(
            ManagedSession session, NodeId node, ExpandedNodeId type, CancellationToken ct)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                BrowseDirection = BrowseDirection.Forward,
                IncludeSubtypes = false
            });
            ArrayOf<ReferenceDescription> definitions = await browser.BrowseAsync(node, ct).ConfigureAwait(false);
            Assert.That(definitions.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(definitions[0].NodeId, session.NamespaceUris),
                Is.EqualTo(ExpandedNodeId.ToNodeId(type, session.NamespaceUris)));
        }

        private static async Task AssertNativeNumberAsync(
            ManagedSession session,
            NodeId parent,
            string path,
            double expected,
            DateTime timestamp,
            CancellationToken ct)
        {
            DataValue value = await ReadNativePathAsync(session, parent, path, ct).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), path);
            Assert.That(value.WrappedValue.TryGetValue(out double number), Is.True, path);
            Assert.That(number, Is.EqualTo(expected).Within(0.000001), path);
            Assert.That(value.SourceTimestamp.ToDateTime(), Is.EqualTo(timestamp), path);
        }

        private static async Task AssertNativeUnitAsync(
            ManagedSession session, NodeId parent, string path, string code, CancellationToken ct)
        {
            DataValue metadata = await ReadNativePathAsync(session, parent, path + "/EngineeringUnits", ct)
                .ConfigureAwait(false);
            Assert.That(metadata.WrappedValue.TryGetValue<EUInformation>(
                out EUInformation? unit, session.MessageContext), Is.True, path);
            Assert.That(unit!.UnitId, Is.EqualTo(Scales.Server.Runtime.ScaleUnits.UnitIdOf(code)), path);
        }
    }
}
