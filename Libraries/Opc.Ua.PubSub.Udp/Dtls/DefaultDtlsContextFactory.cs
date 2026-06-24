/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Default BCL-backed DTLS context factory.
    /// </summary>
    public sealed class DefaultDtlsContextFactory : IDtlsContextFactory
    {
        /// <summary>
        /// Initializes a new <see cref="DefaultDtlsContextFactory"/>.
        /// </summary>
        public DefaultDtlsContextFactory(
            IOptions<DtlsTransportOptions> options,
            DtlsProfileRegistry profileRegistry,
            ICertificateValidatorEx? certificateValidator = null,
            ICertificateProvider? certificateProvider = null,
            ApplicationConfiguration? applicationConfiguration = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (profileRegistry is null)
            {
                throw new ArgumentNullException(nameof(profileRegistry));
            }

            Options = options.Value ?? new DtlsTransportOptions();
            ProfileRegistry = profileRegistry;
            CertificateValidator = certificateValidator ?? applicationConfiguration?.CertificateManager;
            CertificateProvider = certificateProvider ??
                (certificateValidator as ICertificateManager)?.CertificateProvider ??
                applicationConfiguration?.CertificateManager?.CertificateProvider;
            ApplicationConfiguration = applicationConfiguration;
        }

        /// <summary>
        /// Direct-construct fallback options.
        /// </summary>
        public DtlsTransportOptions Options { get; }

        /// <summary>
        /// Runtime DTLS profile registry.
        /// </summary>
        public DtlsProfileRegistry ProfileRegistry { get; }

        /// <summary>
        /// Injected stack certificate validator used for DTLS peer authentication.
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; }

        /// <summary>
        /// Injected certificate provider used to resolve identifier-backed local certificates.
        /// </summary>
        public ICertificateProvider? CertificateProvider { get; }

        /// <summary>
        /// Optional application configuration used for certificate-store passwords and URI fallback.
        /// </summary>
        public ApplicationConfiguration? ApplicationConfiguration { get; }

        /// <inheritdoc/>
        public async ValueTask<IDtlsContext> CreateAsync(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            DtlsProfile profile,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            CancellationToken cancellationToken = default)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (!endpoint.IsValid)
            {
                throw new ArgumentException("DTLS endpoint is not valid.", nameof(endpoint));
            }

            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            cancellationToken.ThrowIfCancellationRequested();
            ILogger logger = telemetry.CreateLogger<DefaultDtlsContextFactory>();
            logger.LogInformation(
                "Creating OPC UA PubSub DTLS context: connection='{Connection}' endpoint={Endpoint} profile={Profile}.",
                connection.Name,
                endpoint,
                profile.Name);
            List<Certificate> resolvedLocalCertificates = await ResolveLocalCertificatesAsync(
                    telemetry,
                    logger,
                    cancellationToken)
                .ConfigureAwait(false);
            DtlsTransportOptions effectiveOptions = resolvedLocalCertificates.Count == 0
                ? Options
                : CreateEffectiveOptions(resolvedLocalCertificates);
            // CA2000: ownership is transferred to DtlsDatagramTransport, which disposes the context on close.
            // TODO(CA2000): introduce an owned-context result type if this factory gains additional disposable contexts.
#pragma warning disable CA2000
            IDtlsContext context;
            try
            {
                context = new DtlsHandshakeContext(
                    profile,
                    effectiveOptions,
                    CertificateValidator ?? effectiveOptions.PeerCertificateValidator,
                    DetermineRole(connection),
                    endpoint,
                    timeProvider);
            }
            catch
            {
                DisposeCertificates(resolvedLocalCertificates);
                throw;
            }
