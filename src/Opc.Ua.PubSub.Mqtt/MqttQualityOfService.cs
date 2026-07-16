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
    /// MQTT delivery guarantee mapped onto the Part 14
    /// <c>BrokerTransportQualityOfService</c> enumeration.
    /// </summary>
    /// <remarks>
    /// Implements the QoS mapping table of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.5">
    /// Part 14 §7.3.4.5 MQTT Quality of Service mapping</see>. Numeric
    /// values match the MQTT wire QoS encoding so the adapter can cast
    /// without an extra lookup.
    /// </remarks>
    public enum MqttQualityOfService
    {
        /// <summary>
        /// QoS 0 — fire and forget. Maps to
        /// <c>BrokerTransportQualityOfService.BestEffort</c> /
        /// <c>AtMostOnce</c>.
        /// </summary>
        AtMostOnce = 0,

        /// <summary>
        /// QoS 1 — delivery acknowledged. Maps to
        /// <c>BrokerTransportQualityOfService.AtLeastOnce</c>.
        /// </summary>
        AtLeastOnce = 1,

        /// <summary>
        /// QoS 2 — exactly-once handshake. Maps to
        /// <c>BrokerTransportQualityOfService.ExactlyOnce</c>.
        /// </summary>
        ExactlyOnce = 2
    }
}
