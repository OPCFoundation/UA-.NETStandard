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
using System.Globalization;
using System.Linq;

namespace Opc.Ua.Recycling
{
    public sealed partial class RecyclingEngine
    {
        /// <summary>
        /// Private proposal workspace; nothing escapes until every invariant has passed.
        /// </summary>
        private sealed class Transition
        {
            /// <summary>
            /// Creates an isolated proposal workspace from the previous committed image.
            /// </summary>
            internal Transition(
                RecyclingConfiguration configuration,
                CommittedSnapshot previous,
                ArrayOf<RecyclingProcess> available)
            {
                m_configuration = configuration;
                m_previous = previous;
                m_available = available.Span.ToArray().ToHashSet();
                m_stages = previous.Stages.Span.ToArray();
                m_parcels = [.. previous.Parcels.Span];
                m_packages = [.. previous.Packages.Span];
                m_residues = [.. previous.Residues.Span];
                m_holds = [.. previous.Holds.Span];
                m_history = [.. previous.History.Span];
                m_sources = [.. previous.Sources.Span];
                m_nextId = previous.NextId;
                m_tick = checked(previous.Tick + 1);
                m_random = previous.RandomState;
                m_sourceBatches = previous.SourceBatches;
                m_sourceMass = previous.SourceMass;
                m_window = configuration.Windows.Span.ToArray()
                    .FirstOrDefault(window => m_tick >= window.StartTick && m_tick < window.EndTick)
                    ?? new ScenarioWindow(0, long.MaxValue, 1000, 1000, 1000);
                m_electrical = MaterialLedger.Scale(
                    configuration.ElectricalSupplyWatts, m_window.ElectricalSupplyPermille);
                m_thermal = MaterialLedger.Scale(configuration.ThermalSupplyWatts, m_window.ThermalSupplyPermille);
            }

            /// <summary>
            /// Resolves downstream first. Newly received material cannot participate in this tick's next substep.
            /// </summary>
            internal CommittedSnapshot Advance(DateTime sourceTimestampUtc)
            {
                m_sourceTimestampUtc = sourceTimestampUtc;
                for (int stage = m_stages.Length - 1; stage >= 0; stage--)
                {
                    Process(stage);
                }
                AdmitSource();
                for (int i = 0; i < m_stages.Length; i++)
                {
                    int index = i;
                    m_stages[i] = m_stages[i] with
                    {
                        Inventory = MaterialLedger.Sum(
                            m_parcels.Where(parcel => parcel.Stage == index).Select(static parcel => parcel.Mass))
                    };
                }
                return m_previous with
                {
                    Tick = m_tick,
                    LogicalSeconds = checked(m_tick * m_configuration.TickSeconds),
                    SourceTimestampUtc = sourceTimestampUtc,
                    NextId = m_nextId,
                    RandomState = m_random,
                    SourceBatches = m_sourceBatches,
                    SourceMass = m_sourceMass,
                    Sources = MaterialLedger.Merge(m_sources),
                    Stages = m_stages.ToArrayOf(),
                    Parcels = m_parcels.OrderBy(static parcel => parcel.Id).ToArrayOf(),
                    Packages = m_packages.ToArrayOf(),
                    Residues = m_residues.ToArrayOf(),
                    Holds = m_holds.ToArrayOf(),
                    History = m_history.ToArrayOf()
                };
            }

