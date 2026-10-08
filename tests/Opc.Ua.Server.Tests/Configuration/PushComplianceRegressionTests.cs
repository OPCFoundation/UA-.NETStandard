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

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the findings of the manual OPC 10000-12 Push
    /// Model compliance run (TrustList and UpdateCertificate handling).
    /// </summary>
    [TestFixture]
    [Category("TrustList")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class PushComplianceRegressionTests
    {
        private ITelemetryContext m_telemetry;
        private string m_basePath;
        private CertificateStoreIdentifier m_trustedStore;
        private CertificateStoreIdentifier m_issuerStore;
        private PushConfigurationTransactionCoordinator m_coordinator;
        private readonly List<TrustList> m_createdTrustLists = [];

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "pushcompliance",
                Guid.NewGuid().ToString("N")[..8]);
            m_trustedStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "trusted"));
            m_issuerStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "issuer"));
            m_coordinator = new PushConfigurationTransactionCoordinator(m_telemetry);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (TrustList trustList in m_createdTrustLists)
            {
                trustList.Dispose();
            }
            m_createdTrustLists.Clear();

            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
        }

        /// <summary>
        /// Re-adding a certificate that is already trusted succeeds and the
        /// following ApplyChanges commits instead of failing because the
        /// store refuses a second copy.
        /// </summary>
        [Test]
        public async Task AddCertificateOfAlreadyTrustedCertificateCommitsAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateSelfSigned("CN=Push Duplicate Add");
            await SeedTrustedAsync(cert).ConfigureAwait(false);

            AddCertificateMethodStateResult result = await AddCertificateAsync(node, context, cert.RawData)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(GetSessionId(context), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True, applyResult.ToString());
            Assert.That(await CountTrustedAsync(cert.Thumbprint).ConfigureAwait(false), Is.EqualTo(1));
        }

        /// <summary>
        /// A rolled back transaction must not delete a certificate that was
        /// trusted before AddCertificate staged the same certificate again.
        /// </summary>
        [Test]
        public async Task RollbackOfDuplicateAddCertificateKeepsPreExistingCertificateAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateSelfSigned("CN=Push Duplicate Rollback");
            await SeedTrustedAsync(cert).ConfigureAwait(false);

            AddCertificateMethodStateResult result = await AddCertificateAsync(node, context, cert.RawData)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            m_coordinator.Stage(GetSessionId(context), new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId(Guid.NewGuid(), 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "boom")
            });

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(GetSessionId(context), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.False);
            Assert.That(
                await CountTrustedAsync(cert.Thumbprint).ConfigureAwait(false),
                Is.EqualTo(1),
                "the certificate trusted before the transaction must survive the rollback");
        }

        /// <summary>
        /// A PEM encoded certificate passed to AddCertificate is accepted and
        /// stored in its DER form.
        /// </summary>
        [Test]
        public async Task AddCertificateAcceptsPemEncodedCertificateAsync()
        {
            TrustListState node = CreateNode();
            CreateTransactionalTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateSelfSigned("CN=Push Pem Add");

            AddCertificateMethodStateResult result = await AddCertificateAsync(node, context, ToPem(cert.RawData))
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True, result.ServiceResult?.ToString());

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(GetSessionId(context), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True);

            using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection stored = await store.FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].RawData, Is.EqualTo(cert.RawData));
        }

        /// <summary>
        /// OPC 10000-6 §6.2.2 / OPC 10000-4 Table 100: an X.509 version 1
        /// certificate is a structural error that is not suppressible.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateRejectsVersion1CertificateAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            byte[] version1 = CreateVersion1Certificate("CN=Push Version 1");
            using (var parsed = Certificate.FromRawData(version1))
            {
                Assert.That(X509Utils.IsX509Version3(parsed), Is.False, "test certificate must be version 1");
            }

            ServiceResult result = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = new[] { version1.ToByteString() }
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(m_coordinator.IsTransactionActive, Is.False);
        }

        /// <summary>
        /// A version 3 certificate without extensions is a valid (self-signed)
        /// certificate and is accepted.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateAcceptsVersion3CertificateWithoutExtensionsAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Push No Extensions", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 noExtensions = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            ServiceResult result = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = new[] { noExtensions.RawData.ToByteString() }
                }).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>
        /// OPC 10000-12 §7.8.2.5: a CRL that is not issued by a CA of the new
        /// TrustList is rejected by CloseAndUpdate (it used to be accepted and
        /// only failed the later ApplyChanges).
        /// </summary>
        [Test]
        public async Task CloseAndUpdateRejectsCrlOfUnknownIssuerAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate trustedCa = CreateCa("CN=Push Trusted CA");
            using Certificate unknownCa = CreateCa("CN=Push Unknown CA");

            ServiceResult result = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls),
                    TrustedCertificates = new[] { trustedCa.RawData.ToByteString() },
                    TrustedCrls = new[]
                    {
                        EmptyCrl(trustedCa).RawData.ToByteString(),
                        EmptyCrl(unknownCa).RawData.ToByteString()
                    }
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(m_coordinator.IsTransactionActive, Is.False);
        }

        /// <summary>
        /// A CRL that names a trusted CA as issuer but is signed with another
        /// key is rejected by CloseAndUpdate.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateRejectsCrlWithForgedSignatureAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate trustedCa = CreateCa("CN=Push Forged CRL CA");
            using Certificate otherKey = CreateCa("CN=Push Forger");
            IX509CRL forged = CrlBuilder.Create(trustedCa.SubjectName)
                .SetThisUpdate(DateTime.UtcNow)
                .SetNextUpdate(DateTime.UtcNow.AddMonths(1))
                .AddCRLExtension(X509Extensions.BuildCRLNumber(1))
                .CreateForRSA(otherKey);

            ServiceResult result = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls),
                    TrustedCertificates = new[] { trustedCa.RawData.ToByteString() },
                    TrustedCrls = new[] { forged.RawData.ToByteString() }
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        /// <summary>
        /// A CRL-only update is validated against the CAs that stay in the
        /// TrustList, so a valid CRL of a trusted CA is accepted and a CRL of
        /// an unknown issuer is not.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateValidatesCrlOnlyUpdatesAgainstStoredCasAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate trustedCa = CreateCa("CN=Push Stored CA");
            using Certificate unknownCa = CreateCa("CN=Push Crl Only Unknown CA");
            await SeedTrustedAsync(trustedCa).ConfigureAwait(false);

            ServiceResult rejected = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCrls,
                    TrustedCrls = new[] { EmptyCrl(unknownCa).RawData.ToByteString() }
                }).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));

            ServiceResult accepted = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCrls,
                    TrustedCrls = new[] { EmptyCrl(trustedCa).RawData.ToByteString() }
                }).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(accepted), Is.True, accepted.ToString());
        }

        /// <summary>
        /// Replacing the trusted certificates while keeping the CRL list must
        /// not leave the CRL of a removed CA behind: every CRL of the
        /// resulting TrustList is validated, not only the uploaded ones.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateRejectsRetainedCrlOfRemovedCaAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate removedCa = CreateCa("CN=Push Removed CA");
            using Certificate otherCa = CreateCa("CN=Push Remaining CA");
            await SeedTrustedAsync(removedCa).ConfigureAwait(false);
            using (ICertificateStore store = m_trustedStore.OpenStore(m_telemetry))
            {
                await store.AddCRLAsync(EmptyCrl(removedCa)).ConfigureAwait(false);
            }

            ServiceResult rejected = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = new[] { otherCa.RawData.ToByteString() }
                }).ConfigureAwait(false);
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));

            ServiceResult accepted = await CloseAndUpdateAsync(
                node,
                context,
                new TrustListDataType
                {
                    SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls),
                    TrustedCertificates = new[] { otherCa.RawData.ToByteString() },
                    TrustedCrls = new[] { EmptyCrl(otherCa).RawData.ToByteString() }
                }).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(accepted), Is.True, accepted.ToString());
        }

        /// <summary>
        /// With an audit event server attached the TrustList audit events are
        /// reported through the server (and so reach the Server Object)
        /// instead of the TrustList node, which is not an event notifier.
        /// </summary>
        [Test]
        public async Task TrustListAuditEventsAreReportedThroughTheServerAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            var auditServer = new CapturingAuditEventServer(new SystemContext(m_telemetry));
            trustList.SetAuditEventServer(auditServer);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateSelfSigned("CN=Push Audit");

            AddCertificateMethodStateResult result = await AddCertificateAsync(node, context, cert.RawData)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(auditServer.Events.OfType<TrustListUpdateRequestedAuditEventState>().Count(), Is.EqualTo(1));

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(GetSessionId(context), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True);

            TrustListUpdatedAuditEventState updated = auditServer.Events
                .OfType<TrustListUpdatedAuditEventState>()
                .Single();
            Assert.That(updated.Status!.Value, Is.True);
        }

        /// <summary>
        /// The server overloads of the TrustList audit helpers report through
        /// the server; without a server they fall back to the node and must not
        /// throw.
        /// </summary>
        [Test]
        public void TrustListAuditEventHelpersReportThroughTheServer()
        {
            var auditServer = new CapturingAuditEventServer(CreateContext(new NodeId(Guid.NewGuid(), 1)));
            TrustListState node = CreateNode();

            Assert.DoesNotThrow(() => ((IAuditEventServer)null!).ReportTrustListUpdatedAuditEvent(
                node,
                auditServer.DefaultAuditContext,
                node.NodeId,
                "Method/AddCertificate",
                NodeId.Null,
                default,
                StatusCodes.Good,
                m_telemetry.CreateLogger<PushComplianceRegressionTests>()));
            auditServer.ReportTrustListUpdateRequestedAuditEvent(
                node,
                auditServer.DefaultAuditContext,
                node.NodeId,
                "Method/AddCertificate",
                NodeId.Null,
                default,
                m_telemetry.CreateLogger<PushComplianceRegressionTests>());
            auditServer.ReportTrustListUpdatedAuditEvent(
                node,
                auditServer.DefaultAuditContext,
                node.NodeId,
                "Method/AddCertificate",
                NodeId.Null,
                default,
                StatusCodes.Good,
                m_telemetry.CreateLogger<PushComplianceRegressionTests>());

            Assert.That(auditServer.Events.OfType<TrustListUpdateRequestedAuditEventState>().Count(), Is.EqualTo(1));
            Assert.That(auditServer.Events.OfType<TrustListUpdatedAuditEventState>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// AddCertificate of a CA without a CRL succeeds (the server only warns
        /// that its certificates fail the revocation check); a CA whose CRL is
        /// already in the TrustList is added as well.
        /// </summary>
        [Test]
        public async Task AddCertificateOfCaWithAndWithoutCrlSucceedsAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration { RejectUnknownRevocationStatus = true });
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate caWithoutCrl = CreateCa("CN=Push CA Without CRL");
            using Certificate caWithCrl = CreateCa("CN=Push CA With CRL");
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                // The directory store only accepts a CRL whose issuer it holds.
                await issuerStore.AddAsync(caWithCrl).ConfigureAwait(false);
                await issuerStore.AddCRLAsync(EmptyCrl(caWithCrl)).ConfigureAwait(false);
            }

            AddCertificateMethodStateResult withoutCrl = await AddCertificateAsync(node, context, caWithoutCrl.RawData)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(withoutCrl.ServiceResult), Is.True, withoutCrl.ServiceResult?.ToString());
            AddCertificateMethodStateResult withCrl = await AddCertificateAsync(node, context, caWithCrl.RawData)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(withCrl.ServiceResult), Is.True, withCrl.ServiceResult?.ToString());

            ServiceResult applyResult = await m_coordinator
                .ApplyChangesAsync(GetSessionId(context), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(applyResult), Is.True);
            Assert.That(await CountTrustedAsync(caWithoutCrl.Thumbprint).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await CountTrustedAsync(caWithCrl.Thumbprint).ConfigureAwait(false), Is.EqualTo(1));
        }

        /// <summary>
        /// CloseAndUpdate from a Session that does not own the active
        /// transaction returns Bad_TransactionPending and is audited with
        /// Status=false through the server.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateWhileAnotherSessionOwnsTheTransactionIsAuditedAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTransactionalTrustList(node);
            var auditServer = new CapturingAuditEventServer(CreateContext(new NodeId(Guid.NewGuid(), 1)));
            trustList.SetAuditEventServer(auditServer);
            ISystemContext writer = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext owner = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateSelfSigned("CN=Push Foreign Transaction");

            OpenMethodStateResult open = await node.Open!.OnCallAsync!(
                writer,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                CancellationToken.None).ConfigureAwait(false);
            await node.Write!.OnCallAsync!(
                writer,
                node.Write,
                node.NodeId,
                open.FileHandle,
                EncodeTrustListPayload(writer, new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = new[] { cert.RawData.ToByteString() }
                }),
                CancellationToken.None).ConfigureAwait(false);

            // another Session starts a transaction while the file is open
            m_coordinator.Stage(GetSessionId(owner), new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            });

            CloseAndUpdateMethodStateResult result = await node.CloseAndUpdate!.OnCallAsync!(
                writer,
                node.CloseAndUpdate,
                node.NodeId,
                open.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));
            TrustListUpdatedAuditEventState updated = auditServer.Events
                .OfType<TrustListUpdatedAuditEventState>()
                .Single();
            Assert.That(updated.Status!.Value, Is.False);
            m_coordinator.CancelChanges(GetSessionId(owner));
        }

        [Test]
        public void IsX509Version3RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => X509Utils.IsX509Version3(null!));
        }

        [Test]
        public void IsX509Version3DistinguishesVersion1FromVersion3()
        {
            using var version1 = Certificate.FromRawData(CreateVersion1Certificate("CN=Push Version Check"));
            using Certificate version3 = CreateSelfSigned("CN=Push Version 3");

            Assert.That(X509Utils.IsX509Version3(version1), Is.False);
            Assert.That(X509Utils.IsX509Version3(version3), Is.True);
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
            var trustList = new TrustList(
                node,
                m_trustedStore,
                m_issuerStore,
                AllowAccess,
                AllowAccess,
                m_telemetry,
                m_coordinator);
            m_createdTrustLists.Add(trustList);
            return trustList;
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

        private static NodeId GetSessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private async Task SeedTrustedAsync(Certificate certificate)
        {
            using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
            using var publicCertificate = Certificate.FromRawData(certificate.RawData);
            await store.AddAsync(publicCertificate, ct: CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<int> CountTrustedAsync(string thumbprint)
        {
            using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await store.FindByThumbprintAsync(thumbprint)
                .ConfigureAwait(false);
            return found.Count;
        }

        private static ValueTask<AddCertificateMethodStateResult> AddCertificateAsync(
            TrustListState node,
            ISystemContext context,
            byte[] certificate)
        {
            return node.AddCertificate!.OnCallAsync!(
                context,
                node.AddCertificate,
                node.NodeId,
                certificate.ToByteString(),
                true,
                CancellationToken.None);
        }

        private static async Task<ServiceResult> CloseAndUpdateAsync(
            TrustListState node,
            ISystemContext context,
            TrustListDataType trustList)
        {
            OpenMethodStateResult open = await node.Open!.OnCallAsync!(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                CancellationToken.None).ConfigureAwait(false);
            await node.Write!.OnCallAsync!(
                context,
                node.Write,
                node.NodeId,
                open.FileHandle,
                EncodeTrustListPayload(context, trustList),
                CancellationToken.None).ConfigureAwait(false);
            CloseAndUpdateMethodStateResult result = await node.CloseAndUpdate!.OnCallAsync!(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                open.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            return result.ServiceResult;
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

        private static Certificate CreateSelfSigned(string subject)
        {
            return CertificateBuilder
                .Create(subject + " " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateCa(string subject)
        {
            return CertificateBuilder
                .Create(subject + " " + Guid.NewGuid().ToString("N")[..8])
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static X509CRL EmptyCrl(Certificate ca)
        {
            return DefaultCertificateIssuer.Instance.RevokeCertificates(ca, null!, null!);
        }

        private static byte[] ToPem(byte[] der)
        {
            return Encoding.ASCII.GetBytes(
                "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----\n");
        }

        /// <summary>
        /// Builds a self-signed X.509 version 1 certificate: the platform
        /// <see cref="CertificateRequest"/> always emits version 3, so the
        /// version field and the extensions are stripped from its TBS and the
        /// result is signed again.
        /// </summary>
        internal static byte[] CreateVersion1Certificate(string subject)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 version3 = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            var certificate = new AsnReader(version3.RawData, AsnEncodingRules.DER).ReadSequence();
            AsnReader tbs = certificate.ReadSequence();
            ReadOnlyMemory<byte> signatureAlgorithm = certificate.ReadEncodedValue();

            var tbsWriter = new AsnWriter(AsnEncodingRules.DER);
            using (tbsWriter.PushSequence())
            {
                var versionTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
                var extensionsTag = new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true);
                while (tbs.HasData)
                {
                    Asn1Tag tag = tbs.PeekTag();
                    ReadOnlyMemory<byte> element = tbs.ReadEncodedValue();
                    if (tag.HasSameClassAndValue(versionTag) || tag.HasSameClassAndValue(extensionsTag))
                    {
                        continue;
                    }
                    tbsWriter.WriteEncodedValue(element.Span);
                }
            }
            byte[] tbsBytes = tbsWriter.Encode();
            byte[] signature = key.SignData(tbsBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                writer.WriteEncodedValue(tbsBytes);
                writer.WriteEncodedValue(signatureAlgorithm.Span);
                writer.WriteBitString(signature);
            }
            return writer.Encode();
        }

        private sealed class CapturingAuditEventServer : IAuditEventServer
        {
            public CapturingAuditEventServer(ISystemContext defaultAuditContext)
            {
                DefaultAuditContext = defaultAuditContext;
            }

            public bool Auditing => true;

            public ISystemContext DefaultAuditContext { get; }

            public List<AuditEventState> Events { get; } = [];

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                Events.Add(e);
            }
        }
    }
}
