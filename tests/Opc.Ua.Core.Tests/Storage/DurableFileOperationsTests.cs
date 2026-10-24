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
using NUnit.Framework;
using Opc.Ua.Storage;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Opc.Ua.Core.Tests.Storage
{
    /// <summary>
    /// Tests immutable-file writes and the real filesystem durability barriers.
    /// </summary>
    [TestFixture]
    [Category("Storage")]
    [Parallelizable]
    public sealed class DurableFileOperationsTests
    {
        /// <summary>
        /// Creates an isolated directory for this test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_testDirectory = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "df-" + Guid.NewGuid().ToString("N"))).FullName;
        }

        /// <summary>
        /// Removes the isolated files owned by this test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_testDirectory))
            {
                Directory.Delete(m_testDirectory, recursive: true);
            }
        }

        /// <summary>
        /// Writes the exact payload across file-buffer boundaries and closes the handle.
        /// </summary>
        [TestCase(1)]
        [TestCase(4095)]
        [TestCase(4096)]
        [TestCase(4097)]
        public async Task WriteNewAsyncWritesExactBytesAndReleasesFile(int length)
        {
            string path = Path.Combine(m_testDirectory, "bytes.bin");
            byte[] expected = CreateBytes(length);

            await DurableFileOperations.WriteNewAsync(path, ByteString.From(expected)).ConfigureAwait(false);

            Assert.That(ReadAllBytesExclusively(path), Is.EqualTo(expected));
        }

        /// <summary>
        /// Treats empty and null ByteStrings as zero-length payloads.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WriteNewAsyncCreatesEmptyFile(bool nullContent)
        {
            string path = Path.Combine(m_testDirectory, "empty.bin");

            await DurableFileOperations.WriteNewAsync(
                path, nullContent ? default : ByteString.Empty).ConfigureAwait(false);

            Assert.That(ReadAllBytesExclusively(path), Is.Empty);
        }

        /// <summary>
        /// Preserves the offset and length of sliced content.
        /// </summary>
        [Test]
        public async Task WriteNewAsyncWritesOnlyByteStringSlice()
        {
            string path = Path.Combine(m_testDirectory, "slice.bin");
            ByteString content = ByteString.From(0xaa, 0x00, 0x01, 0x7f, 0x80, 0xff, 0xbb).Slice(1, 5);
            byte[] expected = [0x00, 0x01, 0x7f, 0x80, 0xff];

            await DurableFileOperations.WriteNewAsync(path, content).ConfigureAwait(false);

            Assert.That(ReadAllBytesExclusively(path), Is.EqualTo(expected));
        }

        /// <summary>
        /// Does not overwrite an existing file or invoke the creation hook.
        /// </summary>
        [Test]
        public async Task WriteNewAsyncRejectsExistingFileWithoutChangingIt()
        {
            string path = Path.Combine(m_testDirectory, "existing.bin");
            byte[] original = [0x37, 0x00, 0xa5, 0xff];
            using (var content = new MemoryStream(original, writable: false))
            using (var existing = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(existing).ConfigureAwait(false);
            }
            int hookCalls = 0;

            await Assert.ThrowsAsync<IOException>(
                async () => await DurableFileOperations.WriteNewAsync(
                    path,
                    ByteString.From(0x12, 0x34),
                    fileCreated: _ => hookCalls++).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(hookCalls, Is.Zero);
            Assert.That(ReadAllBytesExclusively(path), Is.EqualTo(original));
        }

        /// <summary>
        /// Rejects a precancelled write before creating any file.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WriteNewAsyncHonorsPreCancellation(bool emptyContent)
        {
            string path = Path.Combine(m_testDirectory, "cancelled.bin");
            ByteString content = emptyContent ? ByteString.Empty : ByteString.From(0x27, 0x00, 0xff);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            int hookCalls = 0;

            OperationCanceledException error = (await Assert.CatchAsync<OperationCanceledException>(
                async () => await DurableFileOperations.WriteNewAsync(
                    path, content,
                    fileCreated: _ => hookCalls++,
                    cancellationToken: cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(hookCalls, Is.Zero);
            Assert.That(File.Exists(path), Is.False);
        }

        /// <summary>
        /// Invokes the creation hook before any bytes are written.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WriteNewAsyncInvokesHookAfterCreationBeforeWriting(bool emptyContent)
        {
            string path = Path.Combine(m_testDirectory, "hook.bin");
            byte[] expected = emptyContent ? [] : CreateBytes(4097);
            ByteString content = emptyContent ? ByteString.Empty : ByteString.From(expected);
            int hookCalls = 0;
            string? callbackPath = null;
            bool existedAtHook = false;
            long lengthAtHook = -1;

            await DurableFileOperations.WriteNewAsync(
                path,
                content,
                fileCreated: createdPath =>
                {
                    hookCalls++;
                    callbackPath = createdPath;
                    var file = new FileInfo(createdPath);
                    existedAtHook = file.Exists;
                    lengthAtHook = file.Exists ? file.Length : -1;
                }).ConfigureAwait(false);

            Assert.That(hookCalls, Is.EqualTo(1));
            Assert.That(callbackPath, Is.EqualTo(path));
            Assert.That(existedAtHook, Is.True);
            Assert.That(lengthAtHook, Is.Zero);
            Assert.That(ReadAllBytesExclusively(path), Is.EqualTo(expected));
        }

        /// <summary>
        /// Preserves a hook failure without writing and closes the created file.
        /// </summary>
        [Test]
        public async Task WriteNewAsyncPropagatesHookFailureWithoutWriting()
        {
            string path = Path.Combine(m_testDirectory, "hook-failure.bin");
            var expectedError = new InvalidOperationException("Injected file-created hook failure.");
            int hookCalls = 0;
            string? callbackPath = null;

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await DurableFileOperations.WriteNewAsync(
                    path,
                    ByteString.From(0x01, 0x80, 0xff),
                    fileCreated: createdPath =>
                    {
                        hookCalls++;
                        callbackPath = createdPath;
                        throw expectedError;
                    }).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(error, Is.SameAs(expectedError));
            Assert.That(hookCalls, Is.EqualTo(1));
            Assert.That(callbackPath, Is.EqualTo(path));
            Assert.That(ReadAllBytesExclusively(path), Is.Empty);
        }

        /// <summary>
        /// Propagates cancellation observed after creation without publishing content.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WriteNewAsyncHonorsCancellationAfterCreation(bool emptyContent)
        {
            string path = Path.Combine(m_testDirectory, "cancelled-after-create.bin");
            ByteString content = emptyContent ? ByteString.Empty : ByteString.From(0x29, 0xff);
            using var cancellation = new CancellationTokenSource();
            int hookCalls = 0;

            OperationCanceledException error = (await Assert.CatchAsync<OperationCanceledException>(
                async () => await DurableFileOperations.WriteNewAsync(
                    path, content,
                    fileCreated: _ =>
                    {
                        hookCalls++;
                        cancellation.Cancel();
                    },
                    cancellationToken: cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(hookCalls, Is.EqualTo(1));
            Assert.That(ReadAllBytesExclusively(path), Is.Empty);
        }

        /// <summary>
        /// Rejects a missing path without invoking the creation hook.
        /// </summary>
        [Test]
        public async Task WriteNewAsyncRejectsNullPath()
        {
            int hookCalls = 0;
            ArgumentNullException error = (await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await DurableFileOperations.WriteNewAsync(
                    null!, ByteString.Empty,
                    fileCreated: _ => hookCalls++).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(error.ParamName, Is.EqualTo("path"));
            Assert.That(hookCalls, Is.Zero);
        }

        /// <summary>
        /// Exercises the native barrier for ordinary and non-ASCII directory paths.
        /// </summary>
        [TestCase("d")]
        [TestCase("d-\u03bb")]
        public void SynchronizeDirectoryFlushesExistingDirectory(string directoryName)
        {
            string directory = Path.Combine(m_testDirectory, directoryName);
            Directory.CreateDirectory(directory);
            string marker = Path.Combine(directory, "marker.bin");
            byte[] expected = [0x00, 0x46, 0x80, 0xff];
            File.WriteAllBytes(marker, expected);

            // Exercise the actual OS durability barrier; native errors must fail this test.
            DurableFileOperations.SynchronizeDirectory(directory, "durable test store");

            Assert.That(Directory.Exists(directory), Is.True);
            Assert.That(ReadAllBytesExclusively(marker), Is.EqualTo(expected));
        }

        /// <summary>
        /// Reports native failure without creating the missing directory.
        /// </summary>
        [TestCase("RSE test store")]
        [TestCase("Other durable store")]
        public void SynchronizeDirectoryReportsMissingDirectory(string storageDescription)
        {
            string path = Path.Combine(m_testDirectory, "missing");

            IOException error = Assert.Throws<IOException>(
                () => DurableFileOperations.SynchronizeDirectory(path, storageDescription))!;

            Assert.That(error.Message, Does.Contain(storageDescription));
            Assert.That(error.Message, Does.Contain(path));
            Assert.That(error.InnerException, Is.TypeOf<Win32Exception>());
            Assert.That(((Win32Exception)error.InnerException!).NativeErrorCode, Is.Not.Zero);
            Assert.That(Directory.Exists(path), Is.False);
        }

        /// <summary>
        /// Does not treat a path through a regular file as a synchronized directory.
        /// </summary>
        [Test]
        public void SynchronizeDirectoryRejectsPathBelowAFile()
        {
            const string storageDescription = "invalid-path test store";
            string parentFile = Path.Combine(m_testDirectory, "file.bin");
            byte[] original = [0x13, 0x00, 0xfe];
            File.WriteAllBytes(parentFile, original);
            string path = Path.Combine(parentFile, "child");

            IOException error = Assert.Throws<IOException>(
                () => DurableFileOperations.SynchronizeDirectory(path, storageDescription))!;

            Assert.That(error.Message, Does.Contain(storageDescription));
            Assert.That(error.Message, Does.Contain(path));
            Assert.That(error.InnerException, Is.TypeOf<Win32Exception>());
            Assert.That(((Win32Exception)error.InnerException!).NativeErrorCode, Is.Not.Zero);
            Assert.That(ReadAllBytesExclusively(parentFile), Is.EqualTo(original));
        }

        /// <summary>
        /// Rejects a null directory path.
        /// </summary>
        [Test]
        public void SynchronizeDirectoryRejectsNullPath()
        {
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(
                () => DurableFileOperations.SynchronizeDirectory(null!, "test store"))!;

            Assert.That(error.ParamName, Is.EqualTo("path"));
        }

        /// <summary>
        /// Rejects a missing diagnostic description.
        /// </summary>
        [Test]
        public void SynchronizeDirectoryRejectsNullDescription()
        {
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(
                () => DurableFileOperations.SynchronizeDirectory(m_testDirectory, null!))!;

            Assert.That(error.ParamName, Is.EqualTo("storageDescription"));
        }

        private static byte[] CreateBytes(int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 256);
            }
            return bytes;
        }

        private static byte[] ReadAllBytesExclusively(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using var content = new MemoryStream();
            stream.CopyTo(content);
            return content.ToArray();
        }

        private string m_testDirectory = null!;
    }
}
