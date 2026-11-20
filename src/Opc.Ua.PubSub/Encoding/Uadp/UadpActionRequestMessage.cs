// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP ActionRequest NetworkMessage.
    /// </summary>
    /// <remarks>
    /// Implements the UADP action header and Action request DataSetMessage
    /// structure defined by Part 14 v1.05 §7.2.4.4.2 and §7.2.4.5.9.
    /// The UADP NetworkMessage type bits stay at the default
    /// DataSetMessage value; <see cref="ExtendedFlags2EncodingMask.ActionHeaderEnabled"/>
    /// and ActionFlags bit 0 identify the request. TODO: verify the
    /// final 1.05.07 table before removing this note.
    /// </remarks>
    public sealed record UadpActionRequestMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// UADP protocol version (low nibble of header byte).
        /// </summary>
        public byte UadpVersion { get; init; } = 1;

        /// <summary>
        /// DataSetClassId carried at the NetworkMessage level (Guid).
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// Distinguishes this action request from data, discovery, and
        /// action response messages.
        /// </summary>
        public UadpNetworkMessageType MessageType { get; init; }
            = UadpNetworkMessageType.ActionRequest;

        /// <summary>
        /// Writer identifier of the responder Action metadata.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// Action target identifier unique within the Action metadata.
        /// </summary>
        public ushort ActionTargetId { get; init; }

        /// <summary>
        /// Request identifier supplied by the requestor.
        /// </summary>
        public ushort RequestId { get; init; }

        /// <summary>
        /// Expected Action state on the responder.
        /// </summary>
        public ActionState ActionState { get; init; }

        /// <summary>
        /// Optional correlation data returned in the response.
        /// </summary>
        public ByteString CorrelationData { get; init; }

        /// <summary>
        /// PublisherId of the requestor encoded as BaseDataType in the
        /// UADP ActionHeader.
        /// </summary>
        public Variant RequestorId { get; init; }

        /// <summary>
        /// Optional address used by the responder for responses.
        /// </summary>
        public string ResponseAddress { get; init; } = string.Empty;

        /// <summary>
        /// Timeout hint for request processing and response waiting.
        /// </summary>
        public double TimeoutHint { get; init; }

        /// <summary>
        /// Action request fields encoded with the selected UADP field encoding.
        /// </summary>
        public ArrayOf<DataSetField> Payload { get; init; } = [];

        /// <summary>
        /// Field encoding used for <see cref="Payload"/>.
        /// </summary>
        public PubSubFieldEncoding FieldEncoding { get; init; } = PubSubFieldEncoding.Variant;

        /// <summary>
        /// Per-field content mask. DataValue field encoding is not
        /// allowed for UADP Action messages by Part 14 §7.2.4.5.9.
        /// </summary>
        public DataSetFieldContentMask FieldContentMask { get; init; }

        /// <summary>
        /// Marks the frame for wrapping by UADP message security.
        /// </summary>
        public bool SecurityEnabled { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
    }
}
