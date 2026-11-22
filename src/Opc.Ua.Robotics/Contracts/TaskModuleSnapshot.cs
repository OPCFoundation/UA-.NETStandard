// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Read-model snapshot of a TaskModuleType instance.
    /// </summary>
    public sealed record TaskModuleSnapshot
    {
        /// <summary>
        /// The task-module instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The task-module name.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// The task-module version.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// Whether another task module references this module.
        /// </summary>
        public DataValue IsReferenced { get; init; } = DataValue.Null;

        /// <summary>
        /// The IsReferenced variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId IsReferencedId { get; init; } = NodeId.Null;
    }
}
