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
