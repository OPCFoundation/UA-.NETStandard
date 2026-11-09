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
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Deterministic conservation, delay, finite-buffer and product-genealogy specifications.
    /// </summary>
    [TestFixture]
    public sealed class SimulationTests
    {
        /// <summary>
        /// New runs have no implicit source admission before participation is established.
        /// </summary>
        [Test]
        public void NewRunWaitsForTenParticipantsWithoutGeneratingMaterial()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration());
            CommittedSnapshot state = engine.CreateRun("test-run");
            Assert.That(state.Lifecycle, Is.EqualTo(RunLifecycle.WaitingForParticipants));
            Assert.That(state.Stages.Count, Is.EqualTo(21));
            Assert.That(state.Parcels.Count, Is.Zero);
            Assert.That(state.Tick, Is.Zero);
        }

        /// <summary>
        /// A new source parcel cannot cross two substeps in one logical tick.
        /// </summary>
        [Test]
        public void MaterialMovesAtMostOneSubstepPerTickAndBalances()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration { PlannedSourceBatches = 1 });
            CommittedSnapshot initial = engine.CreateRun("test-run") with { Lifecycle = RunLifecycle.Running };
            CommittedSnapshot first = engine.Advance(initial, DateTime.UnixEpoch, AllProcesses());
            CommittedSnapshot second = engine.Advance(first, DateTime.UnixEpoch.AddSeconds(30), AllProcesses());
            Assert.That(first.Parcels.Count, Is.EqualTo(1));
            Assert.That(first.Parcels[0].Stage, Is.Zero);
            Assert.That(second.Parcels.Count, Is.EqualTo(1));
            Assert.That(second.Parcels[0].Stage, Is.EqualTo(1));
            Assert.That(second.Parcels[0].Mass.Total, Is.EqualTo(5_250_000));
            Assert.That(initial.Parcels.Count, Is.Zero);
            engine.ValidateSnapshot(second);
        }

        /// <summary>
        /// A fixed seed and explicit time inputs reproduce each complete generic scenario.
        /// </summary>
        [TestCase("healthy")]
        [TestCase("sorter-drift")]
        [TestCase("utility-disturbance")]
        [TestCase("combined")]
        [TestCase("recovery")]
        [TestCase("equipment-faults")]
        public void EveryScenarioIsDeterministicAndConservesEveryComponent(string scenario)
        {
            RecyclingConfiguration configuration = RecyclingConfiguration.ForScenario(scenario);
            CommittedSnapshot first = Run(configuration, 160);
            CommittedSnapshot second = Run(configuration, 160);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Packages.Count, Is.GreaterThan(0));
            Assert.That(first.SourceBatches, Is.EqualTo(configuration.PlannedSourceBatches));
            Assert.That(first.Stages.Span.ToArray().All(static stage => stage.Executions > 0), Is.True);
            Assert.That(first.Stages.Span.ToArray().Sum(static stage => stage.ThermalJoules), Is.GreaterThan(0));
            Assert.That(first.Stages.Span.ToArray().Sum(static stage => stage.ElectricalJoules), Is.GreaterThan(0));
        }

        /// <summary>
        /// Exact package weights, retained residuals and contribution-bearing splits/merges reconcile.
        /// </summary>
        [Test]
        public void SplitsMergesPackagesAndResidualInventoryRetainTheirGenealogy()
        {
            var configuration = new RecyclingConfiguration { PlannedSourceBatches = 4 };
            CommittedSnapshot result = Run(configuration, 120);
            Assert.That(result.History.Span.ToArray().Any(static execution => execution.Inputs.Count > 1), Is.True);
            Assert.That(result.History.Span.ToArray().Any(static execution => execution.Outputs.Count > 1), Is.True);
            Assert.That(result.Packages.Span.ToArray().All(package =>
                package.Material.Mass.Total == configuration.PackageMilligrams), Is.True);
            Assert.That(result.Parcels.Span.ToArray().All(static parcel => parcel.Stage == 20), Is.True);
            Assert.That(result.Stages[20].Inventory.Total, Is.LessThan(configuration.PackageMilligrams));
            Assert.That(result.Stages[20].Received.Total,
                Is.EqualTo(
                    (result.Packages.Count * configuration.PackageMilligrams) + result.Stages[20].Inventory.Total));
            Assert.That(result.Packages.Span.ToArray().Any(
                static package => package.Material.Contributions.Count > 1), Is.True);
        }

        /// <summary>
        /// Physical contaminant removal is not a fictitious color-removal process or automatic hold release.
        /// </summary>
        [Test]
        public void WashingDensityAndFiltrationDoNotRemoveColorSelectively()
        {
            RecyclingConfiguration configuration = RecyclingConfiguration.ForScenario("sorter-drift");
            CommittedSnapshot result = Run(configuration, 160);
            Assert.That(result.Holds.Count, Is.GreaterThan(0));
            Assert.That(result.Packages.Span.ToArray().Any(
                static package => package.Material.HoldIds.Count > 0), Is.True);
            foreach (MaterialExecution execution in result.History)
            {
                if (execution.Stage is >= 8 and <= 11 or 17)
                {
                    Assert.That(execution.Reject.ColoredPolymer, Is.Zero);
                    Assert.That(execution.Output.ColoredPolymer, Is.EqualTo(execution.Input.ColoredPolymer));
                }
            }
            Assert.That(result.Holds.Span.ToArray().All(
                static hold => hold.Disposition == HoldDisposition.Held), Is.True);
            Assert.That(result.Residues.Span.ToArray().Any(static residue => residue.IsEvaporation), Is.True);
            Assert.That(result.Residues.Span.ToArray().Where(static residue => residue.IsEvaporation)
                .All(static residue => residue.Material.Mass.Dry == 0), Is.True);
        }

        /// <summary>
        /// Lost process availability creates upstream backpressure without unbounded source admission.
        /// </summary>
        [Test]
        public void DisconnectedProcessFillsOnlyFiniteUpstreamBuffers()
        {
            var configuration = new RecyclingConfiguration { PlannedSourceBatches = 100 };
            var engine = new RecyclingEngine(configuration);
            CommittedSnapshot state = engine.CreateRun("bounded-run") with { Lifecycle = RunLifecycle.Running };
            ArrayOf<RecyclingProcess> available = AllProcesses().Span.ToArray()
                .Where(static process => process != RecyclingProcess.Sorting).ToArrayOf();
            for (int i = 0; i < 80; i++)
            {
                state = engine.Advance(state, DateTime.UnixEpoch.AddSeconds(i * 30), available);
            }
            Assert.That(state.SourceBatches, Is.LessThan(configuration.PlannedSourceBatches));
            Assert.That(state.Packages.Count, Is.Zero);
            Assert.That(state.Stages[2].State, Is.EqualTo(EquipmentState.Unavailable));
            Assert.That(state.Stages[1].State, Is.EqualTo(EquipmentState.BufferFull));
            Assert.That(
                state.Stages[0].Inventory.Total, Is.LessThanOrEqualTo(configuration.Stages[0].CapacityMilligrams));
            engine.ValidateSnapshot(state);
        }

        /// <summary>
        /// A thermal interlock is independent of electrical supply and prevents residence advancement.
        /// </summary>
        [Test]
        public void ThermalConstraintsFreezeResidenceWithoutConsumingElectricalEnergy()
        {
            var configuration = new RecyclingConfiguration { ThermalSupplyWatts = 0 };
            CommittedSnapshot state = Run(configuration, 60);
            Assert.That(state.Stages[10].State, Is.EqualTo(EquipmentState.ThermalConstraint));
            Assert.That(state.Stages[10].ThermalJoules, Is.Zero);
            Assert.That(state.Stages[10].ElectricalJoules, Is.Zero);
            Assert.That(state.Stages[10].Executions, Is.Zero);
            Assert.That(state.Packages.Count, Is.Zero);
        }

        /// <summary>
        /// Administrative lifecycle states preserve the last material sample exactly.
        /// </summary>
        [TestCase(RunLifecycle.Paused)]
        [TestCase(RunLifecycle.Stopped)]
        [TestCase(RunLifecycle.WaitingForParticipants)]
        public void NonRunningStateDoesNotAdvanceTickCountersOrSourceTimestamp(RunLifecycle lifecycle)
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration());
            CommittedSnapshot state = engine.CreateRun("paused-run") with { Lifecycle = lifecycle };
            Assert.That(engine.Advance(state, DateTime.UnixEpoch.AddDays(1), AllProcesses()), Is.SameAs(state));
        }

        /// <summary>
        /// Overflow rejects the proposal without corrupting the previous immutable image.
        /// </summary>
        [Test]
        public void ResourceLimitFailsWithoutMutatingThePreviousCommittedImage()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration { MaxHistory = 1 });
            CommittedSnapshot initial = engine.CreateRun("bounded-run") with { Lifecycle = RunLifecycle.Running };
            CommittedSnapshot first = engine.Advance(initial, DateTime.UnixEpoch, AllProcesses());
            ServiceResultException? exception = Assert.Throws<ServiceResultException>(() =>
                engine.Advance(first, DateTime.UnixEpoch.AddSeconds(30), AllProcesses()));
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(first.Tick, Is.EqualTo(1));
            Assert.That(first.SourceBatches, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid units, signs and schedules fail configuration validation.
        /// </summary>
        [Test]
        public void InvalidConfigurationIsRejectedInsteadOfClamped()
        {
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration { Seed = 0 }));
            Assert.Throws<ArgumentException>(() =>
                new RecyclingEngine(new RecyclingConfiguration { TickSeconds = 0 }));
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration
            {
                Windows = [new ScenarioWindow(10, 5, 1000, 1000, 1000)]
            }));
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration
            {
                SourceBatch = new MaterialMass(1, 1, -1, 1, 1, 1)
            }));
        }

        /// <summary>
        /// A peer cannot substitute a missing identity or incompatible wire schema for committed state.
        /// </summary>
        [Test]
        public void WireSnapshotsRejectMissingIdentifiersAndIncompatibleSchema()
        {
            var missingId = new Model.SnapshotDataType
            {
                SchemaVersion = 2,
                RunId = null,
                Scenario = "healthy",
                ConfigurationId = string.Empty
            };
            ServiceResultException missing = Assert.Throws<ServiceResultException>(() =>
                RecyclingModelMapping.FromWire(missingId))!;
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            ServiceResultException incompatible = Assert.Throws<ServiceResultException>(() =>
                RecyclingModelMapping.FromWire(new Model.SnapshotDataType { SchemaVersion = 1 }))!;
            Assert.That(incompatible.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        private static CommittedSnapshot Run(RecyclingConfiguration configuration, int ticks)
        {
            var engine = new RecyclingEngine(configuration);
            CommittedSnapshot state = engine.CreateRun("deterministic-run") with { Lifecycle = RunLifecycle.Running };
            for (int i = 0; i < ticks; i++)
            {
                state = engine.Advance(
                    state, DateTime.UnixEpoch.AddSeconds((long)i * configuration.TickSeconds), AllProcesses());
            }
            return state;
        }

        private static ArrayOf<RecyclingProcess> AllProcesses()
        {
            return Enum.GetValues<RecyclingProcess>().ToArrayOf();
        }
    }
}
