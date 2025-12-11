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
        bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId);

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
            IFilterContext context,
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
        bool IsInView(IFilterContext context, NodeId viewId);

        /// <summary>
        /// Returns TRUE if the node is related to the current target.
        /// </summary>
        bool IsRelatedTo(
            IFilterContext context,
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
            IFilterContext context,
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
            IFilterContext context,
            NodeId typeDefinitionId,
            RelativePath relativePath,
            uint attributeId,
            NumericRange indexRange);
    }

    /// <summary>
    /// Provides context information to used when searching the address space.
    /// </summary>
    public interface IFilterContext : IOperationContext
    {
        /// <summary>
        /// The namespace table to use when evaluating filters.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The type tree to use when evaluating filters.
        /// </summary>
        /// <value>The type tree.</value>
        ITypeTable TypeTree { get; }

        /// <summary>
        /// Telemetry context for logging and tracing.
        /// </summary>
        ITelemetryContext Telemetry { get; }
    }
}
