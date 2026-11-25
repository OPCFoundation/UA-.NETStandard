// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

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
            SksSecurityGroup expected = CreateGroup("group-a") with { CurrentKeyIndex = 1 };

            await keyStore.SaveSecurityGroupAsync(expected).ConfigureAwait(false);
            SksSecurityGroup actual = (await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false))!;

            Assert.That(actual, Is.Not.Null);
            AssertGroupsEqual(actual, expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OverlappingRolloverUsesTheCommittedKeysAndAuthorizationAsync(bool replaceAuthorization)
        {
            using var payload = new InMemorySharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CancellationToken ct = timeout.Token;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayedPayload = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            delayedPayload.Setup(store => store.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken token) => payload.TryGetAsync(key, token));
            delayedPayload.Setup(store => store.SetAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString value, CancellationToken token) =>
                    PauseWriteAsync(key, value, token));

            var deployment = Guid.NewGuid();
            var epoch = Guid.NewGuid();
            var shared = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            var delayed = new FreshnessProtectedKeyValueStore(
                delayedPayload.Object, authority, protector, deployment, epoch);
            await shared.EnrollAsync(ct).ConfigureAwait(false);
            var clock = new FakeTimeProvider();
            var first = new InMemoryPubSubKeyServiceServer(clock, keyStore: CreateKeyStore(delayed, protector));
            var second = new InMemoryPubSubKeyServiceServer(clock, keyStore: CreateKeyStore(shared, protector));
            await second.AddSecurityGroupAsync(CreateUnseededGroup("publisher"), ct).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMinutes(16));
            var request = new SksKeyRequest("rollover", 0, 2);
            Task<SksKeyResponse> firstCall = first.GetSecurityKeysAsync(
                "publisher", request, cancellationToken: ct).AsTask();
            SksKeyResponse committed;
            try
            {
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                string caller = "publisher";
                if (replaceAuthorization)
                {
                    await second.RemoveSecurityGroupAsync("rollover", ct).ConfigureAwait(false);
                    caller = "replacement";
                    await second.AddSecurityGroupAsync(CreateUnseededGroup(caller), ct).ConfigureAwait(false);
                }
                committed = await second.GetSecurityKeysAsync(caller, request, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            if (replaceAuthorization)
            {
                OpcUaSksException? denied = await Assert.ThrowsAsync<OpcUaSksException>(
                    async () => await firstCall.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(denied!.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                return;
            }
            SksKeyResponse retried = await firstCall.ConfigureAwait(false);

            Assert.That(committed.FirstTokenId, Is.EqualTo(4u));
            Assert.That(retried.FirstTokenId, Is.EqualTo(committed.FirstTokenId));
            Assert.That(retried.Keys.Count, Is.EqualTo(2));
            Assert.That(retried.Keys[0], Is.EqualTo(committed.Keys[0]));
            Assert.That(retried.Keys[1], Is.EqualTo(committed.Keys[1]));

            async ValueTask PauseWriteAsync(string key, ByteString value, CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                await payload.SetAsync(key, value, token).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReplicasReadCurrentGenerationAndAuthorizationAfterAdministrationAsync()
        {
            using var payload = new InMemorySharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var deployment = Guid.NewGuid();
            var epoch = Guid.NewGuid();
            var shared = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            var peer = new FreshnessProtectedKeyValueStore(payload, authority, protector, deployment, epoch);
            await shared.EnrollAsync().ConfigureAwait(false);
            var clock = new FakeTimeProvider();
            var first = new InMemoryPubSubKeyServiceServer(clock, keyStore: CreateKeyStore(shared, protector));
            var second = new InMemoryPubSubKeyServiceServer(clock, keyStore: CreateKeyStore(peer, protector));
            var request = new SksKeyRequest("rollover", 0, 2);

            await first.AddSecurityGroupAsync(CreateUnseededGroup("first")).ConfigureAwait(false);
            OpcUaSksException? duplicate = await Assert.ThrowsAsync<OpcUaSksException>(
                async () => await second.AddSecurityGroupAsync(CreateUnseededGroup("second")).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(duplicate!.Status.Code, Is.EqualTo(StatusCodes.BadAlreadyExists));
            Assert.That((await second.GetSecurityGroupIdsAsync().ConfigureAwait(false)).ToArray(),
                Contains.Item("rollover"));

            SksKeyResponse before = await first.GetSecurityKeysAsync("first", request).ConfigureAwait(false);
            await second.ForceKeyRotationAsync("rollover").ConfigureAwait(false);
            SksKeyResponse rotated = await first.GetSecurityKeysAsync("first", request).ConfigureAwait(false);
            Assert.That(rotated.FirstTokenId, Is.EqualTo(before.FirstTokenId + 1));
            Assert.That(rotated.Keys[0], Is.EqualTo(before.Keys[1]));
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var handler = new SksMethodHandler(second, telemetry);
            var context = new SystemContext(telemetry) { UserId = "first" };
            ArrayOf<Variant> inputs = [Variant.From("rollover"), Variant.From(0u), Variant.From(2u)];
            var outputs = new List<Variant>();
            ServiceResult result = await handler.HandleGetSecurityKeysAsync(
                context, ObjectIds.PublishSubscribe, inputs, outputs).ConfigureAwait(false);
            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[1].TryGetValue(out uint currentTokenId), Is.True);
            Assert.That(currentTokenId, Is.EqualTo(rotated.FirstTokenId));

            await first.InvalidateKeysAsync("rollover").ConfigureAwait(false);
            SksKeyResponse replaced = await second.GetSecurityKeysAsync("first", request).ConfigureAwait(false);
            Assert.That(replaced.FirstTokenId, Is.EqualTo(5u));

            await first.RemoveSecurityGroupAsync("rollover").ConfigureAwait(false);
            await first.AddSecurityGroupAsync(CreateUnseededGroup("second")).ConfigureAwait(false);
            OpcUaSksException? denied = await Assert.ThrowsAsync<OpcUaSksException>(
                async () => await second.GetSecurityKeysAsync("first", request).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(denied!.Status.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That((await second.GetSecurityKeysAsync("second", request).ConfigureAwait(false)).Keys.Count,
                Is.EqualTo(2));
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
            Assert.That(new InMemoryPubSubKeyServiceServer(keyStore: keyStore).SecurityGroupIds.ToArray(),
                Is.EquivalentTo(s_groupIds));
        }

        [Test]
        public async Task RemoveSecurityGroupDeletesGroupAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            await keyStore.SaveSecurityGroupAsync(CreateGroup("group-a")).ConfigureAwait(false);

            bool removed = await keyStore.RemoveSecurityGroupAsync("group-a").ConfigureAwait(false);
            SksSecurityGroup? actual = await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false);

            Assert.That(removed, Is.True);
            Assert.That(actual, Is.Null);
        }

        [Test]
        public async Task GetSecurityGroupReturnsNullForUnknownGroupAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);

            SksSecurityGroup? actual = await keyStore.GetSecurityGroupAsync("missing").ConfigureAwait(false);

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
            bool unprotected = protector.TryUnprotect(
                RecordProtectionContext.Create(
                    "pubsub-security-group", PubSubRedundancyStoreKeys.SecurityKeyPrefix + "group-a"),
                stored, out ByteString plaintext);

            Assert.That(found, Is.True);
            Assert.That(unprotected, Is.True);
            Assert.That(stored.ToArray(), Is.Not.EqualTo(plaintext.ToArray()));
        }

        [Test]
        public async Task GetSecurityGroupReturnsNullWhenStoredRecordCannotBeUnprotectedAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);

            await sharedStore.SetAsync(
                PubSubRedundancyStoreKeys.SecurityKeyPrefix + "group-a",
                ByteString.From(new byte[] { 0x01, 0x02, 0x03, 0x04 })).ConfigureAwait(false);

            SksSecurityGroup? group = await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false);

            Assert.That(group, Is.Null);
        }

        [Test]
        public async Task SaveSecurityGroupRejectsStaleFencingTokenAsync()
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            SksSecurityGroup original = CreateGroup("group-a");
            SksSecurityGroup replacement = CreateGroup("group-a", maxFutureKeyCount: 7);

            await keyStore.SaveSecurityGroupAsync(original, 10).ConfigureAwait(false);

            Assert.That(
                async () => await keyStore.SaveSecurityGroupAsync(
                    replacement,
                    9).ConfigureAwait(false),
                Throws.InvalidOperationException);

            SksSecurityGroup actual = (await keyStore.GetSecurityGroupAsync("group-a").ConfigureAwait(false))!;
            Assert.That(actual, Is.Not.Null);
            AssertGroupsEqual(actual, original);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TransplantedSecurityGroupFailsClosedAsync(bool fenced)
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            SksSecurityGroup expected = CreateGroup("source");
            if (fenced)
            {
                await keyStore.SaveSecurityGroupAsync(expected, 10).ConfigureAwait(false);
            }
            else
            {
                await keyStore.SaveSecurityGroupAsync(expected).ConfigureAwait(false);
            }
            (bool found, ByteString record) = await sharedStore.TryGetAsync(
                PubSubRedundancyStoreKeys.SecurityKeyPrefix + "source").ConfigureAwait(false);
            Assert.That(found, Is.True);
            await sharedStore.SetAsync(PubSubRedundancyStoreKeys.SecurityKeyPrefix + "target", record)
                .ConfigureAwait(false);

            Assert.That(await keyStore.GetSecurityGroupAsync("target").ConfigureAwait(false), Is.Null);
            SksSecurityGroup retained = (await keyStore.GetSecurityGroupAsync("source").ConfigureAwait(false))!;
            Assert.That(retained, Is.Not.Null);
            AssertGroupsEqual(retained, expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityGroupRejectsEmptyContextOrTamperingAsync(bool emptyContext)
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            SharedStorePubSubSecurityKeyStore keyStore = CreateKeyStore(sharedStore, protector);
            await keyStore.SaveSecurityGroupAsync(CreateGroup("source")).ConfigureAwait(false);
            const string key = PubSubRedundancyStoreKeys.SecurityKeyPrefix + "source";
            (_, ByteString record) = await sharedStore.TryGetAsync(key).ConfigureAwait(false);
            ByteString invalid;
            if (emptyContext)
            {
                Assert.That(protector.TryUnprotect(
                    RecordProtectionContext.Create("pubsub-security-group", key), record, out ByteString plaintext),
                    Is.True);
                invalid = protector.Protect(default, plaintext);
            }
            else
            {
                byte[] tampered = record.ToArray();
                tampered[^1] ^= 1;
                invalid = ByteString.From(tampered);
            }
            await sharedStore.SetAsync(key, invalid).ConfigureAwait(false);

            Assert.That(await keyStore.GetSecurityGroupAsync("source").ConfigureAwait(false), Is.Null);
            (_, ByteString retained) = await sharedStore.TryGetAsync(key).ConfigureAwait(false);
            Assert.That(retained, Is.EqualTo(invalid));
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

        private static SksSecurityGroup CreateGroup(string securityGroupId, int maxFutureKeyCount = 2)
        {
            return new SksSecurityGroup(
                securityGroupId,
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(5),
                maxFutureKeyCount,
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

        private static SksSecurityGroup CreateUnseededGroup(string caller)
        {
            return new SksSecurityGroup(
                "rollover", PubSubSecurityPolicyUri.PubSubAes128Ctr, TimeSpan.FromMinutes(5), 2, 1, default, [caller]);
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
            Assert.That(actual.CurrentKeyIndex, Is.EqualTo(expected.CurrentKeyIndex));
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
