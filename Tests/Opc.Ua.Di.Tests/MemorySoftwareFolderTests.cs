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

#nullable enable

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for <see cref="MemorySoftwareFolder"/> — multi-version
    /// software repository for a single device.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("SoftwareUpdate")]
    public sealed class MemorySoftwareFolderTests
    {
        private static NodeId ElementId => new("Device", 2);

        private static SoftwarePackage MakePackage(string version, string description = "")
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

        private static MemoryStream Payload(string text)
        {
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        }

        [Test]
        public void ConstructorRejectsNullElementId()
        {
            Assert.Throws<ArgumentNullException>(() => new MemorySoftwareFolder(NodeId.Null));
        }

        [Test]
        public async Task AddVersionStoresPackageAndPayload()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            using MemoryStream payload = Payload("firmware-v1");

            SoftwarePackage stored = await folder.AddVersionAsync(
                MakePackage("1.0.0"), payload).ConfigureAwait(false);

            Assert.That(stored.Version, Is.EqualTo("1.0.0"));
            Assert.That(stored.SizeBytes, Is.EqualTo(payload.Length));
            Assert.That(stored.SizeBytes, Is.GreaterThan(0L));
            Assert.That(stored.CreatedAt, Is.Not.Default);

            SoftwarePackage? fetched = await folder.GetVersionAsync("1.0.0").ConfigureAwait(false);
            Assert.That(fetched, Is.Not.Null);
            Assert.That(fetched!.Version, Is.EqualTo("1.0.0"));
        }

        [Test]
        public async Task OpenVersionReturnsPayloadContent()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            using MemoryStream payload = Payload("expected-content");
            await folder.AddVersionAsync(MakePackage("1.0.0"), payload).ConfigureAwait(false);

            using Stream reader = await folder.OpenVersionAsync("1.0.0").ConfigureAwait(false);
            byte[] buffer = new byte[100];
#if NETSTANDARD2_1_OR_GREATER || NET
            int read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
#else
            int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif
            string content = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

            Assert.That(content, Is.EqualTo("expected-content"));
        }

        [Test]
        public async Task ListVersionsReturnsAllAddedVersions()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            await folder.AddVersionAsync(MakePackage("1.0.0"), Payload("v1")).ConfigureAwait(false);
            await folder.AddVersionAsync(MakePackage("1.0.1"), Payload("v1.1")).ConfigureAwait(false);
            await folder.AddVersionAsync(MakePackage("1.0.2-rc"), Payload("v1.2rc")).ConfigureAwait(false);

            var versions = new List<string>();
            await foreach (SoftwarePackage pkg in folder.ListVersionsAsync())
            {
                versions.Add(pkg.Version);
            }

            Assert.That(versions, Has.Count.EqualTo(3));
            Assert.That(versions, Does.Contain("1.0.0").And.Contain("1.0.1").And.Contain("1.0.2-rc"));
        }

        [Test]
        public async Task GetVersionForUnknownReturnsNull()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            SoftwarePackage? result = await folder.GetVersionAsync("never").ConfigureAwait(false);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void OpenVersionForUnknownThrowsFileNotFound()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await folder.OpenVersionAsync("never").ConfigureAwait(false));
        }

        [Test]
        public async Task RemoveVersionReturnsTrueWhenPresent()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            await folder.AddVersionAsync(MakePackage("1.0.0"), Payload("v1")).ConfigureAwait(false);

            Assert.That(await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false), Is.True);
            Assert.That(await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false), Is.False);
            Assert.That(await folder.GetVersionAsync("1.0.0").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task SetCurrentVersionMarksActive()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            await folder.AddVersionAsync(MakePackage("1.0.0"), Payload("v1")).ConfigureAwait(false);
            await folder.AddVersionAsync(MakePackage("1.0.1"), Payload("v1.1")).ConfigureAwait(false);

            await folder.SetCurrentVersionAsync("1.0.1").ConfigureAwait(false);

            SoftwarePackage? current = await folder.GetCurrentVersionAsync().ConfigureAwait(false);
            Assert.That(current, Is.Not.Null);
            Assert.That(current!.Version, Is.EqualTo("1.0.1"));
        }

        [Test]
        public void SetCurrentVersionUnknownThrowsArgumentException()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            Assert.ThrowsAsync<ArgumentException>(
                async () => await folder.SetCurrentVersionAsync("missing").ConfigureAwait(false));
        }

        [Test]
        public async Task RemovingCurrentVersionClearsCurrentMarker()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            await folder.AddVersionAsync(MakePackage("1.0.0"), Payload("v1")).ConfigureAwait(false);
            await folder.SetCurrentVersionAsync("1.0.0").ConfigureAwait(false);

            Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Not.Null);

            await folder.RemoveVersionAsync("1.0.0").ConfigureAwait(false);

            Assert.That(await folder.GetCurrentVersionAsync().ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task AddVersionWithSameVersionIdReplaces()
        {
            var folder = new MemorySoftwareFolder(ElementId);
            await folder.AddVersionAsync(
                MakePackage("1.0.0", "original"), Payload("v1-orig")).ConfigureAwait(false);
            await folder.AddVersionAsync(
                MakePackage("1.0.0", "replacement"), Payload("v1-new")).ConfigureAwait(false);

            int count = 0;
            await foreach (SoftwarePackage _ in folder.ListVersionsAsync())
            {
                count++;
            }
            Assert.That(count, Is.EqualTo(1));

            SoftwarePackage? fetched = await folder.GetVersionAsync("1.0.0").ConfigureAwait(false);
            Assert.That(fetched!.Description, Is.EqualTo("replacement"));
        }
    }
}
