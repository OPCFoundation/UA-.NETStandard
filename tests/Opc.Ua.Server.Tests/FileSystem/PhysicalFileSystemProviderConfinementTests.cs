/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Regression tests for physical mount confinement against pre-existing links.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class PhysicalFileSystemProviderConfinementTests
    {
        /// <summary>
        /// Creates isolated mount and outside directories under the test output directory.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_workspace = Path.Combine(AppContext.BaseDirectory, "confinement-" + Guid.NewGuid().ToString("N"));
            m_root = Path.Combine(m_workspace, "mount");
            m_outside = Path.Combine(m_workspace, "outside");
            Directory.CreateDirectory(m_root);
            Directory.CreateDirectory(m_outside);
            File.WriteAllText(Path.Combine(m_outside, "secret.txt"), "outside");
            File.WriteAllText(Path.Combine(m_root, "source.txt"), "inside");
        }

        /// <summary>
        /// Removes links before deleting their containing directories.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach ((string path, bool directory) in m_links)
            {
                if (directory)
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
            m_links.Clear();
            Directory.Delete(m_workspace, recursive: true);
        }

        /// <summary>
        /// Every public path operation rejects a linked directory before accessing its target.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LinkedDirectoryRejectsAllOperations(bool junction)
        {
            var provider = new PhysicalFileSystemProvider(m_root);
            CreateLink(Path.Combine(m_root, "link"), m_outside, directory: true, junction);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                using Stream stream = await provider.OpenReadAsync("link/secret.txt", CancellationToken.None)
                    .ConfigureAwait(false);
            });
            FileWriteMode[] modes = [FileWriteMode.OpenOrCreate, FileWriteMode.Truncate, FileWriteMode.Append];
            foreach (FileWriteMode mode in modes)
            {
                Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                {
                    using Stream stream = await provider.OpenWriteAsync("link/secret.txt", mode, CancellationToken.None)
                        .ConfigureAwait(false);
                });
            }
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.GetEntryAsync("link/secret.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.Throws<UnauthorizedAccessException>(() => provider.GetPathIdentity("link/new.txt"));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CreateFileAsync("link/new.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CreateDirectoryAsync("link/new/nested", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.DeleteAsync("link/secret.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.DeleteAsync("link", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.MoveAsync("link", "moved", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("link", "copy", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.MoveAsync("link/secret.txt", "moved.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.MoveAsync("source.txt", "link/moved.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("link/secret.txt", "copy.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("source.txt", "link/copy.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await DrainAsync(provider, "link").ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await DrainAsync(provider, string.Empty).ConfigureAwait(false));
            Assert.That(File.ReadAllText(Path.Combine(m_outside, "secret.txt")), Is.EqualTo("outside"));
            Assert.That(Directory.GetFileSystemEntries(m_outside), Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(m_root, "source.txt")), Is.EqualTo("inside"));
            Assert.That(File.Exists(Path.Combine(m_root, "copy.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(m_root, "moved.txt")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(m_root, "copy")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(m_root, "moved")), Is.False);
        }

        /// <summary>
        /// In-root directory links and cycles are also rejected rather than recursively followed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RecursiveCopyRejectsInRootLinksAndCycles(bool cycle)
        {
            var provider = new PhysicalFileSystemProvider(m_root);
            string tree = Path.Combine(m_root, "tree");
            string nested = Path.Combine(tree, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "safe.txt"), "inside");
            CreateLink(
                Path.Combine(tree, "alias"),
                cycle ? tree : nested,
                directory: true,
                junction: Path.DirectorySeparatorChar == '\\');
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("tree", "copy", CancellationToken.None).ConfigureAwait(false));
            Assert.That(Directory.Exists(Path.Combine(m_root, "copy")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(nested, "safe.txt")), Is.EqualTo("inside"));
        }

        /// <summary>
        /// Recursive operations reject links before traversing a child or mutating the tree.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RecursiveOperationsRejectLinkedDescendantsBeforeMutation(bool junction, bool directory)
        {
            var provider = new PhysicalFileSystemProvider(m_root);
            string tree = Path.Combine(m_root, "tree");
            Directory.CreateDirectory(Path.Combine(tree, "nested"));
            File.WriteAllText(Path.Combine(tree, "nested", "safe.txt"), "safe");
            string target = directory ? m_outside : Path.Combine(m_outside, "secret.txt");
            CreateLink(Path.Combine(tree, "nested", "escape"), target, directory, junction);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("tree", "copy", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.MoveAsync("tree", "moved", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.DeleteAsync("tree", CancellationToken.None).ConfigureAwait(false));
            Assert.That(Directory.Exists(Path.Combine(m_root, "copy")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(m_root, "moved")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(tree, "nested", "safe.txt")), Is.EqualTo("safe"));
            Assert.That(File.ReadAllText(Path.Combine(m_outside, "secret.txt")), Is.EqualTo("outside"));
        }

        /// <summary>
        /// File symlinks are rejected whether live, dangling, or pointing back into the mount.
        /// </summary>
        [TestCase("outside")]
        [TestCase("dangling")]
        [TestCase("inside")]
        public void FileLinksRejectReadWriteAndCopy(string targetKind)
        {
            var provider = new PhysicalFileSystemProvider(m_root);
            string target = targetKind switch
            {
                "outside" => Path.Combine(m_outside, "secret.txt"),
                "dangling" => Path.Combine(m_outside, "missing.txt"),
                _ => Path.Combine(m_root, "source.txt")
            };
            CreateLink(Path.Combine(m_root, "link.txt"), target, directory: false, junction: false);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                using Stream stream = await provider.OpenReadAsync("link.txt", CancellationToken.None)
                    .ConfigureAwait(false);
            });
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                using Stream stream = await provider.OpenWriteAsync(
                    "link.txt", FileWriteMode.Truncate, CancellationToken.None).ConfigureAwait(false);
            });
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("link.txt", "copy.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CopyAsync("source.txt", "link.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.GetEntryAsync("link.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await DrainAsync(provider, string.Empty).ConfigureAwait(false));
            Assert.That(File.ReadAllText(Path.Combine(m_outside, "secret.txt")), Is.EqualTo("outside"));
            Assert.That(File.ReadAllText(Path.Combine(m_root, "source.txt")), Is.EqualTo("inside"));
            Assert.That(File.Exists(Path.Combine(m_outside, "missing.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(m_root, "copy.txt")), Is.False);
        }

        /// <summary>
        /// Mount construction rejects a link at the root or at an ancestor before creating anything.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRejectsLinkedRootAndAncestors(bool junction)
        {
            string link = Path.Combine(m_root, "alias");
            CreateLink(link, m_outside, directory: true, junction);
            Assert.Throws<UnauthorizedAccessException>(() => new PhysicalFileSystemProvider(link));
            Assert.Throws<UnauthorizedAccessException>(() =>
                new PhysicalFileSystemProvider(Path.Combine(link, "missing", "mount")));
            Assert.That(Directory.Exists(Path.Combine(m_outside, "missing")), Is.False);
        }

        /// <summary>
        /// Ancestors are checked again after construction rather than trusting a cached root check.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OperationsRejectReplacedRootAncestor(bool junction)
        {
            string ancestor = Path.Combine(m_root, "ancestor");
            string mount = Path.Combine(ancestor, "mount");
            var provider = new PhysicalFileSystemProvider(mount);
            Directory.Delete(ancestor, recursive: true);
            Directory.CreateDirectory(Path.Combine(m_outside, "mount"));
            CreateLink(ancestor, m_outside, directory: true, junction);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await provider.CreateFileAsync("new.txt", CancellationToken.None).ConfigureAwait(false));
            Assert.Throws<UnauthorizedAccessException>(() => provider.GetPathIdentity(string.Empty));
            Assert.That(File.Exists(Path.Combine(m_outside, "mount", "new.txt")), Is.False);
        }

        /// <summary>
        /// Normal nested creation, copying into a descendant, moving, and deletion remain supported.
        /// </summary>
        [Test]
        public async Task OrdinaryTreeOperationsRemainSupportedAsync()
        {
            var provider = new PhysicalFileSystemProvider(m_root + Path.DirectorySeparatorChar);
            await provider.CreateDirectoryAsync("tree/nested", CancellationToken.None).ConfigureAwait(false);
            using (Stream write = await provider.OpenWriteAsync(
                "tree/nested/data.txt", FileWriteMode.Truncate, CancellationToken.None).ConfigureAwait(false))
            {
#if NET48
                await write.WriteAsync(new byte[] { 42 }, 0, 1).ConfigureAwait(false);
#else
                await write.WriteAsync(new byte[] { 42 }).ConfigureAwait(false);
#endif
            }
            await provider.CopyAsync("tree", "tree/copy", CancellationToken.None).ConfigureAwait(false);
            await provider.MoveAsync("tree/copy", "moved", CancellationToken.None).ConfigureAwait(false);
            using (Stream read = await provider.OpenReadAsync("moved/nested/data.txt", CancellationToken.None)
                .ConfigureAwait(false))
            {
                var bytes = new byte[2];
#if NET48
                int count = await read.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#else
                int count = await read.ReadAsync(bytes.AsMemory()).ConfigureAwait(false);
#endif
                Assert.That(count, Is.EqualTo(1));
                Assert.That(bytes[0], Is.EqualTo(42));
            }
            await provider.DeleteAsync("tree", CancellationToken.None).ConfigureAwait(false);
            Assert.That(Directory.Exists(Path.Combine(m_root, "tree")), Is.False);
            Assert.That(
                File.ReadAllBytes(Path.Combine(m_root, "moved", "nested", "data.txt")),
                Is.EqualTo(new byte[] { 42 }));
        }

        /// <summary>
        /// Creates a real link, skipping only when the platform lacks the requested capability.
        /// </summary>
        private void CreateLink(string path, string target, bool directory, bool junction)
        {
            if (junction && Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("Junctions are Windows-specific.");
            }
            if (Path.DirectorySeparatorChar == '\\')
            {
                string option = junction ? "/J" : directory ? "/D" : string.Empty;
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = $"/d /c mklink {option} \"{path}\" \"{target}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!junction && process.ExitCode != 0 &&
                    error.Contains("privilege", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Ignore("Symbolic link creation requires Windows privileges: " + error);
                }
                Assert.That(process.ExitCode, Is.Zero, output + error);
            }
            else
            {
#if NET48
                Assert.Ignore("Symbolic link creation requires a modern .NET runtime.");
#else
                if (directory)
                {
                    Directory.CreateSymbolicLink(path, target);
                }
                else
                {
                    File.CreateSymbolicLink(path, target);
                }
#endif
            }
            m_links.Add((path, directory));
            Assert.That(File.GetAttributes(path) & FileAttributes.ReparsePoint, Is.Not.Zero);
        }

        /// <summary>
        /// Forces lazy enumeration to run through every entry.
        /// </summary>
        private static async Task DrainAsync(PhysicalFileSystemProvider provider, string path)
        {
            await foreach (FileSystemEntry entry in provider.EnumerateAsync(path, CancellationToken.None)
                .ConfigureAwait(false))
            {
                Assert.That(entry.Path, Is.Not.Empty);
            }
        }

        /// <summary>The isolated test directory.</summary>
        private string m_workspace = null!;
        /// <summary>The mounted physical directory.</summary>
        private string m_root = null!;
        /// <summary>A sibling directory that must remain inaccessible through the provider.</summary>
        private string m_outside = null!;
        /// <summary>Links to unlink before recursive fixture cleanup.</summary>
        private readonly List<(string Path, bool Directory)> m_links = [];
    }
}
