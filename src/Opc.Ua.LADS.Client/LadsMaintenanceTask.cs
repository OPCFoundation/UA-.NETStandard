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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A maintenance task of a device's <c>Maintenance</c> set.
    /// </summary>
    /// <param name="NodeId">The NodeId of the task.</param>
    /// <param name="Name">The browse name of the task.</param>
    public sealed record LadsMaintenanceTask(NodeId NodeId, string Name)
    {
        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// Gets the state of the AMB <c>MaintenanceState</c> state machine.
        /// </summary>
        public LadsStateSnapshot? State { get; init; }

        /// <summary>
        /// Gets the time of the last execution.
        /// </summary>
        public DateTimeUtc LastExecutionDate { get; init; }

        /// <summary>
        /// Gets the planned date.
        /// </summary>
        public DateTimeUtc PlannedDate { get; init; }

        /// <summary>
        /// Gets the recurrence period.
        /// </summary>
        public TimeSpan? RecurrencePeriod { get; init; }

        /// <summary>
        /// Gets the estimated downtime.
        /// </summary>
        public TimeSpan? EstimatedDowntime { get; init; }

        /// <summary>
        /// Gets the operating time of the last execution.
        /// </summary>
        public TimeSpan? LastOperatingTime { get; init; }

        /// <summary>
        /// Gets the operating time of the next execution.
        /// </summary>
        public TimeSpan? NextOperatingTime { get; init; }

        /// <summary>
        /// Gets the operating cycles of the last execution.
        /// </summary>
        public uint? LastOperatingCycles { get; init; }

        /// <summary>
        /// Gets the operating cycles of the next execution.
        /// </summary>
        public uint? NextOperatingCycles { get; init; }
    }
}
