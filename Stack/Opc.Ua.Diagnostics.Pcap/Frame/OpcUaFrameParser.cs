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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;

namespace Opc.Ua.Diagnostics.Pcap.Frame
{
    /// <summary>
    /// Splits TCP byte streams into discrete OPC UA UA-SC chunks.
    /// </summary>
    public sealed class OpcUaFrameParser
    {
        /// <summary>
        /// Constructs an OPC UA frame parser.
        /// </summary>
        public OpcUaFrameParser(ILogger<OpcUaFrameParser>? logger = null)
        {
            m_logger = logger ?? NullLogger<OpcUaFrameParser>.Instance;
        }

        /// <summary>
        /// Processes one TCP segment and yields all complete OPC UA chunks that become available.
        /// </summary>
        public IEnumerable<OpcUaChunk> Process(TcpFlowSegment segment)
        {
            if (segment.Data.IsEmpty)
            {
                yield break;
            }

            FlowBuffer buffer = GetBuffer(segment.FlowKey);
            buffer.Append(segment.Data.Span);
            while (buffer.Length >= HeaderLength)
            {
                ReadOnlySpan<byte> span = buffer.WrittenSpan;
                uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(span);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                if (TcpMessageType.IsValid(messageType) && size >= HeaderLength && size <= MaxChunkSize)
                {
                    if (buffer.Length < size)
                    {
                        yield break;
                    }

                    byte[] chunk = span[..checked((int)size)].ToArray();
                    buffer.Consume(checked((int)size));
                    yield return new OpcUaChunk(
                        segment.Timestamp,
                        segment.FlowKey,
                        segment.SourceEndpoint,
                        segment.DestinationEndpoint,
                        messageType,
                        IsClientToServer(segment, messageType),
                        chunk);
                    continue;
                }

                int resyncOffset = FindNextValidStart(span[1..]);
                int consume = resyncOffset < 0 ? Math.Max(1, buffer.Length - HeaderLength + 1) : resyncOffset + 1;
                m_logger.LogWarning("Skipped {ByteCount} bytes while resynchronizing OPC UA chunk stream.", consume);
                buffer.Consume(consume);
            }
        }

        private FlowBuffer GetBuffer(string flowKey)
        {
            if (!m_buffers.TryGetValue(flowKey, out FlowBuffer? buffer))
            {
                buffer = new FlowBuffer();
                m_buffers.Add(flowKey, buffer);
            }
            return buffer;
        }

        private static bool IsClientToServer(TcpFlowSegment segment, uint messageType)
        {
            if (messageType == TcpMessageType.Hello)
            {
                return true;
            }
            if (messageType == TcpMessageType.Acknowledge)
            {
                return false;
            }
            return IsOpcUaServerEndpoint(segment.DestinationEndpoint);
        }

        private static bool IsOpcUaServerEndpoint(string endpoint)
        {
            int separator = endpoint.LastIndexOf(':');
            if (separator < 0 || !ushort.TryParse(endpoint[(separator + 1)..], out ushort port))
            {
                return false;
            }
            return port == 4840 || port is >= 48010 and <= 48020;
        }

        private static int FindNextValidStart(ReadOnlySpan<byte> span)
        {
            for (int index = 0; index + HeaderLength <= span.Length; index++)
            {
                uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(span[index..]);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(span[(index + 4)..]);
                if (TcpMessageType.IsValid(messageType) && size >= HeaderLength && size <= MaxChunkSize)
                {
                    return index;
                }
            }
            return -1;
        }

        private const int HeaderLength = 8;
        private const uint MaxChunkSize = 64U * 1024U * 1024U;
        private readonly ILogger<OpcUaFrameParser> m_logger;
        private readonly Dictionary<string, FlowBuffer> m_buffers = [];

        private sealed class FlowBuffer
        {
            public int Length { get; private set; }

            public ReadOnlySpan<byte> WrittenSpan => m_buffer.AsSpan(0, Length);

            public void Append(ReadOnlySpan<byte> data)
            {
                EnsureCapacity(Length + data.Length);
                data.CopyTo(m_buffer.AsSpan(Length));
                Length += data.Length;
            }

            public void Consume(int count)
            {
                if (count >= Length)
                {
                    Length = 0;
                    return;
                }

                m_buffer.AsSpan(count, Length - count).CopyTo(m_buffer);
                Length -= count;
            }

            private void EnsureCapacity(int capacity)
            {
                if (capacity <= m_buffer.Length)
                {
                    return;
                }

                int newLength = Math.Max(capacity, m_buffer.Length * 2);
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newLength);
                m_buffer.AsSpan(0, Length).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(m_buffer);
                m_buffer = newBuffer;
            }

            private byte[] m_buffer = ArrayPool<byte>.Shared.Rent(8192);
        }
    }

    /// <summary>
    /// A complete OPC UA UA-SC chunk extracted from a TCP byte stream.
    /// </summary>
    public readonly struct OpcUaChunk : IEquatable<OpcUaChunk>
    {
        /// <summary>
        /// Constructs an OPC UA chunk record.
        /// </summary>
        public OpcUaChunk(
            DateTimeOffset timestamp,
            string flowKey,
            string sourceEndpoint,
            string destinationEndpoint,
            uint messageType,
            bool isClientToServer,
            ReadOnlyMemory<byte> data)
        {
            Timestamp = timestamp;
            FlowKey = flowKey;
            SourceEndpoint = sourceEndpoint;
            DestinationEndpoint = destinationEndpoint;
            MessageType = messageType;
            IsClientToServer = isClientToServer;
            Data = data;
        }

        /// <summary>Chunk timestamp.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>TCP flow key.</summary>
        public string FlowKey { get; }

        /// <summary>Source endpoint.</summary>
        public string SourceEndpoint { get; }

        /// <summary>Destination endpoint.</summary>
        public string DestinationEndpoint { get; }

        /// <summary>OPC UA TCP message type.</summary>
        public uint MessageType { get; }

        /// <summary>Whether this chunk appears to flow from client to server.</summary>
        public bool IsClientToServer { get; }

        /// <summary>Full chunk bytes including the 8-byte UA-SC header.</summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <inheritdoc/>
        public bool Equals(OpcUaChunk other)
        {
            return Timestamp.Equals(other.Timestamp)
                && string.Equals(FlowKey, other.FlowKey, StringComparison.Ordinal)
                && string.Equals(SourceEndpoint, other.SourceEndpoint, StringComparison.Ordinal)
                && string.Equals(DestinationEndpoint, other.DestinationEndpoint, StringComparison.Ordinal)
                && MessageType == other.MessageType
                && IsClientToServer == other.IsClientToServer
                && Data.Span.SequenceEqual(other.Data.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is OpcUaChunk other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Timestamp,
                FlowKey,
                SourceEndpoint,
                DestinationEndpoint,
                MessageType,
                IsClientToServer,
                Data.Length);
        }

        /// <summary>
        /// Equality comparison.
        /// </summary>
        public static bool operator ==(OpcUaChunk left, OpcUaChunk right) => left.Equals(right);

        /// <summary>
        /// Inequality comparison.
        /// </summary>
        public static bool operator !=(OpcUaChunk left, OpcUaChunk right) => !left.Equals(right);
    }
}
