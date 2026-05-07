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

#nullable enable

using System;
using System.Collections.Concurrent;
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
    /// Internal core that performs OPC UA certificate chain validation.
    /// Encapsulates the per-trust-list state and the chain-walk pipeline
    /// previously contained in the legacy <c>CertificateValidator</c>
    /// class. Rejected-store writes and the global per-error accept
    /// callback are owned by the caller (typically
    /// <see cref="CertificateManager"/>).
    /// </summary>
    internal sealed class CertificateValidationCore : IDisposable
    {
        private readonly SemaphoreSlim m_semaphore = new(1, 1);
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly ConcurrentDictionary<string, Certificate> m_validatedCertificates;
        private readonly List<Certificate> m_applicationCertificates;
        private CertificateStoreIdentifier? m_trustedCertificateStore;
        private ArrayOf<CertificateIdentifier> m_trustedCertificateList;
        private CertificateStoreIdentifier? m_issuerCertificateStore;
        private ArrayOf<CertificateIdentifier> m_issuerCertificateList;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CertificateValidationCore"/> class.
        /// </summary>
        public CertificateValidationCore(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<CertificateValidationCore>();
            m_validatedCertificates = [];
            m_applicationCertificates = [];
            AutoAcceptUntrustedCertificates = false;
            RejectSHA1SignedCertificates = CertificateFactory.DefaultHashSize >= 256;
            RejectUnknownRevocationStatus = false;
            MinimumCertificateKeySize = CertificateFactory.DefaultKeySize;
            UseValidatedCertificates = false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            InternalResetValidatedCertificates();

            foreach (Certificate cert in m_applicationCertificates)
            {
                cert?.Dispose();
            }

            m_applicationCertificates.Clear();

            foreach (CertificateIdentifier certId in m_trustedCertificateList)
            {
                certId?.Dispose();
            }

            m_trustedCertificateList = default;

            foreach (CertificateIdentifier certId in m_issuerCertificateList)
            {
                certId?.Dispose();
            }

            m_issuerCertificateList = default;
            m_semaphore.Dispose();
        }

        /// <summary>
        /// If untrusted certificates should be accepted.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates
        {
            get => m_autoAcceptUntrustedCertificates;
            set
            {
                if (m_autoAcceptUntrustedCertificates != value)
                {
                    m_autoAcceptUntrustedCertificates = value;
                    ResetValidatedCertificates();
                }
            }
        }
        private bool m_autoAcceptUntrustedCertificates;

        /// <summary>
        /// If certificates using a SHA1 signature should be trusted.
        /// </summary>
        public bool RejectSHA1SignedCertificates
        {
            get => m_rejectSHA1SignedCertificates;
            set
            {
                if (m_rejectSHA1SignedCertificates != value)
                {
                    m_rejectSHA1SignedCertificates = value;
                    ResetValidatedCertificates();
                }
            }
        }
        private bool m_rejectSHA1SignedCertificates;

        /// <summary>
        /// if certificates with unknown revocation status should be rejected.
        /// </summary>
        public bool RejectUnknownRevocationStatus
        {
            get => m_rejectUnknownRevocationStatus;
            set
            {
                if (m_rejectUnknownRevocationStatus != value)
                {
                    m_rejectUnknownRevocationStatus = value;
                    ResetValidatedCertificates();
                }
            }
        }
        private bool m_rejectUnknownRevocationStatus;

        /// <summary>
        /// The minimum size of an RSA certificate key to be trusted.
        /// </summary>
        public ushort MinimumCertificateKeySize
        {
            get => m_minimumCertificateKeySize;
            set
            {
                if (m_minimumCertificateKeySize != value)
                {
                    m_minimumCertificateKeySize = value;
                    ResetValidatedCertificates();
                }
            }
        }
        private ushort m_minimumCertificateKeySize;

        /// <summary>
        /// Opt-In to use the already validated certificates for validation.
        /// </summary>
        public bool UseValidatedCertificates
        {
            get => m_useValidatedCertificates;
            set
            {
                if (m_useValidatedCertificates != value)
                {
                    m_useValidatedCertificates = value;
                    ResetValidatedCertificates();
                }
            }
        }
        private bool m_useValidatedCertificates;

        /// <summary>
        /// Updates the validator with a new set of trust lists.
        /// </summary>
        public void Update(
            CertificateTrustList? issuerStore,
            CertificateTrustList? trustedStore,
            CertificateStoreIdentifier? rejectedCertificateStore)
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
        /// Updates the validator with the current state of the configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        public async Task UpdateAsync(
            SecurityConfiguration configuration,
            string? applicationUri = null,
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

                if (!configuration.ApplicationCertificates.IsEmpty)
                {
                    ArrayOf<CertificateIdentifier> appCerts = configuration.ApplicationCertificates;
                    for (int i = 0; i < appCerts.Count; i++)
                    {
                        CertificateIdentifier applicationCertificate = appCerts[i];
                        Certificate? certificate = await applicationCertificate
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
                        else
                        {
                            // Release the AddRef'd certificate returned by FindAsync
                            certificate.Dispose();
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
        /// Updates the validator with a new set of trust lists.
        /// Caller must hold <see cref="m_semaphore"/>.
        /// </summary>
        private void InternalUpdate(
            CertificateTrustList? issuerStore,
            CertificateTrustList? trustedStore,
            CertificateStoreIdentifier? rejectedCertificateStore)
        {
            InternalResetValidatedCertificates();

            m_trustedCertificateStore = null;
            m_trustedCertificateList = default;
            if (trustedStore != null)
            {
                m_trustedCertificateStore = new CertificateStoreIdentifier(trustedStore.StorePath)
                {
                    ValidationOptions = trustedStore.ValidationOptions
                };

                if (!trustedStore.TrustedCertificates.IsEmpty)
                {
                    m_trustedCertificateList = trustedStore.TrustedCertificates;
                }
            }

            m_issuerCertificateStore = null;
            m_issuerCertificateList = default;
            if (issuerStore != null)
            {
                m_issuerCertificateStore = new CertificateStoreIdentifier(issuerStore.StorePath)
                {
                    ValidationOptions = issuerStore.ValidationOptions
                };

                if (!issuerStore.TrustedCertificates.IsEmpty)
                {
                    m_issuerCertificateList = issuerStore.TrustedCertificates;
                }
            }

            // Note: the rejectedCertificateStore parameter is accepted for
            // signature compatibility with the legacy CertificateValidator but
            // is intentionally ignored — rejected-store writes are owned by
            // the caller (CertificateManager) via RejectedCertificateProcessor.
            _ = rejectedCertificateStore;
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
        /// Reset the list of validated certificates.
        /// </summary>
        private void InternalResetValidatedCertificates()
        {
            // dispose outdated list
            foreach (KeyValuePair<string, Certificate> kvp in m_validatedCertificates)
            {
                kvp.Value?.Dispose();
            }
            m_validatedCertificates.Clear();
        }

        /// <summary>
        /// Validates a certificate chain.
        /// </summary>
        /// <param name="chain">The certificate chain to validate.</param>
        /// <param name="acceptError">
        /// Optional per-error callback. Called once for each suppressible
        /// validation error encountered during the walk. Returning
        /// <see langword="true"/> accepts the error; returning
        /// <see langword="false"/> (or omitting the callback) rejects it.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// A <see cref="CertificateValidationResult"/> describing the
        /// outcome. Throws no exceptions for ordinary validation failures.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="chain"/> is <c>null</c>.</exception>
        public async Task<CertificateValidationResult> ValidateAsync(
            CertificateCollection chain,
            Func<Certificate, ServiceResult, bool>? acceptError,
            CancellationToken ct)
        {
            if (chain == null)
            {
                throw new ArgumentNullException(nameof(chain));
            }
            if (chain.Count == 0)
            {
                return new CertificateValidationResult(
                    isValid: false,
                    statusCode: StatusCodes.BadCertificateInvalid,
                    errors: [new ServiceResult(StatusCodes.BadCertificateInvalid)],
                    isSuppressible: false);
            }

            Certificate certificate = chain[0];

            try
            {
                await InternalValidateAsync(chain, endpoint: null, ct).ConfigureAwait(false);

                m_validatedCertificates.GetOrAdd(
                   certificate.Thumbprint,
                   _ => Certificate.FromRawData(certificate.RawData));
                return CertificateValidationResult.Success;
            }
            catch (ServiceResultException se)
            {
                return HandleCertificateValidationException(se, certificate, acceptError);
            }
        }

        /// <summary>
        /// Returns the issuers for the certificate.
        /// </summary>
        public async Task<bool> GetIssuersAsync(
            Certificate certificate,
            IList<CertificateIdentifier> issuers,
            CancellationToken ct = default)
        {
            using var chain = new CertificateCollection { certificate };
            return await GetIssuersAsync(chain, issuers, validationErrors: null, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the issuers for the certificates, optionally collecting
        /// per-cert revocation errors.
        /// </summary>
        public async Task<bool> GetIssuersAsync(
            CertificateCollection certificates,
            IList<CertificateIdentifier> issuers,
            Dictionary<Certificate, ServiceResultException>? validationErrors,
            CancellationToken ct = default)
        {
            bool isTrusted = false;
            CertificateIdentifier? issuer = null;
            ServiceResultException? revocationStatus = null;
            Certificate? certificate = certificates[0];
            var ownedCertificates = new List<Certificate>();

            var untrustedList = new List<CertificateIdentifier>();
            for (int ii = 1; ii < certificates.Count; ii++)
            {
                untrustedList.Add(new CertificateIdentifier(certificates[ii]));
            }
            ArrayOf<CertificateIdentifier> untrustedCollection = untrustedList.ToArrayOf();

            do
            {
                // check for root.
                if (certificate == null || X509Utils.IsSelfSigned(certificate))
                {
                    break;
                }

                await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
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
                            validationErrors[certificate!] = revocationStatus!;
                        }

                        bool alreadyPresent = false;
                        foreach (CertificateIdentifier existing in issuers)
                        {
                            if (string.Equals(
                                existing.Thumbprint,
                                issuer.Thumbprint,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyPresent = true;
                                break;
                            }
                        }

                        if (alreadyPresent)
                        {
                            issuer.Dispose();
                            break;
                        }

                        issuers.Add(issuer!);

                        certificate = await issuer.FindAsync(
                            false,
                            applicationUri: null,
                            m_telemetry,
                            ct).ConfigureAwait(false);
                        if (certificate != null)
                        {
                            ownedCertificates.Add(certificate);
                        }
                    }
                }
                finally
                {
                    m_semaphore.Release();
                }
            } while (issuer != null);

            // dispose all intermediate certificates from the issuer chain walk
            foreach (Certificate owned in ownedCertificates)
            {
                owned.Dispose();
            }

            foreach (CertificateIdentifier untrusted in untrustedList)
            {
                untrusted.Dispose();
            }

            return isTrusted;
        }

        /// <summary>
        /// Validate domains in a server certificate against endpoint used for connection.
        /// </summary>
        /// <remarks>
        /// On a client: the endpoint is only checked if the certificate is not already validated.
        /// On a server: the endpoint is always checked but the certificate is not saved.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateHostNameInvalid"/>
        /// when the endpoint URL is not listed in the certificate.
        /// </exception>
        public void ValidateDomains(
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            bool serverValidation,
            Func<Certificate, ServiceResult, bool>? acceptError)
        {
            if (!serverValidation &&
                m_useValidatedCertificates &&
                m_validatedCertificates.TryGetValue(
                    serverCertificate.Thumbprint,
                    out Certificate? certificate2) &&
                Utils.IsEqual(certificate2.RawData, serverCertificate.RawData))
            {
                return;
            }

            Uri? endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null && !CertificateValidationHelpers.FindDomain(serverCertificate, endpointUrl))
            {
                const string message = "The domain '{0}' is not listed in the server certificate.";
                ServiceResultException serviceResult = ServiceResultException.Create(
                    StatusCodes.BadCertificateHostNameInvalid,
                    message,
                    endpointUrl.IdnHost);

                bool accept = false;
                if (acceptError != null)
                {
                    try
                    {
                        accept = acceptError(serverCertificate, new ServiceResult(serviceResult));
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            ex,
                            "AcceptError callback threw; treating as reject.");
                    }
                }

                if (!accept)
                {
                    if (serverValidation)
                    {
                        m_logger.LogError(
                            "The domain '{Url}' is not listed in the server certificate.",
                            Redact.Create(endpointUrl));
                    }
                    else
                    {
                        m_logger.LogError(
                            "Certificate {Certificate} rejected. Reason={ServiceResult}.",
                            serverCertificate,
                            Redact.Create(serviceResult));
                    }

                    throw serviceResult;
                }
            }
        }

        /// <summary>
        /// Validate application Uri in a server certificate against endpoint used for connection.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateUriInvalid"/>
        /// when the application URI cannot be found in the certificate.
        /// </exception>
        public void ValidateApplicationUri(
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            Func<Certificate, ServiceResult, bool>? acceptError)
        {
            ServiceResult serviceResult = CertificateValidationHelpers
                .ValidateServerCertificateApplicationUri(serverCertificate, endpoint);

            if (ServiceResult.IsBad(serviceResult))
            {
                bool accept = false;
                if (acceptError != null)
                {
                    try
                    {
                        accept = acceptError(serverCertificate, serviceResult);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            ex,
                            "AcceptError callback threw; treating as reject.");
                    }
                }

                if (!accept)
                {
                    m_logger.LogError(
                        "Certificate {Certificate} rejected. Reason={ServiceResult}.",
                        serverCertificate,
                        Redact.Create(serviceResult));

                    throw new ServiceResultException(serviceResult);
                }
            }
        }

        /// <summary>
        /// Validates a certificate chain. Throws on failure; the caller
        /// converts the thrown <see cref="ServiceResultException"/> into a
        /// <see cref="CertificateValidationResult"/> via
        /// <see cref="HandleCertificateValidationException(ServiceResultException, Certificate, Func{Certificate, ServiceResult, bool}?)"/>.
        /// </summary>
        /// <exception cref="ServiceResultException">If certificate[0] cannot be accepted</exception>
        private async Task InternalValidateAsync(
            CertificateCollection certificates,
            ConfiguredEndpoint? endpoint,
            CancellationToken ct = default)
        {
            Certificate certificate = certificates[0];

            // check for previously validated certificate.
            if (UseValidatedCertificates &&
                m_validatedCertificates.TryGetValue(
                    certificate.Thumbprint,
                    out Certificate? certificate2) &&
                Utils.IsEqual(certificate2.RawData, certificate.RawData))
            {
                return;
            }

            CertificateIdentifier? trustedCertificate =
                await GetTrustedCertificateAsync(certificate, ct).ConfigureAwait(false);

            // get the issuers (checks the revocation lists if using directory stores).
            var issuers = new List<CertificateIdentifier>();
            var validationErrors = new Dictionary<Certificate, ServiceResultException>();

            try
            {
                bool isIssuerTrusted = await GetIssuersAsync(
                    certificates,
                    issuers,
                    validationErrors,
                    ct)
                    .ConfigureAwait(false);

                ServiceResult? sresult = PopulateSresultWithValidationErrors(validationErrors);

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

                var extraStoreCerts = new List<X509Certificate2>();
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
                    extraStoreCerts.Add(issuer.Certificate!.AsX509Certificate2());
                    policy.ExtraStore.Add(extraStoreCerts[^1]);
                }

                // build chain.
                bool chainIncomplete = false;
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy = policy;
                    using X509Certificate2 certX509 = certificate.AsX509Certificate2();
                    chain.Build(certX509);

                    // check the chain results.
                    using CertificateIdentifier? fallbackTarget = trustedCertificate == null
                        ? new CertificateIdentifier(certificate) : null;
                    CertificateIdentifier target = trustedCertificate ?? fallbackTarget!;

                    foreach (X509ChainStatus chainStatus in chain.ChainStatus ?? [])
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

                        CertificateIdentifier? issuer = null;

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
                                    issuerCert.Subject);
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
                                ServiceResult? result = CheckChainStatus(
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

                foreach (X509Certificate2 extraCert in extraStoreCerts)
                {
                    extraCert.Dispose();
                }

                // check whether the chain is complete (if there is a chain)
                bool issuedByCA = !X509Utils.IsSelfSigned(certificate);
                if (issuers.Count > 0)
                {
                    Certificate? rootCertificate = issuers[^1].Certificate;
                    if (rootCertificate == null || !X509Utils.IsSelfSigned(rootCertificate))
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
                        null,
                        StatusCodes.BadCertificateUntrusted,
                        LocalizedText.From(message),
                        null,
                        sresult);
                }

                // check if certificate is trusted.
                if (trustedCertificate == null && !isIssuerTrusted)
                {
                    await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        // If the certificate is not trusted, check if the certificate is amongst the application certificates
                        bool isApplicationCertificate = false;
                        if (m_applicationCertificates != null)
                        {
                            foreach (Certificate appCert in m_applicationCertificates)
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
                                null,
                                StatusCodes.BadCertificateUntrusted,
                                LocalizedText.From(message),
                                null,
                                sresult);
                        }
                    }
                    finally
                    {
                        m_semaphore.Release();
                    }
                }

                Uri? endpointUrl = endpoint?.EndpointUrl;
                if (endpointUrl != null && !CertificateValidationHelpers.FindDomain(certificate, endpointUrl))
                {
                    string message = Utils.Format(
                        "The domain '{0}' is not listed in the server certificate.",
                        endpointUrl.IdnHost);
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateHostNameInvalid,
                        LocalizedText.From(message),
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
                            null,
                            StatusCodes.BadCertificateUseNotAllowed,
                            LocalizedText.From("Usage of ECDSA certificate is not allowed."),
                            null,
                            sresult);
                    }
                }
                else if ((certificateKeyUsage & X509KeyUsageFlags.DataEncipherment) == 0)
                {
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateUseNotAllowed,
                        LocalizedText.From("Usage of RSA certificate is not allowed."),
                        null,
                        sresult);
                }

                // check if minimum requirements are met
                if (RejectSHA1SignedCertificates &&
                    CertificateValidationHelpers.IsSHA1SignatureAlgorithm(certificate.SignatureAlgorithm))
                {
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificatePolicyCheckFailed,
                        LocalizedText.From("SHA1 signed certificates are not trusted."),
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
                            null,
                            StatusCodes.BadCertificatePolicyCheckFailed,
                            LocalizedText.From("Certificate doesn't meet minimum signature algorithm length requirement."),
                            null,
                            sresult);
                    }
                }
                else // RSA
                {
                    int keySize = X509Utils.GetRSAPublicKeySize(certificate);
                    if (keySize < MinimumCertificateKeySize)
                    {
                        sresult = new ServiceResult(
                            null,
                            StatusCodes.BadCertificatePolicyCheckFailed,
                            LocalizedText.From("Certificate doesn't meet minimum key length requirement."),
                            null,
                            sresult);
                    }
                }

                if (issuedByCA && chainIncomplete)
                {
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateChainIncomplete,
                        LocalizedText.From("Certificate chain validation incomplete."),
                        null,
                        sresult);
                }

                if (sresult != null)
                {
                    throw new ServiceResultException(sresult);
                }
            }
            finally
            {
                trustedCertificate?.Dispose();
                foreach (CertificateIdentifier issuer in issuers)
                {
                    issuer.Dispose();
                }
            }
        }

        /// <summary>
        /// Translates a thrown <see cref="ServiceResultException"/> into a
        /// <see cref="CertificateValidationResult"/>, applying the per-error
        /// <paramref name="acceptError"/> callback to suppressible errors and
        /// the <see cref="AutoAcceptUntrustedCertificates"/> flag to
        /// <see cref="StatusCodes.BadCertificateUntrusted"/>.
        /// </summary>
        private CertificateValidationResult HandleCertificateValidationException(
            ServiceResultException se,
            Certificate certificate,
            Func<Certificate, ServiceResult, bool>? acceptError)
        {
            // check for errors that may be suppressed.
            if (ContainsUnsuppressibleSC(se.Result))
            {
                m_logger.LogError(
                    "Certificate {Certificate} rejected. Reason={ServiceResult}.",
                    certificate,
                    se.Result);

                LogInnerServiceResults(LogLevel.Information, se.Result.InnerResult);

                ServiceResultException unsuppressible = new ServiceResultException(
                    se,
                    StatusCodes.BadCertificateInvalid);
                return new CertificateValidationResult(
                    isValid: false,
                    statusCode: unsuppressible.StatusCode,
                    errors: [unsuppressible.Result],
                    isSuppressible: false);
            }

            // invoke callback per inner-error.
            bool accept = false;
            ServiceResult serviceResult = se.Result;
            do
            {
                accept = false;
                if (acceptError != null)
                {
                    try
                    {
                        accept = acceptError(certificate, serviceResult);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            ex,
                            "AcceptError callback threw; treating as reject.");
                        accept = false;
                    }
                }
                else if (m_autoAcceptUntrustedCertificates &&
                    serviceResult.StatusCode == StatusCodes.BadCertificateUntrusted)
                {
                    accept = true;
                    m_logger.LogInformation("Auto accepted certificate {Certificate}", certificate);
                }

                if (accept)
                {
                    serviceResult = serviceResult.InnerResult;
                }
                else
                {
                    // report the rejected service result
                    se = new ServiceResultException(serviceResult);
                }
            } while (accept && serviceResult != null);

            if (!accept)
            {
                m_logger.LogError(
                    "Certificate {Certificate} validation failed with suppressible errors but was rejected. Reason={ServiceResult}.",
                    certificate,
                    se.Result.ToLongString());
                LogInnerServiceResults(LogLevel.Error, se.Result.InnerResult);

                ServiceResultException suppressible = new ServiceResultException(
                    se,
                    StatusCodes.BadCertificateInvalid);
                return new CertificateValidationResult(
                    isValid: false,
                    statusCode: suppressible.StatusCode,
                    errors: [suppressible.Result],
                    isSuppressible: true);
            }

            // accepted; cache for future fast-path
            m_validatedCertificates.GetOrAdd(
                certificate.Thumbprint,
                _ => Certificate.FromRawData(certificate.RawData));
            return CertificateValidationResult.Success;
        }

        /// <summary>
        /// Recursively checks whether any of the service results or inner service results
        /// of the input sr must not be suppressed.
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
        /// Returns the certificate information for a trusted peer certificate.
        /// </summary>
        private async Task<CertificateIdentifier?> GetTrustedCertificateAsync(
            Certificate certificate,
            CancellationToken ct = default)
        {
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // check if explicitly trusted.
                if (!m_trustedCertificateList.IsEmpty)
                {
                    for (int ii = 0; ii < m_trustedCertificateList.Count; ii++)
                    {
                        Certificate? trusted = await m_trustedCertificateList[ii]
                            .FindAsync(false, applicationUri: null, m_telemetry, ct)
                            .ConfigureAwait(false);

                        if (trusted != null &&
                            trusted.Thumbprint == certificate.Thumbprint &&
                            Utils.IsEqual(trusted.RawData, certificate.RawData))
                        {
                            // return an owned copy so the caller can safely dispose it
                            return new CertificateIdentifier(
                                trusted,
                                m_trustedCertificateList[ii].ValidationOptions);
                        }

                        trusted?.Dispose();
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
                            using CertificateCollection trusted = await store
                                .FindByThumbprintAsync(certificate.Thumbprint, ct)
                                .ConfigureAwait(false);

                            for (int ii = 0; ii < trusted.Count; ii++)
                            {
                                if (Utils.IsEqual(trusted[ii].RawData, certificate.RawData))
                                {
                                    return new CertificateIdentifier(
                                        trusted[ii].AddRef(),
                                        m_trustedCertificateStore.ValidationOptions);
                                }
                            }
                        }
                        finally
                        {
                            store.Dispose();
                        }
                    }
                }
            }
            finally
            {
                m_semaphore.Release();
            }

            // not a trusted.
            return null;
        }

        /// <summary>
        /// Returns true if the certificate matches the criteria.
        /// </summary>
        private static bool Match(
            Certificate certificate,
            X500DistinguishedName subjectName,
            string? serialNumber,
            string? authorityKeyId)
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
                X509SubjectKeyIdentifierExtension? subjectKeyId =
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
        private async Task<(CertificateIdentifier?, ServiceResultException?)> GetIssuerNoExceptionAsync(
            Certificate certificate,
            ArrayOf<CertificateIdentifier> explicitList,
            CertificateStoreIdentifier? certificateStore,
            bool checkRecovationStatus,
            CancellationToken ct = default)
        {
            ServiceResultException? serviceResult = null;

#if DEBUG // check if not self-signed, tested in outer loop
            System.Diagnostics.Debug.Assert(!X509Utils.IsSelfSigned(certificate));
#endif

            X500DistinguishedName subjectName = certificate.IssuerName;
            string? keyId = null;
            string? serialNumber = null;

            // find the authority key identifier.
            X509AuthorityKeyIdentifierExtension? authority =
                certificate.FindExtension<X509AuthorityKeyIdentifierExtension>();
            if (authority != null)
            {
                keyId = authority.KeyIdentifier;
                serialNumber = authority.SerialNumber;
            }

            // check in explicit list.
            if (!explicitList.IsEmpty)
            {
                for (int ii = 0; ii < explicitList.Count; ii++)
                {
                    Certificate? issuer = await explicitList[ii].FindAsync(
                        false,
                        applicationUri: null,
                        m_telemetry,
                        ct)
                        .ConfigureAwait(false);

                    if (issuer != null)
                    {
                        if (!X509Utils.IsIssuerAllowed(issuer))
                        {
                            issuer.Dispose();
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

                        issuer.Dispose();
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

                    using CertificateCollection certificates = await store.EnumerateAsync(ct)
                        .ConfigureAwait(false);

                    for (int ii = 0; ii < certificates.Count; ii++)
                    {
                        Certificate issuer = certificates[ii];

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
                                                status = StatusCodes.BadCertificateIssuerRevocationUnknown;
                                            }

                                            if (m_rejectUnknownRevocationStatus &&
                                                (
                                                    options & CertificateValidationOptions.SuppressRevocationStatusUnknown
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
                                                status = StatusCodes.BadCertificateIssuerRevoked;
                                            }
                                            serviceResult = new ServiceResultException(status);
                                        }
                                    }
                                }

                                // already checked revocation for file based stores. windows based stores always suppress.
                                options
                                    |= CertificateValidationOptions.SuppressRevocationStatusUnknown;

                                return (new CertificateIdentifier(issuer.AddRef(), options), serviceResult);
                            }
                        }
                    }
                }
                finally
                {
                    store?.Dispose();
                }
            }

            // not a trusted issuer.
            return (null, null);
        }

        /// <summary>
        /// Returns the certificate information for a trusted issuer certificate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task<CertificateIdentifier?> GetIssuerAsync(
            Certificate certificate,
            ArrayOf<CertificateIdentifier> explicitList,
            CertificateStoreIdentifier? certificateStore,
            bool checkRecovationStatus,
            CancellationToken ct = default)
        {
            // check for root.
            if (X509Utils.IsSelfSigned(certificate))
            {
                return null;
            }

            (CertificateIdentifier? result, ServiceResultException? srex)
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
        /// Returns an error if the chain status elements indicate an error.
        /// </summary>
        private ServiceResult? CheckChainStatus(
            X509ChainStatus status,
            CertificateIdentifier id,
            CertificateIdentifier? issuer,
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
                    if (!CertificateValidationHelpers.IsSignatureValid(id.Certificate))
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

        private static ServiceResult? PopulateSresultWithValidationErrors(
            Dictionary<Certificate, ServiceResultException> validationErrors)
        {
            var p1List = new Dictionary<Certificate, ServiceResultException>();
            var p2List = new Dictionary<Certificate, ServiceResultException>();
            var p3List = new Dictionary<Certificate, ServiceResultException>();

            ServiceResult? sresult = null;

            foreach (KeyValuePair<Certificate, ServiceResultException> kvp in validationErrors)
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
                        LocalizedText message = CertificateMessage(
                            "Certificate issuer revocation list not found.",
                            kvp.Key);
                        sresult = new ServiceResult(
                            null,
                            StatusCodes.BadCertificateIssuerRevocationUnknown,
                            message,
                            null,
                            sresult);
                    }
                    else if (StatusCode.IsBad(kvp.Value.StatusCode))
                    {
                        LocalizedText message = CertificateMessage(
                            "Unknown error while trying to determine the revocation status.",
                            kvp.Key);
                        sresult = new ServiceResult(
                            null,
                            kvp.Value.StatusCode,
                            message,
                            null,
                            sresult);
                    }
                }
            }

            if (p3List.Count > 0)
            {
                foreach (KeyValuePair<Certificate, ServiceResultException> kvp in p3List)
                {
                    LocalizedText message = CertificateMessage(
                        "Certificate revocation list not found.",
                        kvp.Key);
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateRevocationUnknown,
                        message,
                        null,
                        sresult);
                }
            }
            if (p2List.Count > 0)
            {
                foreach (KeyValuePair<Certificate, ServiceResultException> kvp in p2List)
                {
                    LocalizedText message = CertificateMessage("Certificate issuer is revoked.", kvp.Key);
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateIssuerRevoked,
                        message,
                        null,
                        sresult);
                }
            }
            if (p1List.Count > 0)
            {
                foreach (KeyValuePair<Certificate, ServiceResultException> kvp in p1List)
                {
                    LocalizedText message = CertificateMessage("Certificate is revoked.", kvp.Key);
                    sresult = new ServiceResult(
                        null,
                        StatusCodes.BadCertificateRevoked,
                        message,
                        null,
                        sresult);
                }
            }

            return sresult;
        }

        /// <summary>
        /// Returns a certificate information message.
        /// </summary>
        private static LocalizedText CertificateMessage(string error, Certificate certificate)
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
            return new LocalizedText(message.ToString());
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
    }
}
