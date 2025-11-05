/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// This interface is used by ContentFilterOperation to get values from the
    /// NodeSet for use by the various filter operators. All NodeSets used in a
    /// ContentFilter must implement this interface.
    /// </summary>
    public interface IFilterTarget
    {
        /// <summary>
        /// Checks whether the target is an instance of the specified type.
        /// </summary>
        /// <param name="context">The context to use when checking the type definition.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <returns>
        /// True if the object is an instance of the specified type.
        /// </returns>
        bool IsTypeOf(FilterContext context, NodeId typeDefinitionId);

        /// <summary>
        /// Returns the value of an attribute identified by the operand.
        /// </summary>
        /// <param name="context">The context to use when evaluating the operand.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <param name="relativePath">The path from the instance to the node which defines the attribute.</param>
        /// <param name="attributeId">The attribute to return.</param>
        /// <param name="indexRange">The sub-set of an array value to return.</param>
        /// <returns>
        /// The attribute value. Returns null if the attribute does not exist.
        /// </returns>
        object GetAttributeValue(
            FilterContext context,
            NodeId typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange);
    }

    /// <summary>
    /// This interface is used by ContentFilterOperation to get values from the
    /// NodeSet for use by the various filter operators. All NodeSets used in a
    /// ContentFilter must implement this interface.
    /// </summary>
    public interface IAdvancedFilterTarget : IFilterTarget
    {
        /// <summary>
        /// Checks whether the target is an instance is in the specified view.
        /// </summary>
        /// <param name="context">The context to use when checking the biew.</param>
        /// <param name="viewId">The identifier for the view.</param>
        /// <returns>True if the instance is in the view.</returns>
        bool IsInView(FilterContext context, NodeId viewId);

        /// <summary>
        /// Returns TRUE if the node is related to the current target.
        /// </summary>
        bool IsRelatedTo(
            FilterContext context,
            NodeId intermediateNodeId,
            NodeId sourceTypeId,
            NodeId targetTypeId,
            NodeId referenceTypeId,
            int hops,
            bool includeTypeDefintionSubtypes,
            bool includeReferenceSubtypes);

        /// <summary>
        /// Returns the list of nodes related to the current target.
        /// </summary>
        IList<NodeId> GetRelatedNodes(
            FilterContext context,
            NodeId intermediateNodeId,
            NodeId sourceTypeId,
            NodeId targetTypeId,
            NodeId referenceTypeId,
            int hops,
            bool includeTypeDefintionSubtypes,
            bool includeReferenceSubtypes);

        /// <summary>
        /// Returns the value of attributes for nodes which are related to the current node.
        /// </summary>
        /// <param name="context">The context to use when evaluating the operand.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <param name="relativePath">The relative path to the attribute.</param>
        /// <param name="attributeId">The attribute to return.</param>
        /// <param name="indexRange">The sub-set of an array value to return.</param>
        /// <returns>
        /// The attribute value. Returns null if the attribute does not exist.
        /// </returns>
        object GetRelatedAttributeValue(
            FilterContext context,
            NodeId typeDefinitionId,
            RelativePath relativePath,
            uint attributeId,
            NumericRange indexRange);
    }

    /// <summary>
    /// Filter context
    /// </summary>
    public interface FilterContext
    {

    }
}
