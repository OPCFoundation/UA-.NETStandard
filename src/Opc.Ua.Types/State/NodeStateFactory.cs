/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A class that creates instances of nodes based on the parameters provided.
    /// </summary>
    public class NodeStateFactory : INodeStateFactory, INodeStateFactoryBuilder
    {
        /// <inheritdoc/>
        public virtual NodeState? CreateInstance(
            ISystemContext context,
            NodeState? parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId)
        {
            INodeStateFactory? factory;
            if (typeDefinitionId.IsNull)
            {
                factory = DefaultNodeStateActivator.Instance;
            }
            else
            {
                var id = NodeId.ToExpandedNodeId(
                    typeDefinitionId,
                    context.NamespaceUris);
                if (!m_types.TryGetValue(id, out factory))
                {
                    factory = DefaultNodeStateActivator.Instance;
                }
            }
            return factory.CreateInstance(
                context,
                parent,
                nodeClass,
                browseName,
                referenceTypeId,
                typeDefinitionId);
        }

        /// <inheritdoc/>
        public INodeStateFactoryBuilder RegisterType(
            ExpandedNodeId typeDefinitionId,
            INodeStateFactory typeFactory)
        {
            if (typeDefinitionId.IsNull)
            {
                throw new ArgumentNullException(nameof(typeDefinitionId));
            }
            if (typeFactory is null or NodeStateFactory)
            {
                throw new ArgumentNullException(nameof(typeFactory));
            }
            m_types[typeDefinitionId] = typeFactory;
            return this;
        }

        /// <summary>
        /// Unregisters a node state factory from this factory.
        /// </summary>
        /// <param name="typeDefinitionId">The type definition.</param>
        public void UnRegisterType(ExpandedNodeId typeDefinitionId)
        {
            if (typeDefinitionId.IsNull)
            {
                return;
            }
            m_types.TryRemove(typeDefinitionId, out _);
        }

        private readonly ConcurrentDictionary<ExpandedNodeId, INodeStateFactory> m_types = [];
    }

    /// <summary>
    /// Default activator
    /// </summary>
    internal class DefaultNodeStateActivator : INodeStateFactory
    {
        /// <summary>
        /// Default node state activator
        /// </summary>
        public static DefaultNodeStateActivator Instance { get; } = new();

        /// <inheritdoc/>
        public NodeState? CreateInstance(
            ISystemContext context,
            NodeState? parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId)
        {
            NodeState child;
            switch (nodeClass)
            {
                case NodeClass.Variable:
                    if (context.TypeTable != null &&
                        context.TypeTable.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasProperty))
                    {
                        child = new PropertyState(parent);
                        break;
                    }

                    child = new BaseDataVariableState(parent);
                    break;
                case NodeClass.Object:
                    child = new BaseObjectState(parent);
                    break;
                case NodeClass.Method:
                    child = new MethodState(parent);
                    break;
                case NodeClass.ReferenceType:
                    child = new ReferenceTypeState();
                    break;
                case NodeClass.ObjectType:
                    child = new BaseObjectTypeState();
                    break;
                case NodeClass.VariableType:
                    child = new BaseDataVariableTypeState();
                    break;
                case NodeClass.DataType:
                    child = new DataTypeState();
                    break;
                case NodeClass.View:
                    child = new ViewState();
                    break;
                case NodeClass.Unspecified:
                    child = null!;
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {nodeClass}");
            }
            return child;
        }
    }
}
