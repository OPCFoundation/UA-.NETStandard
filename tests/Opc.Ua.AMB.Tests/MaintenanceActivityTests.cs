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
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.Di;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Gives registered assets current and future maintenance activities
    /// (OPC 10000-110 §12) and runs them through the
    /// <c>MaintenanceEventStateMachineType</c> in a hosted server.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    [NonParallelizable]
    public sealed class MaintenanceActivityTests
    {
        private static readonly DateTimeUtc s_planned = new(new DateTime(2027, 3, 1, 8, 0, 0, DateTimeKind.Utc));

        private static readonly string[] s_interfaceMembers =
        [
            "MaintenanceState", "PlannedDate", "EstimatedDowntime", "MaintenanceSupplier",
            "QualificationOfPersonnel", "PartsOfAssetReplaced", "PartsOfAssetServiced",
            "MaintenanceMethod", "ConfigurationChanged"
        ];

        [Test]
        public async Task AnActivityIsAConditionOfTheServerSpecificTypeAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, DeviceState device) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AnActivityIsAConditionOfTheServerSpecificTypeAsync),
                    builder => builder.WithMaintenance(
                        "AnnualInspection",
                        AmbConditionClass.Inspection,
                        details =>
                        {
                            details.Description = new LocalizedText("Annual inspection of the sensor.");
                            details.PlannedDate = s_planned;
                            details.EstimatedDowntime = TimeSpan.FromHours(2);
                            details.MaintenanceSupplier = new NameNodeIdDataType
                            {
                                Name = new LocalizedText("ACME Service"),
                                NodeId = NodeId.Null
                            };
                            details.MaintenanceMethod = MaintenanceMethodEnum.Local;
                            details.ConfigurationChanged = false;
                        }))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IAssetMaintenance maintenance = asset.Maintenance!;
            Assert.That(maintenance.TryGetActivity("AnnualInspection", out IMaintenanceActivity? inspection), Is.True);
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(inspection!.NodeId)
                .ConfigureAwait(false);
            ushort amb = server.Manager.AmbNamespaceIndex;
            var typeId = new NodeId("AssetMaintenanceActivityConditionType", server.Manager.TypeNamespaceIndex);
            NodeId diMaintenance = server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.MaintenanceRequiredAlarmType);
            var stateMachine = (FiniteStateMachineState)condition.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("MaintenanceState", amb))!;
            IReadOnlyList<ReferenceDescription> interfaces = await server
                .BrowseAsync(typeId, Ua.ReferenceTypeIds.HasInterface)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> declarations = await server
                .BrowseAsync(typeId, Ua.ReferenceTypeIds.HierarchicalReferences)
                .ConfigureAwait(false);
            IReadOnlyList<ReferenceDescription> listed = await server
                .BrowseAsync(device.DeviceHealthAlarms!.NodeId, Ua.ReferenceTypeIds.Organizes)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(maintenance.Activities.Count, Is.EqualTo(1));
                Assert.That(maintenance.TryGetActivity("Other", out _), Is.False);
                Assert.That(inspection.ConditionClass, Is.SameAs(AmbConditionClass.Inspection));
                Assert.That(inspection.State, Is.EqualTo(MaintenanceStateKind.Planned));
                Assert.That(condition, Is.InstanceOf<MaintenanceRequiredAlarmState>());
                Assert.That(condition.TypeDefinitionId, Is.EqualTo(typeId));
                Assert.That(server.Server.TypeTree.IsTypeOf(typeId, diMaintenance), Is.True);
                Assert.That(
                    interfaces.Select(reference => reference.NodeId),
                    Does.Contain((ExpandedNodeId)server.ToNodeId(ObjectTypeIds.IMaintenanceEventType)));
                Assert.That(
                    declarations.Select(reference => reference.BrowseName.Name),
                    Is.SupersetOf(s_interfaceMembers));

                Assert.That(condition.SourceNode!.Value, Is.EqualTo(device.NodeId));
                Assert.That(
                    condition.ConditionClassId!.Value,
                    Is.EqualTo(AmbConditionClass.Inspection.GetTypeId(server.Server.NamespaceUris)));
                Assert.That(condition.ActiveState!.Id!.Value, Is.True, "a planned activity is active");
                Assert.That(condition.Retain!.Value, Is.True, "§12.1: retained until executed");
                Assert.That(condition.Severity!.Value, Is.EqualTo(AssetMaintenance.ActiveSeverity));
                Assert.That(condition.Message!.Value.Text, Is.EqualTo("Annual inspection of the sensor."));
                Assert.That(
                    listed.Select(reference => reference.NodeId),
                    Does.Contain((ExpandedNodeId)condition.NodeId));

                Assert.That(stateMachine, Is.InstanceOf<MaintenanceEventStateMachineState>());
                Assert.That(stateMachine.CurrentState!.Value.Text, Is.EqualTo("Planned"));
                Assert.That(
                    stateMachine.CurrentState.Id!.Value,
                    Is.EqualTo(new NodeId(MaintenanceEventStateMachineTypeIds.StateIds.Planned, amb)));
                Assert.That(stateMachine.NodeId.NamespaceIndex, Is.Not.EqualTo(amb), "an instance identifier");

                Assert.That(Value<DateTimeUtc>(server, condition, "PlannedDate"), Is.EqualTo(s_planned));
                Assert.That(Value<double>(server, condition, "EstimatedDowntime"), Is.EqualTo(7_200_000d));
                Assert.That(
                    Value<NameNodeIdDataType>(server, condition, "MaintenanceSupplier")!.Name.Text,
                    Is.EqualTo("ACME Service"));
                Assert.That(
                    Value<MaintenanceMethodEnum>(server, condition, "MaintenanceMethod"),
                    Is.EqualTo(MaintenanceMethodEnum.Local));
                Assert.That(Value<bool>(server, condition, "ConfigurationChanged"), Is.False);
                Assert.That(
                    condition.FindChildWithQualifiedName(
                        server.Manager.SystemContext,
                        new QualifiedName("PartsOfAssetReplaced", amb)),
                    Is.Null,
                    "an unset member is not published");
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Contain(new QualifiedName("AMB Current and Future Maintenance Activities")));
            });
        }

        [Test]
        public async Task AnActivityRunsThroughItsStatesAsync()
        {
            var observed = new List<MaintenanceStateKind>();
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AnActivityRunsThroughItsStatesAsync),
                    builder => builder.WithMaintenance(
                        "Servicing",
                        AmbConditionClass.Servicing,
                        details => details.Description = new LocalizedText("Replace the seals."),
                        (state, _) =>
                        {
                            observed.Add(state);
                            return default;
                        }))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity servicing = asset.Maintenance!.Activities[0];
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(servicing.NodeId)
                .ConfigureAwait(false);

            ServiceResultException finishTooEarly = Assert.ThrowsAsync<ServiceResultException>(
                async () => await servicing.FinishAsync().ConfigureAwait(false))!;
            await servicing.StartAsync().ConfigureAwait(false);
            ConditionSnapshot executing = Capture(servicing, condition);
            ServiceResultException startTwice = Assert.ThrowsAsync<ServiceResultException>(
                async () => await servicing.StartAsync().ConfigureAwait(false))!;

            await servicing.FinishAsync(new LocalizedText("The seals did not fit; the servicing failed."))
                .ConfigureAwait(false);
            ConditionSnapshot finished = Capture(servicing, condition);
            string? failure = condition.Message!.Value.Text;
            CallMethodResult acknowledged = await server.CallAsync(
                condition.NodeId,
                ((AcknowledgeableConditionState)condition).Acknowledge!.NodeId,
                Variant.From(condition.EventId!.Value),
                Variant.From(new LocalizedText("done"))).ConfigureAwait(false);
            bool retainedAfterAcknowledgement = condition.Retain!.Value;

            DateTimeUtc next = new(new DateTime(2028, 3, 1, 8, 0, 0, DateTimeKind.Utc));
            await servicing.ReplanAsync(next).ConfigureAwait(false);
            ConditionSnapshot replanned = Capture(servicing, condition);

            Assert.Multiple(() =>
            {
                Assert.That(finishTooEarly.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(startTwice.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(
                    executing,
                    Is.EqualTo(new ConditionSnapshot(
                        MaintenanceStateKind.Executing,
                        true,
                        true,
                        AssetMaintenance.ActiveSeverity)));
                Assert.That(finished.State, Is.EqualTo(MaintenanceStateKind.Finished));
                Assert.That(finished.Active, Is.False);
                Assert.That(finished.Retain, Is.True, "retained until acknowledged");
                Assert.That(AssetFaultSeverities.IsInactive(finished.Severity), Is.True);
                Assert.That(failure, Is.EqualTo("The seals did not fit; the servicing failed."));
                Assert.That(StatusCode.IsGood(acknowledged.StatusCode), Is.True, acknowledged.StatusCode.ToString());
                Assert.That(retainedAfterAcknowledgement, Is.False, "finished and acknowledged");
                Assert.That(
                    replanned,
                    Is.EqualTo(new ConditionSnapshot(
                        MaintenanceStateKind.Planned,
                        true,
                        true,
                        AssetMaintenance.ActiveSeverity)));
                Assert.That(servicing.Details.PlannedDate, Is.EqualTo(next));
                Assert.That(Value<DateTimeUtc>(server, condition, "PlannedDate"), Is.EqualTo(next));
                Assert.That(condition.Message.Value.Text, Is.EqualTo("Replace the seals."));
                Assert.That(
                    observed,
                    Is.EqualTo(new[]
                    {
                        MaintenanceStateKind.Executing,
                        MaintenanceStateKind.Finished,
                        MaintenanceStateKind.Planned
                    }));
            });
        }

        [Test]
        public async Task UpdatingTheDetailsPublishesNewMembersAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(UpdatingTheDetailsPublishesNewMembersAsync),
                    builder => builder.WithMaintenance("Repair", AmbConditionClass.Repair))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity repair = asset.Maintenance!.Activities[0];
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(repair.NodeId)
                .ConfigureAwait(false);
            ByteString before = condition.EventId!.Value;

            MaintenanceActivityDetails copy = repair.Details;
            copy.ConfigurationChanged = true;
            await repair.UpdateAsync(
                details =>
                {
                    details.PartsOfAssetReplaced =
                    [
                        new NameNodeIdDataType { Name = new LocalizedText("Bearing"), NodeId = NodeId.Null }
                    ];
                    details.QualificationOfPersonnel = new NameNodeIdDataType
                    {
                        Name = new LocalizedText("Certified technician"),
                        NodeId = NodeId.Null
                    };
                },
                new LocalizedText("The bearing will be replaced.")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(repair.Details.ConfigurationChanged, Is.Null, "Details hands out a copy");
                Assert.That(
                    Value<ArrayOf<NameNodeIdDataType>>(server, condition, "PartsOfAssetReplaced")[0].Name.Text,
                    Is.EqualTo("Bearing"));
                Assert.That(
                    Value<NameNodeIdDataType>(server, condition, "QualificationOfPersonnel")!.Name.Text,
                    Is.EqualTo("Certified technician"));
                Assert.That(condition.Message!.Value.Text, Is.EqualTo("The bearing will be replaced."));
                Assert.That(condition.EventId.Value, Is.Not.EqualTo(before), "the update is reported");
                Assert.That(repair.State, Is.EqualTo(MaintenanceStateKind.Planned));
            });
            Assert.ThrowsAsync<ArgumentNullException>(async () => await repair.UpdateAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public async Task AGuardOnTheStateMachineCanRefuseATransitionAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(AGuardOnTheStateMachineCanRefuseATransitionAsync),
                    builder => builder.WithMaintenance("Improvement", AmbConditionClass.Improvement))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity improvement = asset.Maintenance!.Activities[0];
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(improvement.NodeId)
                .ConfigureAwait(false);
            var stateMachine = (FiniteStateMachineState)condition.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("MaintenanceState", server.Manager.AmbNamespaceIndex))!;
            var transitions = new List<uint>();
            stateMachine.OnBeforeTransition = (_, _, transitionId, _, _, _) =>
                transitionId == MaintenanceEventStateMachineTypeIds.TransitionIds.FromPlannedToExecuting
                    ? StatusCodes.BadUserAccessDenied
                    : ServiceResult.Good;
            stateMachine.OnAfterTransition = (_, _, transitionId, _, _, _) =>
            {
                transitions.Add(transitionId);
                return ServiceResult.Good;
            };

            ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(
                async () => await improvement.StartAsync().ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(improvement.State, Is.EqualTo(MaintenanceStateKind.Planned));
                Assert.That(transitions, Is.Empty);
            });

            stateMachine.OnBeforeTransition = null;
            await improvement.StartAsync().ConfigureAwait(false);
            Assert.That(
                transitions,
                Is.EqualTo(new[] { MaintenanceEventStateMachineTypeIds.TransitionIds.FromPlannedToExecuting }));
        }

        [Test]
        public async Task TheHooksOfTheApplicationRunOutsideTheActivityLockAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(TheHooksOfTheApplicationRunOutsideTheActivityLockAsync),
                    builder => builder.WithMaintenance("Improvement", AmbConditionClass.Improvement))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity improvement = asset.Maintenance!.Activities[0];
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(improvement.NodeId)
                .ConfigureAwait(false);
            var stateMachine = (FiniteStateMachineState)condition.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("MaintenanceState", server.Manager.AmbNamespaceIndex))!;

            // Another thread reads the activity while a hook runs; with the
            // lock held across the hook it would wait for the hook.
            bool ReadableElsewhere()
            {
                return Task.Run(() => improvement.State).Wait(TimeSpan.FromSeconds(5));
            }
            bool readableBefore = false;
            bool readableAfter = false;
            bool readableInUpdate = false;
            stateMachine.OnBeforeTransition = (_, _, _, _, _, _) =>
            {
                readableBefore = ReadableElsewhere();
                return ServiceResult.Good;
            };
            stateMachine.OnAfterTransition = (_, _, _, _, _, _) =>
            {
                readableAfter = ReadableElsewhere();
                return ServiceResult.Good;
            };

            await improvement.StartAsync().ConfigureAwait(false);
            await improvement.UpdateAsync(details =>
            {
                readableInUpdate = ReadableElsewhere();
                details.ConfigurationChanged = true;
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(readableBefore, Is.True, "OnBeforeTransition runs outside the lock");
                Assert.That(readableAfter, Is.True, "OnAfterTransition runs outside the lock");
                Assert.That(readableInUpdate, Is.True, "the update delegate runs outside the lock");
                Assert.That(improvement.State, Is.EqualTo(MaintenanceStateKind.Executing));
                Assert.That(improvement.Details.ConfigurationChanged, Is.True);
            });
        }

        [Test]
        public async Task ATransitionAGuardRaceLostIsRefusedAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(ATransitionAGuardRaceLostIsRefusedAsync),
                    builder => builder.WithMaintenance("Improvement", AmbConditionClass.Improvement))
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            IMaintenanceActivity improvement = asset.Maintenance!.Activities[0];
            AlarmConditionState condition = await server.FindNodeAsync<AlarmConditionState>(improvement.NodeId)
                .ConfigureAwait(false);
            var stateMachine = (FiniteStateMachineState)condition.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName("MaintenanceState", server.Manager.AmbNamespaceIndex))!;
            var transitions = new List<uint>();
            bool nested = false;
            bool nestedCompleted = false;
            stateMachine.OnBeforeTransition = (_, _, transitionId, _, _, _) =>
            {
                // While the start is checked, the activity is started from
                // elsewhere; the outer start then finds it moved.
                if (!nested &&
                    transitionId == MaintenanceEventStateMachineTypeIds.TransitionIds.FromPlannedToExecuting)
                {
                    nested = true;
                    Task nestedStart = improvement.StartAsync().AsTask();
                    nestedCompleted = nestedStart.Status == TaskStatus.RanToCompletion;
                }
                return ServiceResult.Good;
            };
            stateMachine.OnAfterTransition = (_, _, transitionId, _, _, _) =>
            {
                transitions.Add(transitionId);
                return ServiceResult.Good;
            };

            ServiceResultException refused = Assert.ThrowsAsync<ServiceResultException>(
                async () => await improvement.StartAsync().ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(nestedCompleted, Is.True, "the nested start went through");
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(improvement.State, Is.EqualTo(MaintenanceStateKind.Executing));
                Assert.That(
                    transitions,
                    Is.EqualTo(new[] { MaintenanceEventStateMachineTypeIds.TransitionIds.FromPlannedToExecuting }),
                    "the transition is taken and reported once");
            });
        }

        [Test]
        public async Task WithoutServerDefinedTypesTheConditionReferencesTheInterfaceAsync()
        {
            (AmbHostedServer server, IAssetHandle asset, _) = await HealthAlarmTests
                .StartWithSensorAsync(
                    nameof(WithoutServerDefinedTypesTheConditionReferencesTheInterfaceAsync),
                    builder => builder.WithMaintenance("Check", AmbConditionClass.ExternalCheck),
                    options => options.UseServerDefinedAlarmTypes = false)
                .ConfigureAwait(false);
            await using AmbHostedServer serverScope = server;
            AlarmConditionState condition = await server
                .FindNodeAsync<AlarmConditionState>(asset.Maintenance!.Activities[0].NodeId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    condition.TypeDefinitionId,
                    Is.EqualTo(server.ToNodeId(Opc.Ua.Di.ObjectTypeIds.MaintenanceRequiredAlarmType)));
                Assert.That(
                    condition.ReferenceExists(
                        Ua.ReferenceTypeIds.HasInterface,
                        false,
                        server.ToNodeId(ObjectTypeIds.IMaintenanceEventType)),
                    Is.True);
                Assert.That(
                    server.Manager.ConformanceUnits.ToArray(),
                    Does.Not.Contain(new QualifiedName("AMB Current and Future Maintenance Activities")),
                    "§12.1: the condition types have to implement IMaintenanceEventType");
            });
        }

        [Test]
        public void TheBuilderRejectsAnInvalidActivity()
        {
            var builder = new AssetBuilder();
            builder
                .WithHealthAlarm("Fieldbus", AssetHealthAlarmKind.Failure, AmbConditionClass.ConnectionFailure)
                .WithMaintenance("Inspection", AmbConditionClass.Inspection);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(
                    () => builder.WithMaintenance(string.Empty, AmbConditionClass.Inspection));
                Assert.Throws<ArgumentNullException>(() => builder.WithMaintenance("Other", null!));
                Assert.Throws<ArgumentException>(
                    () => builder.WithMaintenance("Other", AmbConditionClass.ConnectionFailure),
                    "§12.1: a maintenance condition class");
                Assert.Throws<ArgumentException>(
                    () => builder.WithMaintenance("Fieldbus", AmbConditionClass.Repair),
                    "the name of an alarm");
                Assert.Throws<ArgumentException>(
                    () => builder.WithMaintenance("Inspection", AmbConditionClass.Repair));
                Assert.Throws<ArgumentException>(() => builder.WithHealthAlarm(
                    "Inspection",
                    AssetHealthAlarmKind.Failure,
                    AmbConditionClass.ConnectionFailure));
                Assert.That(builder.Maintenance, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void TheTablesFollowTheGeneratedModel()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MaintenanceEventStateMachineTables.States, Has.Length.EqualTo(3));
                Assert.That(MaintenanceEventStateMachineTables.Transitions, Has.Length.EqualTo(3));
                Assert.That(
                    MaintenanceEventStateMachineTables.StateOf(MaintenanceStateKind.Executing).Name,
                    Is.EqualTo("Executing"));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => MaintenanceEventStateMachineTables.StateOf((MaintenanceStateKind)42));
                Assert.That(
                    MaintenanceEventStateMachineTables.Transitions.Select(transition => transition.Number),
                    Is.EqualTo(new uint[] { 1, 2, 3 }));
            });
        }

        private static ConditionSnapshot Capture(IMaintenanceActivity activity, AlarmConditionState condition)
        {
            return new ConditionSnapshot(
                activity.State,
                condition.ActiveState!.Id!.Value,
                condition.Retain!.Value,
                condition.Severity!.Value);
        }

        private readonly record struct ConditionSnapshot(
            MaintenanceStateKind State,
            bool Active,
            bool Retain,
            ushort Severity);

        private static T? Value<T>(AmbHostedServer server, NodeState condition, string name)
        {
            NodeState? property = condition.FindChildWithQualifiedName(
                server.Manager.SystemContext,
                new QualifiedName(name, server.Manager.AmbNamespaceIndex));
            Assert.That(property, Is.InstanceOf<PropertyState<T>>(), name);
            return ((PropertyState<T>)property!).Value;
        }
    }
}
