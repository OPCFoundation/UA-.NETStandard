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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal sealed class XRegistryNativeSpool(
        XRegistryBridgeNativeOptions options, XRegistryFileBudget budget) : IDisposable
    {
        public long Length
        {
            get
            {
                EnsureUsable();
                return m_stream.Length;
            }
        }

        public long Position
        {
            get
            {
                EnsureUsable();
                return m_stream.Position;
            }
            set
            {
                EnsureUsable();
                m_stream.Position = value;
            }
        }

        public bool IsSpooled => m_stream is FileStream;

        public async ValueTask WriteAsync(ByteString bytes, CancellationToken ct)
        {
            EnsureUsable();
            long end = Position + bytes.Length;
            try
            {
                await ReserveAsync(end, ct).ConfigureAwait(false);
#if NETSTANDARD2_1_OR_GREATER || NET
                await m_stream.WriteAsync(bytes.Memory, ct).ConfigureAwait(false);
#else
                byte[] value = bytes.ToArray();
                await m_stream.WriteAsync(value, 0, value.Length, ct).ConfigureAwait(false);
#endif
            }
            catch
            {
                m_faulted = true;
                throw;
            }
        }

        public async ValueTask<int> ReadAsync(byte[] bytes, CancellationToken ct)
        {
            EnsureUsable();
#if NETSTANDARD2_1_OR_GREATER || NET
            return await m_stream.ReadAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
#else
            return await m_stream.ReadAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
#endif
        }

        public async ValueTask<ByteString> MaterializeAsync(CancellationToken ct)
        {
            EnsureUsable();
            if (Length > int.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            m_stream.Position = 0;
            byte[] bytes = new byte[(int)Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                int read = await m_stream.ReadAsync(bytes.AsMemory(offset), ct).ConfigureAwait(false);
#else
                int read = await m_stream.ReadAsync(bytes, offset, bytes.Length - offset, ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    throw new InvalidDataException("The native spool was truncated.");
                }
                offset += read;
            }
            return new ByteString(bytes);
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                m_stream.Dispose();
            }
            finally
            {
                budget.ReleaseBytes(m_memory);
                budget.ReleaseSpoolBytes(m_disk);
            }
        }

        public static ByteString Digest(ByteString bytes)
        {
#if NET5_0_OR_GREATER
            return ByteString.From(SHA256.HashData(bytes.Span));
#else
            using SHA256 hash = SHA256.Create();
            return ByteString.From(hash.ComputeHash(bytes.ToArray()));
#endif
        }

        private async ValueTask ReserveAsync(long end, CancellationToken ct)
        {
            if (m_stream is MemoryStream memory)
            {
                if (options.SpoolDirectory is not null && end > options.MemoryBufferThreshold)
                {
                    await SpillAsync(memory, Math.Max(end, memory.Length), ct).ConfigureAwait(false);
                }
                else if (end > memory.Capacity)
                {
                    int additional = checked((int)end - memory.Capacity);
                    budget.ReserveBytes(additional);
                    try
                    {
                        memory.Capacity = checked((int)end);
                    }
                    catch
                    {
                        budget.ReleaseBytes(additional);
                        throw;
                    }
                    m_memory += additional;
                }
            }
            else if (end > m_disk)
            {
                budget.ReserveSpoolBytes(end - m_disk);
                m_disk = end;
            }
        }

        private async ValueTask SpillAsync(MemoryStream memory, long length, CancellationToken ct)
        {
            string directory = Path.GetFullPath(options.SpoolDirectory!);
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The private spool directory cannot be a reparse point.");
            }
            budget.ReserveSpoolBytes(length);
            FileStream? file = null;
            long position = memory.Position;
            bool transferred = false;
            try
            {
                file = new FileStream(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".spool"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096,
                    FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);
                memory.Position = 0;
                await memory.CopyToAsync(file, 65_536, ct).ConfigureAwait(false);
                file.Position = position;
                m_stream = file;
                file = null;
                m_disk = length;
                budget.ReleaseBytes(m_memory);
                m_memory = 0;
#if NETSTANDARD2_1_OR_GREATER || NET
                await memory.DisposeAsync().ConfigureAwait(false);
#else
                memory.Dispose();
#endif
                transferred = true;
            }
            finally
            {
                if (file is not null)
                {
#if NETSTANDARD2_1_OR_GREATER || NET
                    await file.DisposeAsync().ConfigureAwait(false);
#else
                    file.Dispose();
#endif
                }
                if (!transferred)
                {
                    memory.Position = position;
                    budget.ReleaseSpoolBytes(length);
                }
            }
        }

        private void EnsureUsable()
        {
            if (m_faulted)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A failed spool write invalidated the handle.");
            }
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(XRegistryNativeSpool));
            }
        }

        private Stream m_stream = new MemoryStream();
        private int m_memory;
        private long m_disk;
        private bool m_faulted;
        private bool m_disposed;
    }
}
