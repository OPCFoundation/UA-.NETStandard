// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
