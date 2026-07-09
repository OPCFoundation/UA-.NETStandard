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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Deterministic offline tests for <see cref="DirectoryObjectState"/> that drive
    /// the FileDirectoryType method handlers (CreateFile / CreateDirectory /
    /// DeleteFileSystemObject / MoveOrCopy) and browser population against a physical
    /// provider rooted at a temp directory.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class DirectoryObjectStateTests
    {
        private string m_root = null!;
        private ITelemetryContext m_telemetry = null!;
        private FileSystemNodeManager m_manager = null!;
        private ISystemContext m_context = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "dos-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            m_telemetry = NUnitTelemetryContext.Create();

            UseProvider(new PhysicalFileSystemProvider(m_root, "TestMount"));
        }

        [TearDown]
        public void TearDown()
        {
            m_manager?.Dispose();
            m_manager = null!;
            m_context = null!;
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

        private void UseProvider(IFileSystemProvider provider)
        {
            m_manager?.Dispose();
            Mock<IServerInternal> mockServer = DeterministicServerMock.Create(out _);
            mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_manager = new FileSystemNodeManager(mockServer.Object, new ApplicationConfiguration(), provider);
            m_context = m_manager.SystemContext;
        }

        private DirectoryObjectState CreateRootDirectory()
        {
            NodeId nodeId = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);
            return new DirectoryObjectState(m_context, nodeId, string.Empty, "Root", isRoot: true);
        }

        private SystemContext OrphanContext()
        {
            return new SystemContext(m_telemetry) { SystemHandle = null };
        }

        [Test]
        public async Task CreateDirectoryCreatesDirectoryAndReturnsNodeIdAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateDirectoryMethodStateResult result = await state.CreateDirectory!.OnCallAsync!(
                m_context, state.CreateDirectory, state.NodeId, "newdir", CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.DirectoryNodeId, Is.Not.EqualTo(NodeId.Null));
            Assert.That(Directory.Exists(Path.Combine(m_root, "newdir")), Is.True);
        }

        [Test]
        public async Task CreateDirectoryWithEmptyNameReturnsBadInvalidArgumentAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateDirectoryMethodStateResult result = await state.CreateDirectory!.OnCallAsync!(
                m_context, state.CreateDirectory, state.NodeId, string.Empty, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateDirectoryWithUnavailableManagerReturnsBadInvalidStateAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateDirectoryMethodStateResult result = await state.CreateDirectory!.OnCallAsync!(
                OrphanContext(), state.CreateDirectory, state.NodeId, "x", CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task CreateFileWithoutOpenCreatesFileAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateFileMethodStateResult result = await state.CreateFile!.OnCallAsync!(
                m_context, state.CreateFile, state.NodeId, "new.txt", false, CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.FileNodeId, Is.Not.EqualTo(NodeId.Null));
            Assert.That(result.FileHandle, Is.Zero);
            Assert.That(File.Exists(Path.Combine(m_root, "new.txt")), Is.True);
        }

        [Test]
        public async Task CreateFileWithOpenReturnsFileHandleAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateFileMethodStateResult result = await state.CreateFile!.OnCallAsync!(
                m_context, state.CreateFile, state.NodeId, "opened.txt", true, CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.FileHandle, Is.GreaterThan(0u));
        }

        [Test]
        public async Task CreateFileWithEmptyNameReturnsBadInvalidArgumentAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateFileMethodStateResult result = await state.CreateFile!.OnCallAsync!(
                m_context, state.CreateFile, state.NodeId, string.Empty, false, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateFileWithUnavailableManagerReturnsBadInvalidStateAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();

            CreateFileMethodStateResult result = await state.CreateFile!.OnCallAsync!(
                OrphanContext(), state.CreateFile, state.NodeId, "x.txt", false, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task DeleteFileSystemObjectDeletesFileAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "del.txt"), "x");
            NodeId target = FileSystemNodeId.BuildFile("del.txt", m_manager.NamespaceIndex);

            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                m_context, state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "del.txt")), Is.False);
        }

        [Test]
        public async Task DeleteFileSystemObjectRejectsRootAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId target = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                m_context, state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task DeleteFileSystemObjectWithNonFileSystemNodeReturnsBadInvalidStateAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            var target = new NodeId(42);

            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                m_context, state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task DeleteFileSystemObjectForMissingFileReturnsBadNotFoundAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId target = FileSystemNodeId.BuildFile("missing.txt", m_manager.NamespaceIndex);

            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                m_context, state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task DeleteFileSystemObjectWithUnavailableManagerReturnsBadInvalidStateAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId target = FileSystemNodeId.BuildFile("del.txt", m_manager.NamespaceIndex);

            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                OrphanContext(), state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task MoveOrCopyMovesFileAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "src.txt"), "payload");
            NodeId source = FileSystemNodeId.BuildFile("src.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, false, "moved.txt",
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.NewNodeId, Is.Not.EqualTo(NodeId.Null));
            Assert.That(File.Exists(Path.Combine(m_root, "moved.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "src.txt")), Is.False);
        }

        [Test]
        public async Task MoveOrCopyCopiesFileAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "orig.txt"), "payload");
            NodeId source = FileSystemNodeId.BuildFile("orig.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, true, "copy.txt",
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "copy.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "orig.txt")), Is.True);
        }

        [Test]
        public async Task MoveOrCopyUsesSourceNameWhenNewNameEmptyAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            Directory.CreateDirectory(Path.Combine(m_root, "sub"));
            File.WriteAllText(Path.Combine(m_root, "sub", "keep.txt"), "payload");
            NodeId source = FileSystemNodeId.BuildFile("sub/keep.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, true, string.Empty,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "keep.txt")), Is.True);
        }

        [Test]
        public async Task MoveOrCopyWithRootSourceReturnsBadInvalidArgumentAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId source = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, false, "x",
                CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task MoveOrCopyWithFileTargetReturnsBadInvalidArgumentAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "src.txt"), "payload");
            NodeId source = FileSystemNodeId.BuildFile("src.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildFile("target.txt", m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, false, "x",
                CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task MoveOrCopyWithUnavailableManagerReturnsBadInvalidStateAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId source = FileSystemNodeId.BuildFile("src.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                OrphanContext(), state.MoveOrCopy, state.NodeId, source, targetDir, false, "x",
                CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task CreateDirectoryOverExistingFileReturnsBadBrowseNameDuplicatedAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "clash"), "x");

            CreateDirectoryMethodStateResult result = await state.CreateDirectory!.OnCallAsync!(
                m_context, state.CreateDirectory, state.NodeId, "clash", CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task CreateFileOverExistingDirectoryReturnsBadBrowseNameDuplicatedAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            Directory.CreateDirectory(Path.Combine(m_root, "clashdir"));

            CreateFileMethodStateResult result = await state.CreateFile!.OnCallAsync!(
                m_context, state.CreateFile, state.NodeId, "clashdir", false, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task DeleteFileSystemObjectWhenProviderDeniesDeleteReturnsBadAsync()
        {
            var provider = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            provider.SetupGet(p => p.MountName).Returns("TestMount");
            provider.SetupGet(p => p.IsWritable).Returns(true);
            provider
                .Setup(p => p.DeleteAsync("locked.txt", It.IsAny<CancellationToken>()))
                .Returns<string, CancellationToken>((_, _) => throw new IOException("locked"));
            UseProvider(provider.Object);

            DirectoryObjectState state = CreateRootDirectory();
            NodeId target = FileSystemNodeId.BuildFile("locked.txt", m_manager.NamespaceIndex);
            DeleteFileMethodStateResult result = await state.DeleteFileSystemObject!.OnCallAsync!(
                m_context, state.DeleteFileSystemObject, state.NodeId, target, CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            provider.Verify(
                p => p.DeleteAsync("locked.txt", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task MoveOrCopyMissingSourceReturnsBadNotFoundAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            NodeId source = FileSystemNodeId.BuildFile("ghost.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, false, "x.txt",
                CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task MoveOrCopyToExistingTargetReturnsBadBrowseNameDuplicatedAsync()
        {
            DirectoryObjectState state = CreateRootDirectory();
            File.WriteAllText(Path.Combine(m_root, "src.txt"), "a");
            File.WriteAllText(Path.Combine(m_root, "dest.txt"), "b");
            NodeId source = FileSystemNodeId.BuildFile("src.txt", m_manager.NamespaceIndex);
            NodeId targetDir = FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await state.MoveOrCopy!.OnCallAsync!(
                m_context, state.MoveOrCopy, state.NodeId, source, targetDir, false, "dest.txt",
                CancellationToken.None);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void CreateBrowserForRootAddsFileSystemParentReference()
        {
            DirectoryObjectState state = CreateRootDirectory();

            using INodeBrowser browser = state.CreateBrowser(
                m_context, null, ReferenceTypeIds.HasComponent, true,
                BrowseDirection.Inverse, state.BrowseName, null, false);

            bool foundFileSystemParent = false;
            for (IReference? reference = browser.Next(); reference != null; reference = browser.Next())
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                    reference.IsInverse &&
                    reference.TargetId == ObjectIds.FileSystem)
                {
                    foundFileSystemParent = true;
                }
            }

            Assert.That(foundFileSystemParent, Is.True);
        }

        [Test]
        public void CreateBrowserForNestedDirectoryAddsParentReference()
        {
            NodeId nodeId = FileSystemNodeId.BuildDirectory("parent/child", m_manager.NamespaceIndex);
            var state = new DirectoryObjectState(m_context, nodeId, "parent/child", "child", isRoot: false);

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
    }
}
