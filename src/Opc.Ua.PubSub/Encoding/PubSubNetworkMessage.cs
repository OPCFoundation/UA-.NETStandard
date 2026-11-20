// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Abstract container for one PubSub NetworkMessage shared between
    /// the UADP and JSON mappings. Concrete derived records add
    /// mapping-specific header fields and security envelopes.
    /// </summary>
    /// <remarks>
    /// Implements the shared NetworkMessage model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.4">
    /// Part 14 §5.3.4 NetworkMessage</see>. A NetworkMessage carries
    /// shared identification (<see cref="PublisherId"/>,
    /// optional <see cref="WriterGroupId"/>) and one or more
    /// <see cref="DataSetMessages"/> payloads. <see cref="MetaData"/>
    /// is populated only on metadata-announcement messages
    /// (Part 14 §7.2.4.6.4 / §7.2.5.5.2).
    /// </remarks>
    public abstract record PubSubNetworkMessage
    {
        /// <summary>
        /// Identifier of the transport profile this message is bound
        /// to. Used by the dispatcher to route messages to the matching
        /// encoder / decoder.
        /// </summary>
        public abstract string TransportProfileUri { get; }

        /// <summary>
        /// Publisher identity carried in the NetworkMessage header.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// Optional WriterGroupId carried in the NetworkMessage
        /// GroupHeader (UADP) or in the JSON envelope. A
        /// <see langword="null"/> value means the GroupHeader is
        /// omitted or unknown.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Payload DataSetMessages contained in this NetworkMessage.
        /// </summary>
        public ArrayOf<PubSubDataSetMessage> DataSetMessages { get; init; }
            = [];

        /// <summary>
        /// DataSetMetaData carried on a metadata-announcement message.
        /// <see langword="null"/> on regular data messages.
        /// </summary>
        public DataSetMetaDataType? MetaData { get; init; }
    }
}
