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
 *
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
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Resource limits for <see cref="UadpReassembler"/>.
    /// </summary>
    public sealed class UadpReassemblerOptions
    {
        /// <summary>
        /// Default maximum reassembled UADP NetworkMessage size, in bytes.
        /// </summary>
        public const int DefaultMaxReassembledMessageSize = 8 * 1024 * 1024;

        /// <summary>
        /// Default maximum number of concurrent pending reassemblies.
        /// </summary>
        public const int DefaultMaxConcurrentReassemblies = 1024;

        /// <summary>
        /// Default maximum aggregate bytes reserved by pending reassemblies.
        /// </summary>
        public const long DefaultMaxAggregatePendingBytes = 64L * 1024 * 1024;

        /// <summary>
        /// Default maximum time a pending entry can wait for missing chunks.
        /// </summary>
        public static readonly TimeSpan DefaultChunkTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Maximum reassembled UADP NetworkMessage size, in bytes.
        /// Defaults to 8 MiB, which is well above typical UDP PubSub MTU-sized
        /// traffic while bounding unauthenticated allocation.
        /// </summary>
        public int MaxReassembledMessageSize { get; set; } =
            DefaultMaxReassembledMessageSize;

        /// <summary>
        /// Maximum number of concurrent incomplete reassembly contexts.
        /// </summary>
        public int MaxConcurrentReassemblies { get; set; } =
            DefaultMaxConcurrentReassemblies;

        /// <summary>
        /// Maximum aggregate bytes reserved by incomplete reassemblies.
        /// Defaults to 64 MiB.
        /// </summary>
        public long MaxAggregatePendingBytes { get; set; } =
            DefaultMaxAggregatePendingBytes;

        /// <summary>
        /// Maximum time a pending entry can wait for missing chunks before
        /// being garbage-collected. Defaults to 5 seconds.
        /// </summary>
        public TimeSpan ChunkTimeout { get; set; } = DefaultChunkTimeout;
    }

    /// <summary>
    /// Time-to-live bounded reassembler for UADP ChunkMessages. Tracks
    /// in-flight chunk sets keyed by
    /// <c>(PublisherId, WriterGroupId, MessageSequenceNumber)</c>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
    /// Part 14 §7.2.4.4.4 ChunkedNetworkMessage</see>. Duplicate
    /// chunks are silently discarded; chunks whose
    /// <c>TotalSize</c> conflicts with prior chunks of the same key
    /// are rejected. Reassembly state expires according to the
    /// configured <see cref="TimeSpan"/> measured against the
    /// supplied <see cref="TimeProvider"/>.
    /// </remarks>
    public sealed class UadpReassembler : IDisposable
    {
        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_chunkTimeout;
        private readonly int m_maxReassembledMessageSize;
        private readonly int m_maxConcurrentReassemblies;
        private readonly long m_maxAggregatePendingBytes;
        private readonly Lock m_lock = new();
        private readonly Dictionary<ReassemblyKey, ReassemblyEntry> m_pending = [];
        private long m_pendingBytes;

        /// <summary>
        /// Creates a new reassembler.
        /// </summary>
        /// <param name="timeProvider">Provider for timestamps used in
        /// the TTL check. Defaults to <see cref="TimeProvider.System"/>
        /// when <c>null</c>.</param>
        /// <param name="chunkTimeout">Maximum time a pending entry
        /// can wait for missing chunks before being garbage-collected.
        /// Defaults to 5 seconds when not specified.</param>
        public UadpReassembler(
            TimeProvider? timeProvider = null,
            TimeSpan? chunkTimeout = null)
            : this(CreateOptions(chunkTimeout), timeProvider)
        {
        }

        /// <summary>
        /// Creates a new reassembler.
        /// </summary>
        /// <param name="options">Resource limits. Defaults are used when
        /// <c>null</c>.</param>
        /// <param name="timeProvider">Provider for timestamps used in the TTL
        /// check. Defaults to <see cref="TimeProvider.System"/> when
        /// <c>null</c>.</param>
        public UadpReassembler(
            UadpReassemblerOptions? options,
            TimeProvider? timeProvider = null)
        {
            options ??= new UadpReassemblerOptions();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_chunkTimeout = options.ChunkTimeout;
            m_maxReassembledMessageSize = NormalizePositive(
                options.MaxReassembledMessageSize,
                UadpReassemblerOptions.DefaultMaxReassembledMessageSize);
            m_maxConcurrentReassemblies = NormalizePositive(
                options.MaxConcurrentReassemblies,
                UadpReassemblerOptions.DefaultMaxConcurrentReassemblies);
            m_maxAggregatePendingBytes = NormalizePositive(
                options.MaxAggregatePendingBytes,
                UadpReassemblerOptions.DefaultMaxAggregatePendingBytes);
        }

        /// <summary>
        /// Creates a new reassembler.
        /// </summary>
        /// <param name="options">DI-provided resource limits. Defaults are used
        /// when <c>null</c>.</param>
        /// <param name="timeProvider">Provider for timestamps used in the TTL
        /// check. Defaults to <see cref="TimeProvider.System"/> when
        /// <c>null</c>.</param>
        public UadpReassembler(
            IOptions<UadpReassemblerOptions>? options,
            TimeProvider? timeProvider = null)
            : this(options?.Value ?? new UadpReassemblerOptions(), timeProvider)
        {
        }

        /// <summary>
        /// Number of in-flight reassembly contexts.
        /// </summary>
        public int PendingCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_pending.Count;
                }
            }
        }

        /// <summary>
        /// Adds a chunk to the reassembly buffer and returns the full
        /// message bytes once all chunks have arrived.
        /// </summary>
        /// <param name="publisherId">PublisherId of the source
        /// NetworkMessage as decoded from the common header.</param>
        /// <param name="writerGroupId">WriterGroupId of the source
        /// NetworkMessage as decoded from the group header. Use 0
        /// when the GroupHeader did not carry a WriterGroupId.</param>
        /// <param name="chunk">The chunk frame including the 10-byte
        /// chunk header.</param>
        /// <param name="reassembled">When the method returns
        /// <c>true</c> contains the reassembled bytes; otherwise
        /// <c>null</c>.</param>
        /// <returns><c>true</c> when the chunk completed a message;
        /// <c>false</c> when more chunks are required, the chunk was
        /// a duplicate or the chunk was rejected.</returns>
        public bool TryAddChunk(
            PublisherId publisherId,
            ushort writerGroupId,
            ReadOnlyMemory<byte> chunk,
            out ReadOnlyMemory<byte>? reassembled)
        {
            reassembled = null;

            if (!UadpChunker.TryParseChunk(chunk, out ushort sequenceNumber,
                out uint chunkOffset, out uint totalSize,
                out ReadOnlyMemory<byte> payload))
            {
                return false;
            }
            if (totalSize == 0 || payload.Length == 0)
            {
                return false;
            }
            if (!TryGetBoundedTotalSize(totalSize, payload.Length, out int totalSizeInt))
            {
                return false;
            }
            if (chunkOffset > totalSize ||
                (ulong)chunkOffset + (uint)payload.Length > totalSize)
            {
                return false;
            }

            int chunkOffsetInt = (int)chunkOffset;
            var key = new ReassemblyKey(publisherId, writerGroupId, sequenceNumber);
            long nowTicks = m_timeProvider.GetUtcNow().UtcTicks;

            lock (m_lock)
            {
                GarbageCollect(nowTicks);

                if (!m_pending.TryGetValue(key, out ReassemblyEntry? entry))
                {
                    if (m_pending.Count >= m_maxConcurrentReassemblies ||
                        m_pendingBytes + totalSizeInt > m_maxAggregatePendingBytes)
                    {
                        return false;
                    }

                    entry = new ReassemblyEntry(totalSizeInt, nowTicks);
                    m_pending[key] = entry;
                    m_pendingBytes += totalSizeInt;
                }
                else if (entry.Buffer.Length != totalSizeInt)
                {
                    RemovePending(key, entry);
                    return false;
                }

                if (entry.HasOverlap(chunkOffsetInt, payload.Length))
                {
                    return false;
                }

                payload.Span.CopyTo(entry.Buffer.AsSpan(chunkOffsetInt));
                entry.MarkReceived(chunkOffsetInt, payload.Length);

                if (entry.IsComplete)
                {
                    RemovePending(key, entry);
                    reassembled = entry.Buffer;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes any reassembly contexts whose age exceeds the
        /// configured timeout, and returns the count discarded.
        /// </summary>
        public int Sweep()
        {
            long nowTicks = m_timeProvider.GetUtcNow().UtcTicks;
            lock (m_lock)
            {
                return GarbageCollect(nowTicks);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_pending.Clear();
                m_pendingBytes = 0;
            }
        }

        private int GarbageCollect(long nowTicks)
        {
            long timeoutTicks = m_chunkTimeout.Ticks;
            if (timeoutTicks <= 0 || m_pending.Count == 0)
            {
                return 0;
            }

            List<ReassemblyKey>? expired = null;
            foreach (KeyValuePair<ReassemblyKey, ReassemblyEntry> kvp in m_pending)
            {
                if (nowTicks - kvp.Value.CreatedAtTicks > timeoutTicks)
                {
                    expired ??= [];
                    expired.Add(kvp.Key);
                }
            }
            if (expired is null)
            {
                return 0;
            }
            foreach (ReassemblyKey key in expired)
            {
                if (m_pending.TryGetValue(key, out ReassemblyEntry? entry))
                {
                    RemovePending(key, entry);
                }
            }
            return expired.Count;
        }

        private bool TryGetBoundedTotalSize(
            uint totalSize,
            int payloadLength,
            out int totalSizeInt)
        {
            totalSizeInt = 0;
            if (totalSize > int.MaxValue ||
                totalSize > (uint)m_maxReassembledMessageSize ||
                totalSize < (uint)payloadLength)
            {
                return false;
            }

            totalSizeInt = (int)totalSize;
            return true;
        }

        private void RemovePending(ReassemblyKey key, ReassemblyEntry entry)
        {
            if (m_pending.Remove(key))
            {
                m_pendingBytes -= entry.Buffer.Length;
                if (m_pendingBytes < 0)
                {
                    m_pendingBytes = 0;
                }
            }
        }

        private static UadpReassemblerOptions CreateOptions(TimeSpan? chunkTimeout)
        {
            return new UadpReassemblerOptions
            {
                ChunkTimeout = chunkTimeout ?? UadpReassemblerOptions.DefaultChunkTimeout
            };
        }

        private static int NormalizePositive(int value, int defaultValue)
        {
            return value > 0 ? value : defaultValue;
        }

        private static long NormalizePositive(long value, long defaultValue)
        {
            return value > 0 ? value : defaultValue;
        }

        private readonly struct ReassemblyKey : IEquatable<ReassemblyKey>
        {
            public ReassemblyKey(
                PublisherId publisherId,
                ushort writerGroupId,
                ushort sequenceNumber)
            {
                PublisherId = publisherId;
                WriterGroupId = writerGroupId;
                SequenceNumber = sequenceNumber;
            }

            public PublisherId PublisherId { get; }

            public ushort WriterGroupId { get; }

            public ushort SequenceNumber { get; }

            public bool Equals(ReassemblyKey other)
            {
                return WriterGroupId == other.WriterGroupId &&
                    SequenceNumber == other.SequenceNumber &&
                    PublisherId.Equals(other.PublisherId);
            }

            public override bool Equals(object? obj)
            {
                return obj is ReassemblyKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    PublisherId, WriterGroupId, SequenceNumber);
            }
        }

        private sealed class ReassemblyEntry
        {
            private readonly List<(int Offset, int Length)> m_chunks = [];

            public ReassemblyEntry(int totalSize, long createdAtTicks)
            {
                Buffer = new byte[totalSize];
                CreatedAtTicks = createdAtTicks;
            }

            public byte[] Buffer { get; }

            public long CreatedAtTicks { get; }

            public int Received { get; private set; }

            public bool IsComplete => Received == Buffer.Length;

            public bool HasOverlap(int offset, int length)
            {
                foreach ((int Offset, int Length) in m_chunks)
                {
                    int existingEnd = Offset + Length;
                    int newEnd = offset + length;
                    if (offset < existingEnd && Offset < newEnd)
                    {
                        return true;
                    }
                }
                return false;
            }

            public void MarkReceived(int offset, int length)
            {
                m_chunks.Add((offset, length));
                Received += length;
            }
        }
    }
}
