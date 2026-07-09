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
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the shared-store PubSub SKS key store.
    /// </summary>
    [TestFixture]
    public class SharedStorePubSubSecurityKeyStoreTests
    {
        [Test]
        public async Task SaveThenGetRoundTripsEverySecurityGroupFieldAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            SksSecurityGroup expected = CreateGroup("group-a");

            await keyStore.SaveSecurityGroupAsync(expected).ConfigureAwait(false);
            SksSecurityGroup actual = await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false);

            Assert.That(actual, Is.Not.Null);
            AssertGroupsEqual(actual, expected);
        }

        [Test]
        public async Task GetSecurityGroupIdsReflectsSavedGroupsAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);

            await keyStore.SaveSecurityGroupAsync(CreateGroup("group-a")).ConfigureAwait(false);
            await keyStore.SaveSecurityGroupAsync(CreateGroup("group-b")).ConfigureAwait(false);
            ArrayOf<string> groupIds = await keyStore.GetSecurityGroupIdsAsync().ConfigureAwait(false);

            Assert.That(groupIds.ToArray(), Is.EquivalentTo(s_groupIds));
        }

        [Test]
        public async Task RemoveSecurityGroupDeletesGroupAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            await keyStore.SaveSecurityGroupAsync(CreateGroup("group-a")).ConfigureAwait(false);

            bool removed = await keyStore.RemoveSecurityGroupAsync("group-a").ConfigureAwait(false);
            SksSecurityGroup actual = await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false);

            Assert.That(removed, Is.True);
            Assert.That(actual, Is.Null);
        }

        [Test]
        public async Task GetSecurityGroupReturnsNullForUnknownGroupAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);

            SksSecurityGroup actual = await keyStore.GetSecurityGroupAsync("missing").ConfigureAwait(false);

            Assert.That(actual, Is.Null);
        }

        [Test]
        public async Task SaveSecurityGroupStoresProtectedBytesAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);

            await keyStore.SaveSecurityGroupAsync(CreateGroup("group-a")).ConfigureAwait(false);
            (bool found, ByteString stored) = await sharedStore
                .TryGetAsync(PubSubRedundancyStoreKeys.SecurityKeyPrefix + "group-a")
                .ConfigureAwait(false);
            bool unprotected = protector.TryUnprotect(stored, out ByteString plaintext);

            Assert.That(found, Is.True);
            Assert.That(unprotected, Is.True);
            Assert.That(stored.ToArray(), Is.Not.EqualTo(plaintext.ToArray()));
        }

        private static SharedStorePubSubSecurityKeyStore CreateKeyStore(
            ISharedKeyValueStore sharedStore,
            IRecordProtector protector)
        {
            return new SharedStorePubSubSecurityKeyStore(
                sharedStore,
                protector,
                ServiceMessageContext.Create(null));
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(ii + 1);
            }

            return new AesCbcHmacRecordProtector(key);
        }

        private static SksSecurityGroup CreateGroup(string securityGroupId)
        {
            return new SksSecurityGroup(
                securityGroupId,
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                2,
                1,
                new ArrayOf<PubSubSecurityKey>(
                new[]
                {
                    CreateSecurityKey(1, 10),
                    CreateSecurityKey(2, 20)
                }),
                new ArrayOf<string>(s_authorizedCallers),
                new ArrayOf<RolePermissionType>(
                new[]
                {
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)PermissionType.Call
                    }
                }));
        }

        private static PubSubSecurityKey CreateSecurityKey(uint tokenId, byte seed)
        {
            return new PubSubSecurityKey(
                tokenId,
                CreateByteString(seed, 16),
                CreateByteString((byte)(seed + 1), 16),
                CreateByteString((byte)(seed + 2), 4),
                new DateTimeUtc(2026, 1, (int)tokenId, 0, 0, 0),
                TimeSpan.FromMinutes(5));
        }

        private static ByteString CreateByteString(byte seed, int length)
        {
            byte[] bytes = new byte[length];
            for (int ii = 0; ii < bytes.Length; ii++)
            {
                bytes[ii] = (byte)(seed + ii);
            }

            return new ByteString(bytes);
        }

        private static void AssertGroupsEqual(SksSecurityGroup actual, SksSecurityGroup expected)
        {
            Assert.That(actual.SecurityGroupId, Is.EqualTo(expected.SecurityGroupId));
            Assert.That(actual.SecurityPolicyUri, Is.EqualTo(expected.SecurityPolicyUri));
            Assert.That(actual.KeyLifetime, Is.EqualTo(expected.KeyLifetime));
            Assert.That(actual.MaxFutureKeyCount, Is.EqualTo(expected.MaxFutureKeyCount));
            Assert.That(actual.MaxPastKeyCount, Is.EqualTo(expected.MaxPastKeyCount));
            AssertSecurityKeysEqual(actual.Keys, expected.Keys);
            Assert.That(actual.AuthorizedCallerIdentities, Is.EqualTo(expected.AuthorizedCallerIdentities));
            Assert.That(actual.RolePermissions, Has.Count.EqualTo(expected.RolePermissions.Count));
            Assert.That(actual.RolePermissions[0], Is.EqualTo(expected.RolePermissions[0]));
        }

        private static void AssertSecurityKeysEqual(
            ArrayOf<PubSubSecurityKey> actual,
            ArrayOf<PubSubSecurityKey> expected)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int ii = 0; ii < expected.Count; ii++)
            {
                Assert.That(actual[ii].TokenId, Is.EqualTo(expected[ii].TokenId));
                Assert.That(actual[ii].SigningKey, Is.EqualTo(expected[ii].SigningKey));
                Assert.That(actual[ii].EncryptingKey, Is.EqualTo(expected[ii].EncryptingKey));
                Assert.That(actual[ii].KeyNonce, Is.EqualTo(expected[ii].KeyNonce));
                Assert.That(actual[ii].IssuedAt, Is.EqualTo(expected[ii].IssuedAt));
                Assert.That(actual[ii].Lifetime, Is.EqualTo(expected[ii].Lifetime));
            }
        }

        private static readonly string[] s_groupIds = ["group-a", "group-b"];
        private static readonly string[] s_authorizedCallers = ["publisher-a", "subscriber-b"];
    }
}
