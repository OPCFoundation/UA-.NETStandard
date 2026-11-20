// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Snapshot of an emergency-stop or protective-stop function.
    /// </summary>
    public sealed record SafetyFunctionSnapshot
    {
        /// <summary>
        /// The function instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The manufacturer-specific function name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// The Active value, including status and timestamps.
        /// </summary>
        public DataValue Active { get; init; } = DataValue.Null;

        /// <summary>
        /// The Active variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId ActiveId { get; init; } = NodeId.Null;

        /// <summary>
        /// The optional Enabled value, including status and timestamps.
        /// </summary>
        public DataValue Enabled { get; init; } = DataValue.Null;

        /// <summary>
        /// The Enabled variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId EnabledId { get; init; } = NodeId.Null;
    }
}
