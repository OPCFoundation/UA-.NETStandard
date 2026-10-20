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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GMS;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies production composition on actual generated GMS types and rejects invalid ownership transfers.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolProductionFactoryTests
    {
        /// <summary>
        /// GMSJob and GMSPart retain their types, added members and resource bindings across static job replacement.
        /// </summary>
        [Test]
        public async Task ActualGmsFactoriesPreserveDerivedNodesAcrossStaticReplacementAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await LoadGmsAsync(fixture).ConfigureAwait(false);
            GMSState machine = fixture.Context.Context.CreateInstanceOfGMSType(
                fixture.Context.MachinesFolder, new QualifiedName("Gms", fixture.Context.InstanceNamespaceIndex));
            GMSJobState? jobNode = null;
            GMSPartState? partNode = null;
            var jobResource = new Resource();
            var partResource = new Resource();
            var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        jobNode = context.CreateInstanceOfGMSJobType(parent, name);
                        jobNode.AddBatchIdentifier(context).AddMeasurementReason(context);
                        jobNode.BatchIdentifier!.Value = "Batch-preserved";
                        jobNode.MeasurementReason!.Value = MeasurementReasonEnum.AuditMeasurement;
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(jobNode, jobResource));
                    },
                    CreatePartAsync = (context, parent, name, _) =>
                    {
                        partNode = context.CreateInstanceOfGMSPartType(parent, name);
                        partNode.AddPartDescription(context);
                        partNode.PartDescription!.Value = "Description-preserved";
                        return new ValueTask<MachineToolProductionNode<ProductionPartState>>(
                            new MachineToolProductionNode<ProductionPartState>(partNode, partResource));
                    }
                }, TimeProvider.System);
            await using (controller.ConfigureAwait(false))
            {
                MachineToolSnapshot example = MachineToolProductionTests.Example();
                MachineToolProductionJob job = example.Production.Jobs[0];
                MachineToolProgram program = job.Programs[0] with
                {
                    Key = "Program",
                    Phase = MachineToolProductionPhase.Ended
                };
                MachineToolProductionPartSet set = job.PartSets[0] with
                {
                    PartsCompletedPerRun = 1,
                    Parts =
                    [
                        job.PartSets[0].Parts[0] with
                        {
                            Phase = MachineToolProductionPhase.Ended, Quality = PartQuality.Good
                        }
                    ]
                };
                job = job with
                {
                    Key = "Slot",
                    Phase = MachineToolProductionPhase.Ended,
                    RunsCompleted = 1,
                    RunsPlanned = 1,
                    PartsCompleted = 1,
                    PartsGood = 1,
                    Programs = [program],
                    PartSets = [set]
                };
                var initial = example with
                {
                    ActiveProgram = program,
                    Production = example.Production with { Jobs = [job], Statistics = null }
                };
                await controller.InitializeAsync(initial).ConfigureAwait(false);
                Assert.That(controller.State, Is.SameAs(machine.Production));
                Assert.That(jobNode, Is.TypeOf<GMSJobState>());
                Assert.That(partNode, Is.TypeOf<GMSPartState>());
                NodeId jobId = jobNode!.NodeId;
                NodeId partId = partNode!.NodeId;
                Assert.That(jobNode.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1016u, GmsModel.NamespaceUri, fixture.Context.Context.NamespaceUris)));
                Assert.That(partNode.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1017u, GmsModel.NamespaceUri, fixture.Context.Context.NamespaceUris)));
                program = program with
                {
                    Name = "NEXT.NC",
                    JobIdentifier = "NextJob",
                    Phase = MachineToolProductionPhase.Initializing,
                    TransitionSequence = 1
                };
                set = set with
                {
                    PartsCompletedPerRun = 0,
                    Parts =
                    [
                        set.Parts[0] with
                        {
                            Identifier = "NextPart", Phase = MachineToolProductionPhase.Initializing,
                            TransitionSequence = 1, Quality = PartQuality.NotYetMeasured
                        }
                    ]
                };
                job = job with
                {
                    Identifier = "NextJob",
                    Phase = MachineToolProductionPhase.Initializing,
                    TransitionSequence = 1,
                    RunsCompleted = 0,
                    RunsPlanned = 2,
                    PartsCompleted = 0,
                    PartsGood = 0,
                    Programs = [program],
                    PartSets = [set]
                };
                await controller.ApplyAsync(initial with
                {
                    Revision = 1,
                    ActiveProgram = program,
                    Production = initial.Production with { Jobs = [job] }
                }).ConfigureAwait(false);
                Assert.That(jobNode.NodeId, Is.EqualTo(jobId));
                Assert.That(partNode.NodeId, Is.EqualTo(partId));
                Assert.That(jobNode.Identifier!.Value, Is.EqualTo("NextJob"));
                Assert.That(partNode.Identifier!.Value, Is.EqualTo("NextPart"));
                Assert.That(jobNode.BatchIdentifier!.Value, Is.EqualTo("Batch-preserved"));
                Assert.That(jobNode.MeasurementReason!.Value, Is.EqualTo(MeasurementReasonEnum.AuditMeasurement));
                Assert.That(partNode.PartDescription!.Value, Is.EqualTo("Description-preserved"));
                Assert.That(jobNode.State!.LastTransition!.Number!.Value, Is.EqualTo(2));
                Assert.That(partNode.State!.LastTransition!.Number!.Value, Is.EqualTo(2));
                Assert.That(controller.State.ActiveProgram!.JobNodeId!.Value, Is.EqualTo(jobId));
                Assert.That(jobResource.Disposals, Is.Zero);
                Assert.That(partResource.Disposals, Is.Zero);
            }
            Assert.That(jobResource.Disposals, Is.EqualTo(1));
            Assert.That(partResource.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Returning an already owned generated node must not dispose or delete the existing job on rollback.
        /// </summary>
        [Test]
        public async Task FactoryCannotStealAnExistingJobOrItsResourceAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder, new QualifiedName("Ownership", fixture.Context.InstanceNamespaceIndex));
            ProductionJobState? owned = null;
            var resource = new Resource();
            await using var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan,
                    PublishFinalObservationsAsync =
                        (_, _) => throw new InvalidOperationException("No removal is requested."),
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        owned ??= context.CreateInstanceOfProductionJobType(parent, name);
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(owned, resource));
                    }
                }, TimeProvider.System);
            var job = new MachineToolProductionJob
            {
                Identifier = "A",
                Programs = [new MachineToolProgram { Name = "Main" }]
            };
            var initial = new MachineToolSnapshot
            {
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await controller.ApplyAsync(initial with
            {
                Revision = 1,
                Production = initial.Production with { Jobs = [job, job with { Identifier = "B" }] }
            }).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.SameAs(initial));
            Assert.That(controller.State.ProductionPlan!.FindChild(fixture.Context.Context,
                new QualifiedName("A", fixture.Context.InstanceNamespaceIndex)), Is.SameAs(owned));
            Assert.That(owned!.Identifier!.Value, Is.EqualTo("A"));
            Assert.That(resource.Disposals, Is.Zero);
        }

        /// <summary>
        /// The public builder passes real GMS factories into the composed runtime and its live address space.
        /// </summary>
        [Test]
        public async Task PublicBuilderPublishesActualGmsJobAndPartNodesOverTcpAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await LoadGmsAsync(fixture).ConfigureAwait(false);
            var provider = new InMemoryMachineToolProvider(MachineToolProductionTests.Example());
            GMSJobState? job = null;
            GMSPartState? part = null;
            await using MachineToolRuntime<GMSState> runtime = await fixture.Context
                .AddMachineTool("GmsBuilder",
                    static (context, parent, name) => context.CreateInstanceOfGMSType(parent, name))
                .WithProvider(provider)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts |
                        MachineToolFeatures.ProductionStatistics,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        job = context.CreateInstanceOfGMSJobType(parent, name);
                        job.AddBatchIdentifier(context);
                        job.BatchIdentifier!.Value = "GMS batch";
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(job));
                    },
                    CreatePartAsync = (context, parent, name, _) =>
                    {
                        part = context.CreateInstanceOfGMSPartType(parent, name);
                        part.AddPartDescription(context);
                        part.PartDescription!.Value = "GMS part";
                        return new ValueTask<MachineToolProductionNode<ProductionPartState>>(
                            new MachineToolProductionNode<ProductionPartState>(part));
                    }
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("GMS production test");
                    identity.SerialNumber = "GMS-Production";
                    identity.ProductInstanceUri = "urn:production-tests:gms";
                }).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.State, Is.TypeOf<GMSState>());
            Assert.That(fixture.Context.Manager.FindPredefinedNode(job!.NodeId), Is.SameAs(job));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(part!.NodeId), Is.SameAs(part));
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "GmsProductionClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            DataValue batch = await session.ReadValueAsync(job.BatchIdentifier!.NodeId).ConfigureAwait(false);
            DataValue description = await session.ReadValueAsync(part.PartDescription!.NodeId).ConfigureAwait(false);
            Assert.That(batch.WrappedValue.TryGetValue(out string? batchName), Is.True);
            Assert.That(batchName, Is.EqualTo("GMS batch"));
            Assert.That(description.WrappedValue.TryGetValue(out string? partName), Is.True);
            Assert.That(partName, Is.EqualTo("GMS part"));
            MachineToolSnapshot next = await provider.ReadAsync().ConfigureAwait(false);
            MachineToolProgram running = next.ActiveProgram with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1
            };
            await provider.SetSnapshotAsync(next with
            {
                ActiveProgram = running,
                Production = next.Production with
                {
                    Jobs =
                    [
                        next.Production.Jobs[0] with
                        {
                            Phase = MachineToolProductionPhase.Running, TransitionSequence = 1, Programs = [running]
                        }
                    ]
                }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            DataValue current = await session.ReadValueAsync(job.State!.CurrentState!.Id!.NodeId).ConfigureAwait(false);
            Assert.That(current.WrappedValue.TryGetValue(out NodeId state), Is.True);
            Assert.That(state, Is.EqualTo(NodeId.Create(5017u, Namespaces.MachineTool, session.NamespaceUris)));
            Assert.That(runtime.State.Production!.ActiveProgram!.JobNodeId!.Value, Is.EqualTo(job.NodeId));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads released GMS declarations into the existing namespace owner without creating another runtime.
        /// </summary>
        private static async Task LoadGmsAsync(MachineToolRuntimeFixture fixture)
        {
            fixture.Context.Context.EncodeableFactory.Builder.AddGmsTypeSystem().Commit();
            var definitions = new NodeStateCollection();
            definitions.AddGmsTypeSystem(fixture.Context.Context);
            for (int i = 0; i < definitions.Count; i++)
            {
                NodeState definition = definitions[i];
                if (fixture.Context.Manager.FindPredefinedNode(definition.NodeId) == null)
                {
                    await fixture.Context.Manager.AddPredefinedNodeAsync(definition).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Counts transfers of a companion-specific resource without inspecting module implementation fields.
        /// </summary>
        private sealed class Resource : IAsyncDisposable
        {
            /// <summary>
            /// Gets the number of completed disposals.
            /// </summary>
            public int Disposals { get; private set; }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Disposals++;
                return default;
            }
        }
    }
}
