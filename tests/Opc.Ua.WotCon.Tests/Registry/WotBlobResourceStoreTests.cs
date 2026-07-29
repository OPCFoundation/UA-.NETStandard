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

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;
using Opc.Ua.XRegistry.Tests;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Runs the shared <see cref="IXRegistryResourceStore"/> contract against the WoT registry's
    /// content-addressed blob store, so it stays substitutable for any other implementation.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotBlobResourceStoreContractTests : XRegistryResourceStoreContractTests
    {
        /// <inheritdoc/>
        protected override IXRegistryResourceStore CreateStore()
        {
            m_fileSystem ??= new VirtualFileSystem();
            return new WotBlobResourceStore("blobs", m_fileSystem);
        }

        [TearDown]
        public void TearDown()
        {
            m_fileSystem?.Dispose();
            m_fileSystem = null;
        }

        private VirtualFileSystem? m_fileSystem;
    }

    /// <summary>
    /// Behaviour specific to the WoT blob store: the on-disk layout must stay byte-compatible with
    /// what <c>FileWotRegistryStore</c> has always written, so an existing registry folder keeps
    /// working when the byte layer moves behind the injectable xRegistry interface.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotBlobResourceStoreLayoutTests
    {
        [Test]
        public async Task ADigestKeyIsStoredAsTheHistoricalBlobFileNameAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            using var store = new WotBlobResourceStore("blobs", fileSystem);
            const string digest = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

            await store.WriteAsync(digest, 0, ByteString.From([1, 2, 3])).ConfigureAwait(false);

            Assert.That(fileSystem.Exists(Path.Combine("blobs", digest + ".bin")), Is.True,
                "The historical blobs/{digest}.bin layout must be preserved so that moving the " +
                "byte layer behind IXRegistryResourceStore needs no on-disk migration.");
        }

        [Test]
        public async Task AKeyThatIsNotFileNameSafeIsEncodedAndStillRoundTripsAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            using var store = new WotBlobResourceStore("blobs", fileSystem);

            await store.WriteAsync("a/b", 0, ByteString.From([7])).ConfigureAwait(false);
            ByteString read = await store.ReadAsync("a/b", 0, 16).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(read.Span.ToArray(), Is.EqualTo(new byte[] { 7 }));
                Assert.That(fileSystem.Exists(Path.Combine("blobs", "a", "b.bin")), Is.False,
                    "A key containing a separator must not escape into a sub-directory.");
            });
        }

        [Test]
        public async Task AnEncodedKeyCannotCollideWithAVerbatimKeyAsync()
        {
            using var fileSystem = new VirtualFileSystem();
            using var store = new WotBlobResourceStore("blobs", fileSystem);

            // "a/b" encodes to the hex of its UTF-8 bytes; the verbatim key below is that same
            // hex text, so without the encoded-form prefix the two would share a file.
            await store.WriteAsync("a/b", 0, ByteString.From([1])).ConfigureAwait(false);
            await store.WriteAsync("612f62", 0, ByteString.From([2])).ConfigureAwait(false);

            ByteString encoded = await store.ReadAsync("a/b", 0, 16).ConfigureAwait(false);
            ByteString verbatim = await store.ReadAsync("612f62", 0, 16).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(encoded.Span.ToArray(), Is.EqualTo(new byte[] { 1 }));
                Assert.That(verbatim.Span.ToArray(), Is.EqualTo(new byte[] { 2 }));
            });
        }
    }
}
