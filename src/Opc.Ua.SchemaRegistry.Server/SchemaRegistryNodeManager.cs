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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.Server;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    /// <summary>
    /// Creates <see cref="SchemaRegistryNodeManager"/> instances for a server.
    /// </summary>
    public sealed class SchemaRegistryNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory for the supplied options.
        /// </summary>
        public SchemaRegistryNodeManagerFactory(SchemaRegistryServerOptions? options = null)
        {
            m_options = options ?? new SchemaRegistryServerOptions();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => SchemaRegistryNodeManager.NamespacesOf(m_options);

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
            IAsyncNodeManager nodeManager = new SchemaRegistryNodeManager(server, configuration, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly SchemaRegistryServerOptions m_options;
    }

    /// <summary>
    /// Hosts the optional well-known SchemaRegistry root with SchemaId resolution and native schema access.
    /// </summary>
    public sealed partial class SchemaRegistryNodeManager : XRegistryFastPathNodeManager
    {
        /// <summary>
        /// Creates the node manager.
        /// </summary>
        public SchemaRegistryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            SchemaRegistryServerOptions? options = null)
            : base(
                server,
                configuration,
                new XRegistryServerOptions { RegistryNamespaceUri = Namespaces.SchemaRegistry },
                [.. NamespacesOf(options ?? new SchemaRegistryServerOptions())])
        {
            m_options = options ?? new SchemaRegistryServerOptions();
            m_authorize = m_options.Authorize ?? RegistryAccessPolicy.Default;
            ArrayOf<ISchemaFormatProvider> providers = m_options.SchemaFormatProviders ??
                [new JsonSchemaFormatProvider(), new AvroSchemaFormatProvider(), new ArrowSchemaFormatProvider()];
            server.Factory.Builder
                .AddOpcUaXRegistry()
                .AddOpcUaSchemaRegistry()
                .Commit();
            var messageContext = new ServiceMessageContext(server.Telemetry, server.Factory)
            {
                NamespaceUris = server.NamespaceUris,
                ServerUris = server.ServerUris
            };
            m_formats = providers;
            m_documentContext = messageContext;
            m_documentMapper = new RegistryRecordMapper(RegistrySharedNativeCatalog.Catalog, messageContext);
            var origin = new RegistryEntityReferenceDataType
            {
                OriginUri = m_options.OriginUri ?? string.Empty,
                ApplicationUri = m_options.OriginUri is null ? configuration.ApplicationUri : string.Empty,
                RegistryNode = m_options.OriginUri is null
                    ? ObjectIds.SchemaRegistry : ExpandedNodeId.Null
            };
            m_ownedStore = m_options.Store is null ? new MemoryRegistryStateStore() : null;
            m_store = new SchemaRegistryStore(providers, m_options.Store ?? m_ownedStore!, messageContext, origin,
                m_options.BindSelector);
            m_store.DescribeVersion = m_options.DescribeVersion;
            m_store.VerifyCompatibility = m_options.VerifyCompatibility;
            m_store.BeforePublication = CheckPublication;
            m_store.ValidateReference = reference =>
            {
                string groupId = reference.Entity.Xid!.Split('/')[2];
                if (!m_options.NamespaceUris.TryGetValue(groupId, out string? uri) ||
                    !Uri.TryCreate(uri, UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound,
                        "The selected Schema Group has no configured NamespaceUri.");
                }
                if (XRegistryIdentifier.FromSourceIdentity(uri) != groupId)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The Schema GroupId differs from its NamespaceUri source identity.");
                }
                string[] parts = reference.Entity.Xid!.Substring(1).Split('/');
                string resourceXid = "/" + string.Join("/", parts, 0, 4);
                if (!m_options.SchemaNames.TryGetValue(resourceXid, out string? schemaName) ||
                    string.IsNullOrEmpty(schemaName))
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound,
                        "The schema subject source identity is not configured.");
                }
                string token = reference.Format?.ToUpperInvariant() switch
                {
                    "JSONSCHEMA/2020-12" => "jsonschema",
                    "AVRO/1.11" => "avro",
                    "APACHEARROW/1.0" => "arrow",
                    _ => throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The schema format has no configured source-identity token.")
                };
                if (XRegistryIdentifier.FromSourceIdentity(schemaName + "/" + token) != parts[3])
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The ResourceId differs from the schema subject and format source identity.");
                }
            };
            m_snapshots = new RegistryNativeSnapshots(messageContext);
        }

        /// <inheritdoc/>
        protected override bool HasDynamicContentLookup => m_options.Enabled;

        /// <inheritdoc/>
        protected override ServiceResult ReadDynamicContent(
            ISystemContext context, ByteString contentId, out ByteString document)
        {
            document = default;
            ServiceResult allowed = m_authorize(context, RegistryAccessKind.Read);
            if (ServiceResult.IsBad(allowed))
            {
                return allowed;
            }
            try
            {
                document = m_store.Resolve(contentId, entry => IsVisible(context, entry.Reference)).Document;
                return ServiceResult.Good;
            }
            catch (ServiceResultException error)
            {
                return error.StatusCode == StatusCodes.BadNotFound ? StatusCodes.BadNodeIdUnknown : error.Result;
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
            }
            nodes.AddOpcUaSchemaRegistry(context);
            if (!m_options.Enabled)
            {
                Remove(nodes, ObjectIds.SchemaRegistry, context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predefinedNode is SchemaRegistryState root)
            {
                m_root = root;
                BindRoot(context, root);
            }
            return new ValueTask<NodeState>(predefinedNode);
        }

        /// <summary>Gets the committed schema catalog owned by this node manager.</summary>
        public SchemaRegistryStore Store => m_store;

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            m_store.Activation = ProjectSchemasAsync;
            await m_store.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask SessionClosingAsync(
            OperationContext context, NodeId sessionId, bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            m_snapshots.ReleaseSession(sessionId);
            m_files.ReleaseSession(sessionId);
            await ReleaseUploadsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask SessionActivatedAsync(
            OperationContext context, NodeId sessionId, CancellationToken cancellationToken = default)
        {
            m_snapshots.ReleaseSession(sessionId);
            m_files.ReleaseSession(sessionId);
            await ReleaseUploadsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await base.SessionActivatedAsync(context, sessionId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_snapshotBinding?.Dispose();
                m_snapshots.Dispose();
                m_files.Dispose();
                m_store.Dispose();
                m_projectionGate.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            await ReleaseUploadsAsync(null, cancellationToken).ConfigureAwait(false);
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            if (m_ownedStore is not null)
            {
                await m_ownedStore.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal static ArrayOf<string> NamespacesOf(SchemaRegistryServerOptions options)
        {
            return options.LoadDependencyModels
                ? [Namespaces.SchemaRegistry, XRegistry.Namespaces.xRegistry]
                : [Namespaces.SchemaRegistry];
        }

        private void BindRoot(ISystemContext context, SchemaRegistryState root)
        {
            root.GetSchema!.OnCallAsync = (caller, _, _, schemaId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return new ValueTask<GetSchemaMethodStateResult>(
                        new GetSchemaMethodStateResult { ServiceResult = allowed });
                }
                SchemaRegistryStore.SchemaEntry entry = m_store.Resolve(schemaId,
                    candidate => IsVisible(caller, candidate.Reference));
                return new ValueTask<GetSchemaMethodStateResult>(new GetSchemaMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Document = entry.Document,
                    Format = entry.Format,
                    ContentType = entry.ContentType
                });
            };
            root.AddTypedSchemas(context).AddTypedAccess(context);
            BindDocuments(context, root.TypedAccess!);
            BindCreation(context, root);
            root.AddCapabilitiesInfo(context);
            XRegistryProjectionEngine.SetValue(root.CapabilitiesInfo, new RegistryCapabilitiesDataType
            {
                Flags = [],
                Mutable = ["entities"],
                Pagination = false,
                ShortSelf = false,
                SpecVersions = ["1.0-rc4"],
                StickyVersions = true,
                EnforceCompatibility = m_options.VerifyCompatibility is not null,
                Apis = [],
                Schemas = [.. m_formats.ToArray()!.Select(provider => provider.Format)]
            });
            XRegistryProjectionEngine.SetValue(root.RegistryId, m_options.RegistryId);
            NativeSchemaAccessState access = root.TypedSchemas!;
            access.AddReadSchema(context).AddWriteSchema(context);
            access.AddRegisterSchema(context).AddBeginSchemaUpload(context);
            access.RegisterSchema!.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
                if (ServiceResult.IsBad(allowed))
                {
                    return new RegisterSchemaMethodStateResult { ServiceResult = allowed };
                }
                return new RegisterSchemaMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Result = await m_store.RegisterAsync(request, ct).ConfigureAwait(false)
                };
            };
            access.BeginSchemaUpload!.OnCallAsync = (caller, _, _, registration, ct) =>
                BeginUploadAsync(caller, registration, ct);
            access.ReadSchema!.OnCallAsync = (caller, _, _, reference, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                return new ValueTask<ReadSchemaMethodStateResult>(ServiceResult.IsBad(allowed)
                    ? new ReadSchemaMethodStateResult { ServiceResult = allowed }
                    : new ReadSchemaMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Result = ReadAuthorized(caller, reference)
                    });
            };
            access.WriteSchema!.OnCallAsync = async (caller, _, _, request, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Write);
                return ServiceResult.IsBad(allowed)
                    ? new WriteSchemaMethodStateResult { ServiceResult = allowed }
                    : new WriteSchemaMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Result = await m_store.WriteAsync(request, ct).ConfigureAwait(false)
                    };
            };
            m_snapshotBinding = new RegistrySnapshotBinding(root.TypedAccess!, context, m_snapshots,
                RegistryAccessPolicy.AuthorizationView, (caller, request, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    ServiceResult allowed = m_authorize(caller, RegistryAccessKind.Read);
                    if (ServiceResult.IsBad(allowed))
                    {
                        throw new ServiceResultException(allowed);
                    }
                    if (request.DocumentKind != "schema")
                    {
                        return new ValueTask<RegistrySnapshotSource>(SharedDocument(caller, request.TargetXid,
                            request.DocumentKind, request.View));
                    }
                    if (request.View > 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                    }
                    (TypedSchemaReadResultDataType result, uint registryEpoch) = m_store.ReadXid(request.TargetXid!);
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode);
                    }
                    if (!IsVisible(caller, result.Document.Reference))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    var pinnedReference = (SchemaReferenceDataType)result.Document.Reference.Clone();
                    return new ValueTask<RegistrySnapshotSource>(new RegistrySnapshotSource(
                        result.Document, result.Document.Epoch, registryEpoch)
                    {
                        Reauthorize = () =>
                        {
                            ServiceResult access = m_authorize(caller, RegistryAccessKind.Read);
                            return ServiceResult.IsBad(access) ? access :
                                IsVisible(caller, pinnedReference)
                                    ? ServiceResult.Good : StatusCodes.BadUserAccessDenied;
                        }
                    });
                });
            XRegistryProjectionEngine.LinkMethodArguments(root, context);
        }

        private bool IsVisible(ISystemContext context, SchemaReferenceDataType reference) =>
            m_options.IsVisible is null || m_options.IsVisible(context, reference);

        private TypedSchemaReadResultDataType ReadAuthorized(
            ISystemContext context, SchemaReferenceDataType reference)
        {
            TypedSchemaReadResultDataType result = m_store.Read(reference);
            if (StatusCode.IsGood(result.StatusCode) && !IsVisible(context, result.Document.Reference))
            {
                return new TypedSchemaReadResultDataType
                {
                    StatusCode = StatusCodes.BadUserAccessDenied,
                    Document = null!,
                    Issues =
                    [
                        new RegistryDiagnosticDataType
                        {
                            StatusCode = StatusCodes.BadUserAccessDenied,
                            Code = "E_SCHEMA_ACCESS",
                            Path = [],
                            Detail = "The selected Version is not authorized."
                        }
                    ]
                };
            }
            return result;
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

        private readonly SchemaRegistryServerOptions m_options;
        private readonly Func<ISystemContext, RegistryAccessKind, ServiceResult> m_authorize;
        private readonly SchemaRegistryStore m_store;
        private readonly MemoryRegistryStateStore? m_ownedStore;
        private readonly RegistryNativeSnapshots m_snapshots;
        private readonly RegistryMetadataFileBinding m_files = new();
        private RegistrySnapshotBinding? m_snapshotBinding;
        private SchemaRegistryState? m_root;
    }
}
