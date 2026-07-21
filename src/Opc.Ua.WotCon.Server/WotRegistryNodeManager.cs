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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.V2;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// The stable NodeManager that exposes the WoT Connectivity V2 registry
    /// (<c>WoTRegistry</c>) and its xRegistry-derived group structure. It hosts
    /// the injected <see cref="IWotRegistryService"/> and
    /// <see cref="WotMaterializationCoordinator"/>: content mutations trigger a
    /// coordinator refresh that projects TD/TM closures as separate runtime
    /// NodeManagers, so this manager stays stable while projections come and go.
    /// The generated <c>Refresh</c> Method is wired to the coordinator; the
    /// coordinator's events are re-emitted as the generated V2 event types.
    /// </summary>
    public sealed class WotRegistryNodeManager : AsyncCustomNodeManager
    {
        /// <summary>Initializes a new registry NodeManager.</summary>
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
                  V2.Namespaces.WotConV2,
                  XRegistry.Namespaces.XRegistry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            m_coordinator.StrictBindings = options.StrictBindings;
            m_coordinator.ServerNamespaceUris = server.NamespaceUris;
            m_projection = new WotRegistryProjection(this, m_registry, m_options, m_logger);
        }

        /// <summary>Gets the hosted registry service.</summary>
        public IWotRegistryService Registry => m_registry;

        /// <summary>Gets the hosted materialization coordinator.</summary>
        public WotMaterializationCoordinator Coordinator => m_coordinator;

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = new NodeStateCollection()
                .AddOpcUaXRegistry(context)
                .AddOpcUaWotConV2(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            NodeId registryNodeId = ExpandedNodeId.ToNodeId(
                V2.ObjectIds.WoTRegistry, Server.NamespaceUris);
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
            typed.AddCreateGroup(context);
            typed.AddGetOrCreateGroup(context);
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

            // Chain WoTRegistry into the Server's notifier tree so its events
            // reach subscribing clients. The generated WoTRegistry Object already
            // declares the inverse HasNotifier to the Server object, so only the
            // forward reference on the Server side is added here.
            if (m_registryNode is not null)
            {
                if (externalReferences.TryGetValue(
                        Ua.ObjectIds.Server, out IList<IReference>? serverRefs) ||
                    (serverRefs = EnsureList(externalReferences, Ua.ObjectIds.Server)) != null)
                {
                    serverRefs.Add(new NodeStateReference(
                        Ua.ReferenceTypeIds.HasNotifier, false, m_registryNode.NodeId));
                }
            }

            await m_registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            m_registry.Changed += OnRegistryChanged;
            m_coordinator.Event += OnCoordinatorEvent;

            // Materialize the browseable group/resource projection, then project
            // whatever is already persisted into the AddressSpace.
            if (m_registryNode is not null)
            {
                await m_projection.AttachAsync(m_registryNode, cancellationToken)
                    .ConfigureAwait(false);
            }
            await SafeRefreshAsync("startup").ConfigureAwait(false);
            await m_projection.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            m_registry.Changed -= OnRegistryChanged;
            m_coordinator.Event -= OnCoordinatorEvent;
            await m_coordinator.RemoveAllAsync(cancellationToken).ConfigureAwait(false);
            m_projection.Dispose();
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_projection.Dispose();
            }
            base.Dispose(disposing);
        }

        private void WireRefreshMethod(BaseObjectState registry)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(V2.Namespaces.WotConV2);
            if (registry.FindChild(SystemContext, new QualifiedName(V2.BrowseNames.Refresh, ns))
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
                new Variant(Opc.Ua.Wot.WotNodeSetConverter.VocabularyNamespace));
            ApplyBindingCapabilities(registry);
        }

        private void ApplyBindingCapabilities(BaseObjectState registry)
        {
            IReadOnlyList<WoTBindingCapabilityDataType> caps = m_coordinator.BindingCapabilities;
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

            WotRefreshResult result = await m_coordinator
                .RefreshAsync(request, cancellationToken).ConfigureAwait(false);

            outputArguments.Clear();
            outputArguments.Add(new Variant(new ExtensionObject(result.Summary)));
            var encodedResults = new ExtensionObject[result.Results.Length];
            for (int i = 0; i < result.Results.Length; i++)
            {
                encodedResults[i] = new ExtensionObject(result.Results[i]);
            }
            outputArguments.Add(new Variant(new ArrayOf<ExtensionObject>(encodedResults)));
            outputArguments.Add(new Variant(result.NewGeneration));
            return ServiceResult.Good;
        }

        private void OnRegistryChanged(object? sender, WotRegistryChangedEventArgs e)
        {
            // Keep the browseable projection synchronized on every change,
            // including projection-only callbacks (which must never re-trigger
            // materialization).
            _ = SafeReconcileAsync();
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
                m_logger.LogWarning(ex, "Failed to report WoT materialization event.");
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
                        SetEventStruct(evt, V2.BrowseNames.Summary, e.Summary);
                    }
                    SetEventValue(evt, V2.BrowseNames.RequestId, new Variant(e.RequestId));
                    SetEventValue(evt, V2.BrowseNames.Generation, new Variant(e.Generation));
                    return evt;
                }
                case WotMaterializationEventKind.ValidationFailure:
                {
                    var evt = new WoTValidationFailureEventState(source);
                    InitializeEvent(evt, source, "ValidationFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    if (e.Validation is not null)
                    {
                        SetEventStruct(evt, V2.BrowseNames.ValidationOutcome, e.Validation);
                    }
                    return evt;
                }
                case WotMaterializationEventKind.LoadFailure:
                {
                    var evt = new WoTLoadFailureEventState(source);
                    InitializeEvent(evt, source, "LoadFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    SetEventEnum(evt, V2.BrowseNames.LoadState, e.LoadState);
                    SetEventValue(
                        evt, V2.BrowseNames.FailedNodeId, new Variant(e.FailedNodeId ?? NodeId.Null));
                    SetEventValue(evt, V2.BrowseNames.Reason, new Variant(e.Reason));
                    return evt;
                }
                case WotMaterializationEventKind.BindingFailure:
                {
                    var evt = new WoTBindingFailureEventState(source);
                    InitializeEvent(evt, source, "BindingFailure: " + e.Reason);
                    PopulateResourceEventFields(evt, e);
                    SetEventValue(evt, V2.BrowseNames.BindingUri, new Variant(e.BindingUri));
                    SetEventValue(evt, V2.BrowseNames.Reason, new Variant(e.Reason));
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
            SetEventValue(evt, V2.BrowseNames.Xid, new Variant(e.Xid));
            SetEventValue(evt, V2.BrowseNames.ResourceId, new Variant(e.ResourceId));
            SetEventValue(evt, V2.BrowseNames.VersionId, new Variant(e.VersionId));
            SetEventEnum(evt, V2.BrowseNames.DocumentKind, e.DocumentKind);
            SetEventValue(evt, V2.BrowseNames.Generation, new Variant(e.Generation));
            SetEventEnum(evt, V2.BrowseNames.Phase, e.Phase);
            SetEventEnum(evt, V2.BrowseNames.Outcome, e.Outcome);
        }

        private void SetEventValue(BaseEventState evt, string browseName, Variant value)
            => evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value, false);

        private void SetEventEnum<TEnum>(BaseEventState evt, string browseName, TEnum value)
            where TEnum : struct, Enum
            => evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value);

        private void SetEventStruct<TStruct>(BaseEventState evt, string browseName, TStruct value)
            where TStruct : IEncodeable
            => evt.SetChildValue(SystemContext, WoTQualifiedName(browseName), value, false);

        private QualifiedName WoTQualifiedName(string browseName)
            => new(browseName, (ushort)Server.NamespaceUris.GetIndex(V2.Namespaces.WotConV2));

        private async Task SafeReconcileAsync()
        {
            try
            {
                await m_projection.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "WoT registry projection reconcile failed.");
            }
        }

        private async Task SafeRefreshAsync(string reason)
        {
            try
            {
                await m_coordinator.RefreshAsync(new WotRefreshRequest { RequestId = reason })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "WoT registry refresh ({Reason}) failed.", reason);
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
                .EndpointDescription?.SecurityMode ?? MessageSecurityMode.None;
            if (securityMode != policy.MinimumSecurityMode)
            {
                m_logger.LogWarning(
                    "Denied WoT registry '{Operation}': channel security mode {Mode} is too low.",
                    operation, securityMode);
                return StatusCodes.BadUserAccessDenied;
            }
            IUserIdentity? identity = operationContext.UserIdentity;
            if (identity is null ||
                (!policy.AllowAnonymous && identity.TokenType == UserTokenType.Anonymous))
            {
                m_logger.LogWarning(
                    "Denied WoT registry '{Operation}': anonymous or missing identity.", operation);
                return StatusCodes.BadUserAccessDenied;
            }
            if (!identity.GrantedRoleIds.Contains(policy.RequiredRoleId))
            {
                m_logger.LogWarning(
                    "Denied WoT registry '{Operation}': caller lacks required role.", operation);
                return StatusCodes.BadUserAccessDenied;
            }
            return ServiceResult.Good;
        }

        private static IList<IReference> EnsureList(
            IDictionary<NodeId, IList<IReference>> externalReferences, NodeId nodeId)
        {
            if (!externalReferences.TryGetValue(nodeId, out IList<IReference>? list))
            {
                list = new List<IReference>();
                externalReferences[nodeId] = list;
            }
            return list;
        }

        private void SetChildValue(BaseObjectState parent, string browseName, Variant value)
        {
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(V2.Namespaces.WotConV2);
            if (parent.FindChild(SystemContext, new QualifiedName(browseName, ns))
                is BaseVariableState variable)
            {
                variable.Value = value;
            }
        }

        private readonly WotRegistryServerOptions m_options;
        private readonly IWotRegistryService m_registry;
        private readonly WotMaterializationCoordinator m_coordinator;
        private readonly WotRegistryProjection m_projection;
        private BaseObjectState? m_registryNode;
    }
}
