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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

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
        private readonly Lock m_lock = new();
        private readonly Dictionary<ReassemblyKey, ReassemblyEntry> m_pending = [];

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
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_chunkTimeout = chunkTimeout ?? TimeSpan.FromSeconds(5);
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
            if (chunkOffset > totalSize ||
                chunkOffset + (uint)payload.Length > totalSize)
            {
                return false;
            }

            var key = new ReassemblyKey(publisherId, writerGroupId, sequenceNumber);
            long nowTicks = m_timeProvider.GetUtcNow().UtcTicks;

            lock (m_lock)
            {
                GarbageCollect(nowTicks);

                if (!m_pending.TryGetValue(key, out ReassemblyEntry? entry))
                {
                    entry = new ReassemblyEntry((int)totalSize, nowTicks);
                    m_pending[key] = entry;
                }
                else if (entry.Buffer.Length != (int)totalSize)
                {
                    m_pending.Remove(key);
                    return false;
                }

                if (entry.HasOverlap((int)chunkOffset, payload.Length))
                {
                    return false;
                }

                payload.Span.CopyTo(entry.Buffer.AsSpan((int)chunkOffset));
                entry.MarkReceived((int)chunkOffset, payload.Length);

                if (entry.IsComplete)
                {
                    m_pending.Remove(key);
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
                m_pending.Remove(key);
            }
            return expired.Count;
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
                return WriterGroupId == other.WriterGroupId
                    && SequenceNumber == other.SequenceNumber
                    && PublisherId.Equals(other.PublisherId);
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
                foreach ((int Offset, int Length) existing in m_chunks)
                {
                    int existingEnd = existing.Offset + existing.Length;
                    int newEnd = offset + length;
                    if (offset < existingEnd && existing.Offset < newEnd)
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
