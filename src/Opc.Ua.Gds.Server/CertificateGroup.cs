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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua.Gds.Server
{
    public class CertificateGroup : ICertificateGroup
    {
        /// <inheritdoc/>
        public NodeId Id { get; set; }

        /// <inheritdoc/>
        public ArrayOf<NodeId> CertificateTypes { get; set; }

        /// <inheritdoc/>
        public CertificateGroupConfiguration Configuration { get; }

        /// <inheritdoc/>
        public ConcurrentDictionary<NodeId, Certificate?> Certificates { get; }

        /// <inheritdoc/>
        public TrustListState DefaultTrustList { get; set; }

        /// <inheritdoc/>
        public bool UpdateRequired { get; set; }

        /// <inheritdoc/>
        public CertificateStoreIdentifier AuthoritiesStore { get; }

        /// <inheritdoc/>
        public CertificateStoreIdentifier? IssuerCertificatesStore { get; }

        /// <summary>
        /// Gets or sets the certificate issuer service.
        /// When set, certificate signing and CRL operations use this interface.
        /// </summary>
        public ICertificateIssuer? CertificateIssuer { get; set; }

        protected string SubjectName { get; }

        [Obsolete("Use CertificateGroup(TelemetryContext) instead")]
        public CertificateGroup()
            : this(null!)
        {
        }

        public CertificateGroup(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateGroup>();
            UpdateRequired = false;
            // Configuration/SubjectName/AuthoritiesStore/Certificates are populated by the
            // protected constructor used by Create(); the parameterless ctor is a factory shim.
            Configuration = null!;
            SubjectName = null!;
            AuthoritiesStore = null!;
            Certificates = new ConcurrentDictionary<NodeId, Certificate?>();
            DefaultTrustList = null!;
        }

        protected CertificateGroup(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            ITelemetryContext telemetry,
            [Optional] string? trustedIssuerCertificatesStorePath)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateGroup>();
            // DefaultTrustList is assigned by the consumer (ApplicationsNodeManager.SetCertificateGroupNodes).
            DefaultTrustList = null!;

            AuthoritiesStore = new CertificateStoreIdentifier(authoritiesStorePath, false);
            Configuration = certificateGroupConfiguration;
            if (trustedIssuerCertificatesStorePath != null)
            {
                IssuerCertificatesStore = new CertificateStoreIdentifier(
                    trustedIssuerCertificatesStorePath);
            }
            SubjectName = Configuration.SubjectName!
                .Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);
            CertificateTypes = [];

            Certificates = new ConcurrentDictionary<NodeId, Certificate?>();

            foreach (string certificateTypeString in Configuration.CertificateTypes)
            {
                if (Ua.ObjectTypeIds.TryGetValue(certificateTypeString, out NodeId certificateType))
                {
                    if (!Utils.IsSupportedCertificateType(certificateType))
                    {
                        m_logger.CertificateTypeNotSupported(certificateType);
                        continue;
                    }

                    CertificateTypes = CertificateTypes.AddItem(certificateType);
                    Certificates.TryAdd(certificateType, null);
                }
                else
                {
                    throw new NotImplementedException(
                        $"Unknown certificate type {certificateTypeString}. Use ApplicationCertificateType, HttpsCertificateType or UserCredentialCertificateType");
                }
            }
            if (CertificateTypes.IsEmpty)
            {
                throw new ArgumentException("Please specify at least one valid Certificate Type");
            }
        }

        public virtual async Task InitAsync(CancellationToken ct = default)
        {
            m_logger.InitializeCertificateGroup(SubjectName);

            ICertificateStore store = AuthoritiesStore.OpenStore(m_telemetry);
            try
            {
                using CertificateCollection certificates = await store.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                foreach (Certificate certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.Subject, SubjectName))
                    {
                        if (!X509Utils.IsECDsaSignature(certificate) &&
                            X509Utils.GetRSAPublicKeySize(certificate) != Configuration
                                .CACertificateKeySize)
                        {
                            continue;
                        }

                        // TODO check hash size

                        NodeId certificateType = CertificateIdentifier.GetCertificateType(
                            certificate);

                        if (CertificateTypes.Contains(c => c == certificateType))
                        {
                            if (Certificates[certificateType] != null)
                            {
                                // always use latest issued cert in store
                                if (certificate.NotBefore > DateTime.UtcNow ||
                                    Certificates[certificateType]!.NotBefore > certificate.NotBefore)
                                {
                                    continue;
                                }
                            }
                            SetCertificate(certificateType, certificate.AddRef());
                        }
                    }
                }
            }
            finally
            {
                store?.Dispose();
            }

            foreach (KeyValuePair<NodeId, Certificate?> keyValuePair in Certificates)
            {
                Certificate? certificate = keyValuePair.Value;
                NodeId certificateType = keyValuePair.Key;

                if (certificate == null)
                {
                    m_logger.CreateNewCaCertificate(
                        SubjectName,
                        certificateType,
                        Configuration.CACertificateKeySize,
                        Configuration.CACertificateHashSize,
                        Configuration.CACertificateLifetime);
                    using Certificate createdCertificate =
                        await CreateCACertificateAsync(SubjectName, certificateType, ct).ConfigureAwait(
                            false);
                    m_logger.CreatedCaCertificate(Certificates[certificateType]);
                }
            }
        }

        public virtual ICertificateGroup Create(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            [Optional] string? issuerCertificatesStorePath)
        {
            return new CertificateGroup(
                authoritiesStorePath,
                certificateGroupConfiguration,
                m_telemetry,
                issuerCertificatesStorePath);
        }

        /// <summary>
        /// Create a certificate with a new key pair signed by the CA of the cert group.
        /// </summary>
        /// <param name="application">The application record.</param>
        /// <param name="certificateType">The certificate type to create.</param>
        /// <param name="subjectName">The subject of the certificate.</param>
        /// <param name="domainNames">The domain names for the subject alt name extension.</param>
        /// <param name="privateKeyFormat">The private key format as PFX or PEM.</param>
        /// <param name="privateKeyPassword">A password for the private key.</param>
        /// <param name="ct"></param>
        /// <exception cref="ArgumentNullException"><paramref name="application"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task<X509Certificate2KeyPair> NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword,
            CancellationToken ct = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (application.ApplicationUri == null)
            {
                throw new ArgumentNullException(nameof(application), "ApplicationUri is null");
            }

            using Certificate signingKey = await LoadSigningKeyAsync(
                Certificates[certificateType]!,
                null,
                m_telemetry,
                ct)
                .ConfigureAwait(false);

            ICertificateBuilderIssuer builder = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    application.ApplicationUri,
                    application.ApplicationNames.Count > 0
                        ? application.ApplicationNames[0].Text!
                        : "ApplicationName",
                    subjectName,
                    domainNames)
                .SetIssuer(signingKey);

            using Certificate certificate = TryGetECCCurve(certificateType, out ECCurve curve)
                ? builder.SetECCurve(curve).CreateForECDsa()
                : builder.CreateForRSA();

            ByteString privateKey;
            if (privateKeyFormat == "PFX")
            {
                if (privateKeyPassword == null || privateKeyPassword.Length == 0)
                {
                    privateKey = ByteString.From(certificate.Export(X509ContentType.Pfx));
                }
                else
                {
                    privateKey = ByteString.From(certificate.Export(X509ContentType.Pfx, privateKeyPassword));
                }
            }
            else if (privateKeyFormat == "PEM")
            {
                privateKey = ByteString.From(
                    PEMWriter.ExportPrivateKeyAsPEM(certificate, privateKeyPassword));
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Invalid private key format");
            }

            var publicKey = Certificate.FromRawData(certificate.RawData);

            return new X509Certificate2KeyPair(publicKey, privateKeyFormat, privateKey);
        }

        public virtual async Task<X509CRL> RevokeCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default)
        {
            X509CRL crl = await RevokeCertificateAsync(
                AuthoritiesStore, certificate, null, m_telemetry,
                CertificateIssuer, ct)
                .ConfigureAwait(false);

            // Also update TrustedList CRL so registerd Applications can get the new CRL
            if (crl != null)
            {
                var certificateStoreIdentifier = new CertificateStoreIdentifier(
                    Configuration.TrustedListPath);
                await UpdateAuthorityCertInCertificateStoreAsync(certificateStoreIdentifier, ct)
                    .ConfigureAwait(false);

                //Also update TrustedIssuerCertificates Store
                if (IssuerCertificatesStore != null)
                {
                    await UpdateAuthorityCertInCertificateStoreAsync(IssuerCertificatesStore, ct)
                        .ConfigureAwait(false);
                }
            }

            // return crl
            return crl!;
        }

        public virtual Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            ByteString certificateRequest,
            CancellationToken ct = default)
        {
            try
            {
                var pkcs10CertificationRequest = new Pkcs10CertificationRequest(certificateRequest.ToArray());

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "CSR signature invalid.");
                }

                // OPC 10000-12 §7.9.3: the ApplicationUri shall be specified
                // in the CSR, so a CSR without a SubjectAltName URI is rejected.
                X509SubjectAltNameExtension? altNameExtension =
                    Pkcs10Utils.GetSubjectAltNameExtension(pkcs10CertificationRequest.Attributes);
                if (altNameExtension == null ||
                    !altNameExtension.Uris.Contains(application.ApplicationUri))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateUriInvalid,
                        "CSR AltNameExtension does not match " + application.ApplicationUri);
                }
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ex is not ServiceResultException)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }
        }

        /// <summary>
        /// Verifies a signing request as
        /// <see cref="VerifySigningRequestAsync(ApplicationRecordDataType, ByteString, CancellationToken)"/>
        /// does, and additionally checks that the public key of the CSR
        /// can be issued as a certificate of <paramref name="certificateType"/>.
        /// </summary>
        /// <remarks>
        /// OPC 10000-12 §7.9.3 requires <c>StartSigningRequest</c> to reject
        /// such a request: Bad_InvalidArgument when the CSR does not fit the
        /// CertificateTypeId, Bad_NotSupported when the key algorithm or size
        /// is not supported. Without this check the mismatch only surfaced in
        /// <c>FinishRequest</c>.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// The CSR is invalid or its key does not match the certificate type.
        /// </exception>
        public virtual async Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            ByteString certificateRequest,
            CancellationToken ct = default)
        {
            await VerifySigningRequestAsync(application, certificateRequest, ct).ConfigureAwait(false);
            VerifySigningRequestKey(certificateType, certificateRequest);
        }

        /// <summary>
        /// Checks that the public key of a PKCS#10 signing request matches
        /// the key algorithm, curve and size of a certificate type.
        /// </summary>
        /// <param name="certificateType">The requested certificate type.</param>
        /// <param name="certificateRequest">The DER encoded PKCS#10 request.</param>
        /// <exception cref="ServiceResultException">
        /// Bad_InvalidArgument when the request cannot be parsed or its key
        /// does not match the type; Bad_NotSupported when the key algorithm
        /// or size cannot be issued for the type.
        /// </exception>
        public static void VerifySigningRequestKey(
            NodeId certificateType,
            ByteString certificateRequest)
        {
            string keyAlgorithm;
            string? curve;
            int rsaKeySize;
            try
            {
                var request = new Pkcs10CertificationRequest(certificateRequest.ToArray());
                (keyAlgorithm, curve, rsaKeySize) = ReadPublicKeyInfo(request.SubjectPublicKeyInfo);
            }
            catch (Exception ex) when (ex is CryptographicException or AsnContentException)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateRequest public key cannot be decoded: " + ex.Message);
            }

            if (IsRsaCertificateType(certificateType))
            {
                if (keyAlgorithm != RsaEncryptionOid)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        CoreUtils.Format(
                            "The CertificateRequest has a {0} public key, but the CertificateTypeId {1} requires an RSA key.",
                            DescribeKey(keyAlgorithm, curve),
                            certificateType));
                }

                // OPC 10000-12 §7.8.4.8 / §7.8.4.9 key sizes.
                (int minKeySize, int maxKeySize) =
                    certificateType == Ua.ObjectTypeIds.RsaMinApplicationCertificateType
                        ? (1024, 2048)
                        : certificateType == Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                            ? (2048, 4096)
                            : (0, int.MaxValue);
                if (rsaKeySize < minKeySize || rsaKeySize > maxKeySize)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        CoreUtils.Format(
                            "The CertificateRequest has an RSA key of {0} bits, but the CertificateTypeId {1} requires {2} to {3} bits.",
                            rsaKeySize,
                            certificateType,
                            minKeySize,
                            maxKeySize));
                }
                return;
            }

            if (!TryGetEccCurveOids(certificateType, out string[]? curveOids))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    CoreUtils.Format(
                        "The CertificateTypeId {0} is not supported for signing requests.",
                        certificateType));
            }

            if (keyAlgorithm != EcPublicKeyOid)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    CoreUtils.Format(
                        "The CertificateRequest has a {0} public key, but the CertificateTypeId {1} requires an ECC key.",
                        DescribeKey(keyAlgorithm, curve),
                        certificateType));
            }

            if (curve == null || Array.IndexOf(curveOids!, curve) < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    CoreUtils.Format(
                        "The CertificateRequest has a {0} public key, but the CertificateTypeId {1} requires the curve {2}.",
                        DescribeKey(keyAlgorithm, curve),
                        certificateType,
                        string.Join(" or ", curveOids!.Select(DescribeCurve))));
            }
        }

        public virtual async Task<Certificate> SigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string[] domainNames,
            ByteString certificateRequest,
            CancellationToken ct = default)
        {
            try
            {
                var pkcs10CertificationRequest = new Pkcs10CertificationRequest(certificateRequest.ToArray());

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "CSR signature invalid.");
                }

                X509SubjectAltNameExtension? altNameExtension =
                    Pkcs10Utils.GetSubjectAltNameExtension(pkcs10CertificationRequest.Attributes);
                if (altNameExtension == null)
                {
                    // OPC 10000-12 §7.9.3: the ApplicationUri shall be specified in the CSR.
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateUriInvalid,
                        "CSR has no AltNameExtension with the ApplicationUri " + application.ApplicationUri);
                }
                else
                {
                    if (!altNameExtension.Uris.Contains(application.ApplicationUri))
                    {
                        var applicationUriMissing = new StringBuilder();
                        applicationUriMissing.AppendLine(
                            "Expected AltNameExtension (ApplicationUri):")
                            .AppendLine(application.ApplicationUri)
                            .AppendLine("CSR AltNameExtensions found:");
                        foreach (string uri in altNameExtension.Uris)
                        {
                            applicationUriMissing.AppendLine(uri);
                        }
                        throw new ServiceResultException(
                            StatusCodes.BadCertificateUriInvalid,
                            applicationUriMissing.ToString());
                    }

                    if (altNameExtension.IPAddresses.Count > 0 ||
                        altNameExtension.DomainNames.Count > 0)
                    {
                        var domainNameList = new List<string>();
                        domainNameList.AddRange(altNameExtension.DomainNames);
                        domainNameList.AddRange(altNameExtension.IPAddresses);
                        domainNames = [.. domainNameList];
                    }
                }

                // a request queued before the key check existed, or one a
                // custom group verified without it, still fails here with the
                // exact reason rather than a public key decoding error.
                VerifySigningRequestKey(certificateType, certificateRequest);

                DateTime yesterday = DateTime.Today.AddDays(-1);
                using Certificate signingKey = await LoadSigningKeyAsync(
                    Certificates[certificateType]!,
                    null,
                    m_telemetry,
                    ct)
                    .ConfigureAwait(false);
                X500DistinguishedName subjectName = pkcs10CertificationRequest.Subject;

                ICertificateBuilder builder = CertificateBuilder
                    .Create(subjectName)
                    .AddExtension(
                        new X509SubjectAltNameExtension(application.ApplicationUri!, domainNames))
                    .SetNotBefore(yesterday)
                    .SetLifeTime(Configuration.DefaultCertificateLifetime);

                return TryGetECCCurve(certificateType, out ECCurve curve)
                    ? builder
                        .SetIssuer(signingKey)
                        .SetECDsaPublicKey(pkcs10CertificationRequest.SubjectPublicKeyInfo)
                        .CreateForECDsa()
                    : builder
                        .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(
                            Configuration.DefaultCertificateHashSize))
                        .SetIssuer(signingKey)
                        .SetRSAPublicKey(pkcs10CertificationRequest.SubjectPublicKeyInfo)
                        .CreateForRSA();
            }
            catch (Exception ex) when (ex is not ServiceResultException)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }
        }

        public virtual async Task<Certificate> CreateCACertificateAsync(
            string subjectName,
            NodeId certificateType,
            CancellationToken ct = default)
        {
            // validate new subjectName matches the previous subject
            // TODO: An issuer may modify the subject of the CA certificate,
            // but then the configuration must be updated too!
            // NOTE: not a strict requirement here for ASN.1 byte compare
            if (!X509Utils.CompareDistinguishedName(subjectName, SubjectName))
            {
                throw new ArgumentException(
                    "SubjectName provided does not match the SubjectName property of the CertificateGroup \n" +
                    "CA Certificate is not created until the subjectName " +
                    SubjectName +
                    " is provided",
                    subjectName);
            }

            if (certificateType.IsNull)
            {
                throw new ArgumentNullException(nameof(certificateType));
            }

            DateTime yesterday = DateTime.Today.AddDays(-1);
            ICertificateBuilder builder = DefaultCertificateFactory.Instance
                .CreateCertificate(subjectName)
                .SetNotBefore(yesterday)
                .SetLifeTime(Configuration.CACertificateLifetime)
                .SetCAConstraint();

            using Certificate certificate = TryGetECCCurve(certificateType, out ECCurve curve)
                ? builder.SetECCurve(curve).CreateForECDsa()
                : builder
                    .SetHashAlgorithm(
                        X509Utils.GetRSAHashAlgorithmName(Configuration.CACertificateHashSize))
                    .SetRSAKeySize(Configuration.CACertificateKeySize)
                    .CreateForRSA();

            await certificate.AddToStoreAsync(
                AuthoritiesStore,
                password: null,
                m_telemetry,
                ct).ConfigureAwait(false);

            // save only public key
            SetCertificate(certificateType, Certificate.FromRawData(certificate.RawData));

            // initialize revocation list
            X509CRL initialCrl = await LoadCrlCreateEmptyIfNonExistantAsync(certificate, AuthoritiesStore, m_telemetry, ct: ct).ConfigureAwait(false);

            //Update TrustedList Store
            await initialCrl.AddToStoreAsync(AuthoritiesStore, m_telemetry, ct).ConfigureAwait(false);
            // TODO: make CA trust selectable
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                Configuration.TrustedListPath);
            await UpdateAuthorityCertInCertificateStoreAsync(certificateStoreIdentifier, ct)
                .ConfigureAwait(false);

            // Update TrustedIssuerCertificates Store
            if (IssuerCertificatesStore != null)
            {
                await UpdateAuthorityCertInCertificateStoreAsync(IssuerCertificatesStore, ct)
                    .ConfigureAwait(false);
            }

            return Certificates[certificateType]!.AddRef();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases certificates owned by the group.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (Certificate? certificate in Certificates.Values)
            {
                certificate?.Dispose();
            }

            Certificates.Clear();
        }

        private void SetCertificate(NodeId certificateType, Certificate certificate)
        {
            if (Certificates.TryGetValue(certificateType, out Certificate? existing))
            {
                existing?.Dispose();
            }

            Certificates[certificateType] = certificate;
        }

        /// <summary>
        /// load the authority signing key.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task<Certificate> LoadSigningKeyAsync(
            Certificate signingCertificate,
            char[]? signingKeyPassword,
            ITelemetryContext? telemetry = null,
            CancellationToken ct = default)
        {
            // Build a metadata identifier that points the resolver at the
            // authorities store and lets it look up the signing key by the
            // signing certificate's thumbprint/subject. The identifier no
            // longer needs to own the cert (we already have it via the
            // signingCertificate parameter); the resolver takes ownership of
            // the loaded private-key-bearing cert and the caller owns the
            // returned reference.
            var certIdentifier = new CertificateIdentifier
            {
                StorePath = AuthoritiesStore.StorePath,
                StoreType = AuthoritiesStore.StoreType,
                Thumbprint = signingCertificate.Thumbprint,
                SubjectName = signingCertificate.Subject,
                CertificateType = CertificateIdentifier.GetCertificateType(signingCertificate)
            };
            return await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    certIdentifier,
                    new CertificatePasswordProvider(signingKeyPassword),
                    applicationUri: null,
                    telemetry,
                    ct)
                .ConfigureAwait(false)
                ?? throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Failed to load signing key for certificate.");
        }

        /// <summary>
        /// Create an empty Crl for the given ca certificate
        /// </summary>
        /// <param name="caCertificate">CA certificate to search for the according crl for</param>
        /// <param name="thisUpdate">Time the crl will be valid from (defaults to UtcNow)</param>
        /// <param name="nextUpdate">Time until the crl will be valid to (defaults to UtcNow + 12 Months)</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Task<X509CRL> CreateEmptyCrlAsync(Certificate caCertificate, DateTime? thisUpdate = null, DateTime? nextUpdate = null)
        {
            bool isCACert = X509Utils.IsCertificateAuthority(caCertificate);
            if (!isCACert)
            {
                throw new ArgumentException("Cannot create an empty Crl for non-CA certificate!");
            }
            CrlBuilder crlBuilder = CrlBuilder
                .Create(caCertificate.SubjectName)
                .SetThisUpdate(thisUpdate ?? DateTime.UtcNow)
                .SetNextUpdate(nextUpdate ?? DateTime.UtcNow.AddMonths(12))
                .AddCRLExtension(caCertificate.BuildAuthorityKeyIdentifier())
                .AddCRLExtension(X509Extensions.BuildCRLNumber(1));
            if (X509PfxUtils.IsECDsaSignature(caCertificate))
            {
                return Task.FromResult(new X509CRL(crlBuilder.CreateForECDsa(caCertificate)));
            }
            return Task.FromResult(new X509CRL(crlBuilder.CreateForRSA(caCertificate)));
        }

        /// <summary>
        /// Load the crl or newly create an empty one if it does not exist for the CA certificate.
        /// </summary>
        /// <param name="caCertificate">CA certificate to search for the according crl for</param>
        /// <param name="storeIdentifier">Store Identifier - to search/insert the crl for</param>
        /// <param name="telemetry">Telemetry for logging purposes</param>
        /// <param name="thisUpdate">Time the crl will be valid from (defaults to UtcNow)</param>
        /// <param name="nextUpdate">Time until the crl will be valid to (defaults to UtcNow + 12 Months)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Crl for the CA Certificate</returns>
        /// <exception cref="ArgumentException">Non-CA certificates or when no store is provided</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<X509CRL> LoadCrlCreateEmptyIfNonExistantAsync(
            Certificate caCertificate,
            CertificateStoreIdentifier storeIdentifier,
            ITelemetryContext? telemetry = null,
            DateTime? thisUpdate = null,
            DateTime? nextUpdate = null,
            CancellationToken ct = default)
        {
            bool isCACert = X509Utils.IsCertificateAuthority(caCertificate);
            if (!isCACert)
            {
                throw new ArgumentException("Cannot create an empty Crl for non-CA certificate!");
            }
            // telemetry may be null on legacy callers; OpenStore's signature is non-nullable but
            // implementations tolerate null. Forward as-is to preserve existing behavior.
            ICertificateStore store = storeIdentifier.OpenStore(telemetry!) ?? throw new ArgumentException("Invalid store path/type");
            try
            {
                X509CRLCollection certCACrl = await store.EnumerateCRLsAsync(caCertificate, false, ct)
                    .ConfigureAwait(false);
                X509CRL? result = null;
                if (certCACrl == null || certCACrl.Count == 0)
                {
                    result = await CreateEmptyCrlAsync(caCertificate, thisUpdate, nextUpdate).ConfigureAwait(false);
                    await store.AddCRLAsync(result, ct).ConfigureAwait(false);
                }
                else
                {
                    if (certCACrl.Count > 1)
                    {
                        telemetry?.CreateLogger<CertificateGroup>().MultipleCrlsFound(caCertificate.Subject);
                    }
                    result = certCACrl.OrderByDescending(crl => crl.ThisUpdate).FirstOrDefault();
                }
                return result ??
                    throw new ServiceResultException(StatusCodes.BadCertificateIssuerRevocationUnknown,
                        "Issuer Crl should have been created but it seems it was not!");
            }
            finally
            {
                store.Dispose();
            }
        }

        /// <summary>
        /// Revoke the CA signed certificate.
        /// The issuer CA public key, the private key and the crl reside in the storepath.
        /// The CRL number is increased by one and existing CRL for the issuer are deleted
        /// from the store.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<X509CRL> RevokeCertificateAsync(
            CertificateStoreIdentifier storeIdentifier,
            Certificate certificate,
            char[]? issuerKeyFilePassword = null,
            ITelemetryContext? telemetry = null,
            CancellationToken ct = default)
        {
            return RevokeCertificateAsync(
                storeIdentifier, certificate,
                issuerKeyFilePassword, telemetry,
                certificateIssuer: null, ct);
        }

        /// <summary>
        /// Revoke the CA signed certificate with optional certificate issuer support.
        /// The CRL number is increased by one and existing CRL for the issuer are deleted
        /// from the store.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<X509CRL> RevokeCertificateAsync(
            CertificateStoreIdentifier storeIdentifier,
            Certificate certificate,
            char[]? issuerKeyFilePassword,
            ITelemetryContext? telemetry,
            ICertificateIssuer? certificateIssuer,
            CancellationToken ct = default)
        {
            X509CRL? updatedCRL = null;

            _ = X509Utils.IsCertificateAuthority(certificate);

            // find the authority key identifier.
            X509AuthorityKeyIdentifierExtension? authority =
                certificate.FindExtension<X509AuthorityKeyIdentifierExtension>();
            string serialNumber;
            string keyIdentifier;
            if (authority != null)
            {
                serialNumber = authority.SerialNumber;
                keyIdentifier = authority.KeyIdentifier;
            }
            else
            {
                throw new ArgumentException("Certificate does not contain an Authority Key");
            }

            if (serialNumber == certificate.SerialNumber || X509Utils.IsSelfSigned(certificate))
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Cannot revoke self signed (root) certificates");
            }

            ICertificateStore store = storeIdentifier.OpenStore(telemetry!);
            try
            {
                if (store == null)
                {
                    throw new ArgumentException("Invalid store path/type");
                }
                using Certificate certCA =
                    await X509Utils
                        .FindIssuerCABySerialNumberAsync(
                            store,
                            certificate.IssuerName,
                            serialNumber)
                        .ConfigureAwait(false)
                    ?? await X509Utils
                        .FindIssuerCAByKeyIdentifierAsync(
                            store,
                            certificate.IssuerName,
                            keyIdentifier)
                        .ConfigureAwait(false)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Cannot find issuer certificate in store.");

                var certCAIdentifier = new CertificateIdentifier
                {
                    StorePath = store.StorePath,
                    StoreType = store.StoreType,
                    Thumbprint = certCA.Thumbprint,
                    SubjectName = certCA.Subject,
                    CertificateType = CertificateIdentifier.GetCertificateType(certCA)
                };
                using Certificate certCAWithPrivateKey =
                    await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                        certCAIdentifier,
                        new CertificatePasswordProvider(issuerKeyFilePassword),
                        applicationUri: null,
                        telemetry,
                        ct)
                        .ConfigureAwait(false)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Failed to load issuer private key. Is the password correct?");

                if (!certCAWithPrivateKey.HasPrivateKey)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Issuer certificate has no private key, cannot revoke certificate.");
                }

                X509CRLCollection certCACrl = await store.EnumerateCRLsAsync(certCA, false, ct)
                    .ConfigureAwait(false);

                using var certificateCollection = new CertificateCollection
                {
                    certificate
                };
                ICertificateIssuer issuer = certificateIssuer ?? DefaultCertificateIssuer.Instance;
                updatedCRL = issuer.RevokeCertificates(
                    certCAWithPrivateKey,
                    certCACrl,
                    certificateCollection);

                await store.AddCRLAsync(updatedCRL, ct).ConfigureAwait(false);

                // delete outdated CRLs from store
                foreach (X509CRL caCrl in certCACrl)
                {
                    await store.DeleteCRLAsync(caCrl, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                store.Dispose();
            }
            return updatedCRL!;
        }

        /// <summary>
        /// GetTheEccCurve of the CertificateGroups CertificateType
        /// </summary>
        /// <returns>returns false if RSA CertificateType, true if a ECCurve can be found, else throws Exception</returns>
        /// <exception cref="ServiceResultException"></exception>
        private static bool TryGetECCCurve(NodeId certificateType, out ECCurve curve)
        {
            curve = default;
            if (IsRsaCertificateType(certificateType))
            {
                return false;
            }
            curve =
                CryptoUtils.GetCurveFromCertificateTypeId(certificateType)
                ?? throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    $"The certificate type {certificateType} is not supported.");
            return true;
        }

        /// <summary>
        /// Checks if the certificate type is issued with an RSA key.
        /// </summary>
        private static bool IsRsaCertificateType(NodeId certificateType)
        {
            return certificateType.IsNull ||
                certificateType == Ua.ObjectTypeIds.ApplicationCertificateType ||
                certificateType == Ua.ObjectTypeIds.HttpsCertificateType ||
                certificateType == Ua.ObjectTypeIds.UserCertificateType ||
                certificateType == Ua.ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateType == Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
        }

        /// <summary>
        /// Gets the named curve OIDs a CSR key may use for an ECC
        /// certificate type. Returns false for a type the group cannot
        /// issue from a signing request.
        /// </summary>
        private static bool TryGetEccCurveOids(NodeId certificateType, out string[]? curveOids)
        {
            if (certificateType == Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType)
            {
                curveOids = [NistP256Oid];
            }
            else if (certificateType == Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType)
            {
                curveOids = [NistP384Oid];
            }
            else if (certificateType == Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                curveOids = [BrainpoolP256r1Oid];
            }
            else if (certificateType == Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                curveOids = [BrainpoolP384r1Oid];
            }
            else if (certificateType == Ua.ObjectTypeIds.EccApplicationCertificateType)
            {
                curveOids = [NistP256Oid, NistP384Oid, BrainpoolP256r1Oid, BrainpoolP384r1Oid];
            }
            else
            {
                curveOids = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reads the key algorithm, the named curve (EC keys) and the
        /// modulus size in bits (RSA keys) of a DER encoded
        /// SubjectPublicKeyInfo (RFC 5280 §4.1.2.7).
        /// </summary>
        private static (string keyAlgorithm, string? curve, int rsaKeySize) ReadPublicKeyInfo(
            byte[] subjectPublicKeyInfo)
        {
            var reader = new AsnReader(subjectPublicKeyInfo, AsnEncodingRules.DER);
            AsnReader spki = reader.ReadSequence();
            AsnReader algorithm = spki.ReadSequence();
            string keyAlgorithm = algorithm.ReadObjectIdentifier();

            string? curve = null;
            if (keyAlgorithm == EcPublicKeyOid &&
                algorithm.HasData &&
                algorithm.PeekTag() == Asn1Tag.ObjectIdentifier)
            {
                curve = algorithm.ReadObjectIdentifier();
            }

            int rsaKeySize = 0;
            if (keyAlgorithm == RsaEncryptionOid)
            {
                byte[] publicKey = spki.ReadBitString(out _);
                AsnReader rsaKey = new AsnReader(publicKey, AsnEncodingRules.DER).ReadSequence();
                ReadOnlySpan<byte> modulus = rsaKey.ReadIntegerBytes().Span;
                while (modulus.Length > 1 && modulus[0] == 0)
                {
                    modulus = modulus[1..];
                }
                rsaKeySize = modulus.Length * 8;
                if (modulus.Length > 0)
                {
                    // count the unused leading bits of the top byte
                    for (int bit = 0x80; bit > 0 && (modulus[0] & bit) == 0; bit >>= 1)
                    {
                        rsaKeySize--;
                    }
                }
            }

            return (keyAlgorithm, curve, rsaKeySize);
        }

        private static string DescribeKey(string keyAlgorithm, string? curve)
        {
            return keyAlgorithm switch
            {
                RsaEncryptionOid => "RSA",
                EcPublicKeyOid => "ECC " + (curve == null ? "(no named curve)" : DescribeCurve(curve)),
                _ => "unsupported (" + keyAlgorithm + ")"
            };
        }

        private static string DescribeCurve(string curveOid)
        {
            return curveOid switch
            {
                NistP256Oid => "nistP256",
                NistP384Oid => "nistP384",
                BrainpoolP256r1Oid => "brainpoolP256r1",
                BrainpoolP384r1Oid => "brainpoolP384r1",
                _ => curveOid
            };
        }

        private const string RsaEncryptionOid = "1.2.840.113549.1.1.1";
        private const string EcPublicKeyOid = "1.2.840.10045.2.1";
        private const string NistP256Oid = "1.2.840.10045.3.1.7";
        private const string NistP384Oid = "1.3.132.0.34";
        private const string BrainpoolP256r1Oid = "1.3.36.3.3.2.8.1.1.7";
        private const string BrainpoolP384r1Oid = "1.3.36.3.3.2.8.1.1.11";

        /// <summary>
        /// Updates the certificate authority certificate and CRL in the provided CertificateStore
        /// </summary>
        /// <param name="trustedOrIssuerStoreIdentifier">The store which contains the authority
        /// ceritificate. (trusted or issuer)</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <exception cref="ServiceResultException"></exception>
        protected async Task UpdateAuthorityCertInCertificateStoreAsync(
            CertificateStoreIdentifier trustedOrIssuerStoreIdentifier,
            CancellationToken ct = default)
        {
            ICertificateStore authorityStore = AuthoritiesStore.OpenStore(m_telemetry);
            ICertificateStore trustedOrIssuerStore = trustedOrIssuerStoreIdentifier.OpenStore(m_telemetry);
            try
            {
                if (authorityStore == null || trustedOrIssuerStore == null)
                {
                    throw new ServiceResultException(
                        "Unable to update authority certificate in stores");
                }

                using CertificateCollection certificates = await authorityStore.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                foreach (Certificate certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.Subject, SubjectName))
                    {
                        using CertificateCollection certs = await trustedOrIssuerStore
                            .FindByThumbprintAsync(certificate.Thumbprint, ct)
                            .ConfigureAwait(false);
                        if (certs.Count == 0)
                        {
                            using var x509 = Certificate.FromRawData(
                                certificate.RawData);
                            await trustedOrIssuerStore.AddAsync(x509, ct: ct).ConfigureAwait(false);
                        }

                        // delete existing CRL in trusted list
                        foreach (
                            X509CRL crl in await trustedOrIssuerStore
                                .EnumerateCRLsAsync(certificate, false, ct)
                                .ConfigureAwait(false))
                        {
                            if (crl.VerifySignature(certificate, false))
                            {
                                await trustedOrIssuerStore.DeleteCRLAsync(crl, ct)
                                    .ConfigureAwait(false);
                            }
                        }

                        // copy latest CRL to trusted list
                        foreach (
                            X509CRL crl in await authorityStore
                                .EnumerateCRLsAsync(certificate, true, ct)
                                .ConfigureAwait(false))
                        {
                            await trustedOrIssuerStore.AddCRLAsync(crl, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            finally
            {
                authorityStore.Dispose();
                trustedOrIssuerStore.Dispose();
            }
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }

    internal static partial class CertificateGroupLog
    {
        [LoggerMessage(EventId = GdsServerCommonEventIds.CertificateGroup + 0, Level = LogLevel.Error,
            Message = "Certificate type {CertificateType} specified for Certificate Group is not supported " +
                "on this platform")]
        public static partial void CertificateTypeNotSupported(this ILogger logger, NodeId certificateType);

        [LoggerMessage(EventId = GdsServerCommonEventIds.CertificateGroup + 1, Level = LogLevel.Information,
            Message = "InitializeCertificateGroup: {SubjectName}")]
        public static partial void InitializeCertificateGroup(this ILogger logger, string? subjectName);

        [LoggerMessage(EventId = GdsServerCommonEventIds.CertificateGroup + 2, Level = LogLevel.Information,
            Message = "Create new CA Certificate: {SubjectName}, CertificateType {CertificateType}  " +
                "KeySize: {KeySize}, " +
                "HashSize: {HashSize}, LifeTime: {LifeTime} months")]
        public static partial void CreateNewCaCertificate(
            this ILogger logger,
            string? subjectName,
            NodeId certificateType,
            ushort keySize,
            ushort hashSize,
            ushort lifeTime);

        [LoggerMessage(EventId = GdsServerCommonEventIds.CertificateGroup + 3, Level = LogLevel.Information,
            Message = "Created CA certificate {Certificate}")]
        public static partial void CreatedCaCertificate(this ILogger logger, Certificate? certificate);

        [LoggerMessage(EventId = GdsServerCommonEventIds.CertificateGroup + 4, Level = LogLevel.Warning,
            Message = "Multiple CRLs found for CA certificate {CertificateSubject}. The most recent one will be used.")]
        public static partial void MultipleCrlsFound(this ILogger logger, string certificateSubject);
    }
}
