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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Encoding tier carried inside the MQTT topic hierarchy as the
    /// <c>&lt;Encoding&gt;</c> segment of the Part 14 §7.3.4.7.3 data
    /// topic and §7.3.4.7.4 metadata topic.
    /// </summary>
    /// <remarks>
    /// Implements the topic-encoding selector defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.3">
    /// Part 14 §7.3.4.7.3 Data topic</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.9.1">
    /// Part 14 §7.3.4.9.1 JSON message body</see> /
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.9.2">
    /// §7.3.4.9.2 UADP message body</see>. The wire segment is the
    /// lowercase enum name.
    /// </remarks>
    public enum MqttEncoding
    {
        /// <summary>
        /// UADP binary NetworkMessage body
        /// (<see cref="Profiles.PubSubMqttUadpTransport"/>).
        /// </summary>
        Uadp,

        /// <summary>
        /// JSON NetworkMessage body
        /// (<see cref="Profiles.PubSubMqttJsonTransport"/>).
        /// </summary>
        Json
    }

    /// <summary>
    /// Extension helpers for <see cref="MqttEncoding"/>.
    /// </summary>
    public static class MqttEncodingExtensions
    {
        /// <summary>
        /// Returns the lowercase topic segment for the given encoding.
        /// </summary>
        /// <param name="encoding">Encoding value.</param>
        /// <returns>
        /// <c>"uadp"</c> for <see cref="MqttEncoding.Uadp"/>,
        /// <c>"json"</c> for <see cref="MqttEncoding.Json"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="encoding"/> is not a defined value.
        /// </exception>
        public static string ToTopicSegment(this MqttEncoding encoding) => encoding switch
        {
            MqttEncoding.Uadp => "uadp",
            MqttEncoding.Json => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };

        /// <summary>
        /// Returns the MQTT 5 ContentType property value for the given
        /// encoding (Part 14 §7.3.4.9.1 / §7.3.4.9.2).
        /// </summary>
        /// <param name="encoding">Encoding value.</param>
        /// <returns>
        /// <c>"application/json"</c> for <see cref="MqttEncoding.Json"/>,
        /// <c>"application/opcua+uadp"</c> for <see cref="MqttEncoding.Uadp"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="encoding"/> is not a defined value.
        /// </exception>
        public static string ToContentType(this MqttEncoding encoding) => encoding switch
        {
            MqttEncoding.Uadp => "application/opcua+uadp",
            MqttEncoding.Json => "application/json",
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
    }
}
