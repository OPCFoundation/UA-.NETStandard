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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Abstract container for one PubSub DataSetMessage shared between
    /// the UADP and JSON mappings. Concrete derived records add
    /// mapping-specific header fields (DataSetFlags1 / DataSetFlags2
    /// for UADP, message-type discriminator for JSON).
    /// </summary>
    /// <remarks>
    /// Implements the shared DataSetMessage model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.2">
    /// Part 14 §5.3.2 DataSetMessage</see>. Field order in
    /// <see cref="Fields"/> mirrors the metadata field order, per the
    /// requirement of Part 14 §5.2.3.
    /// </remarks>
    public abstract record PubSubDataSetMessage
    {
        /// <summary>
        /// DataSetWriterId of the writer that produced this message.
        /// Matched against the DataSetReader's filter on the receive
        /// side.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// Per-writer monotonically increasing sequence number used by
        /// the receive-side replay window.
        /// </summary>
        public uint SequenceNumber { get; init; }

        /// <summary>
        /// Publish-side timestamp populated from the DataSetWriter
        /// clock at the moment the message was produced.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Aggregate status of the DataSetMessage. Encodes good /
        /// uncertain / bad on a per-message basis; per-field status is
        /// carried by individual <see cref="DataSetField"/> values when
        /// the DataValue field-encoding is in use.
        /// </summary>
        public StatusCode Status { get; init; }

        /// <summary>
        /// Kind of DataSetMessage (KeyFrame / DeltaFrame / Event /
        /// KeepAlive).
        /// </summary>
        public PubSubDataSetMessageType MessageType { get; init; }

        /// <summary>
        /// MetaDataVersion of the DataSetMetaData this message conforms
        /// to. Receivers must reject the payload when MajorVersion
        /// differs from the registered metadata's MajorVersion.
        /// </summary>
        public ConfigurationVersionDataType MetaDataVersion { get; init; }
            = new ConfigurationVersionDataType();

        /// <summary>
        /// Payload fields, in the order specified by the
        /// DataSetMetaData. Delta-frames may carry fewer fields than
        /// metadata; KeepAlive carries none.
        /// </summary>
        public ArrayOf<DataSetField> Fields { get; init; } = [];
    }
}
