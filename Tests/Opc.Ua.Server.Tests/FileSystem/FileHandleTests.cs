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

#nullable enable

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for the internal <see cref="FileHandle"/> against a
    /// <see cref="PhysicalFileSystemProvider"/> rooted at a temp directory.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class FileHandleTests
    {
        private const byte ModeRead = 0x1;
        private const byte ModeWrite = 0x2;
        private const byte ModeEraseExisting = 0x4;
        private const byte ModeAppend = 0x8;

        private string m_root = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "fh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // best-effort
            }
        }

        private PhysicalFileSystemProvider CreateProvider(bool isWritable = true)
        {
            return new PhysicalFileSystemProvider(m_root, "mount", isWritable);
        }

        private void WriteFile(string name, string content)
        {
            File.WriteAllText(Path.Combine(m_root, name), content);
        }

        [Test]
        public void OpenWithNoModeReturnsBadInvalidArgument()
        {
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            ServiceResult result = handle.Open(0x0, out uint fileHandle);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void OpenWithReadAndWriteReturnsBadInvalidArgument()
        {
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            ServiceResult result = handle.Open((byte)(ModeRead | ModeWrite), out _);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OpenWriteOnReadOnlyProviderReturnsBadUserAccessDenied()
        {
            WriteFile("f.txt", "abc");
            using var handle = new FileHandle(CreateProvider(isWritable: false), "f.txt");

            ServiceResult result = handle.Open(ModeWrite, out _);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OpenReadForMissingFileReturnsBadNotFound()
        {
            using var handle = new FileHandle(CreateProvider(), "missing.txt");

            ServiceResult result = handle.Open(ModeRead, out _);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
        }

        [Test]
        public void OpenReadSucceedsAndDispensesHandle()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            ServiceResult result = handle.Open(ModeRead, out uint fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.GreaterThan(0u));
            Assert.That(handle.OpenCount, Is.EqualTo(1));
            Assert.That(handle.GetStream(fileHandle), Is.Not.Null);
        }

        [Test]
        public void MultipleReadersGetDistinctHandles()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            handle.Open(ModeRead, out uint first);
            handle.Open(ModeRead, out uint second);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(handle.OpenCount, Is.EqualTo(2));
        }

        [Test]
        public void OpenWriteWhileReadingReturnsBadInvalidState()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeRead, out _);

            ServiceResult result = handle.Open(ModeWrite, out _);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void OpenReadWhileWritingReturnsBadInvalidState()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeWrite, out _);

            ServiceResult result = handle.Open(ModeRead, out _);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void OpenWriteReturnsHandleOne()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            ServiceResult result = handle.Open(ModeWrite, out uint fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.EqualTo(1u));
            Assert.That(handle.GetStream(1u), Is.Not.Null);
        }

        [Test]
        public void OpenWriteWithEraseExistingTruncates()
        {
            WriteFile("f.txt", "existing-content");
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            handle.Open((byte)(ModeWrite | ModeEraseExisting), out uint fileHandle);
            Stream stream = handle.GetStream(fileHandle)!;
            byte[] payload = Encoding.UTF8.GetBytes("new");
            stream.Write(payload, 0, payload.Length);
            handle.Close(fileHandle);

            Assert.That(File.ReadAllText(Path.Combine(m_root, "f.txt")), Is.EqualTo("new"));
        }

        [Test]
        public void OpenWriteWithAppendKeepsExistingContent()
        {
            WriteFile("f.txt", "a");
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            handle.Open((byte)(ModeWrite | ModeAppend), out uint fileHandle);
            Stream stream = handle.GetStream(fileHandle)!;
            byte[] payload = Encoding.UTF8.GetBytes("b");
            stream.Write(payload, 0, payload.Length);
            handle.Close(fileHandle);

            Assert.That(File.ReadAllText(Path.Combine(m_root, "f.txt")), Is.EqualTo("ab"));
        }

        [Test]
        public void GetStreamReturnsNullForUnknownHandle()
        {
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            Assert.That(handle.GetStream(999u), Is.Null);
        }

        [Test]
        public void CloseUnknownHandleReturnsFalse()
        {
            using var handle = new FileHandle(CreateProvider(), "f.txt");

            Assert.That(handle.Close(42u), Is.False);
        }

        [Test]
        public void CloseReaderReleasesHandle()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeRead, out uint fileHandle);

            bool closed = handle.Close(fileHandle);

            Assert.That(closed, Is.True);
            Assert.That(handle.OpenCount, Is.Zero);
            Assert.That(handle.GetStream(fileHandle), Is.Null);
        }

        [Test]
        public void CloseWriterReleasesHandle()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeWrite, out uint fileHandle);

            bool closed = handle.Close(fileHandle);

            Assert.That(closed, Is.True);
            Assert.That(handle.OpenCount, Is.Zero);
        }

        [Test]
        public void MetadataPropertiesReflectFile()
        {
            WriteFile("data.json", "{}");
            using var handle = new FileHandle(CreateProvider(), "data.json");

            Assert.That(handle.Length, Is.EqualTo(2));
            Assert.That(handle.MimeType, Is.EqualTo("application/json"));
            Assert.That(handle.LastModifiedTime, Is.GreaterThan(DateTime.MinValue));
            Assert.That(handle.IsWriteable, Is.True);
        }

        [Test]
        public void MetadataPropertiesReturnDefaultsForMissingFile()
        {
            using var handle = new FileHandle(CreateProvider(), "gone.txt");

            Assert.That(handle.Length, Is.Zero);
            Assert.That(handle.MimeType, Is.Empty);
            Assert.That(handle.LastModifiedTime, Is.EqualTo(DateTime.MinValue));
            Assert.That(handle.IsWriteable, Is.False);
        }

        [Test]
        public void IsWriteableIsFalseWhileOpen()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeRead, out _);

            Assert.That(handle.IsWriteable, Is.False);
        }

        [Test]
        public void IsWriteableIsFalseForReadOnlyProvider()
        {
            WriteFile("f.txt", "hello");
            using var handle = new FileHandle(CreateProvider(isWritable: false), "f.txt");

            Assert.That(handle.IsWriteable, Is.False);
        }

        [Test]
        public void DisposeClosesAllStreams()
        {
            WriteFile("f.txt", "hello");
            var handle = new FileHandle(CreateProvider(), "f.txt");
            handle.Open(ModeRead, out _);
            handle.Open(ModeRead, out _);

            handle.Dispose();

            Assert.That(handle.OpenCount, Is.Zero);
        }
    }
}
