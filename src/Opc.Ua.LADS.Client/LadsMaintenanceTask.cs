// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
