// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GmsSample.Client
{
    /// <summary>
    /// Demonstrates caller cancellation after the existing download client has opened and read the remote file.
    /// </summary>
    internal sealed class DownloadCancellationStream : MemoryStream
    {
        /// <summary>
        /// Borrows the one-transfer cancellation source; the caller retains ownership.
        /// </summary>
        internal DownloadCancellationStream(CancellationTokenSource cancellation)
        {
            m_cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        }

        /// <summary>
        /// Gets the actual remote bytes presented to the destination before cancellation.
        /// </summary>
        internal int AttemptedBytes { get; private set; }

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            AttemptedBytes += count;
            m_cancellation.Cancel();
            return Task.FromCanceled(m_cancellation.Token);
        }

#if NET8_0_OR_GREATER
        /// <inheritdoc/>
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AttemptedBytes += buffer.Length;
            m_cancellation.Cancel();
            return new ValueTask(Task.FromCanceled(m_cancellation.Token));
        }
#endif

        /// <summary>The caller-owned transfer cancellation source.</summary>
        private readonly CancellationTokenSource m_cancellation;
    }
}
