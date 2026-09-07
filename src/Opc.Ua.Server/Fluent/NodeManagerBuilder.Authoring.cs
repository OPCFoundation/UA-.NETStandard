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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Node creation half of the fluent builder. Nodes staged here are held
    /// in an authored-roots list until
    /// <see cref="RegisterAuthoredNodesAsync"/> hands them to the manager,
    /// which happens after the user's <c>Configure</c> delegates return and
    /// before the builder is sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every staged node receives its final NodeId from the manager's
    /// <see cref="INodeIdFactory"/> before the returned per-node builder is
    /// handed back, so <c>OnRead</c>/<c>OnWrite</c> registrations — which key
    /// off <see cref="NodeState.NodeId"/> — remain valid once the node is
    /// registered.
    /// </para>
    /// <para>
    /// Staging rather than registering immediately keeps the fluent surface
    /// usable from a <c>Configure</c> partial that runs before the manager
    /// has finished building its address space, and lets a node reference an
    /// authored sibling that has not been registered yet.
    /// </para>
    /// </remarks>
    public sealed partial class NodeManagerBuilder
    {
        /// <inheritdoc/>
        public INodeBuilder<TState> Add<TState>(TState node, NodeId parentId = default)
            where TState : NodeState
        {
            return AddNode(node, parentId, attachDefaultParent: true);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> Add<TState>(
            Func<NodeState?, TState> factory,
            NodeId parentId = default)
            where TState : NodeState
        {
            ThrowIfSealed();
            ThrowIfAuthoredNodesRegistered();
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            NodeId effectiveParentId = parentId.IsNull
                ? ObjectIds.ObjectsFolder
                : parentId;
            NodeState? parent = ResolveAuthoredOrRegisteredNode(effectiveParentId);
            if (parent is null && IsOwnedNamespace(effectiveParentId.NamespaceIndex))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Parent '{0}' belongs to this node manager but has not been added.",
                    effectiveParentId);
            }

            // The factory is handed an identity-only proxy when the parent
            // lives in another node manager, so a NodeId factory that derives
            // child ids from the parent still sees the real parent identity.
            NodeState factoryParent = parent ?? new ExternalParentState(effectiveParentId);
            TState node = factory(factoryParent) ??
                throw new InvalidOperationException(
                    "The node factory returned null.");
            if (parent is null &&
                node is BaseInstanceState instance &&
                ReferenceEquals(instance.Parent, factoryParent))
            {
                // The proxy is a stand-in for identity only, so drop the
                // parent link the factory established. AddChild/RemoveChild
                // is the only public way to clear BaseInstanceState.Parent.
                factoryParent.AddChild(instance);
                factoryParent.RemoveChild(instance);
            }

            return AddNode(node, parentId, attachDefaultParent: true);
        }

        /// <inheritdoc/>
        public INodeBuilder<TState> AddRoot<TState>(TState node)
            where TState : NodeState
        {
            return AddNode(node, NodeId.Null, attachDefaultParent: false);
        }

        /// <inheritdoc/>
        public bool TryGetNode(NodeId nodeId, out NodeState? node)
        {
            if (nodeId.IsNull)
            {
                node = null;
                return false;
            }

            if (m_authoredNodes.TryGetValue(nodeId, out node))
            {
                return true;
            }

            node = m_nodeIdResolver(nodeId);
            return node is not null;
        }

        /// <inheritdoc/>
        public INodeBuilder<FolderState> AddFolder(
            string browseName,
            NodeId parentId = default)
        {
            return AddFolderCore(CreateDefaultBrowseName(browseName), parentId);
        }

        /// <inheritdoc/>
        public INodeBuilder<FolderState> AddFolder(
            QualifiedName browseName,
            NodeId parentId = default)
        {
            return AddFolderCore(ValidateExplicitBrowseName(browseName), parentId);
        }

        /// <inheritdoc/>
        public INodeBuilder<BaseObjectState> AddObject(
            string browseName,
            NodeId parentId = default,
            NodeId typeDefinitionId = default)
        {
            return AddObjectCore(
                CreateDefaultBrowseName(browseName),
                parentId,
                typeDefinitionId);
        }

        /// <inheritdoc/>
        public INodeBuilder<BaseObjectState> AddObject(
            QualifiedName browseName,
            NodeId parentId = default,
            NodeId typeDefinitionId = default)
        {
            return AddObjectCore(
                ValidateExplicitBrowseName(browseName),
                parentId,
                typeDefinitionId);
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> AddVariable<TValue>(
            string browseName,
            NodeId parentId = default)
        {
            return AddVariableCore<TValue>(
                CreateDefaultBrowseName(browseName),
                parentId);
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> AddVariable<TValue>(
            QualifiedName browseName,
            NodeId parentId = default)
        {
            return AddVariableCore<TValue>(
                ValidateExplicitBrowseName(browseName),
                parentId);
        }

        /// <inheritdoc/>
        public INodeBuilder<MethodState> AddMethod(
            string browseName,
            NodeId parentId = default)
        {
            return AddMethodCore(CreateDefaultBrowseName(browseName), parentId);
        }

        /// <inheritdoc/>
        public INodeBuilder<MethodState> AddMethod(
            QualifiedName browseName,
            NodeId parentId = default)
        {
            return AddMethodCore(ValidateExplicitBrowseName(browseName), parentId);
        }

        /// <summary>
        /// Hands every staged root to <paramref name="register"/> in the order
        /// it was added. Called once by the owning manager between the user's
        /// <c>Configure</c> delegates and <see cref="SealAsync"/>; a manager
        /// that staged no nodes registers nothing.
        /// </summary>
        /// <param name="register">
        /// Registers one root subtree with the manager; typically
        /// <c>AddPredefinedNodeAsync</c>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="register"/> is null.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> — the builder is sealed
        /// or the staged graph was already registered.
        /// </exception>
        public async ValueTask RegisterAuthoredNodesAsync(
            Func<NodeState, CancellationToken, ValueTask> register,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSealed();
            if (register is null)
            {
                throw new ArgumentNullException(nameof(register));
            }
            ThrowIfAuthoredNodesRegistered();

            m_authoredNodesRegistered = true;
            foreach (NodeState root in m_authoredRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await register(root, cancellationToken).ConfigureAwait(false);
            }
        }

        private NodeBuilder<TState> AddNode<TState>(
            TState node,
            NodeId parentId,
            bool attachDefaultParent)
            where TState : NodeState
        {
            ThrowIfSealed();
            ThrowIfAuthoredNodesRegistered();
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            AttachToParent(node, parentId, attachDefaultParent);
            PrepareNodeIds(node);
            IndexAuthoredSubtree(node);

            // A node hung off an already staged parent is registered with that
            // parent's subtree, so only unparented nodes become roots.
            if (!HasAuthoredAncestor(node))
            {
                AddAuthoredRoot(node);
            }

            return new NodeBuilder<TState>(this, node);
        }

        private NodeBuilder<FolderState> AddFolderCore(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var folder = new FolderState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            return AddNode(folder, parentId, attachDefaultParent: true);
        }

        private NodeBuilder<BaseObjectState> AddObjectCore(
            QualifiedName browseName,
            NodeId parentId,
            NodeId typeDefinitionId)
        {
            string symbolicName = browseName.Name!;
            var instance = new BaseObjectState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                TypeDefinitionId = typeDefinitionId.IsNull
                    ? ObjectTypeIds.BaseObjectType
                    : typeDefinitionId
            };
            return AddNode(instance, parentId, attachDefaultParent: true);
        }

        private VariableBuilder<TValue> AddVariableCore<TValue>(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var variable = new BaseDataVariableState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = TypeInfo.GetDataTypeId(typeof(TValue), Context.NamespaceUris),
                ValueRank = TypeInfo.GetValueRank(typeof(TValue)),
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
            NodeBuilder<BaseDataVariableState> nodeBuilder = AddNode(
                variable,
                parentId,
                attachDefaultParent: true);
            return ToVariableBuilder<TValue>(nodeBuilder.Node, browseName.ToString());
        }

        private NodeBuilder<MethodState> AddMethodCore(
            QualifiedName browseName,
            NodeId parentId)
        {
            string symbolicName = browseName.Name!;
            var method = new MethodState(null)
            {
                SymbolicName = symbolicName,
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                Executable = true,
                UserExecutable = true
            };
            return AddNode(method, parentId, attachDefaultParent: true);
        }

        /// <summary>
        /// Resolves a browse-path root against the staged roots first so a
        /// <c>Node("MyFolder/Child")</c> lookup can reach a node added earlier
        /// in the same <c>Configure</c> pass.
        /// </summary>
        private NodeState ResolveRoot(QualifiedName browseName)
        {
            foreach (NodeState root in m_authoredRoots)
            {
                if (root.BrowseName == browseName)
                {
                    return root;
                }
            }
            return m_rootResolver(browseName);
        }

        /// <summary>
        /// Merges the staged nodes matching <paramref name="predicate"/> into
        /// the resolver-supplied candidates so type- and DataType-keyed
        /// lookups also see nodes added in this pass.
        /// </summary>
        private IReadOnlyList<NodeState> CollectAuthoredCandidates(
            IReadOnlyList<NodeState> resolved,
            Func<NodeState, bool> predicate)
        {
            if (m_authoredNodes.Count == 0)
            {
                return resolved;
            }

            List<NodeState> candidates = MatchAuthoredNodes(predicate);
            for (int i = 0; i < resolved.Count; i++)
            {
                if (!ContainsByReference(candidates, resolved[i]))
                {
                    candidates.Add(resolved[i]);
                }
            }
            return candidates;
        }

        /// <inheritdoc cref="CollectAuthoredCandidates(IReadOnlyList{NodeState}, Func{NodeState, bool})"/>
        private List<NodeState> CollectAuthoredCandidates(
            ArrayOf<NodeState> resolved,
            Func<NodeState, bool> predicate)
        {
            List<NodeState> candidates = m_authoredNodes.Count == 0
                ? []
                : MatchAuthoredNodes(predicate);
            for (int i = 0; i < resolved.Count; i++)
            {
                if (!ContainsByReference(candidates, resolved[i]))
                {
                    candidates.Add(resolved[i]);
                }
            }
            return candidates;
        }

        private List<NodeState> MatchAuthoredNodes(Func<NodeState, bool> predicate)
        {
            var candidates = new List<NodeState>();
            foreach (NodeState authored in m_authoredNodes.Values)
            {
                if (predicate(authored))
                {
                    candidates.Add(authored);
                }
            }
            return candidates;
        }

        private static bool ContainsByReference(
            List<NodeState> candidates,
            NodeState candidate)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(candidates[i], candidate))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Links an instance to its parent, defaulting an unparented instance
        /// to the Objects folder. A parent owned by another node manager gets
        /// an inverse reference instead of a hard parent link.
        /// </summary>
        private void AttachToParent(
            NodeState node,
            NodeId parentId,
            bool attachDefaultParent)
        {
            if (node is not BaseInstanceState instance)
            {
                if (!parentId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeClassInvalid,
                        "Node '{0}' is not an instance and cannot be attached to parent '{1}'.",
                        node.BrowseName,
                        parentId);
                }
                return;
            }

            bool useDefaultReferenceType = instance.ReferenceTypeId.IsNull;
            NodeState? parent = instance.Parent;
            NodeId effectiveParentId = parentId;
            if (parent != null)
            {
                if (!parentId.IsNull && parent.NodeId != parentId)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Node '{0}' is already attached to parent '{1}', not '{2}'.",
                        node.BrowseName,
                        parent.NodeId,
                        parentId);
                }

                effectiveParentId = parent.NodeId;
                if (effectiveParentId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "The existing parent of node '{0}' has no NodeId.",
                        node.BrowseName);
                }

                bool knownParent = IsStagedNode(parent) ||
                    ReferenceEquals(m_nodeIdResolver(effectiveParentId), parent);
                if (!knownParent)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "The existing parent '{0}' of node '{1}' is not part of this graph or address space.",
                        effectiveParentId,
                        node.BrowseName);
                }
            }
            else
            {
                if (effectiveParentId.IsNull && attachDefaultParent)
                {
                    effectiveParentId = ObjectIds.ObjectsFolder;
                }

                if (!effectiveParentId.IsNull)
                {
                    parent = ResolveAuthoredOrRegisteredNode(effectiveParentId);
                }

                if (parent == null && IsOwnedNamespace(effectiveParentId.NamespaceIndex))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Parent '{0}' belongs to this node manager but has not been added.",
                        effectiveParentId);
                }
            }

            if (useDefaultReferenceType && !effectiveParentId.IsNull)
            {
                instance.ReferenceTypeId =
                    effectiveParentId == ObjectIds.ObjectsFolder || instance is FolderState
                        ? ReferenceTypeIds.Organizes
                        : ReferenceTypeIds.HasComponent;
            }

            if (parent != null)
            {
                AddChildIfMissing(parent, instance);
                return;
            }

            if (!effectiveParentId.IsNull)
            {
                instance.AddReferenceIfMissing(
                    instance.ReferenceTypeId,
                    true,
                    effectiveParentId);
            }
        }

        /// <summary>
        /// Assigns final NodeIds to the subtree so the returned builder can
        /// register id-keyed callbacks straight away.
        /// </summary>
        private void PrepareNodeIds(NodeState node)
        {
            if (node.BrowseName.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "A node added to the graph must have a browse name.");
            }
            if (NodeManager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node creation requires an AsyncCustomNodeManager-backed builder.");
            }

            manager.PrepareAuthoredNodeIdsForRegistration(node);
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The NodeId factory did not assign an id to node '{0}'.",
                    node.BrowseName);
            }
            ValidateAuthoredNodeId(manager, node);

            // The root pass above already covered the whole subtree, so the
            // descendants only need their namespace checked.
            var descendants = new List<BaseInstanceState>();
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context, descendants);
            for (int i = 0; i < descendants.Count; i++)
            {
                BaseInstanceState child = descendants[i];
                ValidateAuthoredNodeId(manager, child);
                children.Clear();
                child.GetChildren(Context, children);
                descendants.AddRange(children);
            }
        }

        private NodeState? ResolveAuthoredOrRegisteredNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                return null;
            }
            if (m_authoredNodes.TryGetValue(nodeId, out NodeState? authored))
            {
                return authored;
            }
            return m_nodeIdResolver(nodeId);
        }

        private void IndexAuthoredSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            var children = new List<BaseInstanceState>();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeState node = nodes[i];
                IndexAuthoredNode(node);

                children.Clear();
                node.GetChildren(Context, children);
                nodes.AddRange(children);
            }
        }

        private void IndexAuthoredNode(NodeState node)
        {
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The NodeId factory did not assign an id to node '{0}'.",
                    node.BrowseName);
            }

            if (m_authoredNodes.TryGetValue(node.NodeId, out NodeState? existing))
            {
                if (!ReferenceEquals(existing, node))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists,
                        "NodeId '{0}' is already used by node '{1}'.",
                        node.NodeId,
                        existing.BrowseName);
                }
                return;
            }

            NodeState? predefined = m_nodeIdResolver(node.NodeId);
            if (predefined != null && !ReferenceEquals(predefined, node))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdExists,
                    "NodeId '{0}' is already used by a predefined node.",
                    node.NodeId);
            }
            m_authoredNodes.Add(node.NodeId, node);
        }

        private bool HasAuthoredAncestor(NodeState node)
        {
            for (NodeState? current = node is BaseInstanceState instance
                    ? instance.Parent
                    : null;
                current != null;
                current = current is BaseInstanceState parentInstance
                    ? parentInstance.Parent
                    : null)
            {
                if (!current.NodeId.IsNull && IsStagedNode(current))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsStagedNode(NodeState node)
        {
            return m_authoredNodes.TryGetValue(node.NodeId, out NodeState? authored) &&
                ReferenceEquals(authored, node);
        }

        private void AddAuthoredRoot(NodeState node)
        {
            if (!ContainsByReference(m_authoredRoots, node))
            {
                m_authoredRoots.Add(node);
            }
        }

        private void AddChildIfMissing(NodeState parent, BaseInstanceState child)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], child))
                {
                    return;
                }
            }
            parent.AddChild(child);
        }

        private bool IsOwnedNamespace(ushort namespaceIndex)
        {
            if (NodeManager is not AsyncCustomNodeManager manager)
            {
                return namespaceIndex == m_defaultNamespaceIndex;
            }

            for (int i = 0; i < manager.NamespaceIndexes.Count; i++)
            {
                if (manager.NamespaceIndexes[i] == namespaceIndex)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ValidateAuthoredNodeId(
            AsyncCustomNodeManager manager,
            NodeState node)
        {
            if (node.NodeId.NamespaceIndex == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Added node '{0}' cannot use namespace index 0.",
                    node.BrowseName);
            }

            for (int i = 0; i < manager.NamespaceIndexes.Count; i++)
            {
                if (manager.NamespaceIndexes[i] == node.NodeId.NamespaceIndex)
                {
                    return;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadNodeIdInvalid,
                "Added node '{0}' uses namespace index {1}, which is not owned by this node manager.",
                node.BrowseName,
                node.NodeId.NamespaceIndex);
        }

        private QualifiedName CreateDefaultBrowseName(string browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse name is null or empty.");
            }
            return new QualifiedName(browseName, m_defaultNamespaceIndex);
        }

        private static QualifiedName ValidateExplicitBrowseName(QualifiedName browseName)
        {
            if (browseName.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse name is null or empty.");
            }
            if (browseName.NamespaceIndex == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "A QualifiedName browse name must specify a nonzero namespace index. " +
                    "Use the string overload for the manager's default namespace.");
            }
            return browseName;
        }

        private void ThrowIfAuthoredNodesRegistered()
        {
            if (m_authoredNodesRegistered)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Cannot add nodes after the staged graph has been registered. " +
                    "All node creation must occur inside Configure.");
            }
        }

        /// <summary>
        /// Identity-only stand-in handed to a node factory when the requested
        /// parent is owned by another node manager.
        /// </summary>
        private sealed class ExternalParentState : BaseObjectState
        {
            public ExternalParentState(NodeId nodeId)
                : base(parent: null)
            {
                NodeId = nodeId;
            }
        }

        private readonly Dictionary<NodeId, NodeState> m_authoredNodes = [];
        private readonly List<NodeState> m_authoredRoots = [];
        private bool m_authoredNodesRegistered;
    }
}
