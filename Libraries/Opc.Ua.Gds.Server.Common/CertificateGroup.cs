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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server
{
    public class CertificateGroup : ICertificateGroup
    {
        #region Public Fields
        public NodeId Id { get; set; }
        public NodeIdCollection CertificateTypes { get; set; }
        public CertificateGroupConfiguration Configuration { get; }
        public ConcurrentDictionary<NodeId, X509Certificate2> Certificates { get; }
        public TrustListState DefaultTrustList { get; set; }
        public bool UpdateRequired { get; set; }
        public CertificateStoreIdentifier AuthoritiesStore { get; }
        public CertificateStoreIdentifier IssuerCertificatesStore { get; }
        #endregion

        public CertificateGroup()
        {
            UpdateRequired = false;
        }

        protected CertificateGroup(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            [Optional] string trustedIssuerCertificatesStorePath
            )
        {
            AuthoritiesStore = new CertificateStoreIdentifier(authoritiesStorePath, false);
            Configuration = certificateGroupConfiguration;
            if (trustedIssuerCertificatesStorePath != null)
            {
                IssuerCertificatesStore = new CertificateStoreIdentifier(trustedIssuerCertificatesStorePath);
            }
#if NET6_0_OR_GREATER
            SubjectName = Configuration.SubjectName.Replace("localhost", Utils.GetHostName(), StringComparison.OrdinalIgnoreCase);
#else
#pragma warning disable CA1307
            SubjectName = Configuration.SubjectName.Replace("localhost", Utils.GetHostName());
#pragma warning restore CA1307
#endif
            CertificateTypes = new NodeIdCollection();

            Certificates = new ConcurrentDictionary<NodeId, X509Certificate2>();

            foreach (string certificateTypeString in Configuration.CertificateTypes)
            {
                var certificateType = typeof(Opc.Ua.ObjectTypeIds).GetField(certificateTypeString).GetValue(null) as NodeId;
                if (certificateType != null)
                {
                    if (!Utils.IsSupportedCertificateType(certificateType))
                    {
                        throw new NotImplementedException($"Unsupported certificate type {certificateType}");
                    }

                    CertificateTypes.Add(certificateType);
                    Certificates.TryAdd(certificateType, null);
                }
                else
                {
                    throw new NotImplementedException($"Unknown certificate type {certificateTypeString}. Use ApplicationCertificateType, HttpsCertificateType or UserCredentialCertificateType");
                }
            }
            if (CertificateTypes.Count == 0)
            {
                throw new ArgumentException("Please specify at least one valid Certificate Type");
            }
        }

        #region ICertificateGroupProvider
        public virtual async Task Init()
        {
            Utils.LogInfo("InitializeCertificateGroup: {0}", SubjectName);

            ICertificateStore store = AuthoritiesStore.OpenStore();
            try
            {
                X509Certificate2Collection certificates = await store.Enumerate().ConfigureAwait(false);
                foreach (X509Certificate2 certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.Subject, SubjectName))
                    {
                        if (!X509Utils.IsECDsaSignature(certificate) && X509Utils.GetRSAPublicKeySize(certificate) != Configuration.CACertificateKeySize)
                        {
                            continue;
                        }

                        // TODO check hash size

                        NodeId certificateType = CertificateIdentifier.GetCertificateType(certificate);

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
                    Utils.LogInfo(Utils.TraceMasks.Security,
                        "Create new CA Certificate: {0}, CertificateType {1}  KeySize: {2}, HashSize: {3}, LifeTime: {4} months",
                        SubjectName,
                        certificateType,
                        Configuration.CACertificateKeySize,
                        Configuration.CACertificateHashSize,
                        Configuration.CACertificateLifetime
                        );
                    await CreateCACertificateAsync(SubjectName, certificateType).ConfigureAwait(false);
                    Utils.LogCertificate(Utils.TraceMasks.Security, "Created CA certificate: ", Certificates[certificateType]);
                }

            }
        }

        public virtual ICertificateGroup Create(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration,
            [Optional] string trustedIssuerCertificatesStorePath)
        {
            return new CertificateGroup(authoritiesStorePath, certificateGroupConfiguration, trustedIssuerCertificatesStorePath);
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
        public virtual async Task<X509Certificate2KeyPair> NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (application.ApplicationUri == null) throw new ArgumentNullException(nameof(application.ApplicationUri));
            if (application.ApplicationNames == null) throw new ArgumentNullException(nameof(application.ApplicationNames));

            using (X509Certificate2 signingKey = await LoadSigningKeyAsync(Certificates[certificateType], string.Empty).ConfigureAwait(false))
            {
                X509Certificate2 certificate;

                ICertificateBuilderIssuer builder = CertificateFactory.CreateCertificate(
                    application.ApplicationUri,
                    application.ApplicationNames.Count > 0 ? application.ApplicationNames[0].Text : "ApplicationName",
                    subjectName,
                    domainNames)
                    .SetIssuer(signingKey);
#if ECC_SUPPORT
                certificate = TryGetECCCurve(certificateType, out ECCurve curve) ?
                   builder.SetECCurve(curve).CreateForECDsa() :
                   builder.CreateForRSA();
#else
                certificate = builder
                       .CreateForRSA();
#endif

                byte[] privateKey;
                if (privateKeyFormat == "PFX")
                {
                    privateKey = certificate.Export(X509ContentType.Pfx, privateKeyPassword);
                }
                else if (privateKeyFormat == "PEM")
                {
                    privateKey = PEMWriter.ExportPrivateKeyAsPEM(certificate, privateKeyPassword);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid private key format");
                }

                X509Certificate2 publicKey = X509CertificateLoader.LoadCertificate(certificate.RawData);
                Utils.SilentDispose(certificate);

                return new X509Certificate2KeyPair(publicKey, privateKeyFormat, privateKey);
            }
        }

        public async virtual Task<X509CRL> RevokeCertificateAsync(
            X509Certificate2 certificate)
        {
            X509CRL crl = await RevokeCertificateAsync(
                AuthoritiesStore,
                certificate,
                null).ConfigureAwait(false);

            // Also update TrustedList CRL so registerd Applications can get the new CRL
            if (crl != null)
            {
                var certificateStoreIdentifier = new CertificateStoreIdentifier(Configuration.TrustedListPath);
                await UpdateAuthorityCertInCertificateStore(certificateStoreIdentifier).ConfigureAwait(false);

                //Also update TrustedIssuerCertificates Store
                if (IssuerCertificatesStore != null)
                {
                    await UpdateAuthorityCertInCertificateStore(IssuerCertificatesStore).ConfigureAwait(false);
                }
            }

            // return crl
            return crl;
        }

        public virtual Task VerifySigningRequestAsync(
            ApplicationRecordDataType application,
            byte[] certificateRequest)
        {
            try
            {
                var pkcs10CertificationRequest = new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(certificateRequest);

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "CSR signature invalid.");
                }

                Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info = pkcs10CertificationRequest.GetCertificationRequestInfo();
                X509SubjectAltNameExtension altNameExtension = GetAltNameExtensionFromCSRInfo(info);
                if (altNameExtension != null)
                {
                    if (altNameExtension.Uris.Count > 0)
                    {
                        if (!altNameExtension.Uris.Contains(application.ApplicationUri))
                        {
                            throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid,
                                "CSR AltNameExtension does not match " + application.ApplicationUri);
                        }
                    }
                }
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                if (ex is ServiceResultException)
                {
                    throw;
                }
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }
        }


        public virtual async Task<X509Certificate2> SigningRequestAsync(
            ApplicationRecordDataType application,
            NodeId certificateType,
            string[] domainNames,
            byte[] certificateRequest)
        {
            try
            {
                var pkcs10CertificationRequest = new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(certificateRequest);

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "CSR signature invalid.");
                }

                Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info = pkcs10CertificationRequest.GetCertificationRequestInfo();
                X509SubjectAltNameExtension altNameExtension = GetAltNameExtensionFromCSRInfo(info);
                if (altNameExtension != null)
                {
                    if (altNameExtension.Uris.Count > 0)
                    {
                        if (!altNameExtension.Uris.Contains(application.ApplicationUri))
                        {
                            var applicationUriMissing = new StringBuilder();
                            applicationUriMissing.AppendLine("Expected AltNameExtension (ApplicationUri):");
                            applicationUriMissing.AppendLine(application.ApplicationUri);
                            applicationUriMissing.AppendLine("CSR AltNameExtensions found:");
                            foreach (string uri in altNameExtension.Uris)
                            {
                                applicationUriMissing.AppendLine(uri);
                            }
                            throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid,
                                applicationUriMissing.ToString());
                        }
                    }

                    if (altNameExtension.IPAddresses.Count > 0 || altNameExtension.DomainNames.Count > 0)
                    {
                        var domainNameList = new List<string>();
                        domainNameList.AddRange(altNameExtension.DomainNames);
                        domainNameList.AddRange(altNameExtension.IPAddresses);
                        domainNames = domainNameList.ToArray();
                    }
                }

                DateTime yesterday = DateTime.Today.AddDays(-1);
                using (X509Certificate2 signingKey = await LoadSigningKeyAsync(Certificates[certificateType], string.Empty).ConfigureAwait(false))
                {
                    var subjectName = new X500DistinguishedName(info.Subject.GetEncoded());

                    X509Certificate2 certificate;

                    ICertificateBuilder builder = CertificateBuilder.Create(subjectName)
                        .AddExtension(new X509SubjectAltNameExtension(application.ApplicationUri, domainNames))
                        .SetNotBefore(yesterday)
                        .SetLifeTime(Configuration.DefaultCertificateLifetime);

#if ECC_SUPPORT
                    certificate = TryGetECCCurve(certificateType, out ECCurve curve) ?
                       builder.SetIssuer(signingKey).SetECDsaPublicKey(info.SubjectPublicKeyInfo.GetEncoded()).CreateForECDsa() :
                       builder.SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(Configuration.DefaultCertificateHashSize))
                                .SetIssuer(signingKey)
                                .SetRSAPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                                .CreateForRSA();
