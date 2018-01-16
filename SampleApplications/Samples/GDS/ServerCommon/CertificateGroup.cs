/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Pkcs;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server
{

    class CertificateGroup : ICertificateProvider
    {
        public NodeId Id;
        public readonly CertificateGroupConfiguration Configuration;
        public X509Certificate2 Certificate;
        public TrustListState DefaultTrustList;
        public NodeId CertificateType;
        public Boolean UpdateRequired = false;

        public CertificateGroup(
            string authoritiesStorePath,
            CertificateGroupConfiguration certificateGroupConfiguration
            )
        {
            m_authoritiesStorePath = authoritiesStorePath;
            m_authoritiesStoreType = CertificateStoreIdentifier.DetermineStoreType(m_authoritiesStorePath);
            Configuration = certificateGroupConfiguration;
        }

        public virtual X509Certificate2 NewKeyPairRequest(
            ApplicationRecordDataType application, 
            string subjectName, 
            string[] domainNames, 
            string privateKeyFormat, 
            string privateKeyPassword)
        {
            return CertificateFactory.CreateCertificate(
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
                 LoadSigningKeyAsync(Certificate, string.Empty).Result,
                 null);
        }

        public virtual void RevokeCertificate(
            X509Certificate2 certificate)
        {
            CertificateFactory.RevokeCertificateAsync(
                m_authoritiesStorePath,
                certificate,
                null).Wait();
        }

        public virtual X509Certificate2 SigningRequest(
            ApplicationRecordDataType application,
            string[] domainNames,
            byte[] certificateRequest)
        {
            Pkcs10CertificationRequest pkcs10CertificationRequest = new Pkcs10CertificationRequest(certificateRequest);
            CertificationRequestInfo info = pkcs10CertificationRequest.GetCertificationRequestInfo();
            DateTime yesterday = DateTime.UtcNow.AddDays(-1);
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
                LoadSigningKeyAsync(Certificate, string.Empty).Result,
                info.SubjectPublicKeyInfo.GetEncoded());
        }

        public virtual X509Certificate2 CreateCACertificate(
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
                Configuration.DefaultCertificateKeySize,
                yesterday,
                Configuration.DefaultCertificateLifetime,
                Configuration.DefaultCertificateHashSize,
                true,
                null,
                null);

            // save only public key
            Certificate = new X509Certificate2(newCertificate.RawData);

            // initialize cert revocation list (CRL)
            CertificateFactory.RevokeCertificateAsync(m_authoritiesStorePath, newCertificate, null).Wait();

            return Certificate;
        }

        /// <summary>
        /// load the authority signing key.
        /// </summary>
        private async Task<X509Certificate2> LoadSigningKeyAsync(X509Certificate2 signingCertificate, string signingKeyPassword)
        {
            CertificateIdentifier certIdentifier = new CertificateIdentifier(signingCertificate)
            {
                StorePath = m_authoritiesStorePath,
                StoreType = m_authoritiesStoreType
            };
            return await certIdentifier.LoadPrivateKey(signingKeyPassword);
        }

        private readonly string m_authoritiesStorePath;
        private readonly string m_authoritiesStoreType;

    }

}
