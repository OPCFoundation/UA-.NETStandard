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
    /// The authoritative engine, not process views, owns synthetic chemistry, color, provenance and equipment faults.
    /// </summary>
    [TestFixture]
    public sealed class SyntheticTelemetryTests
    {
        /// <summary>
        /// Hot-wash pH is configured explicitly and unavailable/paused steps retain its original source timestamp.
        /// </summary>
        [Test]
        public void ConfiguredHotWashPhIsSampledOnlyByTheAvailableAuthoritativeStage()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration { HotWashPh = 9.25 });
            CommittedSnapshot initial = Running(engine);
            Assert.That(initial.Stages[10].PhAvailable, Is.False);
            CommittedSnapshot sampled = engine.Advance(initial, DateTime.UnixEpoch, AllProcesses());
            Assert.That(sampled.Stages[10].PhAvailable, Is.True);
            Assert.That(sampled.Stages[10].Ph, Is.EqualTo(9.25));
            Assert.That(sampled.Stages.Span.ToArray().Where(static stage => stage.Index != 10)
                .All(static stage => !stage.PhAvailable), Is.True);
            ArrayOf<RecyclingProcess> disconnected = AllProcesses().Span.ToArray()
                .Where(static process => process != RecyclingProcess.Washing).ToArrayOf();
            CommittedSnapshot held = engine.Advance(sampled, DateTime.UnixEpoch.AddMinutes(1), disconnected);
            Assert.That(held.Stages[10].PhSourceTimestampUtc, Is.EqualTo(DateTime.UnixEpoch));
            Assert.That(held.Stages[10].Ph, Is.EqualTo(9.25));
            CommittedSnapshot paused = held with { Lifecycle = RunLifecycle.Paused };
            Assert.That(engine.Advance(paused, DateTime.UnixEpoch.AddHours(1), AllProcesses()), Is.SameAs(paused));
            CommittedSnapshot resumed = engine.Advance(held, DateTime.UnixEpoch.AddMinutes(2), AllProcesses());
            Assert.That(resumed.Stages[10].PhSourceTimestampUtc, Is.EqualTo(DateTime.UnixEpoch.AddMinutes(2)));
        }

        /// <summary>
        /// A known 70/30 polymer mixture gives an exact documented linear display response, independent of water mass.
        /// </summary>
        [Test]
        public void SyntheticRgbUsesConfiguredAnchorsAndCommittedPolymerComposition()
        {
            var configuration = new RecyclingConfiguration
            {
                PlannedSourceBatches = 1,
                SourceBatch = new MaterialMass(700_000, 300_000, 0, 0, 0, 500_000),
                SourceColorVariationMilligrams = 0,
                NaturalPolymerColor = new SyntheticRgb(200, 220, 240),
                ColoredPolymerColor = new SyntheticRgb(20, 40, 60)
            };
            var engine = new RecyclingEngine(configuration);
            CommittedSnapshot state = engine.Advance(Running(engine), DateTime.UnixEpoch, AllProcesses());
            StageSnapshot collection = state.Stages[0];
            Assert.That(collection.ColorAvailable, Is.True);
            Assert.That(collection.Color, Is.EqualTo(new SyntheticRgb(146, 166, 186)));
            Assert.That(collection.ColorNaturalPolymerMilligrams, Is.EqualTo(700_000));
            Assert.That(collection.ColorColoredPolymerMilligrams, Is.EqualTo(300_000));
            Assert.That(collection.ColorMaterialId, Is.EqualTo(state.Parcels[0].Id));
            Assert.That(collection.ColorSourceTimestampUtc, Is.EqualTo(DateTime.UnixEpoch));
            ArrayOf<RecyclingProcess> disconnected = AllProcesses().Span.ToArray()
                .Where(static process => process != RecyclingProcess.Collection).ToArrayOf();
            CommittedSnapshot held = engine.Advance(state, DateTime.UnixEpoch.AddMinutes(5), disconnected);
            Assert.That(held.Stages[0].Color, Is.EqualTo(collection.Color));
            Assert.That(held.Stages[0].ColorSourceTimestampUtc, Is.EqualTo(DateTime.UnixEpoch));
            Assert.That(held.Stages[0].ColorMaterialId, Is.EqualTo(collection.ColorMaterialId));
            CommittedSnapshot paused = held with { Lifecycle = RunLifecycle.Paused };
            Assert.That(engine.Advance(paused, DateTime.UnixEpoch.AddHours(1), AllProcesses()), Is.SameAs(paused));

            var changedEngine = new RecyclingEngine(configuration with
            {
                SourceBatch = new MaterialMass(400_000, 600_000, 0, 0, 0, 500_000)
            });
            CommittedSnapshot changed = changedEngine.Advance(
                Running(changedEngine), DateTime.UnixEpoch, AllProcesses());
            Assert.That(changed.Stages[0].Color, Is.EqualTo(new SyntheticRgb(92, 112, 132)));
            Assert.That(state.SourceMass.Dry, Is.EqualTo(changed.SourceMass.Dry));
            Assert.That(state.SourceMass.Water, Is.EqualTo(changed.SourceMass.Water));
        }

        /// <summary>
        /// No modeled polymer basis produces an explicitly absent color observation, not a fabricated black reading.
        /// </summary>
        [Test]
        public void SyntheticRgbIsUnavailableWithoutNaturalOrColoredPolymer()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration
            {
                PlannedSourceBatches = 1,
                SourceBatch = new MaterialMass(0, 0, 1_000_000, 0, 0, 0),
                SourceColorVariationMilligrams = 0
            });
            CommittedSnapshot state = engine.Advance(Running(engine), DateTime.UnixEpoch, AllProcesses());
            Assert.That(state.Stages[0].ColorAvailable, Is.False);
            Assert.That(state.Stages[0].ColorNaturalPolymerMilligrams, Is.Zero);
            Assert.That(state.Stages[0].ColorColoredPolymerMilligrams, Is.Zero);
            engine.ValidateSnapshot(state);
        }

        /// <summary>
        /// Source zones survive every split, wash-water addition, merge, package and retained residue.
        /// </summary>
        [Test]
        public void SourceZonesRemainAttachedToContributionsAcrossSplitsAndMerges()
        {
            var configuration = new RecyclingConfiguration
            {
                PlannedSourceBatches = 4,
                SourceZones = ["zone-1", "zone-2"],
                SourceColorVariationMilligrams = 0
            };
            var engine = new RecyclingEngine(configuration);
            CommittedSnapshot state = Advance(engine, Running(engine), 120);
            Assert.That(state.Sources.Span.ToArray().Single(static source => source.SourceId == "source-1").SourceZone,
                Is.EqualTo("zone-1"));
            Assert.That(state.Sources.Span.ToArray().Single(static source => source.SourceId == "source-2").SourceZone,
                Is.EqualTo("zone-2"));
            Assert.That(state.Sources.Span.ToArray().Single(static source => source.SourceId == "source-3").SourceZone,
                Is.EqualTo("zone-1"));
            Assert.That(
                state.Sources.Span.ToArray().Single(static source => source.SourceId == "wash-water").SourceZone,
                Is.EqualTo("utility-water"));
            var zones = state.Sources.Span.ToArray().ToDictionary(
                static source => source.SourceId, static source => source.SourceZone, StringComparer.Ordinal);
            MaterialParcel[] retained = state.Parcels.Span.ToArray()
                .Concat(state.Packages.Span.ToArray().Select(static package => package.Material))
                .Concat(state.Residues.Span.ToArray().Select(static residue => residue.Material)).ToArray();
            Assert.That(retained.SelectMany(static parcel => parcel.Contributions.Span.ToArray())
                .All(contribution => contribution.SourceZone == zones[contribution.SourceId]), Is.True);
            Assert.That(state.Packages.Span.ToArray().Any(static package =>
                package.Material.Contributions.Span.ToArray().Where(static source => source.Mass.Dry > 0)
                    .Select(static source => source.SourceZone).Distinct().Count() == 2), Is.True);
            engine.ValidateSnapshot(state);
        }

        /// <summary>
        /// A feed jam starts once, blocks its residence queue causally, and recovers without a duplicate event.
        /// </summary>
        [Test]
        public void JamWindowBlocksMaterialAndResidenceWithOneAbsoluteOnset()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration
            {
                PlannedSourceBatches = 1,
                EquipmentFaults = [new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 6, 9)]
            });
            CommittedSnapshot before = Advance(engine, Running(engine), 5);
            MaterialParcel input = before.Parcels.Span.ToArray().Single(static parcel => parcel.Stage == 3);
            CommittedSnapshot blocked = Advance(engine, before, 3);
            Assert.That(blocked.Tick, Is.EqualTo(8));
            Assert.That(blocked.Stages[3].JamAlarm, Is.True);
            Assert.That(blocked.Stages[3].JamEvents, Is.EqualTo(1));
            Assert.That(blocked.Stages[3].State, Is.EqualTo(EquipmentState.Jammed));
            Assert.That(blocked.Stages[3].Transferred, Is.EqualTo(before.Stages[3].Transferred));
            Assert.That(blocked.Stages[3].ElectricalJoules, Is.EqualTo(before.Stages[3].ElectricalJoules));
            MaterialParcel waiting = blocked.Parcels.Span.ToArray().Single(static parcel => parcel.Stage == 3);
            Assert.That(waiting.Id, Is.EqualTo(input.Id));
            Assert.That(waiting.ResidenceRemaining, Is.EqualTo(input.ResidenceRemaining));
            CommittedSnapshot recovered = Advance(engine, blocked, 1);
            Assert.That(recovered.Stages[3].JamAlarm, Is.False);
            Assert.That(recovered.Stages[3].JamEvents, Is.EqualTo(1));
            Assert.That(recovered.Parcels.Span.ToArray().Single().Stage, Is.EqualTo(4));
            engine.ValidateSnapshot(recovered);
        }

        /// <summary>
        /// Strand breaks stop pellet formation without removing or duplicating material.
        /// </summary>
        [Test]
        public void StrandBreakWindowStopsPelletizingWithoutConsumingResidenceOrMass()
        {
            var engine = new RecyclingEngine(new RecyclingConfiguration
            {
                PlannedSourceBatches = 1,
                EquipmentFaults =
                [
                    new EquipmentFaultWindow("pelletizing", RecyclingEquipmentFault.StrandBreak, 34, 37)
                ]
            });
            CommittedSnapshot before = Advance(engine, Running(engine), 33);
            MaterialParcel input = before.Parcels.Span.ToArray().Single(static parcel => parcel.Stage == 18);
            CommittedSnapshot broken = Advance(engine, before, 3);
            Assert.That(broken.Stages[18].State, Is.EqualTo(EquipmentState.StrandBroken));
            Assert.That(broken.Stages[18].StrandBreakAlarm, Is.True);
            Assert.That(broken.Stages[18].StrandBreakEvents, Is.EqualTo(1));
            Assert.That(broken.Stages[18].Transferred, Is.EqualTo(before.Stages[18].Transferred));
            Assert.That(
                broken.Parcels.Span.ToArray().Single().ResidenceRemaining, Is.EqualTo(input.ResidenceRemaining));
            CommittedSnapshot recovered = Advance(engine, broken, 4);
            Assert.That(recovered.Stages[18].StrandBreakAlarm, Is.False);
            Assert.That(recovered.Stages[18].StrandBreakEvents, Is.EqualTo(1));
            Assert.That(recovered.Packages.Count, Is.EqualTo(1));
            engine.ValidateSnapshot(recovered);
        }

        /// <summary>
        /// Current telemetry is available in the generated wire contract and is preserved by a typed round trip.
        /// </summary>
        [Test]
        public void WireRoundTripPreservesSyntheticTelemetryAndSourceZones()
        {
            RecyclingConfiguration configuration = RecyclingConfiguration.ForScenario("equipment-faults") with
            {
                HotWashPh = 9.25,
                SourceZones = ["zone-1", "zone-2"]
            };
            var engine = new RecyclingEngine(configuration);
            CommittedSnapshot state = Advance(engine, Running(engine), 80);
            CommittedSnapshot decoded = RecyclingModelMapping.FromWire(RecyclingModelMapping.ToWire(state));
            Assert.That(decoded.SchemaVersion, Is.EqualTo(2));
            Assert.That(decoded.Stages, Is.EqualTo(state.Stages));
            Assert.That(decoded.Sources, Is.EqualTo(state.Sources));
            Assert.That(decoded.Packages, Is.EqualTo(state.Packages));
            Assert.That(decoded.Stages[3].JamEvents, Is.EqualTo(1));
            Assert.That(decoded.Stages[6].JamEvents, Is.EqualTo(1));
            Assert.That(decoded.Stages[18].StrandBreakEvents, Is.EqualTo(1));
            Assert.That(decoded.Stages[10].Ph, Is.EqualTo(9.25));
            engine.ValidateSnapshot(decoded);
        }

        /// <summary>
        /// Synthetic configuration rejects invalid chemistry, zone identities and unsupported fault targets.
        /// </summary>
        [Test]
        public void InvalidSyntheticConfigurationAndLegacySchemaAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration { HotWashPh = 15 }));
            Assert.Throws<ArgumentException>(() =>
                new RecyclingEngine(new RecyclingConfiguration { HotWashPh = double.NaN }));
            Assert.Throws<ArgumentException>(() =>
                new RecyclingEngine(new RecyclingConfiguration { SourceZones = ["zone-1", "zone-1"] }));
            Assert.Throws<ArgumentException>(() =>
                new RecyclingEngine(new RecyclingConfiguration { SchemaVersion = 1 }));
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration
            {
                EquipmentFaults =
                [
                    new EquipmentFaultWindow("optical-sorter-one", RecyclingEquipmentFault.Jam, 1, 4)
                ]
            }));
            Assert.Throws<ArgumentException>(() => new RecyclingEngine(new RecyclingConfiguration
            {
                EquipmentFaults =
                [
                    new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 1, 4),
                    new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 4, 6)
                ]
            }));
        }

        private static CommittedSnapshot Running(RecyclingEngine engine)
        {
            return engine.CreateRun("synthetic-telemetry") with { Lifecycle = RunLifecycle.Running };
        }

        private static CommittedSnapshot Advance(RecyclingEngine engine, CommittedSnapshot state, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                state = engine.Advance(state, DateTime.UnixEpoch.AddSeconds(state.Tick * 30), AllProcesses());
            }
            return state;
        }

        private static ArrayOf<RecyclingProcess> AllProcesses()
        {
            return Enum.GetValues<RecyclingProcess>().ToArrayOf();
        }
    }
}
