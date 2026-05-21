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

// CA1861: Test assertions intentionally compare against literal
// expected-value arrays. Lifting them to static readonly fields would
// be noise for one-element vectors used by a handful of tests.
#pragma warning disable CA1861

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// End-to-end mock-based tests for
    /// <see cref="UaDirectoryInfo.EnumerateAsync"/> and the
    /// <see cref="FileSystemClient.EnumerateAsync"/> /
    /// <see cref="FileSystemClient.EnumerateFilesAsync"/> /
    /// <see cref="FileSystemClient.EnumerateDirectoriesAsync"/>
    /// wrappers.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemClientEnumerationTests
    {
        [Test]
        public async Task EnumerateAsyncReturnsBothFilesAndDirectoriesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("a.txt"));
            harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            harness.RegisterFile(harness.Root, new QualifiedName("b.bin"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            var seen = new List<string>();
            await foreach (UaFileSystemInfo entry in client.EnumerateAsync().ConfigureAwait(false))
            {
                seen.Add($"{(entry.IsDirectory ? "DIR" : "FILE")}:{entry.Name}");
            }
            Assert.That(seen, Has.Count.EqualTo(3));
            Assert.That(seen, Does.Contain("FILE:a.txt"));
            Assert.That(seen, Does.Contain("DIR:subdir"));
            Assert.That(seen, Does.Contain("FILE:b.bin"));
        }

        [Test]
        public async Task EnumerateFilesAsyncFiltersToFilesOnlyAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("a.txt"));
            harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            var files = new List<string>();
            await foreach (UaFileInfo file in client.EnumerateFilesAsync().ConfigureAwait(false))
            {
                files.Add(file.Name);
            }
            Assert.That(files, Is.EqualTo(["a.txt"]));
        }

        [Test]
        public async Task EnumerateDirectoriesAsyncFiltersToDirectoriesOnlyAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("a.txt"));
            harness.RegisterDirectory(harness.Root, new QualifiedName("subdir"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            var directories = new List<string>();
            await foreach (UaDirectoryInfo dir in client
                .EnumerateDirectoriesAsync().ConfigureAwait(false))
            {
                directories.Add(dir.Name);
            }
            Assert.That(directories, Is.EqualTo(["subdir"]));
        }

        [Test]
        public async Task EnumerateAsyncSkipsUnknownObjectTypesAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            harness.RegisterFile(harness.Root, new QualifiedName("a.txt"));
            // A child that is neither a FileType nor a FileDirectoryType
            // should be filtered out.
            harness.RegisterObject(harness.Root, new QualifiedName("Other"),
                ObjectTypeIds.BaseObjectType);
            var client = new FileSystemClient(harness.Session, harness.Root);

            var seen = new List<string>();
            await foreach (UaFileSystemInfo entry in client.EnumerateAsync().ConfigureAwait(false))
            {
                seen.Add(entry.Name);
            }
            Assert.That(seen, Is.EqualTo(["a.txt"]));
        }

        [Test]
        public async Task EnumerateAsyncOnEmptyDirectoryYieldsNothingAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId empty = harness.RegisterDirectory(harness.Root, new QualifiedName("empty"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            int count = 0;
            await foreach (UaFileSystemInfo _ in client.EnumerateAsync("/empty")
                .ConfigureAwait(false))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        [Test]
        public async Task EnumerateAsyncPropagatesFullPathAsync()
        {
            var harness = FileSystemSessionHarness.Create();
            NodeId sub = harness.RegisterDirectory(harness.Root, new QualifiedName("Reports"));
            harness.RegisterFile(sub, new QualifiedName("data.csv"));
            var client = new FileSystemClient(harness.Session, harness.Root);

            await foreach (UaFileInfo file in client.EnumerateFilesAsync("/Reports")
                .ConfigureAwait(false))
            {
                Assert.That(file.FullPath, Is.EqualTo("/Reports/data.csv"));
            }
        }
    }
}
