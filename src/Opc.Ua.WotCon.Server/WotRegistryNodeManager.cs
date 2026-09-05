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
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// The stable NodeManager that exposes the WoT Connectivity 1.1 registry
    /// (<c>WoTRegistry</c>) and its xRegistry-derived group structure. It hosts
    /// the injected <see cref="IWotRegistryService"/> and
    /// <see cref="WotMaterializationCoordinator"/>: content mutations trigger a
    /// coordinator refresh that projects TD/TM closures as separate runtime
    /// NodeManagers, so this manager stays stable while projections come and go.
    /// The generated <c>Refresh</c> Method is wired to the coordinator; the
    /// coordinator's events are re-emitted as the generated registry event types.
    /// </summary>
    public sealed class WotRegistryNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes a new registry NodeManager.
        /// </summary>
        public WotRegistryNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            WotRegistryServerOptions options,
            IWotRegistryService registry,
            WotMaterializationCoordinator coordinator)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<WotRegistryNodeManager>(),
                  Namespaces.WotCon,
                  XRegistryWellKnown.XRegistryNamespaceUri)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            Coordinator.StrictBindings = options.StrictBindings;
            Coordinator.RetirementPolicy = options.RetirementPolicy;
            Coordinator.ServerNamespaceUris = server.NamespaceUris;

            // WoT Binding Section 5.1.5 makes a loaded AddressSpace the second
            // half of the local context. This is the first point at which it
            // exists, and without it a Section 5.2.1 binding to a companion
            // model type cannot resolve.
            Coordinator.UseAddressSpace(new AddressSpaceWotNodeResolver(server));
            m_projection = new WotRegistryProjection(this, Registry, m_options);
            m_reconcileQueue = new WotRegistryReconcileQueue(SafeReconcileAsync);
        }

        /// <summary>
        /// Gets the hosted registry service.
        /// </summary>
        public IWotRegistryService Registry { get; }

        /// <summary>
        /// Gets the hosted materialization coordinator.
        /// </summary>
        public WotMaterializationCoordinator Coordinator { get; }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // Load the xRegistry base plus the combined WoT-Con model, then keep
            // only the additive registry slice. The incorporated (deprecated)
            // OPC 10100-1 v1.02 nodes are owned by WotConnectivityNodeManager, so
            // the two managers never claim the same static model node twice.
            NodeStateCollection nodes = new NodeStateCollection()
                .AddOpcUaXRegistry(context)
                .AddOpcUaWotCon(context);
            WotConModelPartition.RetainRegistryNodes(nodes, context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            var registryNodeId = ExpandedNodeId.ToNodeId(
                ObjectIds.WoTRegistry, Server.NamespaceUris);
            if (predefinedNode is BaseObjectState registry &&
                registry.NodeId == registryNodeId)
            {
                m_registryNode = registry;
                registry.EventNotifier = EventNotifiers.SubscribeToEvents;
                EnsureRegistryManagementMethods(context, registry);
                WireRefreshMethod(registry);
                ApplyRegistrySettings(context, registry);
            }
            return new ValueTask<NodeState>(predefinedNode);
        }

        private void EnsureRegistryManagementMethods(
            ISystemContext context, BaseObjectState registry)
        {
            if (registry is not RegistryState typed)
            {
                return;
            }
            // Instantiate the optional xRegistry CreateGroup/GetOrCreateGroup
            // Methods on the well-known singleton. The generated Add helpers mint
            // fresh per-instance NodeIds (through the NodeManager's NodeIdFactory)
            // and rebase the argument references so the Methods never collide with
            // the RegistryType Method declarations.
            typed.AddCreateGroup(context)
                .AddGetOrCreateGroup(context);
            WotRegistryProjection.LinkMethodArguments(typed.CreateGroup, context);
            WotRegistryProjection.LinkMethodArguments(typed.GetOrCreateGroup, context);

            // Instantiate the optional Labels (AttributesType) container and its
            // AddAttribute/RemoveAttribute Methods here, before this predefined
            // node's subtree is registered by the base class's
            // CreateAddressSpaceAsync: only children present at that point are
            // swept into the NodeManager's node table. WotRegistryProjection
            // wires the actual Method handlers later (see AttachAsync).
            typed.AddLabels(context);
            if (typed.Labels is not null)
            {
                typed.Labels.AddAddAttribute(context);
                typed.Labels.AddRemoveAttribute(context);
                WotRegistryProjection.LinkMethodArguments(typed.Labels, context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            // Register WoTRegistry as a root notifier so Server event MonitoredItems subscribe to
            // it, and publish the forward HasNotifier reference for browseability.
            if (m_registryNode is not null)
            {
                await AddRootNotifierAsync(m_registryNode, cancellationToken)
                    .ConfigureAwait(false);
                if (externalReferences.TryGetValue(
                        Ua.ObjectIds.Server, out IList<IReference>? serverRefs) ||
                    (serverRefs = EnsureList(externalReferences, Ua.ObjectIds.Server)) != null)
                {
                    serverRefs.Add(new NodeStateReference(
                        Ua.ReferenceTypeIds.HasNotifier, false, m_registryNode.NodeId));
                }
            }

            await Registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            Registry.Changed += OnRegistryChanged;
            Coordinator.Event += OnCoordinatorEvent;

            // Materialize the browseable group/resource projection, then project
            // whatever is already persisted into the AddressSpace.
            if (m_registryNode is not null)
            {
                await m_projection.AttachAsync(m_registryNode, cancellationToken)
                    .ConfigureAwait(false);
            }
            await SafeRefreshAsync("startup").ConfigureAwait(false);
            await m_projection.ReconcileProjectionAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            Registry.Changed -= OnRegistryChanged;
            Coordinator.Event -= OnCoordinatorEvent;
            await m_reconcileQueue.CompleteAsync(cancellationToken).ConfigureAwait(false);
            await Coordinator.RemoveAllAsync(cancellationToken).ConfigureAwait(false);
            m_projection.Dispose();
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_reconcileQueue.Dispose();
                m_projection.Dispose();
                m_refreshGate.Dispose();
            }
            base.Dispose(disposing);
        }

        private void WireRefreshMethod(BaseObjectState registry)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            if (registry.FindChild(SystemContext, new QualifiedName(BrowseNames.Refresh, ns))
                is MethodState refresh)
            {
                refresh.OnCallMethod2Async = OnRefreshAsync;
            }
        }

        private void ApplyRegistrySettings(ISystemContext context, BaseObjectState registry)
        {
            SetChildValue(registry, "AutoRefresh", new Variant(m_options.AutoRefresh));
            SetChildValue(registry, "RefreshMode",
                new Variant((int)WoTRefreshModeEnum.EventDriven));
            SetChildValue(registry, "VocabularyVersion",
                new Variant(Wot.WotNodeSetConverter.VocabularyNamespace));
            ApplyBindingCapabilities(registry);
        }

        private void ApplyBindingCapabilities(BaseObjectState registry)
        {
            IReadOnlyList<WoTBindingCapabilityDataType> caps = Coordinator.BindingCapabilities;
            if (caps.Count == 0)
            {
                return;
            }
            var encoded = new ExtensionObject[caps.Count];
            for (int i = 0; i < caps.Count; i++)
            {
                encoded[i] = new ExtensionObject(caps[i]);
            }
            SetChildValue(registry, "SelectedBindings",
                new Variant(new ArrayOf<ExtensionObject>(encoded)));
        }

        private async ValueTask<ServiceResult> OnRefreshAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            ServiceResult access = CheckManagementAccess(context, "Refresh");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }

            ServiceResult decoded = WotRefreshArguments.TryDecode(
                inputArguments, Server.MessageContext, out WotRefreshRequest request);
            if (ServiceResult.IsBad(decoded))
            {
                return decoded;
            }

            if (!await m_refreshGate
                    .WaitAsync(0, cancellationToken)
                    .ConfigureAwait(false))
            {
                return StatusCodes.BadServerTooBusy;
            }
            try
            {
                WotRefreshResult result = await Coordinator
                    .RefreshAsync(request, cancellationToken).ConfigureAwait(false);

                outputArguments.Clear();
                outputArguments.Add(Variant.FromStructure(result.Summary));
                outputArguments.Add(Variant.FromStructure(result.Results.ToArrayOf()));
                outputArguments.Add(new Variant(result.NewGeneration));
                return ServiceResult.Good;
            }
            finally
            {
                m_refreshGate.Release();
            }
        }

        private void OnRegistryChanged(object? sender, WotRegistryChangedEventArgs e)
        {
            // Keep the browseable projection synchronized on every change,
            // including projection-only callbacks (which must never re-trigger
            // materialization).
            m_reconcileQueue.Enqueue(e);
            if (e.ProjectionOnly || !m_options.AutoRefresh)
            {
                return;
            }
            // Content mutation: re-project asynchronously without blocking the caller.
            _ = SafeRefreshAsync("auto");
        }

        private void OnCoordinatorEvent(object? sender, WotMaterializationEventArgs e)
        {
            if (m_registryNode is null)
            {
                return;
            }
            try
            {
                NodeState source = EventSourceFor(e);
                BaseEventState? evt = BuildEvent(e, source);
                if (evt is not null)
                {
                    source.ReportEvent(SystemContext, evt);
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToReportMaterializationEvent(ex);
            }
        }

        private NodeState EventSourceFor(WotMaterializationEventArgs e)
        {
            // Resource lifecycle failures are sourced at the specific resource
            // node; the registry object remains the summary source for the
            // refresh-completed event.
            if (e.Kind == WotMaterializationEventKind.RefreshCompleted)
            {
                return m_registryNode!;
            }
            return m_projection.EventSourceFor(e.Xid);
        }

        private BaseEventState? BuildEvent(WotMaterializationEventArgs e, NodeState source)
        {
            switch (e.Kind)
            {
                case WotMaterializationEventKind.RefreshCompleted:
                {
                    var evt = new WoTRefreshCompletedEventState(m_registryNode);
                    InitializeEvent(evt, source, "RefreshCompleted");
                    // Summary/RequestId/NewGeneration come from the coordinator's
                    // refresh summary, which is produced from the registry snapshot.
                    if (e.Summary is not null)
                    {
                        SetEventStruct(evt, BrowseNames.Summary, e.Summary);
                    }
                    SetEventValue(evt, BrowseNames.RequestId, new Variant(e.RequestId));
                    SetEventValue(evt, BrowseNames.Generation, new Variant(e.Generation));
                    return evt;
                }
                case WotMaterializationEventKind.ValidationFailure:
                {
                    var evt = new WoTValidationFailureEventState(source);
                    InitializeEvent(evt, source, "ValidationFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    if (e.Validation is not null)
                    {
                        SetEventStruct(evt, BrowseNames.ValidationOutcome, e.Validation);
                    }
                    return evt;
                }
                case WotMaterializationEventKind.LoadFailure:
                {
                    var evt = new WoTLoadFailureEventState(source);
                    InitializeEvent(evt, source, "LoadFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    SetEventEnum(evt, BrowseNames.LoadState, e.LoadState);
                    SetEventValue(
                        evt, BrowseNames.FailedNodeId, new Variant(e.FailedNodeId));
                    SetEventValue(evt, BrowseNames.Reason, new Variant(e.Reason));
                    return evt;
                }
                case WotMaterializationEventKind.BindingFailure:
                {
                    var evt = new WoTBindingFailureEventState(source);
                    InitializeEvent(evt, source, "BindingFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    SetEventValue(evt, BrowseNames.BindingUri, new Variant(e.BindingUri));
                    SetEventValue(evt, BrowseNames.Reason, new Variant(e.Reason));
                    return evt;
                }
                default:
                {
                    var evt = new WoTResourceEventState(source);
                    InitializeEvent(evt, source, "Resource: " + e.ResourceId);
                    PopulateResourceEventFields(evt, e);
                    return evt;
                }
            }
        }

        private void InitializeEvent(BaseEventState evt, NodeState source, string message)
        {
            evt.Initialize(
                SystemContext,
                source: source,
                EventSeverity.Medium,
                new LocalizedText(message));
            evt.SetChildValue(
                SystemContext, Ua.BrowseNames.SourceName,
                source.DisplayName.Text ?? "WoTRegistry", false);
        }

        /// <summary>
        /// Populates the identity/lifecycle fields shared by every
        /// <c>WoTResourceEventType</c> (and its concrete subtypes) from the
        /// coordinator's event arguments.
        /// </summary>
        private void PopulateResourceEventFields(
            BaseEventState evt, WotMaterializationEventArgs e)
        {
            SetEventValue(evt, BrowseNames.Xid, new Variant(e.Xid));
            SetEventValue(evt, BrowseNames.ResourceId, new Variant(e.ResourceId));
            SetEventValue(evt, BrowseNames.VersionId, new Variant(e.VersionId));
            SetEventEnum(evt, BrowseNames.DocumentKind, e.DocumentKind);
            SetEventValue(evt, BrowseNames.Generation, new Variant(e.Generation));
            SetEventEnum(evt, BrowseNames.Phase, e.Phase);
            SetEventEnum(evt, BrowseNames.Outcome, e.Outcome);
        }

        private void SetEventValue(BaseEventState evt, string browseName, Variant value)
        {
            evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value, false);
        }

        private void SetEventEnum<TEnum>(BaseEventState evt, string browseName, TEnum value)
            where TEnum : struct, Enum
        {
            evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value);
        }

        private void SetEventStruct<TStruct>(BaseEventState evt, string browseName, TStruct value)
            where TStruct : IEncodeable
        {
            evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value, false);
        }

        private QualifiedName WoTQualifiedName(string browseName)
        {
            return new(browseName, (ushort)Server.NamespaceUris.GetIndex(Namespaces.WotCon));
        }

        private async Task SafeReconcileAsync(WotRegistryChangedEventArgs change)
        {
            try
            {
                await m_projection.ReconcileAsync(
                        change.Previous,
                        change.Current,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.RegistryProjectionReconcileFailed(ex);
            }
        }

        private async Task SafeRefreshAsync(string reason)
        {
            try
            {
                await Coordinator.RefreshAsync(new WotRefreshRequest { RequestId = reason })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.RegistryRefreshFailed(ex, reason);
            }
        }

        internal ServiceResult CheckManagementAccess(ISystemContext context, string operation)
        {
            if (context is not SessionSystemContext { OperationContext: OperationContext operationContext })
            {
                // Local / programmatic call: allowed.
                return ServiceResult.Good;
            }
            WotManagementAccessPolicy policy = m_options.ManagementAccess;
            MessageSecurityMode securityMode = operationContext.ChannelContext?
                .EndpointDescription?.SecurityMode ??
                MessageSecurityMode.None;
            // MinimumSecurityMode is a floor, not an exact match: MessageSecurityMode is ordered by
            // strength (Invalid < None < Sign < SignAndEncrypt), so a channel at or above the
            // configured mode is accepted and Invalid is always rejected.
            if (securityMode < policy.MinimumSecurityMode)
            {
                m_logger.ManagementCallDeniedSecurityMode(operation, securityMode);
                return StatusCodes.BadUserAccessDenied;
            }
            IUserIdentity? identity = operationContext.UserIdentity;
            if (identity is null ||
                (!policy.AllowAnonymous && identity.TokenType == UserTokenType.Anonymous))
            {
                m_logger.ManagementCallDeniedAnonymousIdentity(operation);
                return StatusCodes.BadUserAccessDenied;
            }
            if (!identity.GrantedRoleIds.Contains(policy.RequiredRoleId))
            {
                m_logger.ManagementCallDeniedMissingRole(operation);
                return StatusCodes.BadUserAccessDenied;
            }
            return ServiceResult.Good;
        }

        private static IList<IReference> EnsureList(
            IDictionary<NodeId, IList<IReference>> externalReferences, NodeId nodeId)
        {
            if (!externalReferences.TryGetValue(nodeId, out IList<IReference>? list))
            {
                list = [];
                externalReferences[nodeId] = list;
            }
            return list;
        }

        private void SetChildValue(BaseObjectState parent, string browseName, Variant value)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            if (parent.FindChild(SystemContext, new QualifiedName(browseName, ns))
                is BaseVariableState variable)
            {
                variable.Value = value;
            }
        }

        private readonly WotRegistryServerOptions m_options;
        private readonly WotRegistryProjection m_projection;
        private readonly WotRegistryReconcileQueue m_reconcileQueue;
        private readonly SemaphoreSlim m_refreshGate = new(1, 1);
        private BaseObjectState? m_registryNode;
    }

    internal sealed class WotRegistryReconcileQueue : IDisposable
    {
        public WotRegistryReconcileQueue(
            Func<WotRegistryChangedEventArgs, Task> reconcile)
        {
            m_reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));
        }

        public void Enqueue(WotRegistryChangedEventArgs change)
        {
            lock (m_lock)
            {
                if (m_completed)
                {
                    return;
                }
                m_changes.Enqueue(change);
                if (!m_running)
                {
                    m_running = true;
                    m_worker = DrainAsync();
                }
            }
        }

        public async ValueTask WhenIdleAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                Task? worker;
                lock (m_lock)
                {
                    if (!m_running)
                    {
                        return;
                    }
                    worker = m_worker;
                }
                cancellationToken.ThrowIfCancellationRequested();
                await worker!.ConfigureAwait(false);
            }
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                m_completed = true;
            }
            await WhenIdleAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lock (m_lock)
            {
                m_completed = true;
                m_changes.Clear();
            }
        }

        private async Task DrainAsync()
        {
            await Task.Yield();
            while (true)
            {
                WotRegistryChangedEventArgs change;
                lock (m_lock)
                {
                    if (m_changes.Count == 0)
                    {
                        m_running = false;
                        return;
                    }
                    change = m_changes.Dequeue();
                }
                await m_reconcile(change).ConfigureAwait(false);
            }
        }

        private readonly Func<WotRegistryChangedEventArgs, Task> m_reconcile;
        private readonly Queue<WotRegistryChangedEventArgs> m_changes = new();
        private readonly Lock m_lock = new();
        private Task? m_worker;
        private bool m_running;
        private bool m_completed;
    }

    /// <summary>
    /// Holds source-generated log messages emitted by the WoT registry NodeManager component.
    /// </summary>
    internal static partial class WotRegistryNodeManagerLog
    {
        /// <summary>
        /// Logs that raising a WoT materialization event failed.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 0, Level = LogLevel.Warning,
            Message = "Failed to report WoT materialization event.")]
        public static partial void FailedToReportMaterializationEvent(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs that reconciling the registry projection failed.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 1, Level = LogLevel.Warning,
            Message = "WoT registry projection reconcile failed.")]
        public static partial void RegistryProjectionReconcileFailed(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs that a registry refresh failed for the supplied reason.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 2, Level = LogLevel.Warning,
            Message = "WoT registry refresh ({Reason}) failed.")]
        public static partial void RegistryRefreshFailed(this ILogger logger, Exception ex, string reason);

        /// <summary>
        /// Logs that a registry management call was denied because channel security was too weak.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 3, Level = LogLevel.Warning,
            Message = "Denied WoT registry '{Operation}': channel security mode {Mode} is too low.")]
        public static partial void ManagementCallDeniedSecurityMode(
            this ILogger logger,
            string operation,
            MessageSecurityMode mode);

        /// <summary>
        /// Logs that a registry management call was denied because the caller was anonymous.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 4, Level = LogLevel.Warning,
            Message = "Denied WoT registry '{Operation}': anonymous or missing identity.")]
        public static partial void ManagementCallDeniedAnonymousIdentity(this ILogger logger, string operation);

        /// <summary>
        /// Logs that a registry management call was denied because the caller lacks the required role.
        /// </summary>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryNodeManager + 5, Level = LogLevel.Warning,
            Message = "Denied WoT registry '{Operation}': caller lacks required role.")]
        public static partial void ManagementCallDeniedMissingRole(this ILogger logger, string operation);
    }
}
