/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server
{
    public class CertificateGroup : ICertificateGroup
    {
        #region Public Fields
        public NodeId Id { get; set; }
        public NodeId CertificateType { get; set; }
        public CertificateGroupConfiguration Configuration { get; }
        public X509Certificate2 Certificate { get; set; }
        public TrustListState DefaultTrustList { get; set; }
        public Boolean UpdateRequired { get; set; }
        #endregion

        public CertificateGroup()
        {
            UpdateRequired = false;
        }

        protected CertificateGroup(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration
            )
        {
            m_authoritiesStorePath = authoritiesStorePath;
            m_authoritiesStoreType = CertificateStoreIdentifier.DetermineStoreType(m_authoritiesStorePath);
            Configuration = certificateGroupConfiguration;
            m_subjectName = Configuration.SubjectName.Replace("localhost", Utils.GetHostName());
        }

        #region ICertificateGroupProvider
        public virtual async Task Init()
        {
            Utils.Trace(Utils.TraceMasks.Information, "InitializeCertificateGroup: {0}", m_subjectName);

            using (ICertificateStore store = CertificateStoreIdentifier.OpenStore(m_authoritiesStorePath))
            {
                X509Certificate2Collection certificates = await store.Enumerate();
                foreach (var certificate in certificates)
                {
                    if (Utils.CompareDistinguishedName(certificate.Subject, m_subjectName))
                    {
                        using (RSA rsa = certificate.GetRSAPublicKey())
                        {
                            if (rsa.KeySize != Configuration.CACertificateKeySize)
                            {
                                continue;
                            }

                            // TODO check hash size
                        }

                        if (Certificate != null)
                        {
                            // always use latest issued cert in store
                            if (certificate.NotBefore > DateTime.UtcNow ||
                                Certificate.NotBefore > certificate.NotBefore)
                            {
                                continue;
                            }
                        }
                        Certificate = certificate;
                    }
                }
            }

            if (Certificate == null)
            {
                Utils.Trace(Utils.TraceMasks.Security,
                    "Create new CA Certificate: {0}, KeySize: {1}, HashSize: {2}, LifeTime: {3} months",
                    m_subjectName,
                    Configuration.CACertificateKeySize,
                    Configuration.CACertificateHashSize,
                    Configuration.CACertificateLifetime
                    );
                X509Certificate2 newCertificate = await CreateCACertificateAsync(m_subjectName);
                Certificate = new X509Certificate2(newCertificate.RawData);
            }
        }

        public virtual CertificateGroup Create(
            string storePath,
            CertificateGroupConfiguration certificateGroupConfiguration)
        {
            return new CertificateGroup(storePath, certificateGroupConfiguration);
        }

        public virtual async Task<X509Certificate2KeyPair> NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
        {
            using (var signingKey = await LoadSigningKeyAsync(Certificate, string.Empty))
            using (var certificate = CertificateFactory.CreateCertificate(
                 null,
                 null,
                 null,
                 application.ApplicationUri ?? "urn:ApplicationURI",
                 application.ApplicationNames.Count > 0 ? application.ApplicationNames[0].Text : "ApplicationName",
                 subjectName,
                 domainNames,
                 Configuration.DefaultCertificateKeySize,
                 DateTime.UtcNow.AddDays(-1),
                 Configuration.DefaultCertificateLifetime,
                 Configuration.DefaultCertificateHashSize,
                 false,
                 signingKey,
                 null))
            {
                byte[] privateKey;
                if (privateKeyFormat == "PFX")
                {
                    privateKey = certificate.Export(X509ContentType.Pfx, privateKeyPassword);
                }
                else if (privateKeyFormat == "PEM")
                {
                    privateKey = CertificateFactory.ExportPrivateKeyAsPEM(certificate);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid private key format");
                }
                return new X509Certificate2KeyPair(new X509Certificate2(certificate.RawData), privateKeyFormat, privateKey);
            }
        }

        public virtual async Task<Opc.Ua.X509CRL> RevokeCertificateAsync(
            X509Certificate2 certificate)
        {
            return await CertificateFactory.RevokeCertificateAsync(
                m_authoritiesStorePath,
                certificate,
                null);
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

                var info = pkcs10CertificationRequest.GetCertificationRequestInfo();
                var altNameExtension = GetAltNameExtensionFromCSRInfo(info);
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
                    throw ex as ServiceResultException;
                }
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }
        }


        public virtual async Task<X509Certificate2> SigningRequestAsync(
            ApplicationRecordDataType application,
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

                var info = pkcs10CertificationRequest.GetCertificationRequestInfo();
                var altNameExtension = GetAltNameExtensionFromCSRInfo(info);
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

                    if (altNameExtension.IPAddresses.Count > 0 || altNameExtension.DomainNames.Count > 0)
                    {
                        var domainNameList = new List<string>();
                        domainNameList.AddRange(altNameExtension.DomainNames);
                        domainNameList.AddRange(altNameExtension.IPAddresses);
                        domainNames = domainNameList.ToArray();
                    }
                }

                DateTime yesterday = DateTime.UtcNow.AddDays(-1);
                using (var signingKey = await LoadSigningKeyAsync(Certificate, string.Empty))
                {
                    return CertificateFactory.CreateCertificate(
                        null,
                        null,
                        null,
                        application.ApplicationUri ?? "urn:ApplicationURI",
                        application.ApplicationNames.Count > 0 ? application.ApplicationNames[0].Text : "ApplicationName",
                        info.Subject.ToString(),
                        domainNames,
                        Configuration.DefaultCertificateKeySize,
                        yesterday,
                        Configuration.DefaultCertificateLifetime,
                        Configuration.DefaultCertificateHashSize,
                        false,
                        signingKey,
                        info.SubjectPublicKeyInfo.GetEncoded());
                }
            }
            catch (Exception ex)
            {
                if (ex is ServiceResultException)
                {
                    throw ex as ServiceResultException;
                }
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex.Message);
            }

        }

        public virtual async Task<X509Certificate2> CreateCACertificateAsync(
            string subjectName
            )
        {
            DateTime yesterday = DateTime.UtcNow.AddDays(-1);
            X509Certificate2 newCertificate = CertificateFactory.CreateCertificate(
                m_authoritiesStoreType,
                m_authoritiesStorePath,
                null,
                null,
                null,
                subjectName,
                null,
                Configuration.CACertificateKeySize,
                yesterday,
                Configuration.CACertificateLifetime,
                Configuration.CACertificateHashSize,
                true,
                null,
                null);

            // save only public key
            Certificate = new X509Certificate2(newCertificate.RawData);

            // initialize revocation list
            await CertificateFactory.RevokeCertificateAsync(m_authoritiesStorePath, newCertificate, null);

            await UpdateAuthorityCertInTrustedList();

            return Certificate;
        }
        #endregion
        #region Public Methods
        /// <summary>
        /// load the authority signing key.
        /// </summary>
        public virtual async Task<X509Certificate2> LoadSigningKeyAsync(X509Certificate2 signingCertificate, string signingKeyPassword)
        {
            CertificateIdentifier certIdentifier = new CertificateIdentifier(signingCertificate)
            {
                StorePath = m_authoritiesStorePath,
                StoreType = m_authoritiesStoreType
            };
            return await certIdentifier.LoadPrivateKey(signingKeyPassword);
        }
        #endregion
        #region Private Methods
        /// <summary>
        /// Updates the certificate authority certificate and CRL in the trusted list.
        /// </summary>
        protected async Task UpdateAuthorityCertInTrustedList()
        {
            string trustedListStorePath = Configuration.TrustedListPath;
            if (!String.IsNullOrEmpty(Configuration.TrustedListPath))
            {
                using (ICertificateStore authorityStore = CertificateStoreIdentifier.OpenStore(m_authoritiesStorePath))
                using (ICertificateStore trustedStore = CertificateStoreIdentifier.OpenStore(trustedListStorePath))
                {
                    X509Certificate2Collection certificates = await authorityStore.Enumerate();
                    foreach (var certificate in certificates)
                    {
                        if (Utils.CompareDistinguishedName(certificate.Subject, m_subjectName))
                        {
                            X509Certificate2Collection certs = await trustedStore.FindByThumbprint(certificate.Thumbprint);
                            if (certs.Count == 0)
                            {
                                await trustedStore.Add(new X509Certificate2(certificate.RawData));
                            }

                            // delete existing CRL in trusted list
                            foreach (var crl in trustedStore.EnumerateCRLs(certificate, false))
                            {
                                if (crl.VerifySignature(certificate, false))
                                {
                                    trustedStore.DeleteCRL(crl);
                                }
                            }

                            // copy latest CRL to trusted list
                            foreach (var crl in authorityStore.EnumerateCRLs(certificate, true))
                            {
                                trustedStore.AddCRL(crl);
                            }
                        }
                    }
                }
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
                        var extensionInstance = Org.BouncyCastle.Asn1.DerSet.GetInstance(sequence[1]);
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

        #region Protected Fields
        protected readonly string m_subjectName;
        protected readonly string m_authoritiesStorePath;
        protected readonly string m_authoritiesStoreType;
        #endregion 

    }

}
