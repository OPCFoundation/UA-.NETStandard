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
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for OPC UA Node Management Service Set (Part 4, Section 5.7).
    /// </summary>
    [McpServerToolType]
    public sealed class NodeManagementServiceTools
    {
        /// <summary>
        /// Add one or more nodes to the address space.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "AddNodes")]
        [Description("Add one or more nodes to the OPC UA server address space.")]
        public static async Task<string> AddNodesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Parent node ID under which to add the new node")] string parentNodeId,
            [Description("Reference type from parent to new node, e.g. 'i=35' (Organizes)")] string referenceTypeId,
            [Description("Browse name for the new node, e.g. '2:MyNewNode'")] string browseName,
            [Description("Node class: 'Object', 'Variable', 'Method', 'ObjectType', 'VariableType', 'ReferenceType', 'DataType', 'View'")] string nodeClass,
            [Description("Type definition node ID, e.g. 'i=58' (BaseObjectType) or 'i=62' (BaseVariableType)")] string? typeDefinition = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                NodeClass nc = nodeClass.ToUpperInvariant() switch
                {
                    "OBJECT" => NodeClass.Object,
                    "VARIABLE" => NodeClass.Variable,
                    "METHOD" => NodeClass.Method,
                    "OBJECTTYPE" => NodeClass.ObjectType,
                    "VARIABLETYPE" => NodeClass.VariableType,
                    "REFERENCETYPE" => NodeClass.ReferenceType,
                    "DATATYPE" => NodeClass.DataType,
                    "VIEW" => NodeClass.View,
                    _ => throw new ArgumentException($"Unknown node class: {nodeClass}", nameof(nodeClass))
                };

                ExtensionObject nodeAttributes = nc switch
                {
                    NodeClass.Object => new ExtensionObject(new ObjectAttributes
                    {
                        DisplayName = (LocalizedText)browseName,
                        SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName
                    }),
                    NodeClass.Variable => new ExtensionObject(new VariableAttributes
                    {
                        DisplayName = (LocalizedText)browseName,
                        DataType = DataTypeIds.BaseDataType,
                        AccessLevel = AccessLevels.CurrentReadOrWrite,
                        UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                        SpecifiedAttributes = (uint)(NodeAttributesMask.DisplayName |
                            NodeAttributesMask.DataType | NodeAttributesMask.AccessLevel |
                            NodeAttributesMask.UserAccessLevel)
                    }),
                    _ => new ExtensionObject(new ObjectAttributes
                    {
                        DisplayName = (LocalizedText)browseName,
                        SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName
                    })
                };

                ArrayOf<AddNodesItem> nodesToAdd =
                [
                    new AddNodesItem
                    {
                        ParentNodeId = OpcUaJsonHelper.ParseExpandedNodeId(parentNodeId),
                        ReferenceTypeId = OpcUaJsonHelper.ParseNodeId(referenceTypeId),
                        RequestedNewNodeId = ExpandedNodeId.Null,
                        BrowseName = OpcUaJsonHelper.ParseQualifiedName(browseName),
                        NodeClass = nc,
                        NodeAttributes = nodeAttributes,
                        TypeDefinition = typeDefinition != null
                            ? OpcUaJsonHelper.ParseExpandedNodeId(typeDefinition)
                            : ExpandedNodeId.Null
                    }
                ];

                AddNodesResponse response = await session.AddNodesAsync(
                    null,
                    nodesToAdd,
                    ct).ConfigureAwait(false);

                var results = response.Results.ToArray()!.Select(r => new Dictionary<string, object?>
                {
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(r.StatusCode),
                    ["addedNodeId"] = r.AddedNodeId.ToString()
                }).ToList();

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["results"] = results
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Add references between nodes.
        /// </summary>
        [McpServerTool(Name = "AddReferences")]
        [Description("Add references (relationships) between existing nodes in the address space.")]
        public static async Task<string> AddReferencesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Source node ID")] string sourceNodeId,
            [Description("Reference type ID, e.g. 'i=35' (Organizes)")] string referenceTypeId,
            [Description("Target node ID")] string targetNodeId,
            [Description("Is the reference forward direction (default: true)")] bool isForward = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<AddReferencesItem> referencesToAdd =
                [
                    new AddReferencesItem
                    {
                        SourceNodeId = OpcUaJsonHelper.ParseNodeId(sourceNodeId),
                        ReferenceTypeId = OpcUaJsonHelper.ParseNodeId(referenceTypeId),
                        IsForward = isForward,
                        TargetNodeId = OpcUaJsonHelper.ParseExpandedNodeId(targetNodeId),
                        TargetNodeClass = NodeClass.Unspecified
                    }
                ];

                AddReferencesResponse response = await session.AddReferencesAsync(
                    null,
                    referencesToAdd,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["results"] = OpcUaJsonHelper.StatusCodesToStrings(response.Results)
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Delete nodes from the address space.
        /// </summary>
        [McpServerTool(Name = "DeleteNodes")]
        [Description("Delete one or more nodes from the OPC UA server address space.")]
        public static async Task<string> DeleteNodesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to delete")] string[] nodeIds,
            [Description("Also delete all target references (default: true)")] bool deleteTargetReferences = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<DeleteNodesItem> nodesToDelete = nodeIds.Select(id => new DeleteNodesItem
                {
                    NodeId = OpcUaJsonHelper.ParseNodeId(id),
                    DeleteTargetReferences = deleteTargetReferences
                }).ToArray();

                DeleteNodesResponse response = await session.DeleteNodesAsync(
                    null,
                    nodesToDelete,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["results"] = OpcUaJsonHelper.StatusCodesToStrings(response.Results)
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }

        /// <summary>
        /// Delete references between nodes.
        /// </summary>
        [McpServerTool(Name = "DeleteReferences")]
        [Description("Delete references (relationships) between nodes in the address space.")]
        public static async Task<string> DeleteReferencesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Source node ID")] string sourceNodeId,
            [Description("Reference type ID to delete")] string referenceTypeId,
            [Description("Target node ID")] string targetNodeId,
            [Description("Is the reference forward direction (default: true)")] bool isForward = true,
            [Description("Delete bidirectional (default: true)")] bool deleteBidirectional = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<DeleteReferencesItem> referencesToDelete =
                [
                    new DeleteReferencesItem
                    {
                        SourceNodeId = OpcUaJsonHelper.ParseNodeId(sourceNodeId),
                        ReferenceTypeId = OpcUaJsonHelper.ParseNodeId(referenceTypeId),
                        IsForward = isForward,
                        TargetNodeId = OpcUaJsonHelper.ParseExpandedNodeId(targetNodeId),
                        DeleteBidirectional = deleteBidirectional
                    }
                ];

                DeleteReferencesResponse response = await session.DeleteReferencesAsync(
                    null,
                    referencesToDelete,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["results"] = OpcUaJsonHelper.StatusCodesToStrings(response.Results)
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }
    }
}
