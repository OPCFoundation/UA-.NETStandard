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

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// A single PubSub transport frame lifted out of a live capture tap or
    /// a replayed pcap. Carries the raw wire bytes (one UDP datagram or one
    /// MQTT application message payload) plus the metadata an offline
    /// dissector needs.
    /// </summary>
    /// <remarks>
    /// Unlike the UA-SC <c>CaptureFrame</c>, a PubSub frame is a complete,
    /// self-contained NetworkMessage rather than a secure-channel chunk:
    /// PubSub is connectionless and message-secured (Part 14 §7.3 / §8.3).
    /// <see cref="Data"/> may be backed by a pooled buffer; consumers must
    /// not mutate it and must not keep references after the enclosing
    /// pipeline returns.
    /// </remarks>
    public readonly struct PubSubCaptureFrame : IEquatable<PubSubCaptureFrame>
    {
        /// <summary>
        /// Constructs a new PubSub capture frame.
        /// </summary>
        /// <param name="timestamp">Capture timestamp.</param>
        /// <param name="direction">Direction relative to the local node.</param>
        /// <param name="transportProfileUri">Transport profile URI.</param>
        /// <param name="data">Raw NetworkMessage bytes.</param>
        /// <param name="endpoint">
        /// Wire endpoint the frame was sent to / received from, or
        /// <see langword="null"/>.
        /// </param>
        /// <param name="topic">MQTT topic, or <see langword="null"/> for UDP.</param>
        public PubSubCaptureFrame(
            DateTimeOffset timestamp,
            PubSubCaptureDirection direction,
            string transportProfileUri,
            ReadOnlyMemory<byte> data,
            string? endpoint = null,
            string? topic = null)
        {
            Timestamp = timestamp;
            Direction = direction;
            TransportProfileUri = transportProfileUri ?? string.Empty;
            Data = data;
            Endpoint = endpoint;
            Topic = topic;
        }

        /// <summary>
        /// The capture timestamp.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Direction of the frame relative to the local node.
        /// </summary>
        public PubSubCaptureDirection Direction { get; }

        /// <summary>
        /// Transport profile URI the frame was observed on.
        /// </summary>
        public string TransportProfileUri { get; }

        /// <summary>
        /// The raw NetworkMessage bytes (one UDP datagram or one MQTT
        /// application-message payload).
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// Wire endpoint the frame was sent to / received from, or
        /// <see langword="null"/> when unavailable.
        /// </summary>
        public string? Endpoint { get; }

        /// <summary>
        /// MQTT topic the frame was delivered on, or <see langword="null"/>
        /// for UDP datagrams.
        /// </summary>
        public string? Topic { get; }

        /// <inheritdoc/>
        public bool Equals(PubSubCaptureFrame other)
        {
            return Timestamp.Equals(other.Timestamp) &&
                Direction == other.Direction &&
                string.Equals(TransportProfileUri, other.TransportProfileUri, StringComparison.Ordinal) &&
                string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal) &&
                string.Equals(Topic, other.Topic, StringComparison.Ordinal) &&
                Data.Span.SequenceEqual(other.Data.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is PubSubCaptureFrame other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Timestamp,
                (int)Direction,
                TransportProfileUri,
                Endpoint,
                Topic,
                Data.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(PubSubCaptureFrame left, PubSubCaptureFrame right)
            => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(PubSubCaptureFrame left, PubSubCaptureFrame right)
            => !left.Equals(right);
    }
}
