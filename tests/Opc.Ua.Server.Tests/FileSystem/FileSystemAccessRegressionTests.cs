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
    /// Verifies Part 20 scoping, locking, mode validation and user access rules of the mounted file system.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileSystemAccessRegressionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "fs-access-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(m_root, "a", "b"));
            Directory.CreateDirectory(Path.Combine(m_root, "c"));
            File.WriteAllText(Path.Combine(m_root, "a", "x.txt"), "x");
            File.WriteAllText(Path.Combine(m_root, "a", "b", "y.txt"), "y");
            File.WriteAllText(Path.Combine(m_root, "c", "secret.txt"), "secret");
            m_server = DeterministicServerMock.Create(out m_queues);
            m_server.SetupGet(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_manager = new FileSystemNodeManager(
                m_server.Object,
                new ApplicationConfiguration(),
                new PhysicalFileSystemProvider(m_root, "AccessRegression"));
            m_manager.SystemContext.SessionId = new NodeId(100, 1);
        }

        [TearDown]
        public void TearDown()
        {
            m_manager.Dispose();
            m_queues.Dispose();
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// S12-1: a directory can only delete objects it organizes; other objects report Bad_NotFound.
        /// </summary>
        [Test]
        public async Task DeleteRejectsObjectsNotOrganizedByTheDirectoryAsync()
        {
            DirectoryObjectState a = CreateDirectory("a");

            ServiceResult sibling = await DeleteAsync(a, FileId("c/secret.txt")).ConfigureAwait(false);
            ServiceResult nested = await DeleteAsync(CreateRoot(), FileId("a/x.txt")).ConfigureAwait(false);
            ServiceResult unknown = await DeleteAsync(a, new NodeId("unknown", 0)).ConfigureAwait(false);
            ServiceResult child = await DeleteAsync(a, FileId("a/x.txt")).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(sibling.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(nested.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(unknown.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(ServiceResult.IsGood(child), Is.True, child.ToString());
                Assert.That(System.IO.File.Exists(Path.Combine(m_root, "c", "secret.txt")), Is.True);
                Assert.That(System.IO.File.Exists(Path.Combine(m_root, "a", "x.txt")), Is.False);
            });
        }

        /// <summary>
        /// S12-1: MoveOrCopy only accepts objects organized by the called directory.
        /// </summary>
        [Test]
        public async Task MoveOrCopyRejectsObjectsNotOrganizedByTheDirectoryAsync()
        {
            DirectoryObjectState a = CreateDirectory("a");

            MoveOrCopyMethodStateResult result = await a.MoveOrCopy!.OnCallAsync!(
                m_manager.SystemContext, a.MoveOrCopy, a.NodeId, FileId("c/secret.txt"), a.NodeId,
                false, "stolen.txt", CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(System.IO.File.Exists(Path.Combine(m_root, "c", "secret.txt")), Is.True);
        }

        /// <summary>
        /// S12-2: an open file, or a directory containing one, is locked and cannot be deleted or moved.
        /// </summary>
        [Test]
        public async Task DeleteAndMoveRejectOpenFilesAsync()
        {
            var file = new FileObjectState(m_manager.SystemContext, FileId("a/b/y.txt"), "a/b/y.txt", "y.txt");
            uint handle = await FileReadRegressionTests.OpenAsync(file, m_manager.SystemContext)
                .ConfigureAwait(false);

            ServiceResult deleteFile = await DeleteAsync(CreateDirectory("a/b"), FileId("a/b/y.txt"))
                .ConfigureAwait(false);
            ServiceResult deleteDirectory = await DeleteAsync(CreateDirectory("a"), DirId("a/b"))
                .ConfigureAwait(false);
            DirectoryObjectState root = CreateRoot();
            MoveOrCopyMethodStateResult move = await root.MoveOrCopy!.OnCallAsync!(
                m_manager.SystemContext, root.MoveOrCopy, root.NodeId, DirId("a"), DirId("c"),
                false, string.Empty, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(deleteFile.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(deleteDirectory.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(move.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(System.IO.File.Exists(Path.Combine(m_root, "a", "b", "y.txt")), Is.True);
            });

            (ServiceResult closed, _) = await FileReadRegressionTests.CallAsync(
                file.Close, m_manager.SystemContext, file.NodeId, [handle]).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closed), Is.True);
            ServiceResult deleted = await DeleteAsync(CreateDirectory("a"), DirId("a/b")).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(deleted), Is.True, deleted.ToString());
        }

        /// <summary>
        /// S12-3: reserved mode bits and EraseExisting without Write are invalid; a reader
        /// cannot open a file that is open for writing.
        /// </summary>
        [Test]
        public async Task OpenValidatesModeAndReportsNotReadableAsync()
        {
            var file = new FileObjectState(m_manager.SystemContext, FileId("a/x.txt"), "a/x.txt", "x.txt");

            (ServiceResult eraseWithoutWrite, _) = await FileReadRegressionTests.CallAsync(
                file.Open, m_manager.SystemContext, file.NodeId, [(byte)0x05]).ConfigureAwait(false);
            (ServiceResult reservedBits, _) = await FileReadRegressionTests.CallAsync(
                file.Open, m_manager.SystemContext, file.NodeId, [(byte)0x11]).ConfigureAwait(false);
            await FileReadRegressionTests.OpenAsync(file, m_manager.SystemContext, 0x02).ConfigureAwait(false);
            (ServiceResult readWhileWriting, _) = await FileReadRegressionTests.CallAsync(
                file.Open, m_manager.SystemContext, file.NodeId, [(byte)0x01]).ConfigureAwait(false);
            (ServiceResult writeWhileWriting, _) = await FileReadRegressionTests.CallAsync(
                file.Open, m_manager.SystemContext, file.NodeId, [(byte)0x02]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(eraseWithoutWrite.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(reservedBits.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(readWhileWriting.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));
                Assert.That(writeWhileWriting.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        /// <summary>
        /// S12-4: an anonymous user cannot modify the mount, and UserWritable / UserExecutable reflect it.
        /// </summary>
        [Test]
        public async Task AnonymousUserCannotModifyTheMountAsync()
        {
            m_manager.SystemContext.UserIdentity = new UserIdentity();
            DirectoryObjectState a = CreateDirectory("a");
            var file = new FileObjectState(m_manager.SystemContext, FileId("a/x.txt"), "a/x.txt", "x.txt");

            CreateFileMethodStateResult create = await a.CreateFile!.OnCallAsync!(
                m_manager.SystemContext, a.CreateFile, a.NodeId, "new.txt", false, CancellationToken.None)
                .ConfigureAwait(false);
            ServiceResult delete = await DeleteAsync(a, FileId("a/x.txt")).ConfigureAwait(false);
            (ServiceResult openWrite, _) = await FileReadRegressionTests.CallAsync(
                file.Open, m_manager.SystemContext, file.NodeId, [(byte)0x02]).ConfigureAwait(false);
            uint readHandle = await FileReadRegressionTests.OpenAsync(file, m_manager.SystemContext)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(create.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(delete.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(openWrite.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(readHandle, Is.Not.Zero);
                Assert.That(ReadBoolean(file.Writable), Is.True);
                Assert.That(ReadBoolean(file.UserWritable), Is.False);
                Assert.That(ReadUserExecutable(a.DeleteFileSystemObject), Is.False);
                Assert.That(ReadUserExecutable(file.Write), Is.False);
                Assert.That(System.IO.File.Exists(Path.Combine(m_root, "a", "x.txt")), Is.True);
            });
        }

        /// <summary>
        /// S12-4: authenticated users, and anonymous users when explicitly allowed, can modify the mount.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AuthenticatedOrExplicitlyAllowedUserCanModifyTheMountAsync(bool anonymousAllowed)
        {
            m_manager.AllowAnonymousWrite = anonymousAllowed;
            m_manager.SystemContext.UserIdentity = anonymousAllowed
                ? new UserIdentity()
                : new UserIdentity("operator", "secret"u8);
            DirectoryObjectState a = CreateDirectory("a");
            var file = new FileObjectState(m_manager.SystemContext, FileId("a/x.txt"), "a/x.txt", "x.txt");

            Assert.That(ReadBoolean(file.UserWritable), Is.True);
            Assert.That(ReadUserExecutable(a.DeleteFileSystemObject), Is.True);
            ServiceResult delete = await DeleteAsync(a, FileId("a/x.txt")).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(delete), Is.True, delete.ToString());
        }

        /// <summary>
        /// S12-5: copying or moving a directory into its own subtree is rejected.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task MoveOrCopyIntoOwnSubtreeIsRejectedAsync(bool createCopy)
        {
            DirectoryObjectState root = CreateRoot();

            MoveOrCopyMethodStateResult result = await root.MoveOrCopy!.OnCallAsync!(
                m_manager.SystemContext, root.MoveOrCopy, root.NodeId, DirId("a"), DirId("a/b"),
                createCopy, string.Empty, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(System.IO.Directory.Exists(Path.Combine(m_root, "a", "b", "a")), Is.False);
        }

        private NodeId FileId(string providerPath)
        {
            return FileSystemNodeId.BuildFile(providerPath, m_manager.NamespaceIndex);
        }

        private NodeId DirId(string providerPath)
        {
            return FileSystemNodeId.BuildDirectory(providerPath, m_manager.NamespaceIndex);
        }

        private DirectoryObjectState CreateRoot()
        {
            return new DirectoryObjectState(m_manager.SystemContext,
                FileSystemNodeId.BuildRoot(m_manager.NamespaceIndex), string.Empty, "Root", isRoot: true);
        }

        private DirectoryObjectState CreateDirectory(string providerPath)
        {
            return new DirectoryObjectState(m_manager.SystemContext,
                DirId(providerPath), providerPath, Path.GetFileName(providerPath), isRoot: false);
        }

        private async Task<ServiceResult> DeleteAsync(DirectoryObjectState directory, NodeId objectToDelete)
        {
            DeleteFileMethodStateResult result = await directory.DeleteFileSystemObject!.OnCallAsync!(
                m_manager.SystemContext, directory.DeleteFileSystemObject, directory.NodeId, objectToDelete,
                CancellationToken.None).ConfigureAwait(false);
            return result.ServiceResult;
        }

        private bool ReadBoolean(NodeState node)
        {
            return ReadBooleanAttribute(node, Attributes.Value);
        }

        private bool ReadUserExecutable(NodeState method)
        {
            return ReadBooleanAttribute(method, Attributes.UserExecutable);
        }

        private bool ReadBooleanAttribute(NodeState node, uint attributeId)
        {
            (ServiceResult result, DataValue value) = node.ReadAttributeAsync(
                m_manager.SystemContext, attributeId, NumericRange.Null, QualifiedName.Null, default)
                .AsTask().GetAwaiter().GetResult();
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out bool flag), Is.True);
            return flag;
        }

        private string m_root;
        private Mock<IServerInternal> m_server;
        private MonitoredItemQueueFactory m_queues;
        private FileSystemNodeManager m_manager;
    }
}
