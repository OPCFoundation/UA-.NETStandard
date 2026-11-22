// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// Default <see cref="IMqttTrustedIssuerResolver"/> that resolves CA references from
    /// the application's trusted issuer certificate store
    /// (<c>SecurityConfiguration.TrustedIssuerCertificates</c>).
    /// </summary>
    /// <remarks>
    /// A reference matches a stored certificate when it equals either the certificate
    /// subject distinguished name or its thumbprint (case-insensitive). Only public CA
    /// certificates are returned, so no private key material is touched.
    /// </remarks>
    internal sealed class TrustedIssuerStoreResolver : IMqttTrustedIssuerResolver
    {
        private readonly ApplicationConfiguration? m_configuration;

        /// <summary>
        /// Initializes a new <see cref="TrustedIssuerStoreResolver"/>.
        /// </summary>
        /// <param name="configuration">
        /// The application configuration whose trusted issuer store is searched. When
        /// <see langword="null"/> the resolver always returns an empty collection.
        /// </param>
        public TrustedIssuerStoreResolver(ApplicationConfiguration? configuration = null)
        {
            m_configuration = configuration;
        }

        /// <inheritdoc/>
        public async ValueTask<CertificateCollection> ResolveAsync(
            IReadOnlyList<string> subjects,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var result = new CertificateCollection();
            if (subjects is null || subjects.Count == 0)
            {
                return result;
            }

            ILogger logger = telemetry.CreateLogger<TrustedIssuerStoreResolver>();
            CertificateStoreIdentifier? storeIdentifier =
                m_configuration?.SecurityConfiguration?.TrustedIssuerCertificates;
            if (storeIdentifier is null)
            {
                logger.TrustedIssuerCertificateStoreNotAvailable();
                return result;
            }

            try
            {
                using ICertificateStore store = storeIdentifier.OpenStore(telemetry);
                using CertificateCollection candidates = await store
                    .EnumerateAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (Certificate candidate in candidates)
                {
                    if (Matches(candidate, subjects))
                    {
                        // CertificateCollection.Add takes its own independent handle (AddRef);
                        // the enumerated candidates are released when 'candidates' is disposed.
                        result.Add(candidate);
                    }
                }

                foreach (string subject in subjects)
                {
                    if (!string.IsNullOrWhiteSpace(subject) && !Contains(result, subject))
                    {
                        logger.TrustedIssuerCertificateNotFound(subject);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Dispose();
                logger.FailedToResolveTrustedIssuerCertificates(ex);
                throw;
            }
            catch
            {
                result.Dispose();
                throw;
            }

            return result;
        }

        private static bool Matches(Certificate certificate, IReadOnlyList<string> subjects)
        {
            foreach (string subject in subjects)
            {
                if (string.IsNullOrWhiteSpace(subject))
                {
                    continue;
                }
                if (string.Equals(certificate.Subject, subject, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(certificate.Thumbprint, subject, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(CertificateCollection resolved, string subject)
        {
            foreach (Certificate certificate in resolved)
            {
                if (string.Equals(certificate.Subject, subject, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(certificate.Thumbprint, subject, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Source-generated log messages for TrustedIssuerStoreResolver.
    /// </summary>
    internal static partial class TrustedIssuerStoreResolverLog
    {
        [LoggerMessage(EventId = PubSubMqttEventIds.TrustedIssuerStoreResolver + 0, Level = LogLevel.Warning,
            Message = "MQTT TrustedIssuerCertificateSubjects are configured but no trusted issuer " +
                "certificate store is available; the broker chain falls back to the platform trust store.")]
        public static partial void TrustedIssuerCertificateStoreNotAvailable(this ILogger logger);

        [LoggerMessage(EventId = PubSubMqttEventIds.TrustedIssuerStoreResolver + 1, Level = LogLevel.Warning,
            Message = "MQTT trusted issuer certificate '{Subject}' was not found in the trusted issuer store.")]
        public static partial void TrustedIssuerCertificateNotFound(
            this ILogger logger,
            [ApplicationIdentity] string subject);

        [LoggerMessage(EventId = PubSubMqttEventIds.TrustedIssuerStoreResolver + 2, Level = LogLevel.Error,
            Message = "Failed to resolve MQTT trusted issuer certificates from the trusted issuer store.")]
        public static partial void FailedToResolveTrustedIssuerCertificates(
            this ILogger logger,
            Exception exception);
    }

}
