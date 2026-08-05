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
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Resolves the OPC UA target variable an OPC 10101 §6.5.4 target-mapping
    /// descriptor declares, against a node manager's freshly imported
    /// predefined nodes. Injectable so a host application can supply an
    /// alternate resolution strategy (for example one that also consults an
    /// external addressing table); <see cref="WotTargetVariableResolver"/> is
    /// the default, spec-compliant implementation and is always available via
    /// direct construction.
    /// </summary>
    public interface IWotTargetVariableResolver
    {
        /// <summary>
        /// Resolves the target variable declared by <paramref name="mapping"/>.
        /// </summary>
        /// <param name="builder">
        /// The fluent builder for the node manager generation whose predefined
        /// nodes (including the freshly imported NodeSet2 content) the mapping
        /// is resolved against.
        /// </param>
        /// <param name="mapping">The target-mapping descriptor to resolve.</param>
        /// <returns>The resolved target variable.</returns>
        /// <exception cref="ServiceResultException">
        /// The mapping is missing, malformed, ambiguous, resolves to a node
        /// that is not a <see cref="BaseVariableState"/>, or (for a mapping
        /// that declares both terms) resolves to a variable whose
        /// <c>DataType</c> does not equal the declared target type.
        /// </exception>
        BaseVariableState Resolve(INodeManagerBuilder builder, WotTargetMappingDescriptor mapping);
    }

    /// <summary>
    /// The default <see cref="IWotTargetVariableResolver"/>. It implements the
    /// OPC 10101 §6.5.4 target-mapping resolution rules by reusing the fluent
    /// <see cref="INodeManagerBuilder"/> lookup surface (so lookup failures
    /// throw the same deterministic <see cref="ServiceResultException"/>
    /// statuses the builder already defines for missing, ambiguous, or
    /// wrong-node-class lookups):
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>uav:mapToNodeId</c> alone resolves the exact target and requires it
    ///   to be a <see cref="BaseVariableState"/>.
    ///   </description></item>
    ///   <item><description>
    ///   <c>uav:mapToType</c> alone resolves the unique variable whose
    ///   <c>DataType</c> equals the target type.
    ///   </description></item>
    ///   <item><description>
    ///   Both terms resolve the exact target and additionally validate that its
    ///   <c>DataType</c> equals the declared target type.
    ///   </description></item>
    /// </list>
    /// </summary>
    public sealed class WotTargetVariableResolver : IWotTargetVariableResolver
    {
        /// <inheritdoc/>
        public BaseVariableState Resolve(INodeManagerBuilder builder, WotTargetMappingDescriptor mapping)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            bool hasTargetNodeId = !string.IsNullOrWhiteSpace(mapping.TargetNodeId);
            bool hasTargetType = !string.IsNullOrWhiteSpace(mapping.TargetTypeNodeId);

            if (!hasTargetNodeId && !hasTargetType)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "The target mapping declares neither 'uav:mapToNodeId' nor 'uav:mapToType'.");
            }

            if (hasTargetNodeId)
            {
                NodeId targetNodeId = ParsePortableNodeId(builder, mapping.TargetNodeId!, "uav:mapToNodeId");
                BaseVariableState variable = builder.Variable<Variant>(targetNodeId).Node;

                if (hasTargetType)
                {
                    NodeId targetTypeId = ParsePortableNodeId(builder, mapping.TargetTypeNodeId!, "uav:mapToType");
                    if (variable.DataType != targetTypeId)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTypeMismatch,
                            "Target variable '{0}' has DataType '{1}', which does not match the " +
                            "declared 'uav:mapToType' target type '{2}'.",
                            targetNodeId,
                            variable.DataType,
                            targetTypeId);
                    }
                }
                return variable;
            }

            NodeId dataTypeId = ParsePortableNodeId(builder, mapping.TargetTypeNodeId!, "uav:mapToType");
            return builder.VariableFromDataTypeId<Variant>(dataTypeId).Node;
        }

        /// <summary>
        /// Parses a portable NodeId (including <c>nsu=</c> forms) against the
        /// builder's namespace table, translating every parse failure —
        /// including a <see cref="ServiceResultException"/> raised by the
        /// parser itself — into a deterministic
        /// <see cref="StatusCodes.BadNodeIdInvalid"/> naming the offending
        /// term, so callers never need to special-case the parser's own
        /// exception shape.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static NodeId ParsePortableNodeId(INodeManagerBuilder builder, string text, string term)
        {
            try
            {
                return ExpandedNodeId.Parse(text, builder.Context.NamespaceUris);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException and not OutOfMemoryException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "'{0}' value '{1}' is not a valid portable NodeId: {2}",
                    term,
                    text,
                    ex.Message);
            }
        }
    }
}
