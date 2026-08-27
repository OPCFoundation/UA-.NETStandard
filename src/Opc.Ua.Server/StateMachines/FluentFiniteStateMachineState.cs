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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// A generic concrete <see cref="FiniteStateMachineState"/> whose
    /// state, transition and cause tables are sourced from a mutable
    /// definition holder managed by the unified
    /// <see cref="StateMachineBuilder{TState}"/>. Construct via
    /// <see cref="StateMachineBuilder.Create"/>:
    /// </summary>
    /// <remarks>
    /// <code language="csharp">
    /// FluentFiniteStateMachineState sm = StateMachineBuilder
    ///     .Create(parent, context, nodeId, browseName)
    ///     .AddState(1, "Off", isInitial: true)
    ///     .AddState(2, "On")
    ///     .AddTransition(10, "OffToOn", from: 1, to: 2)
    ///     .OnCause(causeId: 100, from: 1, transition: 10)
    ///     .WithInitialState(1)
    ///     .StateMachine;
    /// </code>
    /// <para>
    /// Vendors who need additional properties or methods on top of the
    /// standard finite state-machine surface can subclass
    /// <see cref="FluentFiniteStateMachineState"/> and use the
    /// (protected) <see cref="MutableDefinition"/> property to feed
    /// the same builder-driven tables.
    /// </para>
    /// </remarks>
    public class FluentFiniteStateMachineState : FiniteStateMachineState
    {
        /// <summary>
        /// Cached projections of the mutable holder. Invalidated by
        /// version counter.
        /// </summary>
        private ElementInfo[]? m_stateTable;
        private ElementInfo[]? m_transitionTable;
        private uint[,]? m_transitionMappings;
        private uint[,]? m_causeMappings;
        private int m_cacheVersion = -1;

        /// <summary>
        /// The materialized <c>StateType</c> nodes by numeric id.
        /// Populated once by <see cref="MaterializeStateNodes"/>; stays
        /// <c>null</c> until then, in which case the base (numeric)
        /// element-NodeId convention applies. The reverse direction is
        /// resolved by scanning the nodes' <em>live</em> NodeIds — no
        /// reverse map is kept, so a NodeId reassigned after
        /// materialization (e.g. by a node manager's per-instance
        /// NodeId rebase during registration) still resolves.
        /// </summary>
        private Dictionary<uint, BaseObjectState>? m_stateNodesById;

        /// <summary>
        /// The materialized <c>TransitionType</c> nodes by numeric id.
        /// See <see cref="MaterializeTransitionNodes"/>.
        /// </summary>
        private Dictionary<uint, BaseObjectState>? m_transitionNodesById;

        /// <summary>
        /// When <c>true</c>, the state machine rejects all incoming
        /// transitions and cause invocations with
        /// <see cref="StatusCodes.BadInvalidState"/>. Used to model
        /// the inactive state of a sub-state-machine whose parent
        /// has exited the attached state. Set by
        /// <see cref="StateMachineBuilder{TState}.WithSubStateMachine"/>
        /// lifecycle hooks.
        /// </summary>
        public bool IsSuspended { get; set; }

        /// <inheritdoc/>
        public override bool IsCausePermitted(
            ISystemContext context,
            uint causeId,
            bool checkUserAccessRights)
        {
            if (IsSuspended)
            {
                return false;
            }
            return base.IsCausePermitted(context, causeId, checkUserAccessRights);
        }

        /// <inheritdoc/>
        public override ServiceResult DoCause(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            System.Collections.Generic.List<Variant> outputArguments)
        {
            if (IsSuspended)
            {
                return StatusCodes.BadInvalidState;
            }
            return base.DoCause(context, causeMethod, causeId, inputArguments, outputArguments);
        }

        /// <inheritdoc/>
        public override ServiceResult DoTransition(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            System.Collections.Generic.List<Variant> outputArguments)
        {
            if (IsSuspended)
            {
                return StatusCodes.BadInvalidState;
            }
            return base.DoTransition(context, transitionId, causeId, inputArguments, outputArguments);
        }

        /// <summary>
        /// Initializes a new state machine instance from the given
        /// immutable definition. This overload exists for callers that
        /// construct definitions directly; the recommended path is the
        /// unified <see cref="StateMachineBuilder"/>.
        /// </summary>
        /// <param name="parent">The parent node (may be <c>null</c>).</param>
        /// <param name="definition">The fluent definition snapshot.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="definition"/> is <c>null</c>.
        /// </exception>
        public FluentFiniteStateMachineState(
            NodeState parent,
            StateMachineDefinition definition)
            : this(parent, FromSnapshot(definition), useHolder: true)
        {
        }

        /// <summary>
        /// Internal factory used by the unified builder so it can mutate
        /// the definition holder incrementally.
        /// </summary>
        internal static FluentFiniteStateMachineState CreateWithHolder(
            NodeState parent,
            MutableStateMachineDefinition holder)
        {
            return new FluentFiniteStateMachineState(parent, holder, useHolder: true);
        }

        private FluentFiniteStateMachineState(
            NodeState parent,
            MutableStateMachineDefinition holder,
            bool useHolder)
            : base(parent)
        {
            _ = useHolder;
            MutableDefinition = holder ?? throw new ArgumentNullException(nameof(holder));
        }

        /// <summary>
        /// The mutable definition holder backing this state machine.
        /// Visible to assembly-internal callers (the unified
        /// <see cref="StateMachineBuilder{TState}"/>) so they can
        /// populate the definition incrementally.
        /// </summary>
        internal MutableStateMachineDefinition MutableDefinition { get; }

        /// <summary>
        /// The immutable definition snapshot this state machine reads
        /// from. Allocates a fresh snapshot on each access — use
        /// sparingly (for introspection / testing).
        /// </summary>
        public StateMachineDefinition Definition => MutableDefinition.Snapshot();

        /// <inheritdoc/>
        protected override string ElementNamespaceUri
            => MutableDefinition.ElementNamespaceUri;

        /// <inheritdoc/>
        protected override ElementInfo[]? StateTable
        {
            get
            {
                RefreshCache();
                return m_stateTable;
            }
        }

        /// <inheritdoc/>
        protected override ElementInfo[]? TransitionTable
        {
            get
            {
                RefreshCache();
                return m_transitionTable;
            }
        }

        /// <inheritdoc/>
        protected override uint[,]? TransitionMappings
        {
            get
            {
                RefreshCache();
                return m_transitionMappings;
            }
        }

        /// <inheritdoc/>
        protected override uint[,]? CauseMappings
        {
            get
            {
                RefreshCache();
                return m_causeMappings;
            }
        }

        /// <summary>
        /// Materializes the state and transition nodes once the base has
        /// resolved <c>ElementNamespaceIndex</c>. The definition is
        /// always frozen by this point — the builder freezes before it
        /// creates the node, and the snapshot constructor freezes on
        /// construction.
        /// </summary>
        protected override void OnAfterCreate(
            ISystemContext context,
            NodeState node,
            System.Threading.CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);
            MaterializeStateNodes(context);
            MaterializeTransitionNodes(context);
        }

        /// <summary>
        /// Materializes one <c>StateType</c> node per declared state as
        /// a <c>HasComponent</c> child of this state machine, each with
        /// a <c>StateNumber</c> property, and publishes them through the
        /// optional <c>AvailableStates</c> variable.
        /// </summary>
        /// <remarks>
        /// Part 16 §B.3 hangs <c>HasSubStateMachine</c> off the parent
        /// state node, and Part 5 has <c>CurrentState/Id</c> point at
        /// that same node — neither is expressible while the states
        /// exist only as <c>ElementInfo</c> table rows. Nodes are
        /// per-instance so that several machines built from one
        /// definition can coexist in a single address space; the
        /// <see cref="FiniteStateMachineState.GetStateNodeId"/> /
        /// <see cref="FiniteStateMachineState.GetStateId"/> overrides
        /// below keep <c>CurrentState/Id</c> in step.
        /// <para>
        /// Idempotent — driven from <see cref="OnAfterCreate"/>, so a
        /// machine that is never created keeps the numeric convention.
        /// </para>
        /// </remarks>
        private void MaterializeStateNodes(ISystemContext context)
        {
            if (m_stateNodesById != null)
            {
                return;
            }

            ushort elementNamespaceIndex = ElementNodeIdNamespaceIndex;
            string ownerPrefix = ComposeOwnerPrefix();

            var nodesById = new Dictionary<uint, BaseObjectState>();

            foreach (StateMachineStateDefinition state in MutableDefinition.States)
            {
                nodesById[state.Id] = MaterializeElementNode(
                    ownerPrefix,
                    state.BrowseName,
                    ObjectTypeIds.StateType,
                    BrowseNames.StateNumber,
                    state.Id,
                    elementNamespaceIndex);
            }

            m_stateNodesById = nodesById;

            // The value is served live so it stays correct even if a
            // node manager reassigns the element NodeIds when the
            // machine is registered.
            AddAvailableStates(
                context,
                available =>
                {
                    available.Value = ReadLiveStateNodeIds();
                    available.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
                    {
                        value = Variant.From(ReadLiveStateNodeIds());
                        return ServiceResult.Good;
                    };
                },
                new NodeId(
                    ownerPrefix + "_" + BrowseNames.AvailableStates,
                    NodeId.NamespaceIndex));
        }

        /// <summary>
        /// Projects the state nodes' current NodeIds in declaration
        /// order.
        /// </summary>
        private ArrayOf<NodeId> ReadLiveStateNodeIds()
        {
            if (m_stateNodesById == null)
            {
                return default;
            }
            var nodeIds = new NodeId[MutableDefinition.States.Count];
            for (int ii = 0; ii < nodeIds.Length; ii++)
            {
                nodeIds[ii] =
                    m_stateNodesById[MutableDefinition.States[ii].Id].NodeId;
            }
            return ArrayOf.Wrapped(nodeIds);
        }

        /// <summary>
        /// Projects the transition nodes' current NodeIds in
        /// declaration order.
        /// </summary>
        private ArrayOf<NodeId> ReadLiveTransitionNodeIds()
        {
            if (m_transitionNodesById == null)
            {
                return default;
            }
            var nodeIds = new NodeId[MutableDefinition.Transitions.Count];
            for (int ii = 0; ii < nodeIds.Length; ii++)
            {
                nodeIds[ii] =
                    m_transitionNodesById[MutableDefinition.Transitions[ii].Id].NodeId;
            }
            return ArrayOf.Wrapped(nodeIds);
        }

        /// <summary>
        /// Materializes one <c>TransitionType</c> node per declared
        /// transition as a <c>HasComponent</c> child of this state
        /// machine, each with a <c>TransitionNumber</c> property and the
        /// Part 16 §B.4 <c>FromState</c> / <c>ToState</c> /
        /// <c>HasEffect</c> references, and publishes them through the
        /// optional <c>AvailableTransitions</c> variable.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="MaterializeStateNodes"/>, and
        /// what makes <c>LastTransition/Id</c> resolve to a node a
        /// client can browse. <c>HasCause</c> is added later, by
        /// <c>StateMachineBuilder.WithCause</c>, which is where the
        /// cause id is tied to a concrete method node.
        /// </remarks>
        private void MaterializeTransitionNodes(ISystemContext context)
        {
            if (m_transitionNodesById != null)
            {
                return;
            }

            ushort elementNamespaceIndex = ElementNodeIdNamespaceIndex;
            string ownerPrefix = ComposeOwnerPrefix();

            var nodesById = new Dictionary<uint, BaseObjectState>();

            foreach (StateMachineTransitionDefinition transition
                in MutableDefinition.Transitions)
            {
                BaseObjectState transitionNode = MaterializeElementNode(
                    ownerPrefix,
                    transition.BrowseName,
                    ObjectTypeIds.TransitionType,
                    BrowseNames.TransitionNumber,
                    transition.Id,
                    elementNamespaceIndex);

                AddTransitionEndpointReference(
                    transitionNode, ReferenceTypeIds.FromState, transition.FromStateId);
                AddTransitionEndpointReference(
                    transitionNode, ReferenceTypeIds.ToState, transition.ToStateId);

                if (transition.HasEffect)
                {
                    // Part 16 §B.4: HasEffect names the event type the
                    // transition causes the machine to report.
                    transitionNode.AddReference(
                        ReferenceTypeIds.HasEffect,
                        false,
                        ObjectTypeIds.TransitionEventType);
                }

                nodesById[transition.Id] = transitionNode;
            }

            m_transitionNodesById = nodesById;

            AddAvailableTransitions(
                context,
                available =>
                {
                    available.Value = ReadLiveTransitionNodeIds();
                    available.OnSimpleReadValue = (ISystemContext ctx, NodeState node, ref Variant value) =>
                    {
                        value = Variant.From(ReadLiveTransitionNodeIds());
                        return ServiceResult.Good;
                    };
                },
                new NodeId(
                    ownerPrefix + "_" + BrowseNames.AvailableTransitions,
                    NodeId.NamespaceIndex));

            // LastTransition is Optional on FiniteStateMachineType and
            // is not created by default, which would leave the base
            // class's UpdateTransitionVariable call a no-op and give
            // clients nothing to subscribe to. Materialize it alongside
            // the transitions it names.
            if (MutableDefinition.Transitions.Count > 0)
            {
                AddLastTransition(
                    context,
                    new NodeId(
                        ownerPrefix + "_" + BrowseNames.LastTransition,
                        NodeId.NamespaceIndex));
            }
        }

        /// <summary>
        /// Creates one materialized element node — the shape shared by
        /// states and transitions: a <c>HasComponent</c>
        /// <see cref="BaseObjectState"/> child carrying a numeric
        /// element property, with a NodeId derived from the machine's
        /// own identifier and the element's browse name.
        /// </summary>
        private BaseObjectState MaterializeElementNode(
            string ownerPrefix,
            string browseName,
            NodeId typeDefinitionId,
            string numberBrowseName,
            uint number,
            ushort namespaceIndex)
        {
            var nodeId = new NodeId(ownerPrefix + "_" + browseName, namespaceIndex);

            var node = new BaseObjectState(this)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = typeDefinitionId,
                SymbolicName = browseName,
                NodeId = nodeId,
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText(browseName)
            };

            PropertyState numberProperty = node
                .AddProperty<uint, VariantBuilder>(
                    numberBrowseName,
                    DataTypeIds.UInt32,
                    ValueRanks.Scalar);
            numberProperty.NodeId = new NodeId(
                ownerPrefix + "_" + browseName + "_" + numberBrowseName,
                namespaceIndex);
            ((PropertyState<uint>)numberProperty).Value = number;

            AddChild(node);
            return node;
        }

        /// <summary>
        /// The stable string prefix all of this machine's materialized
        /// element NodeIds share — the machine's own <em>full</em>
        /// NodeId string (namespace and id type included, so two
        /// machines whose identifiers happen to coincide in different
        /// namespaces cannot mint colliding element ids), or one random
        /// prefix for a machine created without a NodeId.
        /// </summary>
        private string ComposeOwnerPrefix()
        {
            return m_ownerPrefix ??= ComposeOwnerPrefix(NodeId);
        }

        private static string ComposeOwnerPrefix(NodeId ownerNodeId)
        {
            return ownerNodeId.IsNull
                ? Guid.NewGuid().ToString()
                : ownerNodeId.ToString();
        }

        private string? m_ownerPrefix;

        /// <summary>
        /// Links a transition node to one of its endpoint states, in
        /// both directions — so a client can inverse-browse
        /// <c>FromState</c> / <c>ToState</c> from a state node to find
        /// its transitions. Silently skips endpoints the definition
        /// does not declare — <c>ValidateDefinition</c> already rejects
        /// those, and a definition built by hand should not throw from
        /// create.
        /// </summary>
        private void AddTransitionEndpointReference(
            BaseObjectState transitionNode,
            NodeId referenceTypeId,
            uint stateId)
        {
            BaseObjectState? stateNode = FindStateNode(stateId);
            if (stateNode != null)
            {
                transitionNode.AddReference(referenceTypeId, false, stateNode.NodeId);
                stateNode.AddReference(referenceTypeId, true, transitionNode.NodeId);
            }
        }

        /// <summary>
        /// Records a <c>HasCause</c> reference from every transition the
        /// cause can trigger to the method node that carries it, plus
        /// the inverse on the method so inverse-browsing
        /// <c>HasCause</c> from the method finds the transitions.
        /// Invoked by <c>StateMachineBuilder.WithCause</c>, the only
        /// place where a numeric cause id is bound to a real method.
        /// </summary>
        internal void AddCauseReferences(uint causeId, MethodState causeMethod)
        {
            if (m_transitionNodesById == null || causeMethod.NodeId.IsNull)
            {
                return;
            }

            NodeId methodNodeId = causeMethod.NodeId;
            foreach (StateMachineCauseMapping mapping in MutableDefinition.CauseMappings)
            {
                // The ReferenceExists check keeps repeated WithCause
                // calls for the same method from stacking duplicates.
                if (mapping.CauseId == causeId &&
                    m_transitionNodesById.TryGetValue(
                        mapping.TransitionId, out BaseObjectState? transitionNode) &&
                    !transitionNode.ReferenceExists(
                        ReferenceTypeIds.HasCause, false, methodNodeId))
                {
                    transitionNode.AddReference(
                        ReferenceTypeIds.HasCause, false, methodNodeId);
                    causeMethod.AddReference(
                        ReferenceTypeIds.HasCause, true, transitionNode.NodeId);
                }
            }
        }

        /// <summary>
        /// Returns the materialized <c>StateType</c> node for the given
        /// state, or <c>null</c> when the states have not been
        /// materialized or the id is unknown.
        /// </summary>
        internal BaseObjectState? FindStateNode(uint stateId)
        {
            if (m_stateNodesById != null &&
                m_stateNodesById.TryGetValue(stateId, out BaseObjectState? node))
            {
                return node;
            }
            return null;
        }

        /// <inheritdoc/>
        public override NodeId GetStateNodeId(uint stateId)
        {
            if (m_stateNodesById != null)
            {
                // Once nodes are materialized the map is authoritative —
                // the numeric convention would mint a NodeId pointing
                // at nothing (or at an unrelated standard node).
                return m_stateNodesById.TryGetValue(stateId, out BaseObjectState? node)
                    ? node.NodeId
                    : NodeId.Null;
            }
            return base.GetStateNodeId(stateId);
        }

        /// <inheritdoc/>
        public override uint GetStateId(NodeId stateNodeId)
        {
            if (m_stateNodesById != null)
            {
                // Authoritative once materialized — falling back to the
                // base numeric convention would let any numeric NodeId
                // in the element namespace resolve to a state this
                // machine does not have. Resolved against the nodes'
                // LIVE NodeIds (not a snapshot map), so ids reassigned
                // by a node manager during registration keep resolving.
                return FindElementId(m_stateNodesById, stateNodeId);
            }
            return base.GetStateId(stateNodeId);
        }

        /// <inheritdoc/>
        public override NodeId GetTransitionNodeId(uint transitionId)
        {
            if (m_transitionNodesById != null)
            {
                // Authoritative once materialized — see GetStateNodeId.
                return m_transitionNodesById.TryGetValue(
                    transitionId, out BaseObjectState? node)
                    ? node.NodeId
                    : NodeId.Null;
            }
            return base.GetTransitionNodeId(transitionId);
        }

        /// <inheritdoc/>
        public override uint GetTransitionId(NodeId transitionNodeId)
        {
            if (m_transitionNodesById != null)
            {
                // Authoritative once materialized — see GetStateId.
                return FindElementId(m_transitionNodesById, transitionNodeId);
            }
            return base.GetTransitionId(transitionNodeId);
        }

        /// <summary>
        /// Reverse-resolves an element NodeId by scanning the
        /// materialized nodes' current NodeIds. Linear in the element
        /// count (small by construction), allocation-free, and — unlike
        /// a snapshot dictionary keyed by NodeId — correct even after a
        /// node manager reassigns the element NodeIds.
        /// </summary>
        private static uint FindElementId(
            Dictionary<uint, BaseObjectState> nodesById,
            NodeId elementNodeId)
        {
            if (elementNodeId.IsNull)
            {
                return 0;
            }
            foreach (KeyValuePair<uint, BaseObjectState> entry in nodesById)
            {
                if (entry.Value.NodeId == elementNodeId)
                {
                    return entry.Key;
                }
            }
            return 0;
        }

        /// <summary>
        /// The namespace index that qualifies materialized state and
        /// transition NodeIds. The element namespace defaults to the
        /// OPC UA namespace (index 0), which must never host vendor
        /// nodes — so the resolved element namespace is honoured only
        /// when it is a real, registered non-default namespace, and the
        /// machine's own namespace is used otherwise. Keying off the
        /// resolved index (rather than off whether
        /// <c>UseElementNamespace</c> was called) also keeps a machine
        /// rebuilt from a <see cref="Definition"/> snapshot consistent
        /// with the machine the snapshot was taken from.
        /// </summary>
        private ushort ElementNodeIdNamespaceIndex
            => ElementNamespaceIndex != 0
                ? ElementNamespaceIndex
                : NodeId.NamespaceIndex;

        /// <summary>
        /// Derives a deterministic, per-instance NodeId for a state
        /// machine element from the owning node's full NodeId string
        /// and the element's name, so repeated builds produce stable
        /// ids and two machines cannot mint colliding element ids —
        /// not even machines with the same identifier in different
        /// namespaces. (A machine with no NodeId gets a random prefix,
        /// which is stable within one build but not across builds.)
        /// </summary>
        internal static NodeId ComposeElementNodeId(
            NodeId ownerNodeId,
            string elementName,
            ushort namespaceIndex)
        {
            return new NodeId(
                ComposeOwnerPrefix(ownerNodeId) + "_" + elementName,
                namespaceIndex);
        }

        private void RefreshCache()
        {
            int currentVersion = MutableDefinition.Version;
            if (m_cacheVersion == currentVersion)
            {
                return;
            }

            int stateCount = MutableDefinition.States.Count;
            var stateTable = new ElementInfo[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                StateMachineStateDefinition s = MutableDefinition.States[i];
                stateTable[i] = new ElementInfo(s.Id, s.BrowseName, s.Id);
            }

            int transitionCount = MutableDefinition.Transitions.Count;
            var transitionTable = new ElementInfo[transitionCount];
            uint[,] transitionMappings = new uint[transitionCount, 4];
            for (int i = 0; i < transitionCount; i++)
            {
                StateMachineTransitionDefinition t = MutableDefinition.Transitions[i];
                transitionTable[i] = new ElementInfo(t.Id, t.BrowseName, t.Id);
                transitionMappings[i, 0] = t.Id;
                transitionMappings[i, 1] = t.FromStateId;
                transitionMappings[i, 2] = t.ToStateId;
                transitionMappings[i, 3] = t.HasEffect ? 1u : 0u;
            }

            int causeCount = MutableDefinition.CauseMappings.Count;
            uint[,] causeMappings = new uint[causeCount, 3];
            for (int i = 0; i < causeCount; i++)
            {
                StateMachineCauseMapping c = MutableDefinition.CauseMappings[i];
                causeMappings[i, 0] = c.CauseId;
                causeMappings[i, 1] = c.FromStateId;
                causeMappings[i, 2] = c.TransitionId;
            }

            m_stateTable = stateTable;
            m_transitionTable = transitionTable;
            m_transitionMappings = transitionMappings;
            m_causeMappings = causeMappings;
            m_cacheVersion = currentVersion;
        }

        private static MutableStateMachineDefinition FromSnapshot(
            StateMachineDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var holder = new MutableStateMachineDefinition
            {
                InitialStateId = definition.InitialStateId,
                ElementNamespaceUri = definition.ElementNamespaceUri
            };
            holder.States.AddRange(definition.States);
            holder.Transitions.AddRange(definition.Transitions);
            holder.CauseMappings.AddRange(definition.CauseMappings);
            holder.Version = 1;
            holder.Frozen = true;
            return holder;
        }
    }
}
