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
