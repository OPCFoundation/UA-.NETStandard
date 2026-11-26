// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
