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
    /// MQTT protocol version selector. Maps directly onto MQTTnet's
    /// internal version enum (and onto the wire protocol level byte
    /// transmitted in CONNECT).
    /// </summary>
    /// <remarks>
    /// Implements the protocol version surface defined by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.4">
    /// Part 14 §7.3.4.4 MQTT connection properties</see>. MQTT 3.1.1
    /// (level <c>0x04</c>) and MQTT 5.0 (level <c>0x05</c>) are the two
    /// versions the spec mandates; the 3.1 level is included for
    /// completeness because some legacy brokers still negotiate it.
    /// </remarks>
    public enum MqttProtocolVersion
    {
        /// <summary>
        /// MQTT 3.1 — legacy.
        /// </summary>
        V310 = 3,

        /// <summary>
        /// MQTT 3.1.1 — broad broker compatibility, no ContentType
        /// support per Part 14 §7.3.4.4.
        /// </summary>
        V311 = 4,

        /// <summary>
        /// MQTT 5.0 — adds ContentType / ResponseTopic / user properties
        /// per Part 14 §7.3.4.4.
        /// </summary>
        V500 = 5
    }
}
