// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Event payload raised by an
    /// <c>IMqttClientAdapter</c> whenever a fresh application
    /// message arrives from the broker.
    /// </summary>
    /// <remarks>
    /// Implements the receive-side notification surface used by the
    /// MQTT broker transport defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>.
    /// </remarks>
    public sealed class MqttIncomingMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="MqttIncomingMessageEventArgs"/>.
        /// </summary>
        /// <param name="message">The incoming MQTT message.</param>
        /// <param name="receivedAt">
        /// Receive-time stamp from the transport clock.
        /// </param>
        public MqttIncomingMessageEventArgs(MqttMessage message, DateTimeUtc receivedAt)
        {
            Message = message;
            ReceivedAt = receivedAt;
        }

        /// <summary>
        /// The MQTT message delivered to the adapter.
        /// </summary>
        public MqttMessage Message { get; }

        /// <summary>
        /// Receive-time stamp captured by the adapter when the message
        /// was first observed (used downstream for chunk reassembly
        /// timeouts and diagnostic clocks).
        /// </summary>
        public DateTimeUtc ReceivedAt { get; }
    }
}
