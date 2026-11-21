// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Event payload raised by an <c>IKafkaClientAdapter</c> whenever a
    /// fresh record arrives from the broker.
    /// </summary>
    /// <remarks>
    /// Implements the receive-side notification surface used by the
    /// Kafka broker transport defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public sealed class KafkaIncomingMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="KafkaIncomingMessageEventArgs"/>.
        /// </summary>
        /// <param name="message">The incoming Kafka record.</param>
        /// <param name="receivedAt">
        /// Receive-time stamp from the transport clock.
        /// </param>
        public KafkaIncomingMessageEventArgs(KafkaMessage message, DateTimeUtc receivedAt)
        {
            Message = message;
            ReceivedAt = receivedAt;
        }

        /// <summary>
        /// The Kafka record delivered to the adapter.
        /// </summary>
        public KafkaMessage Message { get; }

        /// <summary>
        /// Receive-time stamp captured by the adapter when the record
        /// was first observed (used downstream for chunk reassembly
        /// timeouts and diagnostic clocks).
        /// </summary>
        public DateTimeUtc ReceivedAt { get; }
    }
}
