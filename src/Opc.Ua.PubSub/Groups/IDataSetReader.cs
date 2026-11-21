// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="DataSetReaderDataType"/>: the
    /// writer the reader binds to, the sink it writes decoded
    /// values into, and the receive-timeout governing the state
    /// machine.
    /// </summary>
    /// <remarks>
    /// Implements the DataSetReader contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9">
    /// Part 14 §6.2.9 DataSetReader</see>.
    /// </remarks>
    public interface IDataSetReader
    {
        /// <summary>
        /// DataSetWriterId — the publisher writer this reader matches
        /// (the reader does NOT have its own writer id).
        /// </summary>
        ushort DataSetWriterId { get; }

        /// <summary>
        /// Reader name (matches
        /// <see cref="DataSetReaderDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Sink that consumes decoded DataSet fields.
        /// </summary>
        ISubscribedDataSetSink Sink { get; }

        /// <summary>
        /// Receive timeout — if no DataSetMessage arrives within
        /// this interval the reader transitions to <c>Error</c> and
        /// emits the <c>MessageReceiveTimeouts</c> diagnostic.
        /// </summary>
        TimeSpan MessageReceiveTimeout { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        DataSetReaderDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the ReaderGroup cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
