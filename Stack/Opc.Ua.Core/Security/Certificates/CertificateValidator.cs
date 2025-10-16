/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Security.Certificates;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua
{
    /// <summary>
    /// Validates certificates.
    /// </summary>
    public class CertificateValidator : ICertificateValidator
    {
        /// <summary>
        /// default number of rejected certificates for history
        /// </summary>
        private const int kDefaultMaxRejectedCertificates = 5;

        /// <summary>
        /// Create validator
        /// </summary>
        [Obsolete("Use CertificateValidator(ITelemetryContext) instead.")]
        public CertificateValidator()
            : this(null)
        {
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateValidator(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateValidator>();
            m_validatedCertificates = [];
            m_applicationCertificates = [];
            m_protectFlags = 0;
            m_autoAcceptUntrustedCertificates = false;
            m_rejectSHA1SignedCertificates = CertificateFactory.DefaultHashSize >= 256;
            m_rejectUnknownRevocationStatus = false;
            m_minimumCertificateKeySize = CertificateFactory.DefaultKeySize;
            m_useValidatedCertificates = false;
            m_maxRejectedCertificates = kDefaultMaxRejectedCertificates;
        }

        /// <summary>
        /// Raised when a certificate validation error occurs.
        /// </summary>
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
        [Obsolete("Use UpdateAsync instead.")]
        public virtual Task Update(ApplicationConfiguration configuration)
        {
            return UpdateAsync(configuration);
        }

        /// <summary>
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public virtual async Task UpdateAsync(
            ApplicationConfiguration configuration,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            await UpdateAsync(
                configuration.SecurityConfiguration,
                applicationUri: null,
                ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Updates the validator with a new set of trust lists.
        /// </summary>
        public virtual void Update(
            CertificateTrustList issuerStore,
            CertificateTrustList trustedStore,
            CertificateStoreIdentifier rejectedCertificateStore)
        {
            m_semaphore.Wait();

            try
            {
                InternalUpdate(issuerStore, trustedStore, rejectedCertificateStore);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Updates the validator with a new set of trust lists.
        /// </summary>
        private void InternalUpdate(
            CertificateTrustList issuerStore,
            CertificateTrustList trustedStore,
            CertificateStoreIdentifier rejectedCertificateStore)
        {
            InternalResetValidatedCertificates();

            m_trustedCertificateStore = null;
            m_trustedCertificateList = null;
            if (trustedStore != null)
            {
                m_trustedCertificateStore = new CertificateStoreIdentifier(trustedStore.StorePath)
                {
                    ValidationOptions = trustedStore.ValidationOptions
                };

                if (trustedStore.TrustedCertificates != null)
                {
                    m_trustedCertificateList = [.. trustedStore.TrustedCertificates];
                }
            }

            m_issuerCertificateStore = null;
            m_issuerCertificateList = null;
            if (issuerStore != null)
            {
                m_issuerCertificateStore = new CertificateStoreIdentifier(issuerStore.StorePath)
                {
                    ValidationOptions = issuerStore.ValidationOptions
                };

                if (issuerStore.TrustedCertificates != null)
                {
                    m_issuerCertificateList = [.. issuerStore.TrustedCertificates];
                }
            }

            m_rejectedCertificateStore = null;
            if (rejectedCertificateStore != null)
            {
                m_rejectedCertificateStore = (CertificateStoreIdentifier)rejectedCertificateStore
                    .MemberwiseClone();
            }
        }

        /// <summary>
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public virtual async Task UpdateAsync(
            SecurityConfiguration configuration,
            string applicationUri = null,
            CancellationToken ct = default)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                InternalUpdate(
                    configuration.TrustedIssuerCertificates,
                    configuration.TrustedPeerCertificates,
                    configuration.RejectedCertificateStore);

                // protect the flags if application called to set property
                if ((m_protectFlags & ProtectFlags.AutoAcceptUntrustedCertificates) == 0)
                {
                    m_autoAcceptUntrustedCertificates = configuration
                        .AutoAcceptUntrustedCertificates;
                }
                if ((m_protectFlags & ProtectFlags.RejectSHA1SignedCertificates) == 0)
                {
                    m_rejectSHA1SignedCertificates = configuration.RejectSHA1SignedCertificates;
                }
                if ((m_protectFlags & ProtectFlags.RejectUnknownRevocationStatus) == 0)
                {
                    m_rejectUnknownRevocationStatus = configuration.RejectUnknownRevocationStatus;
                }
                if ((m_protectFlags & ProtectFlags.MinimumCertificateKeySize) == 0)
                {
                    m_minimumCertificateKeySize = configuration.MinimumCertificateKeySize;
                }
                if ((m_protectFlags & ProtectFlags.UseValidatedCertificates) == 0)
                {
                    m_useValidatedCertificates = configuration.UseValidatedCertificates;
                }
                if ((m_protectFlags & ProtectFlags.MaxRejectedCertificates) == 0)
                {
                    m_maxRejectedCertificates = configuration.MaxRejectedCertificates;
                }

                if (configuration.ApplicationCertificates != null)
                {
                    foreach (CertificateIdentifier applicationCertificate in configuration
                        .ApplicationCertificates)
                    {
                        X509Certificate2 certificate = await applicationCertificate
                            .FindAsync(true, applicationUri, m_telemetry, ct)
                            .ConfigureAwait(false);
                        if (certificate == null)
                        {
                            m_logger.LogInformation(
                                Utils.TraceMasks.Security,
                                "Could not find application certificate: {ApplicationCert}",
                                applicationCertificate);
                            continue;
                        }
                        // Add to list of application certificates only if not already in list
                        // necessary since the application certificates may be updated multiple times
                        if (!m_applicationCertificates.Exists(
                            cert => Utils.IsEqual(cert.RawData, certificate.RawData)))
                        {
                            m_applicationCertificates.Add(certificate);
                        }
                    }
                }
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Updates the validator with a new application certificate.
        /// </summary>
        public virtual async Task UpdateCertificateAsync(
            SecurityConfiguration securityConfiguration,
            string applicationUri = null,
            CancellationToken ct = default)
        {
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                m_applicationCertificates.Clear();
                foreach (CertificateIdentifier applicationCertificate in securityConfiguration
                    .ApplicationCertificates)
                {
                    applicationCertificate.DisposeCertificate();
                }

                foreach (CertificateIdentifier applicationCertificate in securityConfiguration
                    .ApplicationCertificates)
                {
                    await applicationCertificate
                        .LoadPrivateKeyExAsync(
                            securityConfiguration.CertificatePasswordProvider,
                            applicationUri,
                            m_telemetry,
                            ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_semaphore.Release();
            }

            await UpdateAsync(securityConfiguration, applicationUri, ct).ConfigureAwait(false);

            lock (m_callbackLock)
            {
                if (m_CertificateUpdate != null)
                {
                    var args = new CertificateUpdateEventArgs(
                        securityConfiguration,
                        GetChannelValidator());
                    m_CertificateUpdate(this, args);
                }
            }
        }

        /// <summary>
        /// Reset the list of validated certificates.
        /// </summary>
        public void ResetValidatedCertificates()
        {
            m_semaphore.Wait();

            try
            {
                InternalResetValidatedCertificates();
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// If untrusted certificates should be accepted.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates
        {
            get => m_autoAcceptUntrustedCertificates;
            set
            {
                m_semaphore.Wait();

                try
                {
                    m_protectFlags |= ProtectFlags.AutoAcceptUntrustedCertificates;
                    if (m_autoAcceptUntrustedCertificates != value)
                    {
                        m_autoAcceptUntrustedCertificates = value;
                        InternalResetValidatedCertificates();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
        }

        /// <summary>
        /// If certificates using a SHA1 signature should be trusted.
        /// </summary>
        public bool RejectSHA1SignedCertificates
        {
            get => m_rejectSHA1SignedCertificates;
            set
            {
                m_semaphore.Wait();

                try
                {
                    m_protectFlags |= ProtectFlags.RejectSHA1SignedCertificates;
                    if (m_rejectSHA1SignedCertificates != value)
                    {
                        m_rejectSHA1SignedCertificates = value;
                        InternalResetValidatedCertificates();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
        }

        /// <summary>
        /// if certificates with unknown revocation status should be rejected.
        /// </summary>
        public bool RejectUnknownRevocationStatus
        {
            get => m_rejectUnknownRevocationStatus;
            set
            {
                m_semaphore.Wait();

                try
                {
                    m_protectFlags |= ProtectFlags.RejectUnknownRevocationStatus;
                    if (m_rejectUnknownRevocationStatus != value)
                    {
                        m_rejectUnknownRevocationStatus = value;
                        InternalResetValidatedCertificates();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
        }

        /// <summary>
        /// The minimum size of an RSA certificate key to be trusted.
        /// </summary>
        public ushort MinimumCertificateKeySize
        {
            get => m_minimumCertificateKeySize;
            set
            {
                m_semaphore.Wait();

                try
                {
                    m_protectFlags |= ProtectFlags.MinimumCertificateKeySize;
                    if (m_minimumCertificateKeySize != value)
                    {
                        m_minimumCertificateKeySize = value;
                        InternalResetValidatedCertificates();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Opt-In to use the already validated certificates for validation.
        /// </summary>
        public bool UseValidatedCertificates
        {
            get => m_useValidatedCertificates;
            set
            {
                m_semaphore.Wait();

                try
                {
                    m_protectFlags |= ProtectFlags.UseValidatedCertificates;
                    if (m_useValidatedCertificates != value)
                    {
                        m_useValidatedCertificates = value;
                        InternalResetValidatedCertificates();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Limits the number of certificates which are kept
        /// in the history before more rejected certificates are added.
        /// A negative value means no history is kept.
        /// A value of 0 means all history is kept.
        /// </summary>
        public int MaxRejectedCertificates
        {
            get => m_maxRejectedCertificates;
            set
            {
                m_semaphore.Wait();
                bool updateStore = false;
                try
                {
                    m_protectFlags |= ProtectFlags.MaxRejectedCertificates;
                    if (m_maxRejectedCertificates != value)
                    {
                        m_maxRejectedCertificates = value;
                        updateStore = true;
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }

                if (updateStore)
                {
                    // update the rejected store
                    Task.Run(async () => await SaveCertificatesAsync([]).ConfigureAwait(false));
                }
            }
        }

        /// <summary>
        /// Validates the specified certificate against the trust list.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        public void Validate(X509Certificate2 certificate)
        {
            Validate([certificate]);
        }

        /// <summary>
        /// Validates a certificate.
        /// </summary>
        /// <remarks>
        /// Each UA application may have a list of trusted certificates that is different from
        /// all other UA applications that may be running on the same machine. As a result, the
        /// certificate validator cannot rely completely on the Windows certificate store and
        /// user or machine specific CTLs (certificate trust lists).
        /// </remarks>
        public virtual void Validate(X509Certificate2Collection certificateChain)
        {
            Validate(certificateChain, null);
        }

        /// <inheritdoc/>
        public Task ValidateAsync(X509Certificate2 certificate, CancellationToken ct)
        {
            return ValidateAsync([certificate], ct);
        }

        /// <inheritdoc/>
        public virtual Task ValidateAsync(
            X509Certificate2Collection certificateChain,
            CancellationToken ct)
        {
            return ValidateAsync(certificateChain, null, ct);
        }

        /// <summary>
        /// Validates a certificate with domain validation check.
        /// <see cref="ValidateAsync(X509Certificate2Collection, CancellationToken)"/>
        /// </summary>
        public virtual async Task ValidateAsync(
            X509Certificate2Collection chain,
            ConfiguredEndpoint endpoint,
            CancellationToken ct)
        {
            X509Certificate2 certificate = chain[0];

            try
            {
                await m_semaphore.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    await InternalValidateAsync(chain, endpoint, ct).ConfigureAwait(false);

                    // add to list of validated certificates.
                    m_validatedCertificates[certificate.Thumbprint] = X509CertificateLoader
                        .LoadCertificate(
                            certificate.RawData);

                    return;
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
            catch (ServiceResultException se)
            {
                HandleCertificateValidationException(se, certificate, chain);
            }

            // add to list of peers.
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_logger.LogWarning(
                    "Validation errors suppressed: {Certificate}",
                    certificate.AsLogSafeString());
                m_validatedCertificates[certificate.Thumbprint] = X509CertificateLoader
                    .LoadCertificate(
                        certificate.RawData);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Validates a certificate with domain validation check.
        /// <see cref="Validate(X509Certificate2Collection)"/>
        /// </summary>
        public virtual void Validate(X509Certificate2Collection chain, ConfiguredEndpoint endpoint)
        {
            X509Certificate2 certificate = chain[0];

            try
            {
                m_semaphore.Wait();

                try
                {
                    InternalValidateAsync(chain, endpoint).GetAwaiter().GetResult();

                    // add to list of validated certificates.
                    m_validatedCertificates[certificate.Thumbprint] = X509CertificateLoader
                        .LoadCertificate(
                            certificate.RawData);

                    return;
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
            catch (ServiceResultException se)
            {
                HandleCertificateValidationException(se, certificate, chain);
            }

            // add to list of peers.
            m_semaphore.Wait();

            try
            {
                m_logger.LogWarning(
                    "Validation errors suppressed: {Certificate}",
                    certificate.AsLogSafeString());
                m_validatedCertificates[certificate.Thumbprint] = X509CertificateLoader
                    .LoadCertificate(
                        certificate.RawData);
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// Returns the issuers for the certificates.
        /// </summary>
        [Obsolete("Use GetIssuersNoExceptionsOnGetIssuerAsync instead.")]
        public Task<bool> GetIssuersNoExceptionsOnGetIssuer(
            X509Certificate2Collection certificates,
            List<CertificateIdentifier> issuers,
            Dictionary<X509Certificate2, ServiceResultException> validationErrors)
        {
            return GetIssuersNoExceptionsOnGetIssuerAsync(certificates, issuers, validationErrors);
        }

        /// <summary>
        /// Returns the issuers for the certificates.
        /// </summary>
        public async Task<bool> GetIssuersNoExceptionsOnGetIssuerAsync(
            X509Certificate2Collection certificates,
            List<CertificateIdentifier> issuers,
            Dictionary<X509Certificate2, ServiceResultException> validationErrors,
            CancellationToken ct = default)
        {
            bool isTrusted = false;
            CertificateIdentifier issuer = null;
            ServiceResultException revocationStatus = null;
            X509Certificate2 certificate = certificates[0];

            var untrustedCollection = new CertificateIdentifierCollection();
            for (int ii = 1; ii < certificates.Count; ii++)
            {
                untrustedCollection.Add(new CertificateIdentifier(certificates[ii]));
            }

            do
            {
                // check for root.
                if (X509Utils.IsSelfSigned(certificate))
                {
                    break;
                }

                if (validationErrors != null)
                {
                    (issuer, revocationStatus) = await GetIssuerNoExceptionAsync(
                            certificate,
                            m_trustedCertificateList,
                            m_trustedCertificateStore,
                            true,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    issuer = await GetIssuerAsync(
                            certificate,
                            m_trustedCertificateList,
                            m_trustedCertificateStore,
                            true,
                            ct)
                        .ConfigureAwait(false);
                }

                if (issuer == null)
                {
                    if (validationErrors != null)
                    {
                        (issuer, revocationStatus) = await GetIssuerNoExceptionAsync(
                                certificate,
                                m_issuerCertificateList,
                                m_issuerCertificateStore,
                                true,
                                ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        issuer = await GetIssuerAsync(
                                certificate,
                                m_issuerCertificateList,
                                m_issuerCertificateStore,
                                true,
                                ct)
                            .ConfigureAwait(false);
                    }

                    if (issuer == null)
                    {
                        if (validationErrors != null)
                        {
                            (issuer, revocationStatus) = await GetIssuerNoExceptionAsync(
                                    certificate,
                                    untrustedCollection,
                                    null,
                                    true,
                                    ct)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            issuer = await GetIssuerAsync(
                                certificate,
                                untrustedCollection,
                                null,
                                true,
                                ct)
                                .ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    isTrusted = true;
                }

                if (issuer != null)
                {
                    if (validationErrors != null)
                    {
                        validationErrors[certificate] = revocationStatus;
                    }

                    if (issuers.Find(iss =>
                            string.Equals(
                                iss.Thumbprint,
                                issuer.Thumbprint,
                                StringComparison.OrdinalIgnoreCase)
                        ) != default(CertificateIdentifier))
                    {
                        break;
                    }

                    issuers.Add(issuer);

                    certificate = await issuer.FindAsync(
                        false,
                        applicationUri: null,
                        m_telemetry,
                        ct).ConfigureAwait(false);
                }
            } while (issuer != null);

            return isTrusted;
        }

        /// <summary>
        /// Returns the issuers for the certificates.
        /// </summary>
        [Obsolete("Use GetIssuersAsync instead.")]
        public Task<bool> GetIssuers(
            X509Certificate2Collection certificates,
            List<CertificateIdentifier> issuers)
        {
            return GetIssuersAsync(certificates, issuers);
        }

        /// <summary>
        /// Returns the issuers for the certificates.
        /// </summary>
        public Task<bool> GetIssuersAsync(
            X509Certificate2Collection certificates,
            List<CertificateIdentifier> issuers,
            CancellationToken ct = default)
        {
            return GetIssuersNoExceptionsOnGetIssuerAsync(
                certificates,
                issuers,
                validationErrors: null, // ensures legacy behavior is respected
                ct);
        }

        /// <summary>
        /// Returns the issuers for the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="issuers">The issuers.</param>
        [Obsolete("Use GetIssuersAsync instead.")]
        public Task<bool> GetIssuers(
            X509Certificate2 certificate,
            List<CertificateIdentifier> issuers)
        {
            return GetIssuersAsync(certificate, issuers);
        }

        /// <summary>
        /// Returns the issuers for the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="issuers">The issuers.</param>
        /// <param name="ct"></param>
        public Task<bool> GetIssuersAsync(
            X509Certificate2 certificate,
            List<CertificateIdentifier> issuers,
            CancellationToken ct = default)
        {
            return GetIssuersAsync([certificate], issuers, ct);
        }

        /// <summary>
        /// Reset the list of validated certificates.
        /// </summary>
        private void InternalResetValidatedCertificates()
        {
            // dispose outdated list
            foreach (X509Certificate2 cert in m_validatedCertificates.Values)
            {
                Utils.SilentDispose(cert);
            }
            m_validatedCertificates.Clear();
        }

        /// <summary>
        /// Validates a certificate chain.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void HandleCertificateValidationException(
            ServiceResultException se,
            X509Certificate2 certificate,
            X509Certificate2Collection chain)
        {
            // check for errors that may be suppressed.
            if (ContainsUnsuppressibleSC(se.Result))
            {
                m_logger.LogError(
                    "Certificate {Certificate} rejected. Reason={ServiceResult}.",
                    certificate.AsLogSafeString(),
                    se.Result);

                // save the chain in rejected store to allow to add certs to a trusted or issuer store
                Task.Run(async () => await SaveCertificatesAsync(chain).ConfigureAwait(false));

                LogInnerServiceResults(LogLevel.Information, se.Result.InnerResult);
                throw new ServiceResultException(se, StatusCodes.BadCertificateInvalid);
            }

            // invoke callback.
            bool accept = false;
            string applicationErrorMsg = string.Empty;

            ServiceResult serviceResult = se.Result;
            lock (m_callbackLock)
            {
                do
                {
                    accept = false;
                    if (m_CertificateValidation != null)
                    {
                        var args = new CertificateValidationEventArgs(serviceResult, certificate);
                        m_CertificateValidation(this, args);
                        if (args.AcceptAll)
                        {
                            accept = true;
                            serviceResult = null;
                            break;
                        }
                        applicationErrorMsg = args.ApplicationErrorMsg;
                        accept = args.Accept;
                    }
                    else if (m_autoAcceptUntrustedCertificates &&
                        serviceResult.StatusCode == StatusCodes.BadCertificateUntrusted)
                    {
                        accept = true;
                        m_logger.LogInformation("Auto accepted certificate {Certificate}", certificate.AsLogSafeString());
                    }

                    if (accept)
                    {
                        serviceResult = serviceResult.InnerResult;
                    }
                    else
                    {
                        // report the rejected service result
                        if (string.IsNullOrEmpty(applicationErrorMsg))
                        {
                            se = new ServiceResultException(serviceResult);
                        }
                        else
                        {
                            se = new ServiceResultException(applicationErrorMsg);
                        }
                    }
                } while (accept && serviceResult != null);
            }

            // throw if rejected.
            if (!accept)
            {
                // only log errors if the cert validation failed and it was not accepted
                m_logger.LogError(
                    "Certificate {Certificate} validation failed with suppressible errors but was rejected. Reason={ServiceResult}.",
                    certificate.AsLogSafeString(),
                    se.Result);
                LogInnerServiceResults(LogLevel.Error, se.Result.InnerResult);

                // save the chain in rejected store to allow to add cert to a trusted or issuer store
                Task.Run(async () => await SaveCertificatesAsync(chain).ConfigureAwait(false));

                throw new ServiceResultException(se, StatusCodes.BadCertificateInvalid);
            }
        }

        /// <summary>
        /// Recursively checks whether any of the service results or inner service results
        /// of the input sr must not be suppressed.
        /// The list of suppressible status codes is - for backwards compatibility - longer
        /// than the spec would imply.
        /// (BadCertificateUntrusted and BadCertificateChainIncomplete
        /// must not be suppressed according to (e.g.) version 1.04 of the spec)
        /// </summary>
        private static bool ContainsUnsuppressibleSC(ServiceResult sr)
        {
            while (sr != null)
            {
                if (!s_suppressibleStatusCodes.Contains(sr.StatusCode))
                {
                    return true;
                }
                sr = sr.InnerResult;
            }
            return false;
        }

        /// <summary>
        /// List all reasons for failing cert validation.
        /// </summary>
        private void LogInnerServiceResults(LogLevel logLevel, ServiceResult result)
        {
            while (result != null)
            {
                m_logger.Log(logLevel, Utils.TraceMasks.Security, " -- {Result}", result.ToString());
                result = result.InnerResult;
            }
        }

        /// <summary>
        /// Saves the certificate in the rejected certificate store.
        /// </summary>
        private Task SaveCertificateAsync(
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            return SaveCertificatesAsync([certificate], ct);
        }

        /// <summary>
        /// Saves the certificate chain in the rejected certificate store.
        /// Times out after 5 seconds waiting to gracefully reduce high CPU load.
        /// </summary>
        private async Task SaveCertificatesAsync(
            X509Certificate2Collection certificateChain,
            CancellationToken ct = default)
        {
            // max time to wait for semaphore
            const int kSaveCertificatesTimeout = 5000;

            CertificateStoreIdentifier rejectedCertificateStore = m_rejectedCertificateStore;
            if (rejectedCertificateStore == null)
            {
                return;
            }

            try
            {
                if (!await m_semaphore.WaitAsync(kSaveCertificatesTimeout, ct)
                    .ConfigureAwait(false))
                {
                    m_logger.LogTrace(
                        "SaveCertificatesAsync: Timed out waiting, skip job to reduce CPU load.");
                    return;
                }

                try
                {
                    m_logger.LogTrace(
                        "Writing rejected certificate chain to: {RefjectedCertificateStore}",
                        rejectedCertificateStore);

                    ICertificateStore store = rejectedCertificateStore.OpenStore(m_telemetry);
                    try
                    {
                        if (store != null)
                        {
                            // number of certs for history + current chain
                            await store
                                .AddRejectedAsync(certificateChain, m_maxRejectedCertificates, ct)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        store?.Close();
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            }
            catch (Exception e)
            {
                m_logger.LogTrace(
                    "Could not write certificate to directory: {RejectedStore} Error:{Message}",
                    rejectedCertificateStore,
                    e.Message);
            }
        }

        /// <summary>
        /// Returns the certificate information for a trusted peer certificate.
        /// </summary>
        private async Task<CertificateIdentifier> GetTrustedCertificateAsync(
            X509Certificate2 certificate,
            CancellationToken ct = default)
        {
            // check if explicitly trusted.
            if (m_trustedCertificateList != null)
            {
                for (int ii = 0; ii < m_trustedCertificateList.Count; ii++)
                {
                    X509Certificate2 trusted = await m_trustedCertificateList[ii]
                        .FindAsync(false, applicationUri: null, m_telemetry, ct)
                        .ConfigureAwait(false);

                    if (trusted != null &&
                        trusted.Thumbprint == certificate.Thumbprint &&
                        Utils.IsEqual(trusted.RawData, certificate.RawData))
                    {
                        return m_trustedCertificateList[ii];
                    }
                }
            }

            // check if in peer trust store.
            if (m_trustedCertificateStore != null)
            {
                ICertificateStore store = m_trustedCertificateStore.OpenStore(m_telemetry);
                if (store != null)
                {
                    try
                    {
                        X509Certificate2Collection trusted = await store
                            .FindByThumbprintAsync(certificate.Thumbprint, ct)
                            .ConfigureAwait(false);

                        for (int ii = 0; ii < trusted.Count; ii++)
                        {
                            if (Utils.IsEqual(trusted[ii].RawData, certificate.RawData))
                            {
                                return new CertificateIdentifier(
                                    trusted[ii],
                                    m_trustedCertificateStore.ValidationOptions);
                            }
                        }
                    }
                    finally
                    {
                        store.Close();
                    }
                }
            }

            // not a trusted.
            return null;
        }

        /// <summary>
        /// Returns true if the certificate matches the criteria.
        /// </summary>
        private static bool Match(
            X509Certificate2 certificate,
            X500DistinguishedName subjectName,
            string serialNumber,
            string authorityKeyId)
        {
            bool check = false;

            // check for null.
            if (certificate == null)
            {
                return false;
            }

            // check for subject name match.
            if (!X509Utils.CompareDistinguishedName(certificate.SubjectName, subjectName))
            {
                return false;
            }

            // check for serial number match.
            if (!string.IsNullOrEmpty(serialNumber))
            {
                if (certificate.SerialNumber != serialNumber)
                {
                    return false;
                }
                check = true;
            }

            // check for authority key id match.
            if (!string.IsNullOrEmpty(authorityKeyId))
            {
                X509SubjectKeyIdentifierExtension subjectKeyId =
                    certificate.FindExtension<X509SubjectKeyIdentifierExtension>();

                if (subjectKeyId != null)
                {
                    if (subjectKeyId.SubjectKeyIdentifier != authorityKeyId)
                    {
                        return false;
                    }
                    check = true;
                }
            }

            // found match if keyId or serial number was checked
            return check;
        }

        /// <summary>
        /// Returns the certificate information for a trusted issuer certificate.
        /// </summary>
        private async Task<(CertificateIdentifier, ServiceResultException)> GetIssuerNoExceptionAsync(
            X509Certificate2 certificate,
            CertificateIdentifierCollection explicitList,
            CertificateStoreIdentifier certificateStore,
            bool checkRecovationStatus,
            CancellationToken ct = default)
        {
            ServiceResultException serviceResult = null;

#if DEBUG // check if not self-signed, tested in outer loop
            System.Diagnostics.Debug.Assert(!X509Utils.IsSelfSigned(certificate));
#endif

            X500DistinguishedName subjectName = certificate.IssuerName;
            string keyId = null;
            string serialNumber = null;

            // find the authority key identifier.
            X509AuthorityKeyIdentifierExtension authority =
                certificate.FindExtension<X509AuthorityKeyIdentifierExtension>();
            if (authority != null)
            {
                keyId = authority.KeyIdentifier;
                serialNumber = authority.SerialNumber;
            }

            // check in explicit list.
            if (explicitList != null)
            {
                for (int ii = 0; ii < explicitList.Count; ii++)
                {
                    X509Certificate2 issuer = await explicitList[ii].FindAsync(
                        false,
                        applicationUri: null,
                        m_telemetry,
                        ct)
                        .ConfigureAwait(false);

                    if (issuer != null)
                    {
                        if (!X509Utils.IsIssuerAllowed(issuer))
                        {
                            continue;
                        }

                        if (Match(issuer, subjectName, serialNumber, keyId))
                        {
                            // can't check revocation.
                            return (
                                new CertificateIdentifier(
                                    issuer,
                                    CertificateValidationOptions.SuppressRevocationStatusUnknown
                                ),
                                null);
                        }
                    }
                }
            }

            // check in certificate store.
            if (certificateStore != null)
            {
                ICertificateStore store = certificateStore.OpenStore(m_telemetry);

                try
                {
                    if (store == null)
                    {
                        m_logger.LogWarning("Failed to open issuer store: {CertificateStore}", certificateStore);
                        // not a trusted issuer.
                        return (null, null);
                    }

                    X509Certificate2Collection certificates = await store.EnumerateAsync(ct)
                        .ConfigureAwait(false);

                    for (int ii = 0; ii < certificates.Count; ii++)
                    {
                        X509Certificate2 issuer = certificates[ii];

                        if (issuer != null)
                        {
                            if (!X509Utils.IsIssuerAllowed(issuer))
                            {
                                continue;
                            }

                            if (Match(issuer, subjectName, serialNumber, keyId))
                            {
                                CertificateValidationOptions options = certificateStore
                                    .ValidationOptions;

                                if (checkRecovationStatus)
                                {
                                    StatusCode status = await store
                                        .IsRevokedAsync(issuer, certificate, ct)
                                        .ConfigureAwait(false);

                                    if (StatusCode.IsBad(status) &&
                                        status != StatusCodes.BadNotSupported)
                                    {
                                        if (status == StatusCodes.BadCertificateRevocationUnknown)
                                        {
                                            if (X509Utils.IsCertificateAuthority(certificate))
                                            {
                                                status.Code = StatusCodes
                                                    .BadCertificateIssuerRevocationUnknown;
                                            }

                                            if (m_rejectUnknownRevocationStatus &&
                                                (
                                                    options &
                                                    CertificateValidationOptions.SuppressRevocationStatusUnknown
                                                ) == 0)
                                            {
                                                serviceResult = new ServiceResultException(status);
                                            }
                                        }
                                        else
                                        {
                                            if (status == StatusCodes.BadCertificateRevoked &&
                                                X509Utils.IsCertificateAuthority(certificate))
                                            {
                                                status.Code = StatusCodes
                                                    .BadCertificateIssuerRevoked;
                                            }
                                            serviceResult = new ServiceResultException(status);
                                        }
                                    }
                                }

                                // already checked revocation for file based stores. windows based stores always suppress.
                                options
                                    |= CertificateValidationOptions.SuppressRevocationStatusUnknown;

                                return (new CertificateIdentifier(issuer, options), serviceResult);
                            }
                        }
                    }
                }
                finally
                {
                    store?.Close();
                }
            }

            // not a trusted issuer.
            return (null, null);
        }

        /// <summary>
        /// Returns the certificate information for a trusted issuer certificate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task<CertificateIdentifier> GetIssuerAsync(
            X509Certificate2 certificate,
            CertificateIdentifierCollection explicitList,
            CertificateStoreIdentifier certificateStore,
            bool checkRecovationStatus,
            CancellationToken ct = default)
        {
            // check for root.
            if (X509Utils.IsSelfSigned(certificate))
            {
                return null;
            }

            (CertificateIdentifier result, ServiceResultException srex)
                = await GetIssuerNoExceptionAsync(
                    certificate,
                    explicitList,
                    certificateStore,
                    checkRecovationStatus,
                    ct)
                .ConfigureAwait(false);
            if (srex != null)
            {
                throw srex;
            }
            return result;
        }

        /// <summary>
        /// Throws an exception if validation fails.
        /// </summary>
        /// <param name="certificates">The certificates to be checked.</param>
        /// <param name="endpoint">The endpoint for domain validation.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">If certificate[0] cannot be accepted</exception>
        // [System.Diagnostics.CodeAnalysis.SuppressMessage(
        //     "Roslynanalyzer",
        //     "IA5352:Do not set X509RevocationMode.NoCheck",
        //     Justification = "Revocation is already checked."
        // )]
        protected virtual async Task InternalValidateAsync(
            X509Certificate2Collection certificates,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default)
        {
            X509Certificate2 certificate = certificates[0];

            // check for previously validated certificate.

            if (m_useValidatedCertificates &&
                m_validatedCertificates.TryGetValue(
                    certificate.Thumbprint,
                    out X509Certificate2 certificate2) &&
                Utils.IsEqual(certificate2.RawData, certificate.RawData))
            {
                return;
            }

            CertificateIdentifier trustedCertificate =
                await GetTrustedCertificateAsync(certificate, ct).ConfigureAwait(false);

            // get the issuers (checks the revocation lists if using directory stores).
            var issuers = new List<CertificateIdentifier>();
            var validationErrors = new Dictionary<X509Certificate2, ServiceResultException>();

            bool isIssuerTrusted = await GetIssuersNoExceptionsOnGetIssuerAsync(
                certificates,
                issuers,
                validationErrors,
                ct)
                .ConfigureAwait(false);

            ServiceResult sresult = PopulateSresultWithValidationErrors(validationErrors);

            // setup policy chain
            var policy = new X509ChainPolicy
            {
                RevocationFlag = X509RevocationFlag.EntireChain,
                RevocationMode = X509RevocationMode.NoCheck,
                VerificationFlags = X509VerificationFlags.NoFlag,
#if NET5_0_OR_GREATER
                DisableCertificateDownloads = true,
#endif
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1)
            };

            foreach (CertificateIdentifier issuer in issuers)
            {
                if ((issuer.ValidationOptions &
                    CertificateValidationOptions.SuppressRevocationStatusUnknown) != 0)
                {
                    policy.VerificationFlags
                        |= X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;
                    policy.VerificationFlags
                        |= X509VerificationFlags.IgnoreCtlSignerRevocationUnknown;
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreEndRevocationUnknown;
                    policy.VerificationFlags |= X509VerificationFlags.IgnoreRootRevocationUnknown;
                }

                // we did the revocation check in the GetIssuers call. No need here.
                policy.RevocationMode = X509RevocationMode.NoCheck;
                policy.ExtraStore.Add(issuer.Certificate);
            }

            // build chain.
            bool chainIncomplete = false;
            using (var chain = new X509Chain())
            {
                chain.ChainPolicy = policy;
                chain.Build(certificate);

                // check the chain results.
                CertificateIdentifier target = trustedCertificate ??
                    new CertificateIdentifier(certificate);

                foreach (X509ChainStatus chainStatus in chain.ChainStatus)
                {
                    switch (chainStatus.Status)
                    {
                        // status codes that are handled in CheckChainStatus
                        case X509ChainStatusFlags.RevocationStatusUnknown:
                        case X509ChainStatusFlags.Revoked:
                        case X509ChainStatusFlags.NotValidForUsage:
                        case X509ChainStatusFlags.OfflineRevocation:
                        case X509ChainStatusFlags.InvalidBasicConstraints:
                        case X509ChainStatusFlags.NotTimeValid:
                        case X509ChainStatusFlags.NotTimeNested:
                        case X509ChainStatusFlags.NoError:
                        // by design, the trust root is not in the default store
                        case X509ChainStatusFlags.UntrustedRoot:
                            break;
                        // mark incomplete, invalidate the issuer trust
                        case X509ChainStatusFlags.PartialChain:
                            chainIncomplete = true;
                            isIssuerTrusted = false;
                            break;
                        case X509ChainStatusFlags.NotSignatureValid:
                            sresult = new ServiceResult(ServiceResult.Create(
                                StatusCodes.BadCertificateInvalid,
                                "Certificate validation failed. {0}: {1}",
                                chainStatus.Status,
                                chainStatus.StatusInformation
                            ), sresult);
                            break;
                        // unexpected error status
                        default:
                            m_logger.LogError(
                                "Unexpected status {ChainStatus} processing certificate chain.",
                                chainStatus.Status);
                            sresult = new ServiceResult(ServiceResult.Create(
                                StatusCodes.BadCertificateInvalid,
                                "Certificate validation failed. {0}: {1}",
                                chainStatus.Status,
                                chainStatus.StatusInformation
                            ), sresult);
                            break;
                    }
                }

                if (issuers.Count + 1 != chain.ChainElements.Count)
                {
                    // invalidate, unexpected result from X509Chain elements
                    chainIncomplete = true;
                    isIssuerTrusted = false;
                }

                for (int ii = 0; ii < chain.ChainElements.Count; ii++)
                {
                    X509ChainElement element = chain.ChainElements[ii];

                    CertificateIdentifier issuer = null;

                    if (ii < issuers.Count)
                    {
                        issuer = issuers[ii];
                    }

                    // validate the issuer chain matches the chain elements
                    if (ii + 1 < chain.ChainElements.Count)
                    {
                        X509Certificate2 issuerCert = chain.ChainElements[ii + 1].Certificate;
                        if (issuer == null || !Utils.IsEqual(issuerCert.RawData, issuer.RawData))
                        {
                            // the chain used for cert validation differs from the issuers provided
                            m_logger.LogInformation(
                                Utils.TraceMasks.Security,
                                "An unexpected certificate {Certificate} was used in the certificate chain.",
                                issuerCert.AsLogSafeString());
                            chainIncomplete = true;
                            isIssuerTrusted = false;
                            break;
                        }
                    }

                    // check for chain status errors.
                    if (element.ChainElementStatus.Length > 0)
                    {
                        foreach (X509ChainStatus status in element.ChainElementStatus)
                        {
                            ServiceResult result = CheckChainStatus(
                                status,
                                target,
                                issuer,
                                ii != 0);
                            if (ServiceResult.IsBad(result))
                            {
                                sresult = new ServiceResult(result, sresult);
                            }
                        }
                    }

                    if (issuer != null)
                    {
                        target = issuer;
                    }
                }
            }

            // check whether the chain is complete (if there is a chain)
            bool issuedByCA = !X509Utils.IsSelfSigned(certificate);
            if (issuers.Count > 0)
            {
                X509Certificate2 rootCertificate = issuers[^1].Certificate;
                if (!X509Utils.IsSelfSigned(rootCertificate))
                {
                    chainIncomplete = true;
                }
            }
            else if (issuedByCA)
            {
                // no issuer found at all
                chainIncomplete = true;
            }

            // check if certificate issuer is trusted.
            if (issuedByCA && !isIssuerTrusted && trustedCertificate == null)
            {
                const string message = "Certificate Issuer is not trusted.";
                sresult = new ServiceResult(
                    StatusCodes.BadCertificateUntrusted,
                    null,
                    null,
                    message,
                    null,
                    sresult);
            }

            // check if certificate is trusted.
            if (trustedCertificate == null && !isIssuerTrusted)
            {
                // If the certificate is not trusted, check if the certificate is amongst the application certificates
                bool isApplicationCertificate = false;
                if (m_applicationCertificates != null)
                {
                    foreach (X509Certificate2 appCert in m_applicationCertificates)
                    {
                        if (Utils.IsEqual(appCert.RawData, certificate.RawData))
                        {
                            // certificate is the application certificate
                            isApplicationCertificate = true;
                            break;
                        }
                    }
                }

                if (m_applicationCertificates == null || !isApplicationCertificate)
                {
                    const string message = "Certificate is not trusted.";
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificateUntrusted,
                        null,
                        null,
                        message,
                        null,
                        sresult);
                }
            }

            Uri endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null && !FindDomain(certificate, endpointUrl))
            {
                string message = Utils.Format(
                    "The domain '{0}' is not listed in the server certificate.",
                    endpointUrl.IdnHost);
                sresult = new ServiceResult(
                    StatusCodes.BadCertificateHostNameInvalid,
                    null,
                    null,
                    message,
                    null,
                    sresult);
            }

            bool isECDsaSignature = X509PfxUtils.IsECDsaSignature(certificate);

            // check if certificate is valid for use as app/sw or user cert
            X509KeyUsageFlags certificateKeyUsage = X509Utils.GetKeyUsage(certificate);
            if (isECDsaSignature)
            {
                if ((certificateKeyUsage & X509KeyUsageFlags.DigitalSignature) == 0)
                {
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificateUseNotAllowed,
                        null,
                        null,
                        "Usage of ECDSA certificate is not allowed.",
                        null,
                        sresult);
                }
            }
            else if ((certificateKeyUsage & X509KeyUsageFlags.DataEncipherment) == 0)
            {
                sresult = new ServiceResult(
                    StatusCodes.BadCertificateUseNotAllowed,
                    null,
                    null,
                    "Usage of RSA certificate is not allowed.",
                    null,
                    sresult);
            }

            // check if minimum requirements are met
            if (m_rejectSHA1SignedCertificates &&
                IsSHA1SignatureAlgorithm(certificate.SignatureAlgorithm))
            {
                sresult = new ServiceResult(
                    StatusCodes.BadCertificatePolicyCheckFailed,
                    null,
                    null,
                    "SHA1 signed certificates are not trusted.",
                    null,
                    sresult);
            }

            // check if certificate signature algorithm length is sufficient
            if (isECDsaSignature)
            {
                int publicKeySize = X509Utils.GetPublicKeySize(certificate);
                bool isInvalid =
                    (certificate.SignatureAlgorithm.Value == Oids.ECDsaWithSha256 &&
                        publicKeySize > 256) ||
                    (
                        certificate.SignatureAlgorithm.Value == Oids.ECDsaWithSha384 &&
                        (publicKeySize <= 256 || publicKeySize > 384)
                    ) ||
                    (certificate.SignatureAlgorithm.Value == Oids.ECDsaWithSha512 &&
                        publicKeySize <= 384);
                if (isInvalid)
                {
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificatePolicyCheckFailed,
                        null,
                        null,
                        "Certificate doesn't meet minimum signature algorithm length requirement.",
                        null,
                        sresult);
                }
            }
            else // RSA
            {
                int keySize = X509Utils.GetRSAPublicKeySize(certificate);
                if (keySize < m_minimumCertificateKeySize)
                {
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificatePolicyCheckFailed,
                        null,
                        null,
                        "Certificate doesn't meet minimum key length requirement.",
                        null,
                        sresult);
                }
            }

            if (issuedByCA && chainIncomplete)
            {
                const string message = "Certificate chain validation incomplete.";
                sresult = new ServiceResult(
                    StatusCodes.BadCertificateChainIncomplete,
                    null,
                    null,
                    message,
                    null,
                    sresult);
            }

            if (sresult != null)
            {
                throw new ServiceResultException(sresult);
            }
        }

        private static ServiceResult PopulateSresultWithValidationErrors(
            Dictionary<X509Certificate2, ServiceResultException> validationErrors)
        {
            var p1List = new Dictionary<X509Certificate2, ServiceResultException>();
            var p2List = new Dictionary<X509Certificate2, ServiceResultException>();
            var p3List = new Dictionary<X509Certificate2, ServiceResultException>();

            ServiceResult sresult = null;

            foreach (KeyValuePair<X509Certificate2, ServiceResultException> kvp in validationErrors)
            {
                if (kvp.Value != null)
                {
                    if (kvp.Value.StatusCode == StatusCodes.BadCertificateRevoked)
                    {
                        p1List[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Value.StatusCode == StatusCodes.BadCertificateIssuerRevoked)
                    {
                        p2List[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Value.StatusCode == StatusCodes.BadCertificateRevocationUnknown)
                    {
                        p3List[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Value.StatusCode == StatusCodes
                        .BadCertificateIssuerRevocationUnknown)
                    {
                        //p4List[kvp.Key] = kvp.Value;
                        string message = CertificateMessage(
                            "Certificate issuer revocation list not found.",
                            kvp.Key);
                        sresult = new ServiceResult(
                            StatusCodes.BadCertificateIssuerRevocationUnknown,
                            null,
                            null,
                            message,
                            null,
                            sresult);
                    }
                    else if (StatusCode.IsBad(kvp.Value.StatusCode))
                    {
                        string message = CertificateMessage(
                            "Unknown error while trying to determine the revocation status.",
                            kvp.Key);
                        sresult = new ServiceResult(
                            kvp.Value.StatusCode,
                            null,
                            null,
                            message,
                            null,
                            sresult);
                    }
                }
            }

            if (p3List.Count > 0)
            {
                foreach (KeyValuePair<X509Certificate2, ServiceResultException> kvp in p3List)
                {
                    string message = CertificateMessage(
                        "Certificate revocation list not found.",
                        kvp.Key);
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificateRevocationUnknown,
                        null,
                        null,
                        message,
                        null,
                        sresult);
                }
            }
            if (p2List.Count > 0)
            {
                foreach (KeyValuePair<X509Certificate2, ServiceResultException> kvp in p2List)
                {
                    string message = CertificateMessage("Certificate issuer is revoked.", kvp.Key);
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificateIssuerRevoked,
                        null,
                        null,
                        message,
                        null,
                        sresult);
                }
            }
            if (p1List.Count > 0)
            {
                foreach (KeyValuePair<X509Certificate2, ServiceResultException> kvp in p1List)
                {
                    string message = CertificateMessage("Certificate is revoked.", kvp.Key);
                    sresult = new ServiceResult(
                        StatusCodes.BadCertificateRevoked,
                        null,
                        null,
                        message,
                        null,
                        sresult);
                }
            }

            return sresult;
        }

        /// <summary>
        /// Returns an object that can be used with a UA channel.
        /// </summary>
        public ICertificateValidator GetChannelValidator()
        {
            return this;
        }

        /// <summary>
        /// Validate domains in a server certificate against endpoint used for connection.
        /// A url mismatch can be accepted by the certificate validation event,
        /// otherwise an exception is thrown.
        /// </summary>
        /// <remarks>
        /// On a client: the endpoint is only checked if the certificate is not already validated.
        ///   A rejected server certificate is saved.
        /// On a server: the endpoint is always checked but the certificate is not saved.
        /// </remarks>
        /// <param name="serverCertificate">The server certificate which contains the list of domains.</param>
        /// <param name="endpoint">The endpoint used to connect to a server.</param>
        /// <param name="serverValidation">if the domain validation is called by a server or client.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadCertificateHostNameInvalid"/>if the endpoint can not be found in the list of domais in the certificate.
        /// </exception>
        public void ValidateDomains(
            X509Certificate2 serverCertificate,
            ConfiguredEndpoint endpoint,
            bool serverValidation = false)
        {
            if (!serverValidation &&
                m_useValidatedCertificates &&
                m_validatedCertificates.TryGetValue(
                    serverCertificate.Thumbprint,
                    out X509Certificate2 certificate2) &&
                Utils.IsEqual(certificate2.RawData, serverCertificate.RawData))
            {
                return;
            }

            Uri endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null && !FindDomain(serverCertificate, endpointUrl))
            {
                bool accept = false;
                const string message = "The domain '{0}' is not listed in the server certificate.";
                var serviceResult = ServiceResultException.Create(
                    StatusCodes.BadCertificateHostNameInvalid,
                    message,
                    endpointUrl.IdnHost);
                if (m_CertificateValidation != null)
                {
                    var args = new CertificateValidationEventArgs(
                        new ServiceResult(serviceResult),
                        serverCertificate);
                    m_CertificateValidation(this, args);
                    accept = args.Accept || args.AcceptAll;
                }
                // throw if rejected.
                if (!accept)
                {
                    if (serverValidation)
                    {
                        m_logger.LogError("The domain '{Url}' is not listed in the server certificate.", Redact.Create(endpointUrl));
                    }
                    else
                    {
                        // write the invalid certificate to rejected store if specified.
                        m_logger.LogError(
                            "Certificate {Certificate} rejected. Reason={ServiceResult}.",
                            serverCertificate.AsLogSafeString(),
                            Redact.Create(serviceResult));
                        Task.Run(async () => await SaveCertificateAsync(serverCertificate)
                            .ConfigureAwait(false));
                    }

                    throw serviceResult;
                }
            }
        }

        /// <summary>
        /// Returns an error if the chain status elements indicate an error.
        /// </summary>
        private ServiceResult CheckChainStatus(
            X509ChainStatus status,
            CertificateIdentifier id,
            CertificateIdentifier issuer,
            bool isIssuer)
        {
            switch (status.Status)
            {
                case X509ChainStatusFlags.NotValidForUsage:
                    return ServiceResult.Create(
                        isIssuer
                            ? StatusCodes.BadCertificateUseNotAllowed
                            : StatusCodes.BadCertificateIssuerUseNotAllowed,
                        "Certificate may not be used as an application instance certificate. {Status}: {Information}",
                        status.Status,
                        status.StatusInformation);
                case X509ChainStatusFlags.NoError:
                case X509ChainStatusFlags.OfflineRevocation:
                case X509ChainStatusFlags.InvalidBasicConstraints:
                    break;
                case X509ChainStatusFlags.PartialChain:
                    goto case X509ChainStatusFlags.UntrustedRoot;
                case X509ChainStatusFlags.UntrustedRoot:
                    if (issuer != null ||
                        id.Certificate == null ||
                        !X509Utils.IsSelfSigned(id.Certificate))
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadCertificateChainIncomplete,
                            "Certificate chain validation failed. {0}: {1}",
                            status.Status,
                            status.StatusInformation);
                    }
                    // self signed cert signature validation
                    // .NET Core ChainStatus returns NotSignatureValid only on Windows,
                    // so we have to do the extra cert signature check on all platforms
                    if (!IsSignatureValid(id.Certificate))
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadCertificateInvalid,
                            "Certificate validation failed. {0}: {1}",
                            status.Status,
                            status.StatusInformation);
                    }
                    break;
                case X509ChainStatusFlags.RevocationStatusUnknown:
                    if (issuer != null &&
                        (issuer.ValidationOptions &
                            CertificateValidationOptions.SuppressRevocationStatusUnknown) != 0)
                    {
                        m_logger.LogWarning(
                            Utils.TraceMasks.Security,
                            "Error suppressed: {Status}: {Information}",
                            status.Status,
                            status.StatusInformation);
                        break;
                    }

                    // check for meaning less errors for self-signed certificates.
                    if (id.Certificate != null && X509Utils.IsSelfSigned(id.Certificate))
                    {
                        break;
                    }

                    return ServiceResult.Create(
                        isIssuer
                            ? StatusCodes.BadCertificateIssuerRevocationUnknown
                            : StatusCodes.BadCertificateRevocationUnknown,
                        "Certificate revocation status cannot be verified. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                case X509ChainStatusFlags.Revoked:
                    return ServiceResult.Create(
                        isIssuer
                            ? StatusCodes.BadCertificateIssuerRevoked
                            : StatusCodes.BadCertificateRevoked,
                        "Certificate has been revoked. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                case X509ChainStatusFlags.NotTimeNested:
                    if (id != null &&
                        ((id.ValidationOptions &
                            CertificateValidationOptions.SuppressCertificateExpired) != 0))
                    {
                        m_logger.LogWarning(
                            Utils.TraceMasks.Security,
                            "Error suppressed: {Status}: {Information}",
                            status.Status,
                            status.StatusInformation);
                        break;
                    }
                    return ServiceResult.Create(
                        StatusCodes.BadCertificateIssuerTimeInvalid,
                        "Issuer Certificate has expired or is not yet valid. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                case X509ChainStatusFlags.NotTimeValid:
                    if (id != null &&
                        ((id.ValidationOptions &
                            CertificateValidationOptions.SuppressCertificateExpired) != 0))
                    {
                        m_logger.LogWarning(
                            Utils.TraceMasks.Security,
                            "Error suppressed: {Status}: {Information}",
                            status.Status,
                            status.StatusInformation);
                        break;
                    }
                    return ServiceResult.Create(
                        isIssuer
                            ? StatusCodes.BadCertificateIssuerTimeInvalid
                            : StatusCodes.BadCertificateTimeInvalid,
                        "Certificate has expired or is not yet valid. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
                default:
                    return ServiceResult.Create(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate validation failed. {0}: {1}",
                        status.Status,
                        status.StatusInformation);
            }

            return null;
        }

        /// <summary>
        /// Returns if a certificate is signed with a SHA1 algorithm.
        /// </summary>
        private static bool IsSHA1SignatureAlgorithm(Oid oid)
        {
            return oid.Value
                is "1.3.14.3.2.29"
                    or // sha1RSA
                    "1.2.840.10040.4.3"
                    or // sha1DSA
                    Oids.ECDsaWithSha1
                    or // sha1ECDSA
                    "1.2.840.113549.1.1.5"
                    or // sha1RSA
                    "1.3.14.3.2.13"
                    or // sha1DSA
                    "1.3.14.3.2.27"; // dsaSHA1
        }

        /// <summary>
        /// Returns a certificate information message.
        /// </summary>
        private static string CertificateMessage(string error, X509Certificate2 certificate)
        {
            StringBuilder message = new StringBuilder()
                .AppendLine(error)
                .AppendFormat(CultureInfo.InvariantCulture, "Subject: {0}", certificate.Subject)
                .AppendLine();
            if (!string.Equals(certificate.Subject, certificate.Issuer, StringComparison.Ordinal))
            {
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Issuer: {0}",
                    certificate.Issuer).AppendLine();
            }
            return message.ToString();
        }

        /// <summary>
        /// Returns if a self signed certificate is properly signed.
        /// </summary>
        private static bool IsSignatureValid(X509Certificate2 cert)
        {
            return X509Utils.VerifySelfSigned(cert);
        }

        /// <summary>
        /// The list of suppressible status codes.
        /// </summary>
        private static readonly HashSet<StatusCode> s_suppressibleStatusCodes = new(
            [
                StatusCodes.BadCertificateHostNameInvalid,
                StatusCodes.BadCertificateIssuerRevocationUnknown,
                StatusCodes.BadCertificateChainIncomplete,
                StatusCodes.BadCertificateIssuerTimeInvalid,
                StatusCodes.BadCertificateIssuerUseNotAllowed,
                StatusCodes.BadCertificateRevocationUnknown,
                StatusCodes.BadCertificateTimeInvalid,
                StatusCodes.BadCertificatePolicyCheckFailed,
                StatusCodes.BadCertificateUseNotAllowed,
                StatusCodes.BadCertificateUntrusted
            ]);

#if ECC_SUPPORT
        /// <summary>
        /// Dictionary of named curves and their bit sizes.
        /// </summary>
        internal static readonly Dictionary<string, int> NamedCurveBitSizes = new()
        {
            // NIST Curves
            { ECCurve.NamedCurves.nistP256.Oid.Value ?? "1.2.840.10045.3.1.7", 256 }, // NIST P-256
            { ECCurve.NamedCurves.nistP384.Oid.Value ?? "1.3.132.0.34", 384 }, // NIST P-384
            { ECCurve.NamedCurves.nistP521.Oid.Value ?? "1.3.132.0.35", 521 }, // NIST P-521
            // Brainpool Curves
            { ECCurve.NamedCurves.brainpoolP256r1.Oid.Value ?? "1.3.36.3.3.2.8.1.1.7", 256 }, // BrainpoolP256r1
            { ECCurve.NamedCurves.brainpoolP384r1.Oid.Value ?? "1.3.36.3.3.2.8.1.1.11", 384 } // BrainpoolP384r1
        };
#endif

        /// <summary>
        /// Find the domain in a certificate in the
        /// endpoint that was used to connect a session.
        /// </summary>
        /// <param name="serverCertificate">The server certificate which is tested for domain names.</param>
        /// <param name="endpointUrl">The endpoint Url which was used to connect.</param>
        /// <returns>True if domain was found.</returns>
        private static bool FindDomain(X509Certificate2 serverCertificate, Uri endpointUrl)
        {
            bool domainFound = false;

            // check the certificate domains.
            IList<string> domains = X509Utils.GetDomainsFromCertificate(serverCertificate);

            if (domains != null && domains.Count > 0)
            {
                string hostname;
                string dnsHostName = hostname = endpointUrl.IdnHost;
                bool isLocalHost = false;
                if (endpointUrl.HostNameType == UriHostNameType.Dns)
                {
                    if (string.Equals(dnsHostName, "localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        isLocalHost = true;
                    }
                    else
                    { // strip domain names from hostname
                        hostname = dnsHostName.Split('.')[0];
                    }
                }
                else
                { // dnsHostname is a IPv4 or IPv6 address
                    // normalize ip addresses, cert parser returns normalized addresses
                    hostname = Utils.NormalizedIPAddress(dnsHostName);
                    if (hostname is "127.0.0.1" or "::1")
                    {
                        isLocalHost = true;
                    }
                }

                if (isLocalHost)
                {
                    dnsHostName = Utils.GetFullQualifiedDomainName();
                    hostname = Utils.GetHostName();
                }

                for (int ii = 0; ii < domains.Count; ii++)
                {
                    if (string.Equals(hostname, domains[ii], StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dnsHostName, domains[ii], StringComparison.OrdinalIgnoreCase))
                    {
                        domainFound = true;
                        break;
                    }
                }
            }
            return domainFound;
        }

#if ECC_SUPPORT
        /// <summary>
        /// Returns if the certificate is secure enough for the profile.
        /// </summary>
        /// <param name="certificate">The certificate to check.</param>
        /// <param name="requiredKeySizeInBits">The required key size in bits.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public static bool IsECSecureForProfile(
            X509Certificate2 certificate,
            int requiredKeySizeInBits)
        {
            using ECDsa ecdsa =
                certificate.GetECDsaPublicKey()
                ?? throw new ArgumentException("Certificate does not contain an ECC public key");

            if (ecdsa.KeySize != 0)
            {
                return ecdsa.KeySize >= requiredKeySizeInBits;
            }
            ECCurve curve = ecdsa.ExportParameters(false).Curve;

            if (curve.IsNamed)
            {
                if (NamedCurveBitSizes.TryGetValue(curve.Oid.Value, out int curveSize))
                {
                    return curveSize >= requiredKeySizeInBits;
                }
                throw new NotSupportedException($"Unknown named curve: {curve.Oid.Value}");
            }

            throw new NotSupportedException("Unsupported curve type.");
        }
#endif

        /// <summary>
        /// Flag to protect setting by application
        /// from a modification by a SecurityConfiguration.
        /// </summary>
        [Flags]
        private enum ProtectFlags
        {
            None = 0,
            AutoAcceptUntrustedCertificates = 1,
            RejectSHA1SignedCertificates = 2,
            RejectUnknownRevocationStatus = 4,
            MinimumCertificateKeySize = 8,
            UseValidatedCertificates = 16,
            MaxRejectedCertificates = 32
        }

        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private readonly Lock m_callbackLock = new();
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly Dictionary<string, X509Certificate2> m_validatedCertificates;
        private CertificateStoreIdentifier m_trustedCertificateStore;
        private CertificateIdentifierCollection m_trustedCertificateList;
        private CertificateStoreIdentifier m_issuerCertificateStore;
        private CertificateIdentifierCollection m_issuerCertificateList;
        private CertificateStoreIdentifier m_rejectedCertificateStore;
        private event CertificateValidationEventHandler m_CertificateValidation;
        private event CertificateUpdateEventHandler m_CertificateUpdate;
        private readonly List<X509Certificate2> m_applicationCertificates;
        private ProtectFlags m_protectFlags;
        private bool m_autoAcceptUntrustedCertificates;
        private bool m_rejectSHA1SignedCertificates;
        private bool m_rejectUnknownRevocationStatus;
        private ushort m_minimumCertificateKeySize;
        private bool m_useValidatedCertificates;
        private int m_maxRejectedCertificates;
    }

    /// <summary>
    /// The event arguments provided when a certificate validation error occurs.
    /// </summary>
    public class CertificateValidationEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public CertificateValidationEventArgs(ServiceResult error, X509Certificate2 certificate)
        {
            Error = error;
            Certificate = certificate;
        }

        /// <summary>
        /// The error that occurred.
        /// </summary>
        public ServiceResult Error { get; }

        /// <summary>
        /// The certificate.
        /// </summary>
        public X509Certificate2 Certificate { get; }

        /// <summary>
        /// Whether the current error reported for
        /// a certificate should be accepted and suppressed.
        /// </summary>
        public bool Accept { get; set; }

        /// <summary>
        /// Whether all the errors reported for
        /// a certificate should be accepted and suppressed.
        /// </summary>
        public bool AcceptAll { get; set; }

        /// <summary>
        /// The custom error message from the application.
        /// </summary>
        public string ApplicationErrorMsg { get; set; }
    }

    /// <summary>
    /// Used to handled certificate validation errors.
    /// </summary>
    public delegate void CertificateValidationEventHandler(
        CertificateValidator sender,
        CertificateValidationEventArgs e);

    /// <summary>
    /// The event arguments provided when a certificate validation error occurs.
    /// </summary>
    public class CertificateUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public CertificateUpdateEventArgs(
            SecurityConfiguration configuration,
            ICertificateValidator validator)
        {
            SecurityConfiguration = configuration;
            CertificateValidator = validator;
        }

        /// <summary>
        /// The new security configuration.
        /// </summary>
        public SecurityConfiguration SecurityConfiguration { get; }

        /// <summary>
        /// The new certificate validator.
        /// </summary>
        public ICertificateValidator CertificateValidator { get; }
    }

    /// <summary>
    /// Used to handle certificate update events.
    /// </summary>
    public delegate void CertificateUpdateEventHandler(
        CertificateValidator sender,
        CertificateUpdateEventArgs e);
}
