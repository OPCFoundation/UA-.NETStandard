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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Imports one or more NodeSet documents into a single unlinked batch.
    /// </summary>
    /// <remarks>
    /// State construction may be intercepted by namespace-stable, concrete
    /// factories. All NodeSet attributes and references are still applied by
    /// <see cref="UANodeSet"/> itself, and parent-child links are established
    /// once after the complete batch has been collected.
    /// </remarks>
    internal sealed class NodeSetImporter
    {
        /// <summary>
        /// Initializes an importer for one prepared graph generation.
        /// </summary>
        public NodeSetImporter(
            ISystemContext context,
            INodeSetImportFactoryProvider? factoryProvider)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_factoryRegistry = new NodeSetImportFactoryRegistry(
                factoryProvider?.GetNodeSetImportFactories() ?? [],
                context);
        }

        /// <summary>
        /// Gets every node imported by this batch.
        /// </summary>
        public NodeStateCollection ImportedNodes => m_importedNodes;

        /// <summary>
        /// Imports a document without linking its parent-child relationships.
        /// </summary>
        public ArrayOf<NodeState> Import(UANodeSet nodeSet)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            if (m_completed)
            {
                throw new InvalidOperationException(
                    "The NodeSet import batch has already been completed.");
            }

            RegisterMappingNamespaces(nodeSet);

            var imported = new NodeStateCollection();
            nodeSet.Import(
                m_context,
                imported,
                new NodeSetImportStateFactory(m_factoryRegistry.CreateEmptyState),
                linkParentChild: false);

            var result = new NodeState[imported.Count];
            for (int i = 0; i < imported.Count; i++)
            {
                NodeState node = imported[i];
                if (node is BaseInstanceState instance &&
                    instance.Handle is NodeId parentNodeId)
                {
                    instance.Handle = null;
                    m_parentNodeIds.Add(instance, parentNodeId);
                }
                NodeId nodeId = node.NodeId;
                if (!nodeId.IsNull &&
                    !m_nodesById.TryAdd(nodeId, node))
                {
                    throw new InvalidOperationException(
                        $"Duplicate NodeId '{nodeId}' detected across the loaded NodeSet2 " +
                        "sources. Each node must have a unique NodeId.");
                }

                m_importedNodes.Add(node);
                result[i] = node;
            }

            return new ArrayOf<NodeState>(result);
        }

        public bool TryGetNode(NodeId nodeId, out NodeState? node)
        {
            return m_nodesById.TryGetValue(nodeId, out node);
        }

        public bool TryGetTypedReplacement(
            NodeState candidate,
            Func<NodeState, bool> isFactoryAuthored,
            out NodeState? replacement)
        {
            if (isFactoryAuthored is null)
            {
                throw new ArgumentNullException(nameof(isFactoryAuthored));
            }
            return TryGetTypedReplacement(
                candidate,
                isFactoryAuthored,
                [],
                out replacement);
        }

        private bool TryGetTypedReplacement(
            NodeState candidate,
            Func<NodeState, bool> isFactoryAuthored,
            List<NodeState> visited,
            out NodeState? replacement)
        {
            replacement = null;
            if (visited.Exists(node => ReferenceEquals(node, candidate)) ||
                candidate is not BaseInstanceState { Parent: { } parent })
            {
                return false;
            }
            visited.Add(candidate);

            NodeId replacementParentNodeId = NodeId.Null;
            if (isFactoryAuthored(parent) &&
                TryGetTypedReplacement(
                    parent,
                    isFactoryAuthored,
                    visited,
                    out NodeState? replacementParent))
            {
                replacementParentNodeId = replacementParent!.NodeId;
            }
            for (int i = 0; i < m_importedNodes.Count; i++)
            {
                NodeState imported = m_importedNodes[i];
                if (imported is BaseInstanceState importedInstance &&
                    importedInstance.BrowseName == candidate.BrowseName &&
                    m_parentNodeIds.TryGetValue(
                        importedInstance,
                        out NodeId parentNodeId) &&
                    (parentNodeId == parent.NodeId ||
                        parentNodeId == replacementParentNodeId) &&
                    m_factoryRegistry.IsFactoryCreated(imported))
                {
                    replacement = imported;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Links the complete import batch exactly once.
        /// </summary>
        /// <param name="availableNodes">
        /// Additional staged nodes that imported parent identifiers may target.
        /// </param>
        /// <param name="isFactoryAuthored">
        /// Identifies staged parents created through a typed graph factory.
        /// </param>
        /// <param name="onTypedReplacement">
        /// Receives each generated placeholder displaced by an imported child.
        /// </param>
        public void Complete(
            IReadOnlyDictionary<NodeId, NodeState>? availableNodes = null,
            Func<NodeState, bool>? isFactoryAuthored = null,
            Action<BaseInstanceState, BaseInstanceState>? onTypedReplacement = null)
        {
            if (m_completed)
            {
                return;
            }

            UANodeSet.LinkParentChildRelationships(
                m_context,
                m_importedNodes,
                new NodeSetImportLinkOptions
                {
                    ParentNodeIds = m_parentNodeIds,
                    AvailableNodes = availableNodes,
                    UseTypedReplacement = (parent, child) =>
                        m_factoryRegistry.IsFactoryCreated(child) &&
                        (m_factoryRegistry.IsFactoryCreated(parent) ||
                            isFactoryAuthored?.Invoke(parent) == true),
                    OnTypedReplacement = onTypedReplacement
                });
            m_completed = true;
        }

        /// <summary>
        /// Completes the StructureDefinitions of every DataType in the batch with the
        /// fields inherited from supertypes in other documents of the batch or owned
        /// by the server (Part 3 8.48, Part 6 F.12).
        /// </summary>
        /// <param name="server">The server whose address space owns external supertypes.</param>
        /// <param name="availableNodes">
        /// Additional staged nodes which are not yet part of the address space.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public async ValueTask CompleteDataTypeDefinitionsAsync(
            IServerInternal server,
            IReadOnlyDictionary<NodeId, NodeState>? availableNodes,
            CancellationToken cancellationToken = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            // Definitions owned outside the batch are complete already; resolve the
            // direct supertypes which leave the batch once, asynchronously.
            var externalDefinitions = new Dictionary<NodeId, DataTypeDefinition?>();
            for (int i = 0; i < m_importedNodes.Count; i++)
            {
                if (m_importedNodes[i] is not DataTypeState dataType)
                {
                    continue;
                }

                NodeId superTypeId = dataType.SuperTypeId;
                if (superTypeId.IsNull ||
                    m_nodesById.ContainsKey(superTypeId) ||
                    externalDefinitions.ContainsKey(superTypeId))
                {
                    continue;
                }

                externalDefinitions[superTypeId] = await ResolveExternalDefinitionAsync(
                    server,
                    availableNodes,
                    superTypeId,
                    0,
                    cancellationToken).ConfigureAwait(false);
            }

            UANodeSet.CompleteDataTypeDefinitions(
                m_context,
                m_importedNodes,
                dataTypeId => externalDefinitions.TryGetValue(
                    dataTypeId,
                    out DataTypeDefinition? definition)
                    ? definition
                    : null);
        }

        private static async ValueTask<DataTypeDefinition?> ResolveExternalDefinitionAsync(
            IServerInternal server,
            IReadOnlyDictionary<NodeId, NodeState>? availableNodes,
            NodeId dataTypeId,
            int depth,
            CancellationToken cancellationToken)
        {
            if (dataTypeId.IsNull || depth > kMaxSuperTypeDepth)
            {
                return null;
            }
            if (dataTypeId == DataTypeIds.Structure || dataTypeId == DataTypeIds.Union)
            {
                return new StructureDefinition
                {
                    BaseDataType = dataTypeId == DataTypeIds.Union
                        ? DataTypeIds.Structure
                        : DataTypeIds.BaseDataType,
                    StructureType = dataTypeId == DataTypeIds.Union
                        ? StructureType.Union
                        : StructureType.Structure,
                    Fields = []
                };
            }

            // A compiled type describes its complete layout.
            var typeId = NodeId.ToExpandedNodeId(dataTypeId, server.NamespaceUris);
            if (server.Factory.TryGetEncodeableType(typeId, out IEncodeableType? encodeableType) &&
                encodeableType is IDataTypeDefinitionSource structureSource)
            {
                return structureSource.GetDataTypeDefinition(server.NamespaceUris);
            }
            if (server.Factory.TryGetEnumeratedType(typeId, out IEnumeratedType? enumeratedType) &&
                enumeratedType is IDataTypeDefinitionSource enumSource)
            {
                return enumSource.GetDataTypeDefinition(server.NamespaceUris);
            }

            NodeState? node = null;
            if (availableNodes?.TryGetValue(dataTypeId, out node) != true)
            {
                node = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(dataTypeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (node is not DataTypeState dataType)
            {
                return null;
            }
            if (dataType.DataTypeDefinition.TryGetValue(out DataTypeDefinition? definition) &&
                definition != null)
            {
                return definition;
            }

            // A Structure subtype without own fields has the layout of its supertype.
            NodeId superTypeId = dataType.SuperTypeId.IsNull
                ? server.TypeTree.FindSuperType(dataTypeId)
                : dataType.SuperTypeId;
            if (await ResolveExternalDefinitionAsync(
                server,
                availableNodes,
                superTypeId,
                depth + 1,
                cancellationToken).ConfigureAwait(false) is StructureDefinition baseDefinition)
            {
                return new StructureDefinition
                {
                    BaseDataType = superTypeId,
                    StructureType = baseDefinition.StructureType,
                    Fields = baseDefinition.Fields
                };
            }
            return null;
        }

        private void RegisterMappingNamespaces(UANodeSet nodeSet)
        {
            if (nodeSet.NamespaceUris is null)
            {
                return;
            }

            for (int i = 0; i < nodeSet.NamespaceUris.Length; i++)
            {
                string uri = nodeSet.NamespaceUris[i];
                if (!string.IsNullOrEmpty(uri))
                {
                    m_context.NamespaceUris.GetIndexOrAppend(uri);
                }
            }
        }

        private const int kMaxSuperTypeDepth = 64;
        private readonly ISystemContext m_context;
        private readonly NodeSetImportFactoryRegistry m_factoryRegistry;
        private readonly NodeStateCollection m_importedNodes = [];
        private readonly Dictionary<NodeId, NodeState> m_nodesById = [];
        private readonly Dictionary<BaseInstanceState, NodeId> m_parentNodeIds =
            new(BaseInstanceStateReferenceComparer.Instance);
        private bool m_completed;

        private sealed class BaseInstanceStateReferenceComparer :
            IEqualityComparer<BaseInstanceState>
        {
            public static BaseInstanceStateReferenceComparer Instance { get; } = new();

            public bool Equals(
                BaseInstanceState? left,
                BaseInstanceState? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(BaseInstanceState state)
            {
                return RuntimeHelpers.GetHashCode(state);
            }
        }
    }

    /// <summary>
    /// Resolves import factory registrations into the three NodeSet discriminator maps.
    /// </summary>
    internal sealed class NodeSetImportFactoryRegistry
    {
        /// <summary>
        /// Resolves namespace-stable registrations for one import generation.
        /// </summary>
        public NodeSetImportFactoryRegistry(
            ArrayOf<INodeSetImportFactory> factories,
            ISystemContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (factories.IsNull)
            {
                return;
            }

            for (int i = 0; i < factories.Count; i++)
            {
                INodeSetImportFactory factory = factories[i] ??
                    throw new InvalidOperationException(
                        $"NodeSet import factory at index {i} is null.");
                NodeId discriminatorId = ResolveDiscriminatorId(
                    factory.DiscriminatorId,
                    context.NamespaceUris);

                bool added = factory.Discriminator switch
                {
                    NodeSetImportDiscriminator.TypeDefinition
                        when factory.NodeClass is NodeClass.Object or NodeClass.Variable =>
                        m_instanceFactories.TryAdd(
                            (factory.NodeClass, discriminatorId),
                            factory),
                    NodeSetImportDiscriminator.MethodDeclaration
                        when factory.NodeClass == NodeClass.Method =>
                        m_methodFactories.TryAdd(discriminatorId, factory),
                    NodeSetImportDiscriminator.NodeId =>
                        m_declarationFactories.TryAdd(
                            (factory.NodeClass, discriminatorId),
                            factory),
                    _ => throw new InvalidOperationException(
                        $"NodeSet import factory discriminator '{factory.Discriminator}' " +
                        $"is not valid for node class '{factory.NodeClass}'.")
                };
                if (!added && !ContainsSameFactory(
                    factory,
                    discriminatorId))
                {
                    throw new InvalidOperationException(
                        $"A NodeSet import factory is already registered for " +
                        $"{factory.NodeClass} discriminator '{discriminatorId}'.");
                }
            }
        }

        private bool ContainsSameFactory(
            INodeSetImportFactory factory,
            NodeId discriminatorId)
        {
            INodeSetImportFactory? existing;
            bool found = factory.Discriminator switch
            {
                NodeSetImportDiscriminator.TypeDefinition =>
                    m_instanceFactories.TryGetValue(
                        (factory.NodeClass, discriminatorId),
                        out existing),
                NodeSetImportDiscriminator.MethodDeclaration =>
                    m_methodFactories.TryGetValue(
                        discriminatorId,
                        out existing),
                NodeSetImportDiscriminator.NodeId =>
                    m_declarationFactories.TryGetValue(
                        (factory.NodeClass, discriminatorId),
                        out existing),
                _ => SetFactoryNotFound(out existing)
            };
            return found && ReferenceEquals(existing, factory);
        }

        /// <summary>
        /// Creates an empty state for a resolved discriminator, if one is registered.
        /// </summary>
        public NodeState? CreateEmptyState(
            NodeClass nodeClass,
            NodeId nodeId,
            NodeId discriminatorId)
        {
            if (!nodeId.IsNull &&
                m_declarationFactories.TryGetValue(
                    (nodeClass, nodeId),
                    out INodeSetImportFactory? exactFactory))
            {
                return CreateAndValidate(
                    exactFactory,
                    nodeClass,
                    nodeId);
            }

            if (discriminatorId.IsNull)
            {
                return null;
            }

            INodeSetImportFactory? factory;
            bool found = nodeClass switch
            {
                NodeClass.Object or NodeClass.Variable =>
                    m_instanceFactories.TryGetValue(
                        (nodeClass, discriminatorId),
                        out factory),
                NodeClass.Method =>
                    m_methodFactories.TryGetValue(
                        discriminatorId,
                        out factory),
                _ => SetFactoryNotFound(out factory)
            };
            if (!found)
            {
                return null;
            }

            return CreateAndValidate(factory!, nodeClass, discriminatorId);
        }

        private NodeState CreateAndValidate(
            INodeSetImportFactory factory,
            NodeClass nodeClass,
            NodeId discriminatorId)
        {
            NodeState state = factory.CreateEmptyState() ??
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' returned null.");
            ValidateEmptyState(nodeClass, discriminatorId, state);
            if (!m_factoryCreatedStates.Add(state))
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' returned a state that was already used.");
            }
            return state;
        }

        /// <summary>
        /// Gets whether a state was created by a registered import factory.
        /// </summary>
        public bool IsFactoryCreated(NodeState state)
        {
            return m_factoryCreatedStates.Contains(state);
        }

        private static bool SetFactoryNotFound(
            out INodeSetImportFactory? factory)
        {
            factory = null;
            return false;
        }

        private void ValidateEmptyState(
            NodeClass nodeClass,
            NodeId discriminatorId,
            NodeState state)
        {
            bool compatible = nodeClass switch
            {
                NodeClass.Object => state is BaseObjectState,
                NodeClass.Variable => state is BaseVariableState,
                NodeClass.Method => state is MethodState,
                NodeClass.ObjectType => state is BaseObjectTypeState,
                NodeClass.VariableType => state is BaseVariableTypeState,
                NodeClass.DataType => state is DataTypeState,
                NodeClass.ReferenceType => state is ReferenceTypeState,
                NodeClass.View => state is ViewState,
                _ => false
            };
            if (!compatible)
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' returned incompatible state type " +
                    $"'{state.GetType().FullName}'.");
            }
            if (state.IsCreated)
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' returned an initialized state. Import " +
                    "factories must create empty states.");
            }
            if (state is BaseInstanceState { Parent: not null })
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' returned a state with a parent.");
            }

            var children = new List<BaseInstanceState>();
            state.GetChildren(m_context, children);
            if (children.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' materialized {children.Count} child node(s). " +
                    "Import factories must create empty states.");
            }

            var references = new List<IReference>();
            state.GetReferences(m_context, references);
            if (references.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The NodeSet import factory for {nodeClass} discriminator " +
                    $"'{discriminatorId}' materialized {references.Count} reference(s). " +
                    "Import factories must create empty states.");
            }
        }

        private static NodeId ResolveDiscriminatorId(
            ExpandedNodeId discriminatorId,
            NamespaceTable namespaceUris)
        {
            if (discriminatorId.IsNull)
            {
                throw new InvalidOperationException(
                    "A NodeSet import factory must declare a discriminator.");
            }
            if (discriminatorId.ServerIndex != 0)
            {
                throw new InvalidOperationException(
                    $"NodeSet import discriminator '{discriminatorId}' targets a remote server.");
            }
            if (string.IsNullOrEmpty(discriminatorId.NamespaceUri))
            {
                if (discriminatorId.InnerNodeId.NamespaceIndex != 0)
                {
                    throw new InvalidOperationException(
                        $"NodeSet import discriminator '{discriminatorId}' is namespace-index " +
                        "dependent. Use an ExpandedNodeId with a namespace URI.");
                }
                return discriminatorId.InnerNodeId;
            }

            ushort namespaceIndex = namespaceUris.GetIndexOrAppend(
                discriminatorId.NamespaceUri);
            return discriminatorId.InnerNodeId.WithNamespaceIndex(namespaceIndex);
        }

        private readonly ISystemContext m_context;
        private readonly Dictionary<
            (NodeClass NodeClass, NodeId DiscriminatorId),
            INodeSetImportFactory> m_instanceFactories = [];
        private readonly Dictionary<NodeId, INodeSetImportFactory>
            m_methodFactories = [];
        private readonly Dictionary<
            (NodeClass NodeClass, NodeId DiscriminatorId),
            INodeSetImportFactory> m_declarationFactories = [];
        private readonly HashSet<NodeState> m_factoryCreatedStates =
            new(NodeStateReferenceComparer.Instance);

        private sealed class NodeStateReferenceComparer :
            IEqualityComparer<NodeState>
        {
            public static NodeStateReferenceComparer Instance { get; } = new();

            public bool Equals(NodeState? left, NodeState? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(NodeState state)
            {
                return RuntimeHelpers.GetHashCode(state);
            }
        }
    }
}
