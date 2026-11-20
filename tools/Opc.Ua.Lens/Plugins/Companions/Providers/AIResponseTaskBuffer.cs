/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// A fixed-capacity destination for the typed transfer client's streaming overload.
    /// Bytes are never accumulated beyond the approved limit or exposed before digest verification.
    /// </summary>
    internal sealed class AIResponseTaskBuffer : Stream
    {
        public AIResponseTaskBuffer(int maximumBytes)
        {
            if (maximumBytes is < 1 or > MaximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            m_bytes = new byte[maximumBytes];
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !m_disposed;

        public override long Length => m_length;

        public override long Position
        {
            get => m_length;
            set => throw new NotSupportedException();
        }

        public ByteString VerifyAndCopy(ByteString expectedDigest)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (expectedDigest.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(m_bytes.AsSpan(0, m_length)), expectedDigest.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The response does not match the approved SHA-256 digest.");
            }
            return ByteString.From(m_bytes.AsSpan(0, m_length));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (buffer.Length > m_bytes.Length - m_length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The response exceeds the approved transfer byte cap.");
            }
            buffer.CopyTo(m_bytes.AsSpan(m_length));
            m_length += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !m_disposed)
            {
                CryptographicOperations.ZeroMemory(m_bytes);
                m_disposed = true;
            }
            base.Dispose(disposing);
        }

        internal const int MaximumBytes = 1024 * 1024;
        private readonly byte[] m_bytes;
        private int m_length;
        private bool m_disposed;
    }
}
