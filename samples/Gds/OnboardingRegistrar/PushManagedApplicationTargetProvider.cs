/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;

namespace OnboardingRegistrar
{
    /// <summary>
    /// Resolves one operator-configured managed server without trusting discovery metadata.
    /// </summary>
    internal sealed class PushManagedApplicationTargetProvider : IDeviceRegistrarManagedApplicationProvider
    {
        /// <summary>
        /// Creates a provider for one managed application and its explicit connection policy.
        /// </summary>
        public PushManagedApplicationTargetProvider(
            string applicationUri,
            string endpointUrl,
            string securityPolicyUri,
            string certificatePath,
            ArrayOf<string> serverCapabilities,
            ExpandedNodeId certificateTypeId,
            IClientIdentityProvider identityProvider,
            Func<ApplicationConfiguration> configurationFactory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationUri);
            ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(securityPolicyUri);
            ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
            m_applicationUri = applicationUri;
            m_endpointUrl = endpointUrl;
            m_securityPolicyUri = securityPolicyUri;
            m_certificatePath = certificatePath;
            m_certificateTypeId = certificateTypeId;
            if (serverCapabilities.IsEmpty)
            {
                throw new ArgumentException(
                    "Explicit managed server capabilities are required.", nameof(serverCapabilities));
            }
            for (int index = 0; index < serverCapabilities.Count; index++)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(serverCapabilities[index]);
            }
            m_serverCapabilities = serverCapabilities.Span.ToArray().ToArrayOf();
            m_identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
            m_configurationFactory = configurationFactory ??
                throw new ArgumentNullException(nameof(configurationFactory));
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _) ||
                !Uri.TryCreate(endpointUrl, UriKind.Absolute, out _) ||
                !Uri.TryCreate(securityPolicyUri, UriKind.Absolute, out _) ||
                !Path.IsPathFullyQualified(certificatePath) ||
                !IsSupportedSecurityPolicy(securityPolicyUri))
            {
                throw new ArgumentException(
                    "Managed application settings must identify absolute URIs " +
                    "and a supported SHA-256 security policy.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarManagedApplicationTarget> GetTargetAsync(
            string applicationUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(applicationUri, m_applicationUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            byte[] encodedCertificate = await File.ReadAllBytesAsync(
                m_certificatePath, cancellationToken).ConfigureAwait(false);
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(encodedCertificate);
            if (certificate.HasPrivateKey)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The managed application certificate file must not contain a private key.");
            }
            ByteString installedCertificate = ByteString.From(certificate.RawData.AsSpan());

            ApplicationConfiguration configuration = m_configurationFactory() ??
                throw new InvalidOperationException("The client application configuration is not initialized.");
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            endpointConfiguration.OperationTimeout = 15000;
            using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                configuration,
                new Uri(m_endpointUrl),
                endpointConfiguration,
                ct: cancellationToken).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await discovery.GetEndpointsAsync(
                default, cancellationToken).ConfigureAwait(false);

            EndpointDescription? selected = null;
            for (int index = 0; index < endpoints.Count; index++)
            {
                EndpointDescription offered = endpoints[index];
                if (!string.Equals(offered.EndpointUrl, m_endpointUrl, StringComparison.Ordinal) ||
                    !string.Equals(offered.Server?.ApplicationUri, m_applicationUri, StringComparison.Ordinal) ||
                    offered.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    !string.Equals(offered.SecurityPolicyUri, m_securityPolicyUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (selected != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The configured managed endpoint is ambiguous.");
                }
                selected = CoreUtils.Clone(offered)!;
            }
            if (selected == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Discovery did not return the configured encrypted endpoint for the managed application.");
            }

            return new DeviceRegistrarManagedApplicationTarget(
                [m_endpointUrl],
                installedCertificate,
                new ConfiguredEndpoint(null, selected, endpointConfiguration),
                await m_identityProvider.AcquireIdentityAsync(
                    selected, configuration.CreateMessageContext(), cancellationToken).ConfigureAwait(false),
                m_serverCapabilities)
            {
                CertificateTypeId = m_certificateTypeId
            };
        }

        /// <summary>
        /// Restricts managed application endpoints to the stack's supported SHA-256 security policies.
        /// </summary>
        private static bool IsSupportedSecurityPolicy(string securityPolicyUri)
        {
            return securityPolicyUri is SecurityPolicies.Basic256Sha256 or
                SecurityPolicies.Aes128_Sha256_RsaOaep or
                SecurityPolicies.Aes256_Sha256_RsaPss;
        }

        /// <summary>The application URI authorized by the sample policy.</summary>
        private readonly string m_applicationUri;

        /// <summary>The exact endpoint URL selected by the operator and advertised to the GDS.</summary>
        private readonly string m_endpointUrl;

        /// <summary>The exact encrypted security policy selected by the operator.</summary>
        private readonly string m_securityPolicyUri;

        /// <summary>The operator-supplied public certificate file for the currently installed credential.</summary>
        private readonly string m_certificatePath;

        /// <summary>The optional operator-selected certificate type for this managed application.</summary>
        private readonly ExpandedNodeId m_certificateTypeId;

        /// <summary>The operator-selected server capability identifiers.</summary>
        private readonly ArrayOf<string> m_serverCapabilities;

        /// <summary>Resolves the host-selected identity from its dedicated secret registry for each attempt.</summary>
        private readonly IClientIdentityProvider m_identityProvider;

        /// <summary>Resolves the initialized client configuration with explicit peer trust.</summary>
        private readonly Func<ApplicationConfiguration> m_configurationFactory;
    }
}
