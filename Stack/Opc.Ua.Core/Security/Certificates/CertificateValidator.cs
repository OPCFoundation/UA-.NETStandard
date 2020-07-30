/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Validates certificates.
    /// </summary>
    public class CertificateValidator : ICertificateValidator
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateValidator()
        {
            m_validatedCertificates = new Dictionary<string, X509Certificate2>();
            m_rejectSHA1SignedCertificates = CertificateFactory.DefaultHashSize >= 256;
            m_rejectUnknownRevocationStatus = false;
            m_minimumCertificateKeySize = CertificateFactory.DefaultKeySize;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Raised when a certificate validation error occurs.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1009:DeclareEventHandlersCorrectly")]
        public event CertificateValidationEventHandler CertificateValidation
        {
            add
            {
                lock (m_callbackLock)
                {
                    m_CertificateValidation += value;
                }
            }

            remove
            {
                lock (m_callbackLock)
                {
                    m_CertificateValidation -= value;
                }
            }
        }

        /// <summary>
        /// Raised when an application certificate update occurs.
        /// </summary>
        public event CertificateUpdateEventHandler CertificateUpdate
        {
            add
            {
                lock (m_callbackLock)
                {
                    m_CertificateUpdate += value;
                }
            }

            remove
            {
                lock (m_callbackLock)
                {
                    m_CertificateUpdate -= value;
                }
            }
        }

        /// <summary>
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        public virtual async Task Update(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            await Update(configuration.SecurityConfiguration);
        }

        /// <summary>
        /// Updates the validator with a new set of trust lists.
        /// </summary>
        public virtual void Update(
            CertificateTrustList issuerStore,
            CertificateTrustList trustedStore,
            CertificateStoreIdentifier rejectedCertificateStore)
        {
            lock (m_lock)
            {
                m_validatedCertificates.Clear();

                m_trustedCertificateStore = null;
                m_trustedCertificateList = null;

                if (trustedStore != null)
                {
                    m_trustedCertificateStore = new CertificateStoreIdentifier();

                    m_trustedCertificateStore.StoreType = trustedStore.StoreType;
                    m_trustedCertificateStore.StorePath = trustedStore.StorePath;
                    m_trustedCertificateStore.ValidationOptions = trustedStore.ValidationOptions;

                    if (trustedStore.TrustedCertificates != null)
                    {
                        m_trustedCertificateList = new CertificateIdentifierCollection();
                        m_trustedCertificateList.AddRange(trustedStore.TrustedCertificates);
                    }
                }


                m_issuerCertificateStore = null;
                m_issuerCertificateList = null;

                if (issuerStore != null)
                {
                    m_issuerCertificateStore = new CertificateStoreIdentifier();

                    m_issuerCertificateStore.StoreType = issuerStore.StoreType;
                    m_issuerCertificateStore.StorePath = issuerStore.StorePath;
                    m_issuerCertificateStore.ValidationOptions = issuerStore.ValidationOptions;

                    if (issuerStore.TrustedCertificates != null)
                    {
                        m_issuerCertificateList = new CertificateIdentifierCollection();
                        m_issuerCertificateList.AddRange(issuerStore.TrustedCertificates);
                    }
                }

                m_rejectedCertificateStore = null;

                if (rejectedCertificateStore != null)
                {
                    m_rejectedCertificateStore = (CertificateStoreIdentifier)rejectedCertificateStore.MemberwiseClone();
                }
            }
        }

        /// <summary>
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        public virtual async Task Update(SecurityConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            lock (m_lock)
            {
                Update(
                    configuration.TrustedIssuerCertificates,
                    configuration.TrustedPeerCertificates,
                    configuration.RejectedCertificateStore);
                m_rejectSHA1SignedCertificates = configuration.RejectSHA1SignedCertificates;
                m_rejectUnknownRevocationStatus = configuration.RejectUnknownRevocationStatus;
                m_minimumCertificateKeySize = configuration.MinimumCertificateKeySize;
            }

            if (configuration.ApplicationCertificate != null)
            {
                m_applicationCertificate = await configuration.ApplicationCertificate.Find(true);
            }
        }

        /// <summary>
        /// Updates the validator with a new application certificate.
        /// </summary>
        public virtual async Task UpdateCertificate(SecurityConfiguration securityConfiguration)
        {
            lock (m_lock)
            {
                securityConfiguration.ApplicationCertificate.Certificate = null;
            }

            await Update(securityConfiguration);
            await securityConfiguration.ApplicationCertificate.LoadPrivateKey(null);

            lock (m_callbackLock)
            {
                if (m_CertificateUpdate != null)
                {
                    var args = new CertificateUpdateEventArgs(securityConfiguration, GetChannelValidator());
                    m_CertificateUpdate(this, args);
                }
            }
        }


        /// <summary>
        /// Validates the specified certificate against the trust list.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        public void Validate(X509Certificate2 certificate)
        {
            Validate(new X509Certificate2Collection() { certificate });
        }

        /// <summary>
        /// Validates a certificate.
        /// </summary>
        /// <remarks>
        /// Each UA application may have a list of trusted certificates that is different from 
        /// all other UA applications that may be running on the same machine. As a result, the
        /// certificate validator cannot rely completely on the Windows certificate store and
        /// user or machine specific CTLs (certificate trust lists).
        ///
        /// The validator constructs the trust chain for the certificate and follows the chain
        /// until it finds a certification that is in the application trust list. Non-fatal trust
        /// chain errors (i.e. certificate expired) are ignored if the certificate is in the 
        /// application trust list.
        ///
        /// If no certificate in the chain is trusted then the validator will still accept the
        /// certification if there are no trust chain errors.
        /// 
        /// The validator may be configured to ignore the application trust list and/or trust chain.
        /// </remarks>
        public virtual void Validate(X509Certificate2Collection chain)
        {
            X509Certificate2 certificate = chain[0];

            try
            {
                lock (m_lock)
                {

                    InternalValidate(chain).GetAwaiter().GetResult();

                    // add to list of validated certificates.
                    m_validatedCertificates[certificate.Thumbprint] = new X509Certificate2(certificate.RawData);
                }
            }
            catch (ServiceResultException se)
            {
                // check for errors that may be suppressed.
                switch (se.StatusCode)
                {
                    case StatusCodes.BadCertificateHostNameInvalid:
                    case StatusCodes.BadCertificateIssuerRevocationUnknown:
                    case StatusCodes.BadCertificateChainIncomplete:
                    case StatusCodes.BadCertificateIssuerTimeInvalid:
                    case StatusCodes.BadCertificateIssuerUseNotAllowed:
                    case StatusCodes.BadCertificateRevocationUnknown:
                    case StatusCodes.BadCertificateTimeInvalid:
                    case StatusCodes.BadCertificatePolicyCheckFailed:
                    case StatusCodes.BadCertificateUseNotAllowed:
                    case StatusCodes.BadCertificateUntrusted:
                    {
                        Utils.Trace("Certificate Vaildation failed for '{0}'. Reason={1}", certificate.Subject, (StatusCode)se.StatusCode);
                        break;
                    }

                    default:
                    {
                        // write the invalid certificate to rejected store if specified.
                        Utils.Trace((int)Utils.TraceMasks.Error, "Certificate '{0}' rejected. Reason={1}", certificate.Subject, (StatusCode)se.StatusCode);
                        SaveCertificate(certificate);

                        throw new ServiceResultException(se, StatusCodes.BadCertificateInvalid);
                    }
                }

                // invoke callback.
                bool accept = false;

                lock (m_callbackLock)
                {
                    if (m_CertificateValidation != null)
                    {
                        CertificateValidationEventArgs args = new CertificateValidationEventArgs(new ServiceResult(se), certificate);
                        m_CertificateValidation(this, args);
                        accept = args.Accept;
                    }
                }

                // throw if rejected.
                if (!accept)
                {
                    // write the invalid certificate to rejected store if specified.
                    Utils.Trace((int)Utils.TraceMasks.Error, "Certificate '{0}' rejected. Reason={1}", certificate.Subject, (StatusCode)se.StatusCode);
                    SaveCertificate(certificate);

                    throw new ServiceResultException(se, StatusCodes.BadCertificateInvalid);
                }

                // add to list of peers.
                lock (m_lock)
                {
                    Utils.Trace("Validation error suppressed for '{0}'.", certificate.Subject);
                    m_validatedCertificates[certificate.Thumbprint] = new X509Certificate2(certificate.RawData);
                }
            }
        }

        /// <summary>
        /// Saves the certificate in the rejected certificate store.
        /// </summary>
        private void SaveCertificate(X509Certificate2 certificate)
        {
            lock (m_lock)
            {
                if (m_rejectedCertificateStore != null)
                {
                    Utils.Trace((int)Utils.TraceMasks.Error, "Writing rejected certificate to directory: {0}", m_rejectedCertificateStore);
                    try
                    {
                        ICertificateStore store = m_rejectedCertificateStore.OpenStore();

                        try
                        {
                            store.Delete(certificate.Thumbprint);
                            store.Add(certificate);
                        }
                        finally
                        {
                            store.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Could not write certificate to directory: {0}", m_rejectedCertificateStore);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the certificate information for a trusted peer certificate.
        /// </summary>
        private async Task<CertificateIdentifier> GetTrustedCertificate(X509Certificate2 certificate)
        {
            string certificateThumbprint = certificate.Thumbprint.ToUpper();

            // check if explicitly trusted.
            if (m_trustedCertificateList != null)
            {
                for (int ii = 0; ii < m_trustedCertificateList.Count; ii++)
                {
                    X509Certificate2 trusted = await m_trustedCertificateList[ii].Find(false);

                    if (trusted != null && trusted.Thumbprint == certificate.Thumbprint)
                    {
                        if (Utils.IsEqual(trusted.RawData, certificate.RawData))
                        {
                            return m_trustedCertificateList[ii];
                        }
                    }
                }
            }

            // check if in peer trust store.
            if (m_trustedCertificateStore != null)
            {
                ICertificateStore store = m_trustedCertificateStore.OpenStore();

                try
                {
                    X509Certificate2Collection trusted = await store.FindByThumbprint(certificate.Thumbprint);

                    for (int ii = 0; ii < trusted.Count; ii++)
                    {
                        if (Utils.IsEqual(trusted[ii].RawData, certificate.RawData))
                        {
                            return new CertificateIdentifier(trusted[ii], m_trustedCertificateStore.ValidationOptions);
                        }
                    }
                }
                finally
                {
                    store.Close();
                }
            }

            // not a trusted.
            return null;
        }

        /// <summary>
        /// Returns the authority key identifier in the certificate.
        /// </summary>
        private X509AuthorityKeyIdentifierExtension FindAuthorityKeyIdentifier(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509Extension extension = certificate.Extensions[ii];

                switch (extension.Oid.Value)
                {
                    case X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifierOid:
                    case X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid:
                    {
                        return new X509AuthorityKeyIdentifierExtension(extension, extension.Critical);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Determines whether the certificate is allowed to be an issuer.
        /// </summary>
        private bool IsIssuerAllowed(X509Certificate2 certificate)
        {
            X509BasicConstraintsExtension constraints = new X509BasicConstraintsExtension();

            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                constraints = certificate.Extensions[ii] as X509BasicConstraintsExtension;

                if (constraints != null)
                {
                    return constraints.CertificateAuthority;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the subject key identifier in the certificate.
        /// </summary>
        private X509SubjectKeyIdentifierExtension FindSubjectKeyIdentifierExtension(X509Certificate2 certificate)
        {
            for (int ii = 0; ii < certificate.Extensions.Count; ii++)
            {
                X509SubjectKeyIdentifierExtension extension = certificate.Extensions[ii] as X509SubjectKeyIdentifierExtension;

                if (extension != null)
                {
                    return extension;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the certificate matches the criteria.
        /// </summary>
        private bool Match(
            X509Certificate2 certificate,
            string subjectName,
            string serialNumber,
            string authorityKeyId)
        {
            // check for null.
            if (certificate == null)
            {
                return false;
            }

            // check for subject name match.
            if (!Utils.CompareDistinguishedName(certificate.SubjectName.Name, subjectName))
            {
                return false;
            }

            // check for serial number match.
            if (!String.IsNullOrEmpty(serialNumber))
            {
                if (certificate.SerialNumber != serialNumber)
                {
                    return false;
                }
            }

            // check for authority key id match.
            if (!String.IsNullOrEmpty(authorityKeyId))
            {
                X509SubjectKeyIdentifierExtension subjectKeyId = FindSubjectKeyIdentifierExtension(certificate);

                if (subjectKeyId != null)
                {
                    if (subjectKeyId.SubjectKeyIdentifier != authorityKeyId)
                    {
                        return false;
                    }
                }
            }

            // found match.
            return true;
        }

        /// <summary>
        /// Returns the issuers for the certificates.
        /// </summary>
        public async Task<bool> GetIssuers(X509Certificate2Collection certificates, List<CertificateIdentifier> issuers)
        {
            bool isTrusted = false;
            CertificateIdentifier issuer = null;
            X509Certificate2 certificate = certificates[0];

            CertificateIdentifierCollection collection = new CertificateIdentifierCollection();
            for (int ii = 1; ii < certificates.Count; ii++)
            {
                collection.Add(new CertificateIdentifier(certificates[ii]));
            }

            do
            {
                issuer = await GetIssuer(certificate, m_trustedCertificateList, m_trustedCertificateStore, true);

                if (issuer == null)
                {
                    issuer = await GetIssuer(certificate, m_issuerCertificateList, m_issuerCertificateStore, true);

                    if (issuer == null)
                    {
                        issuer = await GetIssuer(certificate, collection, null, true);
                    }
                }
                else
                {
                    isTrusted = true;
                }

                if (issuer != null)
                {
                    issuers.Add(issuer);
                    certificate = await issuer.Find(false);

                    // check for root.
                    if (Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer))
                    {
                        break;
                    }
                }
            }
            while (issuer != null);

            return isTrusted;
        }

        /// <summary>
        /// Returns the issuers for the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="issuers">The issuers.</param>
        /// <returns></returns>
        public Task<bool> GetIssuers(X509Certificate2 certificate, List<CertificateIdentifier> issuers)
        {
            return GetIssuers(new X509Certificate2Collection { certificate }, issuers);
        }

        /// <summary>
        /// Returns the certificate information for a trusted issuer certificate.
        /// </summary>
        private async Task<CertificateIdentifier> GetIssuer(
            X509Certificate2 certificate,
            CertificateIdentifierCollection explicitList,
            CertificateStoreIdentifier certificateStore,
            bool checkRecovationStatus)
        {
            // check if self-signed.
            if (Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer))
            {
                return null;
            }

            string subjectName = certificate.IssuerName.Name;
            string keyId = null;
            string serialNumber = null;

            // find the authority key identifier.
            X509AuthorityKeyIdentifierExtension authority = FindAuthorityKeyIdentifier(certificate);

            if (authority != null)
            {
                keyId = authority.KeyId;
                serialNumber = authority.SerialNumber;
            }

            // check in explicit list.
            if (explicitList != null)
            {
                for (int ii = 0; ii < explicitList.Count; ii++)
                {
                    X509Certificate2 issuer = await explicitList[ii].Find(false);

                    if (issuer != null)
                    {
                        if (!IsIssuerAllowed(issuer))
                        {
                            continue;
                        }

                        if (Match(issuer, subjectName, serialNumber, keyId))
                        {
                            // can't check revocation.
                            return new CertificateIdentifier(issuer, CertificateValidationOptions.SuppressRevocationStatusUnknown);
                        }
                    }
                }
            }

            // check in certificate store.
            if (certificateStore != null)
            {
                ICertificateStore store = certificateStore.OpenStore();

                try
                {
                    X509Certificate2Collection certificates = await store.Enumerate();

                    for (int ii = 0; ii < certificates.Count; ii++)
                    {
                        X509Certificate2 issuer = certificates[ii];

                        if (issuer != null)
                        {
                            if (!IsIssuerAllowed(issuer))
                            {
                                continue;
                            }

                            if (Match(issuer, subjectName, serialNumber, keyId))
                            {
                                CertificateValidationOptions options = certificateStore.ValidationOptions;

                                // already checked revocation for file based stores. windows based stores always suppress.
                                options |= CertificateValidationOptions.SuppressRevocationStatusUnknown;

                                if (checkRecovationStatus)
                                {
                                    StatusCode status = store.IsRevoked(issuer, certificate);

                                    if (StatusCode.IsBad(status) && status != StatusCodes.BadNotSupported)
                                    {
                                        if (status == StatusCodes.BadCertificateRevocationUnknown)
                                        {
                                            if (CertificateFactory.IsCertificateAuthority(certificate))
                                            {
                                                status.Code = StatusCodes.BadCertificateIssuerRevocationUnknown;
                                            }

                                            if (m_rejectUnknownRevocationStatus)
                                            {
                                                throw new ServiceResultException(status);
                                            }
                                        }
                                        else
                                        {
                                            throw new ServiceResultException(status);
                                        }
                                    }
                                }

                                return new CertificateIdentifier(certificates[ii], options);
                            }
                        }
                    }
                }
                finally
                {
                    store.Close();
                }
            }

            // not a trusted issuer.
            return null;
        }

        /// <summary>
        /// Throws an exception if validation fails.
        /// </summary>
        /// <param name="certificates">The certificates to be checked.</param>
        /// <exception cref="ServiceResultException">If certificate[0] cannot be accepted</exception>
        protected virtual async Task InternalValidate(X509Certificate2Collection certificates)
        {
            X509Certificate2 certificate = certificates[0];

            // check for previously validated certificate.
            X509Certificate2 certificate2 = null;

            if (m_validatedCertificates.TryGetValue(certificate.Thumbprint, out certificate2))
            {
                if (Utils.IsEqual(certificate2.RawData, certificate.RawData))
                {
                    return;
                }
            }

            CertificateIdentifier trustedCertificate = await GetTrustedCertificate(certificate);

            // get the issuers (checks the revocation lists if using directory stores).
            List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
            bool isIssuerTrusted = await GetIssuers(certificates, issuers);

            // setup policy chain
            X509ChainPolicy policy = new X509ChainPolicy();
            policy.RevocationFlag = X509RevocationFlag.EntireChain;
            policy.RevocationMode = X509RevocationMode.NoCheck;
            policy.VerificationFlags = X509VerificationFlags.NoFlag;

            foreach (CertificateIdentifier issuer in issuers)
            {
                if ((issuer.ValidationOptions & CertificateValidationOptions.SuppressRevocationStatusUnknown) != 0)
                {
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreCtlSignerRevocationUnknown;
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreEndRevocationUnknown;
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreRootRevocationUnknown;
                }

                // we did the revocation check in the GetIssuers call. No need here.
                policy.RevocationMode = X509RevocationMode.NoCheck;
                policy.ExtraStore.Add(issuer.Certificate);
            }

            // build chain.
            bool chainStatusChecked = false;
            X509Chain chain = new X509Chain();
            chain.ChainPolicy = policy;
            chain.Build(certificate);

            // check the chain results.
            CertificateIdentifier target = trustedCertificate;

            if (target == null)
            {
                target = new CertificateIdentifier(certificate);
            }

            for (int ii = 0; ii < chain.ChainElements.Count; ii++)
            {
                X509ChainElement element = chain.ChainElements[ii];

                CertificateIdentifier issuer = null;

                if (ii < issuers.Count)
                {
                    issuer = issuers[ii];
                }

                // check for chain status errors.
                if (element.ChainElementStatus.Length > 0)
                {
                    foreach (X509ChainStatus status in element.ChainElementStatus)
                    {
                        ServiceResult result = CheckChainStatus(status, target, issuer, (ii != 0));

                        if (ServiceResult.IsBad(result))
                        {
                            // check untrusted certificates.
                            if (trustedCertificate == null)
                            {
                                ServiceResult errorResult = new ServiceResult(
                                    result.StatusCode,
                                    result.SymbolicId,
                                    result.NamespaceUri,
                                    result.LocalizedText,
                                    result.AdditionalInfo,
                                    StatusCodes.BadCertificateUntrusted);

                                throw new ServiceResultException(errorResult);
                            }

                            throw new ServiceResultException(result);
                        }

                        chainStatusChecked = true;
                    }
                }
                else
                {
                    chainStatusChecked = true;
                }

                if (issuer != null)
                {
                    target = issuer;
                }
            }

            // check whether the chain is complete (if there is a chain)
            bool issuedByCA = !Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer);
            bool chainIncomplete = false;
            if (issuers.Count > 0)
            {
                var rootCertificate = issuers[issuers.Count - 1].Certificate;
                if (!Utils.CompareDistinguishedName(rootCertificate.Subject, rootCertificate.Issuer))
                {
                    chainIncomplete = true;
                }
            }
            else
            {
                if (issuedByCA)
                {
                    // no issuer found at all
                    chainIncomplete = true;
                }
            }

            if (issuedByCA && (!chainStatusChecked || chainIncomplete))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateChainIncomplete,
                    "Certificate chain validation incomplete.\r\nSubjectName: {0}\r\nIssuerName: {1}",
                    certificate.SubjectName.Name,
                    certificate.IssuerName.Name);
            }

            // check if certificate issuer is trusted.
            if (issuedByCA && !isIssuerTrusted && trustedCertificate == null)
            {
                if (m_applicationCertificate == null || !Utils.IsEqual(m_applicationCertificate.RawData, certificate.RawData))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateUntrusted,
                        "Certificate issuer is not trusted.\r\nSubjectName: {0}\r\nIssuerName: {1}",
                        certificate.SubjectName.Name,
                        certificate.IssuerName.Name);
                }
            }

            // check if certificate is trusted.
            if (trustedCertificate == null && !isIssuerTrusted)
            {
                if (m_applicationCertificate == null || !Utils.IsEqual(m_applicationCertificate.RawData, certificate.RawData))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadCertificateUntrusted,
                        "Certificate is not trusted.\r\nSubjectName: {0}\r\nIssuerName: {1}",
                        certificate.SubjectName.Name,
                        certificate.IssuerName.Name);
                }
            }

            // check if certificate is valid for use as app/sw or user cert
            X509KeyUsageFlags certificateKeyUsage = CertificateFactory.GetKeyUsage(certificate);

            if ((certificateKeyUsage & X509KeyUsageFlags.DataEncipherment) == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUseNotAllowed, "Usage of certificate is not allowed.");
            }

            // check if minimum requirements are met
            if (m_rejectSHA1SignedCertificates && IsSHA1SignatureAlgorithm(certificate.SignatureAlgorithm))
            {
                throw new ServiceResultException(StatusCodes.BadCertificatePolicyCheckFailed, "SHA1 signed certificates are not trusted");
            }

            int keySize = CertificateFactory.GetRSAPublicKeySize(certificate);
            if (keySize < m_minimumCertificateKeySize)
            {
                throw new ServiceResultException(StatusCodes.BadCertificatePolicyCheckFailed, "Certificate doesn't meet minimum key length requirement");
            }
        }

        /// <summary>
        /// Returns an object that can be used with a UA channel.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public ICertificateValidator GetChannelValidator()
        {
            return this as ICertificateValidator;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns an error if the chain status indicates a fatal error.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private static ServiceResult CheckChainStatus(X509ChainStatus status, CertificateIdentifier id, CertificateIdentifier issuer, bool isIssuer)
        {
            switch (status.Status)
            {
                case X509ChainStatusFlags.NotValidForUsage:
                {
                    return ServiceResult.Create(
                        (isIssuer) ? StatusCodes.BadCertificateUseNotAllowed : StatusCodes.BadCertificateIssuerUseNotAllowed,
                        "Certificate may not be used as an application instance certificate. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }

                case X509ChainStatusFlags.NoError:
                case X509ChainStatusFlags.OfflineRevocation:
                case X509ChainStatusFlags.InvalidBasicConstraints:
                case X509ChainStatusFlags.PartialChain:
                {
                    break;
                }

                case X509ChainStatusFlags.UntrustedRoot:
                {
                    // self signed cert signature validation 
                    // .Net Core ChainStatus returns NotSignatureValid only on Windows, 
                    // so we have to do the extra cert signature check on all platforms
                    if (issuer == null && !isIssuer &&
                        id.Certificate != null && Utils.CompareDistinguishedName(id.Certificate.Subject, id.Certificate.Issuer))
                    {
                        if (!IsSignatureValid(id.Certificate))
                        {
                            goto case X509ChainStatusFlags.NotSignatureValid;
                        }
                    }

                    // ignore this error because the root check is done
                    // by looking the certificate up in the trusted issuer stores passed to the validator.
                    // the ChainStatus uses the trusted issuer stores.
                    break;
                }

                case X509ChainStatusFlags.RevocationStatusUnknown:
                {
                    if (issuer != null)
                    {
                        if ((issuer.ValidationOptions & CertificateValidationOptions.SuppressRevocationStatusUnknown) != 0)
                        {
                            break;
                        }
                    }

                    // check for meaning less errors for self-signed certificates.
                    if (id.Certificate != null && Utils.CompareDistinguishedName(id.Certificate.Subject, id.Certificate.Subject))
                    {
                        break;
                    }

                    return ServiceResult.Create(
                        (isIssuer) ? StatusCodes.BadCertificateIssuerRevocationUnknown : StatusCodes.BadCertificateRevocationUnknown,
                        "Certificate revocation status cannot be verified. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }

                case X509ChainStatusFlags.Revoked:
                {
                    return ServiceResult.Create(
                        (isIssuer) ? StatusCodes.BadCertificateIssuerRevoked : StatusCodes.BadCertificateRevoked,
                        "Certificate has been revoked. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }

                case X509ChainStatusFlags.NotTimeNested:
                {
                    if (id != null && ((id.ValidationOptions & CertificateValidationOptions.SuppressCertificateExpired) != 0))
                    {
                        break;
                    }

                    return ServiceResult.Create(
                        StatusCodes.BadCertificateIssuerTimeInvalid,
                        "Certificate issuer validatity time does not overhas is expired or not yet valid. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }

                case X509ChainStatusFlags.NotTimeValid:
                {
                    if (id != null && ((id.ValidationOptions & CertificateValidationOptions.SuppressCertificateExpired) != 0))
                    {
                        break;
                    }

                    return ServiceResult.Create(
                        (isIssuer) ? StatusCodes.BadCertificateIssuerTimeInvalid : StatusCodes.BadCertificateTimeInvalid,
                        "Certificate has is expired or not yet valid. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }

                case X509ChainStatusFlags.NotSignatureValid:
                {
                    return ServiceResult.Create(
                        StatusCodes.BadCertificateUntrusted,
                        status.StatusInformation);
                }

                default:
                {
                    return ServiceResult.Create(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate validation failed. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                }
            }

            return null;
        }
        /// <summary>
        /// Returns if a certificate is signed with a SHA1 algorithm.
        /// </summary>
        private static bool IsSHA1SignatureAlgorithm(Oid oid)
        {
            return oid.Value == "1.3.14.3.2.29" ||     // sha1RSA
                oid.Value == "1.2.840.10040.4.3" ||    // sha1DSA
                oid.Value == "1.2.840.10045.4.1" ||    // sha1ECDSA
                oid.Value == "1.2.840.113549.1.1.5" || // sha1RSA
                oid.Value == "1.3.14.3.2.13" ||        // sha1DSA
                oid.Value == "1.3.14.3.2.27";          // dsaSHA1
        }
        /// <summary>
        /// Returns if a self signed certificate is properly signed.
        /// </summary>
        private static bool IsSignatureValid(X509Certificate2 cert)
        {
            return CertificateFactory.VerifySelfSigned(cert);
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private object m_callbackLock = new object();
        private Dictionary<string, X509Certificate2> m_validatedCertificates;
        private CertificateStoreIdentifier m_trustedCertificateStore;
        private CertificateIdentifierCollection m_trustedCertificateList;
        private CertificateStoreIdentifier m_issuerCertificateStore;
        private CertificateIdentifierCollection m_issuerCertificateList;
        private CertificateStoreIdentifier m_rejectedCertificateStore;
        private event CertificateValidationEventHandler m_CertificateValidation;
        private event CertificateUpdateEventHandler m_CertificateUpdate;
        private X509Certificate2 m_applicationCertificate;
        private bool m_rejectSHA1SignedCertificates;
        private bool m_rejectUnknownRevocationStatus;
        private ushort m_minimumCertificateKeySize;
        #endregion
    }

    #region CertificateValidationEventArgs Class
    /// <summary>
    /// The event arguments provided when a certificate validation error occurs.
    /// </summary>
    public class CertificateValidationEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal CertificateValidationEventArgs(ServiceResult error, X509Certificate2 certificate)
        {
            m_error = error;
            m_certificate = certificate;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The error that occurred.
        /// </summary>
        public ServiceResult Error => m_error;

        /// <summary>
        /// The certificate.
        /// </summary>
        public X509Certificate2 Certificate => m_certificate;

        /// <summary>
        /// Whether the certificate should be accepted.
        /// </summary>
        public bool Accept
        {
            get { return m_accept; }
            set { m_accept = value; }
        }
        #endregion

        #region Private Fields
        private ServiceResult m_error;
        private X509Certificate2 m_certificate;
        private bool m_accept;
        #endregion
    }


    /// <summary>
    /// Used to handled certificate validation errors.
    /// </summary>
    public delegate void CertificateValidationEventHandler(CertificateValidator sender, CertificateValidationEventArgs e);
    #endregion

    #region CertificateUpdateEventArgs Class
    /// <summary>
    /// The event arguments provided when a certificate validation error occurs.
    /// </summary>
    public class CertificateUpdateEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal CertificateUpdateEventArgs(
            SecurityConfiguration configuration,
            ICertificateValidator validator)
        {
            SecurityConfiguration = configuration;
            CertificateValidator = validator;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The new security configuration.
        /// </summary>
        public SecurityConfiguration SecurityConfiguration { get; private set; }
        /// <summary>
        /// The new certificate validator.
        /// </summary>
        public ICertificateValidator CertificateValidator { get; private set; }

        #endregion
    }


    /// <summary>
    /// Used to handle certificate update events.
    /// </summary>
    public delegate void CertificateUpdateEventHandler(CertificateValidator sender, CertificateUpdateEventArgs e);

    #endregion

}
