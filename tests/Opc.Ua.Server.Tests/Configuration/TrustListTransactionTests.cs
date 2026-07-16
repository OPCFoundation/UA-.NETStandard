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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises <see cref="TrustList"/> when constructed with a shared
    /// <see cref="IPushConfigurationTransactionCoordinator"/> (OPC 10000-12
    /// §§7.10.2-7.10.11): <c>CloseAndUpdate</c>, <c>AddCertificate</c>, and
    /// <c>RemoveCertificate</c> must stage their mutation instead of
    /// applying it immediately, and every store change must be visible
    /// only after the coordinator commits.
    /// </summary>
    [TestFixture]
    [Category("TrustList")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class TrustListTransactionTests
    {
        private ITelemetryContext m_telemetry;
        private string m_basePath;
        private CertificateStoreIdentifier m_trustedStore;
        private CertificateStoreIdentifier m_issuerStore;
        private PushConfigurationTransactionCoordinator m_coordinator;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "tltx",
                Guid.NewGuid().ToString("N")[..8]);
            m_trustedStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "trusted"));
            m_issuerStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "issuer"));
            m_coordinator = new PushConfigurationTransactionCoordinator(m_telemetry);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
        }

        [Test]
        public async Task CloseAndUpdateStagesInsteadOfApplyingImmediatelyAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate trustedCert = CreateTestCertificate();

            uint fileHandle = await OpenForWriteAsync(node, context).ConfigureAwait(false);
            await WriteTrustedCertificateAsync(node, context, fileHandle, trustedCert).ConfigureAwait(false);

            CloseAndUpdateMethodStateResult result = await node.CloseAndUpdate.OnCallAsync(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
            Assert.That(node.OpenCount.Value, Is.Zero, "the file handle must close even though the change is staged");
            Assert.That(m_coordinator.IsTransactionActive, Is.True);
            Assert.That(m_coordinator.HasOpenTrustListWriter, Is.False, "CloseAndUpdate must clear the open-writer flag");

            using ICertificateStore trustedStoreBeforeCommit = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection notYetPresent = await trustedStoreBeforeCommit
                .FindByThumbprintAsync(trustedCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(notYetPresent, Is.Empty, "the store must not change before ApplyChanges commits");

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(context.SessionId(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True);

            using ICertificateStore trustedStoreAfterCommit = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection nowPresent = await trustedStoreAfterCommit
                .FindByThumbprintAsync(trustedCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(nowPresent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task CloseAndUpdateRollsBackWhenALaterOperationFailsAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate originalCert = CreateTestCertificate();

            // Seed the store with a certificate that must survive an
            // aborted transaction.
            using (ICertificateStore seedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await seedStore.AddAsync(originalCert, ct: CancellationToken.None).ConfigureAwait(false);
            }

            using Certificate replacementCert = CreateTestCertificate();
            uint fileHandle = await OpenForWriteAsync(node, context).ConfigureAwait(false);
            await WriteTrustedCertificateAsync(node, context, fileHandle, replacementCert).ConfigureAwait(false);

            CloseAndUpdateMethodStateResult result = await node.CloseAndUpdate.OnCallAsync(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            // A second, deliberately failing operation in the same
            // transaction forces ApplyChanges to reverse-compensate.
            m_coordinator.Stage(context.SessionId(), new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "boom")
            });

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(context.SessionId(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.False);

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection originalStillPresent = await trustedStore
                .FindByThumbprintAsync(originalCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(originalStillPresent, Has.Count.EqualTo(1), "rollback must restore the original certificate");

            using CertificateCollection replacementAbsent = await trustedStore
                .FindByThumbprintAsync(replacementCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(replacementAbsent, Is.Empty, "rollback must remove the staged replacement");
        }

        [Test]
        public async Task CloseAndUpdateSelfCompensatesWhenOnlyOneOfSeveralStoresFailsAsync()
        {
            // A single CloseAndUpdate operation can update up to four
            // stores/collections (issuer certificates/CRLs, trusted
            // certificates/CRLs) in one CommitAsync. The coordinator only
            // reverse-compensates OTHER operations that already committed;
            // it never compensates the operation whose own CommitAsync
            // threw. This proves the operation restores itself: the
            // trusted-certificates category's own update fully succeeds
            // (old removed, new added) before the issuer-certificates
            // category fails (a duplicated thumbprint makes its second
            // AddAsync throw internally), yet both stores must still end
            // up back at their pre-transaction content once ApplyChanges
            // reports the failure.
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate originalTrusted = CreateTestCertificate();
            using Certificate originalIssuer = CreateTestCertificate();
            using Certificate newTrusted = CreateTestCertificate();
            using Certificate duplicateIssuer = CreateTestCertificate();

            using (ICertificateStore trustedSeed = m_trustedStore.OpenStore(m_telemetry))
            {
                await trustedSeed.AddAsync(originalTrusted, ct: CancellationToken.None).ConfigureAwait(false);
            }
            using (ICertificateStore issuerSeed = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerSeed.AddAsync(originalIssuer, ct: CancellationToken.None).ConfigureAwait(false);
            }

            uint fileHandle = await OpenForWriteAsync(node, context).ConfigureAwait(false);

            var trustListData = new TrustListDataType
            {
                SpecifiedLists
                    = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates)
            };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates
                .AddItem(newTrusted.RawData.ToByteString());
            ByteString duplicate = duplicateIssuer.RawData.ToByteString();
            trustListData.IssuerCertificates = trustListData.IssuerCertificates
                .AddItem(duplicate)
                .AddItem(duplicate);

            ByteString payload = EncodeTrustListPayload(context, trustListData);
            await node.Write.OnCallAsync(
                context,
                node.Write,
                node.NodeId,
                fileHandle,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            CloseAndUpdateMethodStateResult closeResult = await node.CloseAndUpdate.OnCallAsync(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closeResult.ServiceResult), Is.True);
            Assert.That(closeResult.ApplyChangesRequired, Is.True);

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(context.SessionId(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                ServiceResult.IsGood(applyResult),
                Is.False,
                "the duplicated issuer certificate must fail the batch update");

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection trustedOriginalStillPresent = await trustedStore
                .FindByThumbprintAsync(originalTrusted.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(
                trustedOriginalStillPresent,
                Has.Count.EqualTo(1),
                "self-compensation must restore the trusted store even though its own update succeeded");

            using CertificateCollection trustedNewAbsent = await trustedStore
                .FindByThumbprintAsync(newTrusted.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(trustedNewAbsent, Is.Empty, "the trusted store must not keep the new certificate");

            using ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry);
            using CertificateCollection issuerOriginalStillPresent = await issuerStore
                .FindByThumbprintAsync(originalIssuer.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(
                issuerOriginalStillPresent,
                Has.Count.EqualTo(1),
                "self-compensation must restore the issuer store's original certificate");

            using CertificateCollection issuerDuplicateAbsent = await issuerStore
                .FindByThumbprintAsync(duplicateIssuer.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(issuerDuplicateAbsent, Is.Empty, "the partially-applied duplicate must not remain");
        }

        [Test]
        public async Task AddCertificateStagesInsteadOfApplyingImmediatelyAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate();

            AddCertificateMethodStateResult result = await node.AddCertificate.OnCallAsync(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(m_coordinator.IsTransactionActive, Is.True);

            using ICertificateStore trustedStoreBeforeCommit = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection notYetPresent = await trustedStoreBeforeCommit
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(notYetPresent, Is.Empty);

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(context.SessionId(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True);

            using ICertificateStore trustedStoreAfterCommit = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection nowPresent = await trustedStoreAfterCommit
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(nowPresent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RemoveCertificateStagesAndRollbackRestoresItAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate();

            using (ICertificateStore seedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await seedStore.AddAsync(cert, ct: CancellationToken.None).ConfigureAwait(false);
            }

            RemoveCertificateMethodStateResult removeResult = await node.RemoveCertificate.OnCallAsync(
                context,
                node.RemoveCertificate,
                node.NodeId,
                cert.Thumbprint,
                true,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(removeResult.ServiceResult), Is.True);

            using (ICertificateStore trustedStoreBeforeCommit = m_trustedStore.OpenStore(m_telemetry))
            using (CertificateCollection stillPresent = await trustedStoreBeforeCommit
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false))
            {
                Assert.That(stillPresent, Has.Count.EqualTo(1), "the store must not change before ApplyChanges commits");
            }

            ServiceResult cancelResult = m_coordinator.CancelChanges(context.SessionId());
            Assert.That(ServiceResult.IsGood(cancelResult), Is.True);

            using ICertificateStore trustedStoreAfterCancel = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection stillThere = await trustedStoreAfterCancel
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(stillThere, Has.Count.EqualTo(1), "CancelChanges must never remove the certificate");
        }

        [Test]
        public async Task OpenForWriteFromAnotherSessionWhileTransactionActiveThrowsBadTransactionPendingAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext ownerContext = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext otherContext = CreateContext(new NodeId(Guid.NewGuid(), 1));

            // The owner starts (and leaves active) a transaction via an
            // unrelated staged operation.
            m_coordinator.Stage(ownerContext.SessionId(), new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.Open.OnCallAsync(
                        otherContext,
                        node.Open,
                        node.NodeId,
                        (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
        }

        [Test]
        public async Task NotifySessionClosingClosesTheOpenWriteHandleAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            await OpenForWriteAsync(node, context).ConfigureAwait(false);
            Assert.That(m_coordinator.HasOpenTrustListWriter, Is.True);

            trustList.NotifySessionClosing(context.SessionId());

            Assert.That(node.OpenCount.Value, Is.Zero);
            Assert.That(m_coordinator.HasOpenTrustListWriter, Is.False);
        }

        [Test]
        public void LegacyConstructorWithoutCoordinatorAppliesCloseAndUpdateImmediately()
        {
            TrustListState node = CreateNode();
            var trustList = new TrustList(
                node,
                m_trustedStore,
                m_issuerStore,
                AllowAccess,
                AllowAccess,
                m_telemetry);

            Assert.That(trustList, Is.Not.Null);
            // The pre-existing (non-transactional) constructor overload
            // must remain usable without a coordinator; full behavioral
            // coverage of the immediate-apply path is in TrustListTests.
        }

        private TrustListState CreateNode()
        {
            var context = new SystemContext(m_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var node = new TrustListState(null);
            node.CreateOrReplaceOpen(context, node);
            node.CreateOrReplaceClose(context, node);
            node.CreateOrReplaceRead(context, node);
            node.CreateOrReplaceWrite(context, node);
            node.CreateOrReplaceOpenCount(context, node);
            node.CreateOrReplaceOpenWithMasks(context, node);
            node.CreateOrReplaceCloseAndUpdate(context, node);
            node.CreateOrReplaceAddCertificate(context, node);
            node.CreateOrReplaceRemoveCertificate(context, node);
            node.CreateOrReplaceLastUpdateTime(context, node);
            return node;
        }

        private TrustList CreateTransactionalTrustList(TrustListState node)
        {
            return new TrustList(
                node,
                m_trustedStore,
                m_issuerStore,
                AllowAccess,
                AllowAccess,
                m_telemetry,
                m_coordinator);
        }

        private static void AllowAccess(ISystemContext context, CertificateStoreIdentifier store)
        {
            // Access granted: no-op.
        }

        private SessionSystemContext CreateContext(NodeId sessionId)
        {
            return new SessionSystemContext(m_telemetry)
            {
                SessionId = sessionId,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable(),
                EncodeableFactory = Opc.Ua.EncodeableFactory.Create()
            };
        }

        private static Certificate CreateTestCertificate()
        {
            return CertificateBuilder
                .Create("CN=TrustListTransaction " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static async Task<uint> OpenForWriteAsync(TrustListState node, ISystemContext context)
        {
            OpenMethodStateResult openResult = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                CancellationToken.None).ConfigureAwait(false);
            return openResult.FileHandle;
        }

        private static async Task WriteTrustedCertificateAsync(
            TrustListState node,
            ISystemContext context,
            uint fileHandle,
            Certificate trustedCert)
        {
            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates
            };
            ArrayOf<ByteString> trustedCertificates = new ByteString[] { trustedCert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(trustedCertificates);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            await node.Write.OnCallAsync(
                context,
                node.Write,
                node.NodeId,
                fileHandle,
                payload,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static ByteString EncodeTrustListPayload(ISystemContext context, TrustListDataType trustList)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(
                context.Telemetry,
                context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris
            };
            using var strm = new MemoryStream();
            using (var encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            return ByteString.From(strm.ToArray());
        }
    }

    /// <summary>
    /// Test-only helper to read the Session NodeId back out of an
    /// <see cref="ISystemContext"/> built by this fixture's
    /// <c>CreateContext</c>.
    /// </summary>
    file static class SystemContextExtensions
    {
        public static NodeId SessionId(this ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }
    }
}
