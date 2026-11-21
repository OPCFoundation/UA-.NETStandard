// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="DataSetWriterDataType"/>
    /// inside a writer group: the writer's identity, the
    /// <see cref="IPublishedDataSet"/> it samples, encoding
    /// configuration, and the state machine that participates in
    /// the WriterGroup → Writer cascade.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side per-writer surface described
    /// in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 DataSetWriter</see>.
    /// </remarks>
    public interface IDataSetWriter
    {
        /// <summary>
        /// DataSetWriterId — unique within the parent WriterGroup
        /// and carried in every DataSetMessage header.
        /// </summary>
        ushort DataSetWriterId { get; }

        /// <summary>
        /// Writer name (matches
        /// <see cref="DataSetWriterDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// PublishedDataSet this writer publishes.
        /// </summary>
        IPublishedDataSet PublishedDataSet { get; }

        /// <summary>
        /// Bitmask selecting which DataSetField envelope fields
        /// (Value, Status, SourceTimestamp …) are written to each
        /// DataSetMessage.
        /// </summary>
        DataSetFieldContentMask FieldContentMask { get; }

        /// <summary>
        /// Number of DeltaFrame messages emitted between successive
        /// KeyFrame messages. Zero means KeyFrame-only.
        /// </summary>
        uint KeyFrameCount { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        DataSetWriterDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the WriterGroup cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
