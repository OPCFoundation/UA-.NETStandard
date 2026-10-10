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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Retains a file whose operating-system sharing rules prevent both content and name replacement.
    /// </summary>
    internal sealed class WotFileContentLease : IWotRegistryContentLease
    {
        private WotFileContentLease(string resourceKey, FileStream stream)
        {
            ResourceKey = resourceKey;
            ContentLength = stream.Length;
            m_stream = stream;
        }

        public string ResourceKey { get; }
        public long ContentLength { get; }

        // POSIX advisory sharing is not an authoritative lease against other filesystem writers.
        internal static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public async ValueTask<ByteString> ReadAsync(
            long offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            AcquireUse();
            bool held = false;
            try
            {
                await m_readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                held = true;
                if (offset >= ContentLength || count == 0)
                {
                    return ByteString.From([]);
                }
                int take = (int)Math.Min(count, ContentLength - offset);
                byte[] buffer = new byte[take];
                m_stream.Position = offset;
                int read = 0;
                while (read < take)
                {
#if NETFRAMEWORK || NETSTANDARD2_0
                    int block = await m_stream.ReadAsync(buffer, read, take - read, cancellationToken)
                        .ConfigureAwait(false);
#else
                    int block = await m_stream.ReadAsync(buffer.AsMemory(read, take - read), cancellationToken)
                        .ConfigureAwait(false);
#endif
                    if (block == 0)
                    {
                        throw new EndOfStreamException("The protected immutable content was truncated.");
                    }
                    read += block;
                }
                return ByteString.From(buffer);
            }
            finally
            {
                if (held)
                {
                    m_readGate.Release();
                }
                ReleaseUse();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                ReleaseUse();
            }
        }

        internal static IWotRegistryContentLease Open(string path, string resourceKey)
        {
            if (!IsSupported)
            {
                throw new NotSupportedException(
                    "The local filesystem does not provide an authoritative immutable content lease.");
            }
            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var lease = new WotFileContentLease(resourceKey, stream);
                stream = null;
                return lease;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private void AcquireUse()
        {
            int count = Volatile.Read(ref m_uses);
            while (count > 0 && Volatile.Read(ref m_disposed) == 0)
            {
                int observed = Interlocked.CompareExchange(ref m_uses, checked(count + 1), count);
                if (observed == count)
                {
                    if (Volatile.Read(ref m_disposed) == 0)
                    {
                        return;
                    }
                    ReleaseUse();
                    break;
                }
                count = observed;
            }
            throw new ObjectDisposedException(nameof(WotFileContentLease));
        }

        private void ReleaseUse()
        {
            if (Interlocked.Decrement(ref m_uses) == 0)
            {
                m_stream.Dispose();
                m_readGate.Dispose();
            }
        }

        private readonly FileStream m_stream;
        private readonly SemaphoreSlim m_readGate = new(1, 1);
        private int m_uses = 1;
        private int m_disposed;
    }
}
