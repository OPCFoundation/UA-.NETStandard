/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Optional host authority at the actual MQTT subscription and publication boundary.
    /// Implementations must be safe for background transport callbacks.
    /// </summary>
    public interface IMqttTopicPolicy
    {
        /// <summary>
        /// Refuses an unauthorized subscription before it is sent to the broker.
        /// </summary>
        void RequireSubscribe(string topic);

        /// <summary>
        /// Refuses an unauthorized topic or oversized publication, including a last will.
        /// </summary>
        void RequirePublish(string topic, int payloadLength);

        /// <summary>
        /// Filters an unsolicited or oversized incoming publication before buffering it.
        /// </summary>
        bool AcceptReceive(string topic, int payloadLength);
    }
}
