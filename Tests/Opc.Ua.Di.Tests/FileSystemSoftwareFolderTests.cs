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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server.FileSystem;

#nullable enable
#pragma warning disable CA1305

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="FileSystemSoftwareFolder"/> over a
    /// <see cref="PhysicalFileSystemProvider"/> rooted under a
    /// temporary directory.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("SoftwareUpdate")]
    public sealed class FileSystemSoftwareFolderTests
    {
        private string m_tempRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            m_tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-sf-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_tempRoot))
            {
                Directory.Delete(m_tempRoot, recursive: true);
            }
        }

        private FileSystemSoftwareFolder CreateFolder(bool writable = true)
        {
            var provider = new PhysicalFileSystemProvider(
                rootDirectory: m_tempRoot,
                mountName: "Software",
                isWritable: writable);
            return new FileSystemSoftwareFolder(
                provider,
                new NodeId("Device", 2),
                rootPath: "/Software");
        }

        private static SoftwarePackage MakePackage(
            string version,
            string description = "")
        {
            return new SoftwarePackage(
                Id: $"pkg-{version}",
                Version: version,
                Vendor: "Acme",
                Description: description,
                SizeBytes: 0,
                CreatedAt: default,
                Hash: string.Empty);
        }

        [Test]
        public async Task AddVersionPersistsPayloadAndMetadata()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            byte[] payload = [1, 2, 3, 4];

            SoftwarePackage stored = await folder.AddVersionAsync(
                MakePackage("1.0.0"), new MemoryStream(payload)).ConfigureAwait(false);

            Assert.That(stored.Version, Is.EqualTo("1.0.0"));
            Assert.That(stored.SizeBytes, Is.EqualTo((long)payload.Length));

            SoftwarePackage? fetched = await folder.GetVersionAsync("1.0.0").ConfigureAwait(false);
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.Version, Is.EqualTo("1.0.0"));
        }

        [Test]
        public async Task OpenVersionReturnsPayloadContent()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            byte[] payload = [9, 8, 7, 6];
            await folder.AddVersionAsync(
                MakePackage("1.0.0"), new MemoryStream(payload)).ConfigureAwait(false);

            using Stream stream = await folder.OpenVersionAsync("1.0.0").ConfigureAwait(false);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy).ConfigureAwait(false);

            Assert.That(copy.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task ListVersionsEnumeratesPersistedVersions()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            await folder.AddVersionAsync(MakePackage("1.0.0"), new MemoryStream([1])).ConfigureAwait(false);
            await folder.AddVersionAsync(MakePackage("1.0.1"), new MemoryStream([2])).ConfigureAwait(false);
            await folder.AddVersionAsync(MakePackage("1.0.2"), new MemoryStream([3])).ConfigureAwait(false);

            var seen = new List<string>();
            await foreach (SoftwarePackage pkg in folder.ListVersionsAsync())
            {
                seen.Add(pkg.Version);
            }

            Assert.That(seen, Has.Count.EqualTo(3));
            Assert.That(seen, Does.Contain("1.0.0").And.Contain("1.0.1").And.Contain("1.0.2"));
        }

        [Test]
        public async Task GetVersionReturnsNullForUnknown()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            SoftwarePackage? r = await folder.GetVersionAsync("never").ConfigureAwait(false);
            Assert.That(r, Is.Null);
        }

        [Test]
        public async Task RemoveVersionDeletesAndReturnsTrue()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            await folder.AddVersionAsync(
                MakePackage("1.0.0"), new MemoryStream([1])).ConfigureAwait(false);

            Assert.That(await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false), Is.True);
            Assert.That(await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false), Is.False);
            Assert.That(await folder.GetVersionAsync("1.0.0").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task SetCurrentVersionMarksActive()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            await folder.AddVersionAsync(
                MakePackage("1.0.0"), new MemoryStream([1])).ConfigureAwait(false);
            await folder.AddVersionAsync(
                MakePackage("1.0.1"), new MemoryStream([2])).ConfigureAwait(false);

            await folder.SetCurrentVersionAsync("1.0.1").ConfigureAwait(false);

            SoftwarePackage? current = await folder.GetCurrentVersionAsync().ConfigureAwait(false);
            Assert.That(current, Is.Not.Null);
            Assert.That(current!.Version, Is.EqualTo("1.0.1"));
        }

        [Test]
        public void SetCurrentVersionUnknownThrows()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.SetCurrentVersionAsync("missing").ConfigureAwait(false));
        }

        [Test]
        public async Task RemovingCurrentVersionClearsMarker()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            await folder.AddVersionAsync(
                MakePackage("1.0.0"), new MemoryStream([1])).ConfigureAwait(false);
            await folder.SetCurrentVersionAsync("1.0.0").ConfigureAwait(false);

            Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Not.Null);
            await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false);
            Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Null);
        }

        [Test]
        public void AddVersionRejectsInvalidVersionString()
        {
            FileSystemSoftwareFolder folder = CreateFolder();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.AddVersionAsync(
                    MakePackage("bad/version"), new MemoryStream([1])).ConfigureAwait(false));
        }

        [Test]
        public void AddVersionOnReadOnlyProviderThrows()
        {
            FileSystemSoftwareFolder folder = CreateFolder(writable: false);
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await folder.AddVersionAsync(
                    MakePackage("1.0.0"), new MemoryStream([1])).ConfigureAwait(false));
        }
    }
}
