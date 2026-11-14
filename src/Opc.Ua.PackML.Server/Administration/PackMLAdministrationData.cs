/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Equipment-owned administration data. Applications can adapt their existing
    /// alarm and condition infrastructure to these PackML structures without a second alarm engine.
    /// </summary>
    public sealed record PackMLAdministrationData
    {
        /// <summary>
        /// Gets the local unit parameters.
        /// </summary>
        public ArrayOf<PackMLDescriptorDataType> Parameters { get; init; }

        /// <summary>
        /// Gets currently active alarms, not historical events.
        /// </summary>
        public ArrayOf<PackMLAlarmDataType> Alarms { get; init; }

        /// <summary>
        /// Gets equipment-supplied alarm history in the standard PackML representation.
        /// </summary>
        public ArrayOf<PackMLAlarmDataType> AlarmHistory { get; init; }

        /// <summary>
        /// Gets warnings that do not stop the machine.
        /// </summary>
        public ArrayOf<PackMLAlarmDataType> Warnings { get; init; }

        /// <summary>
        /// Gets the first-out stop reason, or null when no reason is available.
        /// </summary>
        public PackMLAlarmDataType? StopReason { get; init; }

        /// <summary>
        /// Gets material consumed counts. AccCount remains equipment-owned and non-resetting.
        /// </summary>
        public ArrayOf<PackMLCountDataType> ConsumedCounts { get; init; }

        /// <summary>
        /// Gets processed counts. Index zero represents the primary production stream.
        /// </summary>
        public ArrayOf<PackMLCountDataType> ProcessedCounts { get; init; }

        /// <summary>
        /// Gets defective counts. Index zero represents the primary production stream.
        /// </summary>
        public ArrayOf<PackMLCountDataType> DefectiveCounts { get; init; }

        /// <summary>
        /// Gets installed design speed in primary packages per minute.
        /// </summary>
        public float MachineDesignSpeed { get; init; }

        /// <summary>
        /// Gets acquisition quality for this coherent data sample.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.BadWaitingForInitialData;

        /// <summary>
        /// Gets the equipment acquisition timestamp, preserved without substitution.
        /// </summary>
        public DateTimeUtc SourceTimestamp { get; init; }
    }

    /// <summary>
    /// Configured capacities, independent of how many alarms or counts are currently populated.
    /// </summary>
    public sealed record PackMLAdministrationExtents
    {
        /// <summary>
        /// Gets maximum local parameters.
        /// </summary>
        public int Parameters { get; init; }

        /// <summary>
        /// Gets maximum available alarm elements.
        /// </summary>
        public int Alarms { get; init; }

        /// <summary>
        /// Gets maximum archived alarm elements.
        /// </summary>
        public int AlarmHistory { get; init; }

        /// <summary>
        /// Gets maximum available warning elements.
        /// </summary>
        public int Warnings { get; init; }

        /// <summary>
        /// Gets maximum possible stop reasons, not the scalar StopReason population.
        /// </summary>
        public int StopReasons { get; init; }

        /// <summary>
        /// Gets maximum consumed material count elements.
        /// </summary>
        public int ConsumedCounts { get; init; }

        /// <summary>
        /// Gets maximum processed product count elements.
        /// </summary>
        public int ProcessedCounts { get; init; }

        /// <summary>
        /// Gets maximum defective product count elements.
        /// </summary>
        public int DefectiveCounts { get; init; }
    }

    /// <summary>
    /// A typed persistence boundary for an application-supplied store. No active
    /// machine state or running interval is persisted or replayed on restoration.
    /// </summary>
    public sealed record PackMLAdministrationSnapshot
    {
        /// <summary>
        /// Gets the last current-mode interval lengths, indexed by concrete mode value.
        /// </summary>
        public ArrayOf<int> ModeCurrentTime { get; init; }

        /// <summary>
        /// Gets cumulative mode seconds since the last administrative reset.
        /// </summary>
        public ArrayOf<int> ModeCumulativeTime { get; init; }

        /// <summary>
        /// Gets last current-state interval lengths, indexed by mode then state number.
        /// </summary>
        public MatrixOf<int> StateCurrentTime { get; init; }

        /// <summary>
        /// Gets cumulative state seconds, indexed by mode then state number.
        /// </summary>
        public MatrixOf<int> StateCumulativeTime { get; init; }

        /// <summary>
        /// Gets elapsed runtime seconds since the last administrative reset.
        /// </summary>
        public int AccTimeSinceReset { get; init; }
    }

    /// <summary>
    /// Application-owned durable timer storage. The hosting application selects a
    /// storage identity and lifecycle; this contract does not persist or replay machine commands.
    /// </summary>
    public interface IPackMLAdministrationStore
    {
        /// <summary>
        /// Loads the last successful counter snapshot, or null when none exists.
        /// The application restores it before recording the fresh initial machine state.
        /// </summary>
        ValueTask<PackMLAdministrationSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replaces the stored timer snapshot. Production counters and
        /// alarm persistence remain the equipment provider's responsibility.
        /// </summary>
        ValueTask SaveAsync(
            PackMLAdministrationSnapshot snapshot,
            CancellationToken cancellationToken = default);
    }
}
