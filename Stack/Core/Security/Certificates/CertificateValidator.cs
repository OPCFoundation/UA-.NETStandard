/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua
{
    public class X509CertificateValidator
    {
        public virtual void Validate(X509Certificate2 cert) { }
    }

    /// <summary>
    /// Validates certificates.
    /// </summary>
    public class CertificateValidator
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateValidator()
        {
            m_validatedCertificates = new Dictionary<string,X509Certificate2>();
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
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        public virtual async Task Update(ApplicationConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");

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
            if (configuration == null) throw new ArgumentNullException("configuration");

            lock (m_lock)
            {
                Update(
                    configuration.TrustedIssuerCertificates,
                    configuration.TrustedPeerCertificates,
                    configuration.RejectedCertificateStore);
            }

            if (configuration.ApplicationCertificate != null)
            {
                m_applicationCertificate = await configuration.ApplicationCertificate.Find(false);
            }
        }

        /// <summary>
        /// Validates the specified certificate against the trust list.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        public virtual void Validate(X509Certificate2Collection chain)
        {
            foreach (X509Certificate2 certificate in chain)
            {
                Validate(certificate);
            }
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
        public virtual void Validate(X509Certificate2 certificate)
        {
            try
            {
                Task.Run(async () =>
                {
                    await InternalValidate(certificate);
                }).Wait();

                // add to list of validated certificates.
                lock (m_lock)
                {
                    m_validatedCertificates[certificate.Thumbprint] = certificate;
                }
            }
            catch (AggregateException ae)
            {
                foreach (ServiceResultException e in ae.InnerExceptions)
                {
                    // check for errors that may be suppressed.
                    switch (e.StatusCode)
                    {
                        case StatusCodes.BadCertificateHostNameInvalid:
                        case StatusCodes.BadCertificateIssuerRevocationUnknown:
                        case StatusCodes.BadCertificateIssuerTimeInvalid:
                        case StatusCodes.BadCertificateIssuerUseNotAllowed:
                        case StatusCodes.BadCertificateRevocationUnknown:
                        case StatusCodes.BadCertificateTimeInvalid:
                        case StatusCodes.BadCertificateUriInvalid:
                        case StatusCodes.BadCertificateUseNotAllowed:
                        case StatusCodes.BadCertificateUntrusted:
                            {
                                Utils.Trace("Cert Validate failed: {0}", (StatusCode)e.StatusCode);
                                break;
                            }

                        default:
                            {
                                throw new ServiceResultException(e, StatusCodes.BadCertificateInvalid);
                            }
                    }

                    // invoke callback.
                    bool accept = false;

                    lock (m_callbackLock)
                    {
                        if (m_CertificateValidation != null)
                        {
                            CertificateValidationEventArgs args = new CertificateValidationEventArgs(new ServiceResult(e), certificate);
                            m_CertificateValidation(this, args);
                            accept = args.Accept;
                        }
                    }

                    // throw if rejected.
                    if (!accept)
                    {
                        // write the invalid certificate to a directory if specified.
                        lock (m_lock)
                        {
                            Utils.Trace((int)Utils.TraceMasks.Error, "Certificate '{0}' rejected. Reason={1}", certificate.Subject, (StatusCode)e.StatusCode);

                            if (m_rejectedCertificateStore != null)
                            {
                                Utils.Trace((int)Utils.TraceMasks.Error, "Writing rejected certificate to directory: {0}", m_rejectedCertificateStore);
                                SaveCertificate(certificate);
                            }
                        }

                        throw new ServiceResultException(e, StatusCodes.BadCertificateInvalid);
                    }

                    // add to list of peers.
                    lock (m_lock)
                    {
                        m_validatedCertificates[certificate.Thumbprint] = certificate;
                    }
                }
            }
        }

        /// <summary>
        /// Saves the certificate in the invalid certificate directory.
        /// </summary>
        private void SaveCertificate(X509Certificate2 certificate)
        {
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

                    if (trusted.Count > 0)
                    {
                        if (Utils.IsEqual(trusted[0].RawData, certificate.RawData))
                        {
                            return new CertificateIdentifier(trusted[0], m_trustedCertificateStore.ValidationOptions);
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
        /// Returns the authority key identifier in the certificate.
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
        public async Task<bool> GetIssuers(X509Certificate2 certificate, List<CertificateIdentifier> issuers)
        {
            bool isTrusted = false;
            CertificateIdentifier issuer = null;

            do
            {
                issuer = await GetIssuer(certificate, m_trustedCertificateList, m_trustedCertificateStore, true);

                if (issuer == null)
                {
                    issuer = await GetIssuer(certificate, m_issuerCertificateList, m_issuerCertificateStore, true);
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
        /// Returns the certificate information for a trusted issuer certificate.
        /// </summary>
        private async Task<CertificateIdentifier> GetIssuer(
            X509Certificate2 certificate,
            CertificateIdentifierCollection explicitList,
            CertificateStoreIdentifier certificateStore,
            bool checkRecovationStatus)
        {
            string subjectName = certificate.IssuerName.ToString();
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

                                    if (StatusCode.IsBad(status))
                                    {
                                        if (status != StatusCodes.BadNotSupported && status != StatusCodes.BadCertificateRevocationUnknown)
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
                foreach (X509ChainStatus status in element.ChainElementStatus)
                {
                    ServiceResult result = CheckChainStatus(status, target, issuer, (ii != 0));

                    if (ServiceResult.IsBad(result))
                    {
                        // check untrusted certificates.
                        if (trustedCertificate == null)
                        {
                            throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
                        }

                        throw new ServiceResultException(result);
                    }
                }

                if (issuer != null)
                {
                    target = issuer;
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
                    certificate.IssuerName);
                }
            }
        }

        /// <summary>
        /// Throws an exception if validation fails.
        /// </summary>
        /// <param name="certificate">The certificate to be checked.</param>
        /// <exception cref="ServiceResultException">If certificate cannot be accepted</exception>
        protected async virtual Task InternalValidate(X509Certificate2 certificate)
        {
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
            bool isIssuerTrusted = await GetIssuers(certificate, issuers);

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
                foreach (X509ChainStatus status in element.ChainElementStatus)
                {
                    ServiceResult result = CheckChainStatus(status, target, issuer, (ii != 0));

                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                }

                if (issuer != null)
                {
                    target = issuer;
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
                    certificate.IssuerName);
                }
            }
        }

        /// <summary>
        /// Returns an object that can be used with WCF channel.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public X509CertificateValidator GetChannelValidator()
        {
            return new WcfValidatorWrapper(this);
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
                    // ignore this error because the root check is done
                    // by looking the certificate up in the trusted issuer stores passed to the validator.
                    // the ChainStatus uses the Windows trusted issuer stores.
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
        #endregion

        #region WcfValidatorWrapper Class
        /// <summary>
        /// Wraps a WCF validator so the validator can be used in WCF bindings.
        /// </summary>
        internal class WcfValidatorWrapper : X509CertificateValidator
        {
            public WcfValidatorWrapper(CertificateValidator validator)
            {
                m_validator = validator;
            }

            public override void Validate(X509Certificate2 certificate)
            {
                m_validator.Validate(certificate);
            }

            private CertificateValidator m_validator;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private object m_callbackLock = new object();
        private Dictionary<string,X509Certificate2> m_validatedCertificates;
        private CertificateStoreIdentifier m_trustedCertificateStore;
        private CertificateIdentifierCollection m_trustedCertificateList;
        private CertificateStoreIdentifier m_issuerCertificateStore;
        private CertificateIdentifierCollection m_issuerCertificateList;
        private CertificateStoreIdentifier m_rejectedCertificateStore;
        private event CertificateValidationEventHandler m_CertificateValidation;
        private X509Certificate2 m_applicationCertificate;
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
        public ServiceResult Error
        {
            get { return m_error; }
        }

        /// <summary>
        /// The certificate.
        /// </summary>
        public X509Certificate2 Certificate
        {
            get { return m_certificate; }
        }

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
}
