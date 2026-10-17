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
using System.Text.Json.Serialization;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// The ten independently authenticated process views, in material-flow order.
    /// </summary>
    public enum RecyclingProcess
    {
        /// <summary>Collection bins and transport.</summary>
        Collection,
        /// <summary>Inbound identification, feeding and two optical sorters.</summary>
        Sorting,
        /// <summary>Shredder and granulator.</summary>
        Shredding,
        /// <summary>Four distinct wash operations.</summary>
        Washing,
        /// <summary>Float/sink separation.</summary>
        DensitySeparation,
        /// <summary>Mechanical, thermal and buffered drying.</summary>
        Drying,
        /// <summary>Melting and extrusion.</summary>
        Extrusion,
        /// <summary>Particle filtration, not color removal.</summary>
        MeltFiltration,
        /// <summary>Pellet formation and screening.</summary>
        Pelletizing,
        /// <summary>Weighed full packages and residual inventory.</summary>
        Bagging
    }

    /// <summary>
    /// Simulator lifecycle, deliberately distinct from equipment and product state.
    /// </summary>
    public enum RunLifecycle
    {
        /// <summary>Fresh-run registration barrier.</summary>
        WaitingForParticipants,
        /// <summary>Material time may advance.</summary>
        Running,
        /// <summary>Only administrative/control revisions may advance.</summary>
        Paused,
        /// <summary>Production has been deliberately stopped.</summary>
        Stopped,
        /// <summary>A configured resource bound prevents further material evolution.</summary>
        ResourceLimited
    }

    /// <summary>
    /// Sample equipment state; these values are not standardized companion states.
    /// </summary>
    public enum EquipmentState
    {
        /// <summary>No eligible material.</summary>
        Idle,
        /// <summary>A material transformation was committed.</summary>
        Executing,
        /// <summary>An operator disabled the process.</summary>
        Stopped,
        /// <summary>The authenticated process view is not initialized or its lease expired.</summary>
        Unavailable,
        /// <summary>The downstream buffer is full.</summary>
        BufferFull,
        /// <summary>Insufficient electrical supply.</summary>
        ElectricalConstraint,
        /// <summary>Insufficient thermal supply.</summary>
        ThermalConstraint,
        /// <summary>A scheduled jam blocks material processing and residence progress.</summary>
        Jammed,
        /// <summary>A scheduled strand break blocks pellet formation.</summary>
        StrandBroken
    }

    /// <summary>
    /// Material form resulting from a physical operation.
    /// </summary>
    public enum MaterialForm
    {
        /// <summary>Collected material.</summary>
        Collected,
        /// <summary>Sorted material.</summary>
        Sorted,
        /// <summary>Coarsely reduced material.</summary>
        Shreds,
        /// <summary>Granulated material.</summary>
        Flakes,
        /// <summary>Washed, still wet material.</summary>
        Washed,
        /// <summary>Dried flakes.</summary>
        Dried,
        /// <summary>Molten polymer with entrained contaminants.</summary>
        Melt,
        /// <summary>Solid pellets.</summary>
        Pellets,
        /// <summary>A complete weighed package.</summary>
        Packaged
    }

    /// <summary>
    /// Governed disposition of a persistent material hold.
    /// </summary>
    public enum HoldDisposition
    {
        /// <summary>Not available for release.</summary>
        Held,
        /// <summary>An authorized operator explicitly released the hold.</summary>
        Released,
        /// <summary>An authorized operator rejected the affected material.</summary>
        Rejected
    }

    /// <summary>
    /// Exact nonnegative component masses in milligrams. Water is never included in dry mass.
    /// </summary>
    public readonly record struct MaterialMass(
        long NaturalPolymer,
        long ColoredPolymer,
        long DensePolymer,
        long Labels,
        long Dirt,
        long Water)
    {
        /// <summary>
        /// Total dry mass in milligrams.
        /// </summary>
        [JsonIgnore]
        public long Dry => checked(NaturalPolymer + ColoredPolymer + DensePolymer + Labels + Dirt);

        /// <summary>
        /// Total wet mass in milligrams.
        /// </summary>
        [JsonIgnore]
        public long Total => checked(Dry + Water);

        /// <summary>
        /// Whether every component is nonnegative.
        /// </summary>
        [JsonIgnore]
        public bool IsNonnegative => NaturalPolymer >= 0 && ColoredPolymer >= 0 && DensePolymer >= 0 &&
            Labels >= 0 && Dirt >= 0 && Water >= 0;

        /// <summary>
        /// Component-wise exact addition.
        /// </summary>
        public static MaterialMass operator +(MaterialMass left, MaterialMass right)
        {
            return new MaterialMass(
                checked(left.NaturalPolymer + right.NaturalPolymer),
                checked(left.ColoredPolymer + right.ColoredPolymer),
                checked(left.DensePolymer + right.DensePolymer),
                checked(left.Labels + right.Labels),
                checked(left.Dirt + right.Dirt),
                checked(left.Water + right.Water));
        }

        /// <summary>
        /// Component-wise exact subtraction.
        /// </summary>
        public static MaterialMass operator -(MaterialMass left, MaterialMass right)
        {
            return new MaterialMass(
                checked(left.NaturalPolymer - right.NaturalPolymer),
                checked(left.ColoredPolymer - right.ColoredPolymer),
                checked(left.DensePolymer - right.DensePolymer),
                checked(left.Labels - right.Labels),
                checked(left.Dirt - right.Dirt),
                checked(left.Water - right.Water));
        }
    }

    /// <summary>
    /// A retained component-wise contribution from a material or utility source.
    /// </summary>
    public sealed record SourceContribution(string SourceId, MaterialMass Mass, string SourceZone);

    /// <summary>
    /// Eight-bit synthetic display response, not a physically calibrated RGB, Lab or color-difference measurement.
    /// </summary>
    public readonly record struct SyntheticRgb(byte Red, byte Green, byte Blue);

    /// <summary>
    /// Immutable inventory parcel with residence state, source contributions and inherited hold identifiers.
    /// </summary>
    public sealed record MaterialParcel(
        long Id,
        int Stage,
        long EnteredTick,
        int ResidenceRemaining,
        MaterialForm Form,
        ArrayOf<SourceContribution> Contributions,
        ArrayOf<long> HoldIds)
    {
        /// <summary>
        /// Exact mass reconstructed from contributions, not an independently mutable total.
        /// </summary>
        [JsonIgnore]
        public MaterialMass Mass
        {
            get
            {
                MaterialMass total = default;
                foreach (SourceContribution contribution in Contributions)
                {
                    total += contribution.Mass;
                }
                return total;
            }
        }
    }

    /// <summary>
    /// Immutable complete package. Held packages remain produced inventory, not released output.
    /// </summary>
    public sealed record MaterialPackage(long Id, long Tick, MaterialParcel Material);

    /// <summary>
    /// A retained reject, wastewater or evaporated-water sink with exact inherited genealogy.
    /// </summary>
    public sealed record MaterialResidue(long Id, long Tick, int Stage, bool IsEvaporation, MaterialParcel Material);

    /// <summary>
    /// A persistent inspection and its explicitly authorized disposition.
    /// </summary>
    public sealed record QualityHold(
        long Id,
        long Tick,
        int Stage,
        string Reason,
        HoldDisposition Disposition,
        string DisposedBy,
        long DispositionRevision);

    /// <summary>
    /// Bounded genealogy record for one committed split, merge, transformation or source admission.
    /// </summary>
    public sealed record MaterialExecution(
        long Id,
        long Tick,
        int Stage,
        ArrayOf<long> Inputs,
        ArrayOf<long> Outputs,
        MaterialMass Input,
        MaterialMass Addition,
        MaterialMass Output,
        MaterialMass Reject,
        MaterialMass Evaporation);

    /// <summary>
    /// Aligned run-to-date utility intensity. Denominator is packaged dry mass, including held packages.
    /// A zero denominator is explicitly unavailable; the numeric placeholder is never infinity or a product claim.
    /// </summary>
    public sealed record RecyclingEnergyIntensity(
        string BaselineId,
        long StartTick,
        long EndTick,
        long ElectricalJoules,
        long ThermalJoules,
        long PackagedDryMilligrams)
    {
        /// <summary>
        /// Whether any dry packaged output exists in the aligned window.
        /// </summary>
        public bool IsApplicable => PackagedDryMilligrams > 0;

        /// <summary>
        /// Electrical joules per kilogram, meaningful only when IsApplicable is true.
        /// </summary>
        public double ElectricalJoulesPerKilogram => IsApplicable
            ? (double)ElectricalJoules * 1_000_000 / PackagedDryMilligrams
            : 0;

        /// <summary>
        /// Thermal joules per kilogram, meaningful only when IsApplicable is true.
        /// </summary>
        public double ThermalJoulesPerKilogram => IsApplicable
            ? (double)ThermalJoules * 1_000_000 / PackagedDryMilligrams
            : 0;
    }

    /// <summary>
    /// Current process-view membership. Lease timestamps are wall time, not material time.
    /// </summary>
    public sealed record ProcessParticipant(
        RecyclingProcess Process,
        string Identity,
        string ConnectionId,
        bool Initialized,
        DateTime LeaseExpiresUtc,
        long ObservedRevision);

    /// <summary>
    /// One detailed transformation's committed counters, inventory and utility state.
    /// </summary>
    public sealed record StageSnapshot
    {
        /// <summary>
        /// Stable position in the sample recipe.
        /// </summary>
        public int Index { get; init; }

        /// <summary>
        /// Authenticated process owner.
        /// </summary>
        public RecyclingProcess Process { get; init; }

        /// <summary>
        /// Sample substep identifier, not a companion-specification BrowseName.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Current execution/interlock state.
        /// </summary>
        public EquipmentState State { get; init; }

        /// <summary>
        /// Independently enabled by an authorized production command.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// Whether the configured jam window is active at the current material tick.
        /// </summary>
        public bool JamAlarm { get; init; }

        /// <summary>
        /// Absolute count of scheduled jam onsets in this run; never increments during re-projection.
        /// </summary>
        public long JamEvents { get; init; }

        /// <summary>
        /// Whether the configured pelletizing strand-break window is active.
        /// </summary>
        public bool StrandBreakAlarm { get; init; }

        /// <summary>
        /// Absolute count of scheduled strand-break onsets in this run.
        /// </summary>
        public long StrandBreakEvents { get; init; }

        /// <summary>
        /// Whether a configured synthetic hot-wash pH observation exists.
        /// </summary>
        public bool PhAvailable { get; init; }

        /// <summary>
        /// Configured synthetic bath pH; meaningful only when PhAvailable is true.
        /// </summary>
        public double Ph { get; init; }

        /// <summary>
        /// Original wall timestamp of the committed pH observation.
        /// </summary>
        public DateTime PhSourceTimestampUtc { get; init; } = DateTime.UnixEpoch;

        /// <summary>
        /// Whether the last output contains polymer classes covered by the synthetic color response.
        /// </summary>
        public bool ColorAvailable { get; init; }

        /// <summary>
        /// Synthetic mass-weighted display response for the last outgoing parcel or complete package.
        /// </summary>
        public SyntheticRgb Color { get; init; }

        /// <summary>
        /// Material identifier underlying the response; collection uses the most recently admitted source parcel.
        /// </summary>
        public long ColorMaterialId { get; init; }

        /// <summary>
        /// Natural-polymer mass in the response's basis, milligrams.
        /// </summary>
        public long ColorNaturalPolymerMilligrams { get; init; }

        /// <summary>
        /// Colored-polymer mass in the response's basis, milligrams.
        /// </summary>
        public long ColorColoredPolymerMilligrams { get; init; }

        /// <summary>
        /// Original wall timestamp of this committed color observation, not the time a view reprojects it.
        /// </summary>
        public DateTime ColorSourceTimestampUtc { get; init; } = DateTime.UnixEpoch;

        /// <summary>
        /// Current material inventory.
        /// </summary>
        public MaterialMass Inventory { get; init; }

        /// <summary>
        /// Total material received, including initial source admission.
        /// </summary>
        public MaterialMass Received { get; init; }

        /// <summary>
        /// Total outgoing material.
        /// </summary>
        public MaterialMass Transferred { get; init; }

        /// <summary>
        /// Total utility water added.
        /// </summary>
        public MaterialMass Additions { get; init; }

        /// <summary>
        /// Total solid rejects and drained wastewater.
        /// </summary>
        public MaterialMass Rejected { get; init; }

        /// <summary>
        /// Total evaporated water.
        /// </summary>
        public MaterialMass Evaporated { get; init; }

        /// <summary>
        /// Cumulative electrical energy, joules.
        /// </summary>
        public long ElectricalJoules { get; init; }

        /// <summary>
        /// Cumulative thermal energy, joules.
        /// </summary>
        public long ThermalJoules { get; init; }

        /// <summary>
        /// Requested electrical power in the latest material tick, watts.
        /// </summary>
        public long ElectricalDemandWatts { get; init; }

        /// <summary>
        /// Requested thermal power in the latest material tick, watts.
        /// </summary>
        public long ThermalDemandWatts { get; init; }

        /// <summary>
        /// Number of committed transformation executions.
        /// </summary>
        public long Executions { get; init; }
    }

    /// <summary>
    /// The only authoritative production image. Records and their array elements are immutable.
    /// </summary>
    public sealed record CommittedSnapshot
    {
        /// <summary>
        /// Schema used by durable state and domain mapping.
        /// </summary>
        public int SchemaVersion { get; init; } = 2;

        /// <summary>
        /// Deliberate reset establishes a different run identity.
        /// </summary>
        public string RunId { get; init; } = string.Empty;

        /// <summary>
        /// Versioned scenario label, explicitly a synthetic recipe rather than source-parity evidence.
        /// </summary>
        public string Scenario { get; init; } = string.Empty;

        /// <summary>
        /// Exact configuration/model hash established by the durable coordinator.
        /// </summary>
        public string ConfigurationId { get; init; } = string.Empty;

        /// <summary>
        /// Increases for administrative as well as material commits.
        /// </summary>
        public long Revision { get; init; }

        /// <summary>
        /// Increases only for material evolution.
        /// </summary>
        public long Tick { get; init; }

        /// <summary>
        /// Exact material-time duration, seconds.
        /// </summary>
        public long LogicalSeconds { get; init; }

        /// <summary>
        /// Wall-clock source sample timestamp; unchanged by control-only commits and re-projection.
        /// </summary>
        public DateTime SourceTimestampUtc { get; init; } = DateTime.UnixEpoch;

        /// <summary>
        /// Current simulator lifecycle.
        /// </summary>
        public RunLifecycle Lifecycle { get; init; }

        /// <summary>
        /// Explicit diagnostics for bounded-resource backpressure.
        /// </summary>
        public string BlockingReason { get; init; } = string.Empty;

        /// <summary>
        /// Deterministic pseudorandom state, persisted with every transition.
        /// </summary>
        public ulong RandomState { get; init; }

        /// <summary>
        /// Next unique material/hold/execution identifier in this run.
        /// </summary>
        public long NextId { get; init; } = 1;

        /// <summary>
        /// Number of source batches already admitted.
        /// </summary>
        public int SourceBatches { get; init; }

        /// <summary>
        /// Exact admitted source mass.
        /// </summary>
        public MaterialMass SourceMass { get; init; }

        /// <summary>
        /// Component-wise source ledger, including added wash water.
        /// </summary>
        public ArrayOf<SourceContribution> Sources { get; init; } = [];

        /// <summary>
        /// All detailed process states.
        /// </summary>
        public ArrayOf<StageSnapshot> Stages { get; init; } = [];

        /// <summary>
        /// Bounded active residence queues.
        /// </summary>
        public ArrayOf<MaterialParcel> Parcels { get; init; } = [];

        /// <summary>
        /// Bounded produced packages.
        /// </summary>
        public ArrayOf<MaterialPackage> Packages { get; init; } = [];

        /// <summary>
        /// Retained solid, wastewater and evaporation sinks.
        /// </summary>
        public ArrayOf<MaterialResidue> Residues { get; init; } = [];

        /// <summary>
        /// Inspections persist independently of equipment recovery.
        /// </summary>
        public ArrayOf<QualityHold> Holds { get; init; } = [];

        /// <summary>
        /// Bounded, never silently dropped genealogy.
        /// </summary>
        public ArrayOf<MaterialExecution> History { get; init; } = [];

        /// <summary>
        /// Persisted availability events and authenticated role ownership.
        /// </summary>
        public ArrayOf<ProcessParticipant> Participants { get; init; } = [];

        /// <summary>
        /// Aligned run-to-date electrical and thermal numerators with their exact dry-output denominator.
        /// </summary>
        [JsonIgnore]
        public RecyclingEnergyIntensity Energy => new(
            ConfigurationId.Length == 0 ? "synthetic-" + Scenario + "-schema-2" : ConfigurationId,
            0,
            Tick,
            Stages.Span.ToArray().Sum(static stage => stage.ElectricalJoules),
            Stages.Span.ToArray().Sum(static stage => stage.ThermalJoules),
            Packages.Span.ToArray().Sum(static package => package.Material.Mass.Dry));
    }
}
