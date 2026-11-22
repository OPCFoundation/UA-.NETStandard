// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    [TestFixture]
    public sealed class SecretStorePullDcaNewKeyPairRecoveryProviderTests
    {
        [Test]
        public async Task ReceivedCheckpointSurvivesAdapterRecreationAndRequiresMatchingInstalledCertificate()
        {
            var store = new TestStore();
            PullDcaNewKeyPairRecoveryBinding dispatch = CreateBinding();
            var started = dispatch with { RequestId = new NodeId(20u, 1) };
            ByteString certificate = ByteString.From([1, 2, 3]);
            ByteString pfx = ByteString.From([4, 5, 6]);
            using (SecretStorePullDcaNewKeyPairRecoveryProvider before = store.Open())
            {
                await before.RecordDispatchAsync(dispatch).ConfigureAwait(false);
                await before.RecordStartedAsync(started).ConfigureAwait(false);
                await before.RecordReceivedAsync(started, certificate, [], pfx).ConfigureAwait(false);
            }

            using SecretStorePullDcaNewKeyPairRecoveryProvider after = store.Open();
            PullDcaNewKeyPairRecoveryAttempt? pending =
                await after.GetPendingAsync(dispatch.Scope).ConfigureAwait(false);
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending!.Binding.RequestId, Is.EqualTo(started.RequestId));
            Assert.That(pending.Binding.AttemptId, Is.EqualTo(started.AttemptId));
            Assert.That(pending.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
            Assert.That(pending.Certificate, Is.EqualTo(certificate));
            Assert.That(pending.PrivateKeyPfx, Is.EqualTo(pfx));
            PullDcaConnection.ClearPrivateKeyMaterial(pending.PrivateKeyPfx);

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.CompleteAsync(started, pfx).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.ResetReconciledDispatchAsync(dispatch).ConfigureAwait(false));
            await after.CompleteAsync(started, certificate).ConfigureAwait(false);
            Assert.That(await after.GetPendingAsync(dispatch.Scope).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task UnknownDispatchCannotBeOverwrittenOrResetWithAnotherBinding()
        {
            var store = new TestStore();
            PullDcaNewKeyPairRecoveryBinding dispatch = CreateBinding();
            var other = dispatch with { AttemptId = Guid.NewGuid() };
            using (SecretStorePullDcaNewKeyPairRecoveryProvider before = store.Open())
            {
                await before.RecordDispatchAsync(dispatch).ConfigureAwait(false);
            }

            using SecretStorePullDcaNewKeyPairRecoveryProvider after = store.Open();
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.RecordDispatchAsync(other).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.ResetReconciledDispatchAsync(other).ConfigureAwait(false));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.GetPendingAsync(dispatch.Scope with { RegistrarApplicationUri = "urn:other" })
                    .ConfigureAwait(false));
            Assert.That((await after.GetPendingAsync(dispatch.Scope).ConfigureAwait(false))?.Binding.AttemptId,
                Is.EqualTo(dispatch.AttemptId));
            await after.ResetReconciledDispatchAsync(dispatch).ConfigureAwait(false);
            await after.RecordDispatchAsync(other).ConfigureAwait(false);
            Assert.That((await after.GetPendingAsync(other.Scope).ConfigureAwait(false))?.Binding.AttemptId,
                Is.EqualTo(other.AttemptId));
        }

        [Test]
        public async Task AmbiguousDispatchWriteRemainsPendingAfterRecreation()
        {
            var store = new TestStore { FailAfterWrite = true };
            PullDcaNewKeyPairRecoveryBinding dispatch = CreateBinding();
            using (SecretStorePullDcaNewKeyPairRecoveryProvider before = store.Open())
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await before.RecordDispatchAsync(dispatch).ConfigureAwait(false));
            }

            using SecretStorePullDcaNewKeyPairRecoveryProvider after = store.Open();
            PullDcaNewKeyPairRecoveryAttempt? attempt =
                await after.GetPendingAsync(dispatch.Scope).ConfigureAwait(false);
            Assert.That(attempt?.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Dispatching));
            Assert.That(attempt?.Binding.AttemptId, Is.EqualTo(dispatch.AttemptId));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await after.RecordDispatchAsync(dispatch with { AttemptId = Guid.NewGuid() }).ConfigureAwait(false));
        }

        private static PullDcaNewKeyPairRecoveryBinding CreateBinding()
        {
            var registrar = new PullDcaRegistrar(
                new Uri("opc.tcp://localhost:4840"), new NodeId(1u, 1), new NodeId(2u, 1));
            var profile = new PullDcaCertificateProfile(new NodeId(3u, 1), new NodeId(4u, 1));
            var scope = new PullDcaNewKeyPairRecoveryScope(registrar, new NodeId(5u, 1), profile,
                ByteString.From([1, 2]), ByteString.From([3, 4]), "urn:registrar", [Namespaces.OpcUa, "urn:test"]);
            return new PullDcaNewKeyPairRecoveryBinding(scope, NodeId.Null, Guid.NewGuid());
        }

        private sealed class TestStore
        {
            public TestStore()
            {
                var store = new InMemorySecretStore();
                m_mock.SetupGet(value => value.StoreType).Returns(store.StoreType);
                m_mock.Setup(value => value.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                    .Returns(store.GetAsync);
                m_mock.Setup(value => value.SetAsync(
                        It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (SecretIdentifier id, ReadOnlyMemory<byte> data, CancellationToken ct) =>
                    {
                        await store.SetAsync(id, data, ct).ConfigureAwait(false);
                        if (FailAfterWrite)
                        {
                            throw new TimeoutException("The write response was lost.");
                        }
                    });
                m_mock.Setup(value => value.RemoveAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                    .Returns(store.RemoveAsync);
            }

            public bool FailAfterWrite { get; init; }

            public SecretStorePullDcaNewKeyPairRecoveryProvider Open()
            {
                return new SecretStorePullDcaNewKeyPairRecoveryProvider(
                    m_mock.Object, "test-slot", NUnitTelemetryContext.Create());
            }

            private readonly Mock<IProtectedPullDcaRecoveryStore> m_mock = new(MockBehavior.Strict);
        }
    }
}
