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
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    [TestFixture]
    public sealed class EncryptedKeyLogStreamTests
    {
        [Test]
        public void ConstructorValidatesArguments()
        {
            byte[] key = CreateKey();

            Assert.That(
                () => new EncryptedKeyLogStream(null!, key, leaveOpen: true),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("inner"));
            Assert.That(
                () => new EncryptedKeyLogStream(new MemoryStream(), null!, leaveOpen: true),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("sessionKey"));
            Assert.That(
                () => new EncryptedKeyLogStream(new MemoryStream(), new byte[31], leaveOpen: true),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("sessionKey"));
        }

        [Test]
        public void StreamCapabilitiesAndUnsupportedMembersArePinned()
        {
            using var inner = new MemoryStream();
            using var stream = new EncryptedKeyLogStream(inner, CreateKey(), leaveOpen: true);

            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(stream.CanSeek, Is.False);
            Assert.That(() => _ = stream.Length, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => _ = stream.Position, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => stream.Position = 0, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => stream.Seek(0, SeekOrigin.Begin), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => stream.SetLength(0), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void SynchronousWriteFlushAndReadRoundTripRecords()
        {
            byte[] key = CreateKey();
            using var inner = new MemoryStream();
            byte[] expected = Encoding.UTF8.GetBytes("alpha\nbeta");

            using (var writer = new EncryptedKeyLogStream(inner, key, leaveOpen: true))
            {
                writer.Write(expected, 0, 3);
                writer.Write(expected.AsSpan(3));
                writer.Flush();
            }

            inner.Position = 0;
            using var reader = new EncryptedKeyLogStream(inner, key, leaveOpen: true);
            byte[] first = new byte[4];
            byte[] second = new byte[expected.Length - first.Length];

            int firstRead = reader.Read(first, 0, first.Length);
            int secondRead = ReadFully(reader, second);
            int endRead = reader.Read(Array.Empty<byte>(), 0, 0);

            Assert.That(firstRead, Is.EqualTo(first.Length));
            Assert.That(secondRead, Is.EqualTo(second.Length));
            Assert.That(endRead, Is.Zero);
            byte[] actual = new byte[expected.Length];
            first.CopyTo(actual, 0);
            second.CopyTo(actual, first.Length);
            Assert.That(actual, Is.EqualTo(expected).AsCollection);
        }

        [Test]
        public async Task AsyncWriteReadAndCancellationPathsAreCovered()
        {
            byte[] key = CreateKey();
            using var inner = new MemoryStream();
            byte[] expected = Encoding.UTF8.GetBytes("one\ntwo\n");

            await using (var writer = new EncryptedKeyLogStream(inner, key, leaveOpen: true))
            {
                await writer.WriteAsync(expected.AsMemory(0, 4), CancellationToken.None).ConfigureAwait(false);
                await writer.WriteAsync(expected.AsMemory(4), CancellationToken.None).ConfigureAwait(false);
                await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                using var cts = new CancellationTokenSource();
                cts.Cancel();
                Assert.That(
                    async () => await writer.WriteAsync(ReadOnlyMemory<byte>.Empty, cts.Token).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
            }

            inner.Position = 0;
            await using var reader = new EncryptedKeyLogStream(inner, key, leaveOpen: true);
            byte[] actual = new byte[expected.Length];
            int zeroRead = await reader.ReadAsync(Memory<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
            int read = await ReadFullyAsync(reader, actual).ConfigureAwait(false);
            int eof = await reader.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(zeroRead, Is.Zero);
            Assert.That(read, Is.EqualTo(expected.Length));
            Assert.That(eof, Is.Zero);
            Assert.That(actual, Is.EqualTo(expected).AsCollection);
        }

        [Test]
        public async Task CorruptEncryptedRecordShapesThrowDeterministicExceptions()
        {
            byte[] key = CreateKey();

            Assert.That(
                () => ReadAll(new byte[] { 1, 2 }),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("length is incomplete"));
            Assert.That(
                () => ReadAll(BuildRecordWithLength(4)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("length is invalid"));
            Assert.That(
                () => ReadAll(BuildRecordWithLength(32)),
                Throws.TypeOf<EndOfStreamException>().With.Message.Contains("ended unexpectedly"));

            await AssertCorruptAsync(key).ConfigureAwait(false);

            void ReadAll(byte[] bytes)
            {
                using var inner = new MemoryStream(bytes);
                using var stream = new EncryptedKeyLogStream(inner, key, leaveOpen: true);
                _ = stream.Read(new byte[1], 0, 1);
            }
        }

        private static async Task AssertCorruptAsync(byte[] key)
        {
            using var inner = new MemoryStream(BuildRecordWithLength(32));
            await using var stream = new EncryptedKeyLogStream(inner, key, leaveOpen: true);

            Assert.That(
                () => stream.ReadAsync(new byte[1].AsMemory(), CancellationToken.None).AsTask(),
                Throws.TypeOf<EndOfStreamException>().With.Message.Contains("ended unexpectedly"));
        }

        private static int ReadFully(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static async ValueTask<int> ReadFullyAsync(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    CancellationToken.None).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static byte[] BuildRecordWithLength(int recordLength)
        {
            byte[] data = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(data, recordLength);
            return data;
        }

        private static byte[] CreateKey()
        {
            return RandomNumberGenerator.GetBytes(SessionKeyManager.KeySizeInBytes);
        }
    }
}
