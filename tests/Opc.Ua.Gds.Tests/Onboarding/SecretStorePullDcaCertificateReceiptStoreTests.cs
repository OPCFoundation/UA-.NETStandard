/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies public signing-output retention, binding, and one-adapter serialization without physical storage.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class SecretStorePullDcaCertificateReceiptStoreTests
    {
        /// <summary>
        /// A recreated adapter resolves namespace URIs instead of reusing stale session namespace indices.
        /// </summary>
        [Test]
        public async Task RecreatedAdapterUsesNamespaceUrisAndRetainsDetachedOutput()
        {
            var secrets = new InMemorySecretStore();
            byte[] certificate = [1, 2];
            byte[] issuer = [3, 4];
            var offered = new PullDcaCertificateReceipt(ByteString.From(certificate), [ByteString.From(issuer)]);
            using (var first = Store(secrets))
            {
                PullDcaCertificateReceipt result = await first.GetOrCreateAsync(
                    Registrar(), Id("app"), Id("request"), Profile(),
                    _ => new ValueTask<PullDcaCertificateReceipt>(offered)).ConfigureAwait(false);
                certificate[0] = 9;
                issuer[0] = 9;
                Assert.That(result.Certificate.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
                Assert.That(result.IssuerCertificates[0].ToArray(), Is.EqualTo(new byte[] { 3, 4 }));
            }
            using var restarted = Store(secrets, reordered: true);

            PullDcaCertificateReceipt restored = await restarted.GetOrCreateAsync(
                Registrar(2), Id("app", 2), Id("request", 2), Profile(2),
                _ => throw new AssertionException("A retained output must not repeat FinishRequest."))
                .ConfigureAwait(false);

            Assert.That(restored.Certificate.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(restored.IssuerCertificates[0].ToArray(), Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(await restarted.RemoveAsync(Registrar(2), Id("app", 2), Id("request", 2))
                .ConfigureAwait(false), Is.True);
            Assert.That(await restarted.RemoveAsync(Registrar(2), Id("app", 2), Id("request", 2))
                .ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// A null certificate group is retained as the Part 12 default across adapter recreation.
        /// </summary>
        [Test]
        public async Task DefaultApplicationGroupReceiptSurvivesAdapterRecreation()
        {
            var secrets = new InMemorySecretStore();
            PullDcaCertificateProfile profile = Profile() with { CertificateGroupId = NodeId.Null };
            int receiveCount = 0;
            using (var first = Store(secrets))
            {
                await first.GetOrCreateAsync(
                    Registrar(), Id("app"), Id("request"), profile, _ =>
                    {
                        receiveCount++;
                        return new ValueTask<PullDcaCertificateReceipt>(Receipt());
                    }).ConfigureAwait(false);
            }
            using var restarted = Store(secrets, reordered: true);

            PullDcaCertificateReceipt restored = await restarted.GetOrCreateAsync(
                Registrar(2), Id("app", 2), Id("request", 2),
                Profile(2) with { CertificateGroupId = NodeId.Null }, _ =>
                {
                    receiveCount++;
                    throw new AssertionException("A retained default-group receipt must not repeat FinishRequest.");
                }).ConfigureAwait(false);

            Assert.That(receiveCount, Is.EqualTo(1));
            Assert.That(restored.Certificate, Is.EqualTo(Receipt().Certificate));
            Assert.That(restored.IssuerCertificates, Is.EqualTo(Receipt().IssuerCertificates));
        }

        /// <summary>
        /// An explicit certificate group and the null default group cannot reuse each other's receipt.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DefaultAndExplicitCertificateGroupsRemainDistinct(bool defaultGroupFirst)
        {
            using var store = Store(new InMemorySecretStore());
            PullDcaCertificateProfile defaultGroup = Profile() with { CertificateGroupId = NodeId.Null };
            PullDcaCertificateProfile initial = defaultGroupFirst ? defaultGroup : Profile();
            PullDcaCertificateProfile changed = defaultGroupFirst ? Profile() : defaultGroup;
            await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), initial,
                _ => new ValueTask<PullDcaCertificateReceipt>(Receipt())).ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), changed,
                    _ => throw new AssertionException("A mismatched group must not receive another certificate."))
                    .ConfigureAwait(false));
        }

        /// <summary>
        /// Concurrent completions through one adapter cannot invoke two receipt factories.
        /// </summary>
        [Test]
        public async Task ConcurrentRetryWithinOneAdapterReceivesOnlyOnce()
        {
            using var store = Store(new InMemorySecretStore());
            var release = new TaskCompletionSource<PullDcaCertificateReceipt>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            Task<PullDcaCertificateReceipt> first = store.GetOrCreateAsync(
                Registrar(), Id("app"), Id("request"), Profile(), _ =>
                {
                    calls++;
                    return new ValueTask<PullDcaCertificateReceipt>(release.Task);
                }).AsTask();
            Task<PullDcaCertificateReceipt> retry = store.GetOrCreateAsync(
                Registrar(), Id("app"), Id("request"), Profile(), _ =>
                {
                    calls++;
                    throw new AssertionException("A competing receipt must not be received.");
                }).AsTask();
            bool retryWasPending = !retry.IsCompleted;
            release.SetResult(Receipt());
            PullDcaCertificateReceipt original = await first.ConfigureAwait(false);
            PullDcaCertificateReceipt retained = await retry.ConfigureAwait(false);

            Assert.That(retryWasPending, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(retained.Certificate, Is.EqualTo(original.Certificate));
            Assert.That(retained.IssuerCertificates, Is.EqualTo(original.IssuerCertificates));
        }

        /// <summary>
        /// A persisted request cannot be replayed against a different certificate group or type.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProfileMismatchStopsBeforeReceivingAgain(bool changeGroup)
        {
            using var store = Store(new InMemorySecretStore());
            await ReceiveAsync(store).ConfigureAwait(false);
            PullDcaCertificateProfile changed = changeGroup
                ? Profile() with { CertificateGroupId = Id("other") }
                : Profile() with { CertificateTypeId = Id("other") };

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), changed,
                    _ => throw new AssertionException("Profile mismatch must not call FinishRequest."))
                    .ConfigureAwait(false));
        }

        /// <summary>
        /// Every request binding component separates otherwise identical signing output.
        /// </summary>
        [TestCase("scope")]
        [TestCase("endpoint")]
        [TestCase("registrar")]
        [TestCase("directory")]
        [TestCase("application")]
        [TestCase("request")]
        public async Task RequestBindingSeparatesIndependentOutput(string difference)
        {
            var secrets = new InMemorySecretStore();
            using var original = Store(secrets);
            await ReceiveAsync(original).ConfigureAwait(false);
            using var changedScope = difference == "scope" ? Store(secrets, scope: "other-device") : null;
            SecretStorePullDcaCertificateReceiptStore selected = changedScope ?? original;
            PullDcaRegistrar registrar = difference switch
            {
                "endpoint" => Registrar() with { EndpointUrl = new Uri("opc.tcp://other:4840") },
                "registrar" => Registrar() with { RegistrarId = Id("other") },
                "directory" => Registrar() with { CertificateDirectoryId = Id("other") },
                _ => Registrar()
            };
            int receives = 0;

            PullDcaCertificateReceipt result = await selected.GetOrCreateAsync(
                registrar, Id(difference == "application" ? "other" : "app"),
                Id(difference == "request" ? "other" : "request"), Profile(), _ =>
                {
                    receives++;
                    return new ValueTask<PullDcaCertificateReceipt>(
                        new PullDcaCertificateReceipt(ByteString.From(new byte[] { 9 }), []));
                }).ConfigureAwait(false);

            Assert.That(receives, Is.EqualTo(1));
            Assert.That(result.Certificate.ToArray(), Is.EqualTo(new byte[] { 9 }));
            Assert.That(result.IssuerCertificates.IsEmpty, Is.True);
        }

        /// <summary>
        /// A committed receipt is reused even when the secret store's publication response was lost.
        /// </summary>
        [Test]
        public async Task CommittedPublicationWithLostResponseIsNotReceivedAgain()
        {
            var backing = new InMemorySecretStore();
            Mock<ISecretStore> secrets = ForwardReads(backing);
            var failure = new IOException("Publication response lost.");
            secrets.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    await backing.SetAsync(id, bytes, ct).ConfigureAwait(false);
                    throw failure;
                });
            using (var first = Store(secrets.Object))
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await ReceiveAsync(first).ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
            }
            using var restarted = Store(backing);

            PullDcaCertificateReceipt result = await restarted.GetOrCreateAsync(
                Registrar(), Id("app"), Id("request"), Profile(),
                _ => throw new AssertionException("Committed output must survive an uncertain store response."))
                .ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(Receipt().Certificate));
        }

        /// <summary>
        /// Read errors never turn into absent receipts and never invoke the network operation.
        /// </summary>
        [Test]
        public async Task ReadFailureDoesNotReceiveOrPublish()
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            var failure = new IOException("Receipt read failed.");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            using var store = Store(secrets.Object);

            IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(),
                    _ => throw new AssertionException("Read errors must not repeat FinishRequest."))
                    .ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A failed receive operation leaves no receipt and releases the adapter for an explicit retry.
        /// </summary>
        [Test]
        public async Task ReceiveFailureDoesNotPublishAndAllowsRetry()
        {
            var backing = new InMemorySecretStore();
            Mock<ISecretStore> secrets = ForwardReads(backing);
            secrets.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                    backing.SetAsync(id, bytes, ct));
            using var store = Store(secrets.Object);
            var failure = new IOException("FinishRequest failed before returning output.");

            IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(), _ => throw failure)
                    .ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            PullDcaCertificateReceipt result = await ReceiveAsync(store).ConfigureAwait(false);
            Assert.That(result.Certificate, Is.EqualTo(Receipt().Certificate));
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Corruption is retained and reported instead of requesting replacement material.
        /// </summary>
        [TestCase("version")]
        [TestCase("truncated")]
        [TestCase("trailing")]
        [TestCase("oversized")]
        public async Task CorruptReceiptDoesNotReceiveOrOverwrite(string corruption)
        {
            var backing = new InMemorySecretStore();
            Mock<ISecretStore> secrets = ForwardReads(backing);
            SecretIdentifier? captured = null;
            byte[] persisted = [];
            secrets.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    captured = id;
                    persisted = bytes.ToArray();
                    return backing.SetAsync(id, bytes, ct);
                });
            using var store = Store(secrets.Object);
            await ReceiveAsync(store).ConfigureAwait(false);
            byte[] damaged = corruption switch
            {
                "version" => new byte[] { 99, 0, 0, 0 },
                "truncated" => persisted.Take(5).ToArray(),
                "trailing" => persisted.Concat(new byte[] { 1 }).ToArray(),
                "oversized" => new byte[16 * 1024 * 1024 + 1],
                _ => throw new ArgumentOutOfRangeException(nameof(corruption))
            };
            SecretIdentifier identifier = captured ??
                throw new AssertionException("Reception did not publish its receipt.");
            await backing.SetAsync(identifier, damaged).ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(),
                    _ => throw new AssertionException("Corruption cannot authorize another FinishRequest."))
                    .ConfigureAwait(false));

            using ISecret? retained = await backing.GetAsync(identifier).ConfigureAwait(false);
            Assert.That(retained!.Bytes.ToArray(), Is.EqualTo(damaged));
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// Missing signing output and cancellation cannot produce a retained success.
        /// </summary>
        [TestCase("null")]
        [TestCase("certificate")]
        [TestCase("issuer")]
        [TestCase("cancel")]
        public async Task InvalidOrCancelledReceptionDoesNotPublish(string failure)
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            using var store = Store(secrets.Object);
            using var cancellation = new CancellationTokenSource();
            PullDcaCertificateReceipt Receive(CancellationToken _)
            {
                if (failure == "cancel")
                {
                    cancellation.Cancel();
                    return Receipt();
                }
                return failure switch
                {
                    "null" => null!,
                    "certificate" => new(default, []),
                    "issuer" => new(Receipt().Certificate, [default]),
                    _ => throw new ArgumentOutOfRangeException(nameof(failure))
                };
            }

            if (failure == "cancel")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(),
                        ct => new ValueTask<PullDcaCertificateReceipt>(Receive(ct)), cancellation.Token)
                        .ConfigureAwait(false));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(),
                        ct => new ValueTask<PullDcaCertificateReceipt>(Receive(ct))).ConfigureAwait(false));
            }
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Fluent selection registers one shared adapter and refuses to replace an explicit provider.
        /// </summary>
        [Test]
        public void FluentRegistrationSharesAdapterAndRejectsConflictingSelection()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISecretStore>(new InMemorySecretStore());
            services.AddSingleton(NUnitTelemetryContext.Create());
            IGdsClientBuilder builder = services.AddOpcUa().AddGdsClient();
            Assert.That(builder.AddPullDcaCertificateReceipts(Namespaces(), "device"), Is.SameAs(builder));
            ServiceDescriptor[] before = services.ToArray();
            Assert.That(() => builder.AddPullDcaCertificateReceipts(Namespaces(), "other"),
                Throws.InvalidOperationException);
            Assert.That(services.ToArray(), Is.EqualTo(before));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IPullDcaCertificateReceiptStore>(),
                Is.SameAs(provider.GetRequiredService<SecretStorePullDcaCertificateReceiptStore>()));
        }

        /// <summary>
        /// Creates one receipt without interpreting its deliberately synthetic certificate bytes.
        /// </summary>
        private static PullDcaCertificateReceipt Receipt()
        {
            return new(ByteString.From(new byte[] { 1, 2 }), [ByteString.From(new byte[] { 3, 4 })]);
        }

        /// <summary>
        /// Completes one deterministic receipt.
        /// </summary>
        private static ValueTask<PullDcaCertificateReceipt> ReceiveAsync(
            SecretStorePullDcaCertificateReceiptStore store)
        {
            return store.GetOrCreateAsync(Registrar(), Id("app"), Id("request"), Profile(),
                _ => new ValueTask<PullDcaCertificateReceipt>(Receipt()));
        }

        /// <summary>
        /// Creates the adapter with an explicit one-device scope.
        /// </summary>
        private static SecretStorePullDcaCertificateReceiptStore Store(
            ISecretStore secrets, bool reordered = false, string scope = "device")
        {
            return new(secrets, Namespaces(reordered), scope, NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Creates namespace mappings that differ only by a preceding unrelated namespace.
        /// </summary>
        private static NamespaceTable Namespaces(bool reordered = false)
        {
            var namespaces = new NamespaceTable();
            if (reordered)
            {
                namespaces.Append("urn:unrelated");
            }
            namespaces.Append("urn:registrar:model");
            return namespaces;
        }

        /// <summary>
        /// Creates a local model identifier.
        /// </summary>
        private static NodeId Id(string value, ushort index = 1)
        {
            return new(value, index);
        }

        /// <summary>
        /// Creates an explicitly selected endpoint and model nodes.
        /// </summary>
        private static PullDcaRegistrar Registrar(ushort index = 1)
        {
            return new(new Uri("opc.tcp://registrar:4840"), Id("registrar", index), Id("directory", index));
        }

        /// <summary>
        /// Creates one certificate profile.
        /// </summary>
        private static PullDcaCertificateProfile Profile(ushort index = 1)
        {
            return new(Id("group", index), Id("type", index));
        }

        /// <summary>
        /// Forwards receipt reads while leaving publication under explicit test control.
        /// </summary>
        private static Mock<ISecretStore> ForwardReads(InMemorySecretStore backing)
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns(backing.StoreType);
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, CancellationToken ct) => backing.GetAsync(id, ct));
            return secrets;
        }
    }
}
