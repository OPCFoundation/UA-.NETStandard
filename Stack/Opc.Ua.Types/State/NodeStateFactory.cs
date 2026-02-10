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
        public virtual NodeState CreateInstance(
            ISystemContext context,
            NodeState parent,
            NodeClass nodeClass,
            QualifiedName browseName,
            NodeId referenceTypeId,
            NodeId typeDefinitionId)
        {
            INodeStateFactory factory;
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
        public NodeState CreateInstance(
            ISystemContext context,
            NodeState parent,
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
                    child = null;
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {nodeClass}");
            }
            return child;
        }
    }
}
