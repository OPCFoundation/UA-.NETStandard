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
