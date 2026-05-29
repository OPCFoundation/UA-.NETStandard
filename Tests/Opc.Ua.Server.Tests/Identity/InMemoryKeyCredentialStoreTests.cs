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

#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Category("KeyCredential")]
    public class InMemoryKeyCredentialStoreTests
    {
        private const string CredentialId = "credential-store-1";
        private static readonly byte[] s_secret = [1, 2, 3, 4, 5];
        private static readonly string[] s_orderedIds = ["aaa", "bbb", "ccc"];

        [Test]
        public async Task GetAsyncReturnsNullForUnknownCredentialId()
        {
            using var store = new InMemoryKeyCredentialStore();

            Server.KeyCredential result = await store.GetAsync("unknown", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task UpdateAsyncStoresSecretAndExpirationAndSubject()
        {
            using var store = new InMemoryKeyCredentialStore();
            DateTime expiration = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string[] scopes = ["ua.read", "ua.write"];

            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(s_secret, expiration, subject: null, scopes: scopes),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Server.KeyCredential stored = await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.Secret, Is.EqualTo(s_secret));
            Assert.That(stored.Expiration, Is.EqualTo(expiration));
            Assert.That(stored.Subject, Is.Empty);
            Assert.That(stored.Scopes, Is.EqualTo(scopes));
        }

        [Test]
        public async Task UpdateAsyncReplacesExistingCredentialMetadataAndSecret()
        {
            using var store = new InMemoryKeyCredentialStore();
            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(s_secret, DateTime.UtcNow.AddMinutes(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);

            byte[] replacement = [9, 9, 9];
            DateTime newExpiration = DateTime.UtcNow.AddHours(1);
            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(replacement, newExpiration),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Server.KeyCredential stored = await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(stored.Secret, Is.EqualTo(replacement));
            Assert.That(stored.Expiration, Is.EqualTo(newExpiration));
        }

        [Test]
        public async Task DeleteAsyncRemovesCredential()
        {
            using var store = new InMemoryKeyCredentialStore();
            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(s_secret, DateTime.UtcNow.AddMinutes(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);

            await store.DeleteAsync(CredentialId, CancellationToken.None).ConfigureAwait(false);

            Server.KeyCredential after = await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(after, Is.Null);

            IReadOnlyList<string> list = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(list, Is.Empty);
        }

        [Test]
        public async Task ListAsyncReturnsCredentialIdsInOrdinalOrder()
        {
            using var store = new InMemoryKeyCredentialStore();
            await store.UpdateAsync(
                    "ccc",
                    new Server.KeyCredential(s_secret, DateTime.UtcNow.AddHours(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store.UpdateAsync(
                    "aaa",
                    new Server.KeyCredential(s_secret, DateTime.UtcNow.AddHours(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await store.UpdateAsync(
                    "bbb",
                    new Server.KeyCredential(s_secret, DateTime.UtcNow.AddHours(1)),
                    CancellationToken.None)
                .ConfigureAwait(false);

            IReadOnlyList<string> ids = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(ids, Is.EqualTo(s_orderedIds));
        }

        [Test]
        public void GetAsyncRejectsEmptyCredentialId()
        {
            using var store = new InMemoryKeyCredentialStore();
            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(
                async () => await store.GetAsync(string.Empty, CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("credentialId"));
        }

        [Test]
        public void UpdateAsyncRejectsNullCredential()
        {
            using var store = new InMemoryKeyCredentialStore();
            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await store.UpdateAsync(CredentialId, null, CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("credential"));
        }

        [Test]
        public void OperationsAfterDisposeThrowObjectDisposedException()
        {
            var store = new InMemoryKeyCredentialStore();
            store.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await store.GetAsync(CredentialId, CancellationToken.None).ConfigureAwait(false));
        }
    }
}
