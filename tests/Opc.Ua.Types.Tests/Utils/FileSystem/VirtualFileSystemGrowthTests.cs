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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils.FileSystem
{
    /// <summary>
    /// Tests growth and boundary behavior of the virtual file system.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class VirtualFileSystemGrowthTests
    {
        [Test]
        public void ChunkedWriteAcrossBoundariesRoundTrips()
        {
            using var fileSystem = new VirtualFileSystem();
            byte[] expected = CreatePattern(kLargeFileSize);

            using (Stream stream = fileSystem.OpenWrite("large.bin"))
            {
                for (int offset = 0; offset < expected.Length; offset += kWriteBlockSize)
                {
                    int count = Math.Min(kWriteBlockSize, expected.Length - offset);
                    stream.Write(expected, offset, count);
                }
            }

            Assert.That(fileSystem.Get("large.bin"), Is.EqualTo(expected));

            byte[] actual = new byte[expected.Length];
            using Stream readStream = fileSystem.OpenRead("large.bin");
            int bytesRead = 0;
            while (bytesRead < actual.Length)
            {
                int read = readStream.Read(
                    actual,
                    bytesRead,
                    Math.Min(kWriteBlockSize, actual.Length - bytesRead));
                Assert.That(read, Is.GreaterThan(0));
                bytesRead += read;
            }

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(readStream.ReadByte(), Is.EqualTo(-1));
        }

        [Test]
        public void StreamWriterAcrossBoundariesRoundTrips()
        {
            using var fileSystem = new VirtualFileSystem();
            char[] characters = new char[kLargeFileSize];
            for (int ii = 0; ii < characters.Length; ii++)
            {
                characters[ii] = (char)(' ' + (ii % 95));
            }
            string expected = new(characters);

            using (TextWriter writer = fileSystem.CreateTextWriter("large.txt"))
            {
                writer.Write(expected);
            }

            using TextReader reader = fileSystem.CreateTextReader("large.txt");
            Assert.That(reader.ReadToEnd(), Is.EqualTo(expected));
        }

        [Test]
        public void ShrinkThenGrowClearsDiscardedBytes()
        {
            using var fileSystem = new VirtualFileSystem();
            byte[] original = new byte[1024 * 1024];
            for (int ii = 0; ii < original.Length; ii++)
            {
                original[ii] = 0xA5;
            }
            fileSystem.Add("shrink.bin", original);

            byte[] prefix = "new content"u8.ToArray();
            using (Stream stream = fileSystem.OpenWrite("shrink.bin"))
            {
                stream.Write(prefix, 0, prefix.Length);
            }

            using (Stream stream = fileSystem.OpenWrite("shrink.bin"))
            {
                stream.SetLength(original.Length);
                stream.Position = original.Length;
            }

            byte[] content = fileSystem.Get("shrink.bin");
            Assert.That(content, Has.Length.EqualTo(original.Length));
            Assert.That(content.AsSpan(0, prefix.Length).ToArray(), Is.EqualTo(prefix));
            Assert.That(content.AsSpan(prefix.Length).ToArray(), Is.All.Zero);
        }

        [Test]
        public void SeekPastEndOnWriteZeroFillsGap()
        {
            using var fileSystem = new VirtualFileSystem();
            byte[] prefix = "prefix"u8.ToArray();
            byte[] suffix = "suffix"u8.ToArray();
            const int suffixPosition = 131_089;

            using (Stream stream = fileSystem.OpenWrite("sparse.bin"))
            {
                stream.Write(prefix, 0, prefix.Length);
                Assert.That(
                    stream.Seek(suffixPosition, SeekOrigin.Begin),
                    Is.EqualTo(suffixPosition));
                stream.Write(suffix, 0, suffix.Length);
            }

            byte[] content = fileSystem.Get("sparse.bin");
            Assert.That(content, Has.Length.EqualTo(suffixPosition + suffix.Length));
            Assert.That(content.AsSpan(0, prefix.Length).ToArray(), Is.EqualTo(prefix));
            Assert.That(
                content.AsSpan(suffixPosition, suffix.Length).ToArray(),
                Is.EqualTo(suffix));
            Assert.That(
                content.AsSpan(prefix.Length, suffixPosition - prefix.Length).ToArray(),
                Is.All.Zero);
        }

        [Test]
        public void SeekPastEndOnReadDoesNotChangeLength()
        {
            using var fileSystem = new VirtualFileSystem();
            byte[] content = "content"u8.ToArray();
            fileSystem.Add("read.bin", content);

            using Stream stream = fileSystem.OpenRead("read.bin");
            Assert.That(stream.Seek(1024 * 1024, SeekOrigin.Begin), Is.EqualTo(1024 * 1024));
            Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            Assert.That(fileSystem.GetLength("read.bin"), Is.EqualTo(content.Length));
        }

        [Test]
        public void ManyChunkedFilesRoundTrip()
        {
            using var fileSystem = new VirtualFileSystem();
            const int fileCount = 512;
            const int fileSize = 64 * 1024;
            byte[] content = new byte[fileSize];

            for (int fileIndex = 0; fileIndex < fileCount; fileIndex++)
            {
                for (int ii = 0; ii < content.Length; ii++)
                {
                    content[ii] = (byte)fileIndex;
                }
                fileSystem.Add($"file-{fileIndex}.bin", content);
            }

            for (int fileIndex = 0; fileIndex < fileCount; fileIndex++)
            {
                byte[] actual = fileSystem.Get($"file-{fileIndex}.bin");
                Assert.That(actual, Has.Length.EqualTo(fileSize));
                Assert.That(actual[0], Is.EqualTo((byte)fileIndex));
                Assert.That(actual[^1], Is.EqualTo((byte)fileIndex));
            }
        }

        [Test]
        public void DiskFilePreservesLastWriteTime()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "content");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
                DateTime expected = File.GetLastWriteTimeUtc(path);

                using var fileSystem = new VirtualFileSystem();
                using Stream stream = fileSystem.OpenRead(path);

                Assert.That(fileSystem.GetLastWriteTime(path), Is.EqualTo(expected));
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch
                { /* Ignore cleanup errors */
                }
            }
        }

        [Test]
        public async Task SpanAndAsyncOperationsRoundTripAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            byte[] first = CreatePattern(131_071);
            byte[] second = CreatePattern(131_073);

            using (Stream stream = fileSystem.OpenWrite("async.bin"))
            {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                stream.Write(first.AsSpan());
                await stream.WriteAsync(second.AsMemory()).ConfigureAwait(false);
#else
                stream.Write(first, 0, first.Length);
                await stream.WriteAsync(second, 0, second.Length).ConfigureAwait(false);
#endif
                await stream.FlushAsync().ConfigureAwait(false);
            }

            byte[] actual = new byte[first.Length + second.Length];
            using Stream readStream = fileSystem.OpenRead("async.bin");
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            int firstRead = readStream.Read(actual.AsSpan(0, first.Length));
            int secondRead = await readStream
                .ReadAsync(actual.AsMemory(first.Length, second.Length))
                .ConfigureAwait(false);
            Assert.That(readStream.Read(actual.AsSpan()), Is.Zero);
#else
            int firstRead = readStream.Read(actual, 0, first.Length);
            int secondRead = await readStream
                .ReadAsync(actual, first.Length, second.Length)
                .ConfigureAwait(false);
            Assert.That(readStream.Read(actual, 0, actual.Length), Is.Zero);
#endif

            Assert.That(firstRead, Is.EqualTo(first.Length));
            Assert.That(secondRead, Is.EqualTo(second.Length));
            Assert.That(actual.AsSpan(0, first.Length).ToArray(), Is.EqualTo(first));
            Assert.That(actual.AsSpan(first.Length, second.Length).ToArray(), Is.EqualTo(second));
        }

        private static byte[] CreatePattern(int length)
        {
            byte[] content = new byte[length];
            for (int ii = 0; ii < content.Length; ii++)
            {
                content[ii] = (byte)((ii * 31) + (ii >> 8));
            }
            return content;
        }

        private const int kLargeFileSize = 4 * 1024 * 1024;
        private const int kWriteBlockSize = 7000;
    }
}
