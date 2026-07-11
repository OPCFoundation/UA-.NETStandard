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
    /// Concrete JSON NetworkMessage (<c>ua-data</c> envelope) carrying
    /// one or more DataSetMessages plus the JSON-specific identification
    /// fields described by Part 14 §7.2.5.3.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.3">
    /// Part 14 §7.2.5.3</see> JsonNetworkMessage layout. The
    /// <see cref="SingleMessageMode"/> flag selects the flat layout
    /// described in Annex A.3.3 (no <c>Messages</c> wrapper, envelope
    /// and DataSetMessage fields fused).
    /// </remarks>
    public sealed record JsonNetworkMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// Wire tag value for a regular DataSetMessage envelope.
        /// </summary>
        public const string MessageTypeData = "ua-data";

        /// <summary>
        /// Wire tag value for the metadata-announcement envelope.
        /// </summary>
        public const string MessageTypeMetaData = "ua-metadata";

        /// <summary>
        /// MessageId per §7.2.5.3 - publisher-unique identifier used
        /// for diagnostics and de-duplication.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// MessageType discriminator. Defaults to
        /// <see cref="MessageTypeData"/>.
        /// </summary>
        public string MessageType { get; init; } = MessageTypeData;

        /// <summary>
        /// JSON NetworkMessageContentMask controlling the envelope and
        /// optional NetworkMessage fields.
        /// </summary>
        public JsonNetworkMessageContentMask ContentMask { get; init; }
            = JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.DataSetMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetClassId;

        /// <summary>
        /// DataSetClassId of the published dataset class. May be
        /// <see cref="Uuid.Empty"/> when the publisher does not assign
        /// one.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// Name of the WriterGroup that created the NetworkMessage.
        /// </summary>
        public string WriterGroupName { get; init; } = string.Empty;

        /// <summary>
        /// Optional ReplyTo endpoint list used by request/response
        /// brokered transports (Part 14 §7.2.5.3).
        /// </summary>
        public ArrayOf<string> ReplyTo { get; init; } = [];

        /// <summary>
        /// When <see langword="true"/>, the encoder emits the flat
        /// single-message layout from Annex A.3.3: the
        /// <c>Messages</c> array is suppressed and the single
        /// DataSetMessage's fields are merged into the envelope.
        /// </summary>
        public bool SingleMessageMode { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
