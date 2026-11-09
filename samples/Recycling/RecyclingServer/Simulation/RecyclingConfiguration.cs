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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Configurable residence, wet capacity, throughput and utility demand for a substep.
    /// </summary>
    public sealed record StageRecipe(
        string Name,
        RecyclingProcess Process,
        MaterialForm Form,
        int ResidenceTicks,
        long CapacityMilligrams,
        long ThroughputMilligrams,
        long ElectricalWatts,
        long ThermalWatts);

    /// <summary>
    /// A deterministic half-open fault window; supply and sorting recovery use parts per thousand.
    /// </summary>
    public sealed record ScenarioWindow(
        long StartTick,
        long EndTick,
        int SorterRecoveryPermille,
        int ElectricalSupplyPermille,
        int ThermalSupplyPermille);

    /// <summary>
    /// Explicit synthetic equipment failures; these are not companion-specification alarm enums.
    /// </summary>
    public enum RecyclingEquipmentFault
    {
        /// <summary>Material handling or size reduction is jammed.</summary>
        Jam,
        /// <summary>Pelletizing strands are broken.</summary>
        StrandBreak
    }

    /// <summary>
    /// A bounded half-open logical-tick fault window. Each window start contributes one absolute event.
    /// </summary>
    public sealed record EquipmentFaultWindow(
        string Stage,
        RecyclingEquipmentFault Kind,
        long StartTick,
        long EndTick);

    /// <summary>
    /// Versioned synthetic defaults; not a recovered producer contract or calibrated physical model.
    /// </summary>
    public sealed record RecyclingConfiguration
    {
        /// <summary>
        /// Supported configuration schema.
        /// </summary>
        public int SchemaVersion { get; init; } = 2;

        /// <summary>
        /// Public generic scenario identifier.
        /// </summary>
        public string Scenario { get; init; } = "healthy";

        /// <summary>
        /// Fixed seed. Zero is invalid for the specified xorshift generator.
        /// </summary>
        public ulong Seed { get; init; } = 104729;

        /// <summary>
        /// Logical duration of one tick, independent of host pacing.
        /// </summary>
        public int TickSeconds { get; init; } = 30;

        /// <summary>
        /// Finite source plan.
        /// </summary>
        public int PlannedSourceBatches { get; init; } = 24;

        /// <summary>
        /// Source recipe in milligrams. The seed varies natural/color allocation without changing dry mass.
        /// </summary>
        public MaterialMass SourceBatch { get; init; } =
            new(4_200_000, 300_000, 200_000, 180_000, 120_000, 250_000);

        /// <summary>
        /// Maximum seeded reallocation from natural to colored polymer, milligrams; zero disables that variation.
        /// </summary>
        public long SourceColorVariationMilligrams { get; init; } = 40_000;

        /// <summary>
        /// Generic source-zone identifiers assigned round-robin by admitted batch ordinal and retained in genealogy.
        /// </summary>
        public ArrayOf<string> SourceZones { get; init; } = ["zone-1"];

        /// <summary>
        /// Synthetic nominal hot-wash bath pH, not a calibrated chemical-response model.
        /// </summary>
        public double HotWashPh { get; init; } = 8.5;

        /// <summary>
        /// Configurable synthetic RGB anchor for natural polymer.
        /// </summary>
        public SyntheticRgb NaturalPolymerColor { get; init; } = new(224, 228, 232);

        /// <summary>
        /// Configurable synthetic RGB anchor for colored polymer.
        /// </summary>
        public SyntheticRgb ColoredPolymerColor { get; init; } = new(64, 96, 160);

        /// <summary>
        /// Wet mass of a complete package, milligrams.
        /// </summary>
        public long PackageMilligrams { get; init; } = 4_000_000;

        /// <summary>
        /// Added utility water per wash, parts per thousand of incoming dry mass.
        /// </summary>
        public int WashWaterPermille { get; init; } = 80;

        /// <summary>
        /// Product color hold threshold, parts per million of dry mass.
        /// </summary>
        public int ColorLimitPpm { get; init; } = 5_000;

        /// <summary>
        /// Product moisture hold threshold, parts per million of wet mass.
        /// </summary>
        public int MoistureLimitPpm { get; init; } = 3_000;

        /// <summary>
        /// Independent total electrical supply, watts.
        /// </summary>
        public long ElectricalSupplyWatts { get; init; } = 250_000;

        /// <summary>
        /// Independent total thermal supply, watts.
        /// </summary>
        public long ThermalSupplyWatts { get; init; } = 160_000;

        /// <summary>
        /// Maximum active material parcels; exceeding a bound stops evolution explicitly.
        /// </summary>
        public int MaxActiveParcels { get; init; } = 256;

        /// <summary>
        /// Maximum retained genealogy, packages and inspections, each independently bounded.
        /// </summary>
        public int MaxHistory { get; init; } = 4096;

        /// <summary>
        /// Maximum non-heartbeat command outcomes. These idempotency receipts are never evicted.
        /// Lease renewals retain one separate latest outcome per process.
        /// </summary>
        public int MaxCommands { get; init; } = 4096;

        /// <summary>
        /// Maximum simultaneous calls admitted to the single writer.
        /// </summary>
        public int MaxQueuedCommands { get; init; } = 64;

        /// <summary>
        /// Maximum explicit administrative step count.
        /// </summary>
        public int MaxStepCount { get; init; } = 10;

        /// <summary>
        /// Participant lease duration in wall-clock seconds.
        /// </summary>
        public int LeaseSeconds { get; init; } = 30;

        /// <summary>
        /// Detailed ordered transformations. Names and process ownership are validated.
        /// </summary>
        public ArrayOf<StageRecipe> Stages { get; init; } = CreateStages();

        /// <summary>
        /// Nonoverlapping, deterministic scenario schedule.
        /// </summary>
        public ArrayOf<ScenarioWindow> Windows { get; init; } = [];

        /// <summary>
        /// At most 128 deterministic equipment-fault windows, independent of sorter/utility disturbances.
        /// </summary>
        public ArrayOf<EquipmentFaultWindow> EquipmentFaults { get; init; } = [];

        /// <summary>
        /// Returns one of the documented independent synthetic scenarios.
        /// </summary>
        public static RecyclingConfiguration ForScenario(string scenario)
        {
            ArrayOf<ScenarioWindow> windows = scenario switch
            {
                "healthy" => [],
                "sorter-drift" => [new ScenarioWindow(8, 20, 250, 1000, 1000)],
                "utility-disturbance" => [new ScenarioWindow(18, 30, 1000, 400, 200)],
                "combined" => [new ScenarioWindow(8, 24, 250, 500, 200)],
                "recovery" =>
                [
                    new ScenarioWindow(8, 16, 200, 1000, 1000),
                    new ScenarioWindow(20, 28, 1000, 700, 100)
                ],
                "equipment-faults" => [],
                _ => throw new ArgumentException("Unknown generic scenario.", nameof(scenario))
            };
            ArrayOf<EquipmentFaultWindow> faults = scenario switch
            {
                "combined" =>
                [
                    new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 12, 16),
                    new EquipmentFaultWindow("primary-shredding", RecyclingEquipmentFault.Jam, 22, 25),
                    new EquipmentFaultWindow("pelletizing", RecyclingEquipmentFault.StrandBreak, 48, 51)
                ],
                "recovery" =>
                [
                    new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 10, 13),
                    new EquipmentFaultWindow("primary-shredding", RecyclingEquipmentFault.Jam, 20, 23),
                    new EquipmentFaultWindow("pelletizing", RecyclingEquipmentFault.StrandBreak, 45, 48)
                ],
                "equipment-faults" =>
                [
                    new EquipmentFaultWindow("feed-conveyor", RecyclingEquipmentFault.Jam, 8, 12),
                    new EquipmentFaultWindow("primary-shredding", RecyclingEquipmentFault.Jam, 14, 18),
                    new EquipmentFaultWindow("pelletizing", RecyclingEquipmentFault.StrandBreak, 40, 44)
                ],
                _ => []
            };
            return new RecyclingConfiguration { Scenario = scenario, Windows = windows, EquipmentFaults = faults };
        }

        /// <summary>
        /// Rejects unsupported identities, units, ranges and layouts rather than substituting defaults.
        /// </summary>
        public void Validate()
        {
            if (SchemaVersion != 2 || string.IsNullOrWhiteSpace(Scenario) || Scenario.Length > 64 ||
                Seed == 0 || TickSeconds is < 1 or > 3600 || PlannedSourceBatches is < 0 or > 100_000 ||
                !SourceBatch.IsNonnegative || SourceBatch.Dry <= 0 || SourceBatch.Total > 1_000_000_000 ||
                SourceColorVariationMilligrams < 0 || SourceColorVariationMilligrams > SourceBatch.NaturalPolymer ||
                PackageMilligrams is < 1 or > 1_000_000_000 || WashWaterPermille is < 0 or > 1000 ||
                ColorLimitPpm is < 0 or > 1_000_000 || MoistureLimitPpm is < 0 or > 1_000_000 ||
                ElectricalSupplyWatts is < 0 or > 10_000_000 || ThermalSupplyWatts is < 0 or > 10_000_000 ||
                MaxActiveParcels is < 1 or > 4096 || MaxHistory is < 1 or > 100_000 ||
                MaxCommands is < 1 or > 100_000 || MaxQueuedCommands is < 1 or > 1024 ||
                MaxStepCount is < 1 or > 100 || LeaseSeconds is < 2 or > 3600 ||
                !double.IsFinite(HotWashPh) || HotWashPh is < 0 or > 14 ||
                SourceZones.Count is < 1 or > 32 || EquipmentFaults.Count > 128)
            {
                throw new ArgumentException("Invalid recycling configuration quantities, schema or bounds.");
            }
            var zones = new HashSet<string>(StringComparer.Ordinal);
            foreach (string zone in SourceZones)
            {
                if (string.IsNullOrWhiteSpace(zone) || zone.Length > 64 || !zones.Add(zone) ||
                    !zone.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                {
                    throw new ArgumentException("Source zones must be unique, bounded generic identifiers.");
                }
            }

            ArrayOf<StageRecipe> expected = CreateStages();
            if (Stages.Count != expected.Count)
            {
                throw new ArgumentException("Exactly twenty-one configured substeps are required.");
            }
            for (int i = 0; i < Stages.Count; i++)
            {
                StageRecipe stage = Stages[i];
                if (stage.Name != expected[i].Name || stage.Process != expected[i].Process ||
                    stage.Form != expected[i].Form || stage.ResidenceTicks is < 1 or > 120 ||
                    stage.CapacityMilligrams is < 1 or > 10_000_000_000 ||
                    stage.ThroughputMilligrams is < 1 or > 1_000_000_000 ||
                    stage.ThroughputMilligrams > stage.CapacityMilligrams ||
                    stage.ElectricalWatts is < 0 or > 10_000_000 ||
                    stage.ThermalWatts is < 0 or > 10_000_000)
                {
                    throw new ArgumentException("Invalid substep identity, capacity, residence or utility demand.");
                }
            }
            if (Stages[0].CapacityMilligrams < SourceBatch.Total ||
                Stages[^1].CapacityMilligrams < PackageMilligrams)
            {
                throw new ArgumentException("Source and package must fit their configured buffers.");
            }
            long previousEnd = 0;
            foreach (ScenarioWindow window in Windows)
            {
                if (window.StartTick < previousEnd || window.EndTick <= window.StartTick ||
                    window.SorterRecoveryPermille is < 0 or > 1000 ||
                    window.ElectricalSupplyPermille is < 0 or > 1000 ||
                    window.ThermalSupplyPermille is < 0 or > 1000)
                {
                    throw new ArgumentException("Invalid or overlapping scenario window.");
                }
                previousEnd = window.EndTick;
            }
            var faultEnds = new Dictionary<(string Stage, RecyclingEquipmentFault Kind), long>();
            long previousStart = 0;
            foreach (EquipmentFaultWindow window in EquipmentFaults)
            {
                bool supported = window.Kind switch
                {
                    RecyclingEquipmentFault.Jam => window.Stage is
                        "feed-conveyor" or "primary-shredding" or "secondary-granulation",
                    RecyclingEquipmentFault.StrandBreak => window.Stage == "pelletizing",
                    _ => false
                };
                var key = (window.Stage, window.Kind);
                if (!supported || window.StartTick < 1 || window.StartTick < previousStart ||
                    window.EndTick <= window.StartTick ||
                    (faultEnds.TryGetValue(key, out long end) && window.StartTick <= end))
                {
                    throw new ArgumentException(
                        "Invalid or unsupported equipment-fault window; merge adjacent/overlapping windows.");
                }
                previousStart = window.StartTick;
                faultEnds[key] = window.EndTick;
            }
        }

        private static ArrayOf<StageRecipe> CreateStages()
        {
            var stages = new List<StageRecipe>();
            Add("collection-bins", RecyclingProcess.Collection, MaterialForm.Collected, 1, 400, 0);
            Add("collection-transport", RecyclingProcess.Collection, MaterialForm.Collected, 2, 1600, 0);
            Add("inbound-identification", RecyclingProcess.Sorting, MaterialForm.Collected, 1, 300, 0);
            Add("feed-conveyor", RecyclingProcess.Sorting, MaterialForm.Collected, 1, 1100, 0);
            Add("optical-sorter-one", RecyclingProcess.Sorting, MaterialForm.Sorted, 1, 2400, 0);
            Add("optical-sorter-two", RecyclingProcess.Sorting, MaterialForm.Sorted, 1, 2400, 0);
            Add("primary-shredding", RecyclingProcess.Shredding, MaterialForm.Shreds, 2, 18000, 0);
            Add("secondary-granulation", RecyclingProcess.Shredding, MaterialForm.Flakes, 2, 13000, 0);
            Add("pre-wash", RecyclingProcess.Washing, MaterialForm.Washed, 1, 3000, 0);
            Add("friction-wash", RecyclingProcess.Washing, MaterialForm.Washed, 2, 8000, 0);
            Add("hot-wash", RecyclingProcess.Washing, MaterialForm.Washed, 3, 4000, 28000);
            Add("rinse", RecyclingProcess.Washing, MaterialForm.Washed, 1, 3000, 0);
            Add("float-sink", RecyclingProcess.DensitySeparation, MaterialForm.Washed, 2, 6000, 0);
            Add("mechanical-dewatering", RecyclingProcess.Drying, MaterialForm.Flakes, 2, 11000, 0);
            Add("desiccant-thermal-drying", RecyclingProcess.Drying, MaterialForm.Dried, 4, 5000, 32000);
            Add("post-dry-buffer", RecyclingProcess.Drying, MaterialForm.Dried, 2, 500, 0);
            Add("extrusion", RecyclingProcess.Extrusion, MaterialForm.Melt, 3, 26000, 24000);
            Add("melt-filtration", RecyclingProcess.MeltFiltration, MaterialForm.Melt, 1, 3000, 0);
            Add("pelletizing", RecyclingProcess.Pelletizing, MaterialForm.Pellets, 2, 9000, 0);
            Add("pellet-screening", RecyclingProcess.Pelletizing, MaterialForm.Pellets, 1, 2000, 0);
            Add("bagging", RecyclingProcess.Bagging, MaterialForm.Packaged, 1, 1500, 0);
            return stages.ToArrayOf();

            void Add(
                string name,
                RecyclingProcess process,
                MaterialForm form,
                int residence,
                long electrical,
                long thermal)
            {
                stages.Add(new StageRecipe(
                    name, process, form, residence, 60_000_000, 6_000_000, electrical, thermal));
            }
        }
    }
}