            private void Process(int stage)
            {
                StageRecipe recipe = m_configuration.Stages[stage];
                EquipmentFaultWindow[] faults = m_configuration.EquipmentFaults.Span.ToArray()
                    .Where(window => window.Stage == recipe.Name).ToArray();
                StageSnapshot state = m_stages[stage] with
                {
                    ElectricalDemandWatts = 0,
                    ThermalDemandWatts = 0,
                    JamAlarm = faults.Any(window => window.Kind == RecyclingEquipmentFault.Jam &&
                        window.StartTick <= m_tick && m_tick < window.EndTick),
                    JamEvents = checked(m_stages[stage].JamEvents + faults.Count(window =>
                        window.Kind == RecyclingEquipmentFault.Jam && window.StartTick == m_tick)),
                    StrandBreakAlarm = faults.Any(window => window.Kind == RecyclingEquipmentFault.StrandBreak &&
                        window.StartTick <= m_tick && m_tick < window.EndTick),
                    StrandBreakEvents = checked(m_stages[stage].StrandBreakEvents + faults.Count(window =>
                        window.Kind == RecyclingEquipmentFault.StrandBreak && window.StartTick == m_tick))
                };
                List<MaterialParcel> queue = m_parcels.Where(parcel => parcel.Stage == stage)
                    .OrderBy(static parcel => parcel.Id).ToList();
                if (!m_available.Contains(recipe.Process))
                {
                    m_stages[stage] = state with { State = EquipmentState.Unavailable };
                    return;
                }
                if (!state.Enabled)
                {
                    m_stages[stage] = state with { State = EquipmentState.Stopped };
                    return;
                }
                if (stage == 10)
                {
                    state = state with
                    {
                        PhAvailable = true,
                        Ph = m_configuration.HotWashPh,
                        PhSourceTimestampUtc = m_sourceTimestampUtc
                    };
                }
                if (state.JamAlarm)
                {
                    m_stages[stage] = state with { State = EquipmentState.Jammed };
                    return;
                }
                if (state.StrandBreakAlarm)
                {
                    m_stages[stage] = state with { State = EquipmentState.StrandBroken };
                    return;
                }
                if (queue.Count == 0)
                {
                    m_stages[stage] = state with { State = EquipmentState.Idle };
                    return;
                }
                state = state with
                {
                    ElectricalDemandWatts = recipe.ElectricalWatts,
                    ThermalDemandWatts = recipe.ThermalWatts
                };
                if (recipe.ElectricalWatts > m_electrical)
                {
                    m_stages[stage] = state with { State = EquipmentState.ElectricalConstraint };
                    return;
                }
                if (recipe.ThermalWatts > m_thermal)
                {
                    m_stages[stage] = state with { State = EquipmentState.ThermalConstraint };
                    return;
                }
                m_electrical -= recipe.ElectricalWatts;
                m_thermal -= recipe.ThermalWatts;
                m_stages[stage] = state with
                {
                    State = EquipmentState.Idle,
                    ElectricalJoules = checked(state.ElectricalJoules +
                        (recipe.ElectricalWatts * m_configuration.TickSeconds)),
                    ThermalJoules = checked(state.ThermalJoules +
                        (recipe.ThermalWatts * m_configuration.TickSeconds))
                };
                var ready = new List<MaterialParcel>();
                foreach (MaterialParcel parcel in queue)
                {
                    MaterialParcel aged = parcel with
                    {
                        ResidenceRemaining = Math.Max(0, parcel.ResidenceRemaining - 1)
                    };
                    m_parcels[m_parcels.IndexOf(parcel)] = aged;
                    if (aged.ResidenceRemaining == 0)
                    {
                        ready.Add(aged);
                    }
                }
                if (stage == m_stages.Length - 1)
                {
                    Package(ready, recipe.ThroughputMilligrams, stage);
                    return;
                }
                long remaining = recipe.ThroughputMilligrams;
                foreach (MaterialParcel parcel in ready)
                {
                    if (remaining == 0)
                    {
                        break;
                    }
                    long amount = Math.Min(remaining, parcel.Mass.Total);
                    if (!Transform(stage, parcel, amount))
                    {
                        break;
                    }
                    remaining -= amount;
                }
            }

