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
            byte[] payload = new byte[] { 1, 2, 3, 4, 5 };
            SoftwarePackage stored = await store.AddAsync(
                NewMetadata("fw-1"),
                new MemoryStream(payload));

            Assert.That(stored.SizeBytes, Is.EqualTo(payload.LongLength));
            Assert.That(stored.Id, Is.EqualTo("fw-1"));

            SoftwarePackage? roundTrip = await store.GetAsync("fw-1");
            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.Vendor, Is.EqualTo("Acme"));

            using Stream stream = await store.OpenReadAsync("fw-1");
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy);
            Assert.That(copy.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task MemoryStoreListEnumeratesAllPackages()
        {
            var store = new MemoryPackageStore();
            await store.AddAsync(NewMetadata("fw-1"), new MemoryStream(new byte[] { 1 }));
            await store.AddAsync(NewMetadata("fw-2"), new MemoryStream(new byte[] { 2, 3 }));
            await store.AddAsync(NewMetadata("fw-3"), new MemoryStream(new byte[] { 4, 5, 6 }));

            var seen = new HashSet<string>();
            await foreach (SoftwarePackage p in store.ListAsync())
            {
                seen.Add(p.Id);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { "fw-1", "fw-2", "fw-3" }));
        }

        [Test]
        public async Task MemoryStoreExistsAndDelete()
        {
            var store = new MemoryPackageStore();
            await store.AddAsync(NewMetadata("fw-1"), new MemoryStream(new byte[] { 1 }));

            Assert.That(await store.ExistsAsync("fw-1"), Is.True);
            Assert.That(await store.ExistsAsync("fw-missing"), Is.False);

            Assert.That(await store.DeleteAsync("fw-1"), Is.True);
            Assert.That(await store.DeleteAsync("fw-1"), Is.False,
                "Second delete must return false.");
            Assert.That(await store.ExistsAsync("fw-1"), Is.False);
        }

        [Test]
        public void MemoryStoreOpenReadThrowsForMissingPackage()
        {
            var store = new MemoryPackageStore();
            Assert.ThrowsAsync<FileNotFoundException>(
                async () => await store.OpenReadAsync("nope"));
        }

        [Test]
        public void MemoryStoreRejectsEmptyId()
        {
            var store = new MemoryPackageStore();
            Assert.ThrowsAsync<ArgumentException>(
                async () => await store.GetAsync(string.Empty));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await store.ExistsAsync("   "));
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

                byte[] payload = new byte[] { 10, 20, 30, 40, 50 };
                SoftwarePackage stored = await store.AddAsync(
                    NewMetadata("fw-x"),
                    new MemoryStream(payload));

                Assert.That(stored.SizeBytes, Is.EqualTo(payload.LongLength));

                SoftwarePackage? roundTrip = await store.GetAsync("fw-x");
                Assert.That(roundTrip, Is.Not.Null);
                Assert.That(roundTrip!.Id, Is.EqualTo("fw-x"));
                Assert.That(roundTrip.Vendor, Is.EqualTo("Acme"));
                Assert.That(roundTrip.SizeBytes, Is.EqualTo(payload.LongLength));

                using Stream stream = await store.OpenReadAsync("fw-x");
                using MemoryStream copy = new();
                await stream.CopyToAsync(copy);
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

                await store.AddAsync(NewMetadata("a"), new MemoryStream(new byte[] { 1 }));
                await store.AddAsync(NewMetadata("b"), new MemoryStream(new byte[] { 2, 3 }));

                var seen = new HashSet<string>();
                await foreach (SoftwarePackage p in store.ListAsync())
                {
                    seen.Add(p.Id);
                }

                Assert.That(seen, Is.EquivalentTo(new[] { "a", "b" }));
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
                await store.AddAsync(NewMetadata("doomed"), new MemoryStream(new byte[] { 0 }));

                Assert.That(await store.DeleteAsync("doomed"), Is.True);
                Assert.That(await store.ExistsAsync("doomed"), Is.False);
                Assert.That(await store.DeleteAsync("doomed"), Is.False);
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
                    async () => await store.GetAsync("bad/id"));
                Assert.ThrowsAsync<ArgumentException>(
                    async () => await store.GetAsync("bad\\id"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        private static SoftwarePackage NewMetadata(string id) =>
            new(
                Id: id,
                Version: "1.0.0",
                Vendor: "Acme",
                Description: "Test package",
                SizeBytes: 0,
                CreatedAt: default,
                Hash: string.Empty);
    }
}
