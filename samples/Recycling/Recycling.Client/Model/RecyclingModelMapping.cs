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
using Opc.Ua.Recycling.Model;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Explicit deep copies between immutable committed records and source-generated OPC UA structures.
    /// </summary>
    public static class RecyclingModelMapping
    {
        /// <summary>
        /// The independently authored sample namespace. It makes no companion-specification claim.
        /// </summary>
        public const string NamespaceUri = "urn:opcfoundation.org:sample:recycling";

        /// <summary>
        /// Materializes a typed UA snapshot, preserving original source timestamps and committed counters.
        /// </summary>
        public static SnapshotDataType ToWire(CommittedSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return new SnapshotDataType
            {
                SchemaVersion = snapshot.SchemaVersion,
                RunId = snapshot.RunId,
                Scenario = snapshot.Scenario,
                ConfigurationId = snapshot.ConfigurationId,
                Revision = snapshot.Revision,
                Tick = snapshot.Tick,
                LogicalSeconds = snapshot.LogicalSeconds,
                SourceTimestampUtc = snapshot.SourceTimestampUtc,
                Lifecycle = (LifecycleEnum)snapshot.Lifecycle,
                BlockingReason = snapshot.BlockingReason,
                RandomState = snapshot.RandomState,
                NextId = snapshot.NextId,
                SourceBatches = snapshot.SourceBatches,
                SourceMass = ToWire(snapshot.SourceMass),
                Sources = Map(snapshot.Sources, ToWire),
                Stages = Map(snapshot.Stages, ToWire),
                Parcels = Map(snapshot.Parcels, ToWire),
                Packages = Map(snapshot.Packages, static package => new PackageDataType
                {
                    Id = package.Id,
                    Tick = package.Tick,
                    Material = ToWire(package.Material)
                }),
                Residues = Map(snapshot.Residues, static residue => new ResidueDataType
                {
                    Id = residue.Id,
                    Tick = residue.Tick,
                    Stage = residue.Stage,
                    IsEvaporation = residue.IsEvaporation,
                    Material = ToWire(residue.Material)
                }),
                Holds = Map(snapshot.Holds, static hold => new HoldDataType
                {
                    Id = hold.Id,
                    Tick = hold.Tick,
                    Stage = hold.Stage,
                    Reason = hold.Reason,
                    Disposition = (DispositionEnum)hold.Disposition,
                    DisposedBy = hold.DisposedBy,
                    DispositionRevision = hold.DispositionRevision
                }),
                History = Map(snapshot.History, ToWire),
                Participants = Map(snapshot.Participants, static participant => new ParticipantDataType
                {
                    Process = (ProcessEnum)participant.Process,
                    Identity = participant.Identity,
                    ConnectionId = participant.ConnectionId,
                    Initialized = participant.Initialized,
                    LeaseExpiresUtc = participant.LeaseExpiresUtc,
                    ObservedRevision = participant.ObservedRevision
                }),
                Energy = ToWire(snapshot.Energy)
            };
        }

        /// <summary>
        /// Detaches a received wire snapshot into immutable application records; never advances simulation.
        /// </summary>
        public static CommittedSnapshot FromWire(SnapshotDataType snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.SchemaVersion != 2)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "Unsupported recycling snapshot schema.");
            }
            return new CommittedSnapshot
            {
                SchemaVersion = snapshot.SchemaVersion,
                RunId = RequiredText(snapshot.RunId, nameof(snapshot.RunId)),
                Scenario = RequiredText(snapshot.Scenario, nameof(snapshot.Scenario)),
                ConfigurationId = RequiredText(snapshot.ConfigurationId, nameof(snapshot.ConfigurationId)),
                Revision = snapshot.Revision,
                Tick = snapshot.Tick,
                LogicalSeconds = snapshot.LogicalSeconds,
                SourceTimestampUtc = snapshot.SourceTimestampUtc.ToDateTime(),
                Lifecycle = (RunLifecycle)snapshot.Lifecycle,
                BlockingReason = snapshot.BlockingReason ?? string.Empty,
                RandomState = snapshot.RandomState,
                NextId = snapshot.NextId,
                SourceBatches = snapshot.SourceBatches,
                SourceMass = FromWire(snapshot.SourceMass),
                Sources = Map(snapshot.Sources, FromWire),
                Stages = Map(snapshot.Stages, FromWire),
                Parcels = Map(snapshot.Parcels, FromWire),
                Packages = Map(snapshot.Packages, static package =>
                    new MaterialPackage(package.Id, package.Tick, FromWire(package.Material))),
                Residues = Map(snapshot.Residues, static residue =>
                    new MaterialResidue(
                        residue.Id, residue.Tick, residue.Stage, residue.IsEvaporation, FromWire(residue.Material))),
                Holds = Map(snapshot.Holds, static hold =>
                    new QualityHold(
                        hold.Id, hold.Tick, hold.Stage, RequiredText(hold.Reason, nameof(hold.Reason)),
                        (HoldDisposition)hold.Disposition, hold.DisposedBy ?? string.Empty, hold.DispositionRevision)),
                History = Map(snapshot.History, FromWire),
                Participants = Map(snapshot.Participants, static participant =>
                    new ProcessParticipant(
                        (RecyclingProcess)participant.Process,
                        RequiredText(participant.Identity, nameof(participant.Identity)),
                        participant.ConnectionId ?? string.Empty,
                        participant.Initialized, participant.LeaseExpiresUtc.ToDateTime(),
                        participant.ObservedRevision))
            };
        }

        /// <summary>
        /// Converts a command without introducing an actor field into the wire contract.
        /// </summary>
        public static CommandDataType ToWire(RecyclingCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            return new CommandDataType
            {
                RequestId = command.RequestId,
                LineId = command.LineId,
                RunId = command.RunId,
                ExpectedRevision = command.ExpectedRevision,
                Kind = (CommandEnum)command.Kind,
                Process = (ProcessEnum)command.Process,
                HoldId = command.HoldId,
                Disposition = (DispositionEnum)command.Disposition,
                Steps = command.Steps
            };
        }

        /// <summary>
        /// Converts a typed method input into the bounded coordinator contract.
        /// </summary>
        public static RecyclingCommand FromWire(CommandDataType command)
        {
            ArgumentNullException.ThrowIfNull(command);
            return new RecyclingCommand
            {
                RequestId = command.RequestId ?? string.Empty,
                LineId = command.LineId ?? string.Empty,
                RunId = command.RunId ?? string.Empty,
                ExpectedRevision = command.ExpectedRevision,
                Kind = (RecyclingCommandKind)command.Kind,
                Process = (RecyclingProcess)command.Process,
                HoldId = command.HoldId,
                Disposition = (HoldDisposition)command.Disposition,
                Steps = command.Steps
            };
        }

        /// <summary>
        /// Converts the original durable outcome into the generated method result structure.
        /// </summary>
        public static CommandOutcomeDataType ToWire(RecyclingCommandOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            return new CommandOutcomeDataType
            {
                RequestId = outcome.RequestId,
                RunId = outcome.RunId,
                Revision = outcome.Revision,
                Tick = outcome.Tick,
                Status = new StatusCode(outcome.Status),
                Message = outcome.Message
            };
        }

        /// <summary>
        /// Converts an acknowledged outcome into immutable application data.
        /// </summary>
        public static RecyclingCommandOutcome FromWire(CommandOutcomeDataType outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            return new RecyclingCommandOutcome(
                RequiredText(outcome.RequestId, nameof(outcome.RequestId)),
                RequiredText(outcome.RunId, nameof(outcome.RunId)), outcome.Revision, outcome.Tick,
                outcome.Status.Code, outcome.Message ?? string.Empty);
        }

        /// <summary>
        /// Converts a bounded genealogy page.
        /// </summary>
        public static ArrayOf<ExecutionDataType> ToWire(ArrayOf<MaterialExecution> history)
        {
            return Map(history, ToWire);
        }

        /// <summary>
        /// Converts a bounded received genealogy page.
        /// </summary>
        public static ArrayOf<MaterialExecution> FromWire(ArrayOf<ExecutionDataType> history)
        {
            return Map(history, FromWire);
        }

        private static MassDataType ToWire(MaterialMass mass)
        {
            return new MassDataType
            {
                NaturalPolymer = mass.NaturalPolymer,
                ColoredPolymer = mass.ColoredPolymer,
                DensePolymer = mass.DensePolymer,
                Labels = mass.Labels,
                Dirt = mass.Dirt,
                Water = mass.Water
            };
        }

        private static EnergyIntensityDataType ToWire(RecyclingEnergyIntensity energy)
        {
            return new EnergyIntensityDataType
            {
                BaselineId = energy.BaselineId,
                StartTick = energy.StartTick,
                EndTick = energy.EndTick,
                ElectricalJoules = energy.ElectricalJoules,
                ThermalJoules = energy.ThermalJoules,
                PackagedDryMilligrams = energy.PackagedDryMilligrams,
                IsApplicable = energy.IsApplicable,
                ElectricalJoulesPerKilogram = energy.ElectricalJoulesPerKilogram,
                ThermalJoulesPerKilogram = energy.ThermalJoulesPerKilogram
            };
        }

        private static MaterialMass FromWire(MassDataType mass)
        {
            return new MaterialMass(
                mass.NaturalPolymer, mass.ColoredPolymer, mass.DensePolymer, mass.Labels, mass.Dirt, mass.Water);
        }

        private static ContributionDataType ToWire(SourceContribution source)
        {
            return new ContributionDataType
            {
                SourceId = source.SourceId,
                Mass = ToWire(source.Mass),
                SourceZone = source.SourceZone
            };
        }

        private static SourceContribution FromWire(ContributionDataType source)
        {
            return new SourceContribution(
                RequiredText(source.SourceId, nameof(source.SourceId)), FromWire(source.Mass),
                RequiredText(source.SourceZone, nameof(source.SourceZone)));
        }

        private static ParcelDataType ToWire(MaterialParcel parcel)
        {
            return new ParcelDataType
            {
                Id = parcel.Id,
                Stage = parcel.Stage,
                EnteredTick = parcel.EnteredTick,
                ResidenceRemaining = parcel.ResidenceRemaining,
                Form = (FormEnum)parcel.Form,
                Contributions = Map(parcel.Contributions, ToWire),
                HoldIds = parcel.HoldIds.Span.ToArray().ToArrayOf()
            };
        }

        private static MaterialParcel FromWire(ParcelDataType parcel)
        {
            return new MaterialParcel(
                parcel.Id, parcel.Stage, parcel.EnteredTick, parcel.ResidenceRemaining, (MaterialForm)parcel.Form,
                Map(parcel.Contributions, FromWire), parcel.HoldIds.Span.ToArray().ToArrayOf());
        }

        private static StageDataType ToWire(StageSnapshot stage)
        {
            return new StageDataType
            {
                Index = stage.Index,
                Process = (ProcessEnum)stage.Process,
                Name = stage.Name,
                State = (EquipmentEnum)stage.State,
                Enabled = stage.Enabled,
                Inventory = ToWire(stage.Inventory),
                Received = ToWire(stage.Received),
                Transferred = ToWire(stage.Transferred),
                Additions = ToWire(stage.Additions),
                Rejected = ToWire(stage.Rejected),
                Evaporated = ToWire(stage.Evaporated),
                ElectricalJoules = stage.ElectricalJoules,
                ThermalJoules = stage.ThermalJoules,
                ElectricalDemandWatts = stage.ElectricalDemandWatts,
                ThermalDemandWatts = stage.ThermalDemandWatts,
                Executions = stage.Executions,
                JamAlarm = stage.JamAlarm,
                JamEvents = stage.JamEvents,
                StrandBreakAlarm = stage.StrandBreakAlarm,
                StrandBreakEvents = stage.StrandBreakEvents,
                PhAvailable = stage.PhAvailable,
                Ph = stage.Ph,
                PhSourceTimestampUtc = stage.PhSourceTimestampUtc,
                ColorAvailable = stage.ColorAvailable,
                Color = new SyntheticRgbDataType
                {
                    Red = stage.Color.Red,
                    Green = stage.Color.Green,
                    Blue = stage.Color.Blue
                },
                ColorMaterialId = stage.ColorMaterialId,
                ColorNaturalPolymerMilligrams = stage.ColorNaturalPolymerMilligrams,
                ColorColoredPolymerMilligrams = stage.ColorColoredPolymerMilligrams,
                ColorSourceTimestampUtc = stage.ColorSourceTimestampUtc
            };
        }

        private static StageSnapshot FromWire(StageDataType stage)
        {
            return new StageSnapshot
            {
                Index = stage.Index,
                Process = (RecyclingProcess)stage.Process,
                Name = RequiredText(stage.Name, nameof(stage.Name)),
                State = (EquipmentState)stage.State,
                Enabled = stage.Enabled,
                Inventory = FromWire(stage.Inventory),
                Received = FromWire(stage.Received),
                Transferred = FromWire(stage.Transferred),
                Additions = FromWire(stage.Additions),
                Rejected = FromWire(stage.Rejected),
                Evaporated = FromWire(stage.Evaporated),
                ElectricalJoules = stage.ElectricalJoules,
                ThermalJoules = stage.ThermalJoules,
                ElectricalDemandWatts = stage.ElectricalDemandWatts,
                ThermalDemandWatts = stage.ThermalDemandWatts,
                Executions = stage.Executions,
                JamAlarm = stage.JamAlarm,
                JamEvents = stage.JamEvents,
                StrandBreakAlarm = stage.StrandBreakAlarm,
                StrandBreakEvents = stage.StrandBreakEvents,
                PhAvailable = stage.PhAvailable,
                Ph = stage.Ph,
                PhSourceTimestampUtc = stage.PhSourceTimestampUtc.ToDateTime(),
                ColorAvailable = stage.ColorAvailable,
                Color = new SyntheticRgb(stage.Color.Red, stage.Color.Green, stage.Color.Blue),
                ColorMaterialId = stage.ColorMaterialId,
                ColorNaturalPolymerMilligrams = stage.ColorNaturalPolymerMilligrams,
                ColorColoredPolymerMilligrams = stage.ColorColoredPolymerMilligrams,
                ColorSourceTimestampUtc = stage.ColorSourceTimestampUtc.ToDateTime()
            };
        }

        private static ExecutionDataType ToWire(MaterialExecution execution)
        {
            return new ExecutionDataType
            {
                Id = execution.Id,
                Tick = execution.Tick,
                Stage = execution.Stage,
                Inputs = execution.Inputs.Span.ToArray().ToArrayOf(),
                Outputs = execution.Outputs.Span.ToArray().ToArrayOf(),
                Input = ToWire(execution.Input),
                Addition = ToWire(execution.Addition),
                Output = ToWire(execution.Output),
                Reject = ToWire(execution.Reject),
                Evaporation = ToWire(execution.Evaporation)
            };
        }

        private static MaterialExecution FromWire(ExecutionDataType execution)
        {
            return new MaterialExecution(
                execution.Id, execution.Tick, execution.Stage,
                execution.Inputs.Span.ToArray().ToArrayOf(), execution.Outputs.Span.ToArray().ToArrayOf(),
                FromWire(execution.Input), FromWire(execution.Addition), FromWire(execution.Output),
                FromWire(execution.Reject), FromWire(execution.Evaporation));
        }

        private static string RequiredText(string? value, string field)
        {
            return value ?? throw new ServiceResultException(
                StatusCodes.BadDecodingError, $"Missing required recycling field: {field}.");
        }

        private static ArrayOf<TResult> Map<T, TResult>(ArrayOf<T> values, Func<T, TResult> convert)
        {
            var result = new TResult[values.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = convert(values[i]);
            }
            return result.ToArrayOf();
        }
    }
}
