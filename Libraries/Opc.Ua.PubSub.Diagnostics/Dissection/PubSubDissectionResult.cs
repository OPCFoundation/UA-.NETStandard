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

using System;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// PubSub NetworkMessage mapping identified by the offline dissector.
    /// </summary>
    public enum PubSubDissectionMessageType
    {
        /// <summary>
        /// The mapping could not be determined.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// UADP NetworkMessage mapping.
        /// </summary>
        Uadp = 1,

        /// <summary>
        /// JSON NetworkMessage mapping.
        /// </summary>
        Json = 2,

        /// <summary>
        /// PubSub discovery message.
        /// </summary>
        Discovery = 3
    }

    /// <summary>
    /// PubSub security state visible from the captured NetworkMessage.
    /// </summary>
    public enum PubSubDissectionSecurityState
    {
        /// <summary>
        /// No PubSub message-level security header was present.
        /// </summary>
        None = 0,

        /// <summary>
        /// The NetworkMessage is signed and was not verified offline.
        /// </summary>
        Signed = 1,

        /// <summary>
        /// The NetworkMessage is encrypted and was not decrypted offline.
        /// </summary>
        Encrypted = 2
    }

    /// <summary>
    /// Immutable dissection result for one captured PubSub frame.
    /// </summary>
    public sealed record PubSubDissectionResult
    {
        /// <summary>
        /// Capture timestamp.
        /// </summary>
        public required DateTimeOffset Timestamp { get; init; }

        /// <summary>
        /// Direction of the frame relative to the local node.
        /// </summary>
        public required PubSubCaptureDirection Direction { get; init; }

        /// <summary>
        /// Transport profile URI supplied by the capture seam.
        /// </summary>
        public required string TransportProfileUri { get; init; }

        /// <summary>
        /// Wire endpoint, when known.
        /// </summary>
        public string? Endpoint { get; init; }

        /// <summary>
        /// MQTT topic, when known.
        /// </summary>
        public string? Topic { get; init; }

        /// <summary>
        /// Raw frame length in bytes.
        /// </summary>
        public int PayloadLength { get; init; }

        /// <summary>
        /// Message mapping identified by the dissector.
        /// </summary>
        public PubSubDissectionMessageType MessageType { get; init; }

        /// <summary>
        /// Message-level security state.
        /// </summary>
        public PubSubDissectionSecurityState SecurityState { get; init; }

        /// <summary>
        /// PublisherId carried in the NetworkMessage header, when decoded.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// WriterGroupId carried in the NetworkMessage header, when decoded.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// DataSetWriterIds observed in decoded DataSetMessages.
        /// </summary>
        public ArrayOf<ushort> DataSetWriterIds { get; init; } = [];

        /// <summary>
        /// Decoded DataSets for cleartext messages.
        /// </summary>
        public ArrayOf<PubSubDissectedDataSet> DataSets { get; init; } = [];

        /// <summary>
        /// SecurityTokenId from the UADP SecurityHeader, when present.
        /// </summary>
        public uint? SecurityTokenId { get; init; }

        /// <summary>
        /// True when the frame was decoded into a PubSub object model.
        /// </summary>
        public bool IsDecoded { get; init; }

        /// <summary>
        /// True when malformed or unsupported bytes prevented dissection.
        /// </summary>
        public bool IsUndecodable { get; init; }

        /// <summary>
        /// Human-readable diagnostic note for secured or undecodable frames.
        /// </summary>
        public string? DiagnosticMessage { get; init; }
    }

    /// <summary>
    /// Decoded DataSetMessage projected into an immutable diagnostic shape.
    /// </summary>
    public sealed record PubSubDissectedDataSet
    {
        /// <summary>
        /// DataSetWriterId that produced the DataSetMessage.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// DataSetMessage sequence number.
        /// </summary>
        public uint SequenceNumber { get; init; }

        /// <summary>
        /// DataSetMessage kind.
        /// </summary>
        public PubSubDataSetMessageType MessageType { get; init; }

        /// <summary>
        /// Aggregate DataSetMessage status.
        /// </summary>
        public StatusCode Status { get; init; }

        /// <summary>
        /// Decoded fields in metadata order.
        /// </summary>
        public ArrayOf<PubSubDissectedField> Fields { get; init; } = [];
    }

    /// <summary>
    /// Decoded field value projected from a cleartext DataSetMessage.
    /// </summary>
    public sealed record PubSubDissectedField
    {
        /// <summary>
        /// Field name, when available from metadata or JSON payload.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Field value as decoded by the PubSub stack.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// Field-level status code.
        /// </summary>
        public StatusCode StatusCode { get; init; } = (StatusCode)StatusCodes.Good;

        /// <summary>
        /// Field encoding used by the producer.
        /// </summary>
        public PubSubFieldEncoding Encoding { get; init; }
    }
}
