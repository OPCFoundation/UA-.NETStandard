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

// CA1835: The byte[]-based ReadAsync/WriteAsync overload is used
// throughout because the test fixture targets all TFMs of the parent
// project (incl. net472/net48 which do not expose the Memory<byte>
// overrides). The behaviour is identical on net10+.
#pragma warning disable CA1835

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="UaFileStream"/>. Uses a mock
    /// <see cref="ISessionClient"/> via <see cref="FileTypeSessionMock"/>;
    /// the underlying <c>FileTypeClient</c> proxy translates the
    /// async wrappers into <c>Call</c> requests.
    /// </summary>
    /// <remarks>
    /// Tests use the
    /// <c>await using (stream.ConfigureAwait(false))</c> block pattern
    /// so the dispose itself respects ConfigureAwait, matching the
    /// convention used elsewhere in <c>Opc.Ua.Client.Tests</c>.
    /// </remarks>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class UaFileStreamTests
    {
        private FileTypeSessionMock m_session;
        private FileTypeClient m_proxy;

        [SetUp]
        public void Setup()
        {
            m_session = new FileTypeSessionMock();
            m_proxy = new FileTypeClient(
                m_session.Session,
                new NodeId(42),
                m_session.Session.MessageContext.Telemetry);
            m_session.OnClose(_ => { });
        }

        private UaFileStream NewStream(
            UaFileMode mode = UaFileMode.Read,
            long length = 1024,
            long position = 0,
            int chunkSize = 4)
        {
            return new UaFileStream(
                m_proxy,
                handle: 7,
                mode,
                initialLength: length,
                initialPosition: position,
                chunkSize);
        }

        [Test]
        public async Task ReadAsyncChunksAcrossMultipleServerCallsAsync()
        {
            byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            int offset = 0;
            m_session.OnRead((handle, len) =>
            {
                int remaining = payload.Length - offset;
                int take = Math.Min(remaining, len);
                byte[] slice = payload.AsSpan(offset, take).ToArray();
                offset += take;
                return slice;
            });

            UaFileStream stream = NewStream(
                UaFileMode.Read, length: payload.Length, chunkSize: 4);
            await using (stream.ConfigureAwait(false))
            {
                byte[] buffer = new byte[10];
                int total = await stream
                    .ReadAsync(buffer, 0, 10, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(total, Is.EqualTo(10));
                Assert.That(buffer, Is.EqualTo(payload));
            }
            Assert.That(
                m_session.CapturedFor(Methods.FileType_Read),
                Has.Count.EqualTo(3));
        }

        [Test]
        public async Task ReadAsyncEmptyByteStringMeansEofAsync()
        {
            int callCount = 0;
            m_session.OnRead((_, _) =>
            {
                callCount++;
                return [];
            });

            UaFileStream stream = NewStream(UaFileMode.Read);
            await using (stream.ConfigureAwait(false))
            {
                byte[] buffer = new byte[8];
                int read = await stream
                    .ReadAsync(buffer, 0, 8, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(read, Is.Zero);
                Assert.That(callCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ZeroLengthReadDoesNotHitTheWireAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Read);
            await using (stream.ConfigureAwait(false))
            {
                int read = await stream
                    .ReadAsync([], 0, 0, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(read, Is.Zero);
                Assert.That(m_session.CapturedFor(Methods.FileType_Read), Is.Empty);
            }
        }

        [Test]
        public async Task WriteAsyncChunksAtChunkSizeBoundaryAsync()
        {
            var written = new List<byte>();
            m_session.OnWrite((_, data) => written.AddRange(data));

            UaFileStream stream = NewStream(
                UaFileMode.Write, length: 0, chunkSize: 3);
            await using (stream.ConfigureAwait(false))
            {
                byte[] payload = [1, 2, 3, 4, 5, 6, 7];
                await stream
                    .WriteAsync(payload, 0, 7, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Assert.That(written, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7 }));
            Assert.That(
                m_session.CapturedFor(Methods.FileType_Write),
                Has.Count.EqualTo(3));
        }

        [Test]
        public async Task ZeroLengthWriteDoesNotHitTheWireAsync()
        {
            m_session.OnWrite((_, _) => { });
            UaFileStream stream = NewStream(UaFileMode.Write);
            await using (stream.ConfigureAwait(false))
            {
                await stream
                    .WriteAsync([], 0, 0, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(m_session.CapturedFor(Methods.FileType_Write), Is.Empty);
            }
        }

        [Test]
        public async Task WriteExtendsLengthAsync()
        {
            m_session.OnWrite((_, _) => { });
            UaFileStream stream = NewStream(
                UaFileMode.Write, length: 0, chunkSize: 8);
            await using (stream.ConfigureAwait(false))
            {
                await stream
                    .WriteAsync(new byte[5], 0, 5, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(stream.Length, Is.EqualTo(5));
                Assert.That(stream.Position, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task SeekDoesNotCallSetPositionUntilNextOperationAsync()
        {
            m_session.OnSetPosition((_, _) => { });
            m_session.OnRead((_, _) => []);

            UaFileStream stream = NewStream(UaFileMode.Read, length: 100);
            await using (stream.ConfigureAwait(false))
            {
                stream.Seek(20, SeekOrigin.Begin);
                Assert.That(stream.Position, Is.EqualTo(20));
                Assert.That(m_session.CapturedFor(Methods.FileType_SetPosition), Is.Empty);

                byte[] tmp = new byte[1];
                _ = await stream
                    .ReadAsync(tmp, 0, 1, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            List<CallMethodRequest> setPositions =
                m_session.CapturedFor(Methods.FileType_SetPosition);
            Assert.That(setPositions, Has.Count.EqualTo(1));
            setPositions[0].InputArguments[1].TryGetValue(out ulong pushed);
            Assert.That(pushed, Is.EqualTo(20UL));
        }

        [Test]
        public async Task SecondReadAfterFirstDoesNotResendSetPositionAsync()
        {
            m_session.OnSetPosition((_, _) => { });
            int callCount = 0;
            m_session.OnRead((_, len) =>
            {
                callCount++;
                return new byte[Math.Min(len, 4)];
            });

            UaFileStream stream = NewStream(
                UaFileMode.Read, length: 100, chunkSize: 4);
            await using (stream.ConfigureAwait(false))
            {
                byte[] buffer = new byte[8];
                _ = await stream
                    .ReadAsync(buffer, 0, 8, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Assert.That(m_session.CapturedFor(Methods.FileType_SetPosition), Is.Empty);
            Assert.That(callCount, Is.EqualTo(2));
        }

        [Test]
        public async Task SeekBeforeStartThrowsAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Read, length: 100);
            await using (stream.ConfigureAwait(false))
            {
                Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            }
        }

        [Test]
        public async Task SetLengthThrowsNotSupportedAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Write);
            await using (stream.ConfigureAwait(false))
            {
                Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            }
        }

        [Test]
        public async Task DisposeAsyncCallsCloseExactlyOnceAsync()
        {
            int closeCount = 0;
            m_session.OnClose(_ => closeCount++);
            UaFileStream stream = NewStream(UaFileMode.Read);
            await stream.DisposeAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
            Assert.That(closeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadAfterDisposeThrowsAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Read);
            await stream.DisposeAsync().ConfigureAwait(false);
            byte[] buffer = new byte[1];
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await stream
                    .ReadAsync(buffer, 0, 1, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ReadOnReadOnlyStreamThrowsOnWriteAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Read);
            await using (stream.ConfigureAwait(false))
            {
                Assert.ThrowsAsync<NotSupportedException>(
                    async () => await stream
                        .WriteAsync(new byte[1], 0, 1, CancellationToken.None)
                        .ConfigureAwait(false));
            }
        }

        [Test]
        public async Task WriteOnWriteOnlyStreamThrowsOnReadAsync()
        {
            UaFileStream stream = NewStream(UaFileMode.Write);
            await using (stream.ConfigureAwait(false))
            {
                Assert.ThrowsAsync<NotSupportedException>(
                    async () => await stream
                        .ReadAsync(new byte[1], 0, 1, CancellationToken.None)
                        .ConfigureAwait(false));
            }
        }

        [Test]
        public async Task SyncReadProducesSameResultAsAsyncReadAsync()
        {
            byte[] payload = [10, 20, 30, 40, 50];
            int offset = 0;
            m_session.OnRead((_, len) =>
            {
                int remaining = payload.Length - offset;
                int take = Math.Min(remaining, len);
                byte[] slice = payload.AsSpan(offset, take).ToArray();
                offset += take;
                return slice;
            });

            UaFileStream stream = NewStream(
                UaFileMode.Read, length: payload.Length, chunkSize: 8);
            await using (stream.ConfigureAwait(false))
            {
                byte[] buffer = new byte[5];
                int read = stream.Read(buffer, 0, 5);
                Assert.That(read, Is.EqualTo(5));
                Assert.That(buffer, Is.EqualTo(payload));
            }
        }

        [Test]
        public async Task ReadCallsTargetCorrectMethodIdAsync()
        {
            m_session.OnRead((_, _) => new byte[] { 1, 2 });
            UaFileStream stream = NewStream(
                UaFileMode.Read, length: 2, chunkSize: 2);
            await using (stream.ConfigureAwait(false))
            {
                _ = await stream
                    .ReadAsync(new byte[2], 0, 2, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            CallMethodRequest req = m_session.Capture
                .First(r => r.MethodId.TryGetValue(out uint id) &&
                    id == Methods.FileType_Read);
            req.InputArguments[0].TryGetValue(out uint handle);
            req.InputArguments[1].TryGetValue(out int length);
            Assert.That(handle, Is.EqualTo(7u));
            Assert.That(length, Is.EqualTo(2));
            Assert.That(req.ObjectId, Is.EqualTo(new NodeId(42)));
        }

        [Test]
        public async Task WriteCallsTargetCorrectMethodIdAsync()
        {
            m_session.OnWrite((_, _) => { });
            UaFileStream stream = NewStream(
                UaFileMode.Write, length: 0, chunkSize: 8);
            await using (stream.ConfigureAwait(false))
            {
                await stream
                    .WriteAsync(new byte[] { 9, 8, 7 }, 0, 3, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            CallMethodRequest req = m_session.Capture
                .First(r => r.MethodId.TryGetValue(out uint id) &&
                    id == Methods.FileType_Write);
            req.InputArguments[0].TryGetValue(out uint handle);
            req.InputArguments[1].TryGetValue(out ByteString data);
            Assert.That(handle, Is.EqualTo(7u));
            Assert.That(data.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        }
    }
}
