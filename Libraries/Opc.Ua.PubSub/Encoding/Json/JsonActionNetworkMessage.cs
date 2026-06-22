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
    /// JSON action message carrying Part 14 action request,
    /// response, metadata or responder payloads over JSON-on-MQTT.
    /// </summary>
    /// <remarks>
    /// Carries the source-generated <see cref="Opc.Ua.JsonActionNetworkMessage"/>,
    /// <see cref="Opc.Ua.JsonActionMetaDataMessage"/> and
    /// <see cref="Opc.Ua.JsonActionResponderMessage"/> models while keeping
    /// the PubSub pipeline's <see cref="PubSubNetworkMessage"/> contract.
    /// Implements
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.6">
    /// Part 14 §7.2.5.6</see> request/response Action NetworkMessage
    /// envelope with <c>MessageType=ua-action</c>.
    /// </remarks>
    public sealed record JsonActionNetworkMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// Wire literal for the JSON action envelope.
        /// </summary>
        public const string MessageTypeAction = "ua-action";

        /// <summary>
        /// Wire literal for the JSON action metadata message.
        /// </summary>
        public const string MessageTypeActionMetaData = "ua-actionmetadata";

        /// <summary>
        /// Wire literal for the JSON action responder message.
        /// </summary>
        public const string MessageTypeActionResponder = "ua-actionresponder";

        /// <summary>
        /// Source-generated Part 14 action NetworkMessage envelope.
        /// </summary>
        public Opc.Ua.JsonActionNetworkMessage? NetworkMessage { get; init; }

        /// <summary>
        /// Source-generated Part 14 action metadata message.
        /// </summary>
        public Opc.Ua.JsonActionMetaDataMessage? MetaDataMessage { get; init; }

        /// <summary>
        /// Source-generated Part 14 action responder message.
        /// </summary>
        public Opc.Ua.JsonActionResponderMessage? ResponderMessage { get; init; }

        /// <summary>
        /// MessageId per Part 14 §7.2.5.3. Kept as a convenience mirror
        /// for the generated action message carried by this instance.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// Response address for action responses.
        /// </summary>
        public string ResponseAddress { get; init; } = string.Empty;

        /// <summary>
        /// Binary correlation data for action request/response matching.
        /// </summary>
        public ByteString CorrelationData { get; init; } = ByteString.Empty;

        /// <summary>
        /// Requestor identity supplied by the action requestor.
        /// </summary>
        public string RequestorId { get; init; } = string.Empty;

        /// <summary>
        /// Action timeout hint in milliseconds.
        /// </summary>
        public double TimeoutHint { get; init; }

        /// <summary>
        /// Action request/response structures carried by the network envelope.
        /// </summary>
        public ArrayOf<ExtensionObject> Messages { get; init; } = [];

        /// <summary>
        /// Legacy non-spec Action URI.
        /// </summary>
        [System.Obsolete(
            "Use NetworkMessage.Messages with Opc.Ua.JsonActionRequestMessage or " +
            "Opc.Ua.JsonActionResponseMessage payloads.")]
        public string Action { get; init; } = string.Empty;

        /// <summary>
        /// Legacy non-spec named Variant parameters.
        /// </summary>
        [System.Obsolete(
            "Use the Payload field on Opc.Ua.JsonActionRequestMessage or " +
            "Opc.Ua.JsonActionResponseMessage.")]
        public System.Collections.Generic.IReadOnlyDictionary<string, Variant> Parameters { get; init; }
            = new System.Collections.Generic.Dictionary<string, Variant>();

        /// <summary>
        /// Legacy non-spec request identifier.
        /// </summary>
        [System.Obsolete(
            "Use Opc.Ua.JsonActionRequestMessage.RequestId or " +
            "Opc.Ua.JsonActionResponseMessage.RequestId.")]
        public string RequestId { get; init; } = string.Empty;

        /// <summary>
        /// Legacy non-spec response identifier.
        /// </summary>
        [System.Obsolete("Use NetworkMessage.CorrelationData for response correlation.")]
        public string ResponseId { get; init; } = string.Empty;

        /// <summary>
        /// Indicates the legacy action carries a response.
        /// </summary>
        [System.Obsolete("Inspect NetworkMessage.Messages for Opc.Ua.JsonActionResponseMessage.")]
        public bool IsResponse => !string.IsNullOrEmpty(ResponseId);

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