            private bool Transform(int stage, MaterialParcel parcel, long amount)
            {
                (ArrayOf<SourceContribution> selected, ArrayOf<SourceContribution> remainder) =
                    MaterialLedger.Take(parcel, amount);
                MaterialMass input = MaterialLedger.Sum(
                    selected.Span.ToArray().Select(static source => source.Mass));
                long addedWater = stage is >= 8 and <= 11
                    ? MaterialLedger.Scale(input.Dry, m_configuration.WashWaterPermille)
                    : 0;
                if (addedWater > 0)
                {
                    selected = MaterialLedger.Merge(
                        selected.Span.ToArray().Append(new SourceContribution(
                            "wash-water",
                            new MaterialMass(0, 0, 0, 0, 0, addedWater),
                            "utility-water")));
                }
                (ArrayOf<SourceContribution> kept, ArrayOf<SourceContribution> removed) =
                    MaterialLedger.Retain(selected, Retention(stage));
                MaterialMass output = MaterialLedger.Sum(kept.Span.ToArray().Select(static source => source.Mass));
                MaterialMass removal = MaterialLedger.Sum(removed.Span.ToArray().Select(static source => source.Mass));
                long occupied = MaterialLedger.Sum(
                    m_parcels.Where(material => material.Stage == stage + 1)
                        .Select(static material => material.Mass)).Total;
                if (occupied + output.Total > m_configuration.Stages[stage + 1].CapacityMilligrams)
                {
                    m_stages[stage] = m_stages[stage] with { State = EquipmentState.BufferFull };
                    return false;
                }

                m_parcels.Remove(parcel);
                var outputs = new List<long>();
                MaterialMass residual = default;
                if (!remainder.IsEmpty)
                {
                    MaterialParcel rest = parcel with { Id = Id(), Contributions = remainder };
                    m_parcels.Add(rest);
                    outputs.Add(rest.Id);
                    residual = rest.Mass;
                }

                ArrayOf<long> holdIds = parcel.HoldIds;
                if (stage == 5 && AboveLimit(output.ColoredPolymer, output.Dry, m_configuration.ColorLimitPpm))
                {
                    holdIds = AddHold(holdIds, stage, "color");
                }
                if (output.Total > 0)
                {
                    var next = new MaterialParcel(
                        Id(),
                        stage + 1,
                        m_tick,
                        m_configuration.Stages[stage + 1].ResidenceTicks,
                        m_configuration.Stages[stage].Form,
                        kept,
                        holdIds);
                    m_parcels.Add(next);
                    outputs.Add(next.Id);
                    RecordColor(stage, next.Id, output);
                }
                bool evaporation = stage is 14 or 16;
                if (removal.Total > 0)
                {
                    var waste = parcel with
                    {
                        Id = Id(),
                        Contributions = removed,
                        HoldIds = holdIds,
                        ResidenceRemaining = 0
                    };
                    m_residues.Add(new MaterialResidue(waste.Id, m_tick, stage, evaporation, waste));
                    outputs.Add(waste.Id);
                }
                var addition = new MaterialMass(0, 0, 0, 0, 0, addedWater);
                MaterialMass reject = evaporation ? default : removal;
                MaterialMass emission = evaporation ? removal : default;
                StageSnapshot current = m_stages[stage];
                m_stages[stage] = current with
                {
                    State = EquipmentState.Executing,
                    Transferred = current.Transferred + output,
                    Additions = current.Additions + addition,
                    Rejected = current.Rejected + reject,
                    Evaporated = current.Evaporated + emission,
                    Executions = checked(current.Executions + 1)
                };
                m_stages[stage + 1] = m_stages[stage + 1] with
                {
                    Received = m_stages[stage + 1].Received + output
                };
                if (addedWater > 0)
                {
                    m_sources.Add(new SourceContribution("wash-water", addition, "utility-water"));
                }
                m_history.Add(new MaterialExecution(
                    Id(), m_tick, stage, [parcel.Id], outputs.ToArrayOf(),
                    parcel.Mass, addition, output + residual, reject, emission));
                return true;
            }

