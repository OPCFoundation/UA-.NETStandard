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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Server.Tests
{
    /// <summary>
    /// Verifies the default in-process resource store. The store is the seam that lets a registry
    /// keep its documents outside the server process in a high-availability deployment, so its
    /// contract has to hold for the substitutable implementations too.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class InMemoryXRegistryResourceStoreTests
    {
        [Test]
        public async Task ReadReturnsNullForAnUnknownKeyAsync()
        {
            var store = new InMemoryXRegistryResourceStore();

            ByteString document = await store.ReadAsync("absent").ConfigureAwait(false);

            Assert.That(document.IsNull, Is.True);
        }

        [Test]
        public async Task WriteThenReadRoundTripsTheDocumentAsync()
        {
            var store = new InMemoryXRegistryResourceStore();
            byte[] expected = [1, 2, 3, 4];

            await store.WriteAsync("urn:resource", expected).ConfigureAwait(false);
            ByteString document = await store.ReadAsync("urn:resource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(document.IsNull, Is.False);
                Assert.That(document.Span.ToArray(), Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task WriteCopiesTheCallerBufferAsync()
        {
            var store = new InMemoryXRegistryResourceStore();
            byte[] source = [1, 2, 3];

            await store.WriteAsync("urn:resource", source).ConfigureAwait(false);
            source[0] = 0xFF;
            ByteString document = await store.ReadAsync("urn:resource").ConfigureAwait(false);

            Assert.That(document.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }),
                "A later mutation of the caller's buffer must not change the stored document.");
        }

        [Test]
        public async Task WriteReplacesAnExistingDocumentAsync()
        {
            var store = new InMemoryXRegistryResourceStore();

            await store.WriteAsync("urn:resource", new byte[] { 1 }).ConfigureAwait(false);
            await store.WriteAsync("urn:resource", new byte[] { 9, 9 }).ConfigureAwait(false);
            ByteString document = await store.ReadAsync("urn:resource").ConfigureAwait(false);

            Assert.That(document.Span.ToArray(), Is.EqualTo(new byte[] { 9, 9 }));
        }

        [Test]
        public async Task DeleteRemovesTheDocumentAndReportsWhetherItExistedAsync()
        {
            var store = new InMemoryXRegistryResourceStore();
            await store.WriteAsync("urn:resource", new byte[] { 1 }).ConfigureAwait(false);

            bool removed = await store.DeleteAsync("urn:resource").ConfigureAwait(false);
            bool removedAgain = await store.DeleteAsync("urn:resource").ConfigureAwait(false);
            ByteString document = await store.ReadAsync("urn:resource").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.True);
                Assert.That(removedAgain, Is.False);
                Assert.That(document.IsNull, Is.True);
            });
        }

        [Test]
        public async Task DocumentsAreKeptPerKeyAsync()
        {
            var store = new InMemoryXRegistryResourceStore();

            await store.WriteAsync("a", new byte[] { 1 }).ConfigureAwait(false);
            await store.WriteAsync("b", new byte[] { 2 }).ConfigureAwait(false);

            ByteString a = await store.ReadAsync("a").ConfigureAwait(false);
            ByteString b = await store.ReadAsync("b").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(a.Span.ToArray(), Is.EqualTo(new byte[] { 1 }));
                Assert.That(b.Span.ToArray(), Is.EqualTo(new byte[] { 2 }));
            });
        }

        [Test]
        public void EmptyKeysAreRejected()
        {
            var store = new InMemoryXRegistryResourceStore();

            Assert.Multiple(() =>
            {
                Assert.That(() => store.ReadAsync(string.Empty).AsTask(), Throws.ArgumentException);
                Assert.That(() => store.WriteAsync(string.Empty, new byte[1]).AsTask(), Throws.ArgumentException);
                Assert.That(() => store.DeleteAsync(string.Empty).AsTask(), Throws.ArgumentException);
                Assert.That(() => store.ReadAsync(null!).AsTask(), Throws.ArgumentException);
            });
        }

        [Test]
        public void OptionsDefaultToTheInProcessStore()
        {
            var options = new XRegistryServerOptions();

            Assert.That(options.ResourceStore, Is.InstanceOf<InMemoryXRegistryResourceStore>());
        }
    }
}
