/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
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
        public NodeIdCollection CertificateTypes { get; set; }

        /// <inheritdoc/>
        public CertificateGroupConfiguration Configuration { get; }

        /// <inheritdoc/>
        public ConcurrentDictionary<NodeId, X509Certificate2> Certificates { get; }

        /// <inheritdoc/>
        public TrustListState DefaultTrustList { get; set; }

        /// <inheritdoc/>
        public bool UpdateRequired { get; set; }

        /// <inheritdoc/>
        public CertificateStoreIdentifier AuthoritiesStore { get; }

        /// <inheritdoc/>
        public CertificateStoreIdentifier IssuerCertificatesStore { get; }

        protected string SubjectName { get; }

        [Obsolete("Use CertificateGroup(TelemetryContext) instead")]
        public CertificateGroup()
            : this(null)
        {
        }

        public CertificateGroup(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateGroup>();
            UpdateRequired = false;
        }

        protected CertificateGroup(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            ITelemetryContext telemetry,
            [Optional] string trustedIssuerCertificatesStorePath)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateGroup>();

            AuthoritiesStore = new CertificateStoreIdentifier(authoritiesStorePath, false);
            Configuration = certificateGroupConfiguration;
            if (trustedIssuerCertificatesStorePath != null)
            {
                IssuerCertificatesStore = new CertificateStoreIdentifier(
                    trustedIssuerCertificatesStorePath);
            }
            SubjectName = Configuration.SubjectName
                .Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);
            CertificateTypes = [];

            Certificates = new ConcurrentDictionary<NodeId, X509Certificate2>();

            foreach (string certificateTypeString in Configuration.CertificateTypes)
            {
                var certificateType = typeof(Ua.ObjectTypeIds).GetField(certificateTypeString)
                    .GetValue(null) as NodeId;
                if (certificateType != null)
                {
                    if (!Utils.IsSupportedCertificateType(certificateType))
                    {
                        m_logger.LogError(
                            "Certificate type {CertificateType} specified for Certificate Group is not supported on this platform",
                            certificateType);
                        continue;
                    }

                    CertificateTypes.Add(certificateType);
                    Certificates.TryAdd(certificateType, null);
                }
                else
                {
                    throw new NotImplementedException(
                        $"Unknown certificate type {certificateTypeString}. Use ApplicationCertificateType, HttpsCertificateType or UserCredentialCertificateType");
                }
            }
            if (CertificateTypes.Count == 0)
            {
                throw new ArgumentException("Please specify at least one valid Certificate Type");
            }
        }

        public virtual async Task InitAsync(CancellationToken ct = default)
        {
            m_logger.LogInformation("InitializeCertificateGroup: {SubjectName}", SubjectName);

            ICertificateStore store = AuthoritiesStore.OpenStore(m_telemetry);
            try
            {
                X509Certificate2Collection certificates = await store.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                foreach (X509Certificate2 certificate in certificates)
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

                        if (CertificateTypes.Any(c => c == certificateType))
                        {
                            if (Certificates[certificateType] != null)
                            {
                                // always use latest issued cert in store
                                if (certificate.NotBefore > DateTime.UtcNow ||
                                    Certificates[certificateType].NotBefore > certificate.NotBefore)
                                {
                                    continue;
                                }
                            }
                            Certificates[certificateType] = certificate;
                        }
                    }
                }
            }
            finally
            {
                store?.Close();
            }

            foreach (KeyValuePair<NodeId, X509Certificate2> keyValuePair in Certificates)
            {
                X509Certificate2 certificate = keyValuePair.Value;
                NodeId certificateType = keyValuePair.Key;

                if (certificate == null)
                {
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Create new CA Certificate: {SubjectName}, CertificateType {CertificateType}  KeySize: {KeySize}, HashSize: {HashSize}, LifeTime: {LifeTime} months",
                        SubjectName,
                        certificateType,
                        Configuration.CACertificateKeySize,
                        Configuration.CACertificateHashSize,
                        Configuration.CACertificateLifetime);
                    await CreateCACertificateAsync(SubjectName, certificateType, ct).ConfigureAwait(
                        false);
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Created CA certificate {Certificate}",
                        Certificates[certificateType].AsLogSafeString());
                }
            }
        }

        public virtual ICertificateGroup Create(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            [Optional] string trustedIssuerCertificatesStorePath)
        {
            return new CertificateGroup(
                authoritiesStorePath,
                certificateGroupConfiguration,
                m_telemetry,
                trustedIssuerCertificatesStorePath);
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

            if (application.ApplicationNames == null)
            {
                throw new ArgumentNullException(nameof(application), "ApplicationNames is null");
            }

            using X509Certificate2 signingKey = await LoadSigningKeyAsync(
                Certificates[certificateType],
                null,
                m_telemetry,
                ct)
                .ConfigureAwait(false);

            ICertificateBuilderIssuer builder = CertificateFactory
                .CreateCertificate(
                    application.ApplicationUri,
                    application.ApplicationNames.Count > 0
                        ? application.ApplicationNames[0].Text
                        : "ApplicationName",
                    subjectName,
                    domainNames)
                .SetIssuer(signingKey);
#if ECC_SUPPORT
            using X509Certificate2 certificate = TryGetECCCurve(certificateType, out ECCurve curve)
                ? builder.SetECCurve(curve).CreateForECDsa()
                : builder.CreateForRSA();
#else
            using X509Certificate2 certificate = builder.CreateForRSA();
#endif

            byte[] privateKey;
            if (privateKeyFormat == "PFX")
            {
                if (privateKeyPassword == null || privateKeyPassword.Length == 0)
                {
                    privateKey = certificate.Export(X509ContentType.Pfx);
                }
                else
                {
                    using var passwordString = new SecureString();
                    foreach (char c in privateKeyPassword)
                    {
                        passwordString.AppendChar(c);
                    }
                    passwordString.MakeReadOnly();
                    privateKey = certificate.Export(X509ContentType.Pfx, passwordString);
                }
            }
            else if (privateKeyFormat == "PEM")
            {
                privateKey = PEMWriter.ExportPrivateKeyAsPEM(certificate, privateKeyPassword);
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Invalid private key format");
            }

            X509Certificate2 publicKey = CertificateFactory.Create(certificate.RawData);

            return new X509Certificate2KeyPair(publicKey, privateKeyFormat, privateKey);
        }

        public virtual async Task<X509CRL> RevokeCertificateAsync(
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            X509CRL crl = await RevokeCertificateAsync(AuthoritiesStore, certificate, null, m_telemetry, ct)
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
            return crl;
        }

        public virtual Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            byte[] certificateRequest,
            CancellationToken ct = default)
        {
            try
            {
                var pkcs10CertificationRequest
                    = new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(
                    certificateRequest);

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "CSR signature invalid.");
                }

                Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info =
                    pkcs10CertificationRequest.GetCertificationRequestInfo();
                X509SubjectAltNameExtension altNameExtension = GetAltNameExtensionFromCSRInfo(info);
                if (altNameExtension != null &&
                    altNameExtension.Uris.Count > 0 &&
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

        public virtual async Task<X509Certificate2> SigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string[] domainNames,
            byte[] certificateRequest,
            CancellationToken ct = default)
        {
            try
            {
                var pkcs10CertificationRequest
                    = new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(
                    certificateRequest);

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "CSR signature invalid.");
                }

                Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info =
                    pkcs10CertificationRequest.GetCertificationRequestInfo();
                X509SubjectAltNameExtension altNameExtension = GetAltNameExtensionFromCSRInfo(info);
                if (altNameExtension != null)
                {
                    if (altNameExtension.Uris.Count > 0 &&
                        !altNameExtension.Uris.Contains(application.ApplicationUri))
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

                DateTime yesterday = DateTime.Today.AddDays(-1);
                using X509Certificate2 signingKey = await LoadSigningKeyAsync(
                    Certificates[certificateType],
                    null,
                    m_telemetry,
                    ct)
                    .ConfigureAwait(false);
                var subjectName = new X500DistinguishedName(info.Subject.GetEncoded());

                X509Certificate2 certificate;

                ICertificateBuilder builder = CertificateBuilder
                    .Create(subjectName)
                    .AddExtension(
                        new X509SubjectAltNameExtension(application.ApplicationUri, domainNames))
                    .SetNotBefore(yesterday)
                    .SetLifeTime(Configuration.DefaultCertificateLifetime);

#if ECC_SUPPORT
                certificate = TryGetECCCurve(certificateType, out ECCurve curve)
                    ? builder
                        .SetIssuer(signingKey)
                        .SetECDsaPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                        .CreateForECDsa()
                    : builder
                        .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(
                            Configuration.DefaultCertificateHashSize))
                        .SetIssuer(signingKey)
                        .SetRSAPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                        .CreateForRSA();