#else
                    certificate = builder.SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(Configuration.DefaultCertificateHashSize))
                                    .SetIssuer(signingKey)
                                    .SetRSAPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                                    .CreateForRSA();
#endif

                    return certificate;
                }
            }
            catch (Exception ex)
            {
                if (ex is ServiceResultException)
                {
                    throw;
                }
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }

        }

        public virtual async Task<X509Certificate2> CreateCACertificateAsync(
            string subjectName,
            NodeId certificateType
            )
        {
            // validate new subjectName matches the previous subject
            // TODO: An issuer may modify the subject of the CA certificate,
            // but then the configuration must be updated too!
            // NOTE: not a strict requirement here for ASN.1 byte compare
            if (!X509Utils.CompareDistinguishedName(subjectName, SubjectName))
            {
                throw new ArgumentException("SubjectName provided does not match the SubjectName property of the CertificateGroup \n" +
                    "CA Certificate is not created until the subjectName " + SubjectName + " is provided", subjectName);
            }

            if (certificateType is null)
            {
                throw new ArgumentNullException(nameof(certificateType));
            }

            DateTime yesterday = DateTime.Today.AddDays(-1);
            X509Certificate2 certificate;

            ICertificateBuilder builder = CertificateFactory.CreateCertificate(subjectName)
                .SetNotBefore(yesterday)
                .SetLifeTime(Configuration.CACertificateLifetime)
                .SetCAConstraint();

#if ECC_SUPPORT
            certificate = TryGetECCCurve(certificateType, out ECCurve curve) ?
               builder.SetECCurve(curve).CreateForECDsa() :
               builder.SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(Configuration.CACertificateHashSize))
                      .SetRSAKeySize(Configuration.CACertificateKeySize)
                      .CreateForRSA();
