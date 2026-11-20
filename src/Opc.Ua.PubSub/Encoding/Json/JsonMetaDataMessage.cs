// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Concrete JSON metadata-announcement message
    /// (<c>ua-metadata</c> envelope) carrying a single
    /// <see cref="DataSetMetaDataType"/> for a specific
    /// DataSetWriter.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5">
    /// Part 14 §7.2.5.5</see> JsonDataSetMetaDataMessage layout. The
    /// metadata payload is exposed both on the base
    /// <see cref="PubSubNetworkMessage.MetaData"/> property and on
    /// <see cref="MetaDataPayload"/> so callers can use whichever
    /// accessor matches their fluent style.
    /// </remarks>
    public sealed record JsonMetaDataMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// MessageId per Part 14 §7.2.5.3.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// DataSetWriterId of the writer whose metadata is announced.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// WriterGroupName of the writer group that owns the DataSetWriter
        /// (mandatory in Part 14 §7.2.5.5.2 Table 188).
        /// </summary>
        public string WriterGroupName { get; init; } = string.Empty;

        /// <summary>
        /// Name of the DataSetWriter (mandatory in Part 14 §7.2.5.5.2
        /// Table 188).
        /// </summary>
        public string DataSetWriterName { get; init; } = string.Empty;

        /// <summary>
        /// When the message was first sent to the middleware (mandatory in
        /// Part 14 §7.2.5.5.2 Table 188). The encoder uses the current time
        /// when it is not set.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; } = DateTimeUtc.MinValue;

        /// <summary>
        /// DataSetClassId. Not part of the Part 14 §7.2.5.5.2 wire layout;
        /// used only to key the registry for a locally built message.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// MetaData payload re-exposed for fluent access. When set,
        /// wins over <see cref="PubSubNetworkMessage.MetaData"/> at
        /// encode time.
        /// </summary>
        public DataSetMetaDataType? MetaDataPayload { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
