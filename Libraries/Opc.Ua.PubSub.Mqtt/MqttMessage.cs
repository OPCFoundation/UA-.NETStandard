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
    /// One outbound or inbound MQTT message exchanged through the
    /// adapter. Modelled as a <c>readonly record struct</c> so it can
    /// be moved through bounded channels without per-message
    /// allocation.
    /// </summary>
    /// <remarks>
    /// Implements the per-message payload envelope used for the JSON
    /// and UADP body mappings of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.9.1">
    /// Part 14 §7.3.4.9.1 JSON body</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.9.2">
    /// §7.3.4.9.2 UADP body</see>. <see cref="ContentType"/> and
    /// <see cref="ResponseTopic"/> are MQTT 5.0 properties; they are
    /// silently dropped when the negotiated protocol version is 3.1.1
    /// per §7.3.4.4.
    /// </remarks>
    /// <param name="Topic">Topic to publish to / topic the message was received on.</param>
    /// <param name="Payload">Raw frame bytes (the encoder's output).</param>
    /// <param name="Qos">Delivery guarantee per §7.3.4.5.</param>
    /// <param name="Retain">
    /// Set to <see langword="true"/> for retained metadata / discovery
    /// publications per §7.3.4.8.
    /// </param>
    /// <param name="ContentType">
    /// MQTT 5.0 ContentType property (e.g. <c>application/json</c>,
    /// <c>application/opcua+uadp</c>). Ignored on MQTT 3.1.1.
    /// </param>
    /// <param name="ResponseTopic">
    /// MQTT 5.0 ResponseTopic property. Optional; ignored on MQTT 3.1.1.
    /// </param>
    public readonly record struct MqttMessage(
        string Topic,
        ReadOnlyMemory<byte> Payload,
        MqttQualityOfService Qos,
        bool Retain,
        string? ContentType,
        string? ResponseTopic);
}
