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
using System.Net;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Single inbound transport frame: the raw bytes received from
    /// the underlying socket / broker plus enough context to route
    /// the frame to the right decoder.
    /// </summary>
    /// <remarks>
    /// Implements the receive-side payload contract used by
    /// <see cref="IPubSubTransport.ReceiveAsync"/> as defined for
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. Designed as a
    /// value type so a transport's <see cref="System.Threading.Channels.Channel{T}"/>
    /// buffer does not allocate per inbound frame.
    /// </remarks>
    public readonly record struct PubSubTransportFrame
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubTransportFrame"/>.
        /// </summary>
        /// <param name="payload">The raw frame bytes as received.</param>
        /// <param name="topic">
        /// The MQTT topic the frame was delivered on, or
        /// <see langword="null"/> for UDP datagrams.
        /// </param>
        /// <param name="receivedAt">Receive-time stamp from the transport clock.</param>
        public PubSubTransportFrame(ReadOnlyMemory<byte> payload, string? topic, DateTimeUtc receivedAt)
        {
            Payload = payload;
            Topic = topic;
            ReceivedAt = receivedAt;
            SourceEndpoint = null;
        }

        /// <summary>
        /// Initializes a new <see cref="PubSubTransportFrame"/> carrying the datagram source endpoint.
        /// </summary>
        /// <param name="payload">The raw frame bytes as received.</param>
        /// <param name="topic">
        /// The MQTT topic the frame was delivered on, or
        /// <see langword="null"/> for UDP datagrams.
        /// </param>
        /// <param name="receivedAt">Receive-time stamp from the transport clock.</param>
        /// <param name="sourceEndpoint">
        /// The remote source endpoint the datagram was received from, or
        /// <see langword="null"/> when the transport does not expose it.
        /// </param>
        public PubSubTransportFrame(
            ReadOnlyMemory<byte> payload,
            string? topic,
            DateTimeUtc receivedAt,
            IPEndPoint? sourceEndpoint)
        {
            Payload = payload;
            Topic = topic;
            ReceivedAt = receivedAt;
            SourceEndpoint = sourceEndpoint;
        }

        /// <summary>
        /// Raw frame bytes as received from the transport. May be
        /// backed by a pooled buffer; consumers must complete decode
        /// before yielding control back to the transport loop.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; init; }

        /// <summary>
        /// MQTT topic the frame was delivered on, or
        /// <see langword="null"/> for UDP datagrams.
        /// </summary>
        public string? Topic { get; init; }

        /// <summary>
        /// Receive-time stamp taken from the transport's clock at the
        /// moment the frame entered the receive queue.
        /// </summary>
        public DateTimeUtc ReceivedAt { get; init; }

        /// <summary>
        /// Remote source endpoint the datagram was received from, or
        /// <see langword="null"/> when the transport does not expose it
        /// (for example broker transports). Used by the DTLS transport to
        /// bind a handshake flight and HelloRetryRequest cookie to the
        /// specific peer that sent each ClientHello.
        /// </summary>
        public IPEndPoint? SourceEndpoint { get; init; }
    }
}
