using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.SchemaRegistry.Client
{
    /// <summary>Creates exact native clients without taking ownership of sessions.</summary>
    public sealed class SchemaRegistryClientFactory
    {
        /// <summary>Creates the factory with the host's telemetry context.</summary>
        public SchemaRegistryClientFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>Creates a client which discovers TypedSchemas on first access.</summary>
        public SchemaRegistryClient Create(ISession session) => new(session, m_telemetry);

        /// <summary>Discovers TypedSchemas below the optional well-known root.</summary>
        public Task<SchemaRegistryClient> DiscoverAsync(
            ISession session, CancellationToken cancellationToken = default) =>
            SchemaRegistryClient.DiscoverAsync(session, m_telemetry, cancellationToken);

        private readonly ITelemetryContext m_telemetry;
    }
}
