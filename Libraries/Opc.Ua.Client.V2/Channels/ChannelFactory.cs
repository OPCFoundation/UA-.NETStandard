// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default channel factory implementation that creates transport channels
    /// using the configured bindings.
    /// </summary>
    internal class ChannelFactory : IChannelFactory
    {
        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Create a channel factory with the specified configuration.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="telemetry"></param>
        public ChannelFactory(ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            m_configuration = configuration;
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public ITransportChannel CreateChannel(ConfiguredEndpoint endpoint,
            IServiceMessageContext messageContext,
            X509Certificate2? clientCertificate,
            X509Certificate2Collection? clientCertificateChain)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return SessionChannel.Create(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                clientCertificate,
                clientCertificateChain,
                messageContext);
#pragma warning restore CS0618
        }
    }
}
