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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using OpcUa = Opc.Ua;
using X509ContentType = System.Security.Cryptography.X509Certificates.X509ContentType;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// End-to-end OPC 10000-12 Push Model checks that drive the
    /// ServerConfiguration and TrustList Methods of a running server over a
    /// secure channel with raw Method calls, so the exact operation status
    /// codes are verified. The cases come from the manual Push Model
    /// compliance test plan (CTT-style TrustList transactions, masked reads,
    /// cross-Session signing requests and certificate validation).
    /// </summary>
    [TestFixture]
    [Category("GDSPush")]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class PushComplianceTests
    {
        private GlobalDiscoveryTestServer m_server;
        private ServerConfigurationPushTestClient m_pushClient;
        private ITelemetryContext m_telemetry;
        private TrustListDataType m_originalTrustList;
        private readonly List<ISession> m_sessions = [];

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_server = await TestUtils.StartGDSAsync(true, CertificateStoreType.Directory).ConfigureAwait(false);
            m_pushClient = new ServerConfigurationPushTestClient(true, m_telemetry);
            await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            await m_pushClient.ConnectAsync(SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            m_originalTrustList = await ReadTrustListAsync(session, (uint)TrustListMasks.All).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                if (m_originalTrustList != null && m_pushClient != null)
                {
                    ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
                    await CancelChangesAsync(session).ConfigureAwait(false);
                    m_originalTrustList.SpecifiedLists = (uint)TrustListMasks.All;
                    await PushTrustListAsync(session, m_originalTrustList).ConfigureAwait(false);
                    await ApplyChangesAsync(session).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                await CloseSessionsAsync().ConfigureAwait(false);
                if (m_pushClient != null)
                {
                    await m_pushClient.DisconnectClientAsync().ConfigureAwait(false);
                    m_pushClient.Dispose();
                }
                if (m_server != null)
                {
                    await m_server.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            // Never leave a transaction pending for the next case.
            foreach (ISession session in m_sessions.Where(s => s.Connected))
            {
                await CancelChangesAsync(session).ConfigureAwait(false);
            }
            await CloseSessionsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// TrustList transaction rules (OPC 10000-12 §7.8.2.5, §7.10.9):
        /// ApplyChanges is refused while the TrustList is open for writing,
        /// CloseAndUpdate reports ApplyChangesRequired, another Session cannot
        /// open the TrustList while the transaction is pending, and a second
        /// ApplyChanges has nothing to do.
        /// </summary>
        [Test]
        public async Task TrustListTransactionLifecycleAsync()
        {
            ISession owner = await OpenAdminSessionAsync().ConfigureAwait(false);
            TrustListTypeClient trustList = AppTrustList(owner);
            byte[] payload = Encode(owner, m_originalTrustList);

            uint handle = await trustList.OpenAsync(WriteEraseExisting).ConfigureAwait(false);
            await trustList.WriteAsync(handle, payload.ToByteString()).ConfigureAwait(false);
            await AssertStatusAsync(() => ApplyChangesAsync(owner), StatusCodes.BadInvalidState).ConfigureAwait(false);

            bool applyChangesRequired = await trustList.CloseAndUpdateAsync(handle).ConfigureAwait(false);
            Assert.That(applyChangesRequired, Is.True);

            ISession other = await OpenAdminSessionAsync().ConfigureAwait(false);
            await AssertStatusAsync(
                () => AppTrustList(other).OpenAsync(WriteEraseExisting).AsTask(),
                StatusCodes.BadTransactionPending).ConfigureAwait(false);
            await AssertStatusAsync(
                () => AppTrustList(other).AddCertificateAsync(CreateSelfSigned().RawData.ToByteString(), true).AsTask(),
                StatusCodes.BadTransactionPending).ConfigureAwait(false);

            await ApplyChangesAsync(owner).ConfigureAwait(false);

            ISession afterApply = await OpenAdminSessionAsync().ConfigureAwait(false);
            await AssertStatusAsync(() => ApplyChangesAsync(afterApply), StatusCodes.BadNothingToDo).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-20 §4.2.2: opening a file for writing that is already
        /// open returns Bad_NotWritable. OPC 10000-12 §7.8.2.6: AddCertificate
        /// returns Bad_NotWritable while the TrustList is open for reading and
        /// Bad_InvalidState while it is open for writing.
        /// </summary>
        [Test]
        public async Task OpenTrustListBlocksWritersAsync()
        {
            ISession reader = await OpenAdminSessionAsync().ConfigureAwait(false);
            ISession writer = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate cert = CreateSelfSigned();

            uint readHandle = await AppTrustList(reader).OpenWithMasksAsync((uint)TrustListMasks.All).ConfigureAwait(false);
            try
            {
                await AssertStatusAsync(
                    () => AppTrustList(writer).OpenAsync(WriteEraseExisting).AsTask(),
                    StatusCodes.BadNotWritable).ConfigureAwait(false);
                await AssertStatusAsync(
                    () => AppTrustList(writer).AddCertificateAsync(cert.RawData.ToByteString(), true).AsTask(),
                    StatusCodes.BadNotWritable).ConfigureAwait(false);
            }
            finally
            {
                await AppTrustList(reader).CloseAsync(readHandle).ConfigureAwait(false);
            }

            uint writeHandle = await AppTrustList(writer).OpenAsync(WriteEraseExisting).ConfigureAwait(false);
            try
            {
                await AssertStatusAsync(
                    () => AppTrustList(writer).AddCertificateAsync(cert.RawData.ToByteString(), true).AsTask(),
                    StatusCodes.BadInvalidState).ConfigureAwait(false);
            }
            finally
            {
                await AppTrustList(writer).CloseAsync(writeHandle).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// OPC 10000-12 §7.8.2.3: OpenWithMasks returns only the requested
        /// parts of the TrustList.
        /// </summary>
        [Test]
        public async Task OpenWithMasksReturnsOnlyTheRequestedListsAsync()
        {
            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate ca = CreateCa("CN=Push Masks CA");
            X509CRL crl = DefaultCertificateIssuer.Instance.RevokeCertificates(ca, null, null);
            using Certificate trusted = CreateSelfSigned();
            await PushTrustListAsync(session, new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = new[] { trusted.RawData.ToByteString() },
                TrustedCrls = default,
                IssuerCertificates = new[] { ca.RawData.ToByteString() },
                IssuerCrls = new[] { crl.RawData.ToByteString() }
            }).ConfigureAwait(false);
            await ApplyChangesAsync(session).ConfigureAwait(false);
            session = await OpenAdminSessionAsync().ConfigureAwait(false);

            foreach (TrustListMasks mask in new[]
            {
                TrustListMasks.TrustedCertificates,
                TrustListMasks.IssuerCertificates,
                TrustListMasks.IssuerCrls,
                TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates,
                TrustListMasks.None
            })
            {
                TrustListDataType read = await ReadTrustListAsync(session, (uint)mask).ConfigureAwait(false);
                Assert.That(read.SpecifiedLists, Is.EqualTo((uint)mask), mask.ToString());
                Assert.That(read.TrustedCertificates.Count > 0, Is.EqualTo(mask.HasFlag(TrustListMasks.TrustedCertificates)), mask.ToString());
                Assert.That(read.IssuerCertificates.Count > 0, Is.EqualTo(mask.HasFlag(TrustListMasks.IssuerCertificates)), mask.ToString());
                Assert.That(read.IssuerCrls.Count > 0, Is.EqualTo(mask.HasFlag(TrustListMasks.IssuerCrls)), mask.ToString());
                Assert.That(read.TrustedCrls.Count, Is.Zero, mask.ToString());
            }
        }

        /// <summary>
        /// Re-adding a trusted certificate succeeds and the following
        /// ApplyChanges commits (it used to fail with a bare Bad status because
        /// the store refused the second copy). A PEM encoded certificate is
        /// accepted and stored as DER.
        /// </summary>
        [Test]
        public async Task AddCertificateDuplicateAndPemCommitAsync()
        {
            using Certificate cert = CreateSelfSigned();
            using Certificate pemCert = CreateSelfSigned();

            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            await AppTrustList(session).AddCertificateAsync(cert.RawData.ToByteString(), true).ConfigureAwait(false);
            await ApplyChangesAsync(session).ConfigureAwait(false);

            session = await OpenAdminSessionAsync().ConfigureAwait(false);
            await AppTrustList(session).AddCertificateAsync(cert.RawData.ToByteString(), true).ConfigureAwait(false);
            byte[] pem = Encoding.ASCII.GetBytes(
                "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(pemCert.RawData, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----\n");
            await AppTrustList(session).AddCertificateAsync(pem.ToByteString(), true).ConfigureAwait(false);
            await ApplyChangesAsync(session).ConfigureAwait(false);

            session = await OpenAdminSessionAsync().ConfigureAwait(false);
            TrustListDataType read = await ReadTrustListAsync(session, (uint)TrustListMasks.TrustedCertificates)
                .ConfigureAwait(false);
            Assert.That(read.TrustedCertificates.ToArray().Count(c => c.ToArray().AsSpan().SequenceEqual(cert.RawData)), Is.EqualTo(1));
            Assert.That(read.TrustedCertificates.ToArray().Any(c => c.ToArray().AsSpan().SequenceEqual(pemCert.RawData)), Is.True);
        }

        /// <summary>
        /// OPC 10000-12 §7.8.2.5: a CRL that is not issued by a CA of the new
        /// TrustList is rejected by CloseAndUpdate.
        /// </summary>
        [Test]
        public async Task CloseAndUpdateRejectsCrlOfUnknownIssuerAsync()
        {
            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate ca = CreateCa("CN=Push Known CA");
            using Certificate unknownCa = CreateCa("CN=Push Unknown CRL CA");
            X509CRL unknownCrl = DefaultCertificateIssuer.Instance.RevokeCertificates(unknownCa, null, null);

            await AssertStatusAsync(
                () => PushTrustListAsync(session, new TrustListDataType
                {
                    SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls),
                    TrustedCertificates = new[] { ca.RawData.ToByteString() },
                    TrustedCrls = new[] { unknownCrl.RawData.ToByteString() }
                }),
                StatusCodes.BadCertificateInvalid).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-12 §7.10.5: the issuers of a new certificate must already
        /// be in the TrustList; a certificate of an unknown CA is rejected even
        /// when the CA is passed as IssuerCertificates.
        /// </summary>
        [Test]
        public async Task UpdateCertificateRejectsCertificateOfUnknownIssuerAsync()
        {
            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate unknownCa = CreateCa("CN=Push Update Unknown CA");
            using Certificate certificate = await CreateServerCertificateAsync(session, unknownCa).ConfigureAwait(false);

            await AssertStatusAsync(
                () => ServerConfiguration(session).UpdateCertificateAsync(
                    AppGroup(session),
                    RsaType(session),
                    certificate.RawData.ToByteString(),
                    new[] { unknownCa.RawData.ToByteString() },
                    "PFX",
                    certificate.Export(X509ContentType.Pfx).ToByteString()).AsTask(),
                StatusCodes.BadCertificateChainIncomplete).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-12 §7.10.10: the key pair generated by CreateSigningRequest
        /// is available to UpdateCertificate from another Session of the same
        /// administrator.
        /// </summary>
        [Test]
        public async Task SigningRequestKeyIsUsableFromAnotherSessionAsync()
        {
            ISession first = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate ca = CreateCa("CN=Push Cross Session CA");
            X509CRL crl = DefaultCertificateIssuer.Instance.RevokeCertificates(ca, null, null);
            TrustListDataType withCa = await ReadTrustListAsync(first, (uint)TrustListMasks.All).ConfigureAwait(false);
            withCa.SpecifiedLists = (uint)TrustListMasks.All;
            withCa.TrustedCertificates = withCa.TrustedCertificates.AddItem(ca.RawData.ToByteString());
            withCa.TrustedCrls = withCa.TrustedCrls.AddItem(crl.RawData.ToByteString());
            await PushTrustListAsync(first, withCa).ConfigureAwait(false);
            await ApplyChangesAsync(first).ConfigureAwait(false);

            first = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate current = Certificate.FromRawData(
                first.ConfiguredEndpoint.Description.ServerCertificate);
            ByteString csr = await ServerConfiguration(first).CreateSigningRequestAsync(
                AppGroup(first),
                RsaType(first),
                current.Subject,
                true,
                CreateNonce(32).ToByteString()).ConfigureAwait(false);
            var request = new Pkcs10CertificationRequest(csr.ToArray());
            Assert.That(request.Verify(), Is.True);
            using Certificate signed = CertificateBuilder.Create(request.Subject)
                .AddExtension(new X509SubjectAltNameExtension(
                    m_server.Config.ApplicationUri,
                    X509Utils.GetDomainsFromCertificate(current).ToArray()))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(12)
                .SetIssuer(ca)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();

            ISession second = await OpenAdminSessionAsync().ConfigureAwait(false);
            bool applyChangesRequired = await ServerConfiguration(second).UpdateCertificateAsync(
                AppGroup(second),
                RsaType(second),
                signed.RawData.ToByteString(),
                default,
                null,
                default).ConfigureAwait(false);
            Assert.That(applyChangesRequired, Is.True);
            await CancelChangesAsync(second).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-12 §7.10.10: a regenerated key pair requires a Nonce of at
        /// least 32 bytes.
        /// </summary>
        [TestCase(0)]
        [TestCase(16)]
        [TestCase(31)]
        public async Task SigningRequestWithRegeneratedKeyRequiresLongNonceAsync(int nonceLength)
        {
            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate current = Certificate.FromRawData(
                session.ConfiguredEndpoint.Description.ServerCertificate);
            ByteString nonce = nonceLength == 0 ? default : CreateNonce(nonceLength).ToByteString();

            await AssertStatusAsync(
                () => ServerConfiguration(session).CreateSigningRequestAsync(
                    AppGroup(session), RsaType(session), current.Subject, true, nonce).AsTask(),
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        /// <summary>
        /// OPC 10000-12 §7.8.2.7: a CA that is still needed to validate an
        /// issuer of the TrustList cannot be removed; removing a CA removes its
        /// CRLs as well.
        /// </summary>
        [Test]
        public async Task RemoveCertificateProtectsChainsAndRemovesCrlsAsync()
        {
            ISession session = await OpenAdminSessionAsync().ConfigureAwait(false);
            using Certificate root = CreateCa("CN=Push Remove Root");
            X509CRL rootCrl = DefaultCertificateIssuer.Instance.RevokeCertificates(root, null, null);
            using Certificate intermediate = CertificateBuilder
                .Create("CN=Push Remove Intermediate " + Guid.NewGuid().ToString("N")[..8])
                .SetCAConstraint(0)
                .SetIssuer(root)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            X509CRL intermediateCrl = DefaultCertificateIssuer.Instance.RevokeCertificates(intermediate, null, null);
            await PushTrustListAsync(session, new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = new[] { root.RawData.ToByteString() },
                TrustedCrls = new[] { rootCrl.RawData.ToByteString() },
                IssuerCertificates = new[] { intermediate.RawData.ToByteString() },
                IssuerCrls = new[] { intermediateCrl.RawData.ToByteString() }
            }).ConfigureAwait(false);
            await ApplyChangesAsync(session).ConfigureAwait(false);

            session = await OpenAdminSessionAsync().ConfigureAwait(false);
            await AssertStatusAsync(
                () => AppTrustList(session).RemoveCertificateAsync(root.Thumbprint, true).AsTask(),
                StatusCodes.BadCertificateChainIncomplete).ConfigureAwait(false);

            await AppTrustList(session).RemoveCertificateAsync(intermediate.Thumbprint, false).ConfigureAwait(false);
            await ApplyChangesAsync(session).ConfigureAwait(false);

            session = await OpenAdminSessionAsync().ConfigureAwait(false);
            TrustListDataType read = await ReadTrustListAsync(session, (uint)TrustListMasks.All).ConfigureAwait(false);
            Assert.That(read.IssuerCertificates.Count, Is.Zero);
            Assert.That(read.IssuerCrls.Count, Is.Zero, "the CRLs of the removed CA must be removed as well");
            Assert.That(read.TrustedCertificates.Count, Is.EqualTo(1));
        }

        private const byte WriteEraseExisting = (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting);

        private async Task<ISession> OpenAdminSessionAsync()
        {
            ConfiguredEndpoint endpoint = m_pushClient.PushClient.Endpoint;
            var factory = new DefaultSessionFactory(m_telemetry);
            ISession session = await factory.CreateAsync(
                m_pushClient.Config,
                endpoint,
                false,
                false,
                "PushComplianceTests",
                60000,
                m_pushClient.SysAdminUser,
                default,
                CancellationToken.None).ConfigureAwait(false);
            if (!session.Factory.ContainsEncodeableType(OpcUa.DataTypeIds.TrustListDataType))
            {
                session.Factory.Builder.AddOpcUaGds().Commit();
            }
            m_sessions.Add(session);
            return session;
        }

        private async Task CloseSessionsAsync()
        {
            foreach (ISession session in m_sessions)
            {
                try
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // a session dropped by ApplyChanges cannot be closed cleanly
                }
                session.Dispose();
            }
            m_sessions.Clear();
        }

        private TrustListTypeClient AppTrustList(ISession session)
        {
            return new TrustListTypeClient(
                session,
                ExpandedNodeId.ToNodeId(
                    OpcUa.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList,
                    session.NamespaceUris),
                m_telemetry);
        }

        private ServerConfigurationTypeClient ServerConfiguration(ISession session)
        {
            return new ServerConfigurationTypeClient(
                session,
                ExpandedNodeId.ToNodeId(OpcUa.ObjectIds.ServerConfiguration, session.NamespaceUris),
                m_telemetry);
        }

        private static NodeId AppGroup(ISession session)
        {
            return ExpandedNodeId.ToNodeId(
                OpcUa.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                session.NamespaceUris);
        }

        private static NodeId RsaType(ISession session)
        {
            return ExpandedNodeId.ToNodeId(
                OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                session.NamespaceUris);
        }

        private Task ApplyChangesAsync(ISession session)
        {
            return ServerConfiguration(session).ApplyChangesAsync().AsTask();
        }

        private async Task CancelChangesAsync(ISession session)
        {
            try
            {
                await ServerConfiguration(session).CancelChangesAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // nothing to cancel
            }
        }

        private async Task PushTrustListAsync(ISession session, TrustListDataType trustList)
        {
            TrustListTypeClient client = AppTrustList(session);
            uint handle = await client.OpenAsync(WriteEraseExisting).ConfigureAwait(false);
            try
            {
                await client.WriteAsync(handle, Encode(session, trustList).ToByteString()).ConfigureAwait(false);
            }
            catch
            {
                await client.CloseAsync(handle).ConfigureAwait(false);
                throw;
            }
            await client.CloseAndUpdateAsync(handle).ConfigureAwait(false);
        }

        private async Task<TrustListDataType> ReadTrustListAsync(ISession session, uint masks)
        {
            TrustListTypeClient client = AppTrustList(session);
            uint handle = await client.OpenWithMasksAsync(masks).ConfigureAwait(false);
            using var stream = new MemoryStream();
            try
            {
                while (true)
                {
                    ByteString chunk = await client.ReadAsync(handle, 4096).ConfigureAwait(false);
                    if (chunk.IsEmpty)
                    {
                        break;
                    }
                    stream.Write(chunk.ToArray(), 0, chunk.Length);
                }
            }
            finally
            {
                await client.CloseAsync(handle).ConfigureAwait(false);
            }
            stream.Position = 0;
            var trustList = new TrustListDataType();
            using var decoder = new BinaryDecoder(stream, session.MessageContext);
            trustList.Decode(decoder);
            return trustList;
        }

        private static byte[] Encode(ISession session, TrustListDataType trustList)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, session.MessageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            return stream.ToArray();
        }

        private async Task<Certificate> CreateServerCertificateAsync(ISession session, Certificate issuer)
        {
            using Certificate current = Certificate.FromRawData(
                session.ConfiguredEndpoint.Description.ServerCertificate);
            await Task.CompletedTask.ConfigureAwait(false);
            return CertificateBuilder.Create(current.SubjectName)
                .AddExtension(new X509SubjectAltNameExtension(
                    m_server.Config.ApplicationUri,
                    X509Utils.GetDomainsFromCertificate(current).ToArray()))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(12)
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static byte[] CreateNonce(int length)
        {
            byte[] nonce = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(nonce);
            return nonce;
        }

        private static Certificate CreateSelfSigned()
        {
            return CertificateBuilder
                .Create("CN=Push Compliance " + Guid.NewGuid().ToString("N")[..8])
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

        private static async Task AssertStatusAsync(Func<Task> call, StatusCode expected)
        {
            ServiceResultException error = null;
            try
            {
                await call().ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                error = ex;
            }
            Assert.That(error, Is.Not.Null, $"expected {expected}");
            Assert.That(error.StatusCode, Is.EqualTo(expected), error.Message);
        }
    }
}
