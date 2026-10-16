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

namespace Opc.Ua.MachineTool
{
    public sealed partial record MachineToolSnapshot
    {
        /// <summary>
        /// Gets the retained production observations, shared with any modern job-management adapter.
        /// </summary>
        public MachineToolProductionObservation Production { get; init; } = new();
    }

    public sealed partial record MachineToolProgram
    {
        /// <summary>
        /// Gets the stable application slot key; an empty key uses the program name.
        /// This key is not an additional OPC UA property.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the containing job identifier, required on ActiveProgram when a production plan is populated.
        /// </summary>
        public string JobIdentifier { get; init; } = string.Empty;
    }

    /// <summary>
    /// A complete ordered retained-production observation; it contains no execution commands.
    /// </summary>
    public sealed record MachineToolProductionObservation
    {
        /// <summary>
        /// Gets jobs in their intended execution order, without silently truncating the plan.
        /// </summary>
        public ArrayOf<MachineToolProductionJob> Jobs { get; init; }

        /// <summary>
        /// Gets optional retained lifetime statistics.
        /// </summary>
        public MachineToolProductionStatistics? Statistics { get; init; }
    }

    /// <summary>
    /// One job, its ordered program inventory and the part sets of its current run.
    /// </summary>
    public sealed record MachineToolProductionJob
    {
        /// <summary>
        /// Gets the stable application slot key; an empty key uses Identifier.
        /// Static plans can reuse a slot for a different job only through a terminal-to-initializing transition.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the unique job identifier.
        /// </summary>
        public string Identifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the external customer order identifier.
        /// </summary>
        public string CustomerOrderIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the internal production order identifier.
        /// </summary>
        public string OrderIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the observed state.
        /// </summary>
        public MachineToolProductionPhase Phase { get; init; }

        /// <summary>
        /// Gets the contiguous transition sequence, including repeated running transitions.
        /// </summary>
        public ulong TransitionSequence { get; init; }

        /// <summary>
        /// Gets the number of normally completed runs.
        /// </summary>
        public uint RunsCompleted { get; init; }

        /// <summary>
        /// Gets the planned number of runs, meaningful only when RunsPlannedIsValid is true.
        /// </summary>
        public uint RunsPlanned { get; init; }

        /// <summary>
        /// Gets whether the machine knows the number of planned runs.
        /// </summary>
        public bool RunsPlannedIsValid { get; init; }

        /// <summary>
        /// Gets the cumulative completed-part counter, including aborted parts.
        /// </summary>
        public uint PartsCompleted { get; init; }

        /// <summary>
        /// Gets the cumulative good-part counter, including CapabilityUnavailable and WillNotBeMeasured.
        /// </summary>
        public uint PartsGood { get; init; }

        /// <summary>
        /// Gets the completed parts from earlier runs, before the part nodes representing the current run.
        /// This observation-only baseline reconciles resettable per-run nodes with cumulative job counters.
        /// </summary>
        public uint PartsCompletedBeforeCurrentRun { get; init; }

        /// <summary>
        /// Gets the good parts from earlier runs; later machines must not revise this baseline retrospectively.
        /// </summary>
        public uint PartsGoodBeforeCurrentRun { get; init; }

        /// <summary>
        /// Gets the programs in call order; every job must contain at least one.
        /// </summary>
        public ArrayOf<MachineToolProgram> Programs { get; init; }

        /// <summary>
        /// Gets the part sets representing this job's current run.
        /// </summary>
        public ArrayOf<MachineToolProductionPartSet> PartSets { get; init; }

        /// <summary>
        /// Gets all known interruptions, including cleared conditions that remain available for refresh.
        /// </summary>
        public ArrayOf<MachineToolProductionInterruption> Interruptions { get; init; }

        /// <summary>
        /// Gets the acquisition quality and timestamp.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// A group of parts in the current production run.
    /// </summary>
    public sealed record MachineToolProductionPartSet
    {
        /// <summary>
        /// Gets the stable application-owned key of the set.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the name describing the group.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets whether the group can contain different kinds of part.
        /// </summary>
        public bool ContainsMixedParts { get; init; }

        /// <summary>
        /// Gets the intended number of parts in one run.
        /// </summary>
        public uint PartsPlannedPerRun { get; init; }

        /// <summary>
        /// Gets the number of completed parts in the current run, regardless of quality.
        /// </summary>
        public uint PartsCompletedPerRun { get; init; }

        /// <summary>
        /// Gets the individual parts; an empty array selects aggregate-only monitoring.
        /// </summary>
        public ArrayOf<MachineToolProductionPart> Parts { get; init; }

        /// <summary>
        /// Gets the acquisition quality and timestamp.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// A part observation, whose slot can be reset for the next run without losing its generated subtype.
    /// </summary>
    public sealed record MachineToolProductionPart
    {
        /// <summary>
        /// Gets the stable application-owned part slot.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets the part's display name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the optional unique identifier of the physical workpiece.
        /// </summary>
        public string Identifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the external customer order identifier.
        /// </summary>
        public string CustomerOrderIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the quality for this machine's process step.
        /// </summary>
        public PartQuality Quality { get; init; } = PartQuality.NotYetMeasured;

        /// <summary>
        /// Gets the observed process irregularity.
        /// </summary>
        public ProcessIrregularity Irregularity { get; init; } = ProcessIrregularity.NotYetDetermined;

        /// <summary>
        /// Gets the processing state.
        /// </summary>
        public MachineToolProductionPhase Phase { get; init; }

        /// <summary>
        /// Gets the contiguous transition sequence for this slot.
        /// </summary>
        public ulong TransitionSequence { get; init; }

        /// <summary>
        /// Gets the acquisition quality and timestamp.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// A known reason for interruption, represented by a standard ConditionClass rather than a control command.
    /// </summary>
    public sealed record MachineToolProductionInterruption
    {
        /// <summary>
        /// Gets the stable condition identifier within its job.
        /// </summary>
        public string Identifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the most specific standard or registered application ConditionClass subtype.
        /// </summary>
        public NodeId ConditionClassId { get; init; }

        /// <summary>
        /// Gets whether the reason still prevents the interrupted job from resuming.
        /// </summary>
        public bool IsActive { get; init; }

        /// <summary>
        /// Gets whether this interruption normally ends automatically.
        /// </summary>
        public bool IsAutomated { get; init; }

        /// <summary>
        /// Gets the human-readable reason.
        /// </summary>
        public LocalizedText Message { get; init; }

        /// <summary>
        /// Gets the OPC UA severity, between 1 and 1000.
        /// </summary>
        public ushort Severity { get; init; } = 100;

        /// <summary>
        /// Gets the acquisition quality and timestamp.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// The retained UInt32 statistics, distinct from the newer UInt64 operation counter.
    /// </summary>
    /// <param name="PartsProducedInLifetime">The lifetime produced-part count.</param>
    /// <param name="Sample">The acquisition quality and timestamp.</param>
    public sealed record MachineToolProductionStatistics(
        uint PartsProducedInLifetime, MachineToolSampleInfo Sample);
}
