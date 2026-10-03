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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Tests the stores that persist what clients configure on assets.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    public sealed class AssetConfigurationStoreTests
    {
        private const string Uri = "urn:acme:sensor:4711";

        [Test]
        public async Task MemoryStoreKeepsValuesPerAssetAndNameAsync()
        {
            var store = new MemoryAssetConfigurationStore();

            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
            await store.SetValueAsync("urn:other", AssetConfigurationNames.AssetId, "Other").ConfigureAwait(false);

            string? first = await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false);
            string? other = await store.GetValueAsync("urn:other", AssetConfigurationNames.AssetId).ConfigureAwait(false);
            string? unknown = await store.GetValueAsync(Uri, "Location").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(store.IsPersistent, Is.False);
                Assert.That(first, Is.EqualTo("Sensor-1"));
                Assert.That(other, Is.EqualTo("Other"));
                Assert.That(unknown, Is.Null);
            });

            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, null).ConfigureAwait(false);
            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public void StoresRejectValuesBeyondTheLimit()
        {
            string tooLong = new('v', AssetConfigurationNames.MaxValueLength + 1);
            var memory = new MemoryAssetConfigurationStore();
            using var fileSystem = new FileSystemAssetConfigurationStore(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(StoresRejectValuesBeyondTheLimit),
                Guid.NewGuid().ToString("N")));

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await memory.SetValueAsync(Uri, "AssetId", tooLong).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await fileSystem.SetValueAsync(Uri, "AssetId", tooLong).ConfigureAwait(false));
            });
        }

        [Test]
        public void StoresRejectEmptyKeys()
        {
            var store = new MemoryAssetConfigurationStore();

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await store.GetValueAsync(string.Empty, "AssetId").ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await store.GetValueAsync(Uri, string.Empty).ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(async () => await store.SetValueAsync(null!, "AssetId", "x").ConfigureAwait(false));
            });
        }

        [Test]
        public async Task FileSystemStoreSurvivesANewInstanceAsync()
        {
            string directory = NewDirectory();
            using (var store = new FileSystemAssetConfigurationStore(directory))
            {
                Assert.That(store.IsPersistent, Is.True);
                Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.Null);
                await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
                await store.SetValueAsync(Uri, "Location", "Hall 3").ConfigureAwait(false);
                await store.SetValueAsync(Uri, "Location", null).ConfigureAwait(false);
            }

            using var reopened = new FileSystemAssetConfigurationStore(directory);
            string? assetId = await reopened.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false);
            string? location = await reopened.GetValueAsync(Uri, "Location").ConfigureAwait(false);
            string? other = await reopened.GetValueAsync("urn:other", AssetConfigurationNames.AssetId).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(assetId, Is.EqualTo("Sensor-1"));
                Assert.That(location, Is.Null);
                Assert.That(other, Is.Null);
            });

            // The URI, which a client may have influenced, never reaches the path.
            string[] files = Directory.GetFiles(directory);
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Does.Match("^[0-9a-f]{64}\\.json$"));
        }

        [Test]
        public async Task FileSystemStoreIgnoresAnUnreadableDocumentAsync()
        {
            string directory = NewDirectory();
            using var store = new FileSystemAssetConfigurationStore(directory);
            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
            string file = Directory.GetFiles(directory)[0];

            await WriteAllTextAsync(file, "{ \"productInstanceUri\": ").ConfigureAwait(false);
            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.Null);

            // A write replaces the broken document.
            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-2").ConfigureAwait(false);
            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.EqualTo("Sensor-2"));
        }

        [Test]
        public async Task FileSystemStoreRecoversAWriteInterruptedBeforeTheReplaceAsync()
        {
            string directory = NewDirectory();
            using var store = new FileSystemAssetConfigurationStore(directory);
            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
            string file = Directory.GetFiles(directory)[0];

            // A crash after the old document was deleted: only the complete
            // new one is left, under its temporary name.
            File.Move(file, file + ".tmp");
            string? recovered = await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false);

            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-2").ConfigureAwait(false);
            string[] files = Directory.GetFiles(directory);

            Assert.Multiple(() =>
            {
                Assert.That(recovered, Is.EqualTo("Sensor-1"));
                Assert.That(files, Is.EqualTo(new[] { file }), "the next write replaces it and leaves no temporary");
            });
            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.EqualTo("Sensor-2"));
        }

        [Test]
        public async Task FileSystemStoreKeepsTheLastCompleteDocumentAsync()
        {
            string directory = NewDirectory();
            using var store = new FileSystemAssetConfigurationStore(directory);
            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
            string file = Directory.GetFiles(directory)[0];

            // A crash while the new document was written.
            await WriteAllTextAsync(file + ".tmp", "{ \"productInstanceUri\": ").ConfigureAwait(false);

            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.EqualTo("Sensor-1"));
        }

        [Test]
        public async Task FileSystemStoreIgnoresADocumentOfAnotherAssetAsync()
        {
            string directory = NewDirectory();
            using var store = new FileSystemAssetConfigurationStore(directory);
            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);
            string file = Directory.GetFiles(directory)[0];
            await WriteAllTextAsync(
                file,
                "{ \"productInstanceUri\": \"urn:other\", \"values\": { \"AssetId\": \"Other\" } }").ConfigureAwait(false);

            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task FileSystemStoreWorksBelowARootPathOfAProviderAsync()
        {
            string directory = NewDirectory();
            var provider = new PhysicalFileSystemProvider(directory);
            using var store = new FileSystemAssetConfigurationStore(provider, "/state/amb/");

            await store.SetValueAsync(Uri, AssetConfigurationNames.AssetId, "Sensor-1").ConfigureAwait(false);

            Assert.That(await store.GetValueAsync(Uri, AssetConfigurationNames.AssetId).ConfigureAwait(false), Is.EqualTo("Sensor-1"));
            Assert.That(Directory.GetFiles(Path.Combine(directory, "state", "amb")), Has.Length.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("state")]
        [TestCase("/state/../escape")]
        [TestCase("/state\\amb")]
        public void FileSystemStoreRejectsAnInvalidRootPath(string rootPath)
        {
            var provider = new PhysicalFileSystemProvider(NewDirectory());

            Assert.Throws<ArgumentException>(() => _ = new FileSystemAssetConfigurationStore(provider, rootPath));
        }

        [Test]
        public void FileSystemStoreRejectsMissingArguments()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => _ = new FileSystemAssetConfigurationStore((IFileSystemProvider)null!));
                Assert.Throws<ArgumentException>(() => _ = new FileSystemAssetConfigurationStore(" "));
            });
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AssetConfigurationStoreTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>
        /// Writes a document the way a crash or another process would leave
        /// it (File.WriteAllTextAsync is not available on .NET Framework).
        /// </summary>
        private static async Task WriteAllTextAsync(string path, string text)
        {
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }
}
