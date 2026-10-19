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
using Opc.Ua.Machinery;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises coherent retained production through the actual public builder, provider and runtime over opc.tcp.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolProductionRuntimeTests
    {
        /// <summary>
        /// Either module's validation or factory failure leaves both halves unchanged; callbacks see a coherent commit.
        /// </summary>
        [Test]
        public async Task MonitoringAndProductionAreValidatedBeforeEitherIsPublishedAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolProductionTests.Example();
            var channel = new MachineToolChannel
            {
                Name = "Channel",
                FeedOverride = 80,
                Sample = initial.Operation.Sample,
                State = ChannelState.Active,
                Mode = ChannelMode.Automatic
            };
            initial = initial with
            {
                Operation = initial.Operation with { Mode = MachineOperationMode.Automatic },
                Channels = [channel],
                Software = [new MachineToolSoftware("Controller", "1")]
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Coherent")
                .WithProvider(provider)
                .WithFeatures(MachineToolFeatures.Channels | MachineToolFeatures.SoftwareIdentification)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan |
                        MachineToolFeatures.ProductionParts | MachineToolFeatures.ProductionStatistics,
                    CreateJobAsync = (context, parent, name, _) =>
                    {
                        if (name.Name == "FactoryFailure")
                        {
                            throw new InvalidOperationException("Typed factory failed.");
                        }
                        return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                            new MachineToolProductionNode<ProductionJobState>(
                                context.CreateInstanceOfProductionJobType(parent, name)));
                    }
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Coherence test");
                    identity.SerialNumber = "Coherent-1";
                    identity.ProductInstanceUri = "urn:production-tests:coherent";
                }).BuildAsync().ConfigureAwait(false);
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "CoherenceClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            NodeId modeId = runtime.State.Monitoring!.MachineTool!.OperationMode!.NodeId;
            var software = (SoftwareIdentificationState)runtime.State.Identification!.SoftwareIdentification!
                .FindChild(fixture.Context.Context,
                    new QualifiedName("Controller", fixture.Context.InstanceNamespaceIndex))!;
            var changes = initial with
            {
                Operation = initial.Operation with { Mode = MachineOperationMode.Service },
                Software = [new MachineToolSoftware("Controller", "2")]
            };
            MachineToolProductionJob job = initial.Production.Jobs[0];
            await provider.SetSnapshotAsync(changes with
            {
                Production = changes.Production with { Jobs = [job with { PartsGood = 2 }] }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            await AssertUnchangedAsync().ConfigureAwait(false);

            MachineToolProgram running = job.Programs[0] with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1
            };
            job = job with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1,
                Programs = [running]
            };
            changes = changes with
            {
                ActiveProgram = running,
                Production = changes.Production with { Jobs = [job] }
            };
            await provider.SetSnapshotAsync(changes with
            {
                Channels = [channel with { FeedOverride = 250 }]
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            await AssertUnchangedAsync().ConfigureAwait(false);

            await provider.SetSnapshotAsync(changes with
            {
                Production = changes.Production with
                {
                    Jobs =
                    [
                        job,
                        new MachineToolProductionJob
                        {
                            Identifier = "FactoryFailure", Programs = [new MachineToolProgram { Name = "Other" }]
                        }
                    ]
                }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.RefreshAsync().ConfigureAwait(false));
            await AssertUnchangedAsync().ConfigureAwait(false);

            int reports = 0;
            bool failOnce = true;
            NodeStateReportEventAsyncHandler? forward = runtime.State.OnReportEventAsync;
            runtime.State.OnReportEventAsync = async (context, source, e, ct) =>
            {
                if (e is ProductionJobTransitionEventState)
                {
                    DataValue observed = await session.ReadValueAsync(modeId, ct).ConfigureAwait(false);
                    Assert.That(observed.WrappedValue.TryGetValue(out int mode), Is.True);
                    Assert.That(mode, Is.EqualTo((int)MachineOperationMode.Service));
                    await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await runtime.RefreshAsync(ct).ConfigureAwait(false));
                    await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await runtime.DisposeAsync().ConfigureAwait(false));
                    reports++;
                    if (failOnce)
                    {
                        failOnce = false;
                        throw new InvalidOperationException("Publication transport callback failed.");
                    }
                }
                if (forward != null)
                {
                    await forward(context, source, e, ct).ConfigureAwait(false);
                }
            };
            await provider.SetSnapshotAsync(changes).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(runtime.State.Monitoring.MachineTool.OperationMode.Value,
                Is.EqualTo(MachineOperationMode.Service));
            await runtime.RetryPublicationAsync().ConfigureAwait(false);
            Assert.That(reports, Is.EqualTo(2), "The accepted event was retained and retried, not fabricated anew.");
            DataValue revision = await session.ReadValueAsync(software.SoftwareRevision!.NodeId).ConfigureAwait(false);
            Assert.That(revision.WrappedValue.TryGetValue(out string? text), Is.True);
            Assert.That(text, Is.EqualTo("2"));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            async Task AssertUnchangedAsync()
            {
                DataValue modeValue = await session.ReadValueAsync(modeId).ConfigureAwait(false);
                Assert.That(modeValue.WrappedValue.TryGetValue(out int mode), Is.True);
                Assert.That(mode, Is.EqualTo((int)MachineOperationMode.Automatic));
                DataValue softwareValue = await session.ReadValueAsync(software.SoftwareRevision!.NodeId)
                    .ConfigureAwait(false);
                Assert.That(softwareValue.WrappedValue.TryGetValue(out string? current), Is.True);
                Assert.That(current, Is.EqualTo("1"));
                Assert.That(runtime.Production.Observation!.Revision, Is.Zero);
                Assert.That(runtime.State.Production!.ActiveProgram!.State!.CurrentState!.Value.Text,
                    Is.EqualTo("Initializing"));
            }
        }

        /// <summary>
        /// ActiveProgram mirrors the selected node's history even when programs have independent transition sequences.
        /// </summary>
        [Test]
        public async Task SelectingAnotherProgramCopiesItsHistoryWithoutInventingTransitionsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc), StatusCodes.Good);
            var first = new MachineToolProgram
            {
                Name = "First",
                JobIdentifier = "Job",
                Phase = MachineToolProductionPhase.Ended,
                TransitionSequence = 7,
                Sample = sample
            };
            var second = new MachineToolProgram { Name = "Second", JobIdentifier = "Job", Sample = sample };
            var job = new MachineToolProductionJob
            {
                Identifier = "Job",
                Phase = MachineToolProductionPhase.Running,
                RunsPlanned = 2,
                RunsPlannedIsValid = true,
                Programs = [first, second],
                Sample = sample
            };
            var initial = new MachineToolSnapshot
            {
                ActiveProgram = first,
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("Selection")
                .WithProvider(provider).WithFeatures(MachineToolFeatures.Production)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Selection test");
                    identity.SerialNumber = "Selection-1";
                    identity.ProductInstanceUri = "urn:production-tests:selection";
                }).BuildAsync().ConfigureAwait(false);
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "SelectionClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            ProductionActiveProgramState active = runtime.State.Production!.ActiveProgram!;
            await provider.SetSnapshotAsync(initial with { ActiveProgram = second }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            DataValue index = await session.ReadValueAsync(active.NumberInList!.NodeId).ConfigureAwait(false);
            Assert.That(index.WrappedValue.TryGetValue(out ushort number), Is.True);
            Assert.That(number, Is.EqualTo(1));
            Assert.That(active.State!.CurrentState!.Value.Text, Is.EqualTo("Initializing"));
            Assert.That(active.State.LastTransition!.Id!.Value.IsNull, Is.True);
            second = second with { Phase = MachineToolProductionPhase.Running, TransitionSequence = 1 };
            var next = initial with
            {
                ActiveProgram = second,
                Production = initial.Production with { Jobs = [job with { Programs = [first, second] }] }
            };
            await provider.SetSnapshotAsync(next).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(active.State.LastTransition.Id.Value,
                Is.EqualTo(NodeId.Create(91u, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris)));
            await provider.SetSnapshotAsync(next with { ActiveProgram = first }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            DataValue previous = await session.ReadValueAsync(active.State.LastTransition.Id.NodeId)
                .ConfigureAwait(false);
            Assert.That(previous.WrappedValue.TryGetValue(out NodeId last), Is.True);
            Assert.That(last.IsNull, Is.True,
                "Selecting First must not retain Second's InitializingToRunning transition.");
            Assert.That(active.State.CurrentState.Number!.Value, Is.EqualTo(2));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Cumulative job counters include earlier runs while the same part node resets for the next workpiece.
        /// </summary>
        [Test]
        public async Task CumulativeCountersSurviveASecondRunWithResetPartSlotsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot initial = MachineToolProductionTests.Example();
            MachineToolProgram program = initial.ActiveProgram with { Phase = MachineToolProductionPhase.Running };
            MachineToolProductionJob job = initial.Production.Jobs[0] with
            {
                Phase = MachineToolProductionPhase.Running, Programs = [program]
            };
            MachineToolProductionPartSet set = job.PartSets[0];
            MachineToolProductionPart part = set.Parts[0] with { Phase = MachineToolProductionPhase.Running };
            initial = initial with
            {
                ActiveProgram = program,
                Production = initial.Production with
                {
                    Jobs = [job with { PartSets = [set with { Parts = [part] }] }]
                }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context
                .AddMachineTool("TwoRuns").WithProvider(provider)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts |
                        MachineToolFeatures.ProductionStatistics
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Run counter test");
                    identity.SerialNumber = "TwoRuns";
                    identity.ProductInstanceUri = "urn:production-tests:two-runs";
                }).BuildAsync().ConfigureAwait(false);
            var node = (ProductionJobState)runtime.State.Production!.ProductionPlan!.FindChild(
                fixture.Context.Context, new QualifiedName("Job", fixture.Context.InstanceNamespaceIndex))!;
            var setNode = (ProductionPartSetState)node.PartSets!.FindChild(
                fixture.Context.Context, new QualifiedName("Set", fixture.Context.InstanceNamespaceIndex))!;
            var partNode = (ProductionPartState)setNode.PartsPerRun!.FindChild(
                fixture.Context.Context, new QualifiedName("Part", fixture.Context.InstanceNamespaceIndex))!;
            NodeId partId = partNode.NodeId;
            part = part with
            {
                Phase = MachineToolProductionPhase.Ended, TransitionSequence = 1, Quality = PartQuality.Good
            };
            job = job with { PartsCompleted = 1, PartsGood = 1 };
            await PublishAsync(1).ConfigureAwait(false);
            job = job with
            {
                TransitionSequence = 1, RunsCompleted = 1,
                PartsCompletedBeforeCurrentRun = 1, PartsGoodBeforeCurrentRun = 1
            };
            program = program with { TransitionSequence = 1 };
            part = part with
            {
                Phase = MachineToolProductionPhase.Initializing, TransitionSequence = 2,
                Identifier = "Part-2", Quality = PartQuality.NotYetMeasured
            };
            await PublishAsync(0).ConfigureAwait(false);
            Assert.That(node.PartsCompleted!.Value, Is.EqualTo(1));
            Assert.That(node.PartsGood!.Value, Is.EqualTo(1));
            Assert.That(setNode.PartsCompletedPerRun!.Value, Is.Zero);
            Assert.That(partNode.NodeId, Is.EqualTo(partId));
            Assert.That(partNode.Identifier!.Value, Is.EqualTo("Part-2"));
            part = part with { Phase = MachineToolProductionPhase.Running, TransitionSequence = 3 };
            await PublishAsync(0).ConfigureAwait(false);
            part = part with
            {
                Phase = MachineToolProductionPhase.Ended, TransitionSequence = 4,
                Quality = PartQuality.WillNotBeMeasured
            };
            program = program with { Phase = MachineToolProductionPhase.Ended, TransitionSequence = 2 };
            job = job with
            {
                Phase = MachineToolProductionPhase.Ended, TransitionSequence = 2, RunsCompleted = 2,
                PartsCompleted = 2, PartsGood = 2
            };
            await PublishAsync(1).ConfigureAwait(false);
            Assert.That(node.RunsCompleted!.Value, Is.EqualTo(2));
            Assert.That(node.PartsCompleted.Value, Is.EqualTo(2));
            Assert.That(node.PartsGood.Value, Is.EqualTo(2));
            Assert.That(setNode.PartsCompletedPerRun.Value, Is.EqualTo(1));
            Assert.That(node.State!.LastTransition!.Number!.Value, Is.EqualTo(1));

            async Task PublishAsync(uint completed)
            {
                await provider.SetSnapshotAsync(initial with
                {
                    ActiveProgram = program,
                    Production = initial.Production with
                    {
                        Jobs =
                        [
                            job with
                            {
                                Programs = [program],
                                PartSets = [set with { Parts = [part], PartsCompletedPerRun = completed }]
                            }
                        ]
                    }
                }).ConfigureAwait(false);
                await runtime.RefreshAsync().ConfigureAwait(false);
            }
        }
    }
}
