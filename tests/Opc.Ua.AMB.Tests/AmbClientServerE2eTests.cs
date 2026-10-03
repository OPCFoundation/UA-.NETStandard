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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Drives a hosted Asset Management Basics server with the AMB client
    /// over real sessions: discovery, identification, the AssetId move,
    /// health alarms, maintenance, documentation links, locations and
    /// structure, a restart with persistence, and a Machinery machine as an
    /// asset.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class AmbClientServerE2eTests
    {
        private const string PumpUri = "urn:acme:pump:1";
        private const string MotorUri = "urn:acme:motor:1";
        private const string SensorUri = "urn:acme:sensor:7";
        private static readonly ExpandedNodeId s_eclass = new("0173-1#01-ADN228#012", "urn:example:eclass");
        private static readonly DateTimeUtc s_planned = new(new DateTime(2027, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
        private static readonly string[] s_levels = ["Plant1", "Plant1/Hall3", "Plant1/Hall3/Line2"];

        [Test]
        public async Task TheClientDiscoversAndReadsThePlantAsync()
        {
            await using Plant plant = await Plant.StartAsync(nameof(TheClientDiscoversAndReadsThePlantAsync), null)
                .ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(plant.Server, "discovery")
                .ConfigureAwait(false);
            AmbClient amb = connection.Client;

            ArrayOf<NodeId> assets = await amb.DiscoverAssetsAsync().ConfigureAwait(false);
            ArrayOf<AssetAlias> byUri = await amb
                .FindAssetsAsync("urn:acme:pump:%", AssetAliasCategory.ByProductInstanceUri)
                .ConfigureAwait(false);
            ArrayOf<AssetAlias> byAssetId = await amb.FindAssetsAsync("P-1", AssetAliasCategory.ByAssetId)
                .ConfigureAwait(false);
            var browsed = new List<AssetAlias>();
            await foreach (AssetAlias alias in amb
                .EnumerateAssetsAsync(AssetAliasCategory.Assets)
                .ConfigureAwait(false))
            {
                browsed.Add(alias);
            }
            NodeId pump = plant.Pump!.NodeId;
            AssetSnapshot snapshot = await amb.ReadAssetAsync(pump).ConfigureAwait(false);
            ArrayOf<AssetLocationNode> hierarchy = await amb.BrowseLocationsAsync(AssetLocationKind.Hierarchical)
                .ConfigureAwait(false);
            ArrayOf<AssetEntryRecord> requirements = await amb
                .ReadEntriesAsync(pump, AssetEntryFolder.Requirements)
                .ConfigureAwait(false);
            var subAssets = new List<ExpandedNodeId>();
            await foreach (ExpandedNodeId sub in amb.EnumerateSubAssetsAsync(pump).ConfigureAwait(false))
            {
                subAssets.Add(sub);
            }
            ArrayOf<AssetRelation> pumpRelations = await amb.ReadRelationsAsync(pump).ConfigureAwait(false);
            ArrayOf<AssetRelation> sensorRelations = await amb.ReadRelationsAsync(plant.Sensor!.NodeId)
                .ConfigureAwait(false);
            DataValue profiles = await ReadAsync(
                connection.Session,
                Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            DataValue units = await ReadAsync(
                connection.Session,
                Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(amb.IsSupported, Is.True);
                Assert.That(
                    assets.ToArray(),
                    Is.EquivalentTo(new[] { plant.Motor!.NodeId, pump, plant.Sensor.NodeId }));
                Assert.That(byUri.Count, Is.EqualTo(1));
                Assert.That(byUri[0].Name, Is.EqualTo(PumpUri));
                Assert.That(byAssetId.Count, Is.EqualTo(1));
                Assert.That(
                    browsed.Count(alias => alias.Category == AssetAliasCategory.ByProductInstanceUri),
                    Is.EqualTo(3));
                Assert.That(
                    browsed.Count(alias => alias.Category == AssetAliasCategory.ByAssetId),
                    Is.EqualTo(1),
                    "§8.2.3: the motor and the sensor have no AssetId and are not listed by it");

                Assert.That(snapshot.Identification.ProductInstanceUri, Is.EqualTo(PumpUri));
                Assert.That(snapshot.Identification.AssetId, Is.EqualTo("P-1"));
                Assert.That(snapshot.Identification.HardwareRevision, Is.EqualTo("HW-1"));
                Assert.That(snapshot.Identification.SoftwareRevision, Is.EqualTo("SW-1"));
                Assert.That(snapshot.Identification.RevisionCounter, Is.EqualTo(1));
                Assert.That(snapshot.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
                Assert.That(snapshot.HealthAlarms.Count, Is.EqualTo(1));
                Assert.That(snapshot.HealthAlarms[0].ConditionName, Is.EqualTo("Fieldbus"));
                Assert.That(snapshot.HealthAlarms[0].ConditionClass, Is.SameAs(AmbConditionClass.ConnectionFailure));
                Assert.That(snapshot.HealthAlarms[0].IsActive, Is.False);
                Assert.That(snapshot.HealthAlarms[0].PotentialRootCauses.Count, Is.EqualTo(1));
                Assert.That(snapshot.MaintenanceActivities.Count, Is.EqualTo(1));
                Assert.That(snapshot.MaintenanceActivities[0].State, Is.EqualTo(MaintenanceStateKind.Planned));
                Assert.That(snapshot.MaintenanceActivities[0].PlannedDate, Is.EqualTo(s_planned));
                Assert.That(snapshot.MaintenanceActivities[0].ConditionClass, Is.SameAs(AmbConditionClass.Inspection));
                Assert.That(snapshot.DocumentationLinks.Count, Is.EqualTo(2));
                Assert.That(
                    ListOf(snapshot.DocumentationLinks).Single(link => link.BrowseName.Name == "Manual").IsWritable,
                    Is.False);
                Assert.That(
                    ListOf(snapshot.DocumentationLinks).Single(link => link.BrowseName.Name == "Handbook").IsWritable,
                    Is.True);
                Assert.That(snapshot.Context.HierarchicalLocation, Is.EqualTo("Plant1/Hall3/Line2"));
                Assert.That(snapshot.Context.LocalTime?.Offset, Is.EqualTo((short)60));
                Assert.That(snapshot.Context.Classifications.ToArray(), Does.Contain(s_eclass));
                Assert.That(snapshot.Locations.Count, Is.EqualTo(1));

                Assert.That(
                    ListOf(hierarchy).Select(location => location.Path),
                    Is.EqualTo(s_levels));
                Assert.That(hierarchy[2].Assets.ToArray(), Does.Contain((ExpandedNodeId)pump));
                Assert.That(snapshot.Locations[0].Location, Is.EqualTo(hierarchy[2].NodeId));
                Assert.That(requirements.Count, Is.EqualTo(1));
                Assert.That(requirements[0].Value.TryGetValue(out double voltage) ? voltage : 0, Is.EqualTo(24.0));
                Assert.That(subAssets, Is.EqualTo(new[] { (ExpandedNodeId)plant.Motor.NodeId }));
                Assert.That(
                    ListOf(sensorRelations).Select(relation => relation.ReferenceTypeId),
                    Does.Contain(Ua.ReferenceTypeIds.Utilizes));
                Assert.That(
                    ListOf(pumpRelations).Any(relation =>
                        relation.IsInverse && relation.Target == (ExpandedNodeId)plant.Sensor.NodeId),
                    Is.True,
                    "the pump sees the inverse Utilizes of the sensor");

                Assert.That(
                    profiles.WrappedValue.TryGetValue(out ArrayOf<string> profileUris) ? profileUris.ToArray() : [],
                    Does.Contain("http://opcfoundation.org/UA-Profile/AMB/Server/BaseServer"));
                Assert.That(
                    units.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> unitNames) ? unitNames.ToArray() : [],
                    Is.SupersetOf(new[]
                    {
                        new QualifiedName("AMB Asset Identification"),
                        new QualifiedName("AMB Asset Discovery by AssetId"),
                        new QualifiedName("AMB Hierarchical Location Objects"),
                        new QualifiedName("AMB Sub-assets"),
                        new QualifiedName("AMB Asset relations")
                    }));
            });
        }

        [Test]
        public async Task WritingTheAssetIdMovesTheAssetAsync()
        {
            await using Plant plant = await Plant.StartAsync(nameof(WritingTheAssetIdMovesTheAssetAsync), null)
                .ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(plant.Server, "assetid")
                .ConfigureAwait(false);
            AmbClient amb = connection.Client;
            NodeId pump = plant.Pump!.NodeId;
            string? before = await amb.ReadNodeVersionAsync(AssetAliasCategory.ByAssetId).ConfigureAwait(false);

            int writes = 0;
            string written = string.Empty;
            using var cts = new CancellationTokenSource(s_timeout);
            await using IAsyncEnumerator<AssetSetChange> changes = amb
                .ObserveAssetSetChangesAsync(cancellationToken: cts.Token)
                .GetAsyncEnumerator(cts.Token);
            AssetSetChange change = await NextAsync(
                changes,
                async () =>
                {
                    written = "P-" + (++writes + 1);
                    await amb.WriteAssetIdAsync(pump, written, cts.Token).ConfigureAwait(false);
                },
                candidate => candidate.Category == AssetAliasCategory.ByAssetId,
                cts.Token).ConfigureAwait(false);

            string? after = await amb.ReadNodeVersionAsync(AssetAliasCategory.ByAssetId).ConfigureAwait(false);
            ArrayOf<AssetAlias> moved = await amb.FindAssetsAsync(written, AssetAliasCategory.ByAssetId)
                .ConfigureAwait(false);
            ArrayOf<AssetAlias> old = await amb.FindAssetsAsync("P-1", AssetAliasCategory.ByAssetId)
                .ConfigureAwait(false);
            AssetIdentificationRecord identification = await amb.ReadIdentificationAsync(pump).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(change.Category, Is.EqualTo(AssetAliasCategory.ByAssetId));
                Assert.That(after, Is.Not.EqualTo(before));
                Assert.That(moved.Count, Is.EqualTo(1));
                Assert.That(old.Count, Is.Zero);
                Assert.That(identification.AssetId, Is.EqualTo(written));
            });
            ServiceResultException tooLong = Assert.ThrowsAsync<ServiceResultException>(
                async () => await amb.WriteAssetIdAsync(pump, new string('x', 300)).ConfigureAwait(false))!;
            ServiceResultException none = Assert.ThrowsAsync<ServiceResultException>(
                async () => await amb.WriteAssetIdAsync(plant.Motor!.NodeId, "M-1").ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(tooLong.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(none.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown), "the motor has no AssetId");
            });
        }

        [Test]
        public async Task TheClientFollowsAndAcknowledgesAHealthAlarmAsync()
        {
            await using Plant plant = await Plant
                .StartAsync(nameof(TheClientFollowsAndAcknowledgesAHealthAlarmAsync), null)
                .ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(plant.Server, "alarms")
                .ConfigureAwait(false);
            AmbClient amb = connection.Client;
            IAssetHealthAlarm fieldbus = plant.Pump!.Health!.Alarms[0];
            var cable = new NodeId("Cable", plant.Server.Manager.InstanceNamespaceIndex);

            using var cts = new CancellationTokenSource(s_timeout);
            await using IAsyncEnumerator<AssetAlarmRecord> alarms = amb
                .ObserveHealthAlarmsAsync(cancellationToken: cts.Token)
                .GetAsyncEnumerator(cts.Token);
            AssetAlarmRecord raised = await NextAsync(
                alarms,
                async () => await fieldbus.RaiseAsync(
                    700,
                    new LocalizedText("The fieldbus is down."),
                    [AssetRootCauses.Of(cable, new LocalizedText("Cable cut"))],
                    cts.Token).ConfigureAwait(false),
                record => record.IsActive,
                cts.Token).ConfigureAwait(false);
            DeviceHealthEnumeration? derived = await amb.ReadDeviceHealthAsync(plant.Pump.NodeId).ConfigureAwait(false);

            await amb.AcknowledgeAsync(raised.ConditionId, raised.EventId, new LocalizedText("seen"), cts.Token)
                .ConfigureAwait(false);
            AssetAlarmRecord acknowledged = await NextAsync(
                alarms,
                () => Task.CompletedTask,
                record => record.IsAcknowledged,
                cts.Token).ConfigureAwait(false);
            await fieldbus.ClearAsync(cancellationToken: cts.Token).ConfigureAwait(false);
            AssetAlarmRecord cleared = await NextAsync(
                alarms,
                () => Task.CompletedTask,
                record => !record.IsActive,
                cts.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(raised.SourceNode, Is.EqualTo(plant.Pump.NodeId));
                Assert.That(raised.SourceName, Is.EqualTo("Pump"));
                Assert.That(raised.Severity, Is.EqualTo((ushort)700));
                Assert.That(raised.FaultCategory, Is.EqualTo(AssetFaultSeverity.MajorRecoverableFault));
                Assert.That(raised.ConditionClass, Is.SameAs(AmbConditionClass.ConnectionFailure));
                Assert.That(raised.Message.Text, Is.EqualTo("The fieldbus is down."));
                Assert.That(raised.PotentialRootCauses.Count, Is.EqualTo(1));
                Assert.That(raised.PotentialRootCauses[0].RootCauseId, Is.EqualTo(cable));
                Assert.That(
                    raised.EventType,
                    Is.EqualTo(new NodeId("AssetFailureAlarmType", plant.Server.Manager.TypeNamespaceIndex)));
                Assert.That(derived, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
                Assert.That(acknowledged.IsActive, Is.True);
                Assert.That(cleared.Retain, Is.False, "acknowledged and cleared");
            });
        }

        [Test]
        public async Task AConditionRefreshFindsTheConditionsOfAnAssetAsync()
        {
            await using Plant plant = await Plant
                .StartAsync(nameof(AConditionRefreshFindsTheConditionsOfAnAssetAsync), null)
                .ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(plant.Server, "refresh")
                .ConfigureAwait(false);
            AmbClient amb = connection.Client;
            IAssetHandle pump = plant.Pump!;
            IAssetHealthAlarm bearing = pump.Health!.Alarms[0];
            await bearing.RaiseAsync(450, new LocalizedText("Bearing hot")).ConfigureAwait(false);

            List<Variant[]> refreshed = await amb.RefreshConditionsAsync(pump.NodeId, CancellationToken.None)
                .ConfigureAwait(false);
            HashSet<NodeId> classes = await amb.MaintenanceClassesAsync(CancellationToken.None).ConfigureAwait(false);
            AssetAlarmRecord[] alarms =
            [
                .. refreshed
                    .Where(values => !AmbConditionFields.IsMaintenance(values, classes))
                    .Select(values => AmbConditionFields.ToAlarm(values, amb.Session.MessageContext))
            ];
            MaintenanceActivityRecord[] activities =
            [
                .. refreshed
                    .Where(values => AmbConditionFields.IsMaintenance(values, classes))
                    .Select(values => AmbConditionFields.ToMaintenance(values, amb.Session.MessageContext))
            ];

            Assert.Multiple(() =>
            {
                Assert.That(alarms.Select(alarm => alarm.ConditionName), Does.Contain(bearing.Name));
                Assert.That(alarms.All(alarm => alarm.SourceNode == pump.NodeId), Is.True);
                Assert.That(activities, Is.Not.Empty, "the planned activity is retained");
                Assert.That(activities[0].State, Is.EqualTo(MaintenanceStateKind.Planned));
            });
        }

        [Test]
        public async Task TheClientFollowsAMaintenanceActivityAsync()
        {
            await using Plant plant = await Plant.StartAsync(nameof(TheClientFollowsAMaintenanceActivityAsync), null)
                .ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(plant.Server, "maintenance")
                .ConfigureAwait(false);
            AmbClient amb = connection.Client;
            IMaintenanceActivity inspection = plant.Pump!.Maintenance!.Activities[0];

            using var cts = new CancellationTokenSource(s_timeout);
            await using IAsyncEnumerator<MaintenanceActivityRecord> activities = amb
                .ObserveMaintenanceAsync(cancellationToken: cts.Token)
                .GetAsyncEnumerator(cts.Token);
            MaintenanceActivityRecord planned = await NextAsync(
                activities,
                async () => await inspection
                    .UpdateAsync(details => details.EstimatedDowntime = TimeSpan.FromHours(3), default, cts.Token)
                    .ConfigureAwait(false),
                record => record.State == MaintenanceStateKind.Planned,
                cts.Token).ConfigureAwait(false);
            await inspection.StartAsync(cancellationToken: cts.Token).ConfigureAwait(false);
            MaintenanceActivityRecord executing = await NextAsync(
                activities,
                () => Task.CompletedTask,
                record => record.State == MaintenanceStateKind.Executing,
                cts.Token).ConfigureAwait(false);
            await inspection.FinishAsync(new LocalizedText("No findings."), cts.Token).ConfigureAwait(false);
            MaintenanceActivityRecord finished = await NextAsync(
                activities,
                () => Task.CompletedTask,
                record => record.State == MaintenanceStateKind.Finished,
                cts.Token).ConfigureAwait(false);
            await amb.AcknowledgeAsync(finished.ConditionId, finished.EventId, new LocalizedText("done"), cts.Token)
                .ConfigureAwait(false);
            MaintenanceActivityRecord acknowledged = await NextAsync(
                activities,
                () => Task.CompletedTask,
                record => record.IsAcknowledged,
                cts.Token).ConfigureAwait(false);
            ArrayOf<MaintenanceActivityRecord> read = await amb.ReadMaintenanceActivitiesAsync(plant.Pump.NodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(planned.ConditionName, Is.EqualTo("AnnualInspection"));
                Assert.That(planned.PlannedDate, Is.EqualTo(s_planned));
                Assert.That(planned.EstimatedDowntime, Is.EqualTo(TimeSpan.FromHours(3)));
                Assert.That(planned.IsActive, Is.True);
                Assert.That(executing.IsActive, Is.True);
                Assert.That(finished.IsActive, Is.False);
                Assert.That(finished.Message.Text, Is.EqualTo("No findings."));
                Assert.That(finished.Retain, Is.True, "retained until acknowledged");
                Assert.That(acknowledged.Retain, Is.False);
                Assert.That(read[0].State, Is.EqualTo(MaintenanceStateKind.Finished));
                Assert.That(read[0].IsAcknowledged, Is.True);
            });
        }

        [Test]
        public async Task LinksAndTheAssetIdSurviveARestartAsync()
        {
            string state = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AmbClientServerE2eTests),
                Guid.NewGuid().ToString("N"));
            const string name = nameof(LinksAndTheAssetIdSurviveARestartAsync);
            NodeId added;
            await using (Plant first = await Plant.StartAsync(name, state).ConfigureAwait(false))
            {
                await using Connection connection = await Connection.OpenAsync(first.Server, "first")
                    .ConfigureAwait(false);
                AmbClient amb = connection.Client;
                NodeId pump = first.Pump!.NodeId;
                added = await amb.AddDocumentationLinkAsync(
                    pump,
                    "https://plant.example/wiring",
                    new QualifiedName("Wiring", first.Server.Manager.InstanceNamespaceIndex),
                    new LocalizedText("Wiring diagram")).ConfigureAwait(false);
                ArrayOf<DocumentationLinkRecord> links = await amb.ReadDocumentationLinksAsync(pump)
                    .ConfigureAwait(false);
                await amb.WriteDocumentationLinkAsync(
                    ListOf(links).Single(link => link.BrowseName.Name == "Handbook").NodeId,
                    "https://plant.example/handbook").ConfigureAwait(false);
                await amb.WriteAssetIdAsync(pump, "P-42").ConfigureAwait(false);
                ServiceResultException manual = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await amb.RemoveDocumentationLinkAsync(
                        pump,
                        ListOf(links).Single(link => link.BrowseName.Name == "Manual").NodeId).ConfigureAwait(false))!;
                Assert.That(manual.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }

            await using Plant second = await Plant.StartAsync(name, state).ConfigureAwait(false);
            await using Connection reconnected = await Connection.OpenAsync(second.Server, "second")
                .ConfigureAwait(false);
            AmbClient client = reconnected.Client;
            NodeId restarted = second.Pump!.NodeId;
            ArrayOf<DocumentationLinkRecord> restored = await client.ReadDocumentationLinksAsync(restarted)
                .ConfigureAwait(false);
            ArrayOf<AssetAlias> byAssetId = await client.FindAssetsAsync("P-42", AssetAliasCategory.ByAssetId)
                .ConfigureAwait(false);
            await client.RemoveDocumentationLinkAsync(restarted, added).ConfigureAwait(false);
            ArrayOf<DocumentationLinkRecord> afterRemoval = await client.ReadDocumentationLinksAsync(restarted)
                .ConfigureAwait(false);
            ServiceResultException noAddIn = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.AddDocumentationLinkAsync(
                    second.Motor!.NodeId,
                    "https://x",
                    new QualifiedName("X")).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    ListOf(restored).Single(link => link.NodeId == added).Uri,
                    Is.EqualTo("https://plant.example/wiring"),
                    "the added link keeps its NodeId");
                Assert.That(
                    ListOf(restored).Single(link => link.BrowseName.Name == "Handbook").Uri,
                    Is.EqualTo("https://plant.example/handbook"));
                Assert.That(byAssetId.Count, Is.EqualTo(1));
                Assert.That(afterRemoval.Count, Is.EqualTo(2));
                Assert.That(noAddIn.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            });
        }

        [Test]
        public async Task AMachineryMachineIsAnAssetAsync()
        {
            IMachineHandle<BaseObjectState>? machine = null;
            IAssetHandle? asset = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AMachineryMachineIsAnAssetAsync),
                builder => builder
                    .AddMachinery()
                    .AddAssetManagement()
                    .ConfigureMachinery(async context =>
                    {
                        machine = await context
                            .AddMachine(new QualifiedName("Press"))
                            .WithIdentification(identification =>
                            {
                                identification.Manufacturer = new LocalizedText("Acme");
                                identification.SerialNumber = "SN-1";
                                identification.ProductInstanceUri = "urn:acme:press:1";
                            })
                            .BuildAsync(context.CancellationToken)
                            .ConfigureAwait(false);
                    })
                    .ConfigureDevicesFor<MachineryNodeManager>(async context =>
                    {
                        asset = await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                            machine!.AsNode(),
                            builder => builder.WithConfigurableAssetId("PRESS-1"),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);
            await using Connection connection = await Connection.OpenAsync(server, "machinery").ConfigureAwait(false);
            AmbClient amb = connection.Client;

            ArrayOf<NodeId> assets = await amb.DiscoverAssetsAsync().ConfigureAwait(false);
            AssetIdentificationRecord identification = await amb.ReadIdentificationAsync(asset!.NodeId)
                .ConfigureAwait(false);
            await amb.WriteAssetIdAsync(asset.NodeId, "PRESS-2").ConfigureAwait(false);
            ArrayOf<AssetAlias> moved = await amb.FindAssetsAsync("PRESS-2", AssetAliasCategory.ByAssetId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(assets.ToArray(), Is.EqualTo(new[] { asset.NodeId }));
                Assert.That(identification.ProductInstanceUri, Is.EqualTo("urn:acme:press:1"));
                Assert.That(identification.SerialNumber, Is.EqualTo("SN-1"));
                Assert.That(identification.Manufacturer.Text, Is.EqualTo("Acme"));
                Assert.That(identification.AssetId, Is.EqualTo("PRESS-1"));
                Assert.That(moved.Count, Is.EqualTo(1));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Identification")),
                    "the machine publishes its ProductInstanceUri in its Identification group");
            });
        }

        /// <summary>
        /// Reads stream entries, poking the server until one matches: an event
        /// stream says nothing until its monitored item is up, so the poke
        /// repeats until the first matching event arrives.
        /// </summary>
        /// <typeparam name="T">The type of the stream entries.</typeparam>
        private static async Task<T> NextAsync<T>(
            IAsyncEnumerator<T> stream,
            Func<Task> poke,
            Func<T, bool> match,
            CancellationToken cancellationToken)
        {
            Task<bool> next = stream.MoveNextAsync().AsTask();
            while (true)
            {
                Task done = await Task.WhenAny(next, Task.Delay(500, cancellationToken)).ConfigureAwait(false);
                if (done == next)
                {
                    Assert.That(await next.ConfigureAwait(false), Is.True, "the stream ended");
                    if (match(stream.Current))
                    {
                        return stream.Current;
                    }
                    next = stream.MoveNextAsync().AsTask();
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                await poke().ConfigureAwait(false);
            }
        }

        private static List<T> ListOf<T>(ArrayOf<T> items)
        {
            var list = new List<T>(items.Count);
            foreach (T item in items)
            {
                list.Add(item);
            }
            return list;
        }

        private static async Task<DataValue> ReadAsync(ManagedSession session, NodeId nodeId)
        {
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        /// <summary>
        /// A hosted server with three Device Integration devices: a motor, a
        /// pump that contains it and uses every AMB building block, and a
        /// sensor that utilizes the pump.
        /// </summary>
        private sealed class Plant : IAsyncDisposable
        {
            private Plant(AmbHostedServer server)
            {
                Server = server;
            }

            public AmbHostedServer Server { get; }

            public IAssetHandle? Motor { get; private set; }

            public IAssetHandle? Pump { get; private set; }

            public IAssetHandle? Sensor { get; private set; }

            public static async Task<Plant> StartAsync(string name, string? state)
            {
                IAssetHandle? motor = null;
                IAssetHandle? pump = null;
                IAssetHandle? sensor = null;
                AmbHostedServer server = await AmbHostedServer.StartAsync(
                    name,
                    builder => builder
                        .AddOpcUaDi()
                        .AddAssetManagement(options =>
                        {
                            // AddLink needs a persistent store (§10.5.3).
                            options.UseFileSystemStores(state ??
                                Path.Combine(
                                    TestContext.CurrentContext.WorkDirectory,
                                    nameof(AmbClientServerE2eTests),
                                    Guid.NewGuid().ToString("N")));
                            options.AuthorizeLinkEdit = _ => true;
                        })
                        .ConfigureDevicesFor<DiNodeManager>(async context =>
                        {
                            IAssetManagement assets = context.GetRequiredService<IAssetManagement>();
                            IDeviceBuilder<DeviceState> motorDevice = await AssetRegistrationTests
                                .CreateDeviceAsync(context, "Motor", MotorUri).ConfigureAwait(false);
                            motor = await motorDevice.RegisterAsAssetAsync(assets).ConfigureAwait(false);

                            IDeviceBuilder<DeviceState> pumpDevice = await AssetRegistrationTests
                                .CreateDeviceAsync(context, "Pump", PumpUri).ConfigureAwait(false);
                            pump = await pumpDevice.RegisterAsAssetAsync(
                                assets,
                                asset => asset
                                    .WithConfigurableAssetId("P-1")
                                    .WithDeviceHealth(deriveFromAlarms: true)
                                    .WithHealthAlarm(
                                        "Fieldbus",
                                        AssetHealthAlarmKind.Failure,
                                        AmbConditionClass.ConnectionFailure)
                                    .WithMaintenance(
                                        "AnnualInspection",
                                        AmbConditionClass.Inspection,
                                        details =>
                                        {
                                            details.Description = new LocalizedText("Annual inspection.");
                                            details.PlannedDate = s_planned;
                                        })
                                    .WithDocumentationLinks(links => links
                                        .Add("Manual", "https://acme.example/pump/manual.pdf")
                                        .AddEditable("Handbook")
                                        .AllowUserLinks())
                                    .WithVersionInformation("HW-1", "SW-1", 1)
                                    .LocatedIn(AssetLocationKind.Hierarchical, "Plant1/Hall3/Line2")
                                    .WithLocation(AssetLocationKind.Hierarchical, "Plant1/Hall3/Line2")
                                    .WithLocalTime(60)
                                    .ClassifiedAs(s_eclass)
                                    .WithRequirements(requirements => requirements
                                        .Add("SupplyVoltage", Variant.From(24.0)))
                                    .RelatesTo(Ua.ReferenceTypeIds.HasComponent, motor.NodeId),
                                context.CancellationToken).ConfigureAwait(false);

                            IDeviceBuilder<DeviceState> sensorDevice = await AssetRegistrationTests
                                .CreateDeviceAsync(context, "Sensor", SensorUri).ConfigureAwait(false);
                            sensor = await sensorDevice.RegisterAsAssetAsync(
                                assets,
                                asset => asset.RelatesTo(Ua.ReferenceTypeIds.Utilizes, pump.NodeId),
                                context.CancellationToken).ConfigureAwait(false);
                        })).ConfigureAwait(false);
                return new Plant(server) { Motor = motor, Pump = pump, Sensor = sensor };
            }

            public ValueTask DisposeAsync()
            {
                return Server.DisposeAsync();
            }
        }

        /// <summary>
        /// A managed session with the AMB client on it.
        /// </summary>
        private sealed class Connection : IAsyncDisposable
        {
            private Connection(ClientFixture fixture, ManagedSession session, AmbClient client)
            {
                m_fixture = fixture;
                Session = session;
                Client = client;
            }

            public ManagedSession Session { get; }

            public AmbClient Client { get; }

            public static async Task<Connection> OpenAsync(AmbHostedServer server, string name)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var fixture = new ClientFixture(telemetry);
                await fixture.LoadClientConfigurationAsync(Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(AmbClientServerE2eTests),
                    Guid.NewGuid().ToString("N"),
                    "client-pki")).ConfigureAwait(false);
                EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                    fixture.Config,
                    server.EndpointUrl,
                    useSecurity: false,
                    discoverTimeout: 15000,
                    telemetry).ConfigureAwait(false);
                Assert.That(endpoint, Is.Not.Null);
                ManagedSession session = await new ManagedSessionBuilder(fixture.Config, telemetry)
                    .UseEndpoint(new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(fixture.Config)))
                    .WithSessionName(name)
                    .ConnectAsync().ConfigureAwait(false);
                return new Connection(fixture, session, session.AssetManagement(telemetry));
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_fixture.Dispose();
                }
            }

            private readonly ClientFixture m_fixture;
        }
    }
}
