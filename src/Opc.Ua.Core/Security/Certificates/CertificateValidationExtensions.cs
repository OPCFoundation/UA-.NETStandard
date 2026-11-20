// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Endpoint-aware validation extensions on
    /// <see cref="ICertificateValidatorEx"/>.
    /// </summary>
    /// <remarks>
    /// These extensions provide the
    /// <c>ValidateApplicationUri</c> / <c>ValidateDomains</c> capabilities
    /// originally available on the legacy <c>CertificateValidator</c>
    /// class. When the receiver is a <see cref="CertificateManager"/>,
    /// the extension delegates to the manager's instance methods so the
    /// rejected-certificate processor is consulted. For other
    /// implementations of <see cref="ICertificateValidatorEx"/>, the
    /// extension performs a pure (cache-free, event-free) check that
    /// throws on validation failure.
    /// </remarks>
    public static class CertificateValidationExtensions
    {
        /// <summary>
        /// Validates the application URI in the server certificate against
        /// the URI in the endpoint description.
        /// </summary>
        /// <param name="validator">The validator (receiver).</param>
        /// <param name="serverCertificate">The server certificate that
        /// contains the application URI.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="validator"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateUriInvalid"/>
        /// when the application URI can not be found in the certificate.
        /// </exception>
        public static void ValidateApplicationUri(
            this ICertificateValidatorEx validator,
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            // Prefer the CertificateManager's instance method when available
            // so the rejected-certificate processor is consulted.
            if (validator is CertificateManager manager)
            {
                manager.ValidateApplicationUri(serverCertificate, endpoint);
                return;
            }

            ServiceResult serviceResult = CertificateValidationHelpers
                .ValidateServerCertificateApplicationUri(serverCertificate, endpoint);
            if (ServiceResult.IsBad(serviceResult))
            {
                throw new ServiceResultException(serviceResult);
            }
        }

        /// <summary>
        /// Validates that the endpoint URL host appears in the server
        /// certificate's domain list.
        /// </summary>
        /// <param name="validator">The validator (receiver).</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <param name="serverValidation">
        /// Whether this is a server-side validation (changes how the
        /// failure is logged).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="validator"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateHostNameInvalid"/>
        /// when the endpoint URL host is not listed in the certificate.
        /// </exception>
        public static void ValidateDomains(
            this ICertificateValidatorEx validator,
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            bool serverValidation = false)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            if (validator is CertificateManager manager)
            {
                manager.ValidateDomains(serverCertificate, endpoint, serverValidation);
                return;
            }

            Uri? endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null &&
                !CertificateValidationHelpers.FindDomain(serverCertificate, endpointUrl))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadCertificateHostNameInvalid,
                    "The domain '{0}' is not listed in the server certificate.",
                    endpointUrl.IdnHost);
            }
        }

        /// <summary>
        /// Checks the host name of the endpoint URL against the server certificate,
        /// as Part 4 §6.1.3 requires of a client. When <paramref name="enforce"/> is
        /// <c>true</c> a mismatch fails with
        /// <see cref="StatusCodes.BadCertificateHostNameInvalid"/> (unless the
        /// validator accepts the error); otherwise the suppressed error is reported
        /// as a warning instead of being skipped silently.
        /// </summary>
        /// <param name="validator">The validator, or <c>null</c> when none is configured.</param>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <param name="enforce">Whether a mismatch fails the connection.</param>
        /// <param name="logger">The logger that reports a suppressed mismatch.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadCertificateHostNameInvalid"/>
        /// when <paramref name="enforce"/> is <c>true</c> and the host is not listed.
        /// </exception>
        public static void CheckServerDomain(
            this ICertificateValidatorEx? validator,
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint,
            bool enforce,
            ILogger logger)
        {
            if (enforce)
            {
                validator?.ValidateDomains(serverCertificate, endpoint);
                return;
            }

            Uri? endpointUrl = endpoint?.EndpointUrl;
            if (endpointUrl != null &&
                !CertificateValidationHelpers.FindDomain(serverCertificate, endpointUrl))
            {
                logger.ServerCertificateHostNameNotChecked(endpointUrl.IdnHost);
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="CertificateValidationExtensions"/>.
    /// </summary>
    internal static partial class CertificateValidationExtensionsLog
    {
        [LoggerMessage(EventId = CoreEventIds.CertificateValidationCore + 18, Level = LogLevel.Warning,
            Message = "The host {Host} of the endpoint URL is not listed in the server certificate. " +
                "The connection continues because the host name check is disabled (CheckDomain = false).")]
        public static partial void ServerCertificateHostNameNotChecked(
            this ILogger logger, [NetworkAddress] string host);
    }
}
