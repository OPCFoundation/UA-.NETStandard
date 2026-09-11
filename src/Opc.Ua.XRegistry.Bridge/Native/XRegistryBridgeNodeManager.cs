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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Native projection and experimental transport over one injected endpoint.
    /// This manager owns no registry database and never invents operation replay.
    /// </summary>
    public sealed partial class XRegistryBridgeNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Creates a native endpoint projection for the server's lifecycle to own.
        /// </summary>
        public XRegistryBridgeNodeManager(
            IServerInternal server, ApplicationConfiguration configuration,
            IXRegistryEndpoint endpoint, XRegistryBridgeNativeOptions options)
            : base(server.ThrowIfNull(nameof(server)), configuration.ThrowIfNull(nameof(configuration)),
                server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>(),
                options.ThrowIfNull(nameof(options)).NamespaceUri, XRegistryWellKnown.XRegistryNamespaceUri,
                XRegistryBridgeNativeOptions.ExperimentalNamespaceUri)
        {
            endpoint.ThrowIfNull(nameof(endpoint));
            options.ThrowIfNull(nameof(options));
            options.Validate();
            m_endpoint = endpoint;
            m_options = options;
            m_codec = new XRegistryProtocolCodec(options.MaxMessageBytes);
            m_budget = new XRegistryFileBudget(options);
            InstanceNamespaceIndex = server.NamespaceUris.GetIndexOrAppend(options.NamespaceUri);
        }

        /// <summary>
        /// Gets the configured registry root in the server's current namespace table.
        /// </summary>
        public NodeId RegistryNodeId => new(m_options.RootIdentifier, InstanceNamespaceIndex);

        internal ushort InstanceNamespaceIndex { get; }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            context.ThrowIfNull(nameof(context));
            node.ThrowIfNull(nameof(node));
            if (node is BaseInstanceState { Parent: { } parent } &&
                parent.NodeId.NamespaceIndex == InstanceNamespaceIndex &&
                parent.NodeId.TryGetValue(out string parentPath))
            {
                string name = node.BrowseName.Name ??
                    throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                string uri = Server.NamespaceUris.GetString(node.BrowseName.NamespaceIndex) ??
                    throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                string component = name == "Bridge" && uri == XRegistryBridgeNativeOptions.ExperimentalNamespaceUri
                    ? "Bridge"
                    : Uri.EscapeDataString(uri) + ":" + Uri.EscapeDataString(name);
                return new NodeId(parentPath + "/" + component, InstanceNamespaceIndex);
            }
            return base.New(context, node);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            XRegistryNativeSnapshot snapshot = await XRegistryNativeSnapshot.LoadAsync(
                m_endpoint, m_options, cancellationToken).ConfigureAwait(false);
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            m_registry = SystemContext.CreateInstanceOfRegistryType(null!,
                new QualifiedName(m_options.RootIdentifier, InstanceNamespaceIndex));
            m_registry.NodeId = RegistryNodeId;
            m_registry.AddSpecVersion(SystemContext).AddXid(SystemContext).AddEpoch(SystemContext)
                .AddCreatedAt(SystemContext).AddModifiedAt(SystemContext).AddLabels(SystemContext)
                .AddCreateGroup(SystemContext).AddGetOrCreateGroup(SystemContext)
                .AddModel(SystemContext).AddCapabilities(SystemContext).AddCapabilitiesInfo(SystemContext);
            if (m_options.EnableExperimentalExtension)
            {
                ushort ns = Server.NamespaceUris.GetIndexOrAppend(
                    XRegistryBridgeNativeOptions.ExperimentalNamespaceUri);
                m_bridge = SystemContext.CreateInstanceOfRegistryBridgeType(
                    m_registry, new QualifiedName("Bridge", ns));
                m_bridge.NodeId = new NodeId(m_options.RootIdentifier + "/Bridge", InstanceNamespaceIndex);
                m_bridge.ProtocolVersion!.Value = 2;
                m_bridge.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                BindTransport(m_bridge);
                m_registry.AddChild(m_bridge);
            }
            m_strategy = new XRegistryBridgeProjectionStrategy(this, snapshot);
            m_projection = new XRegistryProjectionEngine(new XRegistryProjectionContext(
                SystemContext, Server.NamespaceUris, InstanceNamespaceIndex,
                (node, ct) => AddPredefinedNodeAsync(SystemContext, node, ct),
                DeleteProjectionNodeAsync,
                (context, operation) => CheckScope(context)), m_strategy, m_options.RootIdentifier);
            ConfigureRegistry(snapshot);
            SystemContext.AssignInstanceChildNodeIds(m_registry);
            XRegistryProjectionEngine.LinkMethodArguments(m_registry, SystemContext);
            m_registry.AddReference(ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);
            if (!externalReferences.TryGetValue(Ua.ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[Ua.ObjectIds.ObjectsFolder] = references = [];
            }
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, m_registry.NodeId));
            await AddPredefinedNodeAsync(SystemContext, m_registry, cancellationToken).ConfigureAwait(false);
            await m_projection.AttachAsync(m_registry, cancellationToken).ConfigureAwait(false);
            ConfigureRegistry(snapshot);
            RebindFiles();
        }

        /// <summary>
        /// Refreshes from a complete authoritative inventory. Failure never publishes
        /// a partial inventory or changes an upstream error into a successful stale read.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                XRegistryNativeSnapshot snapshot = await XRegistryNativeSnapshot.LoadAsync(
                    m_endpoint, m_options, cancellationToken).ConfigureAwait(false);
                if (m_strategy is null || m_projection is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                m_strategy.Snapshot = snapshot;
                ConfigureRegistry(snapshot);
                await m_projection.ReconcileProjectionAsync(cancellationToken).ConfigureAwait(false);
                RebindFiles();
            }
            finally
            {
                m_projectionGate.Release();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask ReadAsync(
            OperationContext context, double maxAge, ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values, IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            if ((nodesToRead.ToArray() ?? []).Any(read => read.NodeId.NamespaceIndex == InstanceNamespaceIndex))
            {
                _ = await AuthorizeAsync(SystemContext.Copy(context), false, cancellationToken).ConfigureAwait(false);
            }
            await base.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(
            OperationContext context, ArrayOf<WriteValue> nodesToWrite, IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            int count = (nodesToWrite.ToArray() ?? []).Count(write => !write.Processed &&
                write.NodeId.NamespaceIndex == InstanceNamespaceIndex);
            if (count != 0)
            {
                _ = await AuthorizeAsync(SystemContext.Copy(context), true, cancellationToken).ConfigureAwait(false);
            }
            if (count > 1)
            {
                for (int index = 0; index < nodesToWrite.Count; index++)
                {
                    if (!nodesToWrite[index].Processed &&
                        nodesToWrite[index].NodeId.NamespaceIndex == InstanceNamespaceIndex)
                    {
                        nodesToWrite[index].Processed = true;
                        errors[index] = ServiceResult.Create(StatusCodes.BadNotSupported,
                            "Use one experimental commit for a combined mutation, not sequential property Writes.");
                    }
                }
            }
            await base.WriteAsync(context, nodesToWrite, errors, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask SessionClosingAsync(
            OperationContext context, NodeId sessionId, bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            await m_transportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (NodeId id in m_transfers.Where(pair => pair.Value.SessionId == sessionId)
                    .Select(pair => pair.Key).ToArray())
                {
                    await RemoveTransferAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_transportGate.Release();
            }
            foreach (XRegistryNativeFile file in m_files.Values)
            {
                await file.CloseSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
            await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            await m_transportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (NodeId id in m_transfers.Keys.ToArray())
                {
                    await RemoveTransferAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_transportGate.Release();
            }
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = new NodeStateCollection();
            var registryType = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.RegistryType, Server.NamespaceUris);
            if (!Server.TypeTree.IsKnown(registryType))
            {
                nodes.AddOpcUaXRegistry(context);
            }
            var bridgeType = ExpandedNodeId.ToNodeId(Model.ObjectTypeIds.RegistryBridgeType, Server.NamespaceUris);
            if (m_options.EnableExperimentalExtension && !Server.TypeTree.IsKnown(bridgeType))
            {
                nodes.AddOpcUaXRegistryBridgeModel(context);
            }
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context, NodeHandle handle, IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            if (handle.NodeId.NamespaceIndex == InstanceNamespaceIndex)
            {
                _ = await AuthorizeAsync(context, false, cancellationToken).ConfigureAwait(false);
                if (context is ISessionSystemContext { SessionId.IsNull: false } &&
                    m_entities.ContainsKey(handle.NodeId))
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                    if (!m_entities.ContainsKey(handle.NodeId))
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                    }
                }
            }
            return await base.ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_projection?.Dispose();
                foreach (XRegistryNativeFile file in m_files.Values)
                {
                    file.Dispose();
                }
                foreach (Transfer transfer in m_transfers.Values)
                {
                    if (transfer.Prepared is { } prepared)
                    {
                        transfer.Prepared = null;
                        _ = ReleasePreparedOnDisposalAsync(prepared);
                    }
                    transfer.File?.Dispose();
                    m_budget.ReleaseBytes(transfer.ReservedBytes);
                }
                m_files.Clear();
                m_transfers.Clear();
                m_resourceMetadata.Clear();
                m_projectionGate.Dispose();
                m_transportGate.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BindTransport(RegistryBridgeState bridge)
        {
            bridge.InspectRegistry!.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                XRegistryEndpointDescription description = await InspectAsync(c, ct).ConfigureAwait(false);
                (NodeId file, uint handle) = await AddTransferAsync(
                    c, m_codec.EncodeDescription(description), false, ct).ConfigureAwait(false);
                output[0] = Variant.From(file);
                output[1] = Variant.From(handle);
                return ServiceResult.Good;
            };
            bridge.BeginRequest!.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                _ = await AuthorizeAsync(c, false, ct).ConfigureAwait(false);
                (NodeId file, uint handle) = await AddTransferAsync(c, ByteString.Empty, true, ct)
                    .ConfigureAwait(false);
                output[0] = Variant.From(file);
                output[1] = Variant.From(handle);
                return ServiceResult.Good;
            };
            bridge.CommitRequest!.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 3 ||
                    !i[0].TryGetValue(out NodeId id) ||
                    !i[1].TryGetValue(out string operationId) ||
                    !i[2].TryGetValue(out string digest))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                XRegistryRequest request = await ClaimRequestAsync(c, id, operationId, digest,
                    ct).ConfigureAwait(false);
                (NodeId file, uint handle) = await ExecuteTransferAsync(c, id, request, ct).ConfigureAwait(false);
                output[0] = Variant.From(file);
                output[1] = Variant.From(handle);
                return ServiceResult.Good;
            };
            BindPreparedTransport(bridge);
            bridge.AbortRequest!.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 1 || !i[0].TryGetValue(out NodeId id))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                _ = await AuthorizeAsync(c, false, ct).ConfigureAwait(false);
                await m_transportGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _ = OwnedTransfer(c, id);
                    await RemoveTransferAsync(id, ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                }
                finally
                {
                    m_transportGate.Release();
                }
            };
            bridge.GetOperationOutcome!.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 1 ||
                    !i[0].TryGetValue(out string operationId) ||
                    string.IsNullOrWhiteSpace(operationId))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                XRegistryEndpointDescription description = await InspectAsync(c, ct).ConfigureAwait(false);
                if (!description.SupportsOperationReplay ||
                    m_endpoint is not IXRegistryOperationJournalEndpoint journal)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported, "The endpoint has no durable outcome journal.");
                }
                XRegistryOperationOutcome outcome = await journal.GetOperationOutcomeAsync(
                    operationId, m_options.ContextFactory(c), ct).ConfigureAwait(false);
                output[0] = Variant.From((uint)outcome.State);
                output[1] = Variant.From(NodeId.Null);
                output[2] = Variant.From(0u);
                if (outcome.Response is not null)
                {
                    (NodeId file, uint handle) = await AddTransferAsync(
                        c, m_codec.EncodeResponse(outcome.Response), false, ct).ConfigureAwait(false);
                    output[1] = Variant.From(file);
                    output[2] = Variant.From(handle);
                }
                return ServiceResult.Good;
            };
        }

        private async ValueTask<XRegistryEndpointDescription> InspectAsync(
            ISystemContext context, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
            XRegistryEndpointDescription description = await m_endpoint.InspectAsync(
                caller, ct).ConfigureAwait(false);
            bool writable = caller.IsAuthenticated &&
                Qualified(description) &&
                await XRegistryNativeAuthorization.CanMutateAsync(m_options, context, ct).ConfigureAwait(false);
            return description with
            {
                Profile = writable ? "experimental-native-bridge-v1" : "native-bridge-readonly",
                SupportsAtomicMutations = writable,
                SupportsConditionalMutations = writable,
                SupportsWriteTouch = writable,
                SupportsPreparedMutations = writable &&
                    description.SupportsPreparedMutations &&
                    m_endpoint is IXRegistryPreparedEndpoint,
                SupportsOperationReplay = description.SupportsOperationReplay &&
                    m_endpoint is IXRegistryOperationJournalEndpoint
            };
        }

        private async ValueTask<XRegistryResponse> ExecuteAsync(
            ISystemContext context, XRegistryRequest request, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, request.IsMutation, ct).ConfigureAwait(false);
            request = request with { Context = caller };
            if (request.IsMutation)
            {
                if (!request.Context.IsAuthenticated)
                {
                    return new XRegistryResponse(403)
                    {
                        Error = new XRegistryError("forbidden",
                            "Native write-through requires an authenticated, explicitly mapped caller.")
                    };
                }
                XRegistryEndpointDescription description = await InspectAsync(context, ct).ConfigureAwait(false);
                if (!Qualified(description) ||
                    (!string.IsNullOrEmpty(request.OperationId) && !description.SupportsOperationReplay))
                {
                    return new XRegistryResponse(405)
                    {
                        Error = new XRegistryError("action_not_supported",
                            "The endpoint lacks the requested atomic/conditional/touch or durable replay guarantees."),
                        AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
                    };
                }
            }
            XRegistryResponse response = await m_endpoint.ExecuteAsync(request, ct).ConfigureAwait(false);
            if (request.IsMutation && response.IsSuccess)
            {
                await RefreshAsync(ct).ConfigureAwait(false);
            }
            return response;
        }

        private async ValueTask<(NodeId File, uint Handle)> AddTransferAsync(
            ISystemContext context, ByteString bytes, bool upload, CancellationToken ct)
        {
            await m_transportGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ExpireTransfersAsync(ct).ConfigureAwait(false);
                if (m_transfers.Count >= m_options.MaxOpenFiles)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                NodeId session = XRegistryNativeFile.SessionId(context);
                FileState node = SystemContext.CreateInstanceOfFileType(m_bridge!,
                    new QualifiedName(Guid.NewGuid().ToString("N"), InstanceNamespaceIndex));
                node.NodeId = new NodeId(m_options.RootIdentifier +
                    "/transfer/" +
                    Guid.NewGuid().ToString("N"), InstanceNamespaceIndex);
                node.Size!.Value = (ulong)bytes.Length;
                var transfer = new Transfer(node, session, m_options.ContextFactory(context),
                    upload, bytes, m_options.TimeProvider.GetUtcNow());
                m_budget.ReserveBytes(bytes.Length);
                transfer.ReservedBytes = bytes.Length;
                transfer.File = new XRegistryNativeFile(
                    node, m_options, m_budget, m_options.MaxMessageBytes,
                    (c, mode, token) =>
                    {
                        if (XRegistryNativeFile.SessionId(c) != transfer.SessionId ||
                            !XRegistryNativeFile.SameCaller(transfer.Caller, m_options.ContextFactory(c)) ||
                            transfer.Opened)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        transfer.Opened = true;
                        return new ValueTask<XRegistryFileSnapshot>(new XRegistryFileSnapshot(transfer.Bytes));
                    },
                    upload ? async (c, baseline, content, token) =>
                    {
                        await m_transportGate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            _ = OwnedTransfer(c, node.NodeId);
                            m_budget.ReserveBytes(content.Length);
                            m_budget.ReleaseBytes(transfer.ReservedBytes);
                            transfer.ReservedBytes = content.Length;
                            transfer.Bytes = content;
                            transfer.Sealed = true;
                            node.Size.Value = (ulong)content.Length;
                            return ServiceResult.Good;
                        }
                        finally
                        {
                            m_transportGate.Release();
                        }
                    }
                : null,
                    commitClean: true);
                m_transfers.Add(node.NodeId, transfer);
                SystemContext.AssignInstanceChildNodeIds(node);
                XRegistryProjectionEngine.LinkMethodArguments(node, SystemContext);
                await AddPredefinedNodeAsync(SystemContext, node, ct).ConfigureAwait(false);
                uint handle = await transfer.File.OpenAsync(context, upload ? (byte)6 : (byte)1, ct)
                    .ConfigureAwait(false);
                return (node.NodeId, handle);
            }
            finally
            {
                m_transportGate.Release();
            }
        }

        private Transfer OwnedTransfer(ISystemContext context, NodeId id)
        {
            if (!m_transfers.TryGetValue(id, out Transfer? transfer) ||
                transfer.SessionId != XRegistryNativeFile.SessionId(context) ||
                !XRegistryNativeFile.SameCaller(transfer.Caller, m_options.ContextFactory(context)))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The transfer is not owned.");
            }
            return transfer;
        }

        private async ValueTask ExpireTransfersAsync(CancellationToken ct)
        {
            DateTimeOffset now = m_options.TimeProvider.GetUtcNow();
            foreach (NodeId id in m_transfers.Where(pair => now - pair.Value.Created >= m_options.FileLifetime)
                .Select(pair => pair.Key).ToArray())
            {
                await RemoveTransferAsync(id, ct).ConfigureAwait(false);
            }
        }

        private async ValueTask RemoveTransferAsync(NodeId id, CancellationToken ct)
        {
            Transfer transfer = m_transfers[id];
            if (transfer.Prepared is { } prepared)
            {
                transfer.Prepared = null;
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            _ = await DeleteNodeAsync(SystemContext, id, ct).ConfigureAwait(false);
            m_transfers.Remove(id);
            transfer.File?.Dispose();
            m_budget.ReleaseBytes(transfer.ReservedBytes);
        }

        private async ValueTask DeleteProjectionNodeAsync(NodeId id, CancellationToken ct)
        {
            if (m_entityNodes.TryGetValue(id, out BaseObjectState? node))
            {
                RetireSubtree(node);
            }
            _ = await DeleteNodeAsync(SystemContext, id, ct).ConfigureAwait(false);
        }

        private void RetireSubtree(NodeState node)
        {
            var retired = new HashSet<NodeId>();
            CollectSubtreeIds(node, retired);
            foreach (KeyValuePair<NodeId, XRegistryNativeFile> entry in m_files
                .Where(pair => retired.Contains(pair.Value.NodeId)).ToArray())
            {
                if (m_files.TryRemove(entry.Key, out XRegistryNativeFile? file))
                {
                    file.Dispose();
                }
            }
            foreach (NodeId id in retired)
            {
                m_entities.TryRemove(id, out _);
                m_entityNodes.TryRemove(id, out _);
                m_resourceMetadata.TryRemove(id, out _);
            }
        }

        private void CollectSubtreeIds(NodeState node, HashSet<NodeId> ids)
        {
            ids.Add(node.NodeId);
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                CollectSubtreeIds(child, ids);
            }
        }

        private void RebindFiles()
        {
            foreach (XRegistryNativeFile file in m_files.Values)
            {
                file.Bind();
            }
            foreach (KeyValuePair<NodeId, BaseObjectState> pair in m_entityNodes)
            {
                if (!m_entities.TryGetValue(pair.Key, out string? path))
                {
                    continue;
                }
                pair.Value.EventNotifier = EventNotifiers.None;
                switch (pair.Value)
                {
                    case RegistryState registry:
                        BindLabels(registry.Labels, path);
                        break;
                    case GroupState group:
                        BindLabels(group.Labels, path);
                        break;
                    case ResourceState resource:
                        BindLabels(resource.Labels, path);
                        if (resource.Versions is not null)
                        {
                            BindLabels(resource.MetaLabels, path + "/meta");
                        }
                        break;
                }
            }
        }

        private ServiceResult CheckScope(ISystemContext context)
        {
            return XRegistryNativeFile.SameCaller(m_options.ProjectionContext, m_options.ContextFactory(context))
                ? ServiceResult.Good
                : ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                    "This address space is restricted to its configured projection identity scope.");
        }

        private async ValueTask<XRegistryCallContext> AuthorizeAsync(
            ISystemContext context, bool isMutation, CancellationToken ct)
        {
            await XRegistryNativeAuthorization.EnsureAsync(m_options, context, isMutation, ct).ConfigureAwait(false);
            XRegistryCallContext caller = m_options.ContextFactory(context);
            if (!XRegistryNativeFile.SameCaller(m_options.ProjectionContext, caller))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "This address space is restricted to its configured projection identity scope.");
            }
            return caller;
        }

        private static bool Qualified(XRegistryEndpointDescription description)
        {
            return description.SupportsAtomicMutations &&
                description.SupportsConditionalMutations &&
                description.SupportsWriteTouch;
        }

        private sealed class Transfer(
            FileState node, NodeId sessionId, XRegistryCallContext caller,
            bool upload, ByteString bytes, DateTimeOffset created)
        {
            public FileState Node { get; } = node;
            public NodeId SessionId { get; } = sessionId;
            public XRegistryCallContext Caller { get; } = caller;
            public bool Upload { get; } = upload;
            public ByteString Bytes { get; set; } = bytes;
            public DateTimeOffset Created { get; } = created;
            public XRegistryNativeFile? File { get; set; }
            public bool Sealed { get; set; }
            public bool Opened { get; set; }
            public bool Claimed { get; set; }
            public int ReservedBytes { get; set; }
            public IXRegistryPreparedOperation? Prepared { get; set; }
            public bool PreparedMutation { get; set; }
        }

        private readonly IXRegistryEndpoint m_endpoint;
        private readonly XRegistryBridgeNativeOptions m_options;
        private readonly XRegistryProtocolCodec m_codec;
        private readonly XRegistryFileBudget m_budget;
        private readonly SemaphoreSlim m_projectionGate = new(1, 1);
        private readonly SemaphoreSlim m_transportGate = new(1, 1);
        private readonly Dictionary<NodeId, Transfer> m_transfers = [];
        private readonly ConcurrentDictionary<NodeId, XRegistryNativeFile> m_files = new();
        private readonly ConcurrentDictionary<NodeId, string> m_entities = new();
        private RegistryState? m_registry;
        private RegistryBridgeState? m_bridge;
        private XRegistryBridgeProjectionStrategy? m_strategy;
        private XRegistryProjectionEngine? m_projection;
    }
}
