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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.AMB.Server;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Gives registered assets their <c>DeviceHealth</c> and health alarms
    /// (OPC 10000-110 §9) and drives them in a hosted server.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class HealthAlarmTests
    {
        private const string SensorUri = "urn:acme:sensor:4711";

        private static readonly QualifiedName[] s_healthUnits =
        [
            new("AMB Asset Health Status Base"),
            new("AMB Asset Health Status Alarms"),
            new("AMB Asset Health Status Root Causes"),
            new("AMB Asset Health Status Alarm Categories")
        ];

        [Test]
        public async Task AHealthAlarmIsAnInstanceOfTheServerSpecificTypeAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await StartWithSensorAsync(
                nameof(AHealthAlarmIsAnInstanceOfTheServerSpecificTypeAsync),
                health => health
                    .WithDeviceHealth()
                    .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;

            IAssetHealth health = asset.Health!;
            Assert.That(health.TryGetAlarm("Fieldbus", out IAssetHealthAlarm? fieldbus), Is.True);
            AlarmConditionState alarm = await server.FindNodeAsync<AlarmConditionState>(fieldbus!.NodeId)
                .ConfigureAwait(false);
            var typeId = new NodeId("AssetFailureAlarmType", server.Manager.TypeNamespaceIndex);
            NodeId diFailure = server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.FailureAlarmType);
            NodeId rootCauseInterface = server.ToNodeId(ObjectTypeIds.IRootCauseIndicationType);

            IReadOnlyList<ReferenceDescription> rootCauses = await server
                .BrowseAsync(alarm.NodeId, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false);
            ReferenceDescription potentialRootCauses = rootCauses.Single(reference =>
                reference.BrowseName == new QualifiedName("PotentialRootCauses", server.Manager.AmbNamespaceIndex));
            DataValue rootCauseValue = await server.ReadAsync(server.ToNodeId(potentialRootCauses.NodeId))
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> listed = await server
                .BrowseAsync(device.DeviceHealthAlarms!.NodeId, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);

            BaseObjectTypeState type = server.Manager.FindPredefinedNode<BaseObjectTypeState>(typeId)!;
            IReadOnlyList<ReferenceDescription> interfaces = await server
                .BrowseAsync(typeId, Ua.ReferenceTypeIds.HasInterface)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> subtypes = await server
                .BrowseAsync(diFailure, Ua.ReferenceTypeIds.HasSubtype)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> declarations = await server
                .BrowseAsync(typeId, Ua.ReferenceTypeIds.HasProperty)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(health.Alarms.Count, Is.EqualTo(1));
                Assert.That(fieldbus.Kind, Is.EqualTo(AssetHealthAlarmKind.Failure));
                Assert.That(fieldbus.ConditionClass, Is.SameAs(AmbConditionClass.ConnectionFailure));
                Assert.That(alarm, Is.InstanceOf<FailureAlarmState>());
                Assert.That(alarm.TypeDefinitionId, Is.EqualTo(typeId));
                Assert.That(alarm.EventType!.Value, Is.EqualTo(typeId));
                Assert.That(server.Server.TypeTree.IsTypeOf(typeId, diFailure), Is.True);
                Assert.That(alarm.SourceNode!.Value, Is.EqualTo(device.NodeId), "§9.3: the source is the asset");
                Assert.That(alarm.SourceName!.Value, Is.EqualTo("Sensor"));
                Assert.That(alarm.ConditionName!.Value, Is.EqualTo("Fieldbus"));
                Assert.That(
                    alarm.ConditionClassId!.Value,
                    Is.EqualTo(AmbConditionClass.ConnectionFailure.GetTypeId(server.Server.NamespaceUris)));
                Assert.That(alarm.ConditionClassName!.Value.Text, Is.EqualTo("ConnectionFailureConditionClassType"));
                Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                Assert.That(alarm.Retain!.Value, Is.False);
                Assert.That(alarm.Severity!.Value, Is.EqualTo(AssetHealth.InactiveSeverity));
                Assert.That(fieldbus.IsActive, Is.False);
                Assert.That(
                    rootCauseValue.WrappedValue.TryGetStructure(out ArrayOf<RootCauseDataType> unknown),
                    Is.True);
                Assert.That(unknown.Count, Is.EqualTo(1));
                Assert.That(unknown[0].RootCauseId.IsNull, Is.True);
                Assert.That(unknown[0].RootCause.Text, Is.EqualTo(AssetRootCauses.UnknownText));
                Assert.That(listed.Select(reference => reference.NodeId), Does.Contain((ExpandedNodeId)alarm.NodeId));

                Assert.That(type, Is.Not.Null);
                Assert.That(type.SuperTypeId, Is.EqualTo(diFailure));
                Assert.That(type.IsAbstract, Is.False);
                Assert.That(
                    interfaces.Select(reference => reference.NodeId),
                    Does.Contain((ExpandedNodeId)rootCauseInterface));
                Assert.That(subtypes.Select(reference => reference.NodeId), Does.Contain((ExpandedNodeId)typeId));
                Assert.That(
                    declarations.Select(reference => reference.BrowseName),
                    Does.Contain(new QualifiedName("PotentialRootCauses", server.Manager.AmbNamespaceIndex)));

                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
                Assert.That(device.DeviceHealth, Is.Not.Null);
                Assert.That(server.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(s_healthUnits));
            });
        }

        [Test]
        public async Task RaisingAndClearingFollowTheSeverityBandsAndRetainAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await StartWithSensorAsync(
                nameof(RaisingAndClearingFollowTheSeverityBandsAndRetainAsync),
                health => health.WithHealthAlarm(
                    "Fieldbus",
                    AssetHealthAlarmKind.Failure,
                    AmbConditionClass.ConnectionFailure))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IAssetHealth health = asset.Health!;
            IAssetHealthAlarm fieldbus = health.Alarms[0];
            AlarmConditionState alarm = await server.FindNodeAsync<AlarmConditionState>(fieldbus.NodeId)
                .ConfigureAwait(false);
            var cable = new NodeId("Cable", server.Manager.InstanceNamespaceIndex);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await fieldbus.RaiseAsync(AssetFaultSeverities.InactiveMaximum, new LocalizedText("x")).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await fieldbus.RaiseAsync(1001, new LocalizedText("x")).ConfigureAwait(false));

            await fieldbus.RaiseAsync(
                AssetFaultSeverities.MinimumOf(AssetFaultSeverity.MajorRecoverableFault),
                new LocalizedText("The fieldbus is down."),
                [AssetRootCauses.Of(cable, new LocalizedText("Cable cut"))]).ConfigureAwait(false);
            bool activeAfterRaise = fieldbus.IsActive;
            ushort severityAfterRaise = fieldbus.Severity;
            DeviceHealthEnumeration? healthAfterRaise = health.DeviceHealth;
            ArrayOf<RootCauseDataType> given = fieldbus.PotentialRootCauses;
            bool retainAfterRaise = alarm.Retain!.Value;
            bool ackedAfterRaise = alarm.AckedState!.Id!.Value;
            string? message = alarm.Message!.Value.Text;

            await fieldbus.RaiseAsync(900, default).ConfigureAwait(false);
            ArrayOf<RootCauseDataType> unknown = fieldbus.PotentialRootCauses;
            await fieldbus.RaiseAsync(950, default, AssetRootCauses.Self).ConfigureAwait(false);
            ArrayOf<RootCauseDataType> self = fieldbus.PotentialRootCauses;

            await fieldbus.ClearAsync().ConfigureAwait(false);
            bool retainedUnacknowledged = alarm.Retain.Value;
            await fieldbus.ClearAsync().ConfigureAwait(false);

            CallMethodResult acknowledged = await server.CallAsync(
                alarm.NodeId,
                ((AcknowledgeableConditionState)alarm).Acknowledge!.NodeId,
                Variant.From(alarm.EventId!.Value),
                Variant.From(new LocalizedText("seen"))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(activeAfterRaise, Is.True);
                Assert.That(severityAfterRaise, Is.EqualTo((ushort)601));
                Assert.That(given.Count, Is.EqualTo(1));
                Assert.That(given[0].RootCauseId, Is.EqualTo(cable));
                Assert.That(retainAfterRaise, Is.True);
                Assert.That(ackedAfterRaise, Is.False);
                Assert.That(message, Is.EqualTo("The fieldbus is down."));
                Assert.That(unknown.Count, Is.EqualTo(1));
                Assert.That(unknown[0].RootCause.Text, Is.EqualTo(AssetRootCauses.UnknownText));
                Assert.That(self.Count, Is.Zero, "§9.4.2: the alarm itself is the root cause");
                Assert.That(fieldbus.IsActive, Is.False);
                Assert.That(fieldbus.Severity, Is.EqualTo(AssetHealth.InactiveSeverity));
                Assert.That(AssetFaultSeverities.IsInactive(fieldbus.Severity), Is.True);
                Assert.That(retainedUnacknowledged, Is.True, "an unacknowledged alarm stays retained");
                Assert.That(StatusCode.IsGood(acknowledged.StatusCode), Is.True, acknowledged.StatusCode.ToString());
                Assert.That(alarm.Retain.Value, Is.False);
                Assert.That(
                    healthAfterRaise,
                    Is.EqualTo(DeviceHealthEnumeration.FAILURE),
                    "§9.2: an asset with health alarms has DeviceHealth, which follows them");
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.NORMAL));
                Assert.That(health.DerivesFromAlarms, Is.True);
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Health Status Base")));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Health Status Alarms")));
            });
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await health.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE).ConfigureAwait(false));
        }

        [Test]
        public async Task DeviceHealthFollowsTheActiveAlarmsWhenDerivedAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await StartWithSensorAsync(
                nameof(DeviceHealthFollowsTheActiveAlarmsWhenDerivedAsync),
                health => health
                    .WithDeviceHealth(deriveFromAlarms: true)
                    .WithHealthAlarm("Wear", AssetHealthAlarmKind.MaintenanceRequired, AmbConditionClass.CalibrationDue)
                    .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IAssetHealth health = asset.Health!;
            Assert.That(health.TryGetAlarm("Wear", out IAssetHealthAlarm? wear), Is.True);
            Assert.That(health.TryGetAlarm("Fieldbus", out IAssetHealthAlarm? fieldbus), Is.True);
            Assert.That(health.TryGetAlarm("Other", out _), Is.False);
            NodeId deviceHealth = device.DeviceHealth!.NodeId;

            var observed = new List<DeviceHealthEnumeration?> { health.DeviceHealth };
            DataValue before = await server.ReadAsync(deviceHealth).ConfigureAwait(false);
            await Task.Delay(20).ConfigureAwait(false);
            await wear!.RaiseAsync(350, new LocalizedText("Calibration due.")).ConfigureAwait(false);
            observed.Add(health.DeviceHealth);
            DataValue maintenance = await server.ReadAsync(deviceHealth).ConfigureAwait(false);
            await fieldbus!.RaiseAsync(900, new LocalizedText("Fieldbus down.")).ConfigureAwait(false);
            observed.Add(health.DeviceHealth);
            await fieldbus.ClearAsync().ConfigureAwait(false);
            observed.Add(health.DeviceHealth);
            await wear.ClearAsync().ConfigureAwait(false);
            observed.Add(health.DeviceHealth);

            Assert.Multiple(() =>
            {
                Assert.That(health.DerivesFromAlarms, Is.True);
                Assert.That(
                    observed,
                    Is.EqualTo(new DeviceHealthEnumeration?[]
                    {
                        DeviceHealthEnumeration.NORMAL,
                        DeviceHealthEnumeration.MAINTENANCE_REQUIRED,
                        DeviceHealthEnumeration.FAILURE,
                        DeviceHealthEnumeration.MAINTENANCE_REQUIRED,
                        DeviceHealthEnumeration.NORMAL
                    }));
                Assert.That(maintenance.SourceTimestamp, Is.GreaterThan(before.SourceTimestamp));
                Assert.That(
                    maintenance.WrappedValue.TryGetValue(out int value) ? (DeviceHealthEnumeration)value : default,
                    Is.EqualTo(DeviceHealthEnumeration.MAINTENANCE_REQUIRED));
            });
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await health.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE).ConfigureAwait(false));
        }

        [Test]
        public async Task SettingDeviceHealthMovesTheTimestampOnlyWithTheValueAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await StartWithSensorAsync(
                nameof(SettingDeviceHealthMovesTheTimestampOnlyWithTheValueAsync),
                health => health.WithDeviceHealth(DeviceHealthEnumeration.OFF_SPEC))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            NodeId deviceHealth = device.DeviceHealth!.NodeId;

            IAssetHealth health = asset.Health!;
            DataValue initial = await server.ReadAsync(deviceHealth).ConfigureAwait(false);
            await Task.Delay(20).ConfigureAwait(false);
            await health.SetDeviceHealthAsync(DeviceHealthEnumeration.OFF_SPEC).ConfigureAwait(false);
            DataValue same = await server.ReadAsync(deviceHealth).ConfigureAwait(false);
            await health.SetDeviceHealthAsync(DeviceHealthEnumeration.FAILURE).ConfigureAwait(false);
            DataValue changed = await server.ReadAsync(deviceHealth).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(health.Alarms.Count, Is.Zero);
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.FAILURE));
                Assert.That(same.SourceTimestamp, Is.EqualTo(initial.SourceTimestamp));
                Assert.That(changed.SourceTimestamp, Is.GreaterThan(initial.SourceTimestamp));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Health Status Base")));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Asset Health Status Alarms")));
            });
        }

        [Test]
        public async Task WithoutServerDefinedTypesTheAlarmReferencesTheInterfaceAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await StartWithSensorAsync(
                nameof(WithoutServerDefinedTypesTheAlarmReferencesTheInterfaceAsync),
                health => health.WithHealthAlarm(
                    "Check",
                    AssetHealthAlarmKind.CheckFunction,
                    AmbConditionClass.SelfTestFailure),
                options => options.UseServerDefinedAlarmTypes = false)
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            AlarmConditionState alarm = await server.FindNodeAsync<AlarmConditionState>(asset.Health!.Alarms[0].NodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    alarm.TypeDefinitionId,
                    Is.EqualTo(server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.CheckFunctionAlarmType)));
                Assert.That(
                    alarm.ReferenceExists(
                        Ua.ReferenceTypeIds.HasInterface,
                        false,
                        server.ToNodeId(ObjectTypeIds.IRootCauseIndicationType)),
                    Is.True);
                Assert.That(
                    server.Manager.FindPredefinedNode<NodeState>(
                        new NodeId("AssetCheckFunctionAlarmType", server.Manager.TypeNamespaceIndex)),
                    Is.Null,
                    "no server-specific types without the option");
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Asset Health Status Alarms")));
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Asset Health Status Root Causes")),
                    "§9.4.1: the alarm types have to implement IRootCauseIndicationType");
            });
        }

        [Test]
        public async Task AnObjectOutsideDeviceIntegrationGetsTheHealthInterfaceAsync()
        {
            IAssetHandle? asset = null;
            INodeBuilder<BaseObjectState>? machine = null;
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(AnObjectOutsideDeviceIntegrationGetsTheHealthInterfaceAsync),
                builder => builder
                    .AddOpcUaDi()
                    .AddAssetManagement()
                    .ConfigureAssetManagement(async context =>
                    {
                        machine = AssetRegistrationTests.CreateMachine(context, "urn:acme:press:1");
                        asset = await context.Assets.RegisterAssetAsync(
                            machine,
                            health => health
                                .WithDeviceHealth(DeviceHealthEnumeration.CHECK_FUNCTION)
                                .WithHealthAlarm(
                                    "Overheat",
                                    AssetHealthAlarmKind.OffSpec,
                                    AmbConditionClass.OverTemperature),
                            context.CancellationToken).ConfigureAwait(false);
                    })).ConfigureAwait(false);
            ushort di = (ushort)server.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            BaseObjectState press = machine!.Node;
            IAssetHealth health = asset!.Health!;
            AlarmConditionState alarm = await server.FindNodeAsync<AlarmConditionState>(health.Alarms[0].NodeId)
                .ConfigureAwait(false);
            NodeState? deviceHealth = press.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("DeviceHealth", di));
            NodeState? folder = press.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("DeviceHealthAlarms", di));

            Assert.Multiple(() =>
            {
                Assert.That(
                    press.ReferenceExists(
                        Ua.ReferenceTypeIds.HasInterface,
                        false,
                        server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.IDeviceHealthType)),
                    Is.True);
                Assert.That(deviceHealth, Is.InstanceOf<BaseDataVariableState<DeviceHealthEnumeration>>());
                Assert.That(
                    ((BaseVariableState)deviceHealth!).DataType,
                    Is.EqualTo(server.ToNodeId(Opc.Ua.Di.DataTypeIds.DeviceHealthEnumeration)));
                Assert.That(health.DeviceHealth, Is.EqualTo(DeviceHealthEnumeration.CHECK_FUNCTION));
                Assert.That(folder, Is.InstanceOf<FolderState>());
                Assert.That(folder!.ReferenceExists(Ua.ReferenceTypeIds.Organizes, false, alarm.NodeId), Is.True);
                Assert.That(alarm.SourceNode!.Value, Is.EqualTo(press.NodeId));
                Assert.That(
                    alarm.TypeDefinitionId,
                    Is.EqualTo(new NodeId("AssetOffSpecAlarmType", server.Manager.TypeNamespaceIndex)));
            });
        }

        [Test]
        public async Task TheAlarmTypesComeWithTheAddressSpaceAsync()
        {
            await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                nameof(TheAlarmTypesComeWithTheAddressSpaceAsync),
                builder => builder.AddOpcUaDi().AddAssetManagement()).ConfigureAwait(false);

