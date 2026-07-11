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

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="SharedSingleUseNonceRegistry"/>: a server nonce
    /// can be consumed at most once across the whole replica set, so a replayed
    /// <c>ActivateSession</c> is rejected on every replica.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class SingleUseNonceRegistryTests
    {
        [Test]
        public async Task FirstConsumeSucceedsSecondIsRejectedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(kv);
            var nonce = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            bool first = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);
            bool second = await registry.TryConsumeAsync(nonce).ConfigureAwait(false);

            Assert.That(first, Is.True, "first consumption is accepted");
            Assert.That(second, Is.False, "a replay of the same nonce is rejected");
        }

        [Test]
        public async Task DistinctNoncesAreEachAcceptedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(kv);

            bool a = await registry.TryConsumeAsync(ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            bool b = await registry.TryConsumeAsync(ByteString.From(new byte[] { 2 })).ConfigureAwait(false);

            Assert.That(a, Is.True);
            Assert.That(b, Is.True);
        }

        [Test]
        public async Task NonceConsumedOnOneReplicaIsRejectedOnAnotherAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var active = new SharedSingleUseNonceRegistry(kv);
            var standby = new SharedSingleUseNonceRegistry(kv);
            var nonce = ByteString.From(new byte[] { 9, 9, 9, 9 });

            bool onActive = await active.TryConsumeAsync(nonce).ConfigureAwait(false);
            bool onStandby = await standby.TryConsumeAsync(nonce).ConfigureAwait(false);

            Assert.That(onActive, Is.True);
            Assert.That(onStandby, Is.False, "no two replicas accept the same nonce");
        }

        [Test]
        public void NullOrEmptyNonceThrows()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(kv);

            Assert.That(
                async () => await registry.TryConsumeAsync(default).ConfigureAwait(false),
                Throws.ArgumentException);
            Assert.That(
                async () => await registry.TryConsumeAsync(ByteString.Empty).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public void NullStoreThrows()
        {
            Assert.That(() => new SharedSingleUseNonceRegistry(null!), Throws.ArgumentNullException);
        }
    }
}