#else
            certificate = builder.SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(Configuration.CACertificateHashSize))
                      .SetRSAKeySize(Configuration.CACertificateKeySize)
                      .CreateForRSA();
#endif

            await certificate.AddToStoreAsync(AuthoritiesStore).ConfigureAwait(false);

            // save only public key
            Certificates[certificateType] = X509CertificateLoader.LoadCertificate(certificate.RawData);

            // initialize revocation list
            X509CRL crl = await RevokeCertificateAsync(AuthoritiesStore, certificate, null).ConfigureAwait(false);

            //Update TrustedList Store
            if (crl != null)
            {
                // TODO: make CA trust selectable
                var certificateStoreIdentifier = new CertificateStoreIdentifier(Configuration.TrustedListPath);
                await UpdateAuthorityCertInCertificateStore(certificateStoreIdentifier).ConfigureAwait(false);

                // Update TrustedIssuerCertificates Store
                if (IssuerCertificatesStore != null)
                {
                    await UpdateAuthorityCertInCertificateStore(IssuerCertificatesStore).ConfigureAwait(false);
                }
            }

            Utils.SilentDispose(certificate);

            return Certificates[certificateType];

        }

        #endregion

        #region Public Methods
        /// <summary>
        /// load the authority signing key.
        /// </summary>
        public virtual async Task<X509Certificate2> LoadSigningKeyAsync(X509Certificate2 signingCertificate, string signingKeyPassword)
        {
            var certIdentifier = new CertificateIdentifier(signingCertificate) {
                StorePath = AuthoritiesStore.StorePath,
                StoreType = AuthoritiesStore.StoreType
            };
            return await certIdentifier.LoadPrivateKey(signingKeyPassword).ConfigureAwait(false);
        }

        /// <summary>
        /// Revoke the CA signed certificate. 
        /// The issuer CA public key, the private key and the crl reside in the storepath.
        /// The CRL number is increased by one and existing CRL for the issuer are deleted from the store.
        /// </summary>
        public static async Task<X509CRL> RevokeCertificateAsync(
            CertificateStoreIdentifier storeIdentifier,
            X509Certificate2 certificate,
            string issuerKeyFilePassword = null
            )
        {
            X509CRL updatedCRL = null;
            X500DistinguishedName subjectName = certificate.IssuerName;
            string keyId = null;
            string serialNumber = null;

            // caller may want to create empty CRL using the CA cert itself
            bool isCACert = X509Utils.IsCertificateAuthority(certificate);

            // find the authority key identifier.

/* Unmerged change from project 'Opc.Ua.Gds.Server.Common (net8.0)'
Before:
            var authority = X509Extensions.FindExtension<Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension>(certificate);
            if (authority != null)
After:
            Security.Certificates.X509AuthorityKeyIdentifierExtension authority = X509Extensions.FindExtension<Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension>(certificate);
            if (authority != null)
*/
            var authority = X509Extensions.FindExtension<Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension>(certificate);
            if (authority != null)
            {
                keyId = authority.KeyIdentifier;
                serialNumber = authority.SerialNumber;
            }
            else
            {
                throw new ArgumentException("Certificate does not contain an Authority Key");
            }

            if (!isCACert)
            {
                if (serialNumber == certificate.SerialNumber ||
                    X509Utils.IsSelfSigned(certificate))
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Cannot revoke self signed certificates");
                }
            }

            X509Certificate2 certCA = null;
            ICertificateStore store = storeIdentifier.OpenStore();
            try
            {
                if (store == null)
                {
                    throw new ArgumentException("Invalid store path/type");
                }
                certCA = await X509Utils.FindIssuerCABySerialNumberAsync(store, certificate.IssuerName, serialNumber).ConfigureAwait(false);

                if (certCA == null)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Cannot find issuer certificate in store.");
                }

                var certCAIdentifier = new CertificateIdentifier(certCA) {
                    StorePath = store.StorePath,
                    StoreType = store.StoreType
                };
                X509Certificate2 certCAWithPrivateKey = await certCAIdentifier.LoadPrivateKey(issuerKeyFilePassword).ConfigureAwait(false);

                if (certCAWithPrivateKey == null)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Failed to load issuer private key. Is the password correct?");
                }

                if (!certCAWithPrivateKey.HasPrivateKey)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Issuer certificate has no private key, cannot revoke certificate.");
                }

                X509CRLCollection certCACrl = await store.EnumerateCRLs(certCA, false).ConfigureAwait(false);

                var certificateCollection = new X509Certificate2Collection();
                if (!isCACert)
                {
                    certificateCollection.Add(certificate);
                }
                updatedCRL = CertificateFactory.RevokeCertificate(certCAWithPrivateKey, certCACrl, certificateCollection);

                await store.AddCRL(updatedCRL).ConfigureAwait(false);

                // delete outdated CRLs from store
                foreach (X509CRL caCrl in certCACrl)
                {
                    await store.DeleteCRL(caCrl).ConfigureAwait(false);
                }
            }
            finally
            {
                store.Close();
            }
            return updatedCRL;
        }
        #endregion

        #region Private Methods
