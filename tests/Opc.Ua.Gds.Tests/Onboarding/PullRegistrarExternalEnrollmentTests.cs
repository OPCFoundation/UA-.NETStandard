// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FdoOnboarding;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using OnboardingClientSample;
using OnboardingRegistrar;
using Opc.Ua.Gds.Fdo;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    public sealed partial class PullRegistrarIntegrationTests
    {
        /// <summary>
        /// A completed external enrollment can install a DCA key pair and use it on a fresh encrypted UA channel.
        /// The test supplies the role's authenticated evidence and completion, not an FDO wire exchange.
        /// </summary>
        [Test]
        public async Task CommittedExternalEnrollmentAuthenticatesOnFreshChannel()
        {
            string root = Path.Combine(m_root, "external-enrollment");
            var configuration = new CertificateGroupConfiguration
            {
                SubjectName = "CN=External Enrollment CA",
                BaseStorePath = root,
                CertificateTypes = [nameof(CoreObjectTypeIds.RsaSha256ApplicationCertificateType)]
            };
            using var factory = new CertificateGroup(m_telemetry);
            using ICertificateGroup group = factory.Create(Path.Combine(root, "authorities"), configuration);
            await group.InitAsync().ConfigureAwait(false);
            var issuer = (ICertificateGroupWithAuthenticatedSigningRequest)group;
            ByteString deviceGuid = ByteString.From(Guid.NewGuid().ToByteArray(bigEndian: true));
            ByteString attestation = GetIdentity(m_signer);
            ByteString attestationPin = ByteString.From(SHA256.HashData(attestation.Span));
            using var receipts = new FdoSampleReceiptStore(Path.Combine(root, "receipts"), m_product, m_product);
            var policy = new FdoRegistrarEnrollmentPolicy(
                receipts, new PullRegistrarPolicy(m_product, m_product),
                m_product, m_product, "CN=Externally Enrolled DCA",
                deviceGuid, attestationPin, issuer);
            ICertificateManager registrarCertificates = m_provider.GetRequiredService<ICertificateManager>();
            TrustListIdentifier registrarTrust = ExternalAuthenticationProvider.AuthenticationAuthorities;
            registrarCertificates.RegisterTrustList(registrarTrust, configuration.TrustedListPath,
                configuration.IssuerListPath);
            var provider = new ExternalAuthenticationProvider(registrarCertificates, policy);
            m_externalAuthentication.Setup(value => value.EvaluateAsync(
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((ByteString certificate, CancellationToken ct) => provider.EvaluateAsync(certificate, ct));

            var credentials = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
            using CertificateEntry key = m_device.Config.CertificateManager!.AcquireApplicationCertificateByType(
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType)!;
            ByteString csr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(key.Certificate));
            using TrustListData authority = await registrarCertificates.ReadTrustListAsync(
                registrarTrust, TrustListMasks.All).ConfigureAwait(false);
            await TrustEnrollmentAuthorityForChannelAsync(authority).ConfigureAwait(false);
            var enrollmentTrust = new CertificateValidationScope(
                authority.TrustedCertificates.Select(cert => ByteString.From(cert.RawData.AsSpan())).ToArrayOf(),
                authority.IssuerCertificates.Select(cert => ByteString.From(cert.RawData.AsSpan())).ToArrayOf(),
                authority.TrustedCrls.Concat(authority.IssuerCrls)
                    .Select(crl => ByteString.From(crl.RawData.AsSpan())).ToArrayOf());

            Guid run = Guid.NewGuid();
            ByteString issued = await policy.AuthorizeAndIssueAsync(run, deviceGuid, attestation, csr)
                .ConfigureAwait(false);
            await credentials.ValidateExternalCredentialAsync(issued, key.Certificate, enrollmentTrust)
                .ConfigureAwait(false);
            Assert.That(GetIdentity(m_device), Is.EqualTo(m_identity));
            await policy.CommitAsync(run, deviceGuid, attestation, csr, issued).ConfigureAwait(false);
            await credentials.InstallCompletedExternalCredentialAsync(issued, key.Certificate,
                authority.TrustedCertificates).ConfigureAwait(false);
            Assert.That(GetIdentity(m_device), Is.EqualTo(issued));

            ISession session = await ConnectAsync(m_device).ConfigureAwait(false);
            try
            {
                Assert.That(await Registrar(session).GetManagersAsync().ConfigureAwait(false), Is.Empty);
                DeviceRegistrarIdentityCandidate? enrolled = await m_lifecycle.FindCandidateByCertificateAsync(issued)
                    .ConfigureAwait(false);
                Assert.That(enrolled, Is.Not.Null);
                Assert.That(enrolled!.ProductInstanceUri, Is.EqualTo(m_product));
                m_externalAuthentication.Verify(value => value.EvaluateAsync(
                    issued, It.IsAny<CancellationToken>()), Times.Once);
                m_policy.VerifyNoOtherCalls();
            }
            finally
            {
                await CloseSessionAsync(session).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Provisions the registrar's ordinary channel trust independently of its external admission policy.
        /// </summary>
        private async Task TrustEnrollmentAuthorityForChannelAsync(TrustListData authority)
        {
            var trusted = new CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(m_root, "registrar", "trusted")
            };
            using ICertificateStore peers = trusted.OpenStore(m_telemetry);
            foreach (Certificate anchor in authority.TrustedCertificates)
            {
                await peers.AddAsync(anchor).ConfigureAwait(false);
            }
            foreach (X509CRL crl in authority.TrustedCrls)
            {
                await peers.AddCRLAsync(crl).ConfigureAwait(false);
            }
        }
    }
}
#endif
