/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Server
{
    /// <summary>
    /// Creates <see cref="EndpointRegistryNodeManager"/> instances for a server.
    /// </summary>
    public sealed class EndpointRegistryNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory for the supplied options.
        /// </summary>
        public EndpointRegistryNodeManagerFactory(EndpointRegistryServerOptions? options = null)
        {
            m_options = options ?? new EndpointRegistryServerOptions();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => EndpointRegistryNodeManager.NamespacesOf(m_options);

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
            IAsyncNodeManager nodeManager = new EndpointRegistryNodeManager(server, configuration, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly EndpointRegistryServerOptions m_options;
    }

    /// <summary>
    /// Hosts the well-known EndpointRegistry and, when configured, MediaEndpointRegistry roots.
    /// Each root owns one committed native state, the shared TypedAccess Methods and a
    /// browseable projection of its Endpoints, Message Groups and metadata-only Messages.
    /// </summary>
    /// <remarks>
    /// Reading an advertised Endpoint never connects to it, and no Schema Registry root or
    /// PubSub runtime is required or created.
    /// </remarks>
    public sealed class EndpointRegistryNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Creates the node manager.
        /// </summary>
        public EndpointRegistryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            EndpointRegistryServerOptions? options = null)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<EndpointRegistryNodeManager>(),
                [.. NamespacesOf(options ?? new EndpointRegistryServerOptions())])
        {
            m_options = options ?? new EndpointRegistryServerOptions();
            if (m_options.Media is { } media)
            {
                foreach (string collection in media.Collections)
                {
                    if (collection != "endpoints")
                    {
                        throw new ArgumentException("A media catalog exposes only the endpoints collection.",
                            nameof(options));
                    }
                }
            }
            m_authorize = m_options.Authorize ?? RegistryAccessPolicy.Default;
            server.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .AddOpcUaEndpointRegistry()
                .Commit();
        }

        /// <summary>
        /// Gets the native host of the generic catalog, or <see langword="null"/> when it is not hosted.
        /// </summary>
        public RegistryNativeHost? Generic => Find(EndpointRegistryRoot.Generic)?.Host;

        /// <summary>
        /// Gets the native host of the media catalog, or <see langword="null"/> when it is not hosted.
        /// </summary>
        public RegistryNativeHost? Media => Find(EndpointRegistryRoot.Media)?.Host;

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            ushort index = NamespaceIndexes[0];
            foreach (Catalog catalog in m_catalogs)
            {
                var context = new XRegistryProjectionContext(
                    SystemContext,
                    Server.NamespaceUris,
                    index,
                    (node, ct) => AddPredefinedNodeAsync(SystemContext, node, ct),
                    async (nodeId, ct) => await DeleteNodeAsync(SystemContext, nodeId, ct).ConfigureAwait(false),
                    (caller, _) => m_authorize(caller, RegistryAccessKind.Write));
                var engine = new XRegistryProjectionEngine(context, catalog.Projection, catalog.Path);
                catalog.Engine = engine;
                catalog.Host.Activation = async (state, ct) =>
                {
                    catalog.Projection.Update(state);
                    PublishRoot(catalog, state);
                    await engine.ReconcileAsync(ct).ConfigureAwait(false);
                };
                await catalog.Host.StartAsync(cancellationToken).ConfigureAwait(false);
                await engine.AttachAsync(catalog.Root, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = new NodeStateCollection();
            if (m_options.LoadDependencyModels)
            {
                nodes.AddOpcUaXRegistry(context);
                nodes.AddOpcUaSchemaRegistry(context);
            }
            nodes.AddOpcUaEndpointRegistry(context);
            // A catalog does not imply a hosted Schema Registry.
            Remove(nodes, SchemaRegistry.ObjectIds.SchemaRegistry, context);
            if (m_options.Generic is null)
            {
                Remove(nodes, ObjectIds.EndpointRegistry, context);
            }
            if (m_options.Media is null)
            {
                Remove(nodes, ObjectIds.MediaEndpointRegistry, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is EndpointRegistryState root && root.TypedAccess is not null)
            {
                bool media = root is MediaEndpointRegistryState;
                EndpointRegistryCatalogOptions? options = media ? m_options.Media : m_options.Generic;
                if (options is not null)
                {
                    m_catalogs.Add(CreateCatalog(context, root, options, media));
                }
            }
            return new ValueTask<NodeState>(predefinedNode);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            Catalog[] catalogs = [.. m_catalogs];
            m_catalogs.Clear();
            foreach (Catalog catalog in catalogs)
            {
                if (catalog.Engine is { } engine)
                {
                    await engine.DetachAsync(cancellationToken).ConfigureAwait(false);
                }
                catalog.Dispose();
                if (catalog.OwnedStore is { } store)
                {
                    await store.DisposeAsync().ConfigureAwait(false);
                }
            }
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // DeleteAddressSpaceAsync releases the catalogs, including their owned stores; this is
                // the synchronous fallback for a manager disposed without an address-space teardown.
                foreach (Catalog catalog in m_catalogs)
                {
                    catalog.Dispose();
                }
                m_catalogs.Clear();
            }
            base.Dispose(disposing);
        }

        internal static ArrayOf<string> NamespacesOf(EndpointRegistryServerOptions options)
        {
            return options.LoadDependencyModels
                ? [Namespaces.EndpointRegistry, SchemaRegistry.Namespaces.SchemaRegistry, XRegistry.Namespaces.xRegistry]
                : [Namespaces.EndpointRegistry];
        }

        private Catalog CreateCatalog(
            ISystemContext context,
            EndpointRegistryState root,
            EndpointRegistryCatalogOptions options,
            bool media)
        {
            var messageContext = new ServiceMessageContext(Server.Telemetry, Server.Factory)
            {
                NamespaceUris = Server.NamespaceUris,
                ServerUris = Server.ServerUris
            };
            ArrayOf<ISchemaFormatProvider> providers = m_options.SchemaFormatProviders ??
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider()];
            string path = media ? BrowseNames.MediaEndpointRegistry : BrowseNames.EndpointRegistry;
            ushort index = NamespaceIndexes[0];
            IRegistryStateStore? ownedStore = options.Store is null ? new MemoryRegistryStateStore() : null;
            var catalog = new Catalog(media ? EndpointRegistryRoot.Media : EndpointRegistryRoot.Generic, root, path,
                ownedStore);
            try
            {
                catalog.Host = new RegistryNativeHost(new RegistryNativeHostOptions
                {
                    Store = options.Store ?? ownedStore,
                    Mapper = EndpointRegistryNativeCatalog.CreateMapper(messageContext, providers),
                    Collections = options.Collections,
                    RegistryRecordType = nameof(EndpointRegistryDocumentDataType),
                    GroupRecordType = collection => collection == "endpoints"
                        ? nameof(EndpointDataType)
                        : nameof(MessageGroupDataType),
                    ResourceRecordType = nameof(MessageDefinitionDataType),
                    InitialDocument = InitialDocument(options.RegistryId),
                    Validate = options.Validate,
                    TargetNodeId = xid => new ExpandedNodeId(new NodeId(path + xid, index)),
                    MessageContext = messageContext,
                    Telemetry = Server.Telemetry
                });
                catalog.Snapshots = new RegistryNativeSnapshots(messageContext, m_options.SnapshotLimits);
                catalog.Binding = new RegistryNativeAccessBinding(root.TypedAccess!, context, catalog.Host,
                    catalog.Snapshots, m_authorize);
            }
            catch
            {
                catalog.Dispose();
                throw;
            }
            RegistryNativeHost host = catalog.Host;
            XRegistryProjectionEngine.LinkMethodArguments(root.TypedAccess, context);
            root.AddLabels(context);
            root.AddCommitMetadata(context);
            root.CommitMetadata!.OnCallAsync = async (caller, _, _, targetXid, patch, expectedEpoch, ct) =>
            {
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
                if (ServiceResult.IsBad(allowed))
                {
                    return new CommitMetadataMethodStateResult { ServiceResult = allowed };
                }
                RegistryMutationResultDataType result = await host.PatchAsync(targetXid, patch, expectedEpoch, ct)
                    .ConfigureAwait(false);
                return new CommitMetadataMethodStateResult
                {
                    ServiceResult = result.Issues.Count == 0
                        ? new ServiceResult(result.StatusCode)
                        : new ServiceResult(result.StatusCode, new LocalizedText(result.Issues[0].Detail)),
                    TargetNodeId = ExpandedNodeId.ToNodeId(result.Target, Server.NamespaceUris),
                    Epoch = result.Epoch
                };
            };
            XRegistryProjectionEngine.LinkMethodArguments(root, context);
            XRegistryProjectionEngine.SetValue(root.RegistryId, options.RegistryId);
            XRegistryProjectionEngine.SetValue(root.ProfileUris, options.ProfileUris);
            root.EventNotifier = EventNotifiers.SubscribeToEvents;
            catalog.Projection = new EndpointRegistryProjection(host, context, index, media, m_authorize);
            return catalog;
        }

        private void PublishRoot(Catalog catalog, RegistryCommittedState state)
        {
            (RegistryRecordDataType record, uint epoch) = catalog.Host.ReadRecord(state, "/");
            XRegistryProjectionEngine.SetValue(catalog.Root.Snapshot, new RegistryReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                Epoch = epoch,
                Document = new ExtensionObject(record),
                ContinuationPoint = ByteString.Empty,
                Issues = []
            });
            if (record is EndpointRegistryDocumentDataType document && document.RegistryId is { } registryId)
            {
                XRegistryProjectionEngine.SetValue(catalog.Root.RegistryId, registryId);
            }
            catalog.Root.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private static RegistryObjectValueDataType InitialDocument(string registryId)
        {
            if (string.IsNullOrEmpty(registryId))
            {
                throw new ArgumentException("A registry identifier is required.", nameof(registryId));
            }
            return new RegistryObjectValueDataType
            {
                Kind = 5,
                Members =
                [
                    new RegistryMemberDataType
                    {
                        Name = "specversion",
                        Value = new RegistryStringValueDataType { Kind = 2, Value = "1.0-rc4" }
                    },
                    new RegistryMemberDataType
                    {
                        Name = "registryid",
                        Value = new RegistryStringValueDataType { Kind = 2, Value = registryId }
                    },
                    new RegistryMemberDataType
                    {
                        Name = "epoch",
                        Value = new RegistryNumberValueDataType
                        {
                            Kind = 3,
                            Coefficient = ByteString.From([1]),
                            IsInteger = true
                        }
                    }
                ]
            };
        }

        private static void Remove(NodeStateCollection nodes, ExpandedNodeId id, ISystemContext context)
        {
            NodeId nodeId = ExpandedNodeId.ToNodeId(id, context.NamespaceUris);
            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                if (nodes[index].NodeId == nodeId)
                {
                    nodes.RemoveAt(index);
                }
            }
        }

        private Catalog? Find(EndpointRegistryRoot kind)
        {
            foreach (Catalog catalog in m_catalogs)
            {
                if (catalog.Kind == kind)
                {
                    return catalog;
                }
            }
            return null;
        }

        private sealed class Catalog(
            EndpointRegistryRoot kind,
            EndpointRegistryState root,
            string path,
            IRegistryStateStore? ownedStore) : IDisposable
        {
            public EndpointRegistryRoot Kind { get; } = kind;

            public EndpointRegistryState Root { get; } = root;

            public string Path { get; } = path;

            public IRegistryStateStore? OwnedStore { get; } = ownedStore;

            public RegistryNativeHost Host { get; set; } = null!;

            public EndpointRegistryProjection Projection { get; set; } = null!;

            public RegistryNativeSnapshots Snapshots { get; set; } = null!;

            public RegistryNativeAccessBinding Binding { get; set; } = null!;

            public XRegistryProjectionEngine? Engine { get; set; }

            public void Dispose()
            {
                Binding?.Dispose();
                Snapshots?.Dispose();
                Engine?.Dispose();
                Host?.Dispose();
            }
        }

        private readonly EndpointRegistryServerOptions m_options;
        private readonly Func<ISystemContext, RegistryAccessKind, ServiceResult> m_authorize;
        private readonly List<Catalog> m_catalogs = [];
    }
}
