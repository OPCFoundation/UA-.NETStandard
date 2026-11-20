// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Create binary channels
    /// </summary>
    public static class UaChannelBase
    {
        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                securityPolicies: null,
                ct: ct);
        }

        /// <summary>
        /// Creates a new UA-binary transport channel using the specified security policies.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ISecurityPolicyRegistry? securityPolicies,
            CancellationToken ct = default)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                transportChannelBindings: null,
                securityPolicies: securityPolicies,
                ct: ct).AsTask();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="ct">The cancellation token</param>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                securityPolicies: null,
                ct: ct);
        }

        /// <summary>
        /// Creates a new UA-binary transport channel using the specified security policies.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="securityPolicies">The security policy registry used by the channel.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext,
            ISecurityPolicyRegistry? securityPolicies,
            CancellationToken ct = default)
        {
            return ClientChannelManager.CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                transportChannelBindings: null,
                securityPolicies: securityPolicies,
                ct: ct).AsTask();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <param name="ct">The cancellation token</param>
        public static Task<ITransportChannel> CreateUaBinaryChannelAsync(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                null,
                messageContext,
                ct);
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                connection,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                messageContext,
                default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        [Obsolete("Use ClientChannelFactory.CreateChannelAsync instead")]
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            IServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannelAsync(
                configuration,
                description,
                endpointConfiguration,
                clientCertificate,
                clientCertificateChain,
                messageContext,
                default).GetAwaiter().GetResult();
        }
    }
}
