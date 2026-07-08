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

namespace Opc.Ua.Pcap.Frame
{
    /// <summary>
    /// Direction of a captured OPC UA chunk relative to the client (the
    /// peer that initiated the secure channel). <see cref="ClientToServer"/>
    /// chunks were sent by the client; <see cref="ServerToClient"/> chunks
    /// were sent by the server. The direction is needed for offline
    /// decryption because client- and server-sent symmetric messages use
    /// different derived key material.
    /// </summary>
    public enum CaptureFrameDirection
    {
        /// <summary>
        /// Direction not yet determined.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Sent by the client (uses ClientSigningKey / ClientEncryptingKey).
        /// </summary>
        ClientToServer = 1,

        /// <summary>
        /// Sent by the server (uses ServerSigningKey / ServerEncryptingKey).
        /// </summary>
        ServerToClient = 2
    }

    /// <summary>
    /// A single OPC UA UA-SC binary chunk lifted out of a pcap or in-proc
    /// tap. Carries the raw wire bytes plus the metadata needed to feed
    /// the offline decoder (timestamp, direction, peer endpoints).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chunk includes the 8-byte OPC UA message header (type + size),
    /// so a consumer can decide whether it is HEL/ACK/ERR/RHE/OPN/MSG/CLO
    /// without re-parsing the header out of the byte stream. Multi-chunk
    /// messages (intermediate + final) are surfaced as multiple
    /// <see cref="CaptureFrame"/> records; assembly into full messages is
    /// the job of the offline decoder.
    /// </para>
    /// <para>
    /// <see cref="Data"/> is owned by the producer and is generally backed
    /// by a pooled buffer. Consumers must not mutate it and must not keep
    /// references after the enclosing pipeline returns.
    /// </para>
    /// </remarks>
    public readonly struct CaptureFrame : IEquatable<CaptureFrame>
    {
        /// <summary>
        /// Constructs a new capture frame.
        /// </summary>
        public CaptureFrame(
            DateTimeOffset timestamp,
            CaptureFrameDirection direction,
            string clientEndpoint,
            string serverEndpoint,
            ReadOnlyMemory<byte> data)
        {
            Timestamp = timestamp;
            Direction = direction;
            ClientEndpoint = clientEndpoint ?? string.Empty;
            ServerEndpoint = serverEndpoint ?? string.Empty;
            Data = data;
        }

        /// <summary>
        /// The capture timestamp.
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// The direction of the chunk relative to the secure channel.
        /// </summary>
        public CaptureFrameDirection Direction { get; }

        /// <summary>
        /// String identifier for the client endpoint, e.g.
        /// <c>192.0.2.10:54321</c>. Empty if not available (in-proc tap).
        /// </summary>
        public string ClientEndpoint { get; }

        /// <summary>
        /// String identifier for the server endpoint, e.g.
        /// <c>192.0.2.20:62541</c>. Empty if not available.
        /// </summary>
        public string ServerEndpoint { get; }

        /// <summary>
        /// The raw chunk bytes, including the 8-byte OPC UA message
        /// header.
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <inheritdoc/>
        public bool Equals(CaptureFrame other)
        {
            return Timestamp.Equals(other.Timestamp) &&
                Direction == other.Direction &&
                string.Equals(ClientEndpoint, other.ClientEndpoint, StringComparison.Ordinal) &&
                string.Equals(ServerEndpoint, other.ServerEndpoint, StringComparison.Ordinal) &&
                Data.Span.SequenceEqual(other.Data.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is CaptureFrame other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Timestamp,
                (int)Direction,
                ClientEndpoint,
                ServerEndpoint,
                Data.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(CaptureFrame left, CaptureFrame right) => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(CaptureFrame left, CaptureFrame right) => !left.Equals(right);
    }
}