            private void Package(List<MaterialParcel> ready, long throughput, int stage)
            {
                long available = MaterialLedger.Sum(ready.Select(static parcel => parcel.Mass)).Total;
                while (available >= m_configuration.PackageMilligrams &&
                    throughput >= m_configuration.PackageMilligrams)
                {
                    long needed = m_configuration.PackageMilligrams;
                    MaterialMass input = default;
                    MaterialMass residual = default;
                    var contributions = new List<SourceContribution>();
                    var inputs = new List<long>();
                    var outputs = new List<long>();
                    var holds = new HashSet<long>();
                    while (needed > 0)
                    {
                        MaterialParcel parcel = ready[0];
                        ready.RemoveAt(0);
                        m_parcels.Remove(parcel);
                        inputs.Add(parcel.Id);
                        input += parcel.Mass;
                        long amount = Math.Min(needed, parcel.Mass.Total);
                        (ArrayOf<SourceContribution> selected, ArrayOf<SourceContribution> remainder) =
                            MaterialLedger.Take(parcel, amount);
                        contributions.AddRange(selected.Span.ToArray());
                        holds.UnionWith(parcel.HoldIds.Span.ToArray());
                        if (!remainder.IsEmpty)
                        {
                            MaterialParcel rest = parcel with { Id = Id(), Contributions = remainder };
                            m_parcels.Add(rest);
                            ready.Insert(0, rest);
                            outputs.Add(rest.Id);
                            residual += rest.Mass;
                        }
                        needed -= amount;
                    }
                    ArrayOf<SourceContribution> merged = MaterialLedger.Merge(contributions);
                    MaterialMass mass = MaterialLedger.Sum(merged.Span.ToArray().Select(static source => source.Mass));
                    ArrayOf<long> holdIds = holds.Order().ToArrayOf();
                    if (AboveLimit(mass.ColoredPolymer, mass.Dry, m_configuration.ColorLimitPpm))
                    {
                        holdIds = AddHold(holdIds, stage, "color");
                    }
                    if (AboveLimit(mass.Water, mass.Total, m_configuration.MoistureLimitPpm))
                    {
                        holdIds = AddHold(holdIds, stage, "moisture");
                    }
                    long packageId = Id();
                    var material = new MaterialParcel(
                        packageId, stage, m_tick, 0, MaterialForm.Packaged, merged, holdIds);
                    m_packages.Add(new MaterialPackage(packageId, m_tick, material));
                    RecordColor(stage, packageId, mass);
                    outputs.Add(packageId);
                    m_history.Add(new MaterialExecution(
                        Id(), m_tick, stage, inputs.ToArrayOf(), outputs.ToArrayOf(),
                        input, default, mass + residual, default, default));
                    m_stages[stage] = m_stages[stage] with
                    {
                        State = EquipmentState.Executing,
                        Transferred = m_stages[stage].Transferred + mass,
                        Executions = checked(m_stages[stage].Executions + 1)
                    };
                    available -= m_configuration.PackageMilligrams;
                    throughput -= m_configuration.PackageMilligrams;
                }
            }

            private ArrayOf<long> AddHold(ArrayOf<long> inherited, int stage, string reason)
            {
                foreach (long id in inherited)
                {
                    if (m_holds.Any(hold => hold.Id == id && hold.Reason == reason))
                    {
                        return inherited;
                    }
                }
                long holdId = Id();
                m_holds.Add(new QualityHold(holdId, m_tick, stage, reason, HoldDisposition.Held, string.Empty, 0));
                return inherited.Span.ToArray().Append(holdId).Order().ToArrayOf();
            }

            private void AdmitSource()
            {
                if (m_sourceBatches >= m_configuration.PlannedSourceBatches ||
                    !m_available.Contains(RecyclingProcess.Collection) || !m_stages[0].Enabled)
                {
                    return;
                }
                long occupied = MaterialLedger.Sum(
                    m_parcels.Where(static parcel => parcel.Stage == 0).Select(static parcel => parcel.Mass)).Total;
                if (occupied + m_configuration.SourceBatch.Total > m_configuration.Stages[0].CapacityMilligrams)
                {
                    m_stages[0] = m_stages[0] with { State = EquipmentState.BufferFull };
                    return;
                }
                m_random ^= m_random << 13;
                m_random ^= m_random >> 7;
                m_random ^= m_random << 17;
                long variation = (long)(m_random % (ulong)(m_configuration.SourceColorVariationMilligrams + 1));
                MaterialMass mass = m_configuration.SourceBatch with
                {
                    NaturalPolymer = m_configuration.SourceBatch.NaturalPolymer - variation,
                    ColoredPolymer = m_configuration.SourceBatch.ColoredPolymer + variation
                };
                string sourceId = "source-" + (m_sourceBatches + 1).ToString(CultureInfo.InvariantCulture);
                string zone = m_configuration.SourceZones[m_sourceBatches % m_configuration.SourceZones.Count];
                var source = new SourceContribution(sourceId, mass, zone);
                var parcel = new MaterialParcel(
                    Id(), 0, m_tick, m_configuration.Stages[0].ResidenceTicks, MaterialForm.Collected, [source], []);
                m_parcels.Add(parcel);
                m_sources.Add(source);
                m_history.Add(new MaterialExecution(
                    Id(), m_tick, -1, [], [parcel.Id], default, mass, mass, default, default));
                m_sourceBatches++;
                m_sourceMass += mass;
                m_stages[0] = m_stages[0] with { Received = m_stages[0].Received + mass };
                RecordColor(0, parcel.Id, mass);
            }

