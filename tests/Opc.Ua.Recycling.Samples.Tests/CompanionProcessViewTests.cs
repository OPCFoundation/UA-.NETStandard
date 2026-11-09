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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.PackML;
using Opc.Ua.Recycling.Model;
using Opc.Ua.Scales;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Public UA browse/read/subscription seams for the default adapter, with actual companion runtimes.
    /// These are binding tests against coordinator commits, not a second simulation oracle.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed partial class CompanionProcessViewTests
    {
        /// <summary>
        /// Every circulation and drain pump exposes readable mandatory two-state status labels.
        /// </summary>
        [TestCase(RecyclingProcess.Washing, 8)]
        [TestCase(RecyclingProcess.DensitySeparation, 1)]
        public async Task PumpStatusExposesMandatoryStateLabels(RecyclingProcess process, int expectedPumps)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct)
                .ConfigureAwait(false);
            IHost host = await environment.StartViewAsync(process, null, ct).ConfigureAwait(false);
            await host.Services.GetRequiredService<RecyclingRuntime>().AddressSpaceReady.WaitAsync(ct)
                .ConfigureAwait(false);
            await WaitForInitializationAsync(environment, process, ct).ConfigureAwait(false);
            string endpoint = host.Services.GetRequiredService<RecyclingHostOptions>().EndpointUrl;
            RecyclingCoordinatorClient connection = await environment.ConnectAsync("observer", ct, endpoint)
                .ConfigureAwait(false);
            ManagedSession session = connection.Session;
            ushort sample = checked((ushort)session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
            ArrayOf<ReferenceDescription> stages = await ChildrenAsync(
                session, new NodeId(Model.Objects.ProcessView, sample), ct).ConfigureAwait(false);
            string[] stateNames = [BrowseNames.FalseState, BrowseNames.TrueState];
            int pumps = 0;
            foreach (StageRecipe recipe in host.Services.GetRequiredService<RecyclingHostOptions>()
                .Simulation.Stages.Span.ToArray().Where(recipe => recipe.Process == process))
            {
                ReferenceDescription stage = stages.Span.ToArray().Single(item => item.BrowseName.Name == recipe.Name);
                NodeId equipment = await ChildAsync(session,
                    ExpandedNodeId.ToNodeId(stage.NodeId, session.NamespaceUris),
                    "Equipment", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                ArrayOf<ReferenceDescription> instruments = await ChildrenAsync(session, equipment, ct)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription pump in instruments.Span.ToArray())
                {
                    NodeId operational = await ChildAsync(session,
                        ExpandedNodeId.ToNodeId(pump.NodeId, session.NamespaceUris),
                        "Operational", Di.Namespaces.OpcUaDi, ct).ConfigureAwait(false);
                    NodeId actuation = await ChildAsync(
                        session, operational, "PumpActuation", Pumps.Namespaces.Pumps, ct).ConfigureAwait(false);
                    NodeId status = await ChildAsync(
                        session, actuation, "Status", Pumps.Namespaces.Pumps, ct).ConfigureAwait(false);
                    ReadResponse attributes = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = status, AttributeId = Attributes.ValueRank }], ct)
                        .ConfigureAwait(false);
                    Assert.That(attributes.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(attributes.Results[0].WrappedValue.TryGetValue(out int valueRank), Is.True);
                    Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar), pump.BrowseName.ToString());
                    ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, status, ct)
                        .ConfigureAwait(false);
                    foreach (string name in stateNames)
                    {
                        ReferenceDescription? property = children.Span.ToArray().SingleOrDefault(
                            child => child.BrowseName == new QualifiedName(name, 0));
                        Assert.That(property, Is.Not.Null, $"{pump.BrowseName}/Status/{name}");
                        Assert.That(property!.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
                        Assert.That(property.TypeDefinition,
                            Is.EqualTo(new ExpandedNodeId(VariableTypeIds.PropertyType)));
                        DataValue value = await session.ReadValueAsync(
                            ExpandedNodeId.ToNodeId(property.NodeId, session.NamespaceUris), ct).ConfigureAwait(false);
                        Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(value.WrappedValue.TryGetValue(out LocalizedText label), Is.True);
                        Assert.That(label.Text, Is.EqualTo(name == BrowseNames.FalseState ? "Stopped" : "Running"));
                    }
                    pumps++;
                }
            }
            Assert.That(pumps, Is.EqualTo(expectedPumps));
        }

        /// <summary>
        /// Every process publishes declared sample types, real appropriate instruments and discoverable assets.
        /// A repeated snapshot cannot change production, and a real coordinator outage retains source data.
        /// </summary>
        [TestCase(RecyclingProcess.Collection)]
        [TestCase(RecyclingProcess.Sorting)]
        [TestCase(RecyclingProcess.Shredding)]
        [TestCase(RecyclingProcess.Washing)]
        [TestCase(RecyclingProcess.DensitySeparation)]
        [TestCase(RecyclingProcess.Drying)]
        [TestCase(RecyclingProcess.Extrusion)]
        [TestCase(RecyclingProcess.MeltFiltration)]
        [TestCase(RecyclingProcess.Pelletizing)]
        [TestCase(RecyclingProcess.Bagging)]
        public async Task DefaultViewPublishesTypedCommittedEquipmentAndRetainsUnavailableValues(
            RecyclingProcess process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct)
                .ConfigureAwait(false);
            IHost host = await environment.StartViewAsync(process, null, ct).ConfigureAwait(false);
            RecyclingRuntime runtime = host.Services.GetRequiredService<RecyclingRuntime>();
            await runtime.AddressSpaceReady.WaitAsync(ct).ConfigureAwait(false);
            await WaitForInitializationAsync(environment, process, ct).ConfigureAwait(false);
            string endpoint = host.Services.GetRequiredService<RecyclingHostOptions>().EndpointUrl;
            RecyclingCoordinatorClient connection = await environment.ConnectAsync("observer", ct, endpoint)
                .ConfigureAwait(false);
            ManagedSession session = connection.Session;
            RegisterModelTypes(session);
            ushort sample = checked((ushort)session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
            var viewId = new NodeId(Model.Objects.ProcessView, sample);
            var view = new RecyclingProcessViewTypeClient(session, viewId, environment.Telemetry);
            ArrayOf<ReferenceDescription> stages = await ChildrenAsync(session, viewId, ct).ConfigureAwait(false);
            StageRecipe[] recipes = [.. host.Services.GetRequiredService<RecyclingHostOptions>()
                .Simulation.Stages.Span.ToArray()
                .Where(recipe => recipe.Process == process)];
            var stageIds = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            foreach (StageRecipe recipe in recipes)
            {
                ReferenceDescription stage = stages.Span.ToArray().Single(item => item.BrowseName.Name == recipe.Name);
                stageIds.Add(recipe.Name, ExpandedNodeId.ToNodeId(stage.NodeId, session.NamespaceUris));
                Assert.That(stage.TypeDefinition, Is.EqualTo(
                    new ExpandedNodeId(ExpectedType(recipe.Name), sample)), recipe.Name);
                Assert.That(stageIds[recipe.Name].IdType, Is.EqualTo(IdType.String), recipe.Name);
            }
            NodeId subscribed = await ChildAsync(session, stageIds[recipes[0].Name], "Stage",
                RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> changes = session.DefaultStreaming.SubscribeDataChangesAsync(
                subscribed,
                new MonitoredItemOptions
                {
                    SamplingInterval = TimeSpan.FromMilliseconds(100),
                    QueueSize = 8,
                    DiscardOldest = true,
                    Filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp }
                }, ct).GetAsyncEnumerator(ct);
            Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);
            for (int tick = 0; tick < 75; tick++)
            {
                await environment.Coordinator.TickAsync(ct).ConfigureAwait(false);
                if (tick % 8 == 7 || tick == 74)
                {
                    DateTime committedTime = environment.Coordinator.Current.SourceTimestampUtc;
                    while (changes.Current.Value.SourceTimestamp.ToDateTime() != committedTime ||
                        StatusCode.IsBad(changes.Current.Value.StatusCode))
                    {
                        Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                    }
                }
            }
            CommittedSnapshot expected = environment.Coordinator.Current;
            while (changes.Current.Value.SourceTimestamp.ToDateTime() != expected.SourceTimestampUtc ||
                StatusCode.IsBad(changes.Current.Value.StatusCode))
            {
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            Assert.That(changes.Current.Value.WrappedValue.TryGetValue<StageDataType>(
                out StageDataType? changed, session.MessageContext), Is.True);
            Assert.That(changed!.Executions, Is.EqualTo(expected.Stages
                .Span.ToArray().Single(stage => stage.Name == recipes[0].Name).Executions));
            Assert.That((await view.ReadSnapshotAsync(ct).ConfigureAwait(false)).Tick, Is.EqualTo(75));
            DataValue completeness = await ReadChildAsync(
                session, viewId, "CompanionBindingsComplete", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            Assert.That(completeness.WrappedValue.TryGetValue(out bool complete), Is.True);
            Assert.That(complete, Is.True);

            var amb = new AmbClient(session, environment.Telemetry);
            ArrayOf<AssetAlias> assets = await amb.FindAssetsAsync(
                "urn:recycling-sample:test-line:%", AssetAliasCategory.ByProductInstanceUri, ct).ConfigureAwait(false);
            foreach (StageRecipe recipe in recipes)
            {
                NodeId stageId = stageIds[recipe.Name];
                Assert.That(assets.Contains(asset => asset.Name == "urn:recycling-sample:test-line:" + recipe.Name &&
                    asset.ReferencedNodes.Contains(
                        target => ExpandedNodeId.ToNodeId(target, session.NamespaceUris) == stageId)),
                    Is.True, recipe.Name);
                StageSnapshot committed = expected.Stages.Span.ToArray().Single(stage => stage.Name == recipe.Name);
                DataValue value = await ReadChildAsync(session, stageId, "Stage",
                    RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good), recipe.Name);
                Assert.That(value.SourceTimestamp.ToDateTime(), Is.EqualTo(expected.SourceTimestampUtc), recipe.Name);
                Assert.That(value.WrappedValue.TryGetValue<StageDataType>(
                    out StageDataType? projected, session.MessageContext), Is.True);
                Assert.That(projected!.Index, Is.EqualTo(committed.Index));
                Assert.That(projected.Transferred.NaturalPolymer, Is.EqualTo(committed.Transferred.NaturalPolymer));
                Assert.That(projected.Transferred.Water, Is.EqualTo(committed.Transferred.Water));
                Assert.That(projected.ElectricalJoules, Is.EqualTo(committed.ElectricalJoules));
                Assert.That(projected.Executions, Is.EqualTo(committed.Executions));
                await AssertMeasurementsAsync(session, stageId, recipe.Name, committed, expected, ct)
                    .ConfigureAwait(false);
                await AssertNativeEquipmentAsync(
                    session, stageId, committed, expected, assets, environment.Telemetry, ct)
                    .ConfigureAwait(false);
            }
            IRecyclingProcessViewAdapter adapter = host.Services.GetRequiredService<IRecyclingProcessViewAdapter>();
            await adapter.ApplyAsync(expected, ct).ConfigureAwait(false);
            await adapter.ApplyAsync(expected, ct).ConfigureAwait(false);
            foreach (StageRecipe recipe in recipes)
            {
                DataValue count = await ReadChildAsync(session, stageIds[recipe.Name], "Executions",
                    RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(count.WrappedValue.TryGetValue(out long executions), Is.True);
                Assert.That(executions,
                    Is.EqualTo(expected.Stages.Span.ToArray().Single(stage => stage.Name == recipe.Name).Executions));
                Assert.That(count.SourceTimestamp.ToDateTime(), Is.EqualTo(expected.SourceTimestampUtc));
            }
            Assert.That(environment.Coordinator.Current.Tick, Is.EqualTo(expected.Tick));
            Assert.That(environment.Coordinator.Current.Packages, Is.EqualTo(expected.Packages));

            if (process == RecyclingProcess.Bagging)
            {
                RecyclingCoordinatorClient operatorConnection = await environment.ConnectAsync("operator", ct, endpoint)
                    .ConfigureAwait(false);
                await AssertBaggingControlRefusalAsync(
                    operatorConnection.Session, stageIds["bagging"], ct).ConfigureAwait(false);
                DataValue before = await session.ReadValueAsync(subscribed, ct).ConfigureAwait(false);
                await environment.ServerHost.StopAsync(ct).ConfigureAwait(false);
                do
                {
                    Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                }
                while (changes.Current.Value.StatusCode != StatusCodes.UncertainLastUsableValue);
                Assert.That(changes.Current.Value.WrappedValue, Is.EqualTo(before.WrappedValue));
                Assert.That(changes.Current.Value.SourceTimestamp, Is.EqualTo(before.SourceTimestamp));
                DataValue packages = await ReadChildAsync(session, stageIds["bagging"], "PackageCount",
                    RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(packages.StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(packages.WrappedValue.TryGetValue(out long packageCount), Is.True);
                Assert.That(packageCount, Is.EqualTo(expected.Packages.Count));
                Assert.That(packages.SourceTimestamp.ToDateTime(), Is.EqualTo(expected.SourceTimestampUtc));
                DataValue available = await ReadChildAsync(session, stageIds["bagging"], "Available",
                    RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(available.WrappedValue.TryGetValue(out bool current), Is.True);
                Assert.That(current, Is.False);
                await changes.DisposeAsync().ConfigureAwait(false);
                await environment.RestartAsync(ct).ConfigureAwait(false);
                await WaitForInitializationAsync(environment, process, ct).ConfigureAwait(false);
                RecyclingCoordinatorClient reconnected = await environment.ConnectAsync("observer", ct, endpoint)
                    .ConfigureAwait(false);
                RegisterModelTypes(reconnected.Session);
                await WaitForProjectionAsync(reconnected.Session, viewId, expected.Tick, ct).ConfigureAwait(false);
                DataValue restoredCount = await ReadChildAsync(
                    reconnected.Session, stageIds["bagging"], "PackageCount", RecyclingModelMapping.NamespaceUri, ct)
                    .ConfigureAwait(false);
                Assert.That(restoredCount.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(restoredCount.WrappedValue.TryGetValue(out long restoredPackages), Is.True);
                Assert.That(restoredPackages, Is.EqualTo(expected.Packages.Count));
                Assert.That(restoredCount.SourceTimestamp.ToDateTime(), Is.EqualTo(expected.SourceTimestampUtc));
                await AssertNativeEquipmentAsync(reconnected.Session, stageIds["bagging"], expected.Stages[20],
                    expected, assets, environment.Telemetry, ct).ConfigureAwait(false);
            }
        }

        private static async Task WaitForProjectionAsync(
            ManagedSession session, NodeId view, long tick, CancellationToken ct)
        {
            NodeId state = await ChildAsync(session, view, "State", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            await foreach (DataValueChange update in session.DefaultStreaming.SubscribeDataChangesAsync(
                state,
                new MonitoredItemOptions { SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 1 },
                ct)
                .ConfigureAwait(false))
            {
                if (StatusCode.IsGood(update.Value.StatusCode) &&
                    update.Value.WrappedValue.TryGetValue<SnapshotDataType>(
                        out SnapshotDataType? image, session.MessageContext) &&
                    image?.Tick == tick)
                {
                    return;
                }
            }
            Assert.Fail("The process view did not recover its committed image.");
        }

        private static void RegisterModelTypes(ManagedSession session)
        {
            RegisterNativeModelTypes(session);
            session.MessageContext.Factory.Builder.AddOpcUaScales().AddOpcUaMachineVision().Commit();
            if (!ReferenceEquals(session.Factory, session.MessageContext.Factory))
            {
                session.Factory.Builder.AddOpcUaScales().AddOpcUaMachineVision().Commit();
            }
        }

        private static async Task AssertMeasurementsAsync(
            ManagedSession session, NodeId stage, string name, StageSnapshot committed, CommittedSnapshot snapshot,
            CancellationToken ct)
        {
            NodeId rate = await ChildAsync(session, stage, "ThroughputKgHr", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            DataValue measured = await session.ReadValueAsync(rate, ct).ConfigureAwait(false);
            Assert.That(measured.WrappedValue.TryGetValue(out double kgHr), Is.True);
            Assert.That(kgHr, Is.EqualTo(committed.Transferred.Total * 3600.0 /
                (1_000_000 * snapshot.LogicalSeconds)).Within(0.000001));
            DataValue units = await ReadChildAsync(session, rate, "EngineeringUnits", Namespaces.OpcUa, ct)
                .ConfigureAwait(false);
            Assert.That(units.WrappedValue.TryGetValue<EUInformation>(
                out EUInformation? unit, session.MessageContext), Is.True);
            Assert.That(unit!.DisplayName.Text, Is.EqualTo("kg/h"));
            DataValue formula = await ReadChildAsync(session, rate, "Formula", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            Assert.That(formula.WrappedValue.TryGetValue(out string? formulaText), Is.True);
            Assert.That(formulaText, Does.Contain("committed logical seconds"));
            foreach (string field in RequiredFields(name).Span.ToArray())
            {
                NodeId id = await ChildAsync(session, stage, field, RecyclingModelMapping.NamespaceUri, ct)
                    .ConfigureAwait(false);
                ReadResponse attributes = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                    [
                        new ReadValueId { NodeId = id, AttributeId = Attributes.DataType },
                        new ReadValueId { NodeId = id, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = id, AttributeId = Attributes.Description }
                    ], ct).ConfigureAwait(false);
                Assert.That(attributes.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good), field);
                Assert.That(attributes.Results[0].WrappedValue.TryGetValue(out NodeId type), Is.True, field);
                Assert.That(type.IsNull, Is.False, field);
                ushort sample = checked((ushort)session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
                NodeId expectedType = field switch
                {
                    "RunState" or "FaultState" or "AlarmState" => new NodeId(Model.DataTypes.EquipmentEnum, sample),
                    "CameraHealth" => ExpandedNodeId.ToNodeId(
                        Di.DataTypeIds.DeviceHealthEnumeration, session.NamespaceUris),
                    "QualityClassification" => new NodeId(Model.DataTypes.DispositionEnum, sample),
                    "Stage" => new NodeId(Model.DataTypes.StageDataType, sample),
                    "LotId" => DataTypeIds.String,
                    "Executions" => DataTypeIds.Int64,
                    "JamEvents" or "StrandBreakEvents" => DataTypeIds.UInt64,
                    "JamAlarm" or "StrandBreakAlarm" or "PumpStatus" or "CirculationPumpStatus" => DataTypeIds.Boolean,
                    _ => DataTypeIds.Double
                };
                Assert.That(type, Is.EqualTo(expectedType), field);
                DateTime sourceTimestamp = field switch
                {
                    "Ph" => committed.PhSourceTimestampUtc,
                    "ColourR" or "ColourG" or "ColourB" => committed.ColorSourceTimestampUtc,
                    _ => snapshot.SourceTimestampUtc
                };
                Assert.That(attributes.Results[1].SourceTimestamp.ToDateTime(), Is.EqualTo(sourceTimestamp), field);
                bool unavailable = field switch
                {
                    "Ph" => !committed.PhAvailable,
                    "ColourR" or "ColourG" or "ColourB" => !committed.ColorAvailable,
                    _ => false
                };
                Assert.That(attributes.Results[1].StatusCode, Is.EqualTo(
                    unavailable ? StatusCodes.BadNoData : StatusCodes.Good), field);
                if (!unavailable)
                {
                    Assert.That(attributes.Results[1].WrappedValue.IsNull, Is.False, field);
                }
                if (expectedType == DataTypeIds.Double)
                {
                    DataValue measurementUnit = await ReadChildAsync(session, id, "EngineeringUnits",
                        Namespaces.OpcUa, ct).ConfigureAwait(false);
                    Assert.That(measurementUnit.WrappedValue.TryGetValue<EUInformation>(
                        out EUInformation? engineeringUnit, session.MessageContext), Is.True, field);
                    Assert.That(engineeringUnit!.DisplayName.Text, Is.EqualTo(ExpectedUnit(field)), field);
                    DataValue range = await ReadChildAsync(session, id, "EURange", Namespaces.OpcUa, ct)
                        .ConfigureAwait(false);
                    Assert.That(range.WrappedValue.TryGetValue<Range>(
                        out Range? limits, session.MessageContext), Is.True, field);
                    Assert.That(limits!.High, Is.GreaterThan(limits.Low), field);
                }
            }
            if (name is "secondary-granulation" or "pelletizing")
            {
                string field = name == "secondary-granulation" ? "FlakeSizeAvgMm" : "PelletSizeAvgMm";
                DataValue size = await ReadChildAsync(session, stage, field, RecyclingModelMapping.NamespaceUri, ct)
                    .ConfigureAwait(false);
                Assert.That(size.WrappedValue.TryGetValue(out double millimetres), Is.True);
                Assert.That(millimetres, Is.EqualTo(name == "secondary-granulation" ? 8 : 3));
            }
            if (name == "bagging")
            {
                DataValue weight = await ReadChildAsync(
                    session, stage, "BagWeightKg", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(weight.WrappedValue.TryGetValue(out double kg), Is.True);
                Assert.That(kg, Is.EqualTo(4));
                DataValue count = await ReadChildAsync(
                    session, stage, "PackageCount", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
                Assert.That(count.WrappedValue.TryGetValue(out long packages), Is.True);
                Assert.That(packages, Is.EqualTo(snapshot.Packages.Count).And.GreaterThan(0));
            }
        }

        private static async Task AssertNativeEquipmentAsync(
            ManagedSession session, NodeId stageId, StageSnapshot stage, CommittedSnapshot snapshot,
            ArrayOf<AssetAlias> assets, ITelemetryContext telemetry, CancellationToken ct)
        {
            NodeId equipment = await ChildAsync(session, stageId, "Equipment", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            ArrayOf<ReferenceDescription> native = await ChildrenAsync(session, equipment, ct).ConfigureAwait(false);
            await AssertNativeProcessAsync(session, stageId, stage, snapshot, native, assets, ct)
                .ConfigureAwait(false);
            foreach (ReferenceDescription instrument in native.Span.ToArray())
            {
                var instrumentId = ExpandedNodeId.ToNodeId(instrument.NodeId, session.NamespaceUris);
                string uri = session.NamespaceUris.GetString(instrument.TypeDefinition.NamespaceIndex)!;
                if (uri != MachineVisionModels.ControlNamespaceUri)
                {
                    Assert.That(assets.Contains(asset => asset.ReferencedNodes.Contains(
                        target => ExpandedNodeId.ToNodeId(target, session.NamespaceUris) == instrumentId)),
                        Is.True, instrument.BrowseName.Name);
                }
            }
            if (stage.Index is 1 or 2 or 20)
            {
                ReferenceDescription scale = native.Span.ToArray()
                    .Single(item => item.BrowseName.Name == stage.Name + "-weighing");
                uint expectedType = stage.Index switch
                {
                    1 => Scales.ObjectTypes.VehicleScaleType,
                    2 => Scales.ObjectTypes.SimpleScaleType,
                    _ => Scales.ObjectTypes.AutomaticFillingScaleType
                };
                ushort ns = checked((ushort)session.NamespaceUris.GetIndex(Scales.Namespaces.Scales));
                Assert.That(scale.TypeDefinition, Is.EqualTo(new ExpandedNodeId(expectedType, ns)));
                var id = ExpandedNodeId.ToNodeId(scale.NodeId, session.NamespaceUris);
                DataValue weight = await ReadChildAsync(session, id, "CurrentWeight", Scales.Namespaces.Scales, ct)
                    .ConfigureAwait(false);
                Assert.That(weight.WrappedValue.TryGetValue<WeightType>(
                    out WeightType? value, session.MessageContext), Is.True);
                Assert.That(value!.Net, Is.EqualTo(stage.Index == 20 ? 4 : stage.Inventory.Total / 1_000_000.0)
                    .Within(0.000001));
                if (stage.Index == 20)
                {
                    NodeId statistics = await ChildAsync(session, id, "ProductionOutput", Scales.Namespaces.Scales, ct)
                        .ConfigureAwait(false);
                    NodeId total = await ChildAsync(session, statistics, "TotalPackages", Scales.Namespaces.Scales, ct)
                        .ConfigureAwait(false);
                    DataValue count = await ReadChildAsync(session, total, "ItemCount", Scales.Namespaces.Scales, ct)
                        .ConfigureAwait(false);
                    Assert.That(count.WrappedValue.TryGetValue(out ulong packageCount), Is.True);
                    Assert.That(packageCount, Is.EqualTo((ulong)snapshot.Packages.Count));
                }
            }
            if (stage.Index is >= 8 and <= 12)
            {
                Assert.That(native.Count, Is.EqualTo(stage.Index == 12 ? 1 : 2));
                foreach (ReferenceDescription pump in native.Span.ToArray())
                {
                    ushort ns = checked((ushort)session.NamespaceUris.GetIndex(Pumps.Namespaces.Pumps));
                    Assert.That(pump.TypeDefinition, Is.EqualTo(new ExpandedNodeId(Pumps.ObjectTypes.PumpType, ns)));
                    var id = ExpandedNodeId.ToNodeId(pump.NodeId, session.NamespaceUris);
                    NodeId operational = await ChildAsync(session, id, "Operational", Di.Namespaces.OpcUaDi, ct)
                        .ConfigureAwait(false);
                    NodeId actuation = await ChildAsync(
                        session, operational, "PumpActuation", Pumps.Namespaces.Pumps, ct)
                        .ConfigureAwait(false);
                    DataValue status = await ReadChildAsync(session, actuation, "Status", Pumps.Namespaces.Pumps, ct)
                        .ConfigureAwait(false);
                    Assert.That(status.WrappedValue.TryGetValue(out bool running), Is.True);
                    Assert.That(running, Is.EqualTo(stage.ElectricalDemandWatts > 0 &&
                        stage.State is not (EquipmentState.ElectricalConstraint or EquipmentState.Unavailable or
                            EquipmentState.Stopped or EquipmentState.ThermalConstraint)));
                    Assert.That(status.SourceTimestamp.ToDateTime(), Is.EqualTo(snapshot.SourceTimestampUtc));
                }
            }
            if (stage.Index is 4 or 5)
            {
                ReferenceDescription system = native.Span.ToArray()
                    .Single(item => item.BrowseName.Name == stage.Name + "-vision-control");
                ReferenceDescription asset = native.Span.ToArray()
                    .Single(item => item.BrowseName.Name == stage.Name + "-vision-asset");
                Assert.That(ExpandedNodeId.ToNodeId(system.TypeDefinition, session.NamespaceUris),
                    Is.EqualTo(ExpandedNodeId.ToNodeId(
                        Vision.MachineVision.ObjectTypeIds.VisionSystemType, session.NamespaceUris)));
                Assert.That(ExpandedNodeId.ToNodeId(asset.TypeDefinition, session.NamespaceUris),
                    Is.EqualTo(ExpandedNodeId.ToNodeId(
                        Vision.MachineVision.AMCM.ObjectTypeIds.VisionSystemAssetType, session.NamespaceUris)));
                NodeId resultId = await ChildAsync(session,
                    ExpandedNodeId.ToNodeId(system.NodeId, session.NamespaceUris), "ResultManagement",
                    MachineVisionModels.ControlNamespaceUri, ct).ConfigureAwait(false);
                var results = new ResultManagementTypeClient(session, resultId, telemetry);
                (_, uint handle, _, bool simulated, _, _, _, _, _, _, _, _, _, DateTimeUtc time, _,
                    ArrayOf<Variant> content, int error) = await results.GetResultComponentsByIdAsync(
                    new ResultIdDataType { Id = "latest" }, 0, ct).ConfigureAwait(false);
                Assert.That(error, Is.Zero);
                Assert.That(simulated, Is.True);
                Assert.That(time.ToDateTime(), Is.LessThanOrEqualTo(snapshot.SourceTimestampUtc));
                Assert.That(content[0].TryGetValue<ExecutionDataType>(
                    out ExecutionDataType? execution, session.MessageContext), Is.True);
                Assert.That(execution!.Id,
                    Is.EqualTo(snapshot.History.Span.ToArray().Last(item => item.Stage == stage.Index).Id));
                Assert.That(execution.Stage, Is.EqualTo(stage.Index));
                Assert.That(await results.ReleaseResultHandleAsync(handle, ct).ConfigureAwait(false), Is.Zero);
            }
        }

        private static async Task AssertBaggingControlRefusalAsync(
            ManagedSession session, NodeId bagging, CancellationToken ct)
        {
            NodeId equipment = await ChildAsync(
                session, bagging, "Equipment", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            ReferenceDescription scale = (await ChildrenAsync(session, equipment, ct).ConfigureAwait(false))
                .Span.ToArray().Single();
            NodeId state = await ChildAsync(session, ExpandedNodeId.ToNodeId(scale.NodeId, session.NamespaceUris),
                "State", Scales.Namespaces.Scales, ct).ConfigureAwait(false);
            NodeId machine = await ChildAsync(session, state, "MachineState", PackML.Namespaces.PackML, ct)
                .ConfigureAwait(false);
            NodeId execute = await ChildAsync(session, machine, "ExecuteState", PackML.Namespaces.PackML, ct)
                .ConfigureAwait(false);
            NodeId start = await ChildAsync(session, execute, "Start", PackML.Namespaces.PackML, ct)
                .ConfigureAwait(false);
            CallResponse response = await session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = execute,
                        MethodId = start,
                        InputArguments = [Variant.FromStructure(ArrayOf<PackMLDescriptorDataType>.Empty)]
                    }
                ], ct).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.AnyOf(
                StatusCodes.BadNotExecutable, StatusCodes.BadUserAccessDenied,
                StatusCodes.BadNotSupported));
        }

        private static async Task WaitForInitializationAsync(
            RecyclingUaEnvironment environment, RecyclingProcess process, CancellationToken ct)
        {
            await foreach (CoordinatorNotification update in environment.Coordinator.WatchAsync(ct)
                .ConfigureAwait(false))
            {
                if (update.Snapshot.Participants.Contains(item => item.Process == process && item.Initialized))
                {
                    return;
                }
            }
            Assert.Fail("The process view did not initialize.");
        }

        private static async Task<ArrayOf<ReferenceDescription>> ChildrenAsync(
            ManagedSession session, NodeId parent, CancellationToken ct)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            });
            return await browser.BrowseAsync(parent, ct).ConfigureAwait(false);
        }

        private static async Task<NodeId> ChildAsync(
            ManagedSession session, NodeId parent, string name, string namespaceUri, CancellationToken ct)
        {
            ushort ns = checked((ushort)session.NamespaceUris.GetIndex(namespaceUri));
            ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, parent, ct).ConfigureAwait(false);
            ReferenceDescription child = children.Span.ToArray().Single(
                item => item.BrowseName == new QualifiedName(name, ns));
            return ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
        }

        private static async Task<DataValue> ReadChildAsync(
            ManagedSession session, NodeId parent, string name, string namespaceUri, CancellationToken ct)
        {
            NodeId id = await ChildAsync(session, parent, name, namespaceUri, ct).ConfigureAwait(false);
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = id, AttributeId = Attributes.Value }], ct).ConfigureAwait(false);
            return read.Results[0];
        }

        private static uint ExpectedType(string stage)
        {
            return stage switch
            {
                "collection-bins" => Model.ObjectTypes.CollectionBinType,
                "collection-transport" => Model.ObjectTypes.CollectionTransportType,
                "inbound-identification" => Model.ObjectTypes.InboundIdentificationType,
                "feed-conveyor" => Model.ObjectTypes.FeedConveyorType,
                "optical-sorter-one" or "optical-sorter-two" => Model.ObjectTypes.OpticalSortingStationType,
                "primary-shredding" => Model.ObjectTypes.PrimaryReductionType,
                "secondary-granulation" => Model.ObjectTypes.SecondaryReductionType,
                "pre-wash" => Model.ObjectTypes.PreWashType,
                "friction-wash" => Model.ObjectTypes.FrictionWashType,
                "hot-wash" => Model.ObjectTypes.HotWashType,
                "rinse" => Model.ObjectTypes.RinsingType,
                "float-sink" => Model.ObjectTypes.DensitySeparationType,
                "mechanical-dewatering" => Model.ObjectTypes.MechanicalDewateringType,
                "desiccant-thermal-drying" => Model.ObjectTypes.SampleThermalDryingType,
                "post-dry-buffer" => Model.ObjectTypes.DryMaterialBufferType,
                "extrusion" => Model.ObjectTypes.SampleExtrusionType,
                "melt-filtration" => Model.ObjectTypes.SampleMeltFiltrationType,
                "pelletizing" => Model.ObjectTypes.SamplePelletizingType,
                "pellet-screening" => Model.ObjectTypes.PelletScreeningType,
                "bagging" => Model.ObjectTypes.BaggingStationType,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
        }

        private static ArrayOf<string> RequiredFields(string stage)
        {
            return stage switch
            {
                "feed-conveyor" => ["RunState", "BeltSpeedMps", "ThroughputTph", "JamAlarm", "JamEvents"],
                "optical-sorter-one" or "optical-sorter-two" =>
                    ["RunState", "ClassificationAccuracyPct", "ContaminationPpm", "DriftScore", "CameraHealth"],
                "primary-shredding" => ["MotorLoadPct", "FeedRateKgMin", "JamAlarm", "JamEvents"],
                "secondary-granulation" => ["FlakeSizeAvgMm", "ThroughputKgHr", "JamAlarm", "JamEvents"],
                "pre-wash" => ["PumpStatus", "TurbidityNtu", "WaterTempC"],
                "friction-wash" => ["FaultState", "WashEffectivenessPct", "MotorLoadPct"],
                "hot-wash" => ["FaultState", "BathTempC", "Ph", "AdhesiveRemovalPct"],
                "float-sink" => ["CirculationPumpStatus", "SeparationPurityPct", "HdpeRecoveryPct"],
                "mechanical-dewatering" => ["OutletMoisturePct", "ThroughputKgHr"],
                "extrusion" => ["MeltTempC", "MeltPressureBar", "ExtrusionStabilityScore", "ThroughputKgHr"],
                "melt-filtration" => ["AlarmState", "DiffPressureBar", "CloggingPct", "MeltCleanlinessIndex"],
                "pelletizing" =>
                    ["FaultState", "PelletSizeAvgMm", "ThroughputKgHr", "StrandBreakAlarm", "StrandBreakEvents"],
                "bagging" => ["RunState", "BagWeightKg", "BagsPerHr", "LotId", "ColourR", "ColourG", "ColourB",
                    "QualityClassification"],
                _ => ["RunState", "Stage", "Executions", "ThroughputKgHr"]
            };
        }

        private static string ExpectedUnit(string field)
        {
            return field switch
            {
                "ThroughputKgHr" => "kg/h",
                "ThroughputTph" => "t/h",
                "BeltSpeedMps" => "m/s",
                "ContaminationPpm" => "ppm",
                "DriftScore" => "ratio",
                "FeedRateKgMin" => "kg/min",
                "FlakeSizeAvgMm" or "PelletSizeAvgMm" => "mm",
                "TurbidityNtu" => "NTU",
                "WaterTempC" or "BathTempC" or "MeltTempC" => "degC",
                "Ph" => "pH",
                "MeltPressureBar" or "DiffPressureBar" => "bar",
                "ExtrusionStabilityScore" or "MeltCleanlinessIndex" => "score (0..100)",
                "BagWeightKg" => "kg",
                "BagsPerHr" => "bags/h",
                "ColourR" or "ColourG" or "ColourB" => "RGB channel",
                _ => "%"
            };
        }
    }
}
