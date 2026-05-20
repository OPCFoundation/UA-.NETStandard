/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests.Client
{
    /// <summary>
    /// Unit tests for <see cref="FileTypeClientExtensions"/> — the
    /// generic, FileType-level chunked Upload / Download helpers,
    /// including the new <see cref="Stream"/>-based overloads.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable(ParallelScope.All)]
    public class FileTypeClientExtensionsTests
    {
        [Test]
        public async Task CopyStreamInChunksSeekableStreamWritesAllBytesInOrderAsync()
        {
            byte[] data = new byte[10240];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 251);
            }
            using var source = new MemoryStream(data);
            var chunks = new List<byte[]>();

            await FileTypeClientExtensions.CopyStreamInChunksAsync(
                source,
                chunkSize: 1024,
                (chunk, _) =>
                {
                    chunks.Add(chunk.ToArray());
                    return default;
                },
                CancellationToken.None);

            byte[] reassembled = Flatten(chunks);
            Assert.That(reassembled, Is.EqualTo(data));
            Assert.That(chunks, Has.Count.EqualTo(10));
            Assert.That(chunks[0], Has.Length.EqualTo(1024));
        }

        [Test]
        public async Task CopyStreamInChunksNonSeekableStreamWritesAllBytesInOrderAsync()
        {
            byte[] data = new byte[3000];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i;
            }
            using var source = new ForwardOnlyStream(data);
            var chunks = new List<byte[]>();

            await FileTypeClientExtensions.CopyStreamInChunksAsync(
                source,
                chunkSize: 256,
                (chunk, _) =>
                {
                    chunks.Add(chunk.ToArray());
                    return default;
                },
                CancellationToken.None);

            Assert.That(Flatten(chunks), Is.EqualTo(data));
        }

        [Test]
        public async Task CopyStreamInChunksEmptyStreamMakesNoWriteCallsAsync()
        {
            using var source = new MemoryStream();
            int writes = 0;

            await FileTypeClientExtensions.CopyStreamInChunksAsync(
                source,
                chunkSize: 256,
                (_, _) =>
                {
                    writes++;
                    return default;
                },
                CancellationToken.None);

            Assert.That(writes, Is.EqualTo(0));
        }

        [Test]
        public async Task CopyStreamInChunksExactMultipleEmitsExpectedChunkCountAsync()
        {
            byte[] data = new byte[2048];
            using var source = new MemoryStream(data);
            int writes = 0;

            await FileTypeClientExtensions.CopyStreamInChunksAsync(
                source,
                chunkSize: 1024,
                (_, _) =>
                {
                    writes++;
                    return default;
                },
                CancellationToken.None);

            Assert.That(writes, Is.EqualTo(2));
        }

        [Test]
        public void CopyStreamInChunksHonorsCancellation()
        {
            using var source = new MemoryStream(new byte[8192]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () =>
            {
                await FileTypeClientExtensions.CopyStreamInChunksAsync(
                    source,
                    1024,
                    (_, _) => default,
                    cts.Token);
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task CopyChunksToStreamAccumulatesAllChunksInOrderAsync()
        {
            byte[] data = new byte[5000];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i;
            }
            var source = new Queue<byte[]>();
            for (int i = 0; i < data.Length; i += 1024)
            {
                int len = Math.Min(1024, data.Length - i);
                byte[] slice = new byte[len];
                Array.Copy(data, i, slice, 0, len);
                source.Enqueue(slice);
            }

            using var dest = new MemoryStream();
            await FileTypeClientExtensions.CopyChunksToStreamAsync(
                dest,
                chunkSize: 1024,
                (_, _) =>
                {
                    if (source.Count == 0)
                    {
                        return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
                    }
                    byte[] next = source.Dequeue();
                    return new ValueTask<ReadOnlyMemory<byte>>(next);
                },
                CancellationToken.None);

            Assert.That(dest.ToArray(), Is.EqualTo(data));
        }

        [Test]
        public async Task CopyChunksToStreamStopsOnShortChunkAsync()
        {
            // Last chunk shorter than chunkSize → loop must stop early
            // even if the read delegate would still return more data.
            byte[] full = new byte[1024];
            byte[] partial = new byte[500];
            byte[] extra = new byte[1024]; // should never be requested
            var source = new Queue<byte[]>(new[] { full, partial, extra });

            using var dest = new MemoryStream();
            await FileTypeClientExtensions.CopyChunksToStreamAsync(
                dest,
                chunkSize: 1024,
                (_, _) =>
                {
                    byte[] next = source.Dequeue();
                    return new ValueTask<ReadOnlyMemory<byte>>(next);
                },
                CancellationToken.None);

            Assert.That(dest.Length, Is.EqualTo(1524));
            Assert.That(source.Count, Is.EqualTo(1));
        }

        [Test]
        public void CopyChunksToStreamHonorsCancellation()
        {
            using var dest = new MemoryStream();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(async () =>
            {
                await FileTypeClientExtensions.CopyChunksToStreamAsync(
                    dest,
                    1024,
                    (_, _) => new ValueTask<ReadOnlyMemory<byte>>(new byte[1024]),
                    cts.Token);
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task UploadStreamEndToEndWritesAllBytesAndClosesAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            byte[] payload = new byte[6000];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xff);
            }
            byte capturedMode = 0;
            byte[] writtenSoFar = Array.Empty<byte>();
            bool closed = false;
            mock.OnOpen(mode => {
            capturedMode = mode;
            return 99; });
            mock.OnWrite((handle, data) =>
            {
                Assert.That(handle, Is.EqualTo(99u));
                byte[] joined = new byte[writtenSoFar.Length + data.Length];
                Array.Copy(writtenSoFar, 0, joined, 0, writtenSoFar.Length);
                Array.Copy(data, 0, joined, writtenSoFar.Length, data.Length);
                writtenSoFar = joined;
            });
            mock.OnClose(handle => {
            Assert.That(handle, Is.EqualTo(99u));
            closed = true; });

            var file = new FileTypeClient(mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var source = new MemoryStream(payload);
            await file.UploadAsync(source, mode: 6, chunkSize: 1024, CancellationToken.None);

            Assert.That(capturedMode, Is.EqualTo((byte)6));
            Assert.That(writtenSoFar, Is.EqualTo(payload));
            Assert.That(closed, Is.True);
        }

        [Test]
        public async Task DownloadToStreamEndToEndReadsUntilShortChunkAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            byte[] payload = new byte[3500];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xff);
            }
            int position = 0;
            byte capturedMode = 0;
            bool closed = false;
            mock.OnOpen(mode => {
            capturedMode = mode;
            return 88; });
            mock.OnRead((handle, len) =>
            {
                Assert.That(handle, Is.EqualTo(88u));
                int take = Math.Min(len, payload.Length - position);
                if (take <= 0)
                {
                    return Array.Empty<byte>();
                }
                byte[] slice = new byte[take];
                Array.Copy(payload, position, slice, 0, take);
                position += take;
                return slice;
            });
            mock.OnClose(handle => {
            Assert.That(handle, Is.EqualTo(88u));
            closed = true; });

            var file = new FileTypeClient(mock.Session, new NodeId(7u), mock.Session.MessageContext.Telemetry);
            using var destination = new MemoryStream();
            await file.DownloadToAsync(destination, chunkSize: 1024, CancellationToken.None);

            Assert.That(capturedMode, Is.EqualTo((byte)1));
            Assert.That(destination.ToArray(), Is.EqualTo(payload));
            Assert.That(closed, Is.True);
        }

        [Test]
        public void UploadStreamNullFileThrows()
        {
            using var source = new MemoryStream();
            Assert.That(async () =>
            {
                await ((FileTypeClient)null!).UploadAsync(source);
            }, Throws.ArgumentNullException);
        }

        [Test]
        public void UploadStreamNullStreamThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            Assert.That(async () =>
            {
                await file.UploadAsync((Stream)null!);
            }, Throws.ArgumentNullException);
        }

        [Test]
        public void UploadStreamNonReadableThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var dest = new MemoryStream(new byte[16], writable: true);
            using var writeOnly = new WriteOnlyStream();
            Assert.That(async () =>
            {
                await file.UploadAsync(writeOnly);
            }, Throws.ArgumentException);
        }

        [Test]
        public void UploadStreamInvalidChunkSizeThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var source = new MemoryStream();
            Assert.That(async () =>
            {
                await file.UploadAsync(source, chunkSize: 0);
            }, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DownloadToStreamNullFileThrows()
        {
            using var dest = new MemoryStream();
            Assert.That(async () =>
            {
                await ((FileTypeClient)null!).DownloadToAsync(dest);
            }, Throws.ArgumentNullException);
        }

        [Test]
        public void DownloadToStreamNullDestinationThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            Assert.That(async () =>
            {
                await file.DownloadToAsync((Stream)null!);
            }, Throws.ArgumentNullException);
        }

        [Test]
        public void DownloadToStreamNonWritableThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var readOnly = new ReadOnlyStream(new byte[8]);
            Assert.That(async () =>
            {
                await file.DownloadToAsync(readOnly);
            }, Throws.ArgumentException);
        }

        [Test]
        public void DownloadToStreamInvalidChunkSizeThrows()
        {
            var mock = new WotAssetFileTypeSessionMock();
            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var dest = new MemoryStream();
            Assert.That(async () =>
            {
                await file.DownloadToAsync(dest, chunkSize: -1);
            }, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task UploadStreamEmptyStreamStillOpensAndClosesAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            bool opened = false;
            bool closed = false;
            int writes = 0;
            mock.OnOpen(_ => {
            opened = true;
            return 1; });
            mock.OnWrite((_, _) => { writes++; });
            mock.OnClose(_ => { closed = true; });

            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var source = new MemoryStream();
            await file.UploadAsync(source);

            Assert.That(opened, Is.True);
            Assert.That(closed, Is.True);
            Assert.That(writes, Is.EqualTo(0));
        }

        [Test]
        public async Task UploadStreamCloseCalledEvenWhenWriteFailsAsync()
        {
            var mock = new WotAssetFileTypeSessionMock();
            bool closed = false;
            mock.OnOpen(_ => 5);
            mock.OnWrite((_, _) => throw new InvalidOperationException("synthetic"));
            mock.OnClose(_ => { closed = true; });

            var file = new FileTypeClient(mock.Session, new NodeId(1u), mock.Session.MessageContext.Telemetry);
            using var source = new MemoryStream(new byte[16]);

            try
            {
                await file.UploadAsync(source, chunkSize: 8);
                Assert.Fail("expected exception");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
            Assert.That(closed, Is.True);
        }

        private static byte[] Flatten(List<byte[]> chunks)
        {
            int total = 0;
            foreach (byte[] c in chunks)
            {
                total += c.Length;
            }
            byte[] result = new byte[total];
            int offset = 0;
            foreach (byte[] c in chunks)
            {
                Array.Copy(c, 0, result, offset, c.Length);
                offset += c.Length;
            }
            return result;
        }

        private sealed class ForwardOnlyStream : Stream
        {
            public ForwardOnlyStream(byte[] data)
            {
                m_data = data;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => m_position;
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int avail = Math.Min(count, m_data.Length - m_position);
                if (avail <= 0)
                {
                    return 0;
                }
                Array.Copy(m_data, m_position, buffer, offset, avail);
                m_position += avail;
                return avail;
            }

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            private readonly byte[] m_data;
            private int m_position;
        }

        private sealed class WriteOnlyStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        private sealed class ReadOnlyStream : Stream
        {
            public ReadOnlyStream(byte[] data)
            {
                m_data = data;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => m_data.Length;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            private readonly byte[] m_data;
        }
    }
}
