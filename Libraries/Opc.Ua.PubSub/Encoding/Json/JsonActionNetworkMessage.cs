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

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// JSON action NetworkMessage carrying an action invocation
    /// request or response over JSON-on-MQTT.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.5.6">
    /// Part 14 §7.2.5.6</see> request/response Action NetworkMessage
    /// envelope with <c>MessageType=ua-action</c>, an
    /// <see cref="Action"/> URI, named
    /// <see cref="Parameters"/> (Variant-keyed) and a request /
    /// response correlation pair (<see cref="RequestId"/> /
    /// <see cref="ResponseId"/>).
    /// </remarks>
    public sealed record JsonActionNetworkMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// Wire literal for the JSON action envelope.
        /// </summary>
        public const string MessageTypeAction = "ua-action";

        /// <summary>
        /// MessageId per Part 14 §7.2.5.3.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// Action URI invoked by this message.
        /// </summary>
        public string Action { get; init; } = string.Empty;

        /// <summary>
        /// Named Variant parameters carrying the action arguments.
        /// </summary>
        public IReadOnlyDictionary<string, Variant> Parameters { get; init; }
            = new Dictionary<string, Variant>();

        /// <summary>
        /// Correlation identifier for the originating request.
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>
        /// Correlation identifier for the matching response (only set
        /// on response messages).
        /// </summary>
        public string ResponseId { get; init; } = string.Empty;

        /// <summary>
        /// Indicates the action carries a response (i.e.
        /// <see cref="ResponseId"/> is non-empty).
        /// </summary>
        public bool IsResponse => !string.IsNullOrEmpty(ResponseId);

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
