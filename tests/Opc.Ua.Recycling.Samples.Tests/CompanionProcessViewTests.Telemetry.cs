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
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Recycling.Model;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Recycling.Samples.Tests
{
    public sealed partial class CompanionProcessViewTests
    {
        /// <summary>
        /// Zone changes are real committed provenance; mixed inventory preserves both source zones over recovery.
        /// </summary>
        [Test]
        public async Task SourceZonesFollowCommittedContributionsAndSurviveReconnect()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct,
                new RecyclingConfiguration { SourceZones = ["north", "south"], SourceColorVariationMilligrams = 0 })
                .ConfigureAwait(false);
            (IHost host, ManagedSession session, NodeId view, NodeId bin) = await StartTelemetryViewAsync(
                environment, RecyclingProcess.Collection, "collection-bins", ct).ConfigureAwait(false);
            NodeId zone = await ChildAsync(session, bin, "SourceZone", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> changes = ObserveSignal(session, zone, ct);
            Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);

            CommittedSnapshot first = await AdvanceTelemetryAsync(environment, session, view, 1, ct)
                .ConfigureAwait(false);
            DataValue north = await NextSignalAsync(changes, first.SourceTimestampUtc, ct).ConfigureAwait(false);
            Assert.That(north.WrappedValue.TryGetValue(out string? northName), Is.True);
            Assert.That(northName, Is.EqualTo("north"));
            CommittedSnapshot second = await AdvanceTelemetryAsync(environment, session, view, 2, ct)
                .ConfigureAwait(false);
            DataValue south = await NextSignalAsync(changes, second.SourceTimestampUtc, ct).ConfigureAwait(false);
            Assert.That(south.WrappedValue.TryGetValue(out string? southName), Is.True);
            Assert.That(southName, Is.EqualTo("south"));

            CommittedSnapshot mixed = await AdvanceTelemetryAsync(environment, session, view, 3, ct)
                .ConfigureAwait(false);
            await NextSignalAsync(changes, mixed.SourceTimestampUtc, ct).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> stages = await ChildrenAsync(session, view, ct).ConfigureAwait(false);
            var transport = ExpandedNodeId.ToNodeId(stages.Span.ToArray()
                .Single(item => item.BrowseName.Name == "collection-transport").NodeId, session.NamespaceUris);
            NodeId mixedZone = await ChildAsync(
                session, transport, "SourceZone", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            NodeId contributions = await ChildAsync(
                session, transport, "Contributions", RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadSignalsAsync(session, [mixedZone, contributions], ct)
                .ConfigureAwait(false);
            Assert.That(values[0].WrappedValue.TryGetValue(out string? mixedName), Is.True);
            Assert.That(mixedName, Is.EqualTo("mixed"));
            Assert.That(values[1].WrappedValue.TryGetValue<ContributionDataType>(
                out ArrayOf<ContributionDataType> sources, session.MessageContext), Is.True);
            Assert.That(sources.Span.ToArray().Select(static source => source.SourceZone),
                Is.EquivalentTo(s_expectedSourceZones));
            Assert.That(sources.Span.ToArray().Select(static source => source.SourceId).Distinct().Count(),
                Is.EqualTo(2));
            await changes.DisposeAsync().ConfigureAwait(false);
            await AssertTelemetryReconnectAsync(environment, host, session, view,
                [zone, mixedZone, contributions], ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Scheduled fault flags and onset counters are committed once, observable over UA, and not replayed.
        /// </summary>
        [TestCase(RecyclingProcess.Sorting, "feed-conveyor", RecyclingEquipmentFault.Jam)]
        [TestCase(RecyclingProcess.Shredding, "primary-shredding", RecyclingEquipmentFault.Jam)]
        [TestCase(RecyclingProcess.Shredding, "secondary-granulation", RecyclingEquipmentFault.Jam)]
        [TestCase(RecyclingProcess.Pelletizing, "pelletizing", RecyclingEquipmentFault.StrandBreak)]
        public async Task EquipmentFaultSignalsUseAbsoluteCommittedOnsets(
            RecyclingProcess process, string stageName, RecyclingEquipmentFault fault)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct,
                new RecyclingConfiguration
                {
                    EquipmentFaults =
                    [
                        new EquipmentFaultWindow(stageName, fault, 2, 5),
                        new EquipmentFaultWindow(stageName, fault, 7, 9)
                    ]
                }).ConfigureAwait(false);
            (IHost host, ManagedSession session, NodeId view, NodeId stage) = await StartTelemetryViewAsync(
                environment, process, stageName, ct).ConfigureAwait(false);
            bool jam = fault == RecyclingEquipmentFault.Jam;
            NodeId alarm = await ChildAsync(session, stage, jam ? "JamAlarm" : "StrandBreakAlarm",
                RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            NodeId events = await ChildAsync(session, stage, jam ? "JamEvents" : "StrandBreakEvents",
                RecyclingModelMapping.NamespaceUri, ct).ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> alarms = ObserveSignal(session, alarm, ct);
            Assert.That(await alarms.MoveNextAsync().ConfigureAwait(false), Is.True);
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);
            foreach (int tick in new[] { 1, 2, 3, 5, 7, 9 })
            {
                CommittedSnapshot committed = await AdvanceTelemetryAsync(environment, session, view, tick, ct)
                    .ConfigureAwait(false);
                DataValue signalled = await NextSignalAsync(alarms, committed.SourceTimestampUtc, ct)
                    .ConfigureAwait(false);
                Assert.That(signalled.WrappedValue.TryGetValue(out bool active), Is.True);
                Assert.That(active, Is.EqualTo(tick is 2 or 3 or 7));
                ArrayOf<DataValue> counts = await ReadSignalsAsync(session, [events], ct).ConfigureAwait(false);
                Assert.That(counts[0].WrappedValue.TryGetValue(out ulong count), Is.True);
                Assert.That(count, Is.EqualTo(tick < 2 ? 0UL : tick < 7 ? 1UL : 2UL));
                StageSnapshot source = committed.Stages.Span.ToArray().Single(item => item.Name == stageName);
                Assert.That(jam ? source.JamEvents : source.StrandBreakEvents, Is.EqualTo((long)count));
                if (active)
                {
                    Assert.That(source.State, Is.EqualTo(jam ? EquipmentState.Jammed : EquipmentState.StrandBroken));
                }
                IRecyclingProcessViewAdapter adapter =
                    host.Services.GetRequiredService<IRecyclingProcessViewAdapter>();
                await adapter.ApplyAsync(committed, ct).ConfigureAwait(false);
                await adapter.ApplyAsync(committed, ct).ConfigureAwait(false);
                ArrayOf<DataValue> repeated = await ReadSignalsAsync(session, [events], ct).ConfigureAwait(false);
                Assert.That(repeated[0].WrappedValue, Is.EqualTo(counts[0].WrappedValue));
                Assert.That(repeated[0].SourceTimestamp, Is.EqualTo(counts[0].SourceTimestamp));
            }
            await alarms.DisposeAsync().ConfigureAwait(false);
            await AssertTelemetryReconnectAsync(environment, host, session, view, [alarm, events], ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A configured synthetic pH sample keeps its own source time when the wash stops and reconnects.
        /// </summary>
        [Test]
        public async Task CommittedPhKeepsItsMeasurementTimestampThroughStoppedTicksAndReconnect()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct,
                new RecyclingConfiguration { HotWashPh = 9.25 }).ConfigureAwait(false);
            (IHost host, ManagedSession session, NodeId view, NodeId stage) = await StartTelemetryViewAsync(
                environment, RecyclingProcess.Washing, "hot-wash", ct).ConfigureAwait(false);
            NodeId ph = await ChildAsync(session, stage, "Ph", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> changes = ObserveSignal(session, ph, ct);
            Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);
            CommittedSnapshot measured = await AdvanceTelemetryAsync(environment, session, view, 1, ct)
                .ConfigureAwait(false);
            DataValue observation = await NextSignalAsync(changes, measured.Stages[10].PhSourceTimestampUtc, ct)
                .ConfigureAwait(false);
            Assert.That(observation.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(9.25));
            Assert.That(measured.Stages[10].PhAvailable, Is.True);
            RecyclingCoordinatorClient actor = await environment.ConnectAsync("operator", ct).ConfigureAwait(false);
            await actor.ExecuteAsync(CoordinatorTests.Command(
                environment.Coordinator, RecyclingCommandKind.DisableProcess, RecyclingProcess.Washing), ct)
                .ConfigureAwait(false);
            CommittedSnapshot stopped = await AdvanceTelemetryAsync(environment, session, view, 3, ct)
                .ConfigureAwait(false);
            Assert.That(stopped.SourceTimestampUtc, Is.GreaterThan(observation.SourceTimestamp.ToDateTime()));
            ArrayOf<DataValue> retained = await ReadSignalsAsync(session, [ph], ct).ConfigureAwait(false);
            Assert.That(retained[0].WrappedValue, Is.EqualTo(observation.WrappedValue));
            Assert.That(retained[0].SourceTimestamp, Is.EqualTo(observation.SourceTimestamp));
            await changes.DisposeAsync().ConfigureAwait(false);
            await AssertTelemetryReconnectAsync(environment, host, session, view, [ph], ct).ConfigureAwait(false);
        }

        /// <summary>
        /// RGB channels come from committed package samples, not view coefficients or reconnect timing.
        /// </summary>
        [Test]
        public async Task PackageRgbSubscriptionsFollowCommittedBasisAndKeepTheirSourceTimeOnRecovery()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            CancellationToken ct = timeout.Token;
            var configuration = new RecyclingConfiguration
            {
                SourceBatch = new MaterialMass(2_000_000, 2_000_000, 0, 0, 0, 0),
                SourceColorVariationMilligrams = 0,
                SourceZones = ["north", "south"],
                NaturalPolymerColor = new SyntheticRgb(255, 0, 0),
                ColoredPolymerColor = new SyntheticRgb(0, 0, 255),
                Windows = [new ScenarioWindow(8, 20, 250, 1000, 1000)]
            };
            await using RecyclingUaEnvironment environment = await RecyclingUaEnvironment.StartAsync(ct, configuration)
                .ConfigureAwait(false);
            (IHost host, ManagedSession session, NodeId view, NodeId stage) = await StartTelemetryViewAsync(
                environment, RecyclingProcess.Bagging, "bagging", ct).ConfigureAwait(false);
            NodeId red = await ChildAsync(session, stage, "ColourR", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            NodeId green = await ChildAsync(session, stage, "ColourG", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            NodeId blue = await ChildAsync(session, stage, "ColourB", RecyclingModelMapping.NamespaceUri, ct)
                .ConfigureAwait(false);
            await using IAsyncEnumerator<DataValueChange> changes = ObserveSignal(session, red, ct);
            Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(changes.Current.Value.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            await environment.InitializeRolesAsync(ct).ConfigureAwait(false);
            CommittedSnapshot first;
            do
            {
                first = await AdvanceTelemetryAsync(environment, session, view,
                    environment.Coordinator.Current.Tick + 1, ct).ConfigureAwait(false);
                Assert.That(first.Tick, Is.LessThan(90), "The finite fixture must produce a package.");
            }
            while (!first.Stages[20].ColorAvailable);
            await NextSignalAsync(changes, first.Stages[20].ColorSourceTimestampUtc, ct).ConfigureAwait(false);
            await AssertRgbAsync(session, [red, green, blue], first, ct).ConfigureAwait(false);
            Assert.That(first.Stages[20].Color.Green, Is.Zero);
            Assert.That(first.Stages[20].Color.Red, Is.InRange((byte)1, (byte)254));
            Assert.That(first.Stages[20].Color.Blue, Is.InRange((byte)1, (byte)254));

            CommittedSnapshot changed;
            do
            {
                changed = await AdvanceTelemetryAsync(environment, session, view,
                    environment.Coordinator.Current.Tick + 1, ct).ConfigureAwait(false);
                Assert.That(changed.Tick, Is.LessThan(110), "The drift fixture must change the package color.");
            }
            while (changed.Stages[20].Color == first.Stages[20].Color);
            await NextSignalAsync(changes, changed.Stages[20].ColorSourceTimestampUtc, ct).ConfigureAwait(false);
            await AssertRgbAsync(session, [red, green, blue], changed, ct).ConfigureAwait(false);
            RecyclingCoordinatorClient actor = await environment.ConnectAsync("operator", ct).ConfigureAwait(false);
            await actor.ExecuteAsync(CoordinatorTests.Command(
                environment.Coordinator, RecyclingCommandKind.DisableProcess, RecyclingProcess.Bagging), ct)
                .ConfigureAwait(false);
            await AdvanceTelemetryAsync(environment, session, view, changed.Tick + 2, ct).ConfigureAwait(false);
            await AssertRgbAsync(session, [red, green, blue], changed, ct).ConfigureAwait(false);
            await changes.DisposeAsync().ConfigureAwait(false);
            await AssertTelemetryReconnectAsync(environment, host, session, view, [red, green, blue], ct)
                .ConfigureAwait(false);
        }

        private static async Task<(IHost Host, ManagedSession Session, NodeId View, NodeId Stage)>
            StartTelemetryViewAsync(
                RecyclingUaEnvironment environment, RecyclingProcess process, string stageName, CancellationToken ct)
        {
            IHost host = await environment.StartViewAsync(process, null, ct).ConfigureAwait(false);
            await host.Services.GetRequiredService<RecyclingRuntime>().AddressSpaceReady.WaitAsync(ct)
                .ConfigureAwait(false);
            await WaitForInitializationAsync(environment, process, ct).ConfigureAwait(false);
            string endpoint = host.Services.GetRequiredService<RecyclingHostOptions>().EndpointUrl;
            RecyclingCoordinatorClient connection = await environment.ConnectAsync("observer", ct, endpoint)
                .ConfigureAwait(false);
            ManagedSession session = connection.Session;
            RegisterModelTypes(session);
            ushort ns = checked((ushort)session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri));
            var view = new NodeId(Model.Objects.ProcessView, ns);
            ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, view, ct).ConfigureAwait(false);
            var stage = ExpandedNodeId.ToNodeId(children.Span.ToArray()
                .Single(item => item.BrowseName.Name == stageName).NodeId, session.NamespaceUris);
            return (host, session, view, stage);
        }

        private static IAsyncEnumerator<DataValueChange> ObserveSignal(
            ManagedSession session, NodeId node, CancellationToken ct)
        {
            return session.DefaultStreaming.SubscribeDataChangesAsync(node, new MonitoredItemOptions
            {
                SamplingInterval = TimeSpan.FromMilliseconds(100),
                QueueSize = 32,
                DiscardOldest = true,
                Filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValueTimestamp }
            }, ct).GetAsyncEnumerator(ct);
        }

        private static async Task<DataValue> NextSignalAsync(
            IAsyncEnumerator<DataValueChange> changes, DateTime sourceTimestamp, CancellationToken ct)
        {
            while (!StatusCode.IsGood(changes.Current.Value.StatusCode) ||
                changes.Current.Value.SourceTimestamp.ToDateTime() != sourceTimestamp)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            return changes.Current.Value;
        }

        private static async Task<CommittedSnapshot> AdvanceTelemetryAsync(
            RecyclingUaEnvironment environment, ManagedSession session, NodeId view, long tick, CancellationToken ct)
        {
            int pending = 0;
            while (environment.Coordinator.Current.Tick < tick)
            {
                await environment.Coordinator.TickAsync(ct).ConfigureAwait(false);
                if (++pending == 8)
                {
                    await WaitForProjectionAsync(session, view, environment.Coordinator.Current.Tick, ct)
                        .ConfigureAwait(false);
                    pending = 0;
                }
            }
            await WaitForProjectionAsync(session, view, tick, ct).ConfigureAwait(false);
            return environment.Coordinator.Current;
        }

        private static async Task<ArrayOf<DataValue>> ReadSignalsAsync(
            ManagedSession session, ArrayOf<NodeId> nodes, CancellationToken ct)
        {
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                nodes.Span.ToArray().Select(id => new ReadValueId { NodeId = id, AttributeId = Attributes.Value })
                    .ToArrayOf(), ct).ConfigureAwait(false);
            return response.Results;
        }

        private static async Task AssertRgbAsync(
            ManagedSession session, ArrayOf<NodeId> nodes, CommittedSnapshot snapshot, CancellationToken ct)
        {
            StageSnapshot basis = snapshot.Stages[20];
            Assert.That(basis.ColorMaterialId, Is.EqualTo(snapshot.Packages[^1].Id));
            Assert.That(basis.ColorNaturalPolymerMilligrams,
                Is.EqualTo(snapshot.Packages[^1].Material.Mass.NaturalPolymer));
            Assert.That(basis.ColorColoredPolymerMilligrams,
                Is.EqualTo(snapshot.Packages[^1].Material.Mass.ColoredPolymer));
            ArrayOf<DataValue> read = await ReadSignalsAsync(session, nodes, ct).ConfigureAwait(false);
            double[] expected = [basis.Color.Red, basis.Color.Green, basis.Color.Blue];
            for (int i = 0; i < read.Count; i++)
            {
                Assert.That(read[i].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read[i].WrappedValue.TryGetValue(out double channel), Is.True);
                Assert.That(channel, Is.EqualTo(expected[i]));
                Assert.That(read[i].SourceTimestamp.ToDateTime(), Is.EqualTo(basis.ColorSourceTimestampUtc));
            }
        }

        private static async Task AssertTelemetryReconnectAsync(
            RecyclingUaEnvironment environment,
            IHost host,
            ManagedSession session,
            NodeId view,
            ArrayOf<NodeId> nodes,
            CancellationToken ct)
        {
            ArrayOf<DataValue> before = await ReadSignalsAsync(session, nodes, ct).ConfigureAwait(false);
            long tick = environment.Coordinator.Current.Tick;
            await using IAsyncEnumerator<DataValueChange> probe = ObserveSignal(session, nodes[0], ct);
            Assert.That(await probe.MoveNextAsync().ConfigureAwait(false), Is.True);
            await environment.ServerHost.StopAsync(ct).ConfigureAwait(false);
            while (probe.Current.Value.StatusCode != StatusCodes.UncertainLastUsableValue)
            {
                Assert.That(await probe.MoveNextAsync().ConfigureAwait(false), Is.True);
            }
            ArrayOf<DataValue> offline = await ReadSignalsAsync(session, nodes, ct).ConfigureAwait(false);
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.That(offline[i].StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
                Assert.That(offline[i].WrappedValue, Is.EqualTo(before[i].WrappedValue));
                Assert.That(offline[i].SourceTimestamp, Is.EqualTo(before[i].SourceTimestamp));
            }
            await probe.DisposeAsync().ConfigureAwait(false);
            await environment.RestartAsync(ct).ConfigureAwait(false);
            RecyclingHostOptions options = host.Services.GetRequiredService<RecyclingHostOptions>();
            await WaitForInitializationAsync(environment, options.Process, ct).ConfigureAwait(false);
            RecyclingCoordinatorClient connection = await environment.ConnectAsync("observer", ct, options.EndpointUrl)
                .ConfigureAwait(false);
            RegisterModelTypes(connection.Session);
            await WaitForProjectionAsync(connection.Session, view, tick, ct).ConfigureAwait(false);
            ArrayOf<DataValue> restored = await ReadSignalsAsync(connection.Session, nodes, ct).ConfigureAwait(false);
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.That(restored[i].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(restored[i].WrappedValue, Is.EqualTo(before[i].WrappedValue));
                Assert.That(restored[i].SourceTimestamp, Is.EqualTo(before[i].SourceTimestamp));
            }
            IRecyclingProcessViewAdapter adapter = host.Services.GetRequiredService<IRecyclingProcessViewAdapter>();
            CommittedSnapshot image = environment.Coordinator.Current;
            await adapter.ApplyAsync(image, ct).ConfigureAwait(false);
            await adapter.ApplyAsync(image, ct).ConfigureAwait(false);
            ArrayOf<DataValue> repeated = await ReadSignalsAsync(connection.Session, nodes, ct).ConfigureAwait(false);
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.That(repeated[i].WrappedValue, Is.EqualTo(before[i].WrappedValue));
                Assert.That(repeated[i].SourceTimestamp, Is.EqualTo(before[i].SourceTimestamp));
            }
        }

        /// <summary>The two explicit generic provenance labels in the zone fixture.</summary>
        private static readonly string[] s_expectedSourceZones = ["north", "south"];
    }
}
