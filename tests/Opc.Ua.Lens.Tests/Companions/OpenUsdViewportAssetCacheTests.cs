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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class OpenUsdViewportAssetCacheTests
    {
        [Test]
        public async Task CacheWritesOnceAndDeletesOnlyItsOwnCompleteClosureAsync()
        {
            string root = NewRoot();
            var cache = new OpenUsdViewportAssetCache(root);
            Assert.That(Directory.Exists(root), Is.False, "Construction is inert.");
            IOpenUsdViewportAssets lease = await cache.AcquireAsync(
                "cell.usda", "/World", Assets(), default).ConfigureAwait(false);
            string owned = Path.GetDirectoryName(lease.StagePath)!;
            try
            {
                Assert.That(File.Exists(lease.StagePath), Is.True);
                Assert.That(await File.ReadAllBytesAsync(Path.Combine(owned, "cell.usda")).ConfigureAwait(false),
                    Is.EqualTo(OpenUsdViewportTestContext.Content.ToArray()));
                Assert.That(await File.ReadAllTextAsync(lease.StagePath).ConfigureAwait(false),
                    Does.Contain("@./cell.usda@").And.Contain("@./ualens-snapshot.usda@"));
                await lease.DisposeAsync().ConfigureAwait(false);
                Assert.That(Directory.Exists(owned), Is.False);
                Assert.That(Directory.EnumerateFileSystemEntries(root), Is.Empty);
            }
            finally
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                Directory.Delete(root, recursive: false);
            }
        }

        [Test]
        public async Task CacheAdmissionCountsRetainedClosuresBeforeCreatingAnotherDirectoryAsync()
        {
            string root = NewRoot();
            var cache = new OpenUsdViewportAssetCache(root);
            var leases = new List<IOpenUsdViewportAssets>();
            try
            {
                for (int index = 0; index < 4; index++)
                {
                    leases.Add(await cache.AcquireAsync("cell.usda", "/World", Assets(), default)
                        .ConfigureAwait(false));
                }
                Assert.That(() => cache.AcquireAsync("cell.usda", "/World", Assets(), default),
                    Throws.TypeOf<IOException>());
                Assert.That(Directory.GetDirectories(root), Has.Length.EqualTo(4));
                await leases[0].DisposeAsync().ConfigureAwait(false);
                leases.Add(await cache.AcquireAsync("cell.usda", "/World", Assets(), default).ConfigureAwait(false));
                Assert.That(Directory.GetDirectories(root), Has.Length.EqualTo(4));
            }
            finally
            {
                foreach (IOpenUsdViewportAssets lease in leases)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                Directory.Delete(root, recursive: false);
            }
        }

        [Test]
        public void InvalidAssetClosureNeverCreatesACacheDirectory()
        {
            string root = NewRoot();
            var cache = new OpenUsdViewportAssetCache(root);
            Assert.That(() => cache.AcquireAsync("cell.usda", "/World",
                [OpenUsdViewportScenePolicyTests.Layer("cell.usda",
                    "#usda 1.0\ndef Xform \"World\" (references = @../outside.usda@) {}")], default),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(Directory.Exists(root), Is.False);
        }

        [Test]
        public async Task CleanupNeverRecursivelyDeletesUnexpectedUserFilesAsync()
        {
            string root = NewRoot();
            var cache = new OpenUsdViewportAssetCache(root);
            IOpenUsdViewportAssets lease = await cache.AcquireAsync(
                "cell.usda", "/World", Assets(), default).ConfigureAwait(false);
            string directory = Path.GetDirectoryName(lease.StagePath)!;
            string foreign = Path.Combine(directory, "user.txt");
            await File.WriteAllTextAsync(foreign, "keep").ConfigureAwait(false);
            try
            {
                Assert.That(async () => await lease.DisposeAsync().ConfigureAwait(false), Throws.TypeOf<IOException>());
                Assert.That(await File.ReadAllTextAsync(foreign).ConfigureAwait(false), Is.EqualTo("keep"));
            }
            finally
            {
                File.Delete(foreign);
                Directory.Delete(directory, recursive: false);
                Directory.Delete(root, recursive: false);
            }
        }

        private static ArrayOf<OpenUsdVerifiedAsset> Assets() => [new("cell.usda", OpenUsdViewportTestContext.Content)];

        private static string NewRoot() =>
            Path.GetFullPath(Path.Combine("TestResults", "viewport-cache", Guid.NewGuid().ToString("N")));
    }
}