#else
                certificate = builder
                    .SetHashAlgorithm(
                        X509Utils.GetRSAHashAlgorithmName(Configuration.DefaultCertificateHashSize))
                    .SetIssuer(signingKey)
                    .SetRSAPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                    .CreateForRSA();
#endif

                return certificate;
            }
            catch (Exception ex) when (ex is not ServiceResultException)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }
        }

        public virtual async Task<X509Certificate2> CreateCACertificateAsync(
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

            if (certificateType is null)
            {
                throw new ArgumentNullException(nameof(certificateType));
            }

            DateTime yesterday = DateTime.Today.AddDays(-1);
            ICertificateBuilder builder = CertificateFactory
                .CreateCertificate(subjectName)
                .SetNotBefore(yesterday)
                .SetLifeTime(Configuration.CACertificateLifetime)
                .SetCAConstraint();

#if ECC_SUPPORT
            using X509Certificate2 certificate = TryGetECCCurve(certificateType, out ECCurve curve)
                ? builder.SetECCurve(curve).CreateForECDsa()
                : builder
                    .SetHashAlgorithm(
                        X509Utils.GetRSAHashAlgorithmName(Configuration.CACertificateHashSize))
                    .SetRSAKeySize(Configuration.CACertificateKeySize)
                    .CreateForRSA();
#else
            using X509Certificate2 certificate = builder
                .SetHashAlgorithm(
                    X509Utils.GetRSAHashAlgorithmName(Configuration.CACertificateHashSize))
                .SetRSAKeySize(Configuration.CACertificateKeySize)
                .CreateForRSA();
#endif

            await certificate.AddToStoreAsync(
                AuthoritiesStore,
                password: null,
                m_telemetry,
                ct).ConfigureAwait(false);

            // save only public key
            Certificates[certificateType] = CertificateFactory.Create(certificate.RawData);

            // initialize revocation list
            X509CRL crl = await RevokeCertificateAsync(
                AuthoritiesStore,
                certificate,
                issuerKeyFilePassword: null,
                m_telemetry,
                ct)
                .ConfigureAwait(false);

            //Update TrustedList Store
            if (crl != null)
            {
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
            }

            return Certificates[certificateType];
        }

        /// <summary>
        /// load the authority signing key.
        /// </summary>
        public virtual Task<X509Certificate2> LoadSigningKeyAsync(
            X509Certificate2 signingCertificate,
            char[] signingKeyPassword,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            var certIdentifier = new CertificateIdentifier(signingCertificate)
            {
                StorePath = AuthoritiesStore.StorePath,
                StoreType = AuthoritiesStore.StoreType
            };
            return certIdentifier.LoadPrivateKeyAsync(signingKeyPassword, null, telemetry, ct);
        }

        /// <summary>
        /// Revoke the CA signed certificate.
        /// The issuer CA public key, the private key and the crl reside in the storepath.
        /// The CRL number is increased by one and existing CRL for the issuer are deleted
        /// from the store.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<X509CRL> RevokeCertificateAsync(
            CertificateStoreIdentifier storeIdentifier,
            X509Certificate2 certificate,
            char[] issuerKeyFilePassword = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            X509CRL updatedCRL = null;

            // caller may want to create empty CRL using the CA cert itself
            bool isCACert = X509Utils.IsCertificateAuthority(certificate);

            // find the authority key identifier.

            X509AuthorityKeyIdentifierExtension authority =
                certificate.FindExtension<X509AuthorityKeyIdentifierExtension>();
            string serialNumber;
            if (authority != null)
            {
                serialNumber = authority.SerialNumber;
            }
            else
            {
                throw new ArgumentException("Certificate does not contain an Authority Key");
            }

            if (!isCACert)
            {
                if (serialNumber == certificate.SerialNumber || X509Utils.IsSelfSigned(certificate))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Cannot revoke self signed certificates");
                }
            }

            ICertificateStore store = storeIdentifier.OpenStore(telemetry);
            try
            {
                if (store == null)
                {
                    throw new ArgumentException("Invalid store path/type");
                }
                X509Certificate2 certCA =
                    await X509Utils
                        .FindIssuerCABySerialNumberAsync(
                            store,
                            certificate.IssuerName,
                            serialNumber)
                        .ConfigureAwait(false)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Cannot find issuer certificate in store.");

                var certCAIdentifier = new CertificateIdentifier(certCA)
                {
                    StorePath = store.StorePath,
                    StoreType = store.StoreType
                };
                X509Certificate2 certCAWithPrivateKey =
                    await certCAIdentifier.LoadPrivateKeyAsync(
                        issuerKeyFilePassword,
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

                var certificateCollection = new X509Certificate2Collection();
                if (!isCACert)
                {
                    certificateCollection.Add(certificate);
                }
                updatedCRL = CertificateFactory.RevokeCertificate(
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
                store.Close();
            }
            return updatedCRL;
        }

#if ECC_SUPPORT
        /// <summary>
        /// GetTheEccCurve of the CertificateGroups CertificateType
        /// </summary>
        /// <returns>returns false if RSA CertificateType, true if a ECCurve can be found, else throws Exception</returns>
        /// <exception cref="ServiceResultException"></exception>
        private static bool TryGetECCCurve(NodeId certificateType, out ECCurve curve)
        {
            curve = default;
            if (IsRSACertificateType(certificateType))
            {
                return false;
            }
            curve =
                EccUtils.GetCurveFromCertificateTypeId(certificateType)
                ?? throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    $"The certificate type {certificateType} is not supported.");
            return true;

            //  Checks if the Certificate Group is for RSA Certificates
            static bool IsRSACertificateType(NodeId certificateType)
            {
                return certificateType == null ||
                    certificateType == Ua.ObjectTypeIds.ApplicationCertificateType ||
                    certificateType == Ua.ObjectTypeIds.HttpsCertificateType ||
                    certificateType == Ua.ObjectTypeIds.UserCredentialCertificateType ||
                    certificateType == Ua.ObjectTypeIds.RsaMinApplicationCertificateType ||
                    certificateType == Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
            }
        }
