// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// An OPC 40010 relationship between two resolved instance NodeIds.
    /// </summary>
    public sealed record RoboticsRelationshipEntry
    {
        /// <summary>
        /// The source instance NodeId.
        /// </summary>
        public NodeId SourceId { get; init; } = NodeId.Null;

        /// <summary>
        /// The resolved ReferenceType NodeId.
        /// </summary>
        public NodeId ReferenceTypeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The target instance NodeId.
        /// </summary>
        public NodeId TargetId { get; init; } = NodeId.Null;

        /// <summary>
        /// Whether the entry was observed in the inverse direction.
        /// </summary>
        public bool IsInverse { get; init; }
    }
}
