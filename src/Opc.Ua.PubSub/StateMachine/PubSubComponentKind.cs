// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.StateMachine
{
    /// <summary>
    /// Classifies the kind of PubSub component a <see cref="PubSubStateMachine"/>
    /// tracks. Used for diagnostics labelling and to determine which counter
    /// classification a transition increments.
    /// </summary>
    /// <remarks>
    /// Implements <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.10.1">
    /// Part 14 §9.1.10.1 PubSubStatusType</see> — every Object that owns a
    /// <c>PubSubStatusType</c> in the address space (PublishSubscribe,
    /// PubSubConnection, PubSubGroup, DataSetWriter, DataSetReader) has a
    /// matching value here.
    /// </remarks>
    public enum PubSubComponentKind
    {
        /// <summary>
        /// The root <c>PublishSubscribe</c> object that owns all connections.
        /// </summary>
        Application,

        /// <summary>
        /// A single <c>PubSubConnection</c> binding a publisher and/or subscriber
        /// to a transport profile and address.
        /// </summary>
        Connection,

        /// <summary>
        /// A <c>WriterGroup</c> grouping <see cref="DataSetWriter"/>s under one
        /// publishing schedule and message mapping.
        /// </summary>
        WriterGroup,

        /// <summary>
        /// A <c>DataSetWriter</c> emitting DataSetMessages for one PublishedDataSet.
        /// </summary>
        DataSetWriter,

        /// <summary>
        /// A <c>ReaderGroup</c> grouping <see cref="DataSetReader"/>s under one
        /// transport subscription and message mapping.
        /// </summary>
        ReaderGroup,

        /// <summary>
        /// A <c>DataSetReader</c> filtering and decoding inbound DataSetMessages.
        /// </summary>
        DataSetReader
    }
}
