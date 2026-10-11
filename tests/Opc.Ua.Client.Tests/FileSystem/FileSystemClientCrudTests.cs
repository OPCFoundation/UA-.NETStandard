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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end mock-based tests for the CRUD surface of
    /// <see cref="FileSystemClient"/>
    /// (<c>CreateDirectoryAsync</c>/<c>CreateFileAsync</c>/
    /// <c>DeleteAsync</c>/<c>MoveAsync</c>/<c>CopyAsync</c>).
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientCrudTests
    {
        [Test]
        public async Task CreateDirectoryAsyncIssuesCallWithCorrectMethodIdAndArgsAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            ScriptCreateDirectory(harness, new NodeId(7001));
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaDirectoryInfo dir = await client.CreateDirectoryAsync("Reports").ConfigureAwait(false);

            CallMethodRequest req = SingleCallTo(
                harness, Methods.FileDirectoryType_CreateDirectory);
            Assert.That(req.ObjectId, Is.EqualTo(harness.Root));
            req.InputArguments[0].TryGetValue(out string dirName);
            Assert.That(dirName, Is.EqualTo("Reports"));
            Assert.That(dir.NodeId, Is.EqualTo(new NodeId(7001)));
        }

        [Test]
        public async Task CreateFileAsyncIssuesCallWithRequestFileOpenFalseAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            ScriptCreateFile(harness, new NodeId(7002));
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.CreateFileAsync("data.bin").ConfigureAwait(false);

            CallMethodRequest req = SingleCallTo(harness, Methods.FileDirectoryType_CreateFile);
            req.InputArguments[0].TryGetValue(out string fileName);
            req.InputArguments[1].TryGetValue(out bool requestFileOpen);
            Assert.That(fileName, Is.EqualTo("data.bin"));
            Assert.That(requestFileOpen, Is.False,
                "Server-allocated handle must never leak through CreateFileAsync.");
            Assert.That(file.NodeId, Is.EqualTo(new NodeId(7002)));
        }

        [Test]
        public async Task CreateFileAsyncReturnsCanonicalPathForUnqualifiedParentSegmentAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId mount = harness.RegisterDirectory(
                harness.Root,
                new QualifiedName("SampleFiles", 2),
                new NodeId(7101, 2));
            NodeId uploads = harness.RegisterDirectory(
                mount,
                new QualifiedName("Uploads", 2),
                new NodeId(7102, 2));
            var createdId = new NodeId(7103, 2);
            ScriptCreateFile(harness, createdId);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client
                .CreateFileAsync(
                    "/2:SampleFiles/Uploads/probe.txt",
                    createIntermediate: false)
                .ConfigureAwait(false);

            CallMethodRequest request = SingleCallTo(
                harness,
                Methods.FileDirectoryType_CreateFile);
            Assert.That(request.ObjectId, Is.EqualTo(uploads));
            Assert.That(file.NodeId, Is.EqualTo(createdId));
            Assert.That(
                file.FullPath,
                Is.EqualTo("/2:SampleFiles/2:Uploads/2:probe.txt"));
        }

        [Test]
        public async Task CreateDirectoryAsyncCreatesIntermediateDirectoriesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            // Each CreateDirectory call yields a fresh NodeId per call.
            int counter = 0;
            harness.CallHandler = req =>
            {
                if (req.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_CreateDirectory)
                {
                    counter++;
                    req.InputArguments[0].TryGetValue(out string newName);
                    var newId = new NodeId((uint)(8000 + counter));
                    harness.RegisterDirectory(req.ObjectId, new QualifiedName(newName), newId);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = new[] { new Variant(newId) }.ToArrayOf()
                    };
                }
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaDirectoryInfo dir = await client
                .CreateDirectoryAsync("/a/b/c", createIntermediate: true)
                .ConfigureAwait(false);

            Assert.That(counter, Is.EqualTo(3));
            Assert.That(dir.FullPath, Is.EqualTo("/a/b/c"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateDirectoryAsyncQualifiesSegmentsWithParentNamespaceAsync(
            bool createIntermediate)
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId mount = harness.RegisterDirectory(
                harness.Root,
                new QualifiedName("SampleFiles", 2),
                new NodeId(7001, 2));
            NodeId uploads = harness.RegisterDirectory(
                mount,
                new QualifiedName("Uploads", 2),
                new NodeId(7002, 2));
            var createdId = new NodeId(7003, 2);
            ScriptCreateDirectory(harness, createdId);
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaDirectoryInfo dir = await client.CreateDirectoryAsync(
                "/2:SampleFiles/Uploads/New",
                createIntermediate).ConfigureAwait(false);

            Assert.That(harness.CallRequests, Has.Count.EqualTo(1));
            CallMethodRequest request = SingleCallTo(
                harness,
                Methods.FileDirectoryType_CreateDirectory);
            Assert.That(request.ObjectId, Is.EqualTo(uploads));
            request.InputArguments[0].TryGetValue(out string directoryName);
            Assert.That(directoryName, Is.EqualTo("New"));
            Assert.That(dir.NodeId, Is.EqualTo(createdId));
            Assert.That(dir.FullPath, Is.EqualTo("/2:SampleFiles/2:Uploads/2:New"));
        }

        [Test]
        public Task CreateDirectoryAsyncThrowsWhenIntermediateMissingAndFlagFalseAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await client
                    .CreateDirectoryAsync("/a/b/c", createIntermediate: false)
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public Task CreateDirectoryAsyncRejectsNamespacePrefixedLeafAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await client
                    .CreateDirectoryAsync("/1:Reports")
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public async Task DeleteAsyncOnFileIssuesCallOnParentAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId fileId = harness.RegisterFile(harness.Root, new QualifiedName("data.bin"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            await client.DeleteAsync("/data.bin").ConfigureAwait(false);

            CallMethodRequest req = SingleCallTo(
                harness, Methods.FileDirectoryType_DeleteFileSystemObject);
            Assert.That(req.ObjectId, Is.EqualTo(harness.Root));
            req.InputArguments[0].TryGetValue(out NodeId toDelete);
            Assert.That(toDelete, Is.EqualTo(fileId));
        }

        [Test]
        public async Task DeleteAsyncOnEmptyDirectoryWithoutRecursiveCallsServerOnceAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            await client.DeleteAsync("/subdir", recursive: false).ConfigureAwait(false);

            Assert.That(harness.CallRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public Task DeleteAsyncOnNonEmptyDirectoryWithoutRecursiveThrowsAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId subdir = harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            harness.RegisterFile(subdir, new QualifiedName("file.txt"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<IOException>(
                async () => await client
                    .DeleteAsync("/subdir", recursive: false)
                    .ConfigureAwait(false));
            // No Delete call should have been issued.
            Assert.That(harness.CallRequests.Any(r =>
                r.MethodId.TryGetValue(out uint mid) &&
                mid == Methods.FileDirectoryType_DeleteFileSystemObject), Is.False);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Review G16: the non-recursive empty check must not loop forever on
        /// a server that answers every BrowseNext with an empty page and a
        /// continuation point; the point is released and nothing is deleted.
        /// </summary>
        [Test]
        public void DeleteAsyncWithoutRecursiveStopsOnEndlessEmptyBrowseNextPages()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId subdir = harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            var continuationPoint = ByteString.From(new byte[] { 7, 7 });
            harness.SessionMock
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(d =>
                        d.Count == 1 &&
                        d[0].NodeId == subdir &&
                        d[0].ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = continuationPoint }]
                });
            int browseNextCalls = 0;
            harness.SessionMock
                .Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    false,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    browseNextCalls++;
                    return new BrowseNextResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new BrowseResult { ContinuationPoint = continuationPoint }]
                    };
                });
            harness.SessionMock
                .Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    true,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });
            var client = new FileSystemClient(harness.Session, harness.Root);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client
                    .DeleteAsync("/subdir", recursive: false)
                    .ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(browseNextCalls, Is.LessThan(20));
            harness.SessionMock.Verify(s => s.BrowseNextAsync(
                It.IsAny<RequestHeader>(),
                true,
                It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(harness.CallRequests.Any(r =>
                r.MethodId.TryGetValue(out uint mid) &&
                mid == Methods.FileDirectoryType_DeleteFileSystemObject), Is.False);
        }

        [Test]
        public Task DeleteAsyncWithoutRecursiveRefusesDirectoryHoldingFilteredSubtypeAsync()
        {
            // The subtype filter hides a TrustListType child from
            // EnumerateAsync, but the server's Delete would still remove it
            // (Part 20 §4.3.5), so the directory is not empty.
            var harness = FileSystemSessionHarness.Create();
            var typeTree = new Mock<ITypeTable>(MockBehavior.Loose);
            typeTree
                .Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>()))
                .Returns<NodeId, NodeId>((sub, super) => sub.Equals(super) ||
                    (sub.Equals(ObjectTypeIds.TrustListType) && super.Equals(ObjectTypeIds.FileType)));
            harness.SessionMock.SetupGet(s => s.TypeTree).Returns(typeTree.Object);
            NodeId subdir = harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            harness.RegisterObject(subdir, new QualifiedName("TrustList"), ObjectTypeIds.TrustListType);
            var client = new FileSystemClient(
                harness.Session,
                harness.Root,
                new FileSystemClientOptions { IncludeFileTypeSubtypes = false });

            Assert.ThrowsAsync<IOException>(
                async () => await client
                    .DeleteAsync("/subdir", recursive: false)
                    .ConfigureAwait(false));
            Assert.That(harness.CallRequests.Any(r =>
                r.MethodId.TryGetValue(out uint mid) &&
                mid == Methods.FileDirectoryType_DeleteFileSystemObject), Is.False);
            return Task.CompletedTask;
        }

        [Test]
        public async Task ExistsAsyncReturnsFalseForExternallyDeletedCachedNodeAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId dir = harness.RegisterDirectory(harness.Root, new QualifiedName("dir"));
            NodeId file = harness.RegisterFile(dir, new QualifiedName("a.txt"));
            var client = new FileSystemClient(harness.Session, harness.Root);
            Assert.That(await client.ExistsAsync("/dir/a.txt").ConfigureAwait(false), Is.True);

            // Another client deletes the file; the path cache still maps it.
            harness.RemoveNode(file);

            Assert.That(await client.ExistsAsync("/dir/a.txt").ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task GetInfoAsyncResolvesExternallyRecreatedDirectoryAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId dir = harness.RegisterDirectory(harness.Root, new QualifiedName("dir"));
            var client = new FileSystemClient(harness.Session, harness.Root);
            Assert.That(await client.ExistsAsync("/dir").ConfigureAwait(false), Is.True);

            // Deleted and recreated elsewhere: same name, new NodeId.
            harness.RemoveNode(dir);
            NodeId recreated = harness.RegisterDirectory(harness.Root, new QualifiedName("dir"));
            NodeId file = harness.RegisterFile(recreated, new QualifiedName("b.txt"));

            UaFileSystemInfo info = (await client.GetInfoAsync("/dir/b.txt").ConfigureAwait(false))!;
            Assert.That(info, Is.Not.Null);
            Assert.That(info.NodeId, Is.EqualTo(file));
        }

        [Test]
        public async Task DeleteAsyncOnNonEmptyDirectoryWithRecursiveCallsServerOnceAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId subdir = harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            harness.RegisterFile(subdir, new QualifiedName("file.txt"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            await client.DeleteAsync("/subdir", recursive: true).ConfigureAwait(false);

            var deletes = harness.CallRequests
                .Where(r => r.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_DeleteFileSystemObject)
                .ToList();
            // Exactly one Delete call — server is responsible for
            // recursive traversal (Part 20 §4.3.2).
            Assert.That(deletes, Has.Count.EqualTo(1));
            deletes[0].InputArguments[0].TryGetValue(out NodeId toDelete);
            Assert.That(toDelete, Is.EqualTo(subdir));
        }

        [Test]
        public async Task MoveAsyncIssuesMoveOrCopyOnSourceParentAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId srcDir = harness.RegisterDirectory(harness.Root, new QualifiedName("src"));
            NodeId fileId = harness.RegisterFile(srcDir, new QualifiedName("data.bin"));
            NodeId destDir = harness.RegisterDirectory(harness.Root, new QualifiedName("dest"));
            ScriptMoveOrCopy(harness, new NodeId(9001));
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileSystemInfo moved = await client
                .MoveAsync("/src/data.bin", "/dest/data.bin")
                .ConfigureAwait(false);

            CallMethodRequest req = SingleCallTo(harness, Methods.FileDirectoryType_MoveOrCopy);
            Assert.That(req.ObjectId, Is.EqualTo(srcDir), "MoveOrCopy must be invoked on the source's parent directory.");
            req.InputArguments[0].TryGetValue(out NodeId objToMove);
            req.InputArguments[1].TryGetValue(out NodeId targetDirectory);
            req.InputArguments[2].TryGetValue(out bool createCopy);
            req.InputArguments[3].TryGetValue(out string newName);
            Assert.That(objToMove, Is.EqualTo(fileId));
            Assert.That(targetDirectory, Is.EqualTo(destDir));
            Assert.That(createCopy, Is.False);
            Assert.That(newName, Is.EqualTo("data.bin"));
            Assert.That(moved.NodeId, Is.EqualTo(new NodeId(9001)));
        }

        [Test]
        public async Task CopyAsyncIssuesMoveOrCopyWithCreateCopyTrueAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId srcDir = harness.RegisterDirectory(harness.Root, new QualifiedName("src"));
            harness.RegisterFile(srcDir, new QualifiedName("data.bin"));
            harness.RegisterDirectory(harness.Root, new QualifiedName("dest"));
            ScriptMoveOrCopy(harness, new NodeId(9002));
            var client = new FileSystemClient(harness.Session, harness.Root);

            _ = await client
                .CopyAsync("/src/data.bin", "/dest/copy.bin")
                .ConfigureAwait(false);

            CallMethodRequest req = SingleCallTo(harness, Methods.FileDirectoryType_MoveOrCopy);
            req.InputArguments[2].TryGetValue(out bool createCopy);
            req.InputArguments[3].TryGetValue(out string newName);
            Assert.That(createCopy, Is.True);
            Assert.That(newName, Is.EqualTo("copy.bin"));
        }

        [Test]
        public Task DeleteAsyncMapsBadUserAccessDeniedAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("locked.bin"));
            harness.CallHandler = req =>
            {
                if (req.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_DeleteFileSystemObject)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.BadUserAccessDenied,
                        OutputArguments = Array.Empty<Variant>().ToArrayOf()
                    };
                }
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<UnauthorizedAccessException>(
                async () => await client.DeleteAsync("/locked.bin").ConfigureAwait(false));
            return Task.CompletedTask;
        }

        private static void ScriptCreateDirectory(
            FileSystemSessionHarness harness, NodeId newId)
        {
            harness.CallHandler = req =>
            {
                if (req.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_CreateDirectory)
                {
                    req.InputArguments[0].TryGetValue(out string newName);
                    harness.RegisterDirectory(
                        req.ObjectId,
                        new QualifiedName(newName, req.ObjectId.NamespaceIndex),
                        newId);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = new[] { new Variant(newId) }.ToArrayOf()
                    };
                }
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
        }

        private static void ScriptCreateFile(
            FileSystemSessionHarness harness, NodeId newId)
        {
            harness.CallHandler = req =>
            {
                if (req.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_CreateFile)
                {
                    req.InputArguments[0].TryGetValue(out string newName);
                    harness.RegisterFile(
                        req.ObjectId,
                        new QualifiedName(newName, req.ObjectId.NamespaceIndex),
                        newId);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = new[]
                        {
                            new Variant(newId),
                            new Variant(0u)
                        }.ToArrayOf()
                    };
                }
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
        }

        private static void ScriptMoveOrCopy(
            FileSystemSessionHarness harness, NodeId resultNodeId)
        {
            harness.CallHandler = req =>
            {
                if (req.MethodId.TryGetValue(out uint mid) &&
                    mid == Methods.FileDirectoryType_MoveOrCopy)
                {
                    req.InputArguments[3].TryGetValue(out string newName);
                    req.InputArguments[1].TryGetValue(out NodeId destDir);
                    harness.RegisterFile(destDir, new QualifiedName(newName), resultNodeId);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = new[] { new Variant(resultNodeId) }.ToArrayOf()
                    };
                }
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
        }

        private static CallMethodRequest SingleCallTo(
            FileSystemSessionHarness harness, uint methodId)
        {
            var matches = harness.CallRequests
                .Where(r => r.MethodId.TryGetValue(out uint mid) && mid == methodId)
                .ToList();
            Assert.That(matches, Has.Count.EqualTo(1),
                $"Expected exactly one call to method {methodId}.");
            return matches[0];
        }
    }
}
