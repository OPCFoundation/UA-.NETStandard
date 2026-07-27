/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// The contract every <see cref="IXRegistryResourceStore"/> has to honour. The store is the seam
    /// that lets a registry keep its documents outside the server process in a high-availability
    /// deployment, so the contract is asserted against each implementation rather than only against
    /// the default one.
    /// </summary>
    public abstract class XRegistryResourceStoreContractTests
    {
        /// <summary>
        /// Creates the store under test.
        /// </summary>
        protected abstract IXRegistryResourceStore CreateStore();

        [Test]
        public async Task ReadReturnsNullForAnUnknownKeyAsync()
        {
            IXRegistryResourceStore store = CreateStore();

            ByteString document = await store.ReadAsync("absent", 0, 16).ConfigureAwait(false);

            Assert.That(document.IsNull, Is.True,
                "A null ByteString distinguishes an unknown key from an empty document.");
        }

        [Test]
        public async Task GetLengthReportsMinusOneForAnUnknownKeyAsync()
        {
            IXRegistryResourceStore store = CreateStore();

            Assert.That(await store.GetLengthAsync("absent").ConfigureAwait(false), Is.EqualTo(-1));
        }

        [Test]
        public async Task WriteThenReadRoundTripsTheDocumentAsync()
        {
            IXRegistryResourceStore store = CreateStore();

            await store.WriteAsync("a", 0, s_document).ConfigureAwait(false);
            ByteString document = await store.ReadAsync("a", 0, s_document.Length).ConfigureAwait(false);

            Assert.Multiple(async () =>
            {
                Assert.That(document.Span.ToArray(), Is.EqualTo(s_document));
                Assert.That(await store.GetLengthAsync("a").ConfigureAwait(false),
                    Is.EqualTo(s_document.Length));
            });
        }

        [Test]
        public async Task ReadReturnsTheRequestedSliceAsync()
        {
            IXRegistryResourceStore store = CreateStore();
            await store.WriteAsync("a", 0, s_document).ConfigureAwait(false);

            ByteString slice = await store.ReadAsync("a", 1, 2).ConfigureAwait(false);

            Assert.That(slice.Span.ToArray(), Is.EqualTo(new byte[] { 0x02, 0x03 }));
        }

        [Test]
        public async Task ReadIsClampedToTheEndOfTheDocumentAsync()
        {
            IXRegistryResourceStore store = CreateStore();
            await store.WriteAsync("a", 0, s_document).ConfigureAwait(false);

            ByteString tail = await store.ReadAsync("a", 2, 100).ConfigureAwait(false);
            ByteString past = await store.ReadAsync("a", 100, 4).ConfigureAwait(false);
            ByteString none = await store.ReadAsync("a", 0, 0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(tail.Span.ToArray(), Is.EqualTo(new byte[] { 0x03, 0x04 }));
                Assert.That(past.IsNull, Is.False, "The key exists, so this is empty and not null.");
                Assert.That(past.Span.Length, Is.Zero);
                Assert.That(none.Span.Length, Is.Zero);
            });
        }

        [Test]
        public async Task WriteAtAnOffsetOverwritesInPlaceAsync()
        {
            IXRegistryResourceStore store = CreateStore();
            await store.WriteAsync("a", 0, s_document).ConfigureAwait(false);

            await store.WriteAsync("a", 1, new byte[] { 0xAA, 0xBB }).ConfigureAwait(false);
            ByteString document = await store.ReadAsync("a", 0, 16).ConfigureAwait(false);

            Assert.Multiple(async () =>
            {
                Assert.That(document.Span.ToArray(),
                    Is.EqualTo(new byte[] { 0x01, 0xAA, 0xBB, 0x04 }));
                Assert.That(await store.GetLengthAsync("a").ConfigureAwait(false), Is.EqualTo(4),
                    "An in-place overwrite does not change the length.");
            });
        }

        [Test]
        public async Task WritePastTheEndGrowsTheDocumentAsync()
        {
            IXRegistryResourceStore store = CreateStore();
            await store.WriteAsync("a", 0, new byte[] { 0x01, 0x02 }).ConfigureAwait(false);

            await store.WriteAsync("a", 4, new byte[] { 0x05 }).ConfigureAwait(false);
            ByteString document = await store.ReadAsync("a", 0, 16).ConfigureAwait(false);

            Assert.That(document.Span.ToArray(),
                Is.EqualTo(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x05 }),
                "The gap created by writing past the end reads back as zero bytes.");
        }

        [Test]
        public async Task SequentialWritesAppendAsync()
        {
            IXRegistryResourceStore store = CreateStore();

            await store.WriteAsync("a", 0, new byte[] { 0x01, 0x02 }).ConfigureAwait(false);
            await store.WriteAsync("a", 2, new byte[] { 0x03, 0x04 }).ConfigureAwait(false);

            ByteString document = await store.ReadAsync("a", 0, 16).ConfigureAwait(false);
            Assert.That(document.Span.ToArray(), Is.EqualTo(s_document));
        }

        [Test]
        public async Task DeleteRemovesTheDocumentAsync()
        {
            IXRegistryResourceStore store = CreateStore();
            await store.WriteAsync("a", 0, s_document).ConfigureAwait(false);

            bool removed = await store.DeleteAsync("a").ConfigureAwait(false);
            bool again = await store.DeleteAsync("a").ConfigureAwait(false);

            Assert.Multiple(async () =>
            {
                Assert.That(removed, Is.True);
                Assert.That(again, Is.False, "Deleting an absent key is a no-op, not a fault.");
                Assert.That(
                    (await store.ReadAsync("a", 0, 16).ConfigureAwait(false)).IsNull, Is.True);
            });
        }

        [Test]
        public async Task KeysAreIsolatedFromEachOtherAsync()
        {
            IXRegistryResourceStore store = CreateStore();

            await store.WriteAsync("a", 0, new byte[] { 0x01 }).ConfigureAwait(false);
            await store.WriteAsync("b", 0, new byte[] { 0x02 }).ConfigureAwait(false);

            Assert.Multiple(async () =>
            {
                Assert.That(
                    (await store.ReadAsync("a", 0, 16).ConfigureAwait(false)).Span.ToArray(),
                    Is.EqualTo(new byte[] { 0x01 }));
                Assert.That(
                    (await store.ReadAsync("b", 0, 16).ConfigureAwait(false)).Span.ToArray(),
                    Is.EqualTo(new byte[] { 0x02 }));
            });
        }

        [Test]
        public void AnEmptyKeyIsRejected()
        {
            IXRegistryResourceStore store = CreateStore();

            Assert.Multiple(() =>
            {
                Assert.That(() => store.ReadAsync(string.Empty, 0, 1).AsTask(),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(() => store.WriteAsync(string.Empty, 0, s_document).AsTask(),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(() => store.GetLengthAsync(string.Empty).AsTask(),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(() => store.DeleteAsync(string.Empty).AsTask(),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void ANegativeOffsetOrCountIsRejected()
        {
            IXRegistryResourceStore store = CreateStore();

            Assert.Multiple(() =>
            {
                Assert.That(() => store.ReadAsync("a", -1, 1).AsTask(),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => store.ReadAsync("a", 0, -1).AsTask(),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => store.WriteAsync("a", -1, s_document).AsTask(),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        private static readonly byte[] s_document = [0x01, 0x02, 0x03, 0x04];
    }

    /// <summary>
    /// Runs the store contract against the default in-process store.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class InMemoryResourceStoreTests : XRegistryResourceStoreContractTests
    {
        /// <inheritdoc/>
        protected override IXRegistryResourceStore CreateStore()
        {
            return new InMemoryResourceStore();
        }
    }

    /// <summary>
    /// Runs the store contract against the file-backed store, over a virtual file system so the test
    /// touches no real disk.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class FileSystemResourceStoreTests : XRegistryResourceStoreContractTests
    {
        [SetUp]
        public void SetUp()
        {
            m_fileSystem = new VirtualFileSystem();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable store in m_stores)
            {
                store.Dispose();
            }
            m_stores.Clear();
            m_fileSystem?.Dispose();
        }

        [Test]
        public void ARootPathIsRequired()
        {
            Assert.That(
                () => new FileSystemResourceStore(string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        /// <inheritdoc/>
        protected override IXRegistryResourceStore CreateStore()
        {
            var store = new FileSystemResourceStore("resources", m_fileSystem);
            m_stores.Add(store);
            return store;
        }

        private readonly System.Collections.Generic.List<IDisposable> m_stores = [];
        private VirtualFileSystem? m_fileSystem;
    }
}
