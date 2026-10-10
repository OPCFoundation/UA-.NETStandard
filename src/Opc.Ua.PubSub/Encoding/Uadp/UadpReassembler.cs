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
        /// Default maximum number of chunks of one reassembled message.
        /// </summary>
        public const int DefaultMaxChunksPerMessage = 16384;

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
        /// When the limit is reached the oldest incomplete reassembly is
        /// evicted to make room for a new one.
        /// </summary>
        public int MaxConcurrentReassemblies { get; set; } =
            DefaultMaxConcurrentReassemblies;

        /// <summary>
        /// Maximum aggregate bytes held by incomplete reassemblies (the
        /// received chunk bytes plus a fixed bookkeeping charge per chunk).
        /// When the limit is reached the oldest incomplete reassemblies are
        /// evicted. Defaults to 64 MiB.
        /// </summary>
        public long MaxAggregatePendingBytes { get; set; } =
            DefaultMaxAggregatePendingBytes;

        /// <summary>
        /// Maximum number of chunks one message may be split into
        /// (<c>ceiling(TotalSize / chunk size)</c>). Bounds the bookkeeping
        /// an unauthenticated sender can create with tiny chunks.
        /// </summary>
        public int MaxChunksPerMessage { get; set; } = DefaultMaxChunksPerMessage;

        /// <summary>
        /// Maximum time a pending entry can wait for missing chunks before
        /// being garbage-collected. Defaults to 5 seconds.
        /// </summary>
        public TimeSpan ChunkTimeout { get; set; } = DefaultChunkTimeout;
    }

    /// <summary>
    /// Time-to-live bounded reassembler for UADP chunk NetworkMessages.
    /// Tracks in-flight chunk sets keyed by
    /// <c>(PublisherId, WriterGroupId, DataSetWriterId, MessageSequenceNumber)</c>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
    /// Part 14 §7.2.4.4.4 UADP Chunk NetworkMessage</see>. All chunks
    /// except the last one have the same size, so every chunk maps to one
    /// slot and duplicate or overlapping chunks are detected in O(1).
    /// Chunks violating that layout, duplicates and chunks whose
    /// <c>TotalSize</c> conflicts with a pending reassembly are discarded
    /// without touching the pending state. Memory is charged per received
    /// chunk rather than per advertised <c>TotalSize</c>, and the oldest
    /// incomplete reassemblies are evicted when a limit is reached, so
    /// spoofed first chunks can neither pin large buffers nor starve
    /// legitimate traffic. Reassembly state expires according to the
    /// configured <see cref="TimeSpan"/> measured against the supplied
    /// <see cref="TimeProvider"/>.
    /// </remarks>
    public sealed class UadpReassembler : IDisposable
    {
        /// <summary>
        /// Fixed bookkeeping charge per stored chunk, on top of its bytes.
        /// </summary>
        internal const int ChunkBookkeepingBytes = 64;

        private readonly TimeProvider m_timeProvider;
        private readonly TimeSpan m_chunkTimeout;
        private readonly int m_maxReassembledMessageSize;
        private readonly int m_maxConcurrentReassemblies;
        private readonly long m_maxAggregatePendingBytes;
        private readonly int m_maxChunksPerMessage;
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
            m_maxChunksPerMessage = NormalizePositive(
                options.MaxChunksPerMessage,
                UadpReassemblerOptions.DefaultMaxChunksPerMessage);
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
        /// Adds a chunk without a DataSetWriterId (for example a chunk of a
        /// discovery announcement, which has no PayloadHeader) to the
        /// reassembly buffer.
        /// </summary>
        /// <param name="publisherId">PublisherId of the chunk
        /// NetworkMessage.</param>
        /// <param name="writerGroupId">WriterGroupId of the chunk
        /// NetworkMessage (0 when the GroupHeader carried none).</param>
        /// <param name="chunk">The chunk payload fields of Part 14
        /// Table 159 (MessageSequenceNumber, ChunkOffset, TotalSize,
        /// ChunkData).</param>
        /// <param name="reassembled">When the method returns
        /// <c>true</c> contains the reassembled payload; otherwise
        /// <c>null</c>.</param>
        /// <returns><c>true</c> when the chunk completed a payload.</returns>
        public bool TryAddChunk(
            PublisherId publisherId,
            ushort writerGroupId,
            ReadOnlyMemory<byte> chunk,
            out ReadOnlyMemory<byte>? reassembled)
        {
            return TryAddChunk(publisherId, writerGroupId, null, chunk, out reassembled);
        }

        /// <summary>
        /// Adds a chunk to the reassembly buffer and returns the complete
        /// payload once all chunks have arrived.
        /// </summary>
        /// <param name="publisherId">PublisherId of the chunk
        /// NetworkMessage as decoded from the common header.</param>
        /// <param name="writerGroupId">WriterGroupId of the chunk
        /// NetworkMessage as decoded from the group header. Use 0
        /// when the GroupHeader did not carry a WriterGroupId.</param>
        /// <param name="dataSetWriterId">DataSetWriterId of the chunk
        /// PayloadHeader (Part 14 Table 158), <c>null</c> when the chunk
        /// NetworkMessage has no PayloadHeader.</param>
        /// <param name="chunk">The chunk payload fields of Part 14
        /// Table 159 (MessageSequenceNumber, ChunkOffset, TotalSize,
        /// ChunkData).</param>
        /// <param name="reassembled">When the method returns
        /// <c>true</c> contains the reassembled payload; otherwise
        /// <c>null</c>.</param>
        /// <returns><c>true</c> when the chunk completed a payload;
        /// <c>false</c> when more chunks are required, the chunk was
        /// a duplicate or the chunk was rejected.</returns>
        public bool TryAddChunk(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort? dataSetWriterId,
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

            int offset = (int)chunkOffset;
            bool isLast = offset + payload.Length == totalSizeInt;
            var key = new ReassemblyKey(
                publisherId, writerGroupId, dataSetWriterId, sequenceNumber);
            long nowTicks = m_timeProvider.GetUtcNow().UtcTicks;
            long charge = (long)payload.Length + ChunkBookkeepingBytes;

            lock (m_lock)
            {
                GarbageCollect(nowTicks);

                if (!m_pending.TryGetValue(key, out ReassemblyEntry? entry))
                {
                    if (offset == 0 && isLast)
                    {
                        // A payload that fits into one chunk needs no state.
                        reassembled = payload.ToArray();
                        return true;
                    }
                    entry = new ReassemblyEntry(totalSizeInt, nowTicks);
                    if (!entry.CanAccept(offset, payload.Length, isLast, m_maxChunksPerMessage) ||
                        !TryMakeRoom(charge, newEntry: true, exclude: null))
                    {
                        return false;
                    }
                    m_pending[key] = entry;
                }
                else if (entry.TotalSize != totalSizeInt ||
                    !entry.CanAccept(offset, payload.Length, isLast, m_maxChunksPerMessage) ||
                    !TryMakeRoom(charge, newEntry: false, exclude: entry))
                {
                    // Conflicting TotalSize, layout violation, duplicate or
                    // no room: the unauthenticated chunk is dropped and the
                    // pending reassembly is left untouched.
                    return false;
                }

                entry.Store(offset, payload.Span, isLast);
                entry.ChargedBytes += charge;
                m_pendingBytes += charge;

                if (entry.IsComplete)
                {
                    RemovePending(key, entry);
                    reassembled = entry.Assemble();
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

        /// <summary>
        /// Evicts the oldest incomplete reassemblies (other than
        /// <paramref name="exclude"/>) until <paramref name="charge"/> more
        /// bytes, and for a new entry one more context, fit the limits.
        /// </summary>
        private bool TryMakeRoom(long charge, bool newEntry, ReassemblyEntry? exclude)
        {
            if (charge > m_maxAggregatePendingBytes)
            {
                return false;
            }
            while ((newEntry && m_pending.Count >= m_maxConcurrentReassemblies) ||
                m_pendingBytes > m_maxAggregatePendingBytes - charge)
            {
                ReassemblyKey oldestKey = default;
                ReassemblyEntry? oldest = null;
                foreach (KeyValuePair<ReassemblyKey, ReassemblyEntry> kvp in m_pending)
                {
                    if (!ReferenceEquals(kvp.Value, exclude) &&
                        (oldest is null || kvp.Value.CreatedAtTicks < oldest.CreatedAtTicks))
                    {
                        oldestKey = kvp.Key;
                        oldest = kvp.Value;
                    }
                }
                if (oldest is null)
                {
                    return false;
                }
                RemovePending(oldestKey, oldest);
            }
            return true;
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
                m_pendingBytes -= entry.ChargedBytes;
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
                ushort? dataSetWriterId,
                ushort sequenceNumber)
            {
                PublisherId = publisherId;
                WriterGroupId = writerGroupId;
                DataSetWriterId = dataSetWriterId;
                SequenceNumber = sequenceNumber;
            }

            public PublisherId PublisherId { get; }

            public ushort WriterGroupId { get; }

            public ushort? DataSetWriterId { get; }

            public ushort SequenceNumber { get; }

            public bool Equals(ReassemblyKey other)
            {
                return WriterGroupId == other.WriterGroupId &&
                    SequenceNumber == other.SequenceNumber &&
                    DataSetWriterId == other.DataSetWriterId &&
                    PublisherId.Equals(other.PublisherId);
            }

            public override bool Equals(object? obj)
            {
                return obj is ReassemblyKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    PublisherId, WriterGroupId, DataSetWriterId, SequenceNumber);
            }
        }

        /// <summary>
        /// Incomplete payload. The size of the first non-last chunk fixes
        /// the chunk size; a last chunk that arrives earlier is held until
        /// then. Received chunks are stored per slot
        /// (<c>ChunkOffset / chunk size</c>), so memory follows the bytes
        /// actually received.
        /// </summary>
        private sealed class ReassemblyEntry
        {
            private readonly Dictionary<int, byte[]> m_chunks = [];
            private int m_chunkSize;
            private int m_lastIndex;
            private int m_pendingLastOffset;
            private byte[]? m_pendingLast;

            public ReassemblyEntry(int totalSize, long createdAtTicks)
            {
                TotalSize = totalSize;
                CreatedAtTicks = createdAtTicks;
            }

            public int TotalSize { get; }

            public long CreatedAtTicks { get; }

            public long ChargedBytes { get; set; }

            public int Received { get; private set; }

            public bool IsComplete => Received == TotalSize;

            /// <summary>
            /// Checks the chunk against the Part 14 §7.2.4.4.4 layout ("All
            /// chunks, except for the last one shall have the same size")
            /// and rejects duplicates, without changing any state.
            /// </summary>
            public bool CanAccept(int offset, int length, bool isLast, int maxChunks)
            {
                if (m_chunkSize == 0)
                {
                    if (isLast)
                    {
                        return m_pendingLast is null;
                    }
                    int chunkSize = length;
                    if (offset % chunkSize != 0 ||
                        ChunkCount(chunkSize) > maxChunks)
                    {
                        return false;
                    }
                    return m_pendingLast is null ||
                        (m_pendingLastOffset % chunkSize == 0 &&
                        m_pendingLastOffset / chunkSize == ChunkCount(chunkSize) - 1);
                }

                if (offset % m_chunkSize != 0)
                {
                    return false;
                }
                int index = offset / m_chunkSize;
                if (isLast ? index != m_lastIndex : length != m_chunkSize)
                {
                    return false;
                }
                return !m_chunks.ContainsKey(index);
            }

            public void Store(int offset, ReadOnlySpan<byte> data, bool isLast)
            {
                if (m_chunkSize == 0)
                {
                    if (isLast)
                    {
                        m_pendingLastOffset = offset;
                        m_pendingLast = data.ToArray();
                        Received += data.Length;
                        return;
                    }
                    m_chunkSize = data.Length;
                    m_lastIndex = (int)(ChunkCount(m_chunkSize) - 1);
                    if (m_pendingLast is not null)
                    {
                        m_chunks[m_lastIndex] = m_pendingLast;
                        m_pendingLast = null;
                    }
                }
                m_chunks[offset / m_chunkSize] = data.ToArray();
                Received += data.Length;
            }

            public byte[] Assemble()
            {
                byte[] result = new byte[TotalSize];
                foreach (KeyValuePair<int, byte[]> chunk in m_chunks)
                {
                    Buffer.BlockCopy(
                        chunk.Value, 0, result, chunk.Key * m_chunkSize, chunk.Value.Length);
                }
                return result;
            }

            private long ChunkCount(int chunkSize)
            {
                return ((long)TotalSize + chunkSize - 1) / chunkSize;
            }
        }
    }
}
