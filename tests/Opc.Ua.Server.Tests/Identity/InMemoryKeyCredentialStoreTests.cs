// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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

            Server.KeyCredential result = (await store.GetAsync("unknown", CancellationToken.None)
                .ConfigureAwait(false))!;

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task UpdateAsyncStoresSecretAndExpirationAndSubject()
        {
            using var store = new InMemoryKeyCredentialStore();
            var expiration = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string[] scopes = ["ua.read", "ua.write"];

            await store.UpdateAsync(
                    CredentialId,
                    new Server.KeyCredential(s_secret, expiration, subject: null, scopes: scopes),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Server.KeyCredential stored = (await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false))!;

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

            Server.KeyCredential stored = (await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false))!;

            Assert.That(stored!.Secret, Is.EqualTo(replacement));
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

            Server.KeyCredential after = (await store.GetAsync(CredentialId, CancellationToken.None)
                .ConfigureAwait(false))!;
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
        public async Task GetAsyncRejectsEmptyCredentialId()
        {
            using var store = new InMemoryKeyCredentialStore();
            ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
                async () => await store.GetAsync(string.Empty, CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("credentialId"));
        }

        [Test]
        public async Task UpdateAsyncRejectsNullCredential()
        {
            using var store = new InMemoryKeyCredentialStore();
            ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await store.UpdateAsync(CredentialId, null!, CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.ParamName, Is.EqualTo("credential"));
        }

        [Test]
        public async Task OperationsAfterDisposeThrowObjectDisposedException()
        {
            var store = new InMemoryKeyCredentialStore();
            store.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await store.GetAsync(CredentialId, CancellationToken.None).ConfigureAwait(false));
        }
    }
}
