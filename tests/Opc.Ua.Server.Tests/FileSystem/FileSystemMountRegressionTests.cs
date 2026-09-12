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
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileSystemMountRegressionTests
    {
        [Test]
        public async Task AlternateRootIdsCannotDeleteMoveOrCopyMountAsync(
            [Values("delete", "move", "copy")] string operation,
            [Values("0:", "1:", "2:")] string identifier)
        {
            Mock<IFileSystemProvider> provider = CreateProvider();
            using FileSystemNodeManager manager = CreateManager(provider.Object);
            var root = CreateRoot(manager);
            var source = new NodeId(identifier, manager.NamespaceIndex);

            ServiceResult result = await MutateAsync(root, manager.SystemContext, source, operation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(operation == "delete"
                    ? StatusCodes.BadUserAccessDenied
                    : StatusCodes.BadInvalidArgument));
                VerifyNoMutation(provider);
            });
        }

        [Test]
        public async Task InvalidObjectIdsCannotDeleteMoveOrCopyAsync(
            [Values("delete", "move", "copy")] string operation,
            [Values("3:keep.txt", "4294967298:keep.txt", "2147483648:keep.txt",
                "0:keep.txt", "2:keep.txt?Open", "2:keep.txt?")] string identifier,
            [Values(false, true)] bool foreignNamespace)
        {
            Mock<IFileSystemProvider> provider = CreateProvider();
            using FileSystemNodeManager manager = CreateManager(provider.Object);
            var root = CreateRoot(manager);
            var source = new NodeId(identifier,
                foreignNamespace ? (ushort)(manager.NamespaceIndex + 1) : manager.NamespaceIndex);

            ServiceResult result = await MutateAsync(root, manager.SystemContext, source, operation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode.Code, Is.EqualTo(operation == "delete"
                    ? StatusCodes.BadInvalidState
                    : StatusCodes.BadInvalidArgument));
                VerifyNoMutation(provider);
            });
        }

        [Test]
        public async Task ForeignNamespaceCannotAliasAValidFileAsync(
            [Values("delete", "move", "copy")] string operation)
        {
            Mock<IFileSystemProvider> provider = CreateProvider();
            using FileSystemNodeManager manager = CreateManager(provider.Object);
            var root = CreateRoot(manager);
            NodeId source = FileSystemNodeId.BuildFile("keep.txt", (ushort)(manager.NamespaceIndex + 1));

            ServiceResult result = await MutateAsync(root, manager.SystemContext, source, operation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsBad(result), Is.True);
                VerifyNoMutation(provider);
            });
        }

        [Test]
        public async Task InvalidMoveOrCopyTargetDoesNotInvokeProviderAsync(
            [Values(false, true)] bool createCopy,
            [Values("3:target", "2:target", "1:target?CreateFile", "0:target")] string identifier,
            [Values(false, true)] bool foreignNamespace)
        {
            Mock<IFileSystemProvider> provider = CreateProvider();
            using FileSystemNodeManager manager = CreateManager(provider.Object);
            var root = CreateRoot(manager);
            NodeId source = FileSystemNodeId.BuildFile("keep.txt", manager.NamespaceIndex);
            var target = new NodeId(identifier,
                foreignNamespace ? (ushort)(manager.NamespaceIndex + 1) : manager.NamespaceIndex);

            MoveOrCopyMethodStateResult result = await root.MoveOrCopy!.OnCallAsync!(
                manager.SystemContext, root.MoveOrCopy, root.NodeId, source, target,
                createCopy, "renamed.txt", CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidArgument));
                VerifyNoMutation(provider);
            });
        }

        [TestCase("3:")]
        [TestCase("2147483648:")]
        [TestCase("4294967296:")]
        [TestCase("4294967297:")]
        [TestCase("4294967298:")]
        [TestCase("999999999999999999999999999999999999:")]
        public void ParserRejectsUnsupportedAndOverflowingTypes(string identifier)
        {
            Assert.That(FileSystemNodeId.TryParse(new NodeId(identifier, 2), out _), Is.False);
        }

        [Test]
        public async Task PhysicalProviderRejectsRootMutationsBeforeTouchingContentsAsync(
            [Values("delete", "move", "copy")] string operation,
            [Values("", "/", ".", "child/..")] string source)
        {
            string root = Path.Combine(Path.GetTempPath(), "fs-root-regression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "child"));
            string sentinel = Path.Combine(root, "keep.txt");
            await WriteTextAsync(sentinel, "unchanged").ConfigureAwait(false);
            var provider = new PhysicalFileSystemProvider(root, "RootRegression");
            try
            {
                // An existing target also keeps the unfixed recursive copy bounded.
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                {
                    switch (operation)
                    {
                        case "delete":
                            await provider.DeleteAsync(source, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case "move":
                            await provider.MoveAsync(source, "keep.txt", CancellationToken.None).ConfigureAwait(false);
                            break;
                        default:
                            await provider.CopyAsync(source, "keep.txt", CancellationToken.None).ConfigureAwait(false);
                            break;
                    }
                });
                Assert.That(await ReadTextAsync(sentinel).ConfigureAwait(false), Is.EqualTo("unchanged"));
                Assert.That(Directory.Exists(Path.Combine(root, "child")), Is.True);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public async Task OrdinaryChildMutationsRemainSupportedAsync(
            [Values("delete", "move", "copy")] string operation)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), "fs-child-regression-" + Guid.NewGuid().ToString("N"));
            var provider = new PhysicalFileSystemProvider(rootPath, "ChildRegression");
            try
            {
                await WriteTextAsync(Path.Combine(rootPath, "keep.txt"), "payload").ConfigureAwait(false);
                using FileSystemNodeManager manager = CreateManager(provider);
                var root = CreateRoot(manager);
                NodeId source = FileSystemNodeId.BuildFile("keep.txt", manager.NamespaceIndex);

                ServiceResult result = await MutateAsync(root, manager.SystemContext, source, operation)
                    .ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(File.Exists(Path.Combine(rootPath, "keep.txt")), Is.EqualTo(operation == "copy"));
                if (operation != "delete")
                {
                    Assert.That(await ReadTextAsync(Path.Combine(rootPath, "renamed.txt"))
                        .ConfigureAwait(false), Is.EqualTo("payload"));
                }
            }
            finally
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        private static async Task WriteTextAsync(string path, string value)
        {
            using var writer = new StreamWriter(path);
            await writer.WriteAsync(value).ConfigureAwait(false);
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            using var reader = new StreamReader(path);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private static Mock<IFileSystemProvider> CreateProvider()
        {
            var provider = new Mock<IFileSystemProvider>();
            provider.SetupGet(p => p.MountName).Returns("MountRegression");
            provider.SetupGet(p => p.IsWritable).Returns(true);
            return provider;
        }

        private static FileSystemNodeManager CreateManager(IFileSystemProvider provider)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out _);
            server.SetupGet(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            return new FileSystemNodeManager(server.Object, new ApplicationConfiguration(), provider);
        }

        private static DirectoryObjectState CreateRoot(FileSystemNodeManager manager)
        {
            return new DirectoryObjectState(manager.SystemContext,
                FileSystemNodeId.BuildRoot(manager.NamespaceIndex), string.Empty, "Root", isRoot: true);
        }

        private static async ValueTask<ServiceResult> MutateAsync(
            DirectoryObjectState root,
            ISystemContext context,
            NodeId source,
            string operation)
        {
            if (operation == "delete")
            {
                DeleteFileMethodStateResult deleted = await root.DeleteFileSystemObject!.OnCallAsync!(
                    context, root.DeleteFileSystemObject, root.NodeId, source, CancellationToken.None)
                    .ConfigureAwait(false);
                return deleted.ServiceResult;
            }

            MoveOrCopyMethodStateResult moved = await root.MoveOrCopy!.OnCallAsync!(
                context, root.MoveOrCopy, root.NodeId, source, root.NodeId,
                operation == "copy", "renamed.txt", CancellationToken.None).ConfigureAwait(false);
            return moved.ServiceResult;
        }

        private static void VerifyNoMutation(Mock<IFileSystemProvider> provider)
        {
            provider.Verify(p => p.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            provider.Verify(p => p.MoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            provider.Verify(p => p.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
