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
 *
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

namespace Opc.Ua.Core.Tests.Security.Secrets
{
    /// <summary>
    /// Tests for <see cref="InMemorySecretStore"/> and
    /// <see cref="SecretRegistry"/>.
    /// </summary>
    [TestFixture]
    [Category("Secrets")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SecretStoreTests
    {
        [Test]
        public void TryGetReturnsNullForUnknownSecret()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("missing", InMemorySecretStore.DefaultStoreType);

            ISecret secret = store.TryGet(id);

            Assert.That(secret, Is.Null);
        }

        [Test]
        public async Task SetAsyncStoresAndTryGetReturnsCopyOfBytes()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("password", InMemorySecretStore.DefaultStoreType);
            byte[] expected = [0x01, 0x02, 0x03, 0x04];

            await store.SetAsync(id, expected).ConfigureAwait(false);

            using ISecret secret = store.TryGet(id);
            Assert.That(secret, Is.Not.Null);
            Assert.That(secret.Bytes.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public async Task GetAsyncCompletesSynchronouslyOnHit()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("password", InMemorySecretStore.DefaultStoreType);
            await store.SetAsync(id, new byte[] { 0xAB }).ConfigureAwait(false);

            ValueTask<ISecret> task = store.GetAsync(id);

            Assert.That(task.IsCompletedSuccessfully, Is.True,
                "InMemorySecretStore.GetAsync must complete sync on cache hit.");
            using ISecret secret = task.Result;
            Assert.That(secret.Bytes[0], Is.EqualTo((byte)0xAB));
        }

        [Test]
        public async Task RemoveAsyncReturnsFalseWhenAbsent()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("missing", InMemorySecretStore.DefaultStoreType);

            bool removed = await store.RemoveAsync(id).ConfigureAwait(false);

            Assert.That(removed, Is.False);
        }

        [Test]
        public async Task RemoveAsyncReturnsTrueWhenPresent()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("password", InMemorySecretStore.DefaultStoreType);
            await store.SetAsync(id, new byte[] { 0xAB }).ConfigureAwait(false);

            bool removed = await store.RemoveAsync(id).ConfigureAwait(false);

            Assert.That(removed, Is.True);
            Assert.That(store.TryGet(id), Is.Null);
        }

        [Test]
        public async Task SetAsyncReplacesExistingEntry()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("password", InMemorySecretStore.DefaultStoreType);
            await store.SetAsync(id, new byte[] { 0xAA }).ConfigureAwait(false);
            await store.SetAsync(id, new byte[] { 0xBB }).ConfigureAwait(false);

            using ISecret secret = store.TryGet(id);

            Assert.That(secret.Bytes[0], Is.EqualTo((byte)0xBB),
                "SetAsync on an existing entry must replace it.");
        }

        [Test]
        public async Task SetAsyncCopiesIncomingBytesSoMutatingOriginalDoesNotAffectStore()
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier("password", InMemorySecretStore.DefaultStoreType);
            byte[] source = [0x01, 0x02, 0x03];
            await store.SetAsync(id, source).ConfigureAwait(false);

            // Mutate the source after Set; the store must hold its own copy.
            source[0] = 0xFF;

            using ISecret secret = store.TryGet(id);
            Assert.That(secret.Bytes[0], Is.EqualTo((byte)0x01),
                "InMemorySecretStore must copy the incoming bytes on Set.");
        }

        [Test]
        public void TryGetThrowsArgumentNullExceptionForNullIdentifier()
        {
            var store = new InMemorySecretStore();

            Assert.That(() => store.TryGet(null), Throws.ArgumentNullException);
        }

        [Test]
        public void RegistryDispatchesToStoreByStoreType()
        {
            var inMem = new InMemorySecretStore("Custom");
            var registry = new SecretRegistry(inMem);
            var id = new SecretIdentifier("k", "Custom");
            byte[] bytes = [0x42];
            inMem.SetAsync(id, bytes).AsTask().Wait();

            using ISecret secret = registry.TryGet(id);

            Assert.That(secret, Is.Not.Null);
            Assert.That(secret.Bytes[0], Is.EqualTo((byte)0x42));
        }

        [Test]
        public void RegistryReturnsNullWhenStoreTypeNotRegistered()
        {
            var registry = new SecretRegistry();
            var id = new SecretIdentifier("k", "DoesNotExist");

            Assert.That(registry.TryGet(id), Is.Null);
        }

        [Test]
        public async Task RegistryGetAsyncReturnsNullWhenStoreTypeNotRegisteredAsync()
        {
            var registry = new SecretRegistry();
            var id = new SecretIdentifier("k", "DoesNotExist");

            ISecret secret = await registry.GetAsync(id).ConfigureAwait(false);

            Assert.That(secret, Is.Null);
        }

        [Test]
        public async Task RegistryRegisterStoreReplacesByStoreTypeAsync()
        {
            var first = new InMemorySecretStore("X");
            var second = new InMemorySecretStore("X");
            var id = new SecretIdentifier("k", "X");
            await first.SetAsync(id, new byte[] { 0xAA }).ConfigureAwait(false);
            await second.SetAsync(id, new byte[] { 0xBB }).ConfigureAwait(false);

            var registry = new SecretRegistry();
            registry.RegisterStore(first);
            registry.RegisterStore(second);

            using ISecret secret = registry.TryGet(id);

            Assert.That(secret.Bytes[0], Is.EqualTo((byte)0xBB),
                "RegisterStore with same StoreType must replace the previous registration.");
        }

        [Test]
        public void IdentifierIsValueEqual()
        {
            var a = new SecretIdentifier("k", "X", "path");
            var b = new SecretIdentifier("k", "X", "path");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }
    }
}