#if NET5_0_OR_GREATER
            AssetHealthAlarmKind[] kinds = Enum.GetValues<AssetHealthAlarmKind>();
#else
            var kinds = (AssetHealthAlarmKind[])Enum.GetValues(typeof(AssetHealthAlarmKind));
#endif
            foreach (AssetHealthAlarmKind kind in kinds)
            {
                var typeId = new NodeId(AmbAlarmTypes.NameOf(kind), server.Manager.TypeNamespaceIndex);
                Assert.That(
                    server.Manager.FindPredefinedNode<BaseObjectTypeState>(typeId),
                    Is.Not.Null,
                    kind.ToString());
                Assert.That(
                    server.Server.TypeTree.IsTypeOf(typeId, AmbAlarmTypes.DiTypeOf(kind, server.Server.NamespaceUris)),
                    Is.True,
                    kind.ToString());
            }
            Assert.That(
                server.Server.NamespaceUris.GetString(server.Manager.TypeNamespaceIndex),
                Is.EqualTo(AmbServerOptions.DefaultTypeNamespaceUri));
        }

        [Test]
        public async Task TheAlarmTypesFollowTheDeviceIntegrationModelAsync()
        {
            // The AMB manager builds its address space before the Device
            // Integration model is loaded, so the types follow with the
            // first health alarm.
            (AmbHostedServer server, IAssetHandle asset, _) = await StartWithSensorAsync(
                nameof(TheAlarmTypesFollowTheDeviceIntegrationModelAsync),
                health => health.WithHealthAlarm(
                    "Fieldbus",
                    AssetHealthAlarmKind.Failure,
                    AmbConditionClass.ConnectionFailure),
                assetManagementFirst: true)
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            AlarmConditionState alarm = await server.FindNodeAsync<AlarmConditionState>(asset.Health!.Alarms[0].NodeId)
                .ConfigureAwait(false);

            Assert.That(
                alarm.TypeDefinitionId,
                Is.EqualTo(new NodeId("AssetFailureAlarmType", server.Manager.TypeNamespaceIndex)));
        }

        [Test]
        public void TheBuilderRejectsAnInvalidHealthAlarm()
        {
            var builder = new AssetBuilder();
            builder.WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => builder.WithHealthAlarm(
                    string.Empty,
                    AssetHealthAlarmKind.Failure,
                    AmbConditionClass.ConnectionFailure));
                Assert.Throws<ArgumentException>(() => builder.WithHealthAlarm(
                    "Fieldbus",
                    AssetHealthAlarmKind.OffSpec,
                    AmbConditionClass.OverTemperature));
                Assert.Throws<ArgumentNullException>(() => builder.WithHealthAlarm(
                    "Other",
                    AssetHealthAlarmKind.OffSpec,
                    null!));
                Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithHealthAlarm(
                    "Other",
                    (AssetHealthAlarmKind)42,
                    AmbConditionClass.OverTemperature));
                Assert.That(builder.HealthAlarms, Has.Count.EqualTo(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => AmbAlarmTypes.NameOf((AssetHealthAlarmKind)42));
            });
        }

        [Test]
        public void TheRootCauseHelpersFollowTheSpecification()
        {
            var node = new NodeId(7u, 2);

            Assert.Multiple(() =>
            {
                Assert.That(AssetRootCauses.Unknown.Count, Is.EqualTo(1));
                Assert.That(AssetRootCauses.Unknown[0].RootCauseId.IsNull, Is.True);
                Assert.That(AssetRootCauses.Self.IsNull, Is.False);
                Assert.That(AssetRootCauses.Self.Count, Is.Zero);
                Assert.That(AssetRootCauses.Of(node, default).RootCauseId, Is.EqualTo(node));
                Assert.That(
                    AssetRootCauses.Of(NodeId.Null, new LocalizedText("cable")).RootCause.Text,
                    Is.EqualTo("cable"));
                Assert.Throws<ArgumentException>(() => AssetRootCauses.Of(NodeId.Null, default));
                Assert.That(AssetRootCauses.Normalize(default).Count, Is.EqualTo(1));
                Assert.That(AssetRootCauses.Normalize(AssetRootCauses.Self).Count, Is.Zero);
                Assert.Throws<ArgumentException>(
                    () => AssetRootCauses.Normalize(new RootCauseDataType[] { null! }.ToArrayOf()));
            });
        }

        internal static async Task<(AmbHostedServer Server, IAssetHandle Asset, DeviceState Device)>
            StartWithSensorAsync(
            string name,
            Action<IAssetBuilder> configure,
            Action<AmbServerOptions>? options = null,
            bool assetManagementFirst = false)
        {
            IAssetHandle? asset = null;
            DeviceState? device = null;
            AmbHostedServer server = await AmbHostedServer.StartAsync(
                name,
                builder =>
                {
                    if (assetManagementFirst)
                    {
                        builder.AddAssetManagement(options).AddOpcUaDi();
                    }
                    else
                    {
                        builder.AddOpcUaDi().AddAssetManagement(options);
                    }
                    builder.ConfigureDevicesFor<DiNodeManager>(async context =>
                    {
                        IDeviceBuilder<DeviceState> sensor = await AssetRegistrationTests.CreateDeviceAsync(
                            context,
                            "Sensor",
                            SensorUri).ConfigureAwait(false);
                        device = sensor.Device;
                        asset = await sensor.RegisterAsAssetAsync(
                            context.GetRequiredService<IAssetManagement>(),
                            configure,
                            context.CancellationToken).ConfigureAwait(false);
                    });
                }).ConfigureAwait(false);
            Assert.That(asset, Is.Not.Null, "the asset was registered");
            return (server, asset!, device!);
        }
    }
}
