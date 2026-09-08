/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Opt-in base class for node managers that want to use the fluent
    /// <c>Publish</c> surface (external event sources delivered through
    /// <see cref="NodeState.ReportEvent"/>). The source-generator-emitted
    /// <c>NodeManagerBase</c> derives from this class when any wrapper
    /// in the design exposes a <c>Publish</c> binding; hand-written
    /// managers can also derive directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class owns an <see cref="EventSourceRegistry"/> whose
    /// reconcile loop runs as long as the manager is alive. Override
    /// <see cref="OnSubscribeToEventsAsync"/> hooks the registry so it
    /// activates and deactivates sources in lock-step with
    /// <see cref="NodeState.AreEventsMonitored"/>. <c>Dispose(bool)</c>
    /// tears the registry down before the base implementation runs so
    /// no iterator outlives the manager.
    /// </para>
    /// <para>
    /// Subclasses should never call into <see cref="EventSources"/>
    /// outside the fluent builder pipeline; the surface is exposed for
    /// generated code only.
    /// </para>
    /// </remarks>
    public abstract class FluentNodeManagerBase : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            params string[] namespaceUris)
            : base(server, namespaceUris)
        {
            Configuration = null;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, logger, namespaceUris)
        {
            Configuration = null;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            params string[] namespaceUris)
            : base(server, configuration, namespaceUris)
        {
            Configuration = configuration;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, logger, namespaceUris)
        {
            Configuration = configuration;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, namespaceUris)
        {
            Configuration = configuration;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        protected FluentNodeManagerBase(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
            Configuration = configuration;
            EventSources = new EventSourceRegistry(this, m_logger);
            Simulations = new SimulationRegistry(this, m_logger);
            MonitoredSources = new MonitoredSourceRegistry(this, m_logger);
        }

        /// <summary>
        /// The application configuration supplied when this manager was
        /// constructed. Configuration-less legacy constructors expose
        /// <c>null</c>.
        /// </summary>
        protected ApplicationConfiguration? Configuration { get; }

        /// <summary>
        /// The concrete fluent builder attached to this manager.
        /// </summary>
        internal NodeManagerBuilder? AttachedBuilder { get; private set; }

        /// <summary>
        /// Registry that the fluent <c>Publish</c> surface stores its
        /// registered event sources in. Accessed by
        /// <see cref="NodeManagerBuilder.AttachEventSources"/> during
        /// <c>Configure</c> and by generated wrappers; not intended for
        /// direct subclass use.
        /// </summary>
        internal EventSourceRegistry EventSources { get; }

        /// <summary>
        /// Registry that the fluent <c>Simulation</c> surface stores its
        /// registered periodic tick loops in. Started after
        /// <c>Configure</c> completes (via <c>NodeManagerBuilder.SealAsync</c>)
        /// and torn down on disposal.
        /// </summary>
        internal SimulationRegistry Simulations { get; }

        /// <summary>
        /// Registry for data sources activated by monitored-item lifecycle.
        /// </summary>
        internal MonitoredSourceRegistry MonitoredSources { get; }

        /// <summary>
        /// Constructs a <see cref="NodeManagerBuilder"/> for this
        /// manager and attaches the fluent event-source / simulation
        /// registries in a single call. Hand-written managers use this
        /// to collapse the imperative
        /// <c>new NodeManagerBuilder(SystemContext, this, nsIndex, ...)</c>
        /// + <c>AttachToBuilder(builder)</c> + <c>Configure(builder)</c>
        /// + <c>builder.SealAsync()</c> quadruple to a short pipeline:
        /// <code>
        /// NodeManagerBuilder builder = CreateFluentBuilder(nsIndex);
        /// Configure(builder);
        /// await builder.SealAsync(cancellationToken);
        /// </code>
        /// The root/nodeId/typeId/dataTypeId lookups default to scanning the
        /// manager's <see cref="CustomNodeManager2.PredefinedNodes"/>
        /// dictionary, mirroring the resolver wiring that the
        /// source-generated <c>NodeManagerBase.CreateAddressSpaceAsync</c>
        /// emits.
        /// </summary>
        /// <param name="defaultNamespaceIndex">
        /// Namespace index used when a browse-path segment omits an
        /// explicit <c>ns=N;</c> prefix. Typically the manager's
        /// model-specific namespace index.
        /// </param>
        /// <returns>
        /// A configured <see cref="NodeManagerBuilder"/> ready to
        /// receive <c>Configure(builder)</c> wiring and, once the wiring
        /// is done, <see cref="NodeManagerBuilder.SealAsync"/>.
        /// </returns>
        public NodeManagerBuilder CreateFluentBuilder(ushort defaultNamespaceIndex)
        {
            var builder = new NodeManagerBuilder(
                SystemContext,
                this,
                defaultNamespaceIndex,
                browseName => PredefinedNodes.Values.FindByBrowseName(browseName)!,
                nodeId => PredefinedNodes.FindById(nodeId)!,
                PredefinedNodes.Values.FindByTypeDefinition,
                PredefinedNodes.Values.FindByDataType);
            AttachToBuilder(builder);
            return builder;
        }

        /// <summary>
        /// Attaches this manager's event-source registry to the supplied
        /// fluent builder so that <c>Publish</c> extension methods can
        /// resolve it. The generator-emitted <c>CreateAddressSpaceAsync</c>
        /// invokes this immediately after constructing the builder; hand-
        /// written managers that build their own
        /// <see cref="NodeManagerBuilder"/> should call this once before
        /// passing the builder into <c>Configure</c>.
        /// </summary>
        /// <param name="builder">
        /// The fluent builder that the manager's <c>Configure</c>
        /// partial(s) will receive.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// Raised when <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public void AttachToBuilder(NodeManagerBuilder builder)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            lock (m_attachedBuildersLock)
            {
                foreach (NodeManagerBuilder attached in m_attachedBuilders)
                {
                    if (ReferenceEquals(attached, builder))
                    {
                        return;
                    }
                }

                builder.AttachEventSources(EventSources);
                builder.AttachSimulations(Simulations);
                builder.AttachMonitoredSources(MonitoredSources);
                builder.AttachOwner(this);
                m_attachedBuilders.Add(builder);
                AttachedBuilder = builder;
            }
        }

        /// <summary>
        /// Resolves the concrete builder attached to the manager exposed by
        /// an arbitrary <see cref="INodeManagerBuilder"/> facade.
        /// </summary>
        internal static NodeManagerBuilder ResolveAttachedBuilder(
            INodeManagerBuilder builder,
            string feature)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            if (builder is NodeManagerBuilder concreteBuilder &&
                concreteBuilder.FluentOwner != null)
            {
                return concreteBuilder;
            }

            if (builder.NodeManager is FluentNodeManagerBase manager &&
                manager.AttachedBuilder is { } concrete)
            {
                return concrete;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "{0} requires the node manager to derive from FluentNodeManagerBase " +
                "and attach its builder before Configure runs. Manager type '{1}' does not opt in.",
                feature,
                builder.NodeManager?.GetType().FullName ?? "(unknown)");
        }

        internal VirtualNodeRegistration? FindVirtualNodeRegistration(
            NodeId nodeId)
        {
            VirtualNodeRegistration? match = null;
            foreach (NodeManagerBuilder builder in GetAttachedBuilders())
            {
                VirtualNodeRegistration? registration =
                    builder.FindVirtualNodeRegistration(nodeId);
                if (registration == null)
                {
                    continue;
                }
                if (match != null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "NodeId '{0}' matches virtual-node families registered " +
                        "on more than one fluent builder.",
                        nodeId);
                }
                match = registration;
            }
            return match;
        }

        /// <summary>
        /// Asynchronous counterpart of the <c>Configure(INodeManagerBuilder)</c>
        /// hook, invoked once per manager activation with the same builder
        /// immediately <em>before</em> the synchronous <c>Configure</c>
        /// callbacks run.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the seam for wiring that has to await: materialising
        /// instances from a store or a companion-spec factory, reading a
        /// configuration source, or registering nodes whose creation is
        /// asynchronous. Because it runs before the synchronous
        /// <c>Configure</c> pass, nodes it creates are already in the
        /// address space when <c>Configure</c> wires callbacks against
        /// them, and everything it stages on the builder is picked up by
        /// the <c>RegisterAuthoredNodes</c> / reverse-reference /
        /// <see cref="NodeManagerBuilder.SealAsync"/> steps that follow.
        /// </para>
        /// <para>
        /// The hook is a <c>virtual</c> method rather than a second
        /// <c>partial</c> declaration because a <c>partial</c> method can be
        /// either optional (<c>partial void</c>, no return value) or
        /// awaitable (an extended partial method, which must be
        /// implemented) — not both. Generated managers therefore keep
        /// <c>partial void Configure(INodeManagerBuilder)</c> for
        /// synchronous wiring and override this method for asynchronous
        /// wiring; a manager can use either or both.
        /// </para>
        /// <para>
        /// The default implementation does nothing. Overrides do not need
        /// to invoke <c>base.ConfigureAsync</c>.
        /// </para>
        /// </remarks>
        /// <param name="builder">
        /// The fluent builder for this activation, already attached to the
        /// manager's registries.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected virtual ValueTask ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            return default;
        }

        /// <summary>
        /// Re-runs the reverse-reference collection pass after the user's
        /// <c>Configure</c> callbacks return so that nodes registered
        /// during <c>Configure</c> publish their references to nodes
        /// owned by other node managers (e.g. an inverse
        /// <c>Organizes</c> reference to the Objects folder) into
        /// <paramref name="externalReferences"/>, exactly like nodes
        /// declared in a NodeSet do. Inverse <c>HasNotifier</c>
        /// references to external notifiers additionally register the
        /// source as a root notifier.
        /// </summary>
        /// <remarks>
        /// The source-generated <c>CreateAddressSpaceAsync</c> and the
        /// hosting <c>FluentNodeManager</c> invoke this once between the
        /// <c>Configure</c> callbacks and <see cref="NodeManagerBuilder.SealAsync"/>;
        /// hand-written managers that drive
        /// <see cref="CreateFluentBuilder"/> themselves should do the
        /// same. Timing is safe because the master node manager
        /// distributes <paramref name="externalReferences"/> only after
        /// every manager's <c>CreateAddressSpaceAsync</c> has returned.
        /// The pass is idempotent, so running it after an earlier
        /// <c>LoadPredefinedNodesAsync</c> sweep adds no duplicates.
        /// </remarks>
        /// <param name="externalReferences">
        /// The dictionary of references to add to external targets that
        /// was handed to <c>CreateAddressSpaceAsync</c>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected async ValueTask CompleteConfigureAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await CompleteNodeSetImportsAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            await AddReverseReferencesAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Links and registers the NodeSet documents which the
        /// <c>Configure</c> pass imported through
        /// <see cref="INodeManagerBuilder.Import"/>.
        /// </summary>
        private async ValueTask CompleteNodeSetImportsAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = 0; ii < builders.Length; ii++)
            {
                NodeManagerBuilder builder = builders[ii];
                if (!builder.HasPendingNodeSetImports)
                {
                    continue;
                }

                // Snapshot the nodes owned before the import so linking can
                // resolve parents in them without seeing the imported nodes.
                var existingNodes = new Dictionary<NodeId, NodeState>();
                foreach (KeyValuePair<NodeId, NodeState> entry in PredefinedNodes)
                {
                    existingNodes[entry.Key] = entry.Value;
                }

                // RemovePredefinedNodeAsync only collects the references other
                // node managers hold on a removed node; dropping them is the
                // caller's job.
                var referencesToRemove = new List<LocalReference>();
                await builder.CompleteNodeSetImportsAsync(
                    existingNodes,
                    (node, ct) => AddPredefinedNodeAsync(
                        SystemContext,
                        node,
                        externalReferences,
                        ct),
                    (node, ct) => RemovePredefinedNodeAsync(
                        SystemContext,
                        node,
                        referencesToRemove,
                        ct),
                    cancellationToken).ConfigureAwait(false);

                if (referencesToRemove.Count > 0)
                {
                    DropPendingExternalReferences(externalReferences, referencesToRemove);
                    await Server.NodeManager
                        .RemoveReferencesAsync(referencesToRemove, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Removes references to displaced nodes which this pass has staged in
        /// <paramref name="externalReferences"/> but the master node manager
        /// has not applied yet.
        /// </summary>
        /// <remarks>
        /// At startup the reverse-reference pass of
        /// <c>LoadPredefinedNodesAsync</c> has already published the generated
        /// nodes' references into the dictionary. An entry that targets a node
        /// the import has just removed would otherwise be applied afterwards,
        /// leaving another node manager pointing at a node that no longer
        /// exists.
        /// </remarks>
        private static void DropPendingExternalReferences(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            List<LocalReference> referencesToRemove)
        {
            for (int ii = 0; ii < referencesToRemove.Count; ii++)
            {
                LocalReference reference = referencesToRemove[ii];
                if (!externalReferences.TryGetValue(
                        reference.SourceId,
                        out IList<IReference>? references) ||
                    references is null)
                {
                    continue;
                }

                for (int jj = references.Count - 1; jj >= 0; jj--)
                {
                    IReference candidate = references[jj];
                    if (candidate.IsInverse == reference.IsInverse &&
                        candidate.ReferenceTypeId == reference.ReferenceTypeId &&
                        !candidate.TargetId.IsAbsolute &&
                        (NodeId)candidate.TargetId == reference.TargetId)
                    {
                        references.RemoveAt(jj);
                    }
                }
            }
        }

        /// <summary>
        /// Registers every node staged by the builder's <c>Add*</c> methods
        /// with this manager.
        /// </summary>
        /// <remarks>
        /// Call this once, after the user's <c>Configure</c> delegates return
        /// and before <see cref="CompleteConfigureAsync"/>, so that the
        /// reverse-reference pass sees the newly registered nodes and mirrors
        /// their references to nodes owned by other managers (typically the
        /// Objects folder) into <c>externalReferences</c>. A builder that
        /// staged no nodes registers nothing, so the call is safe to make
        /// unconditionally. The source-generated
        /// <c>CreateAddressSpaceAsync</c> emits it for you.
        /// </remarks>
        /// <param name="builder">The builder whose staged nodes to register.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        protected ValueTask RegisterAuthoredNodesAsync(
            NodeManagerBuilder builder,
            CancellationToken cancellationToken = default)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            return builder.RegisterAuthoredNodesAsync(
                (node, ct) => AddPredefinedNodeAsync(SystemContext, node, ct),
                cancellationToken);
        }

        /// <summary>
        /// Seals the builder, replays <c>NotifyNodeAdded</c> for every node
        /// this manager owns, and then completes the registrations the
        /// <c>Configure</c> pass could not finish synchronously and starts
        /// the simulations it registered.
        /// </summary>
        /// <remarks>
        /// Call this once the address space is complete - after
        /// <see cref="RegisterAuthoredNodesAsync"/> and
        /// <see cref="CompleteConfigureAsync"/>. Both ends of the order
        /// matter: sealing first stops a lifecycle handler from authoring
        /// nodes that nothing would register any more, and activating last
        /// keeps a simulated value change from preceding the
        /// <c>OnNodeAdded</c> handler of its own node. The source-generated
        /// <c>CreateAddressSpaceAsync</c> emits this call for you.
        /// </remarks>
        /// <param name="builder">The builder the Configure pass used.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        protected ValueTask SealConfigurationAsync(
            NodeManagerBuilder builder,
            CancellationToken cancellationToken = default)
        {
            if (builder == null)
            {
                throw new System.ArgumentNullException(nameof(builder));
            }

            builder.SealGraphAuthoring();

            foreach (KeyValuePair<NodeId, NodeState> entry in PredefinedNodes)
            {
                builder.Dispatcher.NotifyNodeAdded(SystemContext, entry.Value);
            }

            return builder.CompleteSealAsync(cancellationToken);
        }

        /// <inheritdoc/>
        protected override async ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            NodeHandle handle = await base.GetManagerHandleAsync(
                context,
                nodeId,
                cache,
                cancellationToken).ConfigureAwait(false);
            if (handle != null || !IsNodeIdInNamespace(nodeId))
            {
                return handle!;
            }

            VirtualNodeRegistration? registration =
                FindVirtualNodeRegistration(nodeId);
            return registration == null
                ? null!
                : new NodeHandle
                {
                    NodeId = nodeId,
                    ParsedNodeId = registration,
                    Validated = false
                };
        }

        /// <inheritdoc/>
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            NodeState? node = await base.ValidateNodeAsync(
                context,
                handle,
                cache,
                cancellationToken).ConfigureAwait(false);
            if (node != null ||
                handle?.ParsedNodeId is not VirtualNodeRegistration registration)
            {
                return node!;
            }

            if (cache != null && cache.TryGetValue(handle.NodeId, out NodeState? cached))
            {
                return cached == null
                    ? null!
                    : ValidationComplete(context, handle, cached, cache);
            }

            NodeState? resolved = await registration.Resolver(
                context,
                handle.NodeId,
                cancellationToken).ConfigureAwait(false);
            if (resolved == null)
            {
                if (cache != null)
                {
                    cache[handle.NodeId] = null!;
                }
                return null!;
            }

            if (resolved.NodeId.IsNull)
            {
                resolved.NodeId = handle.NodeId;
            }
            else if (resolved.NodeId != handle.NodeId)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Virtual-node resolver returned NodeId '{0}' for requested NodeId '{1}'.",
                    resolved.NodeId,
                    handle.NodeId);
            }

            registration.Apply(resolved);
            return ValidationComplete(context, handle, resolved, cache!);
        }

        /// <inheritdoc/>
        protected override bool TryHandleHistoryRead(
            ISystemContext context,
            NodeState source,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            out ServiceResult status)
        {
            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].Dispatcher.TryHandleHistoryRead(
                    context,
                    source,
                    details,
                    timestampsToReturn,
                    releaseContinuationPoints,
                    nodeToRead,
                    result,
                    out status))
                {
                    return true;
                }
            }

            return base.TryHandleHistoryRead(
                context,
                source,
                details,
                timestampsToReturn,
                releaseContinuationPoints,
                nodeToRead,
                result,
                out status);
        }

        /// <inheritdoc/>
        protected override bool TryHandleHistoryUpdate(
            ISystemContext context,
            NodeState source,
            HistoryUpdateDetails nodeToUpdate,
            HistoryUpdateResult result,
            out ServiceResult status)
        {
            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].Dispatcher.TryHandleHistoryUpdate(
                    context,
                    source,
                    nodeToUpdate,
                    result,
                    out status))
                {
                    return true;
                }
            }

            return base.TryHandleHistoryUpdate(
                context,
                source,
                nodeToUpdate,
                result,
                out status);
        }

        /// <summary>
        /// Signals the registry whenever a notifier's monitored-events
        /// ref-count flips so the reconcile loop can start or stop the
        /// matching iterator. Subclasses that further override
        /// <see cref="AsyncCustomNodeManager.OnSubscribeToEventsAsync"/>
        /// must call <c>base</c> before doing their own work.
        /// </summary>
        protected override async ValueTask OnSubscribeToEventsAsync(
            ServerSystemContext context,
            MonitoredNode2 monitoredNode,
            bool unsubscribe,
            CancellationToken cancellationToken = default)
        {
            if (unsubscribe)
            {
                EventSources.SignalReconcile();
            }
            else
            {
                await EventSources.WaitUntilReadyAsync(monitoredNode.Node, cancellationToken).ConfigureAwait(false);
            }
            await base.OnSubscribeToEventsAsync(context, monitoredNode, unsubscribe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            base.OnMonitoredItemCreated(context, handle, monitoredItem);

            if (handle?.Node is not { } node)
            {
                return;
            }

            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].HasMonitoredItemCreatedHandler(node.NodeId))
                {
                    builders[ii].Dispatcher.NotifyMonitoredItemCreated(
                        context,
                        node,
                        monitoredItem);
                    break;
                }
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask<MonitoredItemCreateDecision>
            OnCreatingMonitoredItemAsync(
                MonitoredItemCreateContext context,
                CancellationToken cancellationToken = default)
        {
            MonitoredItemCreateDecision decision =
                await base.OnCreatingMonitoredItemAsync(
                    context,
                    cancellationToken).ConfigureAwait(false);
            if (decision.Kind != MonitoredItemCreateDecisionKind.Default)
            {
                return decision;
            }

            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].HasMonitoredItemCreatingHandler(
                    context.Source.NodeId))
                {
                    return await builders[ii].Dispatcher
                        .GetMonitoredItemCreateDecisionAsync(
                            context,
                            cancellationToken).ConfigureAwait(false);
                }
            }
            return decision;
        }

        /// <inheritdoc/>
        protected override async ValueTask OnMonitoredItemModifiedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            await base.OnMonitoredItemModifiedAsync(
                context,
                handle,
                monitoredItem,
                cancellationToken).ConfigureAwait(false);

            if (handle?.Node is not { } node)
            {
                return;
            }

            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].HasMonitoredItemModifiedHandler(node.NodeId))
                {
                    await builders[ii].Dispatcher.NotifyMonitoredItemModifiedAsync(
                        context,
                        node,
                        monitoredItem,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

        }

        /// <inheritdoc/>
        protected override async ValueTask OnMonitoredItemDeletedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            await base.OnMonitoredItemDeletedAsync(
                context,
                handle,
                monitoredItem,
                cancellationToken).ConfigureAwait(false);

            if (handle?.Node is not { } node)
            {
                return;
            }

            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].HasMonitoredItemDeletedHandler(node.NodeId))
                {
                    await builders[ii].Dispatcher.NotifyMonitoredItemDeletedAsync(
                        context,
                        node,
                        monitoredItem,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

        }

        /// <inheritdoc/>
        protected override async ValueTask OnMonitoringModeChangedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken = default)
        {
            await base.OnMonitoringModeChangedAsync(
                context,
                handle,
                monitoredItem,
                previousMode,
                monitoringMode,
                cancellationToken).ConfigureAwait(false);

            if (handle?.Node is not { } node)
            {
                return;
            }

            NodeManagerBuilder[] builders = GetAttachedBuilders();
            for (int ii = builders.Length - 1; ii >= 0; ii--)
            {
                if (builders[ii].HasMonitoringModeChangedHandler(node.NodeId))
                {
                    await builders[ii].Dispatcher.NotifyMonitoringModeChangedAsync(
                        context,
                        node,
                        monitoredItem,
                        previousMode,
                        monitoringMode,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
            }

        }

        /// <inheritdoc/>
        protected override async ValueTask OnMonitoredItemAttachedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            await base.OnMonitoredItemAttachedAsync(
                context,
                handle,
                monitoredItem,
                cancellationToken).ConfigureAwait(false);

            if (handle?.Node is { } source)
            {
                await MonitoredSources.OnCreatedAsync(
                    context,
                    source,
                    monitoredItem).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnMonitoredItemDetachedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            await base.OnMonitoredItemDetachedAsync(
                context,
                handle,
                monitoredItem,
                cancellationToken).ConfigureAwait(false);

            if (handle?.Node is { } source)
            {
                await MonitoredSources.OnDeletedAsync(
                    context,
                    source,
                    monitoredItem).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnCreateMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            await base.OnCreateMonitoredItemsCompleteAsync(
                context,
                monitoredItems,
                cancellationToken).ConfigureAwait(false);

            foreach (IMonitoredItem item in monitoredItems)
            {
                if (item is ISampledDataChangeMonitoredItem sampled &&
                    sampled.ManagerHandle is NodeHandle { Node: { } source })
                {
                    await MonitoredSources.OnCreatedAsync(
                        context,
                        source,
                        sampled).ConfigureAwait(false);
                }
            }

            ArrayOf<IMonitoredItem> snapshot =
                new List<IMonitoredItem>(monitoredItems);
            foreach (NodeManagerBuilder builder in GetAttachedBuilders())
            {
                await builder.Dispatcher.NotifyMonitoredItemsCreatedAsync(
                    context,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnDeleteMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            await base.OnDeleteMonitoredItemsCompleteAsync(
                context,
                monitoredItems,
                cancellationToken).ConfigureAwait(false);

            foreach (IMonitoredItem item in monitoredItems)
            {
                if (item is ISampledDataChangeMonitoredItem sampled &&
                    sampled.ManagerHandle is NodeHandle { Node: { } source })
                {
                    await MonitoredSources.OnDeletedAsync(
                        context,
                        source,
                        sampled).ConfigureAwait(false);
                }
            }

            ArrayOf<IMonitoredItem> snapshot =
                new List<IMonitoredItem>(monitoredItems);
            foreach (NodeManagerBuilder builder in GetAttachedBuilders())
            {
                await builder.Dispatcher.NotifyMonitoredItemsDeletedAsync(
                    context,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnModifyMonitoredItemsCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            await base.OnModifyMonitoredItemsCompleteAsync(
                context,
                monitoredItems,
                cancellationToken).ConfigureAwait(false);

            foreach (IMonitoredItem item in monitoredItems)
            {
                if (item is ISampledDataChangeMonitoredItem sampled &&
                    sampled.ManagerHandle is NodeHandle { Node: { } source })
                {
                    await MonitoredSources.OnModifiedAsync(
                        context,
                        source,
                        sampled).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask OnSetMonitoringModeCompleteAsync(
            ServerSystemContext context,
            IList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            await base.OnSetMonitoringModeCompleteAsync(
                context,
                monitoredItems,
                cancellationToken).ConfigureAwait(false);

            foreach (IMonitoredItem item in monitoredItems)
            {
                if (item is ISampledDataChangeMonitoredItem sampled &&
                    sampled.ManagerHandle is NodeHandle { Node: { } source })
                {
                    await MonitoredSources.OnMonitoringModeChangedAsync(
                        context,
                        source,
                        sampled,
                        sampled.MonitoringMode).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Cancels every running iterator and waits (bounded by each
        /// source's
        /// <see cref="EventPublishOptions.CancellationTimeout"/>) before
        /// invoking the base disposer. Subclasses that further override
        /// <c>Dispose</c> must call <c>base.Dispose(disposing)</c>.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MonitoredSources.Dispose();
                Simulations.Dispose();
                EventSources.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Internal trampoline used by <see cref="EventSourceRegistry"/>
        /// to register a notifier as a root notifier from a background
        /// worker thread. Wraps
        /// <see cref="AsyncCustomNodeManager.AddRootNotifierAsync"/>
        /// so the registry does not have to know its protected
        /// signature.
        /// </summary>
        internal Task AddRootNotifierFromFluentAsync(
            NodeState notifier,
            CancellationToken cancellationToken)
        {
            return AddRootNotifierAsync(notifier, cancellationToken).AsTask();
        }

        private NodeManagerBuilder[] GetAttachedBuilders()
        {
            lock (m_attachedBuildersLock)
            {
                return [.. m_attachedBuilders];
            }
        }

        private readonly Lock m_attachedBuildersLock = new();
        private readonly List<NodeManagerBuilder> m_attachedBuilders = [];
    }
}
