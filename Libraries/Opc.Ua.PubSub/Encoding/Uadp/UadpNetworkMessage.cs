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

using System.Collections.Generic;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP concrete <see cref="PubSubNetworkMessage"/>. Adds the UADP
    /// header fields (version, group / payload headers, extended
    /// flags, promoted fields, discovery selector) on top of the
    /// transport-neutral payload tree.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 — UADP NetworkMessage mapping</see>. The
    /// <see cref="ContentMask"/> drives which optional header fields
    /// are emitted; the encoder honours every bit defined in
    /// <see cref="UadpNetworkMessageContentMask"/>.
    /// </remarks>
    public sealed record UadpNetworkMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// UADP protocol version stored in the low nibble of the
        /// NetworkMessage header byte. Currently <c>1</c>; the encoder
        /// rejects any other value at write time.
        /// </summary>
        public byte UadpVersion { get; init; } = 1;

        /// <summary>
        /// Mask of optional NetworkMessage header sections to emit /
        /// expect on decode. Bits follow the stack-generated
        /// <see cref="UadpNetworkMessageContentMask"/> enumeration.
        /// </summary>
        public UadpNetworkMessageContentMask ContentMask { get; init; }

        /// <summary>
        /// GroupVersion stamp carried in the optional GroupHeader.
        /// Receivers use it to detect a publisher GroupVersion change
        /// requiring metadata refresh.
        /// </summary>
        public uint GroupVersion { get; init; }

        /// <summary>
        /// Sequence number of this NetworkMessage within the
        /// WriterGroup output stream. Increments per-NetworkMessage
        /// when <see cref="UadpNetworkMessageContentMask.NetworkMessageNumber"/>
        /// is enabled.
        /// </summary>
        public ushort NetworkMessageNumber { get; init; }

        /// <summary>
        /// Per-WriterGroup message sequence number used by the
        /// receive-side replay detection window.
        /// </summary>
        public ushort SequenceNumber { get; init; }

        /// <summary>
        /// DataSetClassId stamped on every DataSetMessage in this
        /// NetworkMessage; absent value when not configured.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// Optional network-wide Timestamp carried at the
        /// NetworkMessage level when
        /// <see cref="UadpNetworkMessageContentMask.Timestamp"/> is
        /// enabled.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Optional fractional-second component complementing
        /// <see cref="Timestamp"/>. Present when
        /// <see cref="UadpNetworkMessageContentMask.PicoSeconds"/> is
        /// enabled.
        /// </summary>
        public ushort PicoSeconds { get; init; }

        /// <summary>
        /// Promoted fields carried in the NetworkMessage header per
        /// Part 14 §7.2.4.5.5 — visible to middleware filters without
        /// decrypting / decoding the DataSetMessages.
        /// </summary>
        public IReadOnlyList<DataSetField> PromotedFields { get; init; } = [];

        /// <summary>
        /// Discriminator distinguishing regular data NetworkMessages
        /// from the two discovery variants. The encoder routes
        /// discovery messages to the UADP discovery coder.
        /// </summary>
        public UadpNetworkMessageType MessageType { get; init; }
            = UadpNetworkMessageType.DataSetMessage;

        /// <inheritdoc/>
        public override string TransportProfileUri => Profiles.PubSubUdpUadpTransport;
    }
}