            private void RecordColor(int stage, long materialId, MaterialMass mass)
            {
                bool available = mass.NaturalPolymer + mass.ColoredPolymer > 0;
                m_stages[stage] = m_stages[stage] with
                {
                    ColorAvailable = available,
                    Color = available
                        ? CalculateSyntheticColor(mass.NaturalPolymer, mass.ColoredPolymer, m_configuration)
                        : default,
                    ColorMaterialId = materialId,
                    ColorNaturalPolymerMilligrams = mass.NaturalPolymer,
                    ColorColoredPolymerMilligrams = mass.ColoredPolymer,
                    ColorSourceTimestampUtc = available ? m_sourceTimestampUtc : DateTime.UnixEpoch
                };
            }

            private MaterialMass Retention(int stage)
            {
                long color = 1000 - MaterialLedger.Scale(850, m_window.SorterRecoveryPermille);
                return stage switch
                {
                    4 => new MaterialMass(1000, color, 300, 500, 1000, 1000),
                    5 => new MaterialMass(
                        1000, 1000 - MaterialLedger.Scale(900, m_window.SorterRecoveryPermille), 300, 700, 1000, 1000),
                    6 => new MaterialMass(995, 995, 1000, 1000, 1000, 1000),
                    7 => new MaterialMass(997, 997, 1000, 1000, 1000, 1000),
                    8 => new MaterialMass(1000, 1000, 1000, 950, 400, 300),
                    9 => new MaterialMass(1000, 1000, 1000, 450, 300, 300),
                    10 => new MaterialMass(1000, 1000, 1000, 150, 900, 300),
                    11 => new MaterialMass(1000, 1000, 1000, 900, 400, 200),
                    12 => new MaterialMass(995, 995, 30, 1000, 1000, 800),
                    13 => new MaterialMass(1000, 1000, 1000, 1000, 1000, 50),
                    14 or 16 => new MaterialMass(1000, 1000, 1000, 1000, 1000, 100),
                    17 => new MaterialMass(1000, 1000, 1000, 300, 100, 1000),
                    19 => new MaterialMass(985, 985, 985, 985, 985, 985),
                    _ => new MaterialMass(1000, 1000, 1000, 1000, 1000, 1000)
                };
            }

            private static bool AboveLimit(long component, long total, int limitPpm)
            {
                return total > 0 && (decimal)component * 1_000_000 > (decimal)total * limitPpm;
            }

            private long Id()
            {
                long id = m_nextId;
                m_nextId = checked(m_nextId + 1);
                return id;
            }

            private readonly RecyclingConfiguration m_configuration;
            private readonly CommittedSnapshot m_previous;
            private readonly HashSet<RecyclingProcess> m_available;
            private readonly StageSnapshot[] m_stages;
            private readonly List<MaterialParcel> m_parcels;
            private readonly List<MaterialPackage> m_packages;
            private readonly List<MaterialResidue> m_residues;
            private readonly List<QualityHold> m_holds;
            private readonly List<MaterialExecution> m_history;
            private readonly List<SourceContribution> m_sources;
            private readonly ScenarioWindow m_window;
            private readonly long m_tick;
            private long m_nextId;
            private ulong m_random;
            private int m_sourceBatches;
            private MaterialMass m_sourceMass;
            private long m_electrical;
            private long m_thermal;
            private DateTime m_sourceTimestampUtc;
        }
    }
}
