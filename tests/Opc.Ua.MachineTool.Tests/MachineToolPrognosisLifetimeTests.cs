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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GMS;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies dynamic prognosis publication, real resources, reference atomicity and actual GMS subtypes.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolPrognosisLifetimeTests
    {
        /// <summary>
        /// Prognosis removal waits for the final predicted time in a real PublishResponse and drains its resource.
        /// </summary>
        [Test]
        public async Task DynamicForecastRemovalDeliversFinalPredictionAndDrainsOwnedObservationAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolPrognosisValidationTests.Example();
            var provider = new InMemoryMachineToolProvider(initial);
            EquipmentResource? resource = null;
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "DynamicForecast")
                    .WithPrognoses(new MachineToolPrognosisOptions
                    {
                        Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                        SupportedKinds =
                        [
                            MachineToolPrognosisKind.Maintenance, MachineToolPrognosisKind.ManualActivity
                        ],
                        CreatePrognosisAsync = (context, parent, name, value, _) =>
                        {
                            PrognosisState node;
                            if (value.Kind == MachineToolPrognosisKind.Maintenance)
                            {
                                resource = new EquipmentResource();
                                node = context.CreateInstanceOfMaintenancePrognosisType(parent, name);
                            }
                            else
                            {
                                node = context.CreateInstanceOfManualActivityPrognosisType(parent, name);
                            }
                            return new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                                new MachineToolEquipmentNode<PrognosisState>(node,
                                    value.Kind == MachineToolPrognosisKind.Maintenance ? resource : null));
                        }
                    }).BuildAsync().ConfigureAwait(false);
            PrognosisListState list = runtime.Prognoses.State!;
            var maintenance = (MaintenancePrognosisState)list.FindChild(fixture.Context.Context,
                new QualifiedName("Maintenance", fixture.Context.InstanceNamespaceIndex))!;
            NodeId removedId = maintenance.NodeId;
            string version = list.GetNodeVersionProperty()!.Value;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "DynamicForecastClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, telemetry);
            await using var receiver = new MachineToolEquipmentReceiver(session);
            await receiver.SubscribeAsync(list.NodeId, maintenance.PredictedTime!.NodeId, runtime.NodeId)
                .ConfigureAwait(false);
            MachineToolPrognosis next = initial.Prognoses!.Entries[0] with
            {
                PredictedTime = MachineToolEquipmentTests.Sample(
                    Variant.From((DateTimeUtc)new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
                    initial.Prognoses.Entries[0].Sample)
            };
            MachineToolPrognosis manual = next with
            {
                Key = "Inspection", Kind = MachineToolPrognosisKind.ManualActivity,
                Activity = new LocalizedText("Inspect fixture")
            };
            await provider.SetSnapshotAsync(initial with { Prognoses = new() { Entries = [next, manual] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            await receiver.WaitAsync(r => r.NestedModelEvents > 0 && r.RootModelEvents > 0).ConfigureAwait(false);
            Assert.That(receiver.RootModelEvents, Is.EqualTo(1));
            Assert.That(receiver.NestedModelEvents, Is.EqualTo(1));
            Assert.That(list.GetNodeVersionProperty()!.Value, Is.Not.EqualTo(version));
            version = list.GetNodeVersionProperty()!.Value;
            await provider.SetSnapshotAsync(initial with { Prognoses = new() { Entries = [manual, next] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(list.GetNodeVersionProperty()!.Value, Is.Not.EqualTo(version));
            MachineToolPrognosisReadSnapshot read = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(read.Entries[0].BrowseName.Name, Is.EqualTo("Inspection"));
            Assert.That(read.Entries[1].NodeId, Is.EqualTo(removedId));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await facade.ReadPrognosesAsync(runtime.NodeId, maximumPrognoses: 1).ConfigureAwait(false));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () => await facade.ReadPrognosesAsync(
                runtime.NodeId, cancellationToken: cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Task delivered = receiver.WaitAsync(r => r.Deleted.Contains(removedId)).AsTask();
            await provider.SetSnapshotAsync(initial with { Prognoses = new() { Entries = [manual] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            await delivered.ConfigureAwait(false);
            Assert.That(receiver.Values.Exists(value =>
                value.WrappedValue == next.PredictedTime.WrappedValue &&
                value.SourceTimestamp == next.PredictedTime.SourceTimestamp), Is.True);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(removedId), Is.Null);
            Assert.That(resource!.Disposals, Is.EqualTo(1));
            Assert.That(resource.WorkCompleted, Is.True);
            Assert.That((await facade.ReadPrognosesAsync(runtime.NodeId).ConfigureAwait(false)).Entries.Count,
                Is.EqualTo(1));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Initial and simultaneous forecasts resolve actual new GMS jobs/parts, not old or fabricated NodeIds.
        /// </summary>
        [Test]
        public async Task PrognosesReferenceActualProspectiveGmsProductionAndToolsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture([new MachineToolEquipmentModelProvider()]);
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot production = MachineToolProductionTests.Example();
            MachineToolSnapshot equipment = MachineToolEquipmentTests.Example();
            MachineToolSampleInfo sample = production.Operation.Sample;
            DataValue prediction = MachineToolEquipmentTests.Sample(
                Variant.From((DateTimeUtc)new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc)), sample);
            var jobForecast = new MachineToolPrognosis
            {
                Key = "JobEnd", Kind = MachineToolPrognosisKind.ProductionJobEnd,
                PredictedTime = prediction, Sample = sample, Target = new() { JobKey = "Job" }
            };
            var partForecast = new MachineToolPrognosis
            {
                Key = "PartUnload", Kind = MachineToolPrognosisKind.PartUnload,
                PredictedTime = prediction, Sample = sample, Location = new LocalizedText("Work area"),
                Target = new() { JobKey = "Job", PartSetKey = "Set", PartKey = "Part" }
            };
            var toolForecast = new MachineToolPrognosis
            {
                Key = "ToolChange", Kind = MachineToolPrognosisKind.ToolChange,
                PredictedTime = prediction, Sample = sample, Location = new LocalizedText("Magazine"),
                ToolIdentifier = "GLOBAL-123"
            };
            var initial = production with
            {
                Equipment = equipment.Equipment,
                Prognoses = new() { Entries = [jobForecast, partForecast, toolForecast] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            GMSJobState? actualJob = null;
            GMSPartState? actualPart = null;
            SensorState? actualTool = null;
            await using MachineToolRuntime<GMSState> runtime = await fixture.Context.AddMachineTool("References",
                static (context, parent, name) => context.CreateInstanceOfGMSType(parent, name))
                .WithProvider(provider)
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife,
                    CreateToolAsync = (context, parent, name, _, _) =>
                    {
                        actualTool = context.CreateInstanceOfSensorType(parent, name);
                        actualTool.Class!.StatusCode = StatusCodes.BadWaitingForInitialData;
                        return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(actualTool));
                    }
                })
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan |
                        MachineToolFeatures.ProductionParts | MachineToolFeatures.ProductionStatistics,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        actualJob = context.CreateInstanceOfGMSJobType(parent, name);
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(actualJob));
                    },
                    CreatePartAsync = (context, parent, name, _) =>
                    {
                        actualPart = context.CreateInstanceOfGMSPartType(parent, name);
                        return new ValueTask<MachineToolProductionNode<ProductionPartState>>(
                            new MachineToolProductionNode<ProductionPartState>(actualPart));
                    }
                })
                .WithPrognoses(new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                    SupportedKinds = MachineToolPrognosisTests.StandardKinds
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("GMS prognosis tests");
                    identity.SerialNumber = "References";
                    identity.ProductInstanceUri = "urn:prognosis-tests:references";
                }).BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "PrognosisReferenceClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, telemetry);
            MachineToolPrognosisReadSnapshot first = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(first.Entries[0].JobNodeId.WrappedValue.TryGetValue(out NodeId jobId), Is.True);
            Assert.That(jobId, Is.EqualTo(actualJob!.NodeId));
            Assert.That(first.Entries[1].PartNodeId.WrappedValue.TryGetValue(out NodeId partId), Is.True);
            Assert.That(partId, Is.EqualTo(actualPart!.NodeId));
            Assert.That(first.Entries[1].PartName.WrappedValue.TryGetValue(out string? partName), Is.True);
            Assert.That(partName, Is.EqualTo("Bracket"));
            Assert.That(first.Entries[1].PartIdentifier.WrappedValue.TryGetValue(out string? partIdentifier), Is.True);
            Assert.That(partIdentifier, Is.EqualTo("Part-1"));
            Assert.That(first.Entries[2].ToolNodeId.WrappedValue.TryGetValue(out NodeId toolId), Is.True);
            Assert.That(toolId, Is.EqualTo(actualTool!.NodeId));
            await provider.SetSnapshotAsync(initial with
            {
                Operation = initial.Operation with { Mode = MachineOperationMode.Service },
                Prognoses = new()
                {
                    Entries = [jobForecast, partForecast with { PartName = "Wrong part" }, toolForecast]
                }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(runtime.Production.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Equipment.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Prognoses.Observation!.Revision, Is.Zero);
            Assert.That(runtime.State.Monitoring!.MachineTool!.OperationMode!.Value,
                Is.EqualTo(initial.Operation.Mode));
            MachineToolProductionJob newJob = production.Production.Jobs[0] with
            {
                Identifier = "NextJob",
                Programs = [production.ActiveProgram with { JobIdentifier = "NextJob" }],
                PartSets =
                [
                    production.Production.Jobs[0].PartSets[0] with
                    {
                        Parts =
                        [
                            production.Production.Jobs[0].PartSets[0].Parts[0] with
                            {
                                Identifier = "NextPart", Name = "Next bracket"
                            }
                        ]
                    }
                ]
            };
            var next = initial with
            {
                Production = initial.Production with { Jobs = [initial.Production.Jobs[0], newJob] },
                Prognoses = new()
                {
                    Entries =
                    [
                        jobForecast with { Target = new() { JobKey = "NextJob" } },
                        partForecast with
                        {
                            Target = new() { JobKey = "NextJob", PartSetKey = "Set", PartKey = "Part" }
                        },
                        toolForecast
                    ]
                }
            };
            await provider.SetSnapshotAsync(next).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            MachineToolPrognosisReadSnapshot changed = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(changed.Entries[0].JobNodeId.WrappedValue.TryGetValue(out NodeId nextJobId), Is.True);
            Assert.That(nextJobId, Is.EqualTo(actualJob!.NodeId));
            Assert.That(nextJobId, Is.Not.EqualTo(jobId));
            Assert.That(changed.Entries[1].PartNodeId.WrappedValue.TryGetValue(out NodeId nextPartId), Is.True);
            Assert.That(nextPartId, Is.EqualTo(actualPart!.NodeId));
            Assert.That(nextPartId, Is.Not.EqualTo(partId));
            Assert.That(changed.Entries[1].PartName.WrappedValue.TryGetValue(out partName), Is.True);
            Assert.That(partName, Is.EqualTo("Next bracket"));
            MachineToolProductionJob oldAborted = Abort(initial.Production.Jobs[0]);
            MachineToolProductionJob nextAborted = Abort(newJob);
            next = next with
            {
                ActiveProgram = oldAborted.Programs[0],
                Production = next.Production with { Jobs = [oldAborted, nextAborted] }
            };
            await provider.SetSnapshotAsync(next).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            ulong accepted = runtime.Prognoses.Observation!.Revision;
            var removed = next with { Production = next.Production with { Jobs = [oldAborted] } };
            await provider.SetSnapshotAsync(removed).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(runtime.Prognoses.Observation.Revision, Is.EqualTo(accepted));
            Assert.That(runtime.Production.Observation!.Revision, Is.EqualTo(accepted));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(nextJobId), Is.Not.Null);
            await provider.SetSnapshotAsync(removed with { Prognoses = new() { Entries = [toolForecast] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(nextJobId), Is.Null);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(nextPartId), Is.Null);
            MachineToolPrognosisReadSnapshot final = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(final.Entries.Count, Is.EqualTo(1));
            Assert.That(final.Entries[0].ToolNodeId.WrappedValue.TryGetValue(out NodeId remainingTool), Is.True);
            Assert.That(remainingTool, Is.EqualTo(actualTool.NodeId));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            static MachineToolProductionJob Abort(MachineToolProductionJob job) => job with
            {
                Phase = MachineToolProductionPhase.Aborted, TransitionSequence = 1,
                Programs =
                [
                    job.Programs[0] with { Phase = MachineToolProductionPhase.Aborted, TransitionSequence = 1 }
                ]
            };
        }

        /// <summary>
        /// A genuine GMS calibration forecast retains its type, QName and derived calibration children on updates.
        /// </summary>
        [Test]
        public async Task ActualGmsCalibrationPrognosisIsAdoptedWithoutBaseSubstitutionAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture([new MachineToolEquipmentModelProvider()]);
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolPrognosis basis = MachineToolPrognosisValidationTests.Example().Prognoses!.Entries[0];
            var forecast = basis with
            {
                Key = "Calibration", NamespaceUri = GmsModel.NamespaceUri, Kind = MachineToolPrognosisKind.Companion,
                CompanionTypeId = new ExpandedNodeId(1019u, GmsModel.NamespaceUri),
                Activity = LocalizedText.Null
            };
            var initial = new MachineToolSnapshot { Prognoses = new() { Entries = [forecast] } };
            var provider = new InMemoryMachineToolProvider(initial);
            CalibrationPrognosisState? actual = null;
            var resource = new EquipmentResource();
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "Calibration")
                    .WithPrognoses(new MachineToolPrognosisOptions
                    {
                        Features = MachineToolFeatures.Prognoses,
                        SupportedKinds = [MachineToolPrognosisKind.Companion],
                        CreatePrognosisAsync = (context, parent, name, _, _) =>
                        {
                            actual = context.CreateInstanceOfCalibrationPrognosisType(parent, name);
                            actual.Calibrated!.Value = true;
                            actual.DateOfCalibration!.Value =
                                new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
                            actual.AddCalibrationCertificate(context);
                            actual.CalibrationCertificate!.Value = ["urn:calibration-certificate:test"];
                            return new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                                new MachineToolEquipmentNode<PrognosisState>(actual, resource));
                        }
                    }).BuildAsync().ConfigureAwait(false);
            NodeId id = actual!.NodeId;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "CalibrationClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, telemetry);
            MachineToolPrognosisReadSnapshot read = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(read.Entries[0].Kind, Is.EqualTo(MachineToolPrognosisKind.Companion));
            Assert.That(read.Entries[0].TypeDefinitionId,
                Is.EqualTo(NodeId.Create(1019u, GmsModel.NamespaceUri, session.NamespaceUris)));
            Assert.That(read.Entries[0].BrowseName,
                Is.EqualTo(new QualifiedName("Calibration",
                    (ushort)session.NamespaceUris.GetIndex(GmsModel.NamespaceUri))));
            await provider.SetSnapshotAsync(initial with
            {
                Prognoses = new()
                {
                    Entries =
                    [
                        forecast with
                        {
                            PredictedTime = MachineToolEquipmentTests.Sample(
                                Variant.From((DateTimeUtc)new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                                basis.Sample)
                        }
                    ]
                }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(actual.NodeId, Is.EqualTo(id));
            Assert.That(actual.Calibrated!.Value, Is.True);
            Assert.That(actual.DateOfCalibration!.Value,
                Is.EqualTo((DateTimeUtc)new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)));
            Assert.That(actual.CalibrationCertificate!.Value,
                Is.EqualTo((ArrayOf<string>)["urn:calibration-certificate:test"]));
            Assert.That(resource.Disposals, Is.Zero);
            DataValue changed = await session.ReadValueAsync(actual.PredictedTime!.NodeId).ConfigureAwait(false);
            Assert.That(changed.WrappedValue.TryGetValue(out DateTimeUtc predicted), Is.True);
            Assert.That(predicted, Is.EqualTo((DateTimeUtc)new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Identical tool identifiers on two machines resolve locally; foreign, wrong-kind and removed targets fail.
        /// </summary>
        [Test]
        public async Task CrossInstanceAndRemovedReferencesAreRejectedBeforeAnyCommitAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolTool tool = MachineToolEquipmentLifetimeTests.Tool("Tool", 1) with { Identifier = "SharedTool" };
            MachineToolPrognosis basis = MachineToolPrognosisValidationTests.Example().Prognoses!.Entries[0];
            var forecast = basis with
            {
                Kind = MachineToolPrognosisKind.ToolUnload, Activity = LocalizedText.Null,
                Location = new LocalizedText("Magazine"), ToolIdentifier = "SharedTool"
            };
            var initial = new MachineToolSnapshot
            {
                Equipment = new() { Tools = [tool] }, Prognoses = new() { Entries = [forecast] }
            };
            var firstProvider = new InMemoryMachineToolProvider(initial);
            var secondProvider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> first = await BuildAsync(firstProvider, "First")
                .ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> second = await BuildAsync(secondProvider, "Second")
                .ConfigureAwait(false);
            var firstTool = (ToolState)first.Equipment.State.Tools!.FindChild(fixture.Context.Context,
                new QualifiedName("Tool", fixture.Context.InstanceNamespaceIndex))!;
            var secondTool = (ToolState)second.Equipment.State.Tools!.FindChild(fixture.Context.Context,
                new QualifiedName("Tool", fixture.Context.InstanceNamespaceIndex))!;
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "IsolatedForecastClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, NUnitTelemetryContext.Create());
            foreach (NodeId target in new[] { secondTool.NodeId, first.State.Production!.ActiveProgram!.NodeId })
            {
                await firstProvider.SetSnapshotAsync(initial with
                {
                    Prognoses = new() { Entries = [forecast with { Target = new() { NodeId = target } }] }
                }).ConfigureAwait(false);
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await first.RefreshAsync().ConfigureAwait(false));
                Assert.That(first.Equipment.Observation!.Revision, Is.Zero);
                Assert.That(first.Prognoses.Observation!.Revision, Is.Zero);
                Assert.That(second.Prognoses.Observation!.Revision, Is.Zero);
            }
            await firstProvider.SetSnapshotAsync(initial with { Equipment = new() }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await first.RefreshAsync().ConfigureAwait(false));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(firstTool.NodeId), Is.SameAs(firstTool));
            await firstProvider.SetSnapshotAsync(initial with { Equipment = new(), Prognoses = new() })
                .ConfigureAwait(false);
            await first.RefreshAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(firstTool.NodeId), Is.Null);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(secondTool.NodeId), Is.SameAs(secondTool));
            MachineToolPrognosisReadSnapshot empty = await facade.ReadPrognosesAsync(first.NodeId)
                .ConfigureAwait(false);
            Assert.That(empty.Entries, Is.Empty);
            Assert.That(empty.ListNodeId.IsNull, Is.False);
            MachineToolPrognosisReadSnapshot other = await facade.ReadPrognosesAsync(second.NodeId)
                .ConfigureAwait(false);
            Assert.That(other.Entries[0].ToolNodeId.WrappedValue.TryGetValue(out NodeId linked), Is.True);
            Assert.That(linked, Is.EqualTo(secondTool.NodeId));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            ValueTask<MachineToolRuntime<MachineToolState>> BuildAsync(IMachineToolProvider provider, string name) =>
                MachineToolEquipmentLifetimeTests.Builder(fixture, provider, name)
                    .WithEquipment(new MachineToolEquipmentOptions
                    {
                        Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools
                    })
                    .WithPrognoses(new MachineToolPrognosisOptions
                    {
                        Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                        SupportedKinds = [MachineToolPrognosisKind.ToolUnload]
                    }).BuildAsync();
        }

        /// <summary>
        /// A rejected prognosis factory releases its resource and a staged sibling tool without partial publication.
        /// </summary>
        [Test]
        public async Task PrognosisFactoryFailureRollsBackPreparedToolAndBothResourceTransfersAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolPrognosisValidationTests.Example() with
            {
                Equipment = new() { Tools = [MachineToolEquipmentLifetimeTests.Tool("First", 1)] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            EquipmentResource? toolResource = null;
            EquipmentResource? forecastResource = null;
            ToolState? stagedTool = null;
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "CrossRollback")
                    .WithEquipment(new MachineToolEquipmentOptions
                    {
                        Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools,
                        CreateToolAsync = (context, parent, name, _, _) =>
                        {
                            ToolState node = context.CreateInstanceOfToolType(parent, name);
                            if (name.Name == "New")
                            {
                                stagedTool = node;
                                toolResource = new EquipmentResource();
                            }
                            return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                                new MachineToolEquipmentNode<ToolState>(node,
                                    name.Name == "New" ? toolResource : null));
                        }
                    })
                    .WithPrognoses(new MachineToolPrognosisOptions
                    {
                        Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                        SupportedKinds = [MachineToolPrognosisKind.Maintenance],
                        CreatePrognosisAsync = (context, parent, name, _, _) =>
                        {
                            if (name.Name == "Reject")
                            {
                                forecastResource = new EquipmentResource();
                                parent = fixture.Context.MachinesFolder;
                            }
                            return new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                                new MachineToolEquipmentNode<PrognosisState>(
                                    context.CreateInstanceOfMaintenancePrognosisType(parent, name),
                                    name.Name == "Reject" ? forecastResource : null));
                        }
                    }).BuildAsync().ConfigureAwait(false);
            string version = runtime.Equipment.State.Tools!.GetNodeVersionProperty()!.Value;
            await provider.SetSnapshotAsync(initial with
            {
                Equipment = new()
                {
                    Tools = [initial.Equipment!.Tools[0], MachineToolEquipmentLifetimeTests.Tool("New", 2)]
                },
                Prognoses = new()
                {
                    Entries = [initial.Prognoses!.Entries[0], initial.Prognoses.Entries[0] with { Key = "Reject" }]
                },
                Operation = initial.Operation with { Mode = MachineOperationMode.Service }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(toolResource!.Disposals, Is.EqualTo(1));
            Assert.That(forecastResource!.Disposals, Is.EqualTo(1));
            Assert.That(toolResource.WorkCompleted, Is.True);
            Assert.That(forecastResource.WorkCompleted, Is.True);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(stagedTool!.NodeId), Is.Null);
            Assert.That(runtime.Equipment.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Prognoses.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Production.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Equipment.State.Tools!.GetNodeVersionProperty()!.Value, Is.EqualTo(version));
        }

        /// <summary>
        /// Cancellation before commit retains the forecast and resource even after its actual delivery barrier ran.
        /// </summary>
        [Test]
        public async Task CancelledFinalPublicationRetainsForecastAndRejectsReentrantMutationAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolPrognosisValidationTests.Example();
            var provider = new InMemoryMachineToolProvider(initial);
            var resource = new EquipmentResource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool hold = true;
            MachineToolRuntime<MachineToolState>? runtime = null;
            runtime = await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "Cancellation")
                .WithPrognoses(new MachineToolPrognosisOptions
                {
                    Features = MachineToolFeatures.Prognoses | MachineToolFeatures.DynamicPrognoses,
                    SupportedKinds = [MachineToolPrognosisKind.Maintenance],
                    CreatePrognosisAsync = (context, parent, name, _, _) =>
                        new ValueTask<MachineToolEquipmentNode<PrognosisState>>(
                            new MachineToolEquipmentNode<PrognosisState>(
                                context.CreateInstanceOfMaintenancePrognosisType(parent, name), resource)),
                    PublishFinalObservationsAsync = async (nodes, ct) =>
                    {
                        await fixture.Context.Manager.WaitForValuePublicationAsync(nodes, ct).ConfigureAwait(false);
                        Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await runtime!.RefreshAsync(ct).ConfigureAwait(false));
                        Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await runtime!.DisposeAsync().ConfigureAwait(false));
                        if (hold)
                        {
                            entered.TrySetResult(true);
                            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                        }
                    }
                }).BuildAsync().ConfigureAwait(false);
            await using (runtime.ConfigureAwait(false))
            {
                await provider.SetSnapshotAsync(initial with { Prognoses = new() }).ConfigureAwait(false);
                using var cancelled = new CancellationTokenSource();
                Task refresh = runtime.RefreshAsync(cancelled.Token).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                cancelled.Cancel();
                Assert.That(async () => await refresh.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(runtime.Prognoses.Observation!.Revision, Is.Zero);
                Assert.That(resource.Disposals, Is.Zero);
                Assert.That(resource.WorkCompleted, Is.False);
                hold = false;
                await runtime.RefreshAsync().ConfigureAwait(false);
                Assert.That(runtime.Prognoses.Observation!.Prognoses!.Entries, Is.Empty);
                Assert.That(resource.Disposals, Is.EqualTo(1));
                Assert.That(resource.WorkCompleted, Is.True);
            }
        }
    }
}
