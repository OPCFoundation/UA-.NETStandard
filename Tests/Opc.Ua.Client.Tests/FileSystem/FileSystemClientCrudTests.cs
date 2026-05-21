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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        [Test]
        public async Task CreateDirectoryAsyncThrowsWhenIntermediateMissingAndFlagFalseAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await client
                    .CreateDirectoryAsync("/a/b/c", createIntermediate: false)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task CreateDirectoryAsyncRejectsNamespacePrefixedLeafAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await client
                    .CreateDirectoryAsync("/1:Reports")
                    .ConfigureAwait(false));
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
        public async Task DeleteAsyncOnNonEmptyDirectoryWithoutRecursiveThrowsAsync()
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
        public async Task DeleteAsyncMapsBadUserAccessDeniedAsync()
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
        }

        // -------- helpers ------------------------------------------------

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
                        req.ObjectId, new QualifiedName(newName), newId);
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
                    harness.RegisterFile(req.ObjectId, new QualifiedName(newName), newId);
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
