// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The operation counters of a device, functional unit or component.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>OperationCounters</c> object.</param>
    public sealed record LadsOperationCounters(NodeId NodeId)
    {
        /// <summary>
        /// Gets the duration the item was powered.
        /// </summary>
        public TimeSpan? PowerOnDuration { get; init; }

        /// <summary>
        /// Gets the duration the item was operating.
        /// </summary>
        public TimeSpan? OperationDuration { get; init; }

        /// <summary>
        /// Gets the number of operation cycles.
        /// </summary>
        public ulong? OperationCycleCounter { get; init; }

        /// <summary>
        /// Gets the LADS <c>LifeTime</c>, when present.
        /// </summary>
        public Variant LifeTime { get; init; }
    }
}
