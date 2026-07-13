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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Deterministic offline tests for <see cref="FileObjectState"/> that drive
    /// the FileType method handlers (Open/Read/Write/Close/Get/SetPosition), the
    /// metadata property read hooks and the browser population against a physical
    /// provider rooted at a temp directory.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class FileObjectStateTests
    {
        private string m_root = null!;
        private ITelemetryContext m_telemetry = null!;
        private FileSystemNodeManager m_manager = null!;
        private ISystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "fos-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            m_telemetry = NUnitTelemetryContext.Create();

            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            var provider = new PhysicalFileSystemProvider(m_root, "TestMount");
            m_manager = new FileSystemNodeManager(mockServer.Object, new ApplicationConfiguration(), provider);
            m_context = m_manager.SystemContext;
        }

        [TearDown]
        public void TearDown()
        {
            m_manager?.Dispose();
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

        private FileObjectState CreateFileState(string providerPath, string? content = null)
        {
            string full = Path.Combine(m_root, providerPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content ?? string.Empty);
            NodeId nodeId = FileSystemNodeId.BuildFile(providerPath, m_manager.NamespaceIndex);
            return new FileObjectState(m_context, nodeId, providerPath, Path.GetFileName(providerPath));
        }

        [Test]
        public void OpenForReadThenReadReturnsFileContent()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            uint fileHandle = 0;
            ServiceResult openResult = state.Open!.OnCall!(
                m_context, state.Open, state.NodeId, 0x1, ref fileHandle);

            Assert.That(ServiceResult.IsGood(openResult), Is.True);
            Assert.That(fileHandle, Is.GreaterThan(0u));

            ByteString data = default;
            ServiceResult readResult = state.Read!.OnCall!(
                m_context, state.Read, state.NodeId, fileHandle, 5, ref data);

            Assert.That(ServiceResult.IsGood(readResult), Is.True);
            Assert.That(Encoding.UTF8.GetString(data.ToArray()), Is.EqualTo("hello"));
        }

        [Test]
        public void ReadWithShortRemainderTrimsBuffer()
        {
            FileObjectState state = CreateFileState("data.txt", "abc");

            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);

            ByteString data = default;
            ServiceResult readResult = state.Read!.OnCall!(
                m_context, state.Read, state.NodeId, fileHandle, 100, ref data);

            Assert.That(ServiceResult.IsGood(readResult), Is.True);
            Assert.That(data.Length, Is.EqualTo(3));
        }

        [Test]
        public void ReadWithNegativeLengthReturnsBadInvalidArgument()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);

            ByteString data = default;
            ServiceResult result = state.Read!.OnCall!(
                m_context, state.Read, state.NodeId, fileHandle, -1, ref data);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void ReadWithUnknownHandleReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            ByteString data = default;
            ServiceResult result = state.Read!.OnCall!(
                m_context, state.Read, state.NodeId, 999u, 4, ref data);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void OpenForWriteThenWritePersistsData()
        {
            FileObjectState state = CreateFileState("out.txt");

            uint fileHandle = 0;
            ServiceResult openResult = state.Open!.OnCall!(
                m_context, state.Open, state.NodeId, 0x2, ref fileHandle);
            Assert.That(ServiceResult.IsGood(openResult), Is.True);

            var payload = ByteString.From(Encoding.UTF8.GetBytes("written"));
            ServiceResult writeResult = state.Write!.OnCall!(
                m_context, state.Write, state.NodeId, fileHandle, payload);
            Assert.That(ServiceResult.IsGood(writeResult), Is.True);

            ServiceResult closeResult = state.Close!.OnCall!(
                m_context, state.Close, state.NodeId, fileHandle);
            Assert.That(ServiceResult.IsGood(closeResult), Is.True);

            Assert.That(File.ReadAllText(Path.Combine(m_root, "out.txt")), Is.EqualTo("written"));
        }

        [Test]
        public void WriteWithUnknownHandleReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("out.txt");

            var payload = ByteString.From(new byte[] { 1, 2, 3 });
            ServiceResult result = state.Write!.OnCall!(
                m_context, state.Write, state.NodeId, 999u, payload);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void SetPositionAndGetPositionRoundTrip()
        {
            FileObjectState state = CreateFileState("data.txt", "0123456789");

            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);

            ServiceResult setResult = state.SetPosition!.OnCall!(
                m_context, state.SetPosition, state.NodeId, fileHandle, 3u);
            Assert.That(ServiceResult.IsGood(setResult), Is.True);

            ulong position = 0;
            ServiceResult getResult = state.GetPosition!.OnCall!(
                m_context, state.GetPosition, state.NodeId, fileHandle, ref position);
            Assert.That(ServiceResult.IsGood(getResult), Is.True);
            Assert.That(position, Is.EqualTo(3u));
        }

        [Test]
        public void SetPositionWithUnknownHandleReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            ServiceResult result = state.SetPosition!.OnCall!(
                m_context, state.SetPosition, state.NodeId, 999u, 1u);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void GetPositionWithUnknownHandleReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            ulong position = 0;
            ServiceResult result = state.GetPosition!.OnCall!(
                m_context, state.GetPosition, state.NodeId, 999u, ref position);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CloseWithInvalidHandleReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            ServiceResult result = state.Close!.OnCall!(
                m_context, state.Close, state.NodeId, 999u);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void SizePropertyReadReturnsFileLength()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            Variant value = ReadProperty(state.Size!);

            Assert.That(value.TryGetValue(out ulong size), Is.True);
            Assert.That(size, Is.EqualTo(5u));
        }

        [Test]
        public void WritablePropertyReadReturnsTrueForWritableProvider()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            Variant value = ReadProperty(state.Writable!);

            Assert.That(value.TryGetValue(out bool writable), Is.True);
            Assert.That(writable, Is.True);
        }

        [Test]
        public void MimeTypePropertyReadReturnsValue()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            var value = default(Variant);
            var statusCode = default(StatusCode);
            var timestamp = default(DateTimeUtc);
            ServiceResult result = state.MimeType!.OnReadValue!(
                m_context, state.MimeType, default, QualifiedName.Null,
                ref value, ref statusCode, ref timestamp);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(statusCode.Code, Is.EqualTo(StatusCodes.Uncertain));
        }

        [Test]
        public void LastModifiedTimePropertyReadReturnsValue()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            Variant value = ReadProperty(state.LastModifiedTime!);

            Assert.That(value.TryGetValue(out DateTimeUtc modified), Is.True);
            Assert.That(modified, Is.Not.Default);
        }

        [Test]
        public void OpenCountPropertyReadReturnsZeroWhenNotOpen()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            Variant value = ReadProperty(state.OpenCount!);

            Assert.That(value.TryGetValue(out ushort count), Is.True);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void OpenWithUnavailableManagerReturnsBadInvalidState()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            var orphanContext = new SystemContext(m_telemetry) { SystemHandle = null };

            uint fileHandle = 0;
            ServiceResult result = state.Open!.OnCall!(
                orphanContext, state.Open, state.NodeId, 0x1, ref fileHandle);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void PopulateBrowserAddsParentReferenceForNestedFile()
        {
            FileObjectState state = CreateFileState("folder/data.txt", "hello");

            using INodeBrowser browser = state.CreateBrowser(
                m_context, null, ReferenceTypeIds.HasComponent, true,
                BrowseDirection.Inverse, state.BrowseName, null, false);

            bool foundParent = false;
            for (IReference? reference = browser.Next(); reference != null; reference = browser.Next())
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.HasComponent && reference.IsInverse)
                {
                    foundParent = true;
                }
            }

            Assert.That(foundParent, Is.True);
        }

        private Variant ReadProperty(BaseVariableState variable)
        {
            var value = default(Variant);
            var statusCode = default(StatusCode);
            var timestamp = default(DateTimeUtc);
            ServiceResult result = variable.OnReadValue!(
                m_context, variable, default, QualifiedName.Null,
                ref value, ref statusCode, ref timestamp);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            return value;
        }
    }
}
