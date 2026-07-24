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

// CA1861: collection-literal arguments inside Is.EqualTo/Is.EquivalentTo
// assertions are evaluated once per test, not on a hot path. Lifting them
// to static fields per call-site adds noise without measurable benefit.
#pragma warning disable CA1861

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for the software-package storage layer
    /// (<see cref="MemoryPackageStore"/>,
    /// <see cref="FileSystemPackageStore"/>).
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("SoftwareUpdate")]
    public sealed class PackageStoreTests
    {
        [Test]
        public async Task MemoryStoreAddAndGetRoundTrip()
        {
            var store = new MemoryPackageStore();
            byte[] payload = [1, 2, 3, 4, 5];
            SoftwarePackage stored = await store.AddAsync(
                NewMetadata("fw-1"),
                new MemoryStream(payload)).ConfigureAwait(false);

            Assert.That(stored.SizeBytes, Is.EqualTo(payload.LongLength));
            Assert.That(stored.Id, Is.EqualTo("fw-1"));

            SoftwarePackage? roundTrip = await store.GetAsync("fw-1").ConfigureAwait(false);
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.Vendor, Is.EqualTo("Acme"));

            using Stream stream = await store.OpenReadAsync("fw-1").ConfigureAwait(false);
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy).ConfigureAwait(false);
            Assert.That(copy.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task MemoryStoreListEnumeratesAllPackages()
        {
            var store = new MemoryPackageStore();
            await store.AddAsync(NewMetadata("fw-1"), new MemoryStream([1])).ConfigureAwait(false);
            await store.AddAsync(NewMetadata("fw-2"), new MemoryStream([2, 3])).ConfigureAwait(false);
            await store.AddAsync(NewMetadata("fw-3"), new MemoryStream([4, 5, 6])).ConfigureAwait(false);

            var seen = new HashSet<string>();
            await foreach (SoftwarePackage p in store.ListAsync())
            {
                seen.Add(p.Id);
            }

            Assert.That(seen, Is.EquivalentTo(["fw-1", "fw-2", "fw-3"]));
        }

        [Test]
        public async Task MemoryStoreExistsAndDelete()
        {
            var store = new MemoryPackageStore();
            await store.AddAsync(NewMetadata("fw-1"), new MemoryStream([1])).ConfigureAwait(false);

            Assert.That(await store.ExistsAsync("fw-1").ConfigureAwait(false), Is.True);
            Assert.That(await store.ExistsAsync("fw-missing").ConfigureAwait(false), Is.False);

            Assert.That(await store.DeleteAsync("fw-1").ConfigureAwait(false), Is.True);
            Assert.That(await store.DeleteAsync("fw-1").ConfigureAwait(false), Is.False,
                "Second delete must return false.");
            Assert.That(await store.ExistsAsync("fw-1").ConfigureAwait(false), Is.False);
        }

        [Test]
        public void MemoryStoreOpenReadThrowsForMissingPackage()
        {
            var store = new MemoryPackageStore();
            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.OpenReadAsync("nope").ConfigureAwait(false));
        }

        [Test]
        public void MemoryStoreRejectsEmptyId()
        {
            var store = new MemoryPackageStore();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await store.GetAsync(string.Empty).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await store.ExistsAsync("   ").ConfigureAwait(false));
        }

        [Test]
        public async Task FileSystemStoreAddAndGetRoundTripViaPhysicalProvider()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(provider, rootPath: "/SoftwarePackages");

                byte[] payload = [10, 20, 30, 40, 50];
                SoftwarePackage stored = await store.AddAsync(
                    NewMetadata("fw-x"),
                    new MemoryStream(payload)).ConfigureAwait(false);

                Assert.That(stored.SizeBytes, Is.EqualTo(payload.LongLength));

                SoftwarePackage? roundTrip = await store.GetAsync("fw-x").ConfigureAwait(false);
                Assert.That(roundTrip, Is.Not.Null);
                Assert.That(roundTrip!.Id, Is.EqualTo("fw-x"));
                Assert.That(roundTrip.Vendor, Is.EqualTo("Acme"));
                Assert.That(roundTrip.SizeBytes, Is.EqualTo(payload.LongLength));

                using Stream stream = await store.OpenReadAsync("fw-x").ConfigureAwait(false);
                using MemoryStream copy = new();
                await stream.CopyToAsync(copy).ConfigureAwait(false);
                Assert.That(copy.ToArray(), Is.EqualTo(payload));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public async Task FileSystemStoreListReturnsKnownPackages()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(provider, rootPath: "/Pkgs");

                await store.AddAsync(NewMetadata("a"), new MemoryStream([1])).ConfigureAwait(false);
                await store.AddAsync(NewMetadata("b"), new MemoryStream([2, 3])).ConfigureAwait(false);

                var seen = new HashSet<string>();
                await foreach (SoftwarePackage p in store.ListAsync())
                {
                    seen.Add(p.Id);
                }

                Assert.That(seen, Is.EquivalentTo(["a", "b"]));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public async Task FileSystemStoreDeleteRemovesPackage()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(provider, rootPath: "/Pkgs");
                await store.AddAsync(NewMetadata("doomed"), new MemoryStream([0])).ConfigureAwait(false);

                Assert.That(await store.DeleteAsync("doomed").ConfigureAwait(false), Is.True);
                Assert.That(await store.ExistsAsync("doomed").ConfigureAwait(false), Is.False);
                Assert.That(await store.DeleteAsync("doomed").ConfigureAwait(false), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public void FileSystemStoreRejectsIdWithPathSeparator()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(provider, rootPath: "/Pkgs");
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await store.GetAsync("bad/id").ConfigureAwait(false));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await store.GetAsync("bad\\id").ConfigureAwait(false));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [TestCase(".")]
        [TestCase("..")]
        public void FileSystemStoreRejectsDotSegmentId(string packageId)
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(provider, rootPath: "/Pkgs");

                Assert.ThrowsAsync<ArgumentException>(
                    async () => await store.GetAsync(packageId).ConfigureAwait(false));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [TestCase("/Pkgs/./Nested")]
        [TestCase("/Pkgs/../Outside")]
        [TestCase("\\Pkgs\\..\\Outside")]
        public void FileSystemStoreRejectsRootWithDotSegment(string rootPath)
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);

                Assert.That(
                    () => new FileSystemPackageStore(provider, rootPath),
                    Throws.TypeOf<ArgumentException>());
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        [Test]
        public async Task FileSystemStoreCanonicalizesProviderRelativeRootAsync()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "di-pkg-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var provider = new PhysicalFileSystemProvider(
                    rootDirectory: tempRoot,
                    mountName: "Packages",
                    isWritable: true);
                var store = new FileSystemPackageStore(
                    provider,
                    rootPath: "\\Pkgs\\\\Nested\\");
                using var payload = new MemoryStream([1, 2, 3]);

                await store.AddAsync(
                    NewMetadata("firmware"),
                    payload).ConfigureAwait(false);

                string packageRoot = Path.Combine(tempRoot, "Pkgs", "Nested", "firmware");
                Assert.That(File.Exists(Path.Combine(packageRoot, "payload.bin")), Is.True);
                Assert.That(File.Exists(Path.Combine(packageRoot, "metadata.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(tempRoot, "payload.bin")), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        private static SoftwarePackage NewMetadata(string id)
        {
            return new(
                Id: id,
                Version: "1.0.0",
                Vendor: "Acme",
                Description: "Test package",
                SizeBytes: 0,
                CreatedAt: default,
                Hash: string.Empty);
        }
    }
}
