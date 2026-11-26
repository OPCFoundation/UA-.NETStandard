// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Object that creates instances of an Opc.Ua.Client.Session object.
    /// </summary>
    public class TestableSessionFactory : DefaultSessionFactory
    {
        /// <summary>
        /// Force use of the default instance.
        /// </summary>
        public TestableSessionFactory(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <inheritdoc/>
        public override ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default)
        {
            return new TestableSession(
                channel,
                configuration,
                endpoint,
                clientCertificate!,
                availableEndpoints,
                discoveryProfileUris,
                SubscriptionEngineFactory);
        }
    }
}
