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
    public sealed partial class XRegistryBridgeNodeManager :
        AsyncCustomNodeManager, IBrowseAsyncNodeManager, ITranslateBrowsePathAsyncNodeManager
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

        /// <summary>
        /// Gets diagnostic reservations for currently retained native transfers and handles.
        /// </summary>
        public XRegistryBridgeResourceUsage ResourceUsage => m_budget.Usage;

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
                string name = node.BrowseName.Name
                    ?? throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                string uri = Server.NamespaceUris.GetString(node.BrowseName.NamespaceIndex)
                    ?? throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
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
                (context, operation) => CheckScope(context),
                new XRegistryServerOptions
                {
                    EventsEnabled = m_options.EnableChangeEvents,
                    EventSourceUrl = (m_options.EventSource
                        ?? snapshot.Description.PublicRoot
                            ?? new Uri(m_options.NamespaceUri)).AbsoluteUri,
                    TimeProvider = m_options.TimeProvider
                }), m_strategy, m_options.RootIdentifier);
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
            await ReconcileMappedPropertiesAsync(snapshot, cancellationToken).ConfigureAwait(false);
            RebindFiles();
            if (m_options.EnableChangeEvents)
            {
                AddRootNotifier(m_registry);
            }
        }

        /// <summary>
        /// Refreshes from a complete authoritative inventory. Failure never publishes
        /// a partial inventory or changes an upstream error into a successful stale read.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            XRegistryPreparedEventBatch? events = null;
            await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                XRegistryNativeSnapshot snapshot = await XRegistryNativeSnapshot.LoadAsync(
                    m_endpoint, m_options, cancellationToken).ConfigureAwait(false);
                if (m_strategy is null || m_projection is null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                if (!m_projectionDegraded && snapshot.EquivalentTo(m_strategy.Snapshot))
                {
                    return;
                }
                m_projectionPending = true;
                Interlocked.Increment(ref m_projectionRevision);
                using LocalAddressSpaceNotificationBatch notifications =
                    ((ILocalAddressSpaceNotifications)((ILocalAddressSpaceSource)this).CreateLocalAddressSpace())
                        .BeginNotificationBatch();
                try
                {
                    XRegistryNativeSnapshot previous = m_strategy.Snapshot;
                    await ApplySnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    events = PrepareNativeEvents(previous, snapshot);
                    PublishAddressSpaceNotifications(notifications);
                    m_projectionDegraded = false;
                }
                catch
                {
                    m_projectionDegraded = true;
                    throw;
                }
                finally
                {
                    if (!m_projectionDegraded)
                    {
                        RetireUnpublishedFiles();
                    }
                    Interlocked.Increment(ref m_projectionRevision);
                    m_projectionPending = false;
                }
            }
            finally
            {
                m_projectionGate.Release();
            }
            if (events is not null)
            {
                await PublishCommittedEventsAsync(events).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask ReadAsync(
            OperationContext context, double maxAge, ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values, IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            bool projected = (nodesToRead.ToArray() ?? []).Any(read => IsProjectionNode(read.NodeId));
            if (projected)
            {
                EnsureProjectionAvailable();
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            if ((nodesToRead.ToArray() ?? []).Any(read => read.NodeId.NamespaceIndex == InstanceNamespaceIndex))
            {
                _ = await AuthorizeAsync(SystemContext.Copy(context), false, cancellationToken).ConfigureAwait(false);
            }
            long revision = Volatile.Read(ref m_projectionRevision);
            await base.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken)
                .ConfigureAwait(false);
            if (projected && (m_projectionPending || revision != Volatile.Read(ref m_projectionRevision)))
            {
                throw new ServiceResultException(StatusCodes.BadWaitingForInitialData,
                    "The native projection changed during the read.");
            }
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
            Transfer[] transfers = DetachTransfers(transfer => transfer.SessionId == sessionId);
            XRegistryNativeFile[] files = [.. m_files.Values];
            await AwaitTransferCleanupAsync(async () =>
            {
                try
                {
                    await Task.WhenAll(new[] { CompleteTransfersRemovalAsync(transfers) }.Concat(
                        files.Select(file => file.CloseSessionAsync(sessionId, CancellationToken.None).AsTask())))
                        .ConfigureAwait(false);
                }
                finally
                {
                    await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            lock (m_transportGate)
            {
                m_transportClosed = true;
            }
            Transfer[] transfers = DetachTransfers(static _ => true);
            await AwaitTransferCleanupAsync(async () =>
            {
                try
                {
                    await CompleteTransfersRemovalAsync(transfers).ConfigureAwait(false);
                }
                finally
                {
                    await base.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
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
                if (IsProjectionNode(handle.NodeId))
                {
                    EnsureProjectionAvailable();
                    if (m_projectionDegraded ||
                        !PredefinedNodes.TryGetValue(handle.NodeId, out NodeState? current))
                    {
                        throw new ServiceResultException(m_projectionDegraded
                            ? StatusCodes.BadWaitingForInitialData : StatusCodes.BadNodeIdUnknown);
                    }
                    handle.Node = current;
                    handle.Validated = true;
                }
            }
            return await base.ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Transfer[] transfers;
                lock (m_transportGate)
                {
                    if (m_transportDisposed)
                    {
                        return;
                    }
                    m_transportDisposed = true;
                    m_transportClosed = true;
                    transfers = [.. m_transfers.Values];
                    m_transfers.Clear();
                }
                m_projection?.Dispose();
                foreach (XRegistryNativeFile file in m_files.Values)
                {
                    file.Dispose();
                }
                foreach (Transfer transfer in transfers)
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
                m_resourceMetadata.Clear();
                m_projectionGate.Dispose();
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
                await RemoveTransferAsync(id, ct, c).ConfigureAwait(false);
                return ServiceResult.Good;
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
                SupportsPreparedSnapshots = writable &&
                    description.SupportsPreparedSnapshots &&
                    m_endpoint is IXRegistryPreparedEndpoint,
                SupportsOperationReplay = description.SupportsOperationReplay &&
                    m_endpoint is IXRegistryOperationJournalEndpoint
            };
        }

        private async ValueTask<XRegistryResponse> ExecuteCoreAsync(
            ISystemContext context, XRegistryRequest request, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, request.IsMutation, ct).ConfigureAwait(false);
            request = request with { Context = caller };
            if (request.ExpectedGeneration is not null &&
                !(await InspectAsync(context, ct).ConfigureAwait(false)).SupportsGenerationGuards)
            {
                return new XRegistryResponse(405)
                {
                    Error = new XRegistryError("action_not_supported",
                        "The endpoint does not provide registry generation guards."),
                    AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
                };
            }
            if (request.ExpectedVersionIncarnation is not null &&
                !(await InspectAsync(context, ct).ConfigureAwait(false)).SupportsVersionIncarnationGuards)
            {
                return new XRegistryResponse(405)
                {
                    Error = new XRegistryError("action_not_supported",
                        "The endpoint does not provide Version incarnation guards."),
                    AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
                };
            }
            if (request.IsMutation)
            {
                EnsureProjectionAvailable();
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
                if (m_endpoint is IXRegistryPreparedEndpoint prepared && description.SupportsPreparedMutations)
                {
                    IXRegistryPreparedOperation operation = await PrepareEndpointAsync(prepared, request, ct)
                        .ConfigureAwait(false);
                    operation = await PrepareProjectionAsync(operation, ct).ConfigureAwait(false);
                    try
                    {
                        return await CommitAndRefreshAsync(operation, true, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        await ReleasePreparedAfterOperationAsync(operation).ConfigureAwait(false);
                    }
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
            await ExpireTransfersAsync(ct).ConfigureAwait(false);
            Transfer transfer;
            lock (m_transportGate)
            {
                ct.ThrowIfCancellationRequested();
                if (m_transportClosed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The native transport is closing.");
                }
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
                transfer = new Transfer(node, session, m_options.ContextFactory(context),
                    upload, bytes, m_options.TimeProvider.GetUtcNow());
                SystemContext.AssignInstanceChildNodeIds(node);
                XRegistryProjectionEngine.LinkMethodArguments(node, SystemContext);
                m_budget.ReserveBytes(bytes.Length);
                transfer.ReservedBytes = bytes.Length;
                bool registered = false;
                try
                {
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
                    upload ? (c, baseline, content, token) =>
                    {
                        lock (m_transportGate)
                        {
                            token.ThrowIfCancellationRequested();
                            _ = OwnedTransfer(c, node.NodeId);
                            m_budget.ReserveBytes(content.Length);
                            m_budget.ReleaseBytes(transfer.ReservedBytes);
                            transfer.ReservedBytes = content.Length;
                            transfer.Bytes = content;
                            transfer.Sealed = true;
                            node.Size.Value = (ulong)content.Length;
                            return new ValueTask<ServiceResult>(ServiceResult.Good);
                        }
                    }
                    : null,
                        commitClean: true, telemetry: Server.Telemetry);
                    m_transfers.Add(node.NodeId, transfer);
                    registered = true;
                }
                finally
                {
                    if (!registered)
                    {
                        transfer.File?.Dispose();
                        m_budget.ReleaseBytes(transfer.ReservedBytes);
                    }
                }
            }
            bool completed = false;
            try
            {
                await AddPredefinedNodeAsync(SystemContext, transfer.Node, ct).ConfigureAwait(false);
                uint handle = await transfer.File!.OpenAsync(context, upload ? (byte)6 : (byte)1, ct)
                    .ConfigureAwait(false);
                lock (m_transportGate)
                {
                    ct.ThrowIfCancellationRequested();
                    _ = OwnedTransfer(context, transfer.Node.NodeId);
                    completed = true;
                }
                return (transfer.Node.NodeId, handle);
            }
            finally
            {
                transfer.Initialized.TrySetResult(true);
                if (!completed)
                {
                    await RemoveTransferAsync(transfer.Node.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private Transfer OwnedTransfer(ISystemContext context, NodeId id)
        {
            if (!m_transfers.TryGetValue(id, out Transfer? transfer) ||
                transfer.SessionId != XRegistryNativeFile.SessionId(context) ||
                !XRegistryNativeFile.SameCaller(transfer.Caller, m_options.ContextFactory(context)) ||
                m_options.TimeProvider.GetUtcNow() - transfer.Created >= m_options.FileLifetime)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The transfer is not owned.");
            }
            return transfer;
        }

        private async ValueTask ExpireTransfersAsync(CancellationToken ct)
        {
            DateTimeOffset now = m_options.TimeProvider.GetUtcNow();
            await RemoveTransfersAsync(transfer => now - transfer.Created >= m_options.FileLifetime, ct)
                .ConfigureAwait(false);
        }

        private async ValueTask RemoveTransferAsync(
            NodeId id, CancellationToken ct, ISystemContext? owner = null)
        {
            var transfers = new List<Transfer>();
            lock (m_transportGate)
            {
                ct.ThrowIfCancellationRequested();
                if (owner is not null)
                {
                    _ = OwnedTransfer(owner, id);
                }
                DetachTransfer(id, transfers);
            }
            if (transfers.Count != 0)
            {
                await AwaitTransferCleanupAsync(
                    () => CompleteTransfersRemovalAsync([.. transfers]), ct).ConfigureAwait(false);
            }
        }

        private async ValueTask RemoveTransfersAsync(Func<Transfer, bool> predicate, CancellationToken ct)
        {
            Transfer[] transfers = DetachTransfers(predicate);
            if (transfers.Length != 0)
            {
                await AwaitTransferCleanupAsync(
                    () => CompleteTransfersRemovalAsync(transfers), ct).ConfigureAwait(false);
            }
        }

        private Transfer[] DetachTransfers(Func<Transfer, bool> predicate)
        {
            lock (m_transportGate)
            {
                NodeId[] ids = [.. m_transfers.Values.Where(predicate).Select(transfer => transfer.Node.NodeId)];
                var transfers = new List<Transfer>();
                foreach (NodeId id in ids)
                {
                    DetachTransfer(id, transfers);
                }
                return [.. transfers];
            }
        }

        private void DetachTransfer(NodeId id, List<Transfer> transfers)
        {
            if (m_transfers.TryGetValue(id, out Transfer? transfer))
            {
                m_transfers.Remove(id);
                transfers.Add(transfer);
                if (!transfer.PreviewId.IsNull &&
                    m_transfers.TryGetValue(transfer.PreviewId, out Transfer? preview))
                {
                    m_transfers.Remove(transfer.PreviewId);
                    transfers.Add(preview);
                }
            }
        }

        private async Task CompleteTransfersRemovalAsync(Transfer[] transfers)
        {
            var preparations = new List<IXRegistryPreparedOperation>();
            foreach (Transfer transfer in transfers)
            {
                if (transfer.Prepared is { } prepared)
                {
                    transfer.Prepared = null;
                    preparations.Add(prepared);
                }
            }
            try
            {
                await Task.WhenAll(transfers.Select(RetireTransferLocallyAsync)).ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(preparations.Select(operation => operation.DisposeAsync().AsTask()))
                    .ConfigureAwait(false);
            }
        }

        private async Task RetireTransferLocallyAsync(Transfer transfer)
        {
            try
            {
                await transfer.Initialized.Task.ConfigureAwait(false);
                if (!m_transportDisposed)
                {
                    _ = await DeleteNodeAsync(SystemContext, transfer.Node.NodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                transfer.File?.Dispose();
                m_budget.ReleaseBytes(transfer.ReservedBytes);
            }
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
                if (!DeferProjectionFileDisposal() &&
                    m_files.TryRemove(entry.Key, out XRegistryNativeFile? file))
                {
                    file.Dispose();
                }
            }
            foreach (NodeId id in retired)
            {
                m_mappedNodes.Remove(id);
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
                if (PredefinedNodes.TryGetValue(file.NodeId, out NodeState? node) && node is FileState published)
                {
                    file.Attach(published);
                }
            }
            foreach (KeyValuePair<NodeId, BaseObjectState> pair in m_entityNodes)
            {
                if (!m_entities.TryGetValue(pair.Key, out string? path))
                {
                    continue;
                }
                pair.Value.EventNotifier = m_options.EnableChangeEvents
                    ? EventNotifiers.SubscribeToEvents : EventNotifiers.None;
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
            public XRegistryAction PreparedAction { get; set; }
            public string PreparedPath { get; set; } = "/";
            public NodeId PreviewId { get; set; }

            public TaskCompletionSource<bool> Initialized { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly IXRegistryEndpoint m_endpoint;
        private readonly XRegistryBridgeNativeOptions m_options;
        private readonly XRegistryProtocolCodec m_codec;
        private readonly XRegistryFileBudget m_budget;
        private readonly SemaphoreSlim m_projectionGate = new(1, 1);
        private readonly Lock m_transportGate = new();
        private readonly Dictionary<NodeId, Transfer> m_transfers = [];
        private readonly ConcurrentDictionary<NodeId, XRegistryNativeFile> m_files = new();
        private readonly ConcurrentDictionary<NodeId, string> m_entities = new();
        private RegistryState? m_registry;
        private RegistryBridgeState? m_bridge;
        private XRegistryBridgeProjectionStrategy? m_strategy;
        private XRegistryProjectionEngine? m_projection;
        private volatile bool m_transportClosed;
        private volatile bool m_transportDisposed;
    }
}
