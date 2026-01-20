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
using System.IO;
using System.Linq;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for controlling NodeSet export behavior.
    /// </summary>
    public class NodeSetExportOptions
    {
        /// <summary>
        /// Whether to export value elements for variables.
        /// Default is true.
        /// </summary>
        public bool ExportValues { get; set; } = true;

        /// <summary>
        /// Whether to export the ParentNodeId attribute.
        /// Default is false (ParentNodeId is redundant as it can be inferred from references).
        /// </summary>
        public bool ExportParentNodeId { get; set; } = false;

        /// <summary>
        /// Whether to export the UserAccessLevel attribute.
        /// Default is false (UserAccessLevel often matches AccessLevel).
        /// </summary>
        public bool ExportUserAccessLevel { get; set; } = false;

        /// <summary>
        /// Whether to export the MethodDeclarationId attribute.
        /// Default is false (MethodDeclarationId is often redundant).
        /// </summary>
        public bool ExportMethodDeclarationId { get; set; } = false;

        /// <summary>
        /// Gets the default export options with all values exported.
        /// </summary>
        public static NodeSetExportOptions Default => new NodeSetExportOptions
        {
            ExportValues = true,
            ExportParentNodeId = false,
            ExportUserAccessLevel = false,
            ExportMethodDeclarationId = false
        };

        /// <summary>
        /// Gets minimal export options (smallest file size, values omitted).
        /// </summary>
        public static NodeSetExportOptions Minimal => new NodeSetExportOptions
        {
            ExportValues = false,
            ExportParentNodeId = false,
            ExportUserAccessLevel = false,
            ExportMethodDeclarationId = false
        };

        /// <summary>
        /// Gets complete export options with all metadata.
        /// </summary>
        public static NodeSetExportOptions Complete => new NodeSetExportOptions
        {
            ExportValues = true,
            ExportParentNodeId = true,
            ExportUserAccessLevel = true,
            ExportMethodDeclarationId = true
        };
    }

    /// <summary>
    /// Defines numerous re-useable utility functions for clients.
    /// </summary>
    public static partial class CoreClientUtils
    {
        /// <summary>
        /// Exports a list of nodes from a client session to a NodeSet2 XML file.
        /// </summary>
        /// <param name="context">The system context containing namespace information.</param>
        /// <param name="nodes">The list of nodes to export.</param>
        /// <param name="outputStream">The output stream to write the NodeSet2 XML to.</param>
        public static void ExportNodesToNodeSet2(
            ISystemContext context,
            IList<INode> nodes,
            Stream outputStream)
        {
            ExportNodesToNodeSet2(context, nodes, outputStream, NodeSetExportOptions.Default);
        }

        /// <summary>
        /// Exports a list of nodes from a client session to a NodeSet2 XML file.
        /// </summary>
        /// <param name="context">The system context containing namespace information.</param>
        /// <param name="nodes">The list of nodes to export.</param>
        /// <param name="outputStream">The output stream to write the NodeSet2 XML to.</param>
        /// <param name="options">Options controlling the export behavior.</param>
        public static void ExportNodesToNodeSet2(
            ISystemContext context,
            IList<INode> nodes,
            Stream outputStream,
            NodeSetExportOptions options)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Convert INode instances to NodeState instances
            var nodeStates = new NodeStateCollection();
            foreach (INode node in nodes)
            {
                NodeState? nodeState = CreateNodeState(context, node, options);
                if (nodeState != null)
                {
                    nodeStates.Add(nodeState);
                }
            }

            // Use the existing export functionality
            nodeStates.SaveAsNodeSet2(context, outputStream);
        }

        /// <summary>
        /// Creates a NodeState from an INode.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="node">The node to convert.</param>
        /// <param name="options">Export options.</param>
        /// <returns>A NodeState representing the node.</returns>
        private static NodeState? CreateNodeState(ISystemContext context, INode node, NodeSetExportOptions options)
        {
            if (node == null)
            {
                return null;
            }

            NodeState? nodeState = null;

            switch (node.NodeClass)
            {
                case NodeClass.Object:
                {
                    var objectNode = node as IObject;
                    var state = new BaseObjectState(null)
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        EventNotifier = objectNode?.EventNotifier ?? 0
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.Variable:
                {
                    var variableNode = node as IVariable;
                    var state = new BaseDataVariableState(null)
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        DataType = variableNode?.DataType ?? DataTypeIds.BaseDataType,
                        ValueRank = variableNode?.ValueRank ?? ValueRanks.Any,
                        AccessLevel = variableNode?.AccessLevel ?? 0,
                        MinimumSamplingInterval = variableNode?.MinimumSamplingInterval ?? 0,
                        Historizing = variableNode?.Historizing ?? false
                    };

                    // Export Value only if requested
                    if (options.ExportValues && variableNode?.Value != null)
                    {
                        state.Value = variableNode.Value;
                    }

                    // Export UserAccessLevel only if requested
                    if (options.ExportUserAccessLevel)
                    {
                        state.UserAccessLevel = variableNode?.UserAccessLevel ?? 0;
                    }

                    if (variableNode?.ArrayDimensions != null && variableNode.ArrayDimensions.Count > 0)
                    {
                        state.ArrayDimensions = new ReadOnlyList<uint>(variableNode.ArrayDimensions);
                    }

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.Method:
                {
                    var methodNode = node as IMethod;
                    var state = new MethodState(null)
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        Executable = methodNode?.Executable ?? false,
                        UserExecutable = methodNode?.UserExecutable ?? false
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.ObjectType:
                {
                    var objectTypeNode = node as IObjectType;
                    var state = new BaseObjectTypeState()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        IsAbstract = objectTypeNode?.IsAbstract ?? false
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.VariableType:
                {
                    var variableTypeNode = node as IVariableType;
                    var state = new BaseDataVariableTypeState()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        IsAbstract = variableTypeNode?.IsAbstract ?? false,
                        DataType = variableTypeNode?.DataType ?? DataTypeIds.BaseDataType,
                        ValueRank = variableTypeNode?.ValueRank ?? ValueRanks.Any
                    };

                    // Export Value only if requested
                    if (options.ExportValues && variableTypeNode?.Value != null)
                    {
                        state.Value = variableTypeNode.Value;
                    }

                    if (variableTypeNode?.ArrayDimensions != null && variableTypeNode.ArrayDimensions.Count > 0)
                    {
                        state.ArrayDimensions = new ReadOnlyList<uint>(variableTypeNode.ArrayDimensions);
                    }

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.DataType:
                {
                    var dataTypeNode = node as IDataType;
                    var state = new DataTypeState()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        IsAbstract = dataTypeNode?.IsAbstract ?? false
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.ReferenceType:
                {
                    var referenceTypeNode = node as IReferenceType;
                    var state = new ReferenceTypeState()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        IsAbstract = referenceTypeNode?.IsAbstract ?? false,
                        Symmetric = referenceTypeNode?.Symmetric ?? false,
                        InverseName = referenceTypeNode?.InverseName
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                case NodeClass.View:
                {
                    var viewNode = node as IView;
                    var state = new ViewState()
                    {
                        NodeId = ExpandedNodeId.ToNodeId(node.NodeId, context.NamespaceUris),
                        BrowseName = node.BrowseName,
                        DisplayName = node.DisplayName,
                        EventNotifier = viewNode?.EventNotifier ?? 0,
                        ContainsNoLoops = viewNode?.ContainsNoLoops ?? false
                    };

                    if (node is ILocalNode localNode)
                    {
                        state.Description = localNode.Description;
                        state.WriteMask = (AttributeWriteMask)localNode.WriteMask;
                        state.UserWriteMask = (AttributeWriteMask)localNode.UserWriteMask;
                    }

                    nodeState = state;
                    break;
                }

                default:
                    return null;
            }

            // Handle references - nodeState is guaranteed to be non-null here
            if (node is ILocalNode localNodeWithRefs)
            {
                var references = localNodeWithRefs.References;
                if (references != null && references.Count > 0)
                {
                    foreach (IReference reference in references)
                    {
                        nodeState.AddReference(
                            reference.ReferenceTypeId,
                            reference.IsInverse,
                            reference.TargetId);
                    }
                }
            }

            return nodeState;
        }
    }
}