#if ECC_SUPPORT
        /// <summary>
        /// GetTheEccCurve of the CertificateGroups CertificateType
        /// </summary>
        /// <returns>returns false if RSA CertificateType, true if a ECCurve can be found, else throws Exception</returns>
        /// <exception cref="ServiceResultException"></exception>
        private bool TryGetECCCurve(NodeId certificateType, out ECCurve curve)
        {
            curve = default;
            if (IsRSACertificateType(certificateType))
            {
                return false;
            }
            ECCurve? tempCurve = EccUtils.GetCurveFromCertificateTypeId(certificateType);

            if (tempCurve == null)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, $"The certificate type {certificateType} is not supported.");
            }

            curve = tempCurve.Value;

            return true;
        }
#endif
        /// <summary>
        /// Checks if the Certificate Group is for RSA Certificates
        /// </summary>
        /// <returns>True if the CertificateType of the Certificate Group is an RSA Certificate Type</returns>
        private bool IsRSACertificateType(NodeId certificateType)
        {
            return certificateType == null ||
                   certificateType == Ua.ObjectTypeIds.ApplicationCertificateType ||
                   certificateType == Ua.ObjectTypeIds.HttpsCertificateType ||
                   certificateType == Ua.ObjectTypeIds.UserCredentialCertificateType ||
                   certificateType == Ua.ObjectTypeIds.RsaMinApplicationCertificateType ||
                   certificateType == Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
        }

        /// <summary>
        /// Updates the certificate authority certificate and CRL in the provided CertificateStore
        /// </summary>
        /// <param name="trustedOrIssuerStoreIdentifier">The store which contains the authority ceritificate. (trusted or issuer)</param>
        /// <returns></returns>
        protected async Task UpdateAuthorityCertInCertificateStore(CertificateStoreIdentifier trustedOrIssuerStoreIdentifier)
        {
            ICertificateStore authorityStore = AuthoritiesStore.OpenStore();
            ICertificateStore trustedOrIssuerStore = trustedOrIssuerStoreIdentifier.OpenStore();
            try
            {
                if (authorityStore == null || trustedOrIssuerStore == null)
                {
                    throw new ServiceResultException("Unable to update authority certificate in stores");
                }

                X509Certificate2Collection certificates = await authorityStore.Enumerate().ConfigureAwait(false);
                foreach (X509Certificate2 certificate in certificates)
                {
                    if (X509Utils.CompareDistinguishedName(certificate.Subject, SubjectName))
                    {
                        X509Certificate2Collection certs = await trustedOrIssuerStore.FindByThumbprint(certificate.Thumbprint).ConfigureAwait(false);
                        if (certs.Count == 0)
                        {
                            using (X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(certificate.RawData))
                            {
                                await trustedOrIssuerStore.Add(x509).ConfigureAwait(false);
                            }
                        }

                        // delete existing CRL in trusted list
                        foreach (X509CRL crl in await trustedOrIssuerStore.EnumerateCRLs(certificate, false).ConfigureAwait(false))
                        {
                            if (crl.VerifySignature(certificate, false))
                            {
                                await trustedOrIssuerStore.DeleteCRL(crl).ConfigureAwait(false);
                            }
                        }

                        // copy latest CRL to trusted list
                        foreach (X509CRL crl in await authorityStore.EnumerateCRLs(certificate, true).ConfigureAwait(false))
                        {
                            await trustedOrIssuerStore.AddCRL(crl).ConfigureAwait(false);
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

        protected X509SubjectAltNameExtension GetAltNameExtensionFromCSRInfo(Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info)
        {
            try
            {
                for (int i = 0; i < info.Attributes.Count; i++)
                {
                    var sequence = Org.BouncyCastle.Asn1.Asn1Sequence.GetInstance(info.Attributes[i].ToAsn1Object());
                    var oid = Org.BouncyCastle.Asn1.DerObjectIdentifier.GetInstance(sequence[0].ToAsn1Object());
                    if (oid.Equals(Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Pkcs9AtExtensionRequest))
                    {
                        Org.BouncyCastle.Asn1.Asn1Set extensionInstance = Org.BouncyCastle.Asn1.Asn1Set.GetInstance(sequence[1]);
                        var extensionSequence = Org.BouncyCastle.Asn1.Asn1Sequence.GetInstance(extensionInstance[0]);
                        var extensions = Org.BouncyCastle.Asn1.X509.X509Extensions.GetInstance(extensionSequence);
                        Org.BouncyCastle.Asn1.X509.X509Extension extension = extensions.GetExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                        var asnEncodedAltNameExtension = new System.Security.Cryptography.AsnEncodedData(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName.ToString(), extension.Value.GetOctets());
                        var altNameExtension = new X509SubjectAltNameExtension(asnEncodedAltNameExtension, extension.IsCritical);
                        return altNameExtension;
                    }
                }
            }
            catch
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "CSR altNameExtension invalid.");
            }
            return null;
        }
        #endregion

        #region Protected Properties
        protected string SubjectName { get; }
        #endregion 

    }

}
