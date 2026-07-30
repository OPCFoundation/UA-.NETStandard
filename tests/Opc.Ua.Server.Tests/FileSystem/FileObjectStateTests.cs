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
using System.Threading.Tasks;
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
        private Mock<ISession> m_session = null!;
        private NodeId m_sessionId;

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
            m_sessionId = new NodeId("file-session", 0);
            m_session = CreateSession(m_sessionId);
            m_context = m_manager.SystemContext.Copy(m_session.Object);
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

        private static Mock<ISession> CreateSession(NodeId sessionId)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.Identity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return session;
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
        public void OpenReturnsDistinctOpaqueHandles()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");

            uint firstHandle = 0;
            uint secondHandle = 0;
            ServiceResult firstResult = state.Open!.OnCall!(
                m_context, state.Open, state.NodeId, 0x1, ref firstHandle);
            ServiceResult secondResult = state.Open.OnCall!(
                m_context, state.Open, state.NodeId, 0x1, ref secondHandle);

            Assert.That(ServiceResult.IsGood(firstResult), Is.True);
            Assert.That(ServiceResult.IsGood(secondResult), Is.True);
            Assert.That(firstHandle, Is.Not.Zero);
            Assert.That(secondHandle, Is.Not.Zero);
            Assert.That(secondHandle, Is.Not.EqualTo(firstHandle));
        }

        [Test]
        public void OpenWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            uint fileHandle = 0;
            ServiceResult result = state.Open!.OnCall!(
                contextWithoutSession, state.Open, state.NodeId, 0x1, ref fileHandle);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void CloseWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            ServiceResult result = state.Close!.OnCall!(
                contextWithoutSession, state.Close, state.NodeId, fileHandle);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void SetPositionWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            ServiceResult result = state.SetPosition!.OnCall!(
                contextWithoutSession, state.SetPosition, state.NodeId, fileHandle, 1u);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void GetPositionWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            ulong position = 0;
            ServiceResult result = state.GetPosition!.OnCall!(
                contextWithoutSession, state.GetPosition, state.NodeId, fileHandle, ref position);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void ReadWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            ByteString data = default;
            ServiceResult result = state.Read!.OnCall!(
                contextWithoutSession, state.Read, state.NodeId, fileHandle, 1, ref data);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void WriteWithoutSessionReturnsBadSessionIdInvalid()
        {
            FileObjectState state = CreateFileState("out.txt");
            uint fileHandle = 0;
            state.Open!.OnCall!(m_context, state.Open, state.NodeId, 0x2, ref fileHandle);
            ISystemContext contextWithoutSession = m_manager.SystemContext.Copy();

            var payload = ByteString.From([1, 2, 3]);
            ServiceResult result = state.Write!.OnCall!(
                contextWithoutSession, state.Write, state.NodeId, fileHandle, payload);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        public void FileHandleMethodsRejectDifferentSession()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            ServiceResult openResult = state.Open!.OnCall!(
                m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            Assert.That(ServiceResult.IsGood(openResult), Is.True);

            Mock<ISession> otherSession = CreateSession(new NodeId("other-file-session", 0));
            ISystemContext otherContext = m_manager.SystemContext.Copy(otherSession.Object);

            ByteString data = default;
            ServiceResult readResult = state.Read!.OnCall!(
                otherContext, state.Read, state.NodeId, fileHandle, 1, ref data);
            var payload = ByteString.From([1]);
            ServiceResult writeResult = state.Write!.OnCall!(
                otherContext, state.Write, state.NodeId, fileHandle, payload);
            ulong position = 0;
            ServiceResult getPositionResult = state.GetPosition!.OnCall!(
                otherContext, state.GetPosition, state.NodeId, fileHandle, ref position);
            ServiceResult setPositionResult = state.SetPosition!.OnCall!(
                otherContext, state.SetPosition, state.NodeId, fileHandle, 1);
            ServiceResult closeResult = state.Close!.OnCall!(
                otherContext, state.Close, state.NodeId, fileHandle);

            Assert.Multiple(() =>
            {
                Assert.That(readResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(writeResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(getPositionResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(setPositionResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(closeResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task SessionClosingClosesOwnedFileHandlesAsync()
        {
            FileObjectState state = CreateFileState("data.txt", "hello");
            uint fileHandle = 0;
            ServiceResult openResult = state.Open!.OnCall!(
                m_context, state.Open, state.NodeId, 0x1, ref fileHandle);
            Assert.That(ServiceResult.IsGood(openResult), Is.True);

            Mock<ISession> otherSession = CreateSession(new NodeId("other-file-session", 0));
            ISystemContext otherContext = m_manager.SystemContext.Copy(otherSession.Object);
            uint otherFileHandle = 0;
            ServiceResult otherOpenResult = state.Open.OnCall!(
                otherContext, state.Open, state.NodeId, 0x1, ref otherFileHandle);
            Assert.That(ServiceResult.IsGood(otherOpenResult), Is.True);

            var operationContext = new OperationContext(m_session.Object, DiagnosticsMasks.None);
            await m_manager.SessionClosingAsync(
                operationContext,
                m_sessionId,
                deleteSubscriptions: false).ConfigureAwait(false);

            ByteString data = default;
            ServiceResult readResult = state.Read!.OnCall!(
                m_context, state.Read, state.NodeId, fileHandle, 1, ref data);
            ServiceResult otherReadResult = state.Read.OnCall!(
                otherContext, state.Read, state.NodeId, otherFileHandle, 1, ref data);
            Variant openCount = ReadProperty(state.OpenCount!);

            Assert.That(readResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(ServiceResult.IsGood(otherReadResult), Is.True);
            Assert.That(openCount.TryGetValue(out ushort count), Is.True);
            Assert.That(count, Is.EqualTo(1));
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
