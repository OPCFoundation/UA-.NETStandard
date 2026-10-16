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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Covers ordered-list mutations, cancellation, factory resource ownership and callback-safe publication.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolProductionLifetimeTests
    {
        /// <summary>
        /// Insertion, pure reordering and removal preserve node identities and sequential job/program indices.
        /// </summary>
        [Test]
        public async Task DynamicListsRenumberAndReleaseOnlyRemovedResourcesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = Machine(fixture, "Ordering");
            var resources = new Dictionary<string, Resource>(StringComparer.Ordinal);
            int barriers = 0;
            var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        var resource = new Resource();
                        resources.Add(name.Name!, resource);
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(
                                context.CreateInstanceOfProductionJobType(parent, name), resource));
                    },
                    PublishFinalObservationsAsync = (nodes, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        Assert.That(nodes.Count, Is.GreaterThan(0));
                        Assert.That(fixture.Context.Manager.FindPredefinedNode(machine.NodeId), Is.Null,
                            "This factory-level case has no clients; real subscriber delivery is tested separately.");
                        barriers++;
                        return default;
                    }
                }, TimeProvider.System);
            await using (controller.ConfigureAwait(false))
            {
                MachineToolProductionJob a = Job("A", MachineToolProductionPhase.Running) with
                {
                    RunsCompleted = 0,
                    Programs = [Program("one"), Program("three")]
                };
                MachineToolProductionJob c = Job("C", MachineToolProductionPhase.Ended);
                var initial = new MachineToolSnapshot
                {
                    Production = new MachineToolProductionObservation { Jobs = [a, c] }
                };
                await controller.InitializeAsync(initial).ConfigureAwait(false);
                ProductionJobListState plan = controller.State.ProductionPlan!;
                ProductionJobState aNode = Find<ProductionJobState>(plan, "A", fixture);
                NodeId aId = aNode.NodeId;
                NodeId oneId = Find<ProductionProgramState>(aNode.ProductionPrograms!, "one", fixture).NodeId;
                var modelEvents = new List<GeneralModelChangeEventState>();
                machine.OnReportEventAsync = (_, _, e, _) =>
                {
                    if (e is GeneralModelChangeEventState change)
                    {
                        modelEvents.Add(change);
                    }
                    return default;
                };
                MachineToolProductionJob b = Job("B", MachineToolProductionPhase.Ended);
                a = a with { Programs = [a.Programs[0], Program("two"), a.Programs[1]] };
                var inserted = initial with
                {
                    Revision = 1,
                    Production = initial.Production with { Jobs = [a, b, c] }
                };
                await controller.ApplyAsync(inserted).ConfigureAwait(false);
                Assert.That(Find<ProductionJobState>(plan, "A", fixture).NumberInList!.Value, Is.Zero);
                Assert.That(Find<ProductionJobState>(plan, "B", fixture).NumberInList!.Value, Is.EqualTo(1));
                Assert.That(Find<ProductionJobState>(plan, "C", fixture).NumberInList!.Value, Is.EqualTo(2));
                Assert.That(Find<ProductionProgramState>(aNode.ProductionPrograms!, "two", fixture).NumberInList!.Value,
                    Is.EqualTo(1));
                string version = plan.GetNodeVersionProperty()!.Value;
                a = a with { Programs = [a.Programs[2], a.Programs[1], a.Programs[0]] };
                var reordered = inserted with
                {
                    Revision = 2,
                    Production = inserted.Production with { Jobs = [c, b, a] }
                };
                await controller.ApplyAsync(reordered).ConfigureAwait(false);
                Assert.That(aNode.NodeId, Is.EqualTo(aId));
                Assert.That(aNode.NumberInList!.Value, Is.EqualTo(2));
                Assert.That(Find<ProductionProgramState>(aNode.ProductionPrograms!, "one", fixture).NodeId,
                    Is.EqualTo(oneId));
                Assert.That(Find<ProductionProgramState>(aNode.ProductionPrograms!, "one", fixture).NumberInList!.Value,
                    Is.EqualTo(2));
                Assert.That(plan.GetNodeVersionProperty()!.Value, Is.Not.EqualTo(version));
                a = a with { Programs = [a.Programs[0], a.Programs[2]] };
                var removed = reordered with
                {
                    Revision = 3,
                    Production = reordered.Production with { Jobs = [c, a] }
                };
                await controller.ApplyAsync(removed).ConfigureAwait(false);
                Assert.That(aNode.NumberInList.Value, Is.EqualTo(1));
                Assert.That(Find<ProductionProgramState>(aNode.ProductionPrograms!, "one", fixture).NumberInList!.Value,
                    Is.EqualTo(1));
                Assert.That(resources["B"].Disposals, Is.EqualTo(1));
                Assert.That(resources["A"].Disposals, Is.Zero);
                Assert.That(resources["C"].Disposals, Is.Zero);
                Assert.That(barriers, Is.EqualTo(1),
                    "One delivery barrier covers all removed subtrees in the transaction.");
                Assert.That(modelEvents, Has.Count.EqualTo(3));
            }
            Assert.That(resources["A"].Disposals, Is.EqualTo(1));
            Assert.That(resources["B"].Disposals, Is.EqualTo(1));
            Assert.That(resources["C"].Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A failed or canceled factory transaction releases all returned resources and can be retried.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedStagingRollsBackOwnedResourcesAndAllowsRetryAsync(bool cancel)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = Machine(fixture, "Rollback");
            var resources = new List<Resource>();
            bool fail = true;
            using var cancellation = new CancellationTokenSource();
            await using var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        if (fail && name.Name == "B" && !cancel)
                        {
                            throw new InvalidOperationException("Provider factory failed before returning ownership.");
                        }
                        var resource = new Resource();
                        resources.Add(resource);
                        ProductionJobState node = context.CreateInstanceOfProductionJobType(parent, name);
                        if (fail && name.Name == "B")
                        {
                            cancellation.Cancel();
                        }
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(node, resource));
                    }
                }, TimeProvider.System);
            var snapshot = new MachineToolSnapshot
            {
                Production = new MachineToolProductionObservation
                {
                    Jobs = [Job("A", MachineToolProductionPhase.Ended), Job("B", MachineToolProductionPhase.Ended)]
                }
            };
            if (cancel)
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await controller.InitializeAsync(snapshot, cancellation.Token).ConfigureAwait(false));
            }
            else
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await controller.InitializeAsync(snapshot).ConfigureAwait(false));
            }
            Assert.That(controller.Observation, Is.Null);
            Assert.That(machine.Production!.ProductionPlan, Is.Null);
            foreach (Resource resource in resources)
            {
                Assert.That(resource.Disposals, Is.EqualTo(1));
            }
            fail = false;
            await controller.InitializeAsync(snapshot).ConfigureAwait(false);
            Assert.That(controller.Observation, Is.SameAs(snapshot));
            Assert.That(machine.Production.ProductionPlan, Is.Not.Null);
        }

        /// <summary>
        /// Concurrent updates fail promptly and disposal cancels an uncommitted final-delivery wait.
        /// </summary>
        [Test]
        public async Task DisposalCancelsUncommittedRemovalAndCompetingUpdatesDoNotDeadlockAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = Machine(fixture, "Competing");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan,
                    PublishFinalObservationsAsync = async (_, ct) =>
                    {
                        using CancellationTokenRegistration registration = ct.Register(() => release.TrySetCanceled());
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    }
                }, TimeProvider.System);
            var initial = new MachineToolSnapshot
            {
                Production = new MachineToolProductionObservation
                {
                    Jobs = [Job("A", MachineToolProductionPhase.Ended)]
                }
            };
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            Task removing = controller.ApplyAsync(initial with
            {
                Revision = 1,
                Production = initial.Production with { Jobs = [] }
            }).AsTask();
            await entered.Task.ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(initial with { Revision = 2 }).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.SameAs(initial));
            Task disposing = controller.DisposeAsync().AsTask();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await removing.ConfigureAwait(false));
            await disposing.ConfigureAwait(false);
            Assert.That(machine.Production!.ProductionPlan, Is.Null);
        }

        /// <summary>
        /// Publication callbacks can read the committed state, cannot deadlock by reentering mutations, and can retry.
        /// </summary>
        [Test]
        public async Task FailedPublicationRetainsCommittedSnapshotAndRetriesTheSameEventIdentityAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = Machine(fixture, "RetryPublication");
            await using var controller = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions { Features = MachineToolFeatures.Production }, TimeProvider.System);
            MachineToolProductionJob job = Job("A", MachineToolProductionPhase.Running) with
            {
                RunsCompleted = 0,
                RunsPlanned = 2
            };
            var initial = new MachineToolSnapshot
            {
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            var next = initial with
            {
                Revision = 1,
                Production = initial.Production with { Jobs = [job with { RunsCompleted = 1, TransitionSequence = 1 }] }
            };
            ByteString eventId = default;
            int calls = 0;
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                if (e is not ProductionJobTransitionEventState transition)
                {
                    return default;
                }
                Assert.That(controller.Observation, Is.SameAs(next));
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await controller.ApplyAsync(next).ConfigureAwait(false));
                Assert.That(() => controller.DisposeAsync(), Throws.TypeOf<ServiceResultException>());
                if (++calls == 1)
                {
                    eventId = transition.EventId!.Value;
                    throw new InvalidOperationException("The event sink failed.");
                }
                Assert.That(transition.EventId!.Value, Is.EqualTo(eventId));
                return default;
            };
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await controller.ApplyAsync(next).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.SameAs(next), "The observation was committed before publication.");
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(next with { Revision = 2 }).ConfigureAwait(false));
            await controller.ApplyAsync(next).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Removing the final optional set or individual part removes its mandatory-placeholder container as well.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemovingLastPartOrSetDoesNotLeaveAnEmptyMandatoryPlaceholderContainerAsync(bool removeSet)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolProductionTests.Example();
            MachineToolProductionJob job = initial.Production.Jobs[0];
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
                Phase = MachineToolProductionPhase.Running, PartsCompleted = 1, PartsGood = 1, PartSets = [set]
            };
            initial = initial with { Production = initial.Production with { Jobs = [job] } };
            var provider = new InMemoryMachineToolProvider(initial);
            var resource = new Resource();
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context
                .AddMachineTool("RemoveParts")
                .WithProvider(provider)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan |
                        MachineToolFeatures.ProductionParts | MachineToolFeatures.ProductionStatistics,
                    CreatePartAsync = (context, parent, name, _) =>
                        new ValueTask<MachineToolProductionNode<ProductionPartState>>(
                            new MachineToolProductionNode<ProductionPartState>(
                                context.CreateInstanceOfProductionPartType(parent, name), resource))
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Removal test");
                    identity.SerialNumber = "Removal";
                    identity.ProductInstanceUri = "urn:production-tests:removal";
                }).BuildAsync().ConfigureAwait(false);
            ProductionJobState node =
                Find<ProductionJobState>(runtime.State.Production!.ProductionPlan!, "Job", fixture);
            await provider.SetSnapshotAsync(initial with
            {
                Production = initial.Production with
                {
                    Jobs = [job with { PartSets = removeSet ? [] : [set with { Parts = [] }] }]
                }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(resource.Disposals, Is.EqualTo(1));
            if (removeSet)
            {
                Assert.That(node.PartSets, Is.Null);
            }
            else
            {
                Assert.That(Find<ProductionPartSetState>(node.PartSets!, "Set", fixture).PartsPerRun, Is.Null);
            }
            Assert.That(node.PartsCompleted!.Value, Is.EqualTo(1));
            Assert.That(node.PartsGood!.Value, Is.EqualTo(1));
        }

        /// <summary>
        /// One failing application disposer cannot prevent other owned job resources from being attempted.
        /// </summary>
        [Test]
        public async Task AllOwnedResourcesAreAttemptedWhenDisposalFailsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var resources = new List<Resource>();
            var controller = new MachineToolProductionController(fixture.Context, Machine(fixture, "FailedDisposal"),
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        var resource = new Resource(fail: true);
                        resources.Add(resource);
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(
                                context.CreateInstanceOfProductionJobType(parent, name), resource));
                    }
                }, TimeProvider.System);
            await controller.InitializeAsync(new MachineToolSnapshot
            {
                Production = new MachineToolProductionObservation
                {
                    Jobs = [Job("A", MachineToolProductionPhase.Ended), Job("B", MachineToolProductionPhase.Ended)]
                }
            }).ConfigureAwait(false);
            AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                await controller.DisposeAsync().ConfigureAwait(false))!;
            Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(resources[0].Disposals, Is.EqualTo(1));
            Assert.That(resources[1].Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Returns a generated machine without registering it; these tests exercise the module's factory seam.
        /// </summary>
        private static MachineToolState Machine(MachineToolRuntimeFixture fixture, string name) =>
            fixture.Context.Context.CreateInstanceOfMachineToolType(fixture.Context.MachinesFolder,
                new QualifiedName(name, fixture.Context.InstanceNamespaceIndex));

        /// <summary>
        /// Supplies a terminal program with a usable sample.
        /// </summary>
        private static MachineToolProgram Program(string name) => new()
        {
            Name = name,
            Phase = MachineToolProductionPhase.Ended,
            Sample = new MachineToolSampleInfo(
                new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc), StatusCodes.Good)
        };

        /// <summary>
        /// Supplies a coherent job whose optional program state is already terminal.
        /// </summary>
        private static MachineToolProductionJob Job(string identifier, MachineToolProductionPhase phase) => new()
        {
            Identifier = identifier,
            Phase = phase,
            RunsCompleted = phase == MachineToolProductionPhase.Ended ? 1u : 0u,
            RunsPlanned = 1,
            RunsPlannedIsValid = true,
            Programs = [Program("Main")],
            Sample = new MachineToolSampleInfo(
                new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc), StatusCodes.Good)
        };

        /// <summary>
        /// Reads a typed generated list member at its public browse-name seam.
        /// </summary>
        /// <typeparam name="TState">The expected generated type.</typeparam>
        private static TState Find<TState>(NodeState parent, string name, MachineToolRuntimeFixture fixture)
            where TState : BaseInstanceState
        {
            return (TState)parent.FindChild(fixture.Context.Context,
                new QualifiedName(name, fixture.Context.InstanceNamespaceIndex))!;
        }

        /// <summary>
        /// A resource provided through the public typed factory contract.
        /// </summary>
        private sealed class Resource : IAsyncDisposable
        {
            /// <summary>
            /// Creates a test resource with an optional failing disposal.
            /// </summary>
            public Resource(bool fail = false)
            {
                m_fail = fail;
            }

            /// <summary>
            /// Gets the number of ownership releases.
            /// </summary>
            public int Disposals { get; private set; }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Disposals++;
                if (m_fail)
                {
                    throw new InvalidOperationException("Application disposal failed.");
                }
                return default;
            }

            /// <summary>Whether to exercise an application cleanup failure.</summary>
            private readonly bool m_fail;
        }
    }
}
