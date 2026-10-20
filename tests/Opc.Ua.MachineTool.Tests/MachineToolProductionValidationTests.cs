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
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies rejection atomicity and canonical production transitions at the public controller seam.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolProductionValidationTests
    {
        /// <summary>
        /// Starts a shared model owner; each case still owns a distinct generated machine and production module.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new MachineToolRuntimeFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the isolated model owner and certificate stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Each malformed full observation must leave the previous revision, values and transition state intact.
        /// </summary>
        [TestCase("NullProduction")]
        [TestCase("NullActiveProgram")]
        [TestCase("ActiveProgramUnknownPhase")]
        [TestCase("ActiveProgramMissingTime")]
        [TestCase("ActiveProgramUnknownJob")]
        [TestCase("ActiveProgramWrongName")]
        [TestCase("ActiveProgramWrongState")]
        [TestCase("ActiveProgramWrongSequence")]
        [TestCase("NoJobs")]
        [TestCase("NullJob")]
        [TestCase("DuplicateJobs")]
        [TestCase("DuplicateJobIdentifiers")]
        [TestCase("UnknownJobPhase")]
        [TestCase("JobMissingTime")]
        [TestCase("JobEmptyIdentifier")]
        [TestCase("JobTooLongIdentifier")]
        [TestCase("JobTooManyPrograms")]
        [TestCase("NoPrograms")]
        [TestCase("NullProgram")]
        [TestCase("DuplicatePrograms")]
        [TestCase("ProgramWrongJob")]
        [TestCase("UnknownProgramPhase")]
        [TestCase("ProgramMissingTime")]
        [TestCase("JobExceedsPlannedRuns")]
        [TestCase("UnknownPlanEnded")]
        [TestCase("EndedBeforePlannedRuns")]
        [TestCase("RunsIncrementWithoutTransition")]
        [TestCase("MissingJobTransition")]
        [TestCase("SkippedJobTransition")]
        [TestCase("InvalidJobTransition")]
        [TestCase("ChangedJobIdentifier")]
        [TestCase("GoodExceedsCompleted")]
        [TestCase("BadHistoricalCounters")]
        [TestCase("ChangedHistoricalCounters")]
        [TestCase("NullPartSet")]
        [TestCase("DuplicatePartSets")]
        [TestCase("MismatchedPlannedParts")]
        [TestCase("MismatchedCompletedParts")]
        [TestCase("NullPart")]
        [TestCase("DuplicatePartKeys")]
        [TestCase("DuplicatePartIdentifiers")]
        [TestCase("UnknownPartQuality")]
        [TestCase("UnknownProcessIrregularity")]
        [TestCase("UnknownPartPhase")]
        [TestCase("WrongJobPartCounters")]
        [TestCase("StatisticsMissing")]
        [TestCase("StatisticsDecreased")]
        [TestCase("StatisticsDisagreesWithModernCounter")]
        [TestCase("NullInterruption")]
        [TestCase("BadConditionClass")]
        [TestCase("InvalidSeverity")]
        [TestCase("StaleRevision")]
        public async Task InvalidObservationPreservesPublishedRevisionAsync(string violation)
        {
            MachineToolState machine = CreateMachine(violation);
            await using var controller = new MachineToolProductionController(m_fixture.Context, machine,
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts |
                        MachineToolFeatures.ProductionStatistics | MachineToolFeatures.ProductionInterruptions,
                    MaximumProgramsPerJob = 2,
                    MaximumTextLength = 64
                }, TimeProvider.System);
            MachineToolSnapshot initial = MachineToolProductionTests.Example();
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            NodeId before = machine.Production!.ActiveProgram!.State!.CurrentState!.Id!.Value;
            MachineToolSnapshot bad = InvalidExample(initial, violation);

            ServiceResultException rejected = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(bad).ConfigureAwait(false)))!;

            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState), violation);
            Assert.That(controller.Observation, Is.SameAs(initial), violation);
            Assert.That(machine.Production.ActiveProgram.Name!.Value, Is.EqualTo("MAIN.NC"));
            Assert.That(machine.Production.ActiveProgram.State!.CurrentState!.Id!.Value, Is.EqualTo(before));
            Assert.That(machine.Production.ActiveProgram.State.LastTransition!.Id!.Value.IsNull, Is.True);
        }

        /// <summary>
        /// Every transition in Tables 47 and 53 uses its specified number and the provider's timestamp.
        /// </summary>
        [TestCase(MachineToolProductionPhase.Initializing, MachineToolProductionPhase.Running, 0u)]
        [TestCase(MachineToolProductionPhase.Running, MachineToolProductionPhase.Ended, 1u)]
        [TestCase(MachineToolProductionPhase.Ended, MachineToolProductionPhase.Initializing, 2u)]
        [TestCase(MachineToolProductionPhase.Running, MachineToolProductionPhase.Running, 3u)]
        [TestCase(MachineToolProductionPhase.Running, MachineToolProductionPhase.Interrupted, 4u)]
        [TestCase(MachineToolProductionPhase.Interrupted, MachineToolProductionPhase.Running, 5u)]
        [TestCase(MachineToolProductionPhase.Running, MachineToolProductionPhase.Aborted, 6u)]
        [TestCase(MachineToolProductionPhase.Interrupted, MachineToolProductionPhase.Aborted, 7u)]
        [TestCase(MachineToolProductionPhase.Aborted, MachineToolProductionPhase.Initializing, 8u)]
        [TestCase(MachineToolProductionPhase.Initializing, MachineToolProductionPhase.Aborted, 9u)]
        public async Task StandardTransitionNumbersAreNotEnumOrdinalsAsync(
            MachineToolProductionPhase from, MachineToolProductionPhase to, uint number)
        {
            MachineToolState machine = CreateMachine("Transition" + number);
            await using var controller = new MachineToolProductionController(m_fixture.Context, machine,
                new MachineToolProductionOptions(), TimeProvider.System);
            var program = new MachineToolProgram
            {
                Name = "MAIN.NC",
                Phase = from,
                Sample = new MachineToolSampleInfo(
                    new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue)
            };
            var initial = new MachineToolSnapshot { ActiveProgram = program };
            await controller.InitializeAsync(initial).ConfigureAwait(false);
            var next = initial with
            {
                Revision = 1, ActiveProgram = program with { Phase = to, TransitionSequence = 1 }
            };
            await controller.ApplyAsync(next).ConfigureAwait(false);
            FiniteTransitionVariableState transition = machine.Production!.ActiveProgram!.State!.LastTransition!;
            Assert.That(transition.Number!.Value, Is.EqualTo(number));
            Assert.That(transition.Value.Text, Is.EqualTo(from + "To" + to));
            Assert.That(transition.TransitionTime!.Value, Is.EqualTo(program.Sample.SourceTimestamp));
            Assert.That(transition.StatusCode, Is.EqualTo(program.Sample.StatusCode));
            Assert.That(transition.Timestamp, Is.EqualTo(program.Sample.SourceTimestamp));
            await controller.ApplyAsync(next).ConfigureAwait(false);
            Assert.That(transition.Number.Value, Is.EqualTo(number),
                "Re-reading the same snapshot creates no transition.");
        }

        /// <summary>
        /// Setup guards reject impossible limits and features without a delivery capability.
        /// </summary>
        [TestCase(0, 1)]
        [TestCase(65537, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 65537)]
        public void OrderedInventoryLimitsAreExplicit(int jobs, int programs)
        {
            Assert.That(() => new MachineToolProductionController(m_fixture.Context, CreateMachine("Limits"),
                new MachineToolProductionOptions { MaximumJobs = jobs, MaximumProgramsPerJob = programs },
                TimeProvider.System), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Dynamic jobs cannot advertise successful deletion without an actual final-observation publisher.
        /// </summary>
        [Test]
        public void DynamicPlanRequiresProductionAndFinalDeliveryCapability()
        {
            Assert.That(() => new MachineToolProductionController(m_fixture.Context, CreateMachine("MissingProduction"),
                new MachineToolProductionOptions { Features = MachineToolFeatures.DynamicProductionPlan },
                TimeProvider.System), Throws.TypeOf<ServiceResultException>());
            Assert.That(() => new MachineToolProductionController(m_fixture.Context, CreateMachine("MissingDelivery"),
                new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan
                }, TimeProvider.System), Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Cancellation before commit and lifetime misuse leave the initial program unpublished.
        /// </summary>
        [Test]
        public async Task CancellationAndLifetimeGuardsDoNotPublishValuesAsync()
        {
            MachineToolState machine = CreateMachine("Cancellation");
            var controller = new MachineToolProductionController(m_fixture.Context, machine,
                new MachineToolProductionOptions(), TimeProvider.System);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await controller.InitializeAsync(new MachineToolSnapshot(), canceled.Token).ConfigureAwait(false));
            Assert.That(controller.Observation, Is.Null);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.ApplyAsync(new MachineToolSnapshot()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await controller.InitializeAsync(null!).ConfigureAwait(false));
            await controller.InitializeAsync(new MachineToolSnapshot()).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await controller.InitializeAsync(new MachineToolSnapshot()).ConfigureAwait(false));
            await controller.DisposeAsync().ConfigureAwait(false);
            await controller.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await controller.ApplyAsync(new MachineToolSnapshot()).ConfigureAwait(false));
        }

        /// <summary>
        /// Creates an unpublished generated machine at the public model factory seam.
        /// </summary>
        private MachineToolState CreateMachine(string name) =>
            m_fixture.Context.Context.CreateInstanceOfMachineToolType(
                m_fixture.Context.MachinesFolder,
                new QualifiedName(name + Guid.NewGuid().ToString("N"), m_fixture.Context.InstanceNamespaceIndex));

        /// <summary>
        /// Mutates one independent specification invariant, not the implementation's internal representation.
        /// </summary>
        private static MachineToolSnapshot InvalidExample(MachineToolSnapshot initial, string violation)
        {
            MachineToolSnapshot next = initial with { Revision = 1 };
            MachineToolProductionJob job = initial.Production.Jobs[0];
            MachineToolProgram program = job.Programs[0];
            MachineToolProductionPartSet set = job.PartSets[0];
            MachineToolProductionPart part = set.Parts[0];
            var invalidReason = new MachineToolProductionInterruption
            {
                Identifier = "Reason",
                ConditionClassId = Opc.Ua.ObjectTypeIds.SafetyConditionClassType,
                Message = new LocalizedText("Safety stop"),
                Sample = job.Sample
            };
            switch (violation)
            {
                case "NullProduction":
                    return next with { Production = null! };
                case "NullActiveProgram":
                    return next with { ActiveProgram = null! };
                case "ActiveProgramUnknownPhase":
                    return next with { ActiveProgram = program with { Phase = (MachineToolProductionPhase)99 } };
                case "ActiveProgramMissingTime":
                    return next with { ActiveProgram = program with { Sample = new(default, StatusCodes.Good) } };
                case "ActiveProgramUnknownJob":
                    return next with { ActiveProgram = program with { JobIdentifier = "Unknown" } };
                case "ActiveProgramWrongName":
                    return next with { ActiveProgram = program with { Name = "Unknown" } };
                case "ActiveProgramWrongState":
                    return next with { ActiveProgram = program with { Phase = MachineToolProductionPhase.Running } };
                case "ActiveProgramWrongSequence":
                    return next with { ActiveProgram = program with { TransitionSequence = 1 } };
                case "NoJobs":
                    return next with { Production = next.Production with { Jobs = [] } };
                case "NullJob":
                    return next with { Production = next.Production with { Jobs = [null!] } };
                case "DuplicateJobs":
                    return next with { Production = next.Production with { Jobs = [job, job] } };
                case "DuplicateJobIdentifiers":
                    return next with { Production = next.Production with { Jobs = [job, job with { Key = "Other" }] } };
                case "UnknownJobPhase":
                    job = job with { Phase = (MachineToolProductionPhase)99 };
                    break;
                case "JobMissingTime":
                    job = job with { Sample = new(default, StatusCodes.Good) };
                    break;
                case "JobEmptyIdentifier":
                    job = job with { Identifier = string.Empty };
                    break;
                case "JobTooLongIdentifier":
                    job = job with { Identifier = new string('x', 65) };
                    break;
                case "JobTooManyPrograms":
                    job = job with { Programs = [program, program, program] };
                    break;
                case "NoPrograms":
                    job = job with { Programs = [] };
                    break;
                case "NullProgram":
                    job = job with { Programs = [null!] };
                    break;
                case "DuplicatePrograms":
                    job = job with { Programs = [program, program] };
                    break;
                case "ProgramWrongJob":
                    job = job with { Programs = [program with { JobIdentifier = "Other" }] };
                    break;
                case "UnknownProgramPhase":
                    job = job with { Programs = [program with { Phase = (MachineToolProductionPhase)99 }] };
                    break;
                case "ProgramMissingTime":
                    job = job with { Programs = [program with { Sample = new(default, StatusCodes.Good) }] };
                    break;
                case "JobExceedsPlannedRuns":
                    job = job with { RunsCompleted = 3 };
                    break;
                case "UnknownPlanEnded":
                    job = job with { Phase = MachineToolProductionPhase.Ended, RunsPlannedIsValid = false };
                    break;
                case "EndedBeforePlannedRuns":
                    job = job with { Phase = MachineToolProductionPhase.Ended };
                    break;
                case "RunsIncrementWithoutTransition":
                    job = job with { RunsCompleted = 1 };
                    break;
                case "MissingJobTransition":
                    job = job with { Phase = MachineToolProductionPhase.Running };
                    break;
                case "SkippedJobTransition":
                    job = job with { Phase = MachineToolProductionPhase.Running, TransitionSequence = 2 };
                    break;
                case "InvalidJobTransition":
                    job = job with { Phase = MachineToolProductionPhase.Interrupted, TransitionSequence = 1 };
                    break;
                case "ChangedJobIdentifier":
                    job = job with { Identifier = "Other", Key = "Job" };
                    break;
                case "GoodExceedsCompleted":
                    job = job with { PartsGood = 1 };
                    break;
                case "BadHistoricalCounters":
                    job = job with { PartsGoodBeforeCurrentRun = 1 };
                    break;
                case "ChangedHistoricalCounters":
                    job = job with { PartsCompletedBeforeCurrentRun = 1, PartsCompleted = 1 };
                    break;
                case "NullPartSet":
                    job = job with { PartSets = [null!] };
                    break;
                case "DuplicatePartSets":
                    job = job with { PartSets = [set, set] };
                    break;
                case "MismatchedPlannedParts":
                    set = set with { PartsPlannedPerRun = 2 };
                    goto default;
                case "MismatchedCompletedParts":
                    set = set with { PartsCompletedPerRun = 1 };
                    goto default;
                case "NullPart":
                    set = set with { Parts = [null!] };
                    goto default;
                case "DuplicatePartKeys":
                    set = set with { Parts = [part, part], PartsPlannedPerRun = 2 };
                    goto default;
                case "DuplicatePartIdentifiers":
                    set = set with { Parts = [part, part with { Key = "Other" }], PartsPlannedPerRun = 2 };
                    goto default;
                case "UnknownPartQuality":
                    set = set with { Parts = [part with { Quality = (PartQuality)99 }] };
                    goto default;
                case "UnknownProcessIrregularity":
                    set = set with { Parts = [part with { Irregularity = (ProcessIrregularity)99 }] };
                    goto default;
                case "UnknownPartPhase":
                    set = set with { Parts = [part with { Phase = (MachineToolProductionPhase)99 }] };
                    goto default;
                case "WrongJobPartCounters":
                    job = job with { PartsCompleted = 1 };
                    break;
                case "StatisticsMissing":
                    return next with { Production = next.Production with { Statistics = null } };
                case "StatisticsDecreased":
                    return next with
                    {
                        Production = next.Production with
                        {
                            Statistics = new MachineToolProductionStatistics(99, job.Sample)
                        }
                    };
                case "StatisticsDisagreesWithModernCounter":
                    return next with
                    {
                        Production = next.Production with
                        {
                            Statistics = new MachineToolProductionStatistics(101, job.Sample)
                        }
                    };
                case "NullInterruption":
                    job = job with { Interruptions = [null!] };
                    break;
                case "BadConditionClass":
                    job = job with
                    {
                        Interruptions = [invalidReason with { ConditionClassId = Opc.Ua.ObjectIds.Server }]
                    };
                    break;
                case "InvalidSeverity":
                    job = job with { Interruptions = [invalidReason with { Severity = 1001 }] };
                    break;
                case "StaleRevision":
                    return next with { Revision = 0 };
                default:
                    job = job with { PartSets = [set] };
                    break;
            }
            return next with { Production = next.Production with { Jobs = [job] } };
        }

        /// <summary>The shared model owner, with isolated machines per test.</summary>
        private MachineToolRuntimeFixture m_fixture = null!;
    }
}
