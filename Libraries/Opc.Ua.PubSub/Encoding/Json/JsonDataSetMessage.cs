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

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Concrete JSON DataSetMessage. Adds the JSON-specific
    /// <see cref="ContentMask"/> and the wire-form discriminator on
    /// top of the shared <see cref="PubSubDataSetMessage"/> envelope.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see> JsonDataSetMessage layout.
    /// </remarks>
    public sealed record JsonDataSetMessage : PubSubDataSetMessage
    {
        /// <summary>
        /// JSON content-mask selecting which optional fields appear in
        /// the wire payload (Part 14 §7.2.5.4 Table 165).
        /// </summary>
        public JsonDataSetMessageContentMask ContentMask { get; init; }
            = JsonDataSetMessageContentMask.DataSetWriterId
            | JsonDataSetMessageContentMask.SequenceNumber
            | JsonDataSetMessageContentMask.Timestamp
            | JsonDataSetMessageContentMask.Status
            | JsonDataSetMessageContentMask.MessageType
            | JsonDataSetMessageContentMask.MetaDataVersion;

        /// <summary>
        /// Name of the DataSetWriter that created the DataSetMessage.
        /// </summary>
        public string DataSetWriterName { get; init; } = string.Empty;

        /// <summary>
        /// PublisherId carried at DataSetMessage level when the
        /// NetworkMessage header is suppressed.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// Name of the WriterGroup that created the DataSetMessage.
        /// </summary>
        public string WriterGroupName { get; init; } = string.Empty;

        /// <summary>
        /// Wire-form discriminator (e.g. <c>ua-keyframe</c>) derived
        /// from <see cref="PubSubDataSetMessage.MessageType"/>. When
        /// non-empty this value wins over the enum-derived default,
        /// allowing forward-compatibility with future message types.
        /// </summary>
        public string MessageTypeName { get; init; } = string.Empty;

        /// <summary>
        /// Per-field content mask honoured when
        /// <see cref="JsonEncodingMode"/> emits <c>DataValue</c>
        /// envelopes. The encoder suppresses any <c>DataValue</c>
        /// member whose corresponding bit is not set; the decoder
        /// populates the matching <see cref="DataSetField"/>
        /// properties only for set bits.
        /// </summary>
        /// <remarks>
        /// Implements the per-field selector of
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.3.2.3">
        /// Part 14 §6.3.2.3 DataSetFieldContentMask</see>. The default
        /// <see cref="DataSetFieldContentMask.None"/> preserves
        /// pre-Phase-15 behaviour (all four <c>DataValue</c> members
        /// emitted unconditionally).
        /// </remarks>
        public DataSetFieldContentMask FieldContentMask { get; init; }
            = DataSetFieldContentMask.None;
    }

    /// <summary>
    /// Translates between <see cref="PubSubDataSetMessageType"/> and the
    /// canonical JSON wire strings (<c>ua-keyframe</c>,
    /// <c>ua-deltaframe</c>, <c>ua-event</c>, <c>ua-keepalive</c>) used
    /// by Part 14 §7.2.5.4.
    /// </summary>
    /// <remarks>
    /// Implements the wire-tag table of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>.
    /// </remarks>
    public static class JsonDataSetMessageType
    {
        /// <summary>
        /// Wire tag for a KeyFrame DataSetMessage.
        /// </summary>
        public const string KeyFrame = "ua-keyframe";

        /// <summary>
        /// Wire tag for a DeltaFrame DataSetMessage.
        /// </summary>
        public const string DeltaFrame = "ua-deltaframe";

        /// <summary>
        /// Wire tag for an Event DataSetMessage.
        /// </summary>
        public const string Event = "ua-event";

        /// <summary>
        /// Wire tag for a KeepAlive DataSetMessage.
        /// </summary>
        public const string KeepAlive = "ua-keepalive";

        /// <summary>
        /// Translates a <see cref="PubSubDataSetMessageType"/> to its
        /// wire-tag string.
        /// </summary>
        /// <param name="messageType">Enum value.</param>
        /// <returns>Wire-tag string.</returns>
        public static string ToWireString(PubSubDataSetMessageType messageType)
        {
            return messageType switch
            {
                PubSubDataSetMessageType.KeyFrame => KeyFrame,
                PubSubDataSetMessageType.DeltaFrame => DeltaFrame,
                PubSubDataSetMessageType.Event => Event,
                PubSubDataSetMessageType.KeepAlive => KeepAlive,
                _ => KeyFrame
            };
        }

        /// <summary>
        /// Translates a wire-tag string to a
        /// <see cref="PubSubDataSetMessageType"/>.
        /// </summary>
        /// <param name="value">Wire-tag string (case-sensitive).</param>
        /// <param name="messageType">On success, parsed enum.</param>
        /// <returns><see langword="true"/> when the input is one of the
        /// known tags.</returns>
        public static bool TryParse(string value, out PubSubDataSetMessageType messageType)
        {
            switch (value)
            {
                case KeyFrame:
                    messageType = PubSubDataSetMessageType.KeyFrame;
                    return true;
                case DeltaFrame:
                    messageType = PubSubDataSetMessageType.DeltaFrame;
                    return true;
                case Event:
                    messageType = PubSubDataSetMessageType.Event;
                    return true;
                case KeepAlive:
                    messageType = PubSubDataSetMessageType.KeepAlive;
                    return true;
                default:
                    messageType = PubSubDataSetMessageType.KeyFrame;
                    return false;
            }
        }
    }
}
