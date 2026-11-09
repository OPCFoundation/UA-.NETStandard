/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests issuance storage and its single-adapter completion contract, not certificate or key validity.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class CertificateRequestIssuanceStoreTests
    {
        /// <summary>
        /// Hosted receipts cannot invoke issuance or access storage until their GDS binds the actual table.
        /// </summary>
        [Test]
        public async Task HostedReceiptsRequireNamespaceBindingBeforeIssuanceOrRemoval()
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            using var store = new SecretStoreCertificateRequestIssuanceStore(secrets.Object, "gds");
            NodeId application = new("application", 1);
            NodeId request = new("request", 1);

            ServiceResultException? creation = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.GetOrCreateAsync(application, request,
                    _ => throw new AssertionException("An unbound store cannot issue a certificate."))
                    .ConfigureAwait(false));
            ServiceResultException? removal = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.RemoveAsync(application, request).ConfigureAwait(false));

            Assert.That(creation!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(removal!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            secrets.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A hosted adapter uses stable namespace URIs and rejects rebinding without changing retained receipts.
        /// </summary>
        [Test]
        public async Task HostedReceiptsBindOnceAndRestoreAcrossNamespaceReordering()
        {
            var secrets = new InMemorySecretStore();
            CertificateRequestIssuance original = Issuance(3);
            using (var store = new SecretStoreCertificateRequestIssuanceStore(secrets, "gds"))
            {
                NamespaceTable namespaces = Namespaces(false);
                store.BindNamespaceTable(namespaces);
                store.BindNamespaceTable(namespaces);
                Assert.Throws<InvalidOperationException>(() => store.BindNamespaceTable(Namespaces(true)));
                Assert.Throws<ArgumentNullException>(() => store.BindNamespaceTable(null!));
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                    _ => new ValueTask<CertificateRequestIssuance>(original)).ConfigureAwait(false);
            }
            using var restarted = new SecretStoreCertificateRequestIssuanceStore(secrets, "gds");
            restarted.BindNamespaceTable(Namespaces(true));

            CertificateRequestIssuance restored = await restarted.GetOrCreateAsync(
                new NodeId("app", 2), new NodeId("request", 2),
                _ => throw new AssertionException("Reordered namespaces must retain the original issuance."))
                .ConfigureAwait(false);

            AssertIssuance(restored, original);
        }

        [Test]
        public async Task RecreatedAdapterUsesNamespaceUrisAndRetainsExactIssuanceWithoutCallingFactory()
        {
            var secrets = new InMemorySecretStore();
            CertificateRequestIssuance original = Issuance(1);
            using (var first = new SecretStoreCertificateRequestIssuanceStore(secrets, Namespaces(false), "gds"))
            {
                await first.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                    _ => new ValueTask<CertificateRequestIssuance>(original)).ConfigureAwait(false);
            }
            using var restarted = new SecretStoreCertificateRequestIssuanceStore(secrets, Namespaces(true), "gds");

            CertificateRequestIssuance restored = await restarted.GetOrCreateAsync(
                new NodeId("app", 2), new NodeId("request", 2),
                _ => throw new AssertionException("A retained receipt must not regenerate issuance."))
                .ConfigureAwait(false);

            AssertIssuance(restored, original);
            Assert.That(restored, Is.Not.SameAs(original));
            Assert.That(await restarted.RemoveAsync(new NodeId("app", 2), new NodeId("request", 2))
                .ConfigureAwait(false), Is.True);
            Assert.That(await restarted.RemoveAsync(new NodeId("app", 2), new NodeId("request", 2))
                .ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task ConcurrentRetryWithinOneAdapterDoesNotInvokeACompetingFactory()
        {
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                new InMemorySecretStore(), Namespaces(false), "gds");
            var created = new TaskCompletionSource<CertificateRequestIssuance>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            Task<CertificateRequestIssuance> first = store.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1), _ =>
                {
                    calls++;
                    return new ValueTask<CertificateRequestIssuance>(created.Task);
                }).AsTask();
            Task<CertificateRequestIssuance> retry = store.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1), _ =>
                {
                    calls++;
                    return new ValueTask<CertificateRequestIssuance>(Issuance(2));
                }).AsTask();
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(retry.IsCompleted, Is.False);
            created.SetResult(Issuance(1));

            CertificateRequestIssuance[] results = await Task.WhenAll(first, retry).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            AssertIssuance(results[0], Issuance(1));
            AssertIssuance(results[1], Issuance(1));
        }

        [Test]
        public async Task DatabaseScopeApplicationAndRequestEachSeparateIssuance()
        {
            var secrets = new InMemorySecretStore();
            using var first = new SecretStoreCertificateRequestIssuanceStore(secrets, Namespaces(false), "gds-a");
            using var otherDatabase = new SecretStoreCertificateRequestIssuanceStore(
                secrets, Namespaces(false), "gds-b");
            NodeId application = new("app", 1);
            NodeId request = new("request", 1);
            await first.GetOrCreateAsync(application, request,
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(1))).ConfigureAwait(false);

            AssertIssuance(await first.GetOrCreateAsync(new NodeId("other", 1), request,
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(2))).ConfigureAwait(false), Issuance(2));
            AssertIssuance(await first.GetOrCreateAsync(application, new NodeId("other", 1),
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(3))).ConfigureAwait(false), Issuance(3));
            AssertIssuance(await otherDatabase.GetOrCreateAsync(application, request,
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(4))).ConfigureAwait(false), Issuance(4));
        }

        [Test]
        public async Task PublicationFailureDoesNotReturnIssuanceOrPreventAStillUnpublishedRetry()
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            var failure = new IOException("Secret publication failed.");
            ReadOnlyMemory<byte> temporary = default;
            secrets.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Callback<SecretIdentifier, ReadOnlyMemory<byte>, CancellationToken>((_, bytes, _) => temporary = bytes)
                .Throws(failure);
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds");
            int calls = 0;
            IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1), _ =>
                {
                    calls++;
                    return new ValueTask<CertificateRequestIssuance>(Issuance(1));
                }).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(temporary.IsEmpty, Is.False);
            Assert.That(temporary.ToArray(), Is.All.EqualTo((byte)0),
                "The adapter must erase its serialized private-key staging buffer after a failed publication.");

            secrets.Setup(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            CertificateRequestIssuance retry = await store.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1), _ =>
                {
                    calls++;
                    return new ValueTask<CertificateRequestIssuance>(Issuance(2));
                }).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2),
                "No pending lifecycle publication can have occurred when issuance publication failed.");
            AssertIssuance(retry, Issuance(2));
        }

        [Test]
        public async Task PublicationThatCommitsBeforeReportingFailureIsReusedWithoutRegeneration()
        {
            var backing = new InMemorySecretStore();
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns(backing.StoreType);
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, CancellationToken token) => backing.GetAsync(id, token));
            var failure = new IOException("The store committed, but its response was lost.");
            secrets.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken token) =>
                {
                    await backing.SetAsync(id, bytes, token).ConfigureAwait(false);
                    throw failure;
                });
            using (var first = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds"))
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                        _ => new ValueTask<CertificateRequestIssuance>(Issuance(1))).ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
            }
            using var restarted = new SecretStoreCertificateRequestIssuanceStore(backing, Namespaces(false), "gds");

            CertificateRequestIssuance restored = await restarted.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1),
                _ => throw new AssertionException("Published issuance must survive an uncertain store response."))
                .ConfigureAwait(false);

            AssertIssuance(restored, Issuance(1));
        }

        [Test]
        public async Task ReadFailureCannotBeMistakenForAnAbsentReceipt()
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            var failure = new IOException("Receipt store unavailable.");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds");

            IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                    _ => throw new AssertionException("A read failure must not regenerate issuance."))
                    .ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task CreationFailureReleasesTheClaimWithoutPublishingAnIssuance()
        {
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                new InMemorySecretStore(), Namespaces(false), "gds");
            NodeId application = new("app", 1);
            NodeId request = new("request", 1);
            var failure = new InvalidOperationException("Issuance engine failed.");
            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.GetOrCreateAsync(application, request, _ => throw failure).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));

            CertificateRequestIssuance recovered = await store.GetOrCreateAsync(application, request,
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(2))).ConfigureAwait(false);

            AssertIssuance(recovered, Issuance(2));
        }

        [Test]
        public async Task CancellationAfterCreationDoesNotPublishOrPoisonTheNextClaim()
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds");
            using var cancellation = new CancellationTokenSource();
            NodeId application = new("app", 1);
            NodeId request = new("request", 1);

            await Assert.ThatAsync(async () => await store.GetOrCreateAsync(application, request,
                async _ =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return Issuance(1);
                }, cancellation.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());

            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            secrets.Setup(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            AssertIssuance(await store.GetOrCreateAsync(application, request,
                _ => new ValueTask<CertificateRequestIssuance>(Issuance(2))).ConfigureAwait(false), Issuance(2));
        }

        [Test]
        public void IssuanceDetachesCertificateKeyAndIssuerInputs()
        {
            byte[] certificate = [1];
            byte[] key = [2];
            byte[] issuer = [3];
            ByteString[] issuers = [ByteString.From(issuer)];
            var issuance = new CertificateRequestIssuance(
                "group", "type", ByteString.From(certificate), ByteString.From(key), issuers.ToArrayOf());

            certificate[0] = 4;
            key[0] = 5;
            issuer[0] = 6;
            issuers[0] = default;

            Assert.That(issuance.Certificate, Is.EqualTo(ByteString.From(new byte[] { 1 })));
            Assert.That(issuance.PrivateKey, Is.EqualTo(ByteString.From(new byte[] { 2 })));
            Assert.That(issuance.IssuerCertificates.Count, Is.EqualTo(1));
            Assert.That(issuance.IssuerCertificates[0], Is.EqualTo(ByteString.From(new byte[] { 3 })));
            var signing = new CertificateRequestIssuance("group", "type", issuance.Certificate, default, []);
            Assert.That(signing.PrivateKey.IsEmpty, Is.True);
            Assert.That(signing.IssuerCertificates.IsEmpty, Is.True);
        }

        [Test]
        public void IssuanceRejectsMissingGroupTypeCertificateAndIssuerMaterial()
        {
            ByteString certificate = ByteString.From(new byte[] { 1 });
            Assert.Throws<ArgumentNullException>(() =>
                new CertificateRequestIssuance(null!, "type", certificate, default, []));
            Assert.Throws<ArgumentException>(() =>
                new CertificateRequestIssuance(string.Empty, "type", certificate, default, []));
            Assert.Throws<ArgumentNullException>(() =>
                new CertificateRequestIssuance("group", null!, certificate, default, []));
            Assert.Throws<ArgumentException>(() =>
                new CertificateRequestIssuance("group", string.Empty, certificate, default, []));
            Assert.Throws<ArgumentException>(() =>
                new CertificateRequestIssuance("group", "type", default, default, []));
            Assert.Throws<ArgumentException>(() =>
                new CertificateRequestIssuance("group", "type", certificate, default, [ByteString.Empty]));
        }

        [TestCase("{}")]
        [TestCase("{\"version\":2}")]
        [TestCase("{\"version\":1,\"group\":\"g\",\"type\":\"t\",\"certificate\":\"%%%\",\"privateKey\":\"\"," +
            "\"issuers\":[]}")]
        [TestCase("{\"version\":1,\"group\":\"g\",\"type\":\"t\",\"certificate\":\"AQ==\",\"privateKey\":\"\"," +
            "\"issuers\":[],\"unknown\":true}")]
        [TestCase("{\"version\":1,\"group\":\"g\",\"type\":\"t\",\"certificate\":\"AQ==\",\"privateKey\":\"\"," +
            "\"issuers\":[],\"group\":\"other\"}")]
        public async Task CorruptReceiptNeverPermitsRegenerationAndDisposesSecret(string json)
        {
            using var secret = new TrackingSecret(Encoding.UTF8.GetBytes(json));
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds");

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                    _ => throw new AssertionException("Corrupt storage must not regenerate issuance."))
                    .ConfigureAwait(false));

            Assert.That(secret.Disposed, Is.True);
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task ReceiptSizeBoundaryRejectsOnlyDocumentsAboveSixteenMebibytes(int delta)
        {
            const int maximumBytes = 16 * 1024 * 1024;
            const string json = "{\"version\":1,\"group\":\"g\",\"type\":\"t\",\"certificate\":\"AQ==\"," +
                "\"privateKey\":\"\",\"issuers\":[]}";
            byte[] encoded = Encoding.UTF8.GetBytes(json.PadRight(maximumBytes + delta));
            using var secret = new TrackingSecret(encoded);
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>(secret));
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                secrets.Object, Namespaces(false), "gds");

            if (delta > 0)
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                        _ => throw new AssertionException("An oversized receipt must not regenerate issuance."))
                        .ConfigureAwait(false));
            }
            else
            {
                CertificateRequestIssuance receipt = await store.GetOrCreateAsync(
                    new NodeId("app", 1), new NodeId("request", 1),
                    _ => throw new AssertionException("An existing receipt must not regenerate issuance."))
                    .ConfigureAwait(false);
                Assert.That(receipt.CertificateGroupId, Is.EqualTo("g"));
                Assert.That(receipt.CertificateTypeId, Is.EqualTo("t"));
                Assert.That(receipt.Certificate, Is.EqualTo(ByteString.From(new byte[] { 1 })));
                Assert.That(receipt.PrivateKey.IsEmpty, Is.True);
                Assert.That(receipt.IssuerCertificates.IsEmpty, Is.True);
            }
            Assert.That(secret.Disposed, Is.True);
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task CancellationWhileWaitingDoesNotInvokeFactoryOrConsumeAnotherCompletion()
        {
            using var store = new SecretStoreCertificateRequestIssuanceStore(
                new InMemorySecretStore(), Namespaces(false), "gds");
            var created = new TaskCompletionSource<CertificateRequestIssuance>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<CertificateRequestIssuance> first = store.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1),
                _ => new ValueTask<CertificateRequestIssuance>(created.Task)).AsTask();
            using var cancellation = new CancellationTokenSource();
            Task<CertificateRequestIssuance> waiting = store.GetOrCreateAsync(
                new NodeId("app", 1), new NodeId("request", 1),
                _ => throw new AssertionException("A canceled retry must not generate issuance."),
                cancellation.Token).AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await waiting.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            created.SetResult(Issuance(1));

            AssertIssuance(await first.ConfigureAwait(false), Issuance(1));
            AssertIssuance(await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1),
                _ => throw new AssertionException("The successful writer must have retained its receipt."))
                .ConfigureAwait(false), Issuance(1));
        }

        [Test]
        public async Task ConstructorAndRequestIdentifiersRequireExplicitValidInputs()
        {
            var secrets = new InMemorySecretStore();
            NamespaceTable namespaces = Namespaces(false);
            Assert.Throws<ArgumentNullException>(() =>
                new SecretStoreCertificateRequestIssuanceStore(null!, namespaces, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                new SecretStoreCertificateRequestIssuanceStore(secrets, null!, "gds"));
            Assert.Throws<ArgumentNullException>(() =>
                new SecretStoreCertificateRequestIssuanceStore(secrets, namespaces, null!));
            Assert.Throws<ArgumentException>(() =>
                new SecretStoreCertificateRequestIssuanceStore(secrets, namespaces, string.Empty));
            using var store = new SecretStoreCertificateRequestIssuanceStore(secrets, namespaces, "gds");
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 1), null!)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.GetOrCreateAsync(NodeId.Null, new NodeId("request", 1),
                    _ => new ValueTask<CertificateRequestIssuance>(Issuance(1))).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.GetOrCreateAsync(new NodeId("app", 1), new NodeId("request", 99),
                    _ => new ValueTask<CertificateRequestIssuance>(Issuance(1))).ConfigureAwait(false));
        }

        private static CertificateRequestIssuance Issuance(byte value)
        {
            return new CertificateRequestIssuance(
                "group", "type", ByteString.From(new[] { value }), ByteString.From(new[] { value, (byte)0 }),
                [ByteString.From(new[] { value, (byte)1 })]);
        }

        private static void AssertIssuance(CertificateRequestIssuance actual, CertificateRequestIssuance expected)
        {
            Assert.That(actual.CertificateGroupId, Is.EqualTo(expected.CertificateGroupId));
            Assert.That(actual.CertificateTypeId, Is.EqualTo(expected.CertificateTypeId));
            Assert.That(actual.Certificate, Is.EqualTo(expected.Certificate));
            Assert.That(actual.PrivateKey, Is.EqualTo(expected.PrivateKey));
            Assert.That(actual.IssuerCertificates.ToArray(), Is.EqualTo(expected.IssuerCertificates.ToArray()));
        }

        private static NamespaceTable Namespaces(bool reorder)
        {
            var namespaces = new NamespaceTable();
            if (reorder)
            {
                namespaces.GetIndexOrAppend("urn:test:other");
            }
            namespaces.GetIndexOrAppend("urn:test:issuance");
            return namespaces;
        }

        /// <summary>
        /// Records the lifetime of a returned secret without mocking its span-valued property.
        /// </summary>
        private sealed class TrackingSecret(byte[] bytes) : ISecret
        {
            /// <inheritdoc/>
            public ReadOnlySpan<byte> Bytes => bytes;

            /// <summary>
            /// Gets whether the consumer has released the secret.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
