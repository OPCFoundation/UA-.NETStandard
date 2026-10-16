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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Acquisition metadata for a coherent set of values supplied by an equipment provider.
    /// </summary>
    /// <param name="SourceTimestamp">The equipment acquisition time, not the time a client reads it.</param>
    /// <param name="StatusCode">The quality reported by the equipment.</param>
    public readonly record struct MachineToolSampleInfo(DateTimeUtc SourceTimestamp, StatusCode StatusCode)
    {
        /// <summary>
        /// Gets metadata for values the equipment has not supplied.
        /// </summary>
        public static MachineToolSampleInfo Unavailable => new(default, StatusCodes.BadWaitingForInitialData);
    }

    /// <summary>
    /// Monitoring states of the retained OPC 40501-1 production model; these are not execution commands.
    /// </summary>
    public enum MachineToolProductionPhase
    {
        /// <summary>
        /// The production element is being prepared.
        /// </summary>
        Initializing,

        /// <summary>
        /// The production element is executing.
        /// </summary>
        Running,

        /// <summary>
        /// Execution is interrupted and can resume.
        /// </summary>
        Interrupted,

        /// <summary>
        /// Execution completed normally.
        /// </summary>
        Ended,

        /// <summary>
        /// Execution was aborted.
        /// </summary>
        Aborted
    }

    /// <summary>
    /// One coherent equipment snapshot. Defaults explicitly indicate unavailable measurements.
    /// </summary>
    public sealed partial record MachineToolSnapshot
    {
        /// <summary>
        /// Gets the monotonically increasing provider revision used to reject stale concurrent reads.
        /// </summary>
        public ulong Revision { get; init; }

        /// <summary>
        /// Gets the mandatory overall machine-operation observation.
        /// </summary>
        public MachineToolOperation Operation { get; init; } = new();

        /// <summary>
        /// Gets the mandatory retained legacy active-program observation.
        /// </summary>
        public MachineToolProgram ActiveProgram { get; init; } = new();

        /// <summary>
        /// Gets the complete ordinary and combined channel inventory.
        /// </summary>
        public ArrayOf<MachineToolChannel> Channels { get; init; }

        /// <summary>
        /// Gets the installed software components.
        /// </summary>
        public ArrayOf<MachineToolSoftware> Software { get; init; }

        /// <summary>
        /// Gets the machine's spindles.
        /// </summary>
        public ArrayOf<MachineToolSpindle> Spindles { get; init; }

        /// <summary>
        /// Gets the machine's laser working units.
        /// </summary>
        public ArrayOf<MachineToolLaser> Lasers { get; init; }

        /// <summary>
        /// Gets the machine's EDM generators.
        /// </summary>
        public ArrayOf<MachineToolEdmGenerator> EdmGenerators { get; init; }

        /// <summary>
        /// Gets the machine's segmented physical stacklights.
        /// </summary>
        public ArrayOf<MachineToolStacklight> Stacklights { get; init; }
    }

    /// <summary>
    /// Observed overall machine operation, never an instruction to move or actuate the machine.
    /// </summary>
    public sealed record MachineToolOperation
    {
        /// <summary>
        /// Gets the reported machine operation mode.
        /// </summary>
        public MachineOperationMode Mode { get; init; }

        /// <summary>
        /// Gets the optional overall feed override percentage.
        /// </summary>
        public double? FeedOverride { get; init; }

        /// <summary>
        /// Gets the optional warm-up observation.
        /// </summary>
        public bool? IsWarmUp { get; init; }

        /// <summary>
        /// Gets the total lifetime powered-on duration in milliseconds.
        /// </summary>
        public double? PowerOnDurationMilliseconds { get; init; }

        /// <summary>
        /// Gets the lifetime produced-parts counter, without implying part quality.
        /// </summary>
        public ulong? PartsProducedInLifetime { get; init; }

        /// <summary>
        /// Gets the nested Machinery item state.
        /// </summary>
        public MachineToolItemState? ItemState { get; init; }

        /// <summary>
        /// Gets the nested Machinery operation mode.
        /// </summary>
        public MachineToolMachineryMode? MachineryMode { get; init; }

        /// <summary>
        /// Gets the maintenance substate when that mode is selected.
        /// </summary>
        public MachineToolMaintenanceMode? MaintenanceMode { get; init; }

        /// <summary>
        /// Gets responsibility for the current activity.
        /// </summary>
        public MachineToolObligation? Obligation { get; init; }

        /// <summary>
        /// Gets the acquisition quality and timestamp.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// A program observation for the active program or an ordered job program.
    /// </summary>
    public sealed partial record MachineToolProgram
    {
        /// <summary>
        /// Gets the program name; an empty name represents no selected program.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the reported production phase.
        /// </summary>
        public MachineToolProductionPhase Phase { get; init; }

        /// <summary>
        /// Gets the transition sequence, including repeated Running-to-Running transitions.
        /// </summary>
        public ulong TransitionSequence { get; init; }

        /// <summary>
        /// Gets the quality and acquisition time of the program observation.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// Supplies coherent equipment observations. Implementations own device communication, not the OPC UA protocol.
    /// </summary>
    public interface IMachineToolProvider
    {
        /// <summary>
        /// Acquires the latest snapshot without commanding hardware or fabricating successful measurements.
        /// </summary>
        /// <param name="cancellationToken">Cancels acquisition without changing published state.</param>
        ValueTask<MachineToolSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    }
}
