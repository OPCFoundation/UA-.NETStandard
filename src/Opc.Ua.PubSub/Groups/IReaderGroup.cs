// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="ReaderGroupDataType"/>: the
    /// set of <see cref="IDataSetReader"/> instances grouped under
    /// a common security configuration and the state machine
    /// driving the cascade to its readers.
    /// </summary>
    /// <remarks>
    /// Implements the ReaderGroup contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.8">
    /// Part 14 §6.2.8 ReaderGroup</see>.
    /// </remarks>
    public interface IReaderGroup
    {
        /// <summary>
        /// Group name (matches the configured
        /// <see cref="ReaderGroupDataType"/> <c>Name</c> field).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Snapshot of readers in this group.
        /// </summary>
        ArrayOf<IDataSetReader> DataSetReaders { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        ReaderGroupDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the PubSubConnection
        /// cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
