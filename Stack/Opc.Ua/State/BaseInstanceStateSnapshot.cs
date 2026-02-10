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

namespace Opc.Ua
{
    /// <summary>
    /// A lightweight snapshot of an instance node.
    /// </summary>
    public class InstanceStateSnapshot : IFilterTarget
    {
        /// <summary>
        /// Gets or sets a handled associated with the snapshot.
        /// </summary>
        /// <value>The handle.</value>
        public object Handle { get; set; }

        /// <summary>
        /// Initializes the snapshot from an instance.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="state">The state.</param>
        public void Initialize(ISystemContext context, BaseInstanceState state)
        {
            m_typeDefinitionId = state.TypeDefinitionId;
            m_snapshot = CreateChildNode(context, state);
            Handle = state;
        }

        /// <summary>
        /// Sets the value for a child. Adds it if it does not already exist.
        /// </summary>
        /// <param name="browseName">The BrowseName.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="value">The value.</param>
        public void SetChildValue(QualifiedName browseName, NodeClass nodeClass, object value)
        {
            SetChildValue(m_snapshot, browseName, nodeClass, value);
        }

        /// <summary>
        /// Returns true if the snapshort is an instance of the specified type.
        /// </summary>
        /// <param name="context">The context to use when checking the type definition.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <returns>
        /// True if the object is an instance of the specified type.
        /// </returns>
        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            return typeDefinitionId.IsNull ||
                context.TypeTree.IsTypeOf(m_typeDefinitionId, typeDefinitionId);
        }

        /// <summary>
        /// Returns the value of the attribute for the specified child.
        /// </summary>
        /// <param name="context">The context to use when evaluating the operand.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <param name="relativePath">The path from the instance to the node which defines the attribute.</param>
        /// <param name="attributeId">The attribute to return.</param>
        /// <param name="indexRange">The sub-set of an array value to return.</param>
        /// <returns>
        /// The attribute value. Returns null if the attribute does not exist.
        /// </returns>
        public object GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            if (!typeDefinitionId.IsNull &&
                !context.TypeTree.IsTypeOf(m_typeDefinitionId, typeDefinitionId))
            {
                return null;
            }

            object value = GetAttributeValue(m_snapshot, relativePath, 0, attributeId);

            if (indexRange != NumericRange.Empty)
            {
                StatusCode error = indexRange.ApplyRange(ref value);

                if (StatusCode.IsBad(error))
                {
                    value = null;
                }
            }

            return value;
        }

        /// <summary>
        /// Stores the key attributes of a child node.
        /// </summary>
        private sealed class ChildNode
        {
            public NodeClass NodeClass;
            public QualifiedName BrowseName;
            public object Value;
            public List<ChildNode> Children;
        }

        /// <summary>
        /// Sets the value for a child. Adds it if it does not already exist.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="browseName">The BrowseName.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="value">The value.</param>
        private static void SetChildValue(
            ChildNode node,
            QualifiedName browseName,
            NodeClass nodeClass,
            object value)
        {
            ChildNode child = null;

            if (node.Children != null)
            {
                for (int ii = 0; ii < node.Children.Count; ii++)
                {
                    child = node.Children[ii];

                    if (child.BrowseName == browseName)
                    {
                        break;
                    }

                    child = null;
                }
            }
            else
            {
                node.Children = [];
            }

            if (child == null)
            {
                child = new ChildNode();
                node.Children.Add(child);
            }

            child.BrowseName = browseName;
            child.NodeClass = nodeClass;
            child.Value = value;
        }

        /// <summary>
        /// Creates a snapshot of a node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="state">The state.</param>
        /// <returns>A snapshot of a node.</returns>
        private ChildNode CreateChildNode(ISystemContext context, BaseInstanceState state)
        {
            var node = new ChildNode { NodeClass = state.NodeClass, BrowseName = state.BrowseName };

            if (state is BaseVariableState variable && !StatusCode.IsBad(variable.StatusCode))
            {
                node.Value = CoreUtils.Clone(variable.Value);
            }

            if (state is BaseObjectState instance)
            {
                node.Value = instance.NodeId;
            }

            node.Children = CreateChildNodes(context, state);

            return node;
        }

        /// <summary>
        /// Recursively stores the current value for Object and Variable child nodes.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="state">The state.</param>
        /// <returns>The list of the nodes.</returns>
        private List<ChildNode> CreateChildNodes(ISystemContext context, BaseInstanceState state)
        {
            var children = new List<BaseInstanceState>();
            state.GetChildren(context, children);

            var nodes = new List<ChildNode>();

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                if (child == null ||
                    (child.NodeClass != NodeClass.Object && child.NodeClass != NodeClass.Variable))
                {
                    continue;
                }

                ChildNode node = CreateChildNode(context, child);
                nodes.Add(node);
            }

            return nodes;
        }

        /// <summary>
        /// Returns the value of the attribute for the specified child.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="relativePath">The relative path.</param>
        /// <param name="index">The index.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of the attribute for the specified child.</returns>
        private static object GetAttributeValue(
            ChildNode node,
            IList<QualifiedName> relativePath,
            int index,
            uint attributeId)
        {
            if (index >= relativePath.Count)
            {
                if (attributeId == Attributes.NodeId)
                {
                    return node.Value;
                }

                if (node.NodeClass == NodeClass.Variable && attributeId == Attributes.Value)
                {
                    return node.Value;
                }

                if (attributeId == Attributes.NodeClass)
                {
                    return node.NodeClass;
                }

                if (attributeId == Attributes.BrowseName)
                {
                    return node.BrowseName;
                }

                return null;
            }

            for (int ii = 0; ii < node.Children.Count; ii++)
            {
                if (node.Children[ii].BrowseName == relativePath[index])
                {
                    return GetAttributeValue(
                        node.Children[ii],
                        relativePath,
                        index + 1,
                        attributeId);
                }
            }

            return null;
        }

        private NodeId m_typeDefinitionId;
        private ChildNode m_snapshot;
    }
}
