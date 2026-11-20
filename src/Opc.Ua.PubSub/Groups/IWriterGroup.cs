// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="WriterGroupDataType"/>: the
    /// publishing cadence, the set of writers it owns, and the
    /// state machine driving the cascade to children.
    /// </summary>
    /// <remarks>
    /// Implements the WriterGroup contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6">
    /// Part 14 §6.2.6 WriterGroup</see>.
    /// </remarks>
    public interface IWriterGroup
    {
        /// <summary>
        /// WriterGroupId — unique within the parent PubSubConnection
        /// and carried in every NetworkMessage header.
        /// </summary>
        ushort WriterGroupId { get; }

        /// <summary>
        /// Group name (matches the configured
        /// <see cref="WriterGroupDataType"/> <c>Name</c> field).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Snapshot of writers in this group.
        /// </summary>
        ArrayOf<IDataSetWriter> DataSetWriters { get; }

        /// <summary>
        /// Publishing schedule (period, keep-alive, offsets).
        /// </summary>
        PubSubSchedule Schedule { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        WriterGroupDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the PubSubConnection
        /// cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
