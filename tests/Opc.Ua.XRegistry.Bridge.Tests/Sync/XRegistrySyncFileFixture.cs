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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Bridge.Sync;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    internal sealed class SyncRecordingDurability : IXRegistrySyncFileDurability
    {
        public int FileFlushes { get; private set; }

        public int DirectoryFlushes { get; private set; }

        public int FailDirectoryFlush { get; set; }

        public int Acquisitions { get; private set; }

        public int Writers => Volatile.Read(ref m_writer);

        public ValueTask<IAsyncDisposable> AcquireWriterAsync(
            IFileSystem fileSystem,
            string directory,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_writer, 1, 0) != 0)
            {
                throw new IOException("Fixture writer is already owned.");
            }
            Acquisitions++;
            return new ValueTask<IAsyncDisposable>(new Lease(this));
        }

        public async ValueTask FlushFileAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            FileFlushes++;
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask FlushDirectoryAsync(string directory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryFlushes++;
            if (DirectoryFlushes == FailDirectoryFlush)
            {
                throw new IOException("Fixture directory durability failure.");
            }
            return default;
        }

        private int m_writer;

        private sealed class Lease(SyncRecordingDurability owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref owner.m_writer, 0);
                return default;
            }
        }
    }

    internal sealed class SyncFaultFileSystem : IFileSystem, IDisposable
    {
        public bool FailStagingWrite { get; set; }

        public bool FailBeforeReplace { get; set; }

        public bool FailAfterReplace { get; set; }

        public bool Exists(string path, bool isDirectory = false)
        {
            return m_inner.Exists(path, isDirectory);
        }

        public void Delete(string path, bool isDirectory = false)
        {
            m_inner.Delete(path, isDirectory);
        }

        public Stream OpenRead(string path)
        {
            return m_inner.OpenRead(path);
        }

        public Stream OpenWrite(string path)
        {
            Stream stream = m_inner.OpenWrite(path);
            return FailStagingWrite && path.EndsWith("sync.staged", StringComparison.Ordinal)
                ? new PartialWriteStream(stream)
                : stream;
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            if (FailBeforeReplace)
            {
                throw new IOException("Fixture failure before atomic publication.");
            }
            m_inner.Replace(sourcePath, destinationPath);
            if (FailAfterReplace)
            {
                throw new IOException("Fixture failure after atomic publication.");
            }
        }

        public DateTime GetLastWriteTime(string path)
        {
            return m_inner.GetLastWriteTime(path);
        }

        public long GetLength(string path)
        {
            return m_inner.GetLength(path);
        }

        public byte[] Bytes(string path)
        {
            return m_inner.Get(path);
        }

        public void Dispose()
        {
            m_inner.Dispose();
        }

        private readonly VirtualFileSystem m_inner = new();

        private sealed class PartialWriteStream(Stream stream) : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => stream.Length;

            public override long Position
            {
                get => stream.Position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                stream.Flush();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return stream.FlushAsync(cancellationToken);
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

            public override void Write(byte[] buffer, int offset, int count)
            {
                stream.Write(buffer, offset, 1);
                throw new IOException("Fixture partial staging write.");
            }

            public override async Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                await stream.WriteAsync(buffer.AsMemory(offset, 1), cancellationToken).ConfigureAwait(false);
#else
                await stream.WriteAsync(buffer, offset, 1, cancellationToken).ConfigureAwait(false);
#endif
                throw new IOException("Fixture partial staging write.");
            }

#if NETSTANDARD2_1_OR_GREATER || NET
            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                await stream.WriteAsync(buffer[..1], cancellationToken).ConfigureAwait(false);
                throw new IOException("Fixture partial staging write.");
            }
#endif

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    stream.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
