/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// A finite short-reading source which fails if a client tries to seek or query length.
    /// The callback observes whether a claim and preceding writes happened before consumption.
    /// </summary>
    internal sealed class NonSeekableUploadSource : Stream
    {
        public NonSeekableUploadSource(byte[] bytes, int maxRead = 2)
        {
            m_bytes = bytes;
            m_maxRead = maxRead;
        }

        public int ReadCalls { get; private set; }
        public int BytesRead { get; private set; }
        public bool IsDisposed { get; private set; }
        public Action<int>? BeforeRead { get; set; }
        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException("The source is not seekable.");
        public override long Position
        {
            get => throw new NotSupportedException("The source is not seekable.");
            set => throw new NotSupportedException("The source is not seekable.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadCore(buffer.AsSpan(offset, count), default);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReadCore(buffer.AsSpan(offset, count), cancellationToken));
        }

#if NET6_0_OR_GREATER
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(ReadCore(buffer.Span, cancellationToken));
        }
#endif

        private int ReadCore(Span<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            BeforeRead?.Invoke(ReadCalls);
            ReadCalls++;
            int count = Math.Min(Math.Min(m_maxRead, buffer.Length), m_bytes.Length - BytesRead);
            m_bytes.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        private readonly byte[] m_bytes;
        private readonly int m_maxRead;
    }
}
