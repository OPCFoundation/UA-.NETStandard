// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Bounds allocations and cancellation while existing synchronous serializers write into memory.
    /// </summary>
    public sealed class McpBoundedMemoryStream : MemoryStream
    {
        /// <summary>
        /// Creates a buffer whose capacity can never grow beyond the content limit.
        /// </summary>
        public McpBoundedMemoryStream(long limit, CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(limit);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, int.MaxValue);
            m_limit = (int)limit;
            m_token = ct;
        }

        /// <inheritdoc/>
        public override int Capacity
        {
            get => base.Capacity;
            set
            {
                m_token.ThrowIfCancellationRequested();
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, m_limit);
                base.Capacity = value;
            }
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            _ = buffer.AsSpan(offset, count);
            Reserve(count);
            base.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reserve(buffer.Length);
            long end = Position + buffer.Length;
            byte[]? preserved = null;
            if (end > Length && buffer.Overlaps(GetBuffer().AsSpan((int)Length, (int)(end - Length))))
            {
                preserved = buffer.ToArray();
                buffer = preserved;
            }
            if (end > Length)
            {
                base.SetLength(end);
            }
            buffer.CopyTo(GetBuffer().AsSpan((int)Position, buffer.Length));
            Position = end;
            GC.KeepAlive(preserved);
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            Reserve(1);
            base.WriteByte(value);
        }

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            m_token.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            EnsureCapacity(value);
            base.SetLength(value);
        }

        /// <summary>
        /// Reserves the complete next write before MemoryStream can allocate or mutate its contents.
        /// </summary>
        private void Reserve(int count)
        {
            m_token.ThrowIfCancellationRequested();
            EnsureCapacity(Position + count);
        }

        /// <summary>
        /// Keeps MemoryStream's geometric capacity growth inside the same limit as its length.
        /// </summary>
        /// <exception cref="IOException"></exception>
        private void EnsureCapacity(long length)
        {
            if (length > m_limit)
            {
                throw new IOException("The document or artifact exceeds its configured byte budget.");
            }
            if (length > Capacity)
            {
                Capacity = (int)Math.Min(m_limit, Math.Max(length, Math.Max(8192L, Capacity * 2L)));
            }
        }

        private readonly int m_limit;
        private readonly CancellationToken m_token;
    }
}
