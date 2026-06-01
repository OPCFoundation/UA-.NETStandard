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

using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end mock-based tests for the path-resolution surface of
    /// <see cref="FileSystemClient"/>
    /// (<c>GetInfoAsync</c>/<c>GetFileAsync</c>/<c>GetDirectoryAsync</c>/
    /// <c>ExistsAsync</c>).
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientPathResolutionTests
    {
        [Test]
        public async Task GetInfoAsyncReturnsRootForEmptyPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileSystemInfo info = await client.GetInfoAsync(string.Empty)
                .ConfigureAwait(false);
            Assert.That(info, Is.SameAs(client.Root));

            UaFileSystemInfo infoRoot = await client.GetInfoAsync("/")
                .ConfigureAwait(false);
            Assert.That(infoRoot, Is.SameAs(client.Root));
        }

        [Test]
        public async Task GetFileAsyncResolvesTwoSegmentPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId reports = harness.RegisterDirectory(
                harness.Root, new QualifiedName("Reports"));
            NodeId fileId = harness.RegisterFile(
                reports, new QualifiedName("data.csv"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileInfo file = await client.GetFileAsync("Reports/data.csv")
                .ConfigureAwait(false);
            Assert.That(file.NodeId, Is.EqualTo(fileId));
            Assert.That(file.Name, Is.EqualTo("data.csv"));
            Assert.That(file.FullPath, Is.EqualTo("/Reports/data.csv"));
        }

        [Test]
        public async Task GetDirectoryAsyncResolvesQualifiedSegmentsAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId child = harness.RegisterDirectory(
                harness.Root, new QualifiedName("Reports", 1));
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaDirectoryInfo dir = await client.GetDirectoryAsync("1:Reports")
                .ConfigureAwait(false);
            Assert.That(dir.NodeId, Is.EqualTo(child));
            Assert.That(dir.BrowseName.NamespaceIndex, Is.EqualTo(1));
            Assert.That(dir.FullPath, Is.EqualTo("/1:Reports"));
        }

        [Test]
        public async Task GetInfoAsyncReturnsNullForMissingPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            UaFileSystemInfo info = await client.GetInfoAsync("/missing/path")
                .ConfigureAwait(false);
            Assert.That(info, Is.Null);
        }

        [Test]
        public Task GetFileAsyncThrowsFileNotFoundForMissingPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await client.GetFileAsync("/missing.txt")
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public Task GetDirectoryAsyncThrowsDirectoryNotFoundForMissingPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await client.GetDirectoryAsync("/missing")
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public Task GetFileAsyncThrowsWhenPathResolvesToDirectoryAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterDirectory(harness.Root, new QualifiedName("Reports"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await client.GetFileAsync("/Reports")
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public Task GetDirectoryAsyncThrowsWhenPathResolvesToFileAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("data.csv"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await client.GetDirectoryAsync("/data.csv")
                    .ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public async Task ExistsAsyncReturnsTrueForExistingPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("data.csv"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.That(await client.ExistsAsync("/data.csv").ConfigureAwait(false), Is.True);
            Assert.That(await client.FileExistsAsync("/data.csv").ConfigureAwait(false), Is.True);
            Assert.That(await client.DirectoryExistsAsync("/data.csv").ConfigureAwait(false),
                Is.False);
        }

        [Test]
        public async Task ExistsAsyncReturnsFalseForMissingPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            var client = new FileSystemClient(harness.Session, harness.Root);

            Assert.That(await client.ExistsAsync("/missing").ConfigureAwait(false), Is.False);
            Assert.That(await client.FileExistsAsync("/missing").ConfigureAwait(false), Is.False);
            Assert.That(await client.DirectoryExistsAsync("/missing").ConfigureAwait(false),
                Is.False);
        }

        [Test]
        public async Task ResolvedPathIsCachedAcrossLookupsAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("data.csv"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            int translateCalls = 0;
            harness.SessionMock
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, System.Threading.CancellationToken>(
                    (header, paths, ct) =>
                    {
                        translateCalls++;
                        var results = new BrowsePathResult[paths.Count];
                        for (int i = 0; i < paths.Count; i++)
                        {
                            results[i] = harness.ResolveBrowsePathForTest(paths[i]);
                        }
                        var response = new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results.ToArrayOf(),
                            DiagnosticInfos = default
                        };
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(response);
                    });

            _ = await client.GetFileAsync("/data.csv").ConfigureAwait(false);
            int firstCount = translateCalls;
            _ = await client.GetFileAsync("/data.csv").ConfigureAwait(false);

            // The second resolution should be served from the path
            // cache and not call Translate again for the leaf segment.
            Assert.That(translateCalls, Is.EqualTo(firstCount));
        }
    }
}
