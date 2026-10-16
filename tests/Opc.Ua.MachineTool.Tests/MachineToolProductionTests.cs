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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises OPC 40501-1 section 8.4 through the composed production controller and generated nodes.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolProductionTests
    {
        /// <summary>
        /// The mandatory program uses canonical state numbers rather than CLR enum ordinals.
        /// </summary>
        [Test]
        public async Task MandatoryProgramUsesCanonicalStateAndAcquisitionMetadataAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder,
                new QualifiedName("Production", fixture.Context.InstanceNamespaceIndex));
            await using var production = new MachineToolProductionController(
                fixture.Context, machine, new MachineToolProductionOptions(), TimeProvider.System);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            await production.InitializeAsync(new MachineToolSnapshot
            {
                ActiveProgram = new MachineToolProgram
                {
                    Name = "MAIN.NC",
                    Phase = MachineToolProductionPhase.Ended,
                    Sample = sample
                }
            }).ConfigureAwait(false);

            ProductionActiveProgramState active = machine.Production!.ActiveProgram!;
            Assert.That(active.NumberInList!.Value, Is.Zero);
            Assert.That(active.Name!.Value, Is.EqualTo("MAIN.NC"));
            Assert.That(active.State!.CurrentState!.Number!.Value, Is.EqualTo(2u));
            Assert.That(active.State.CurrentState.Id!.Value,
                Is.EqualTo(NodeId.Create(5038u, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris)));
            Assert.That(active.State.CurrentState.Timestamp, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(active.State.CurrentState.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(active.State.LastTransition!.Id!.Value.IsNull, Is.True);
            Assert.That(machine.Production.ProductionPlan, Is.Null);
        }

        /// <summary>
        /// Tables 38, 41 and 50 require ordered programs, active-job links and a transition for consecutive runs.
        /// </summary>
        [Test]
        public async Task JobsLinkActiveProgramAndCountConsecutiveRunsAtomicallyAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder, new QualifiedName("Jobs", fixture.Context.InstanceNamespaceIndex));
            await using var production = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions { Features = MachineToolFeatures.Production }, TimeProvider.System);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), StatusCodes.Good);
            var active = new MachineToolProgram
            {
                Name = "SECOND.NC",
                JobIdentifier = "Job-1",
                Phase = MachineToolProductionPhase.Running,
                Sample = sample
            };
            var job = new MachineToolProductionJob
            {
                Identifier = "Job-1",
                Phase = MachineToolProductionPhase.Running,
                RunsPlanned = 2,
                RunsPlannedIsValid = true,
                Sample = sample,
                Programs = [new MachineToolProgram { Name = "FIRST.NC", Sample = sample }, active]
            };
            var snapshot = new MachineToolSnapshot
            {
                ActiveProgram = active,
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            await production.InitializeAsync(snapshot).ConfigureAwait(false);
            Assert.That(machine.Production!.ProductionPlan, Is.Not.Null);
            ProductionJobState published = Children<ProductionJobState>(
                machine.Production.ProductionPlan!, fixture.Context.Context)[0];
            Assert.That(machine.Production.ActiveProgram!.NumberInList!.Value, Is.EqualTo(1));
            Assert.That(machine.Production.ActiveProgram.JobIdentifier!.Value, Is.EqualTo("Job-1"));
            Assert.That(machine.Production.ActiveProgram.JobNodeId!.Value, Is.EqualTo(published.NodeId));
            Assert.That(Children<ProductionProgramState>(published.ProductionPrograms!, fixture.Context.Context)[1]
                .NumberInList!.Value, Is.EqualTo(1));
            var reported = new List<ProductionJobTransitionEventState>();
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                if (e is ProductionJobTransitionEventState transition)
                {
                    reported.Add(transition);
                }
                return default;
            };

            var next = snapshot with
            {
                Revision = 1,
                Production = snapshot.Production with
                {
                    Jobs = [job with { RunsCompleted = 1, TransitionSequence = 1 }]
                }
            };
            await production.ApplyAsync(next).ConfigureAwait(false);
            Assert.That(published.RunsCompleted!.Value, Is.EqualTo(1));
            Assert.That(published.State!.LastTransition!.Number!.Value, Is.EqualTo(3));
            Assert.That(published.State.LastTransition.Id!.Value,
                Is.EqualTo(NodeId.Create(151u, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris)));
            Assert.That(published.State.LastTransition.TransitionTime!.Value, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(reported, Has.Count.EqualTo(1));
            Assert.That(reported[0].RunsCompleted!.Value, Is.EqualTo(1));
            Assert.That(reported[0].SourceNode!.Value, Is.EqualTo(published.State.NodeId));
            Assert.That(reported[0].Identifier!.Value, Is.EqualTo("Job-1"));

            ServiceResultException invalid = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await production.ApplyAsync(next with
                {
                    Revision = 2,
                    ActiveProgram = active with { Name = "INVALID" },
                    Production = snapshot.Production with
                    {
                        Jobs = [job with { RunsCompleted = 9, TransitionSequence = 2 }]
                    }
                }).ConfigureAwait(false))!;
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(published.RunsCompleted.Value, Is.EqualTo(1));
            Assert.That(machine.Production.ActiveProgram.Name!.Value, Is.EqualTo("SECOND.NC"));
            Assert.That(production.Observation, Is.SameAs(next));
        }

        /// <summary>
        /// Section 8.4.3 and Table 110 count only the three specified acceptable quality categories.
        /// </summary>
        [TestCase(PartQuality.Good, 1u)]
        [TestCase(PartQuality.CapabilityUnavailable, 1u)]
        [TestCase(PartQuality.WillNotBeMeasured, 1u)]
        [TestCase(PartQuality.Bad, 0u)]
        [TestCase(PartQuality.NotYetMeasured, 0u)]
        public async Task PartsCountersAndTransitionEventsUseSpecifiedQualityCategoriesAsync(
            PartQuality quality, uint good)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder, new QualifiedName("Parts", fixture.Context.InstanceNamespaceIndex));
            await using var production = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts |
                        MachineToolFeatures.ProductionStatistics
                }, TimeProvider.System);
            MachineToolSnapshot initial = Example();
            await production.InitializeAsync(initial).ConfigureAwait(false);
            ProductionJobState node = Children<ProductionJobState>(production.State.ProductionPlan!,
                fixture.Context.Context)[0];
            ProductionPartSetState setNode =
                Children<ProductionPartSetState>(node.PartSets!, fixture.Context.Context)[0];
            ProductionPartState partNode =
                Children<ProductionPartState>(setNode.PartsPerRun!, fixture.Context.Context)[0];
            var parts = new List<ProductionPartTransitionEventState>();
            var programs = new List<ProductionProgramTransitionEventState>();
            machine.OnReportEventAsync = (_, _, e, _) =>
            {
                if (e is ProductionPartTransitionEventState partEvent)
                {
                    parts.Add(partEvent);
                }
                if (e is ProductionProgramTransitionEventState programEvent)
                {
                    programs.Add(programEvent);
                }
                return default;
            };
            MachineToolProductionJob job = initial.Production.Jobs[0];
            MachineToolProductionPartSet set = job.PartSets[0];
            MachineToolProductionPart part = set.Parts[0] with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1
            };
            MachineToolProgram program = job.Programs[0] with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1
            };
            var running = initial with
            {
                Revision = 1,
                ActiveProgram = program,
                Production = initial.Production with
                {
                    Jobs =
                    [
                        job with
                        {
                            Phase = MachineToolProductionPhase.Running, TransitionSequence = 1,
                            Programs = [program], PartSets = [set with { Parts = [part] }]
                        }
                    ]
                }
            };
            await production.ApplyAsync(running).ConfigureAwait(false);
            part = part with { Phase = MachineToolProductionPhase.Ended, TransitionSequence = 2, Quality = quality };
            job = running.Production.Jobs[0] with
            {
                PartsCompleted = 1,
                PartsGood = good,
                PartSets = [set with { Parts = [part], PartsCompletedPerRun = 1 }]
            };
            var finishedPart = running with { Revision = 2, Production = running.Production with { Jobs = [job] } };
            await production.ApplyAsync(finishedPart).ConfigureAwait(false);
            Assert.That(node.PartsCompleted!.Value, Is.EqualTo(1));
            Assert.That(node.PartsGood!.Value, Is.EqualTo(good));
            Assert.That(setNode.PartsCompletedPerRun!.Value, Is.EqualTo(1));
            Assert.That(partNode.PartQuality!.Value, Is.EqualTo(quality));
            Assert.That(partNode.State!.LastTransition!.Number!.Value, Is.EqualTo(1));
            Assert.That(production.State.Statistics!.PartsProducedInLifetime!.Value, Is.EqualTo(100));
            Assert.That(parts, Has.Count.EqualTo(2));
            Assert.That(parts[1].PartQuality!.Value, Is.EqualTo(quality));
            Assert.That(parts[1].JobIdentifier!.Value, Is.EqualTo("Job"));
            Assert.That(parts[1].Identifier!.Value, Is.EqualTo("Part-1"));
            Assert.That(parts[1].ProcessIrregularity!.Value, Is.EqualTo(ProcessIrregularity.NotDetected));
            Assert.That(parts[1].ToState!.Number!.Value, Is.EqualTo(2));
            Assert.That(parts[1].Time!.Value, Is.EqualTo(part.Sample.SourceTimestamp));
            Assert.That(programs, Has.Count.EqualTo(2), "One event from the plan program and one from ActiveProgram.");
            Assert.That(programs[0].JobIdentifier!.Value, Is.EqualTo("Job"));
            Assert.That(programs[0].Name!.Value, Is.EqualTo("MAIN.NC"));

            Assert.ThrowsAsync<ServiceResultException>(async () => await production.ApplyAsync(finishedPart with
            {
                Revision = 3,
                Production = finishedPart.Production with { Jobs = [job with { PartsGood = 2 }] }
            }).ConfigureAwait(false));
            Assert.That(node.PartsGood.Value, Is.EqualTo(good));
            Assert.That(parts, Has.Count.EqualTo(2), "Rejected observations must not emit transitions.");
        }

        /// <summary>
        /// Section 8.4.9 requires every known reason to be cleared before InterruptedToRunning.
        /// </summary>
        [Test]
        public async Task InterruptionConditionsRemainRetainedUntilEveryReasonIsClearedAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder,
                new QualifiedName("Interruptions", fixture.Context.InstanceNamespaceIndex));
            await using var production = new MachineToolProductionController(fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts |
                        MachineToolFeatures.ProductionStatistics | MachineToolFeatures.ProductionInterruptions
                }, TimeProvider.System);
            MachineToolSnapshot initial = Example();
            await production.InitializeAsync(initial).ConfigureAwait(false);
            MachineToolProductionJob job = initial.Production.Jobs[0] with
            {
                Phase = MachineToolProductionPhase.Running,
                TransitionSequence = 1
            };
            var running = initial with { Revision = 1, Production = initial.Production with { Jobs = [job] } };
            await production.ApplyAsync(running).ConfigureAwait(false);
            NodeId missing = NodeId.Create(54u, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris);
            NodeId toolChange = NodeId.Create(58u, Namespaces.MachineTool, fixture.Context.Context.NamespaceUris);
            var first = new MachineToolProductionInterruption
            {
                Identifier = "Missing",
                ConditionClassId = missing,
                Message = new LocalizedText("Load a part"),
                IsActive = true,
                Sample = job.Sample
            };
            var second = first with
            {
                Identifier = "Tool",
                ConditionClassId = toolChange,
                Message = new LocalizedText("Changing tool"),
                IsAutomated = true
            };
            job = job with
            {
                Phase = MachineToolProductionPhase.Interrupted,
                TransitionSequence = 2,
                Interruptions = [first, second]
            };
            var interrupted = running with { Revision = 2, Production = running.Production with { Jobs = [job] } };
            await production.ApplyAsync(interrupted).ConfigureAwait(false);
            ProductionJobState node = Children<ProductionJobState>(production.State.ProductionPlan!,
                fixture.Context.Context)[0];
            List<InterruptionConditionState> conditions =
                Children<InterruptionConditionState>(node, fixture.Context.Context);
            Assert.That(conditions, Has.Count.EqualTo(2));
            Assert.That(conditions[0].ConditionClassId!.Value, Is.EqualTo(missing));
            Assert.That(conditions[0].ConditionClassName!.Value.Text, Is.EqualTo("PartMissingConditionClassType"));
            Assert.That(conditions[0].Retain!.Value, Is.True);
            Assert.That(conditions[1].IsAutomated!.Value, Is.True);
            Assert.That(conditions[1].SourceNode!.Value, Is.EqualTo(node.NodeId));
            var retained = new List<IFilterTarget>();
            conditions[0].ConditionRefresh(fixture.Context.Context, retained, true);
            Assert.That(retained, Has.Count.EqualTo(1));

            Assert.ThrowsAsync<ServiceResultException>(async () => await production.ApplyAsync(interrupted with
            {
                Revision = 3,
                Production = interrupted.Production with
                {
                    Jobs = [job with { Phase = MachineToolProductionPhase.Running, TransitionSequence = 3 }]
                }
            }).ConfigureAwait(false));
            Assert.That(node.State!.CurrentState!.Value.Text, Is.EqualTo("Interrupted"));
            first = first with { IsActive = false };
            second = second with { IsActive = false };
            job = job with { Interruptions = [first, second] };
            var cleared = interrupted with { Revision = 3, Production = interrupted.Production with { Jobs = [job] } };
            await production.ApplyAsync(cleared).ConfigureAwait(false);
            Assert.That(conditions[0].Retain!.Value, Is.False);
            Assert.That(conditions[1].Retain!.Value, Is.False);
            retained.Clear();
            conditions[0].ConditionRefresh(fixture.Context.Context, retained, true);
            Assert.That(retained, Is.Empty);
            await production.ApplyAsync(cleared with
            {
                Revision = 4,
                Production = cleared.Production with
                {
                    Jobs = [job with { Phase = MachineToolProductionPhase.Running, TransitionSequence = 3 }]
                }
            }).ConfigureAwait(false);
            Assert.That(node.State.LastTransition!.Number!.Value, Is.EqualTo(5));
        }

        /// <summary>
        /// Supplies one independently worked example with all retained part and statistics observations.
        /// </summary>
        internal static MachineToolSnapshot Example()
        {
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc), StatusCodes.Good);
            var program = new MachineToolProgram { Name = "MAIN.NC", JobIdentifier = "Job", Sample = sample };
            return new MachineToolSnapshot
            {
                ActiveProgram = program,
                Operation = new MachineToolOperation { PartsProducedInLifetime = 100, Sample = sample },
                Production = new MachineToolProductionObservation
                {
                    Statistics = new MachineToolProductionStatistics(100, sample),
                    Jobs =
                    [
                        new MachineToolProductionJob
                        {
                            Identifier = "Job", RunsPlanned = 2, RunsPlannedIsValid = true, Sample = sample,
                            Programs = [program],
                            PartSets =
                            [
                                new MachineToolProductionPartSet
                                {
                                    Key = "Set", Name = "Pallet", PartsPlannedPerRun = 1, Sample = sample,
                                    Parts =
                                    [
                                        new MachineToolProductionPart
                                        {
                                            Key = "Part", Name = "Bracket", Identifier = "Part-1",
                                            Irregularity = ProcessIrregularity.NotDetected, Sample = sample
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            };
        }

        /// <summary>
        /// Reads real generated children at the public address-space seam.
        /// </summary>
        private static List<TState> Children<TState>(NodeState parent, ISystemContext context)
            where TState : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            var result = new List<TState>();
            foreach (BaseInstanceState child in children)
            {
                if (child is TState typed)
                {
                    result.Add(typed);
                }
            }
            return result;
        }
    }
}
