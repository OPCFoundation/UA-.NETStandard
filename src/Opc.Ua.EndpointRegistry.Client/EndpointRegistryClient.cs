/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.RegistryClients;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.EndpointRegistry.Client
{
    /// <summary>
    /// Session-bound native Endpoint Registry access. Selecting media never falls back to the generic root.
    /// The caller owns the session and all returned snapshot leases.
    /// </summary>
    /// <remarks>
    /// Service failures throw; domain failures remain in the generated result's StatusCode and Issues.
    /// No advertised transport is connected and no payload or JSON document is parsed.
    /// </remarks>
    public sealed class EndpointRegistryClient
    {
        /// <summary>
        /// Binds to well-known nodes. Use <see cref="DiscoverAsync"/> to verify the hosted root and discover TypedAccess.
        /// Providers are needed only when canonicalizing an inline schema.
        /// </summary>
        public EndpointRegistryClient(
            ISession session, ITelemetryContext telemetry, bool media = false,
            ArrayOf<ISchemaFormatProvider> schemaFormats = default)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            IsMedia = media;
            session.MessageContext.Factory.Builder
                .AddOpcUaXRegistry().AddOpcUaSchemaRegistry().AddOpcUaEndpointRegistry().Commit();
            RegistryNodeId = RegistryDiscovery.Resolve(session,
                media ? ObjectIds.MediaEndpointRegistry : ObjectIds.EndpointRegistry);
            TypedAccessNodeId = RegistryDiscovery.Resolve(session, media
                ? EndpointRegistryWellKnown.MediaEndpointRegistryTypedAccess
                : EndpointRegistryWellKnown.EndpointRegistryTypedAccess);
            m_mapper = EndpointRegistryNativeCatalog.CreateMapper(session.MessageContext, schemaFormats);
        }

        /// <summary>
        /// Gets whether the separately hosted Media root is selected.
        /// </summary>
        public bool IsMedia { get; }

        /// <summary>
        /// Gets the selected well-known root.
        /// </summary>
        public NodeId RegistryNodeId { get; }

        /// <summary>
        /// Gets the native access object.
        /// </summary>
        public NodeId TypedAccessNodeId { get; private set; }

        /// <summary>
        /// Gets a generated proxy for native access, including explicit typed diagnostics.
        /// </summary>
        public NativeRegistryAccessTypeClient TypedAccess => new(m_session, TypedAccessNodeId, m_telemetry);

        /// <summary>
        /// Verifies and discovers TypedAccess below the explicitly selected well-known root.
        /// </summary>
        public static async Task<EndpointRegistryClient> DiscoverAsync(
            ISession session, ITelemetryContext telemetry, bool media = false,
            ArrayOf<ISchemaFormatProvider> schemaFormats = default, CancellationToken cancellationToken = default)
        {
            var client = new EndpointRegistryClient(session, telemetry, media, schemaFormats);
            client.TypedAccessNodeId = await RegistryDiscovery.ChildAsync(session, client.RegistryNodeId,
                XRegistry.BrowseNames.TypedAccess, XRegistry.Namespaces.xRegistry, cancellationToken)
                .ConfigureAwait(false);
            return client;
        }

        /// <summary>
        /// Canonicalizes an application-built native record; no provider is required without inline schemas.
        /// </summary>
        public RegistryRecordDataType Canonicalize(RegistryRecordDataType record) => m_mapper.Canonicalize(record);

        /// <summary>
        /// Reads a native document. Check StatusCode and Issues before consuming Document.
        /// </summary>
        public ValueTask<RegistryReadResultDataType> ReadDocumentAsync(
            RegistryReadRequestDataType request, CancellationToken cancellationToken = default) =>
            TypedAccess.ReadDocumentAsync(request, cancellationToken);

        /// <summary>
        /// Writes a native document. Epoch conflicts and validation failures retain their typed diagnostics.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> WriteDocumentAsync(
            RegistryWriteRequestDataType request, CancellationToken cancellationToken = default) =>
            TypedAccess.WriteDocumentAsync(request, cancellationToken);

        /// <summary>
        /// Applies typed changes with optimistic epoch checking.
        /// </summary>
        public ValueTask<RegistryMutationResultDataType> ApplyChangesAsync(
            RegistryChangeRequestDataType request, CancellationToken cancellationToken = default) =>
            TypedAccess.ApplyChangesAsync(request, cancellationToken);

        /// <summary>
        /// Resolves metadata and local base Messages without applying configuration to PubSub.
        /// </summary>
        public ValueTask<NativeMessageResolutionResultDataType> ResolveMessageAsync(
            MessageResolutionRequestDataType request, CancellationToken cancellationToken = default) =>
            new EndpointRegistryTypeClient(m_session, RegistryNodeId, m_telemetry)
                .ResolveMessageAsync(request, cancellationToken);

        /// <summary>
        /// Opens a native snapshot using advertised limits. A nonzero maxBytes further bounds each response.
        /// </summary>
        public ValueTask<RegistrySnapshotClient> OpenSnapshotAsync(
            RegistrySnapshotOpenRequestDataType request, uint maxBytes = 0,
            CancellationToken cancellationToken = default) =>
            RegistryDiscovery.OpenAsync(m_session, TypedAccess, TypedAccessNodeId, request, maxBytes, cancellationToken);

        private readonly ISession m_session;
        private readonly ITelemetryContext m_telemetry;
        private readonly RegistryRecordMapper m_mapper;
    }
}