#pragma warning restore CA2000
            if (resolvedLocalCertificates.Count != 0)
            {
                context = new ResolvedLocalCertificateDtlsContext(context, resolvedLocalCertificates);
            }
            return context;
        }

        private static DtlsEndpointRole DetermineRole(PubSubConnectionDataType connection)
        {
            bool hasWriters = !connection.WriterGroups.IsNull && connection.WriterGroups.Count > 0;
            bool hasReaders = !connection.ReaderGroups.IsNull && connection.ReaderGroups.Count > 0;
            return hasWriters && !hasReaders ? DtlsEndpointRole.Client : DtlsEndpointRole.Server;
        }

        private async ValueTask<List<Certificate>> ResolveLocalCertificatesAsync(
            ITelemetryContext telemetry,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var resolvedCertificates = new List<Certificate>();
            if (Options.LocalCertificateIdentifiers.Count == 0)
            {
                return resolvedCertificates;
            }

            ICertificatePasswordProvider? passwordProvider = ApplicationConfiguration
                ?.SecurityConfiguration
                ?.CertificatePasswordProvider;
            string? applicationUri = ApplicationConfiguration?.ApplicationUri;
            foreach (CertificateIdentifier identifier in Options.LocalCertificateIdentifiers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (identifier is null)
                {
                    continue;
                }

                try
                {
                    Certificate? certificate = CertificateProvider is not null
                        ? await CertificateProvider
                            .GetPrivateKeyCertificateAsync(identifier, passwordProvider, applicationUri, cancellationToken)
                            .ConfigureAwait(false)
                        : await CertificateIdentifierResolver
                            .LoadPrivateKeyAsync(identifier, passwordProvider, applicationUri, telemetry, cancellationToken)
                            .ConfigureAwait(false);
                    if (certificate?.HasPrivateKey == true)
                    {
                        resolvedCertificates.Add(certificate);
                        logger.LogInformation(
                            "Resolved OPC UA PubSub DTLS local certificate identifier '{Identifier}'.",
                            identifier);
                    }
                    else
                    {
                        certificate?.Dispose();
                        logger.LogWarning(
                            "OPC UA PubSub DTLS local certificate identifier '{Identifier}' did not resolve to a " +
                            "certificate with a private key.",
                            identifier);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to resolve OPC UA PubSub DTLS local certificate identifier '{Identifier}'.",
                        identifier);
                }
            }

            return resolvedCertificates;
        }

        private DtlsTransportOptions CreateEffectiveOptions(IReadOnlyList<Certificate> resolvedLocalCertificates)
        {
            var options = new DtlsTransportOptions
            {
                PreferredProfileName = Options.PreferredProfileName,
                MaxHandshakeDatagramSize = Options.MaxHandshakeDatagramSize,
                InitialRetransmissionTimeout = Options.InitialRetransmissionTimeout,
                MaxRetransmissionTimeout = Options.MaxRetransmissionTimeout,
                RequireHelloRetryRequestCookie = Options.RequireHelloRetryRequestCookie,
                PeerCertificateValidator = Options.PeerCertificateValidator,
                RequireClientCertificate = Options.RequireClientCertificate
            };

            foreach (string disabledProfile in Options.DisabledProfiles)
            {
                options.DisabledProfiles.Add(disabledProfile);
            }

            foreach (Certificate certificate in Options.LocalCertificates)
            {
                options.LocalCertificates.Add(certificate);
            }

            foreach (Certificate certificate in resolvedLocalCertificates)
            {
                options.LocalCertificates.Add(certificate);
            }

            return options;
        }

        private sealed class ResolvedLocalCertificateDtlsContext : IDtlsContext
        {
            private readonly IDtlsContext m_inner;
            private readonly IReadOnlyList<Certificate> m_resolvedLocalCertificates;

            public ResolvedLocalCertificateDtlsContext(
                IDtlsContext inner,
                IReadOnlyList<Certificate> resolvedLocalCertificates)
            {
                m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
                m_resolvedLocalCertificates = resolvedLocalCertificates
                    ?? throw new ArgumentNullException(nameof(resolvedLocalCertificates));
            }

            /// <inheritdoc/>
            public DtlsProfile Profile => m_inner.Profile;

            /// <inheritdoc/>
            public ValueTask OpenAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken = default)
            {
                return m_inner.OpenAsync(channel, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<ReadOnlyMemory<byte>> ProtectAsync(
                ReadOnlyMemory<byte> payload,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ProtectAsync(payload, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<ReadOnlyMemory<byte>> UnprotectAsync(
                ReadOnlyMemory<byte> record,
                CancellationToken cancellationToken = default)
            {
                return m_inner.UnprotectAsync(record, cancellationToken);
            }

            public void Dispose()
            {
                try
                {
                    m_inner.Dispose();
                }
                finally
                {
                    DisposeCertificates(m_resolvedLocalCertificates);
                }
            }
        }

        private static void DisposeCertificates(IReadOnlyList<Certificate> certificates)
        {
            foreach (Certificate certificate in certificates)
            {
                certificate.Dispose();
            }
        }
    }

    /// <summary>
    /// Identifies whether a DTLS endpoint drives the handshake as client or server.
    /// </summary>
    internal enum DtlsEndpointRole
    {
        Client,
        Server
    }
}
