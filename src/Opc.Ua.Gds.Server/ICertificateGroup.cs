// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server
{
    public class X509Certificate2KeyPair
    {
        public Certificate Certificate { get; }
        public string PrivateKeyFormat { get; }
        public ByteString PrivateKey { get; }

        public X509Certificate2KeyPair(
            Certificate certificate,
            string privateKeyFormat,
            ByteString privateKey)
        {
            if (certificate.HasPrivateKey)
            {
                certificate = Certificate.FromRawData(certificate.RawData);
            }
            Certificate = certificate;
            PrivateKeyFormat = privateKeyFormat;
            PrivateKey = privateKey;
        }
    }

    /// <summary>
    /// An abstract interface to the certificate provider
    /// </summary>
    public interface ICertificateGroup : IDisposable
    {
        NodeId Id { get; set; }
        ArrayOf<NodeId> CertificateTypes { get; set; }
        ConcurrentDictionary<NodeId, Certificate?> Certificates { get; }
        CertificateGroupConfiguration Configuration { get; }
        CertificateStoreIdentifier AuthoritiesStore { get; }
        CertificateStoreIdentifier? IssuerCertificatesStore { get; }
        TrustListState DefaultTrustList { get; set; }
        bool UpdateRequired { get; set; }

        ICertificateGroup Create(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            [Optional] string? issuerCertificatesStorePath);

        Task InitAsync(CancellationToken ct = default);

        Task<Certificate> CreateCACertificateAsync(
            string subjectName,
            NodeId certificateType,
            CancellationToken ct = default);

        Task<X509CRL> RevokeCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default);

        Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            ByteString certificateRequest,
            CancellationToken ct = default);

        /// <summary>
        /// Verifies a signing request for an application and checks that
        /// the public key of the CSR can be issued as a certificate of
        /// <paramref name="certificateType"/>.
        /// </summary>
        /// <remarks>
        /// OPC 10000-12 §7.9.3: StartSigningRequest returns
        /// Bad_InvalidArgument when the CSR does not fit the
        /// CertificateTypeId and Bad_NotSupported when the key algorithm
        /// or size is not supported.
        /// </remarks>
        /// <param name="application">The application the CSR is for.</param>
        /// <param name="certificateType">The requested certificate type.</param>
        /// <param name="certificateRequest">The DER encoded PKCS#10 request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// The CSR is invalid or its key does not match the certificate type.
        /// </exception>
        Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            ByteString certificateRequest,
            CancellationToken ct = default);

        Task<Certificate> SigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string[] domainNames,
            ByteString certificateRequest,
            CancellationToken ct = default);

        Task<X509Certificate2KeyPair> NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword,
            CancellationToken ct = default);
    }

    /// <summary>
    /// An optional certificate group capability for issuing explicitly approved URI subject alternative names.
    /// </summary>
    public interface ICertificateGroupWithApprovedUriSans : ICertificateGroup
    {
        /// <summary>
        /// Issues a certificate containing the application's URI and the approved additional URI SANs.
        /// </summary>
        /// <param name="application">The application the CSR is for.</param>
        /// <param name="certificateType">The requested certificate type.</param>
        /// <param name="domainNames">Fallback domain names when the CSR has no DNS or IP SANs.</param>
        /// <param name="certificateRequest">The DER encoded PKCS#10 request.</param>
        /// <param name="approvedAdditionalUriSans">
        /// Additional URI SANs explicitly approved by the host.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The issued certificate.</returns>
        /// <remarks>
        /// URI SANs from the CSR that are not in <paramref name="approvedAdditionalUriSans"/> are not copied.
        /// Approved URI SANs may be added even when they are not present in the CSR.
        /// </remarks>
        Task<Certificate> SigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string[] domainNames,
            ByteString certificateRequest,
            ArrayOf<string> approvedAdditionalUriSans,
            CancellationToken ct = default);
    }

    /// <summary>
    /// An optional certificate group capability for issuing a CSR after authenticated host approval of its identity.
    /// </summary>
    public interface ICertificateGroupWithAuthenticatedSigningRequest : ICertificateGroup
    {
        /// <summary>
        /// Issues a CSR for authenticated FDO evidence using only the explicitly approved subject and SAN values.
        /// </summary>
        /// <param name="certificateType">The requested certificate type.</param>
        /// <param name="approvedSubjectName">The X.500 subject name selected by the host.</param>
        /// <param name="applicationUri">The application URI approved by the host.</param>
        /// <param name="productInstanceUri">The product instance URI approved by the host.</param>
        /// <param name="approvedAdditionalUriSans">Other URI SANs explicitly approved by the host.</param>
        /// <param name="approvedDomainNames">DNS names or IP addresses explicitly approved by the host.</param>
        /// <param name="certificateRequest">The DER encoded PKCS#10 request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The issued certificate.</returns>
        /// <remarks>
        /// The subject and SAN values are taken only from the host-approved arguments, not from the CSR.
        /// The caller must bind these approved values to this exact signed CSR and authenticated FDO evidence,
        /// and decide enrollment or re-enrollment eligibility before calling this method. Re-enrollment
        /// prior-certificate subject/SAN checks and authorization of a requested <c>ChangeSubjectName</c>
        /// under the applicable FDO/RFC requirements are not performed.
        /// </remarks>
        Task<Certificate> SigningRequestForAuthenticatedEvidenceAsync(
            NodeId certificateType,
            string approvedSubjectName,
            string applicationUri,
            string productInstanceUri,
            ArrayOf<string> approvedAdditionalUriSans,
            ArrayOf<string> approvedDomainNames,
            ByteString certificateRequest,
            CancellationToken ct = default);
    }
}
