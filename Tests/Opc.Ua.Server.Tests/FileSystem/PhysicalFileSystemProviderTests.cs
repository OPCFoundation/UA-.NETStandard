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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="PhysicalFileSystemProvider"/> exercised against a
    /// temporary host directory.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public class PhysicalFileSystemProviderTests
    {
        private string m_root = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "pfsp-" + Guid.NewGuid().ToString("N"));
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
                // best-effort cleanup
            }
        }

        private PhysicalFileSystemProvider CreateProvider(bool isWritable = true, string? mountName = null)
        {
            return new PhysicalFileSystemProvider(m_root, mountName, isWritable);
        }

        [Test]
        public void ConstructorWithNullRootThrows()
        {
            Assert.That(
                () => new PhysicalFileSystemProvider(null!),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ConstructorCreatesRootAndUsesDirectoryNameAsMountName()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(Directory.Exists(m_root), Is.True);
            Assert.That(provider.MountName, Is.EqualTo(new DirectoryInfo(m_root).Name));
            Assert.That(provider.IsWritable, Is.True);
        }

        [Test]
        public void ConstructorHonorsExplicitMountName()
        {
            PhysicalFileSystemProvider provider = CreateProvider(mountName: "MyMount");

            Assert.That(provider.MountName, Is.EqualTo("MyMount"));
        }

        [Test]
        public async Task GetEntryAsyncReturnsNullForMissingPathAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            FileSystemEntry? entry = await provider.GetEntryAsync("missing.txt", CancellationToken.None);

            Assert.That(entry, Is.Null);
        }

        [Test]
        public async Task GetEntryAsyncReturnsDirectoryEntryForRootAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider(mountName: "Root");

            FileSystemEntry? entry = await provider.GetEntryAsync(string.Empty, CancellationToken.None);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Value.IsDirectory, Is.True);
            Assert.That(entry.Value.Name, Is.EqualTo("Root"));
            Assert.That(entry.Value.Length, Is.Zero);
        }

        [Test]
        public async Task GetEntryAsyncReturnsFileEntryWithMetadataAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await File.WriteAllTextAsync(Path.Combine(m_root, "data.json"), "{}");

            FileSystemEntry? entry = await provider.GetEntryAsync("data.json", CancellationToken.None);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Value.IsDirectory, Is.False);
            Assert.That(entry.Value.Name, Is.EqualTo("data.json"));
            Assert.That(entry.Value.Length, Is.EqualTo(2));
            Assert.That(entry.Value.MimeType, Is.EqualTo("application/json"));
        }

        [Test]
        public async Task CreateFileAndOpenWriteThenReadRoundTripsAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("file.txt", CancellationToken.None);

            byte[] payload = Encoding.UTF8.GetBytes("hello world");
            await using (Stream write = await provider.OpenWriteAsync(
                "file.txt", FileWriteMode.Truncate, CancellationToken.None))
            {
                await write.WriteAsync(payload);
            }

            await using Stream read = await provider.OpenReadAsync("file.txt", CancellationToken.None);
            using var ms = new MemoryStream();
            await read.CopyToAsync(ms);

            Assert.That(ms.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task OpenWriteAppendPreservesExistingContentAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await File.WriteAllTextAsync(Path.Combine(m_root, "log.txt"), "a");

            await using (Stream write = await provider.OpenWriteAsync(
                "log.txt", FileWriteMode.Append, CancellationToken.None))
            {
                await write.WriteAsync(Encoding.UTF8.GetBytes("b"));
            }

            Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "log.txt")), Is.EqualTo("ab"));
        }

        [Test]
        public async Task OpenWriteOpenOrCreateCreatesFileAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            await using (Stream write = await provider.OpenWriteAsync(
                "new.bin", FileWriteMode.OpenOrCreate, CancellationToken.None))
            {
                await write.WriteAsync(new byte[] { 1, 2, 3 });
            }

            Assert.That(File.Exists(Path.Combine(m_root, "new.bin")), Is.True);
        }

        [Test]
        public void OpenReadAsyncForMissingFileThrows()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () => await provider.OpenReadAsync("nope.txt", CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public async Task EnumerateAsyncReturnsFilesAndDirectoriesAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            Directory.CreateDirectory(Path.Combine(m_root, "sub"));
            await File.WriteAllTextAsync(Path.Combine(m_root, "a.txt"), "x");

            var entries = new List<FileSystemEntry>();
            await foreach (FileSystemEntry entry in provider.EnumerateAsync(string.Empty, CancellationToken.None))
            {
                entries.Add(entry);
            }

            Assert.That(entries.Any(e => e.IsDirectory && e.Name == "sub"), Is.True);
            Assert.That(entries.Any(e => !e.IsDirectory && e.Name == "a.txt"), Is.True);
        }

        [Test]
        public async Task EnumerateAsyncUsesProviderPathForNestedDirectoryAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            Directory.CreateDirectory(Path.Combine(m_root, "sub"));
            await File.WriteAllTextAsync(Path.Combine(m_root, "sub", "b.txt"), "y");

            var entries = new List<FileSystemEntry>();
            await foreach (FileSystemEntry entry in provider.EnumerateAsync("sub", CancellationToken.None))
            {
                entries.Add(entry);
            }

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Path, Is.EqualTo("sub/b.txt"));
        }

        [Test]
        public void EnumerateAsyncForMissingDirectoryThrows()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () =>
                {
                    await foreach (FileSystemEntry _ in provider.EnumerateAsync("ghost", CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<DirectoryNotFoundException>());
        }

        [Test]
        public async Task CreateDirectoryAsyncCreatesNestedDirectoriesAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            await provider.CreateDirectoryAsync("a/b/c", CancellationToken.None);

            Assert.That(Directory.Exists(Path.Combine(m_root, "a", "b", "c")), Is.True);
        }

        [Test]
        public async Task CreateDirectoryAsyncOverExistingFileThrowsAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("clash", CancellationToken.None);

            Assert.That(
                async () => await provider.CreateDirectoryAsync("clash", CancellationToken.None),
                Throws.TypeOf<IOException>());
        }

        [Test]
        public async Task CreateFileAsyncOverExistingEntryThrowsAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("dup", CancellationToken.None);

            Assert.That(
                async () => await provider.CreateFileAsync("dup", CancellationToken.None),
                Throws.TypeOf<IOException>());
        }

        [Test]
        public async Task DeleteAsyncRemovesFileAndDirectoryAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("f.txt", CancellationToken.None);
            await provider.CreateDirectoryAsync("d", CancellationToken.None);

            await provider.DeleteAsync("f.txt", CancellationToken.None);
            await provider.DeleteAsync("d", CancellationToken.None);

            Assert.That(File.Exists(Path.Combine(m_root, "f.txt")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(m_root, "d")), Is.False);
        }

        [Test]
        public void DeleteAsyncForMissingPathThrows()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () => await provider.DeleteAsync("gone", CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public async Task MoveAsyncRenamesFileAndDirectoryAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await File.WriteAllTextAsync(Path.Combine(m_root, "src.txt"), "z");
            await provider.CreateDirectoryAsync("srcdir", CancellationToken.None);

            await provider.MoveAsync("src.txt", "dst.txt", CancellationToken.None);
            await provider.MoveAsync("srcdir", "dstdir", CancellationToken.None);

            Assert.That(File.Exists(Path.Combine(m_root, "dst.txt")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(m_root, "dstdir")), Is.True);
        }

        [Test]
        public async Task MoveAsyncToExistingTargetThrowsAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("a", CancellationToken.None);
            await provider.CreateFileAsync("b", CancellationToken.None);

            Assert.That(
                async () => await provider.MoveAsync("a", "b", CancellationToken.None),
                Throws.TypeOf<IOException>());
        }

        [Test]
        public void MoveAsyncForMissingSourceThrows()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () => await provider.MoveAsync("missing", "target", CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public async Task CopyAsyncCopiesFileAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await File.WriteAllTextAsync(Path.Combine(m_root, "orig.txt"), "content");

            await provider.CopyAsync("orig.txt", "copy.txt", CancellationToken.None);

            Assert.That(await File.ReadAllTextAsync(Path.Combine(m_root, "copy.txt")), Is.EqualTo("content"));
        }

        [Test]
        public async Task CopyAsyncCopiesDirectoryRecursivelyAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            Directory.CreateDirectory(Path.Combine(m_root, "tree", "nested"));
            await File.WriteAllTextAsync(Path.Combine(m_root, "tree", "root.txt"), "1");
            await File.WriteAllTextAsync(Path.Combine(m_root, "tree", "nested", "leaf.txt"), "2");

            await provider.CopyAsync("tree", "treecopy", CancellationToken.None);

            Assert.That(File.Exists(Path.Combine(m_root, "treecopy", "root.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(m_root, "treecopy", "nested", "leaf.txt")), Is.True);
        }

        [Test]
        public async Task CopyAsyncToExistingTargetThrowsAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            await provider.CreateFileAsync("s", CancellationToken.None);
            await provider.CreateFileAsync("t", CancellationToken.None);

            Assert.That(
                async () => await provider.CopyAsync("s", "t", CancellationToken.None),
                Throws.TypeOf<IOException>());
        }

        [Test]
        public void CopyAsyncForMissingSourceThrows()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () => await provider.CopyAsync("nope", "dest", CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public void PathTraversalIsRejected()
        {
            PhysicalFileSystemProvider provider = CreateProvider();

            Assert.That(
                async () => await provider.GetEntryAsync("../escape", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
        }

        [Test]
        public void ReadOnlyProviderRejectsWriteOperations()
        {
            PhysicalFileSystemProvider provider = CreateProvider(isWritable: false);

            Assert.That(provider.IsWritable, Is.False);
            Assert.That(
                async () => await provider.CreateFileAsync("x", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(
                async () => await provider.CreateDirectoryAsync("x", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(
                async () => await provider.DeleteAsync("x", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(
                async () => await provider.MoveAsync("x", "y", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(
                async () => await provider.CopyAsync("x", "y", CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(
                async () => await provider.OpenWriteAsync("x", FileWriteMode.Truncate, CancellationToken.None),
                Throws.TypeOf<UnauthorizedAccessException>());
        }

        [Test]
        public async Task GetEntryAsyncGuessesMimeTypesAsync()
        {
            PhysicalFileSystemProvider provider = CreateProvider();
            var expectations = new Dictionary<string, string>
            {
                ["a.txt"] = "text/plain",
                ["a.html"] = "text/html",
                ["a.css"] = "text/css",
                ["a.js"] = "application/javascript",
                ["a.xml"] = "application/xml",
                ["a.png"] = "image/png",
                ["a.jpg"] = "image/jpeg",
                ["a.gif"] = "image/gif",
                ["a.zip"] = "application/zip",
                ["a.unknownext"] = "application/octet-stream"
            };

            foreach ((string name, string mime) in expectations)
            {
                await File.WriteAllTextAsync(Path.Combine(m_root, name), "x");
                FileSystemEntry? entry = await provider.GetEntryAsync(name, CancellationToken.None);
                Assert.That(entry!.Value.MimeType, Is.EqualTo(mime), $"MIME for {name}");
            }
        }
    }
}
