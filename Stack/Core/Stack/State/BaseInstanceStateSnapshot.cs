/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// A lightweight snapshot of an instance node. 
    /// </summary>
    public class InstanceStateSnapshot : IFilterTarget
    {
        #region Public Interface
        /// <summary>
        /// Gets or sets a handled associated with the snapshot.
        /// </summary>
        /// <value>The handle.</value>
        public object Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// Initializes the snapshot from an instance.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="state">The state.</param>
        public void Initialize(
            ISystemContext context, 
            BaseInstanceState state)
        {
            m_typeDefinitionId = state.TypeDefinitionId;
            m_snapshot = CreateChildNode(context, state);
        }

        /// <summary>
        /// Sets the value for a child. Adds it if it does not already exist.
        /// </summary>
        /// <param name="browseName">The BrowseName.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="value">The value.</param>
        public void SetChildValue(
            QualifiedName browseName,
            NodeClass nodeClass,
            object value)
        {
            SetChildValue(m_snapshot, browseName, nodeClass, value);
        }
        #endregion

        #region IFilterTarget Members
        /// <summary>
        /// Returns true if the snapshort is an instance of the specified type.
        /// </summary>
        /// <param name="context">The context to use when checking the type definition.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <returns>
        /// True if the object is an instance of the specified type.
        /// </returns>
        public bool IsTypeOf(FilterContext context, NodeId typeDefinitionId)
        {
            if (!NodeId.IsNull(typeDefinitionId))
            {
                if (!context.TypeTree.IsTypeOf(m_typeDefinitionId, typeDefinitionId))
                {
                    return false;
                }
            }

            return true;
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
            FilterContext context, 
            NodeId typeDefinitionId, 
            IList<QualifiedName> relativePath, 
            uint attributeId, 
            NumericRange indexRange)
        {
            if (!NodeId.IsNull(typeDefinitionId))
            {
                if (!context.TypeTree.IsTypeOf(m_typeDefinitionId, typeDefinitionId))
                {
                    return null;
                }
            }

            object value = GetAttributeValue(
                m_snapshot,
                relativePath,
                0,
                attributeId);

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
        #endregion

        #region ChildNode Class
        /// <summary>
        /// Stores the key attributes of a child node.
        /// </summary>
        private class ChildNode
        {
            public NodeClass NodeClass;
            public QualifiedName BrowseName;
            public object Value;
            public List<ChildNode> Children;
        }     
        #endregion   

        #region Private Methods
        /// <summary>
        /// Sets the value for a child. Adds it if it does not already exist.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="browseName">The BrowseName.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="value">The value.</param>
        private void SetChildValue(
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
                node.Children = new List<ChildNode>();
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
            ChildNode node = new ChildNode();

            node.NodeClass  = state.NodeClass;
            node.BrowseName = state.BrowseName;

            BaseVariableState variable = state as BaseVariableState;

            if (variable != null)
            {
                if (!StatusCode.IsBad(variable.StatusCode))
                {
                    node.Value = Utils.Clone(variable.Value);
                }
            }

            BaseObjectState instance = state as BaseObjectState;

            if (instance != null)
            {
                node.Value = instance.NodeId;                    
            }

            node.Children = CreateChildNodes(context, state);

            return node;
        }

        /// <summary>
        /// Recusively stores the the current value for Object and Variable child nodes.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="state">The state.</param>
        /// <returns>The list of the nodes.</returns>
        private List<ChildNode> CreateChildNodes(ISystemContext context, BaseInstanceState state)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            state.GetChildren(context, children);

            List<ChildNode> nodes = new List<ChildNode>();

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                if (child == null || (child.NodeClass != NodeClass.Object && child.NodeClass != NodeClass.Variable))
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
        private object GetAttributeValue(
            ChildNode node,
            IList<QualifiedName> relativePath,
            int index, 
            uint attributeId)
        {
            if (index >= relativePath.Count)
            {
                if (node.NodeClass == NodeClass.Object && attributeId == Attributes.NodeId)
                {
                    return node.Value;
                }

                if (node.NodeClass == NodeClass.Variable && attributeId == Attributes.Value)
                {
                    return node.Value;
                }

                return null;
            }

            for (int ii = 0; ii < node.Children.Count; ii++)
            {
                if (node.Children[ii].BrowseName == relativePath[index])
                {
                    return GetAttributeValue(node.Children[ii], relativePath, index+1, attributeId);
                }
            }

            return null;
        }
        #endregion
        
        #region Private Fields
        private NodeId m_typeDefinitionId;
        private ChildNode m_snapshot;
        private object m_handle;
        #endregion
    }
}
