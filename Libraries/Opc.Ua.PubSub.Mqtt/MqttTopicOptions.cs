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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Topic-level options that apply to every publish / subscribe on
    /// the connection. The topic structure itself is built by
    /// <see cref="MqttTopicBuilder"/> from the Part 14 §7.3.4.7.3
    /// schema.
    /// </summary>
    /// <remarks>
    /// Implements the retain handling described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
    /// Part 14 §7.3.4.8 Retained discovery messages</see> and the
    /// default QoS selector for data publications per §7.3.4.5.
    /// </remarks>
    public sealed class MqttTopicOptions
    {
        /// <summary>
        /// Topic prefix used as the first segment of every published
        /// data / metadata topic. Must not contain MQTT wildcard
        /// characters (<c>#</c> or <c>+</c>) and must not start or end
        /// with a <c>/</c>. Defaults to <c>opcua</c> per Part 14
        /// §7.3.4.4 Table 201.
        /// </summary>
        public string Prefix { get; set; } = "opcua";

        /// <summary>
        /// Sets the <c>Retain</c> flag on metadata publications so
        /// late-joining subscribers receive the active metadata
        /// before consuming live data (Part 14 §7.3.4.8).
        /// </summary>
        public bool RetainMetaDataMessages { get; set; } = true;

        /// <summary>
        /// Sets the <c>Retain</c> flag on discovery (PublisherEndpoint,
        /// DataSetWriterConfiguration) publications so late-joining
        /// subscribers can discover the publisher topology on connect.
        /// </summary>
        public bool RetainDiscoveryMessages { get; set; } = true;

        /// <summary>
        /// Default QoS applied to data publications when the writer
        /// configuration does not override it (Part 14 §7.3.4.5).
        /// </summary>
        public MqttQualityOfService DefaultQos { get; set; } = MqttQualityOfService.AtLeastOnce;
    }
}
