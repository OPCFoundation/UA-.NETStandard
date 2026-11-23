// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Object that creates an instance of a Session object.
    /// It can be used to create instances of enhanced Session
    /// classes with added functionality or overridden methods.
    /// </summary>
    public class TraceableRequestHeaderClientSessionFactory : DefaultSessionFactory
    {
        public TraceableRequestHeaderClientSessionFactory(ITelemetryContext telemetry)
            : base(telemetry)
        {
            ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
        }

        /// <inheritdoc/>
        public override ISession Create(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            ArrayOf<EndpointDescription> availableEndpoints,
            ArrayOf<string> discoveryProfileUris)
        {
            return new TraceableRequestHeaderClientSession(
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
