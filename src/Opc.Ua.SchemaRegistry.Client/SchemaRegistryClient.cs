using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.RegistryClients;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.SchemaRegistry.Client
{
    /// <summary>
    /// Exact native Schema Registry access. Service errors throw and typed failures retain StatusCode and Issues.
    /// The caller owns the session and returned leases; schema content and downloaded octets are never parsed.
    /// </summary>
    public sealed class SchemaRegistryClient
    {
        /// <summary>Binds the well-known root and registers the generated model codecs.</summary>
        public SchemaRegistryClient(ISession session, ITelemetryContext telemetry)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().Commit();
            RegistryNodeId = RegistryDiscovery.Resolve(session, ObjectIds.SchemaRegistry);
        }

        /// <summary>Gets the well-known root.</summary>
        public NodeId RegistryNodeId { get; }

        /// <summary>Discovers TypedSchemas below the well-known root without assuming a namespace index.</summary>
        public static async Task<SchemaRegistryClient> DiscoverAsync(
            ISession session, ITelemetryContext telemetry, CancellationToken cancellationToken = default)
        {
            var client = new SchemaRegistryClient(session, telemetry);
            await client.DiscoverMembersAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }

        /// <summary>Reads the exact selected schema. Check StatusCode and Issues before consuming Document.</summary>
        public async Task<TypedSchemaReadResultDataType> ReadSchemaAsync(
            SchemaReferenceDataType reference, CancellationToken cancellationToken = default)
        {
            await DiscoverMembersAsync(cancellationToken).ConfigureAwait(false);
            return await new NativeSchemaAccessTypeClient(m_session, m_schemas, m_telemetry)
                .ReadSchemaAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes native content using a configured source identity; no dynamic registration is inferred.</summary>
        public async Task<TypedSchemaReadResultDataType> WriteSchemaAsync(
            TypedSchemaWriteRequestDataType request, CancellationToken cancellationToken = default)
        {
            await DiscoverMembersAsync(cancellationToken).ConfigureAwait(false);
            return await new NativeSchemaAccessTypeClient(m_session, m_schemas, m_telemetry)
                .WriteSchemaAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets opaque schema octets by fingerprint. Ambiguous fingerprints fail explicitly; use exact ReadSchema instead.
        /// </summary>
        public ValueTask<(ByteString Document, string Format, string ContentType)> GetSchemaAsync(
            ByteString schemaId, CancellationToken cancellationToken = default) =>
            new SchemaRegistryTypeClient(m_session, RegistryNodeId, m_telemetry)
                .GetSchemaAsync(schemaId, cancellationToken);

        /// <summary>Opens an exact schema Version snapshot with advertised native limits.</summary>
        public async ValueTask<RegistrySnapshotClient> OpenSnapshotAsync(
            RegistrySnapshotOpenRequestDataType request, uint maxBytes = 0,
            CancellationToken cancellationToken = default)
        {
            await DiscoverMembersAsync(cancellationToken).ConfigureAwait(false);
            NodeId access = await RegistryDiscovery.ChildAsync(m_session, RegistryNodeId,
                XRegistry.BrowseNames.TypedAccess, XRegistry.Namespaces.xRegistry, cancellationToken)
                .ConfigureAwait(false);
            return await RegistryDiscovery.OpenAsync(m_session,
                new NativeRegistryAccessTypeClient(m_session, access, m_telemetry), access, request, maxBytes,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task DiscoverMembersAsync(CancellationToken cancellationToken)
        {
            if (m_schemas.IsNull)
            {
                m_schemas = await RegistryDiscovery.ChildAsync(m_session, RegistryNodeId,
                    BrowseNames.TypedSchemas, Namespaces.SchemaRegistry, cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly ISession m_session;
        private readonly ITelemetryContext m_telemetry;
        private NodeId m_schemas;
    }
}
