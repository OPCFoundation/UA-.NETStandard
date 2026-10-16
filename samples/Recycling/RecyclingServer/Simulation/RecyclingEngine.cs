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
    /// Pure deterministic state transitions. Only the coordinator may publish a transition as committed.
    /// </summary>
    public sealed partial class RecyclingEngine
    {
        /// <summary>
        /// Creates an engine after validating the complete synthetic configuration.
        /// </summary>
        public RecyclingEngine(RecyclingConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            configuration.Validate();
            m_configuration = configuration with
            {
                Stages = configuration.Stages.Span.ToArray().ToArrayOf(),
                Windows = configuration.Windows.Span.ToArray().ToArrayOf(),
                SourceZones = configuration.SourceZones.Span.ToArray().ToArrayOf(),
                EquipmentFaults = configuration.EquipmentFaults.Span.ToArray().ToArrayOf()
            };
        }

        /// <summary>
        /// Creates a fresh stopped-at-the-barrier run with no implicit material generation.
        /// </summary>
        public CommittedSnapshot CreateRun(string runId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            return new CommittedSnapshot
            {
                RunId = runId,
                Scenario = m_configuration.Scenario,
                RandomState = m_configuration.Seed,
                Lifecycle = RunLifecycle.WaitingForParticipants,
                Stages = m_configuration.Stages.Span.ToArray()
                    .Select((recipe, index) => new StageSnapshot
                    {
                        Index = index,
                        Name = recipe.Name,
                        Process = recipe.Process,
                        State = EquipmentState.Unavailable
                    }).ToArrayOf()
            };
        }

        /// <summary>
        /// Computes one tick from the previous image and explicit time/availability inputs.
        /// Paused, stopped and waiting states are returned unchanged.
        /// </summary>
        public CommittedSnapshot Advance(
            CommittedSnapshot previous,
            DateTime sourceTimestampUtc,
            ArrayOf<RecyclingProcess> availableProcesses)
        {
            ArgumentNullException.ThrowIfNull(previous);
            if (sourceTimestampUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The source sample timestamp must be UTC.", nameof(sourceTimestampUtc));
            }
            if (previous.Lifecycle != RunLifecycle.Running)
            {
                return previous;
            }
            var transition = new Transition(m_configuration, previous, availableProcesses);
            CommittedSnapshot next = transition.Advance(sourceTimestampUtc);
            ValidateSnapshot(next);
            return next;
        }

        /// <summary>
        /// Validates exact global, per-substep, per-source and per-execution conservation and resource bounds.
        /// Used on every transition and on recovered state before it is exposed.
        /// </summary>
        public void ValidateSnapshot(CommittedSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.SchemaVersion != 2 || string.IsNullOrWhiteSpace(snapshot.RunId) ||
                snapshot.Scenario != m_configuration.Scenario ||
                snapshot.Revision < 0 || snapshot.Tick < 0 ||
                snapshot.LogicalSeconds != checked(snapshot.Tick * m_configuration.TickSeconds) ||
                snapshot.Stages.Count != m_configuration.Stages.Count ||
                snapshot.SourceBatches < 0 || snapshot.SourceBatches > m_configuration.PlannedSourceBatches ||
                snapshot.RandomState == 0 || snapshot.NextId <= 0 ||
                !snapshot.SourceMass.IsNonnegative || !Enum.IsDefined(snapshot.Lifecycle))
            {
                throw new InvalidOperationException("Invalid committed simulation identity, counters or schema.");
            }
            if (snapshot.Parcels.Count > m_configuration.MaxActiveParcels ||
                snapshot.History.Count > m_configuration.MaxHistory ||
                snapshot.Packages.Count > m_configuration.MaxHistory ||
                snapshot.Holds.Count > m_configuration.MaxHistory ||
                snapshot.Residues.Count > m_configuration.MaxHistory ||
                snapshot.Sources.Count > m_configuration.MaxHistory)
            {
                throw new ServiceResultException(
                    StatusCodes.BadResourceUnavailable,
                    "The configured material, genealogy, inspection or package retention bound was reached.");
            }

            MaterialMass inventory = default;
            MaterialMass produced = default;
            MaterialMass rejected = default;
            MaterialMass evaporated = default;
            MaterialMass additions = default;
            var sources = new Dictionary<string, MaterialMass>(StringComparer.Ordinal);
            var sourceZones = new Dictionary<string, string>(StringComparer.Ordinal);
            var ids = new HashSet<long>();
            var holdIds = new HashSet<long>();
            foreach (QualityHold hold in snapshot.Holds)
            {
                if (!ids.Add(hold.Id) || hold.Id <= 0 || hold.Id >= snapshot.NextId ||
                    hold.Stage < 0 || hold.Stage >= snapshot.Stages.Count ||
                    hold.Tick > snapshot.Tick || !Enum.IsDefined(hold.Disposition) ||
                    (hold.Disposition != HoldDisposition.Held && string.IsNullOrWhiteSpace(hold.DisposedBy)))
                {
                    throw new InvalidOperationException("Invalid hold or disposition record.");
                }
                holdIds.Add(hold.Id);
            }
            foreach (MaterialParcel parcel in snapshot.Parcels)
            {
                ValidateMaterial(parcel, snapshot, ids, holdIds, sources, sourceZones);
                if (parcel.Stage < 0 || parcel.Stage >= snapshot.Stages.Count ||
                    parcel.EnteredTick > snapshot.Tick || parcel.ResidenceRemaining < 0)
                {
                    throw new InvalidOperationException("Invalid material residence queue.");
                }
                inventory += parcel.Mass;
            }
            foreach (MaterialPackage package in snapshot.Packages)
            {
                ValidateMaterial(package.Material, snapshot, ids, holdIds, sources, sourceZones);
                if (package.Id != package.Material.Id || package.Tick > snapshot.Tick ||
                    package.Material.Mass.Total != m_configuration.PackageMilligrams)
                {
                    throw new InvalidOperationException("A package does not reconcile to the exact package weight.");
                }
                produced += package.Material.Mass;
            }
            foreach (MaterialResidue residue in snapshot.Residues)
            {
                ValidateMaterial(residue.Material, snapshot, ids, holdIds, sources, sourceZones);
                if (residue.Id != residue.Material.Id || residue.Tick > snapshot.Tick ||
                    (residue.IsEvaporation && residue.Material.Mass.Dry != 0))
                {
                    throw new InvalidOperationException("Invalid retained material sink.");
                }
                if (residue.IsEvaporation)
                {
                    evaporated += residue.Material.Mass;
                }
                else
                {
                    rejected += residue.Material.Mass;
                }
            }
            for (int i = 0; i < snapshot.Stages.Count; i++)
            {
                StageSnapshot stage = snapshot.Stages[i];
                StageRecipe recipe = m_configuration.Stages[i];
                MaterialMass inStage = MaterialLedger.Sum(
                    snapshot.Parcels.Span.ToArray().Where(parcel => parcel.Stage == i)
                        .Select(static parcel => parcel.Mass));
                if (stage.Index != i || stage.Name != recipe.Name || stage.Process != recipe.Process ||
                    stage.Inventory != inStage || stage.Inventory.Total > recipe.CapacityMilligrams ||
                    stage.Received + stage.Additions !=
                        stage.Inventory + stage.Transferred + stage.Rejected + stage.Evaporated ||
                    !stage.Received.IsNonnegative || !stage.Transferred.IsNonnegative ||
                    !stage.Additions.IsNonnegative || !stage.Rejected.IsNonnegative ||
                    !stage.Evaporated.IsNonnegative || stage.ElectricalJoules < 0 || stage.ThermalJoules < 0)
                {
                    throw new InvalidOperationException("A substep's capacity or exact material balance is invalid.");
                }
                ValidateTelemetry(stage, snapshot);
                additions += stage.Additions;
            }
            if (snapshot.SourceMass + additions != inventory + produced + rejected + evaporated)
            {
                throw new InvalidOperationException("The global dry-component and water balance does not close.");
            }
            if (snapshot.Sources.Count != sources.Count)
            {
                throw new InvalidOperationException("The source ledger does not match retained contributions.");
            }
            var seenSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourceContribution source in snapshot.Sources)
            {
                if (!seenSources.Add(source.SourceId) ||
                    !sources.TryGetValue(source.SourceId, out MaterialMass retained) || retained != source.Mass ||
                    !sourceZones.TryGetValue(source.SourceId, out string? zone) || zone != source.SourceZone ||
                    (source.Mass.Dry > 0 && !m_configuration.SourceZones.Span.ToArray().Contains(source.SourceZone)))
                {
                    throw new InvalidOperationException("A source's component mass or source-zone identity was lost.");
                }
            }
            foreach (MaterialExecution execution in snapshot.History)
            {
                if (!ids.Add(execution.Id) || execution.Id <= 0 || execution.Id >= snapshot.NextId ||
                    execution.Tick > snapshot.Tick || execution.Input + execution.Addition !=
                        execution.Output + execution.Reject + execution.Evaporation)
                {
                    throw new InvalidOperationException("An execution's split/merge material balance does not close.");
                }
            }
            var participants = new HashSet<RecyclingProcess>();
            foreach (ProcessParticipant participant in snapshot.Participants)
            {
                if (!Enum.IsDefined(participant.Process) || !participants.Add(participant.Process) ||
                    string.IsNullOrWhiteSpace(participant.Identity))
                {
                    throw new InvalidOperationException("Invalid or duplicate process membership.");
                }
            }
        }

        private static void ValidateMaterial(
            MaterialParcel material,
            CommittedSnapshot snapshot,
            HashSet<long> ids,
            HashSet<long> holdIds,
            Dictionary<string, MaterialMass> sources,
            Dictionary<string, string> sourceZones)
        {
            if (material.Id <= 0 || material.Id >= snapshot.NextId || !ids.Add(material.Id) ||
                material.Mass.Total <= 0 || !material.Mass.IsNonnegative ||
                !Enum.IsDefined(material.Form))
            {
                throw new InvalidOperationException("Invalid or duplicate retained material.");
            }
            foreach (long holdId in material.HoldIds)
            {
                if (!holdIds.Contains(holdId))
                {
                    throw new InvalidOperationException("Material refers to an unknown quality hold.");
                }
            }
            foreach (SourceContribution contribution in material.Contributions)
            {
                if (string.IsNullOrWhiteSpace(contribution.SourceId) || !contribution.Mass.IsNonnegative ||
                    contribution.Mass.Total <= 0 || string.IsNullOrWhiteSpace(contribution.SourceZone) ||
                    contribution.SourceZone.Length > 64 ||
                    (sourceZones.TryGetValue(contribution.SourceId, out string? zone) &&
                        zone != contribution.SourceZone))
                {
                    throw new InvalidOperationException("Invalid material contribution.");
                }
                sources.TryGetValue(contribution.SourceId, out MaterialMass existing);
                sources[contribution.SourceId] = existing + contribution.Mass;
                sourceZones[contribution.SourceId] = contribution.SourceZone;
            }
        }

        private void ValidateTelemetry(StageSnapshot stage, CommittedSnapshot snapshot)
        {
            EquipmentFaultWindow[] faults = m_configuration.EquipmentFaults.Span.ToArray()
                .Where(window => window.Stage == stage.Name).ToArray();
            if (stage.JamEvents != faults.Count(window =>
                    window.Kind == RecyclingEquipmentFault.Jam && window.StartTick <= snapshot.Tick) ||
                stage.StrandBreakEvents != faults.Count(window =>
                    window.Kind == RecyclingEquipmentFault.StrandBreak && window.StartTick <= snapshot.Tick) ||
                stage.JamAlarm != faults.Any(window => window.Kind == RecyclingEquipmentFault.Jam &&
                    window.StartTick <= snapshot.Tick && snapshot.Tick < window.EndTick) ||
                stage.StrandBreakAlarm != faults.Any(window => window.Kind == RecyclingEquipmentFault.StrandBreak &&
                    window.StartTick <= snapshot.Tick && snapshot.Tick < window.EndTick))
            {
                throw new InvalidOperationException("Equipment-fault state or absolute onset counters are invalid.");
            }
            if (!Enum.IsDefined(stage.State) ||
                (stage.PhAvailable && (stage.Index != 10 || stage.Ph != m_configuration.HotWashPh ||
                    stage.PhSourceTimestampUtc.Kind != DateTimeKind.Utc)) ||
                (!stage.PhAvailable && (stage.Ph != 0 || stage.PhSourceTimestampUtc != DateTime.UnixEpoch)))
            {
                throw new InvalidOperationException("Invalid synthetic pH observation or equipment state.");
            }
            long natural = stage.ColorNaturalPolymerMilligrams;
            long colored = stage.ColorColoredPolymerMilligrams;
            if (natural is < 0 or > 1_000_000_000 || colored is < 0 or > 1_000_000_000 ||
                stage.ColorMaterialId < 0 ||
                stage.ColorMaterialId >= snapshot.NextId ||
                stage.ColorAvailable != (natural + colored > 0) ||
                (stage.ColorMaterialId == 0 && stage.ColorAvailable) ||
                (stage.ColorAvailable &&
                    (stage.Color != CalculateSyntheticColor(natural, colored, m_configuration) ||
                        stage.ColorSourceTimestampUtc.Kind != DateTimeKind.Utc)) ||
                (!stage.ColorAvailable && (stage.Color != default ||
                    stage.ColorSourceTimestampUtc != DateTime.UnixEpoch)))
            {
                throw new InvalidOperationException("Invalid synthetic color response or response basis.");
            }
        }

        private static SyntheticRgb CalculateSyntheticColor(
            long natural,
            long colored,
            RecyclingConfiguration configuration)
        {
            long total = checked(natural + colored);
            return new SyntheticRgb(
                Mix(configuration.NaturalPolymerColor.Red, configuration.ColoredPolymerColor.Red),
                Mix(configuration.NaturalPolymerColor.Green, configuration.ColoredPolymerColor.Green),
                Mix(configuration.NaturalPolymerColor.Blue, configuration.ColoredPolymerColor.Blue));

            byte Mix(byte naturalChannel, byte coloredChannel)
            {
                return checked((byte)((((decimal)natural * naturalChannel) +
                    ((decimal)colored * coloredChannel)) / total));
            }
        }

        private readonly RecyclingConfiguration m_configuration;
    }
}
