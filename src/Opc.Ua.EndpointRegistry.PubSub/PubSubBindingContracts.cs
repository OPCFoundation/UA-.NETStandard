/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Server;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    /// <summary>An explicit schema selection registered for exactly this DataSet metadata.</summary>
    public sealed class PubSubSchemaAssociation
    {
        /// <summary>Creates an association and takes private copies of both native values.</summary>
        public PubSubSchemaAssociation(DataSetMetaDataType metadata, SchemaReferenceDataType schema)
        {
            m_metadata = (DataSetMetaDataType)(metadata ?? throw new ArgumentNullException(nameof(metadata))).Clone();
            m_schema = (SchemaReferenceDataType)(schema ?? throw new ArgumentNullException(nameof(schema))).Clone();
        }

        /// <summary>Returns the metadata whose schema was registered, including both configuration versions.</summary>
        public DataSetMetaDataType MetaData => (DataSetMetaDataType)m_metadata.Clone();

        /// <summary>Returns the exact native schema association; no schema or NodeId is inferred from a name.</summary>
        public SchemaReferenceDataType Schema => (SchemaReferenceDataType)m_schema.Clone();

        private readonly DataSetMetaDataType m_metadata;
        private readonly SchemaReferenceDataType m_schema;
    }

    /// <summary>Resolves explicit existing schema associations without registering or configuring anything.</summary>
    public interface IPubSubBindingSchemaProvider
    {
        /// <summary>Returns the registered schema for this identity and metadata, or null.</summary>
        PubSubSchemaAssociation? FindSchema(DataSetMetaDataKey key, DataSetMetaDataType metadata);
    }

    /// <summary>Server-owned configuration of the optional PubSub Binding facet.</summary>
    public sealed class EndpointRegistryPubSubBindingOptions
    {
        /// <summary>Gets or sets whether local valid publishing queues are surfaced. Defaults to true.</summary>
        public bool SurfaceLocalPublishers { get; set; } = true;

        /// <summary>
        /// Gets or sets the reserved collection identifier prefix for local entries.
        /// It must be stable across restarts and distinct from other provider prefixes.
        /// </summary>
        public string LocalIdentifierPrefix { get; set; } = "pubsub-local-";

        /// <summary>Gets or sets the reserved prefix for authenticated remote entries.</summary>
        public string RemoteIdentifierPrefix { get; set; } = "pubsub-remote-";

        /// <summary>Gets or sets the explicit schema association provider.</summary>
        public IPubSubBindingSchemaProvider? Schemas { get; set; }

        /// <summary>
        /// Gets or sets the server's credential-configuration check for Endpoints declaring
        /// authorization alternatives. Such Endpoints do not bind without this check.
        /// </summary>
        public Func<PubSubAddressSpaceTarget, RegistryObjectValueDataType, bool>? AuthorizeBinding { get; set; }

        /// <summary>
        /// Gets the configured remote Publisher bindings. Observations never infer facts from
        /// the registry's own broker connection. Bindings must have unique identifiers.
        /// </summary>
        public ArrayOf<RemotePubSubPublisherBinding> RemotePublishers { get; set; }

        /// <summary>Gets or sets the clock used for MQTT 5 expiry checks.</summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }

    /// <summary>
    /// Independently authorized remote Publisher facts, supplied by server configuration.
    /// An identity can be a uniquely authorized broker client or a trusted SecurityGroup.
    /// </summary>
    public sealed class RemotePubSubPublisherBinding
    {
        /// <summary>Gets or sets the stable binding identifier.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the PublisherId trusted by this binding.</summary>
        public Variant PublisherId { get; set; }

        /// <summary>Gets or sets the authorized discovery Topic prefix, without a trailing slash.</summary>
        public string TopicPrefix { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact authorized retained connection Topic.</summary>
        public string ConnectionTopic { get; set; } = string.Empty;

        /// <summary>Gets or sets the authenticated identity authorized to announce this Publisher.</summary>
        public string AuthenticatedIdentity { get; set; } = string.Empty;

        /// <summary>Gets or sets the independently authorized path-free broker URL.</summary>
        public string BrokerUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the explicitly established Publisher MQTT version, or null if unknown.</summary>
        public string? MqttVersion { get; set; }
    }

    /// <summary>
    /// A discovery observation after authentication by the host. The host supplies native
    /// decoded values; this module does not subscribe to a broker or verify wire signatures.
    /// </summary>
    public sealed class RemotePubSubObservation
    {
        /// <summary>Gets or sets the configured binding identifier.</summary>
        public string BindingId { get; set; } = string.Empty;

        /// <summary>Gets or sets the authenticated sender identity established by the host.</summary>
        public string AuthenticatedIdentity { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact retained discovery Topic.</summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>Gets or sets the discovery Timestamp used to reject older messages.</summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>Gets or sets the discovery MessageId used to reject duplicates.</summary>
        public string MessageId { get; set; } = string.Empty;

        /// <summary>Gets or sets the MQTT 5 message expiration instant, when present.</summary>
        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>Gets or sets whether the Publisher cleared the retained message.</summary>
        public bool RetainedCleared { get; set; }

        /// <summary>Gets or sets the native connection announcement, with Address and properties omitted.</summary>
        public PubSubConnectionDataType? Connection { get; set; }

        /// <summary>
        /// Gets or sets the independently authenticated native DataSet metadata announcements.
        /// A connection observation alone never invents missing DataSet metadata.
        /// </summary>
        public ArrayOf<PubSubDataSetMetaDataDiscoveryResult> MetaData { get; set; }
    }

    /// <summary>Admission result for an authenticated observation, including incompleteness and replay diagnostics.</summary>
    /// <param name="Accepted">Whether the observation advanced its authenticated Topic watermark.</param>
    /// <param name="Issues">Reasons an observation was rejected or remains incomplete.</param>
    public sealed record RemotePubSubObservationResult(bool Accepted, ArrayOf<RegistryDiagnosticDataType> Issues);
}
