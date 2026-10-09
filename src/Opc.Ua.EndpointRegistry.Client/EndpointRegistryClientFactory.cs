using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.SchemaRegistry;

namespace Opc.Ua.EndpointRegistry.Client
{
    /// <summary>Creates session-bound native clients without taking ownership of sessions.</summary>
    public sealed class EndpointRegistryClientFactory
    {
        /// <summary>Creates the factory with the host's telemetry context.</summary>
        public EndpointRegistryClientFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>Creates a client bound directly to the selected well-known nodes.</summary>
        public EndpointRegistryClient Create(
            ISession session, bool media = false, ArrayOf<ISchemaFormatProvider> schemaFormats = default) =>
            new(session, m_telemetry, media, schemaFormats);

        /// <summary>Discovers native access for the explicitly selected root.</summary>
        public Task<EndpointRegistryClient> DiscoverAsync(
            ISession session, bool media = false, ArrayOf<ISchemaFormatProvider> schemaFormats = default,
            CancellationToken cancellationToken = default) =>
            EndpointRegistryClient.DiscoverAsync(session, m_telemetry, media, schemaFormats, cancellationToken);

        private readonly ITelemetryContext m_telemetry;
    }
}
