/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP ActionResponse NetworkMessage.
    /// </summary>
    /// <remarks>
    /// Implements the UADP action header and Action response DataSetMessage
    /// structure defined by Part 14 v1.05 §7.2.4.4.2 and §7.2.4.5.10.
    /// Part 14 v1.05.07 Table 167 has no UADP Status field between
    /// ActionState and FieldCount; the JSON mapping carries ActionResponse
    /// Status separately.
    /// </remarks>
    public sealed record UadpActionResponseMessage : PubSubNetworkMessage
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
        /// Distinguishes this action response from data, discovery, and
        /// action request messages.
        /// </summary>
        public UadpNetworkMessageType MessageType { get; init; }
            = UadpNetworkMessageType.ActionResponse;

        /// <summary>
        /// Writer identifier of the responder Action metadata.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// Action target identifier unique within the Action metadata.
        /// </summary>
        public ushort ActionTargetId { get; init; }

        /// <summary>
        /// Request identifier copied from the matching request.
        /// </summary>
        public ushort RequestId { get; init; }

        /// <summary>
        /// Current Action state on the responder.
        /// </summary>
        public ActionState ActionState { get; init; }

        /// <summary>
        /// Operation status for the Action response.
        /// </summary>
        public StatusCode Status { get; init; } = (StatusCode)StatusCodes.Good;

        /// <summary>
        /// Optional correlation data copied from the request.
        /// </summary>
        public ByteString CorrelationData { get; init; }

        /// <summary>
        /// PublisherId of the requestor encoded as BaseDataType in the
        /// UADP ActionHeader.
        /// </summary>
        public Variant RequestorId { get; init; }

        /// <summary>
        /// Response address is not encoded for responses by Part 14
        /// Table 154; the property is retained for request/response API symmetry.
        /// </summary>
        public string ResponseAddress { get; init; } = string.Empty;

        /// <summary>
        /// TimeoutHint is not used for responses by Part 14 Table 154.
        /// </summary>
        public double TimeoutHint { get; init; }

        /// <summary>
        /// Action response fields encoded with the selected UADP field encoding.
        /// </summary>
        public ArrayOf<DataSetField> Payload { get; init; } = [];

        /// <summary>
        /// Field encoding used for <see cref="Payload"/>.
        /// </summary>
        public PubSubFieldEncoding FieldEncoding { get; init; } = PubSubFieldEncoding.Variant;

        /// <summary>
        /// Per-field content mask for DataValue encoding.
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
