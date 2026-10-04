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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    internal static class FluentNodeRegistration
    {
        /// <summary>
        /// Resolves an unsealed owning builder for registering handlers on an existing node.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static NodeBuilder GetHandlerBuilder(INodeManagerBuilder builder, NodeState node)
        {
            NodeManagerBuilder owner = builder as NodeManagerBuilder ??
                FluentNodeManagerBase.TryResolveAttachedBuilder(builder) ??
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "The node manager does not expose a fluent handler dispatcher.");
            owner.ThrowIfSealed();
            return new NodeBuilder(owner, node);
        }

        /// <summary>
        /// Rejects further graph authoring when the resolved fluent owner has already been sealed.
        /// </summary>
        internal static void EnsureGraphAuthoringOpen(INodeManagerBuilder builder)
        {
            NodeManagerBuilder? owner = builder as NodeManagerBuilder ??
                FluentNodeManagerBase.TryResolveAttachedBuilder(builder);
            owner?.ThrowIfSealed();
        }

        /// <summary>
        /// Mints the NodeId for a node the fluent surface just created.
        /// </summary>
        /// <remarks>
        /// Routed through the owning NodeManager so that a fluent graph
        /// obeys the same <see cref="NodeIdAssignmentMode"/> as the rest of
        /// the manager, instead of the ambiguous
        /// <c>{parentIdentifier}_{browseName}</c> concatenation the fluent
        /// builders used to each spell out for themselves. The node is
        /// created here and there is nothing to preserve, so the NodeId is
        /// cleared first to say so.
        /// </remarks>
        /// <param name="builder">The builder that owns the node.</param>
        /// <param name="node">The freshly created node.</param>
        internal static void AssignNodeId(
            INodeManagerBuilder builder,
            NodeState node)
        {
            node.NodeId = NodeId.Null;
            node.NodeId = builder.NodeManager.New(builder.Context, node);
        }

        internal static void RegisterCreatedNode(
            INodeManagerBuilder builder,
            NodeState node)
        {
            builder.NodeManager.AddNode(node);
        }

        /// <summary>
        /// Promotes the notifier chain above an alarm source and registers the topmost
        /// object as a root notifier.
        /// </summary>
        /// <returns>
        /// An ownership reference that keeps shared notifier flags and browse links
        /// alive until the last alarm releases them. Pre-existing state is retained.
        /// </returns>
        internal static AlarmEventSourceRegistration RegisterAlarmEventSource(
            INodeManagerBuilder builder,
            NodeState source)
        {
            var chain = new List<BaseObjectState>();
            for (NodeState? current = source; current != null;)
            {
                if (current is BaseObjectState notifier)
                {
                    chain.Add(notifier);
                }

                current = current is BaseInstanceState instance ? instance.Parent : null;
            }

            AlarmNotifierOwnership ownership = s_notifierOwnership.GetValue(
                builder.NodeManager, static manager => new AlarmNotifierOwnership(manager));
            return ownership.Register(chain);
        }

        private static readonly ConditionalWeakTable<IAsyncNodeManager, AlarmNotifierOwnership> s_notifierOwnership =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif
    }

    /// <summary>
    /// Records what registering an alarm event source changed in the address space.
    /// </summary>
    internal sealed class AlarmEventSourceRegistration : IAsyncDisposable
    {
        public AlarmEventSourceRegistration(
            AlarmNotifierOwnership ownership,
            List<BaseObjectState> chain)
        {
            m_ownership = ownership;
            m_chain = chain;
        }

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref m_released, 1) == 0
                ? m_ownership.ReleaseAsync(m_chain)
                : default;
        }

        private readonly AlarmNotifierOwnership m_ownership;
        private readonly List<BaseObjectState> m_chain;
        private int m_released;
    }

    internal sealed class AlarmNotifierOwnership
    {
        public AlarmNotifierOwnership(IAsyncNodeManager manager)
        {
            m_manager = manager;
        }

        public AlarmEventSourceRegistration Register(List<BaseObjectState> chain)
        {
            lock (m_lock)
            {
                foreach (BaseObjectState node in chain)
                {
                    if (m_nodes.TryGetValue(node.NodeId, out NodeOwnership? prior) &&
                        (prior.Retiring || !ReferenceEquals(prior.Node, node)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The alarm notifier generation is being released.");
                    }
                }
                foreach (BaseObjectState node in chain)
                {
                    if (!m_nodes.TryGetValue(node.NodeId, out NodeOwnership? state))
                    {
                        state = new NodeOwnership(node);
                        m_nodes.Add(node.NodeId, state);
                        node.EventNotifier |= EventNotifiers.SubscribeToEvents;
                    }
                    state.References++;
                }
                for (int i = 1; i < chain.Count; i++)
                {
                    BaseObjectState parent = chain[i];
                    BaseObjectState child = chain[i - 1];
                    (NodeId, NodeId) key = (parent.NodeId, child.NodeId);
                    if (!m_links.TryGetValue(key, out LinkOwnership? link))
                    {
                        link = new LinkOwnership(parent, child);
                        m_links.Add(key, link);
                        // Only browsing edges: AddNotifier would add a second event-delivery route.
                        parent.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, false, child.NodeId);
                        child.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, parent.NodeId);
                    }
                    link.References++;
                }
                if (chain.Count != 0)
                {
                    NodeOwnership root = m_nodes[chain[^1].NodeId];
                    if (root.RootReferences++ == 0)
                    {
                        root.RootOwned = m_manager is AsyncCustomNodeManager manager &&
                            !manager.IsRootNotifier(root.Node.NodeId);
                        m_manager.AddRootNotifier(root.Node);
                    }
                }
            }
            return new AlarmEventSourceRegistration(this, chain);
        }

        public async ValueTask ReleaseAsync(List<BaseObjectState> chain)
        {
            NodeOwnership? retiringRoot = null;
            lock (m_lock)
            {
                if (chain.Count != 0)
                {
                    NodeOwnership root = m_nodes[chain[^1].NodeId];
                    if (--root.RootReferences == 0 && root.RootOwned)
                    {
                        root.Retiring = true;
                        retiringRoot = root;
                    }
                }
            }
            try
            {
                if (retiringRoot != null && m_manager is AsyncCustomNodeManager manager)
                {
                    await manager.RemoveAlarmRootNotifierAsync(retiringRoot.Node).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (m_lock)
                {
                    for (int i = 1; i < chain.Count; i++)
                    {
                        (NodeId, NodeId) key = (chain[i].NodeId, chain[i - 1].NodeId);
                        LinkOwnership link = m_links[key];
                        if (--link.References == 0)
                        {
                            if (link.OwnForward)
                            {
                                chain[i].RemoveReference(ReferenceTypeIds.HasNotifier, false, chain[i - 1].NodeId);
                            }
                            if (link.OwnInverse)
                            {
                                chain[i - 1].RemoveReference(ReferenceTypeIds.HasNotifier, true, chain[i].NodeId);
                            }
                            m_links.Remove(key);
                        }
                    }
                    foreach (BaseObjectState node in chain)
                    {
                        NodeOwnership state = m_nodes[node.NodeId];
                        if (--state.References == 0)
                        {
                            if (state.Promoted)
                            {
                                node.EventNotifier = (byte)(node.EventNotifier &
                                    unchecked((byte)~EventNotifiers.SubscribeToEvents));
                            }
                            m_nodes.Remove(node.NodeId);
                        }
                    }
                }
            }
        }

        private sealed class NodeOwnership
        {
            public NodeOwnership(BaseObjectState node)
            {
                Node = node;
                Promoted = (node.EventNotifier & EventNotifiers.SubscribeToEvents) == 0;
            }

            public BaseObjectState Node { get; }
            public bool Promoted { get; }
            public int References { get; set; }
            public int RootReferences { get; set; }
            public bool RootOwned { get; set; }
            public bool Retiring { get; set; }
        }

        private sealed class LinkOwnership
        {
            public LinkOwnership(BaseObjectState parent, BaseObjectState child)
            {
                OwnForward = !parent.ReferenceExists(ReferenceTypeIds.HasNotifier, false, child.NodeId);
                OwnInverse = !child.ReferenceExists(ReferenceTypeIds.HasNotifier, true, parent.NodeId);
            }

            public bool OwnForward { get; }
            public bool OwnInverse { get; }
            public int References { get; set; }
        }

        private readonly IAsyncNodeManager m_manager;
        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, NodeOwnership> m_nodes = [];
        private readonly Dictionary<(NodeId Parent, NodeId Child), LinkOwnership> m_links = [];
    }
}