#endif

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

                X509Certificate2Collection certificates = await authorityStore.EnumerateAsync(ct)
                    .ConfigureAwait(false);
                foreach (X509Certificate2 certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.Subject, SubjectName))
                    {
                        X509Certificate2Collection certs = await trustedOrIssuerStore
                            .FindByThumbprintAsync(certificate.Thumbprint, ct)
                            .ConfigureAwait(false);
                        if (certs.Count == 0)
                        {
                            using X509Certificate2 x509 = CertificateFactory.Create(
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
                authorityStore?.Close();
                trustedOrIssuerStore?.Close();
            }
        }

        protected X509SubjectAltNameExtension GetAltNameExtensionFromCSRInfo(
            Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info)
        {
            try
            {
                for (int i = 0; i < info.Attributes.Count; i++)
                {
                    var sequence = Org.BouncyCastle.Asn1.Asn1Sequence
                        .GetInstance(info.Attributes[i].ToAsn1Object());
                    var oid = Org.BouncyCastle.Asn1.DerObjectIdentifier
                        .GetInstance(sequence[0].ToAsn1Object());
                    if (oid.Equals(
                        Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Pkcs9AtExtensionRequest))
                    {
                        var extensionInstance = Org.BouncyCastle.Asn1.Asn1Set
                            .GetInstance(sequence[1]);
                        var extensionSequence = Org.BouncyCastle.Asn1.Asn1Sequence
                            .GetInstance(extensionInstance[0]);
                        var extensions = Org.BouncyCastle.Asn1.X509.X509Extensions
                            .GetInstance(extensionSequence);
                        Org.BouncyCastle.Asn1.X509.X509Extension extension = extensions
                            .GetExtension(
                                Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                        var asnEncodedAltNameExtension = new AsnEncodedData(
                            Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName
                                .ToString(),
                            extension.Value.GetOctets());
                        return new X509SubjectAltNameExtension(
                            asnEncodedAltNameExtension,
                            extension.IsCritical);
                    }
                }
            }
            catch
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "CSR altNameExtension invalid.");
            }
            return null;
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }
}
