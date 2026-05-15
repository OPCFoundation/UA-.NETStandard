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
    /// MCP tools for OPC UA View Service Set (Part 4, Section 5.8).
    /// </summary>
    [McpServerToolType]
    public sealed class ViewServiceTools
    {
        /// <summary>
        /// Browse the OPC UA address space.
        /// </summary>
        [McpServerTool(Name = "Browse")]
        [Description("Browse the OPC UA server address space starting from a given node. Returns child references (nodes) of the specified node.")]
        public static async Task<string> BrowseAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID to browse from, e.g. 'i=85' (Objects folder), 'i=84' (Root). Default is Objects folder.")] string? nodeId = null,
            [Description("Browse direction: 'Forward' (default), 'Inverse', or 'Both'")] string direction = "Forward",
            [Description("Reference type to follow, e.g. 'i=33' (HierarchicalReferences). Default is HierarchicalReferences.")] string? referenceTypeId = null,
            [Description("Include subtypes of the reference type (default: true)")] bool includeSubtypes = true,
            [Description("Node class mask filter (default: 0 = all classes)")] uint nodeClassMask = 0,
            [Description("Maximum references to return (default: 0 = server decides)")] uint maxResults = 0,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                BrowseDirection browseDirection = direction.ToUpperInvariant() switch
                {
                    "INVERSE" => BrowseDirection.Inverse,
                    "BOTH" => BrowseDirection.Both,
                    _ => BrowseDirection.Forward
                };

                ArrayOf<BrowseDescription> browseDescription =
                [
                    new BrowseDescription
                    {
                        NodeId = nodeId != null ? OpcUaJsonHelper.ParseNodeId(nodeId) : ObjectIds.ObjectsFolder,
                        BrowseDirection = browseDirection,
                        ReferenceTypeId = referenceTypeId != null
                            ? OpcUaJsonHelper.ParseNodeId(referenceTypeId)
                            : ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = nodeClassMask,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ];

                BrowseResponse response = await session.BrowseAsync(
                    null,
                    null,
                    maxResults,
                    browseDescription,
                    ct).ConfigureAwait(false);

                BrowseResult result = response.Results[0];
                List<Dictionary<string, object?>> references = result.References.IsNull
                    ? []
                    : [.. result.References.ToArray()!.Select(OpcUaJsonHelper.ReferenceDescriptionToDict)];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(result.StatusCode),
                    ["continuationPoint"] = !result.ContinuationPoint.IsNull
                        ? result.ContinuationPoint.ToBase64() : null,
                    ["references"] = references
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
        /// Continue a browse operation using a continuation point.
        /// </summary>
        [McpServerTool(Name = "BrowseNext")]
        [Description("Continue a previously started browse operation using the continuation point returned from Browse.")]
        public static async Task<string> BrowseNextAsync(
            OpcUaSessionManager sessionManager,
            [Description("The continuation point from a previous Browse or BrowseNext call (base64 string)")] string continuationPoint,
            [Description("If true, releases the continuation point without returning results (default: false)")] bool releaseContinuationPoint = false,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<ByteString> continuationPoints =
                [
                    Convert.FromBase64String(continuationPoint).ToByteString()
                ];

                BrowseNextResponse response = await session.BrowseNextAsync(
                    null,
                    releaseContinuationPoint,
                    continuationPoints,
                    ct).ConfigureAwait(false);

                BrowseResult result = response.Results[0];
                List<Dictionary<string, object?>> references = result.References.IsNull
                    ? []
                    : [.. result.References.ToArray()!.Select(OpcUaJsonHelper.ReferenceDescriptionToDict)];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(result.StatusCode),
                    ["continuationPoint"] = !result.ContinuationPoint.IsNull
                        ? result.ContinuationPoint.ToBase64() : null,
                    ["references"] = references
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
        /// Translate browse paths to node IDs.
        /// </summary>
        [McpServerTool(Name = "TranslateBrowsePaths")]
        [Description("Translate browse paths (starting from a node) to NodeIds. Useful for finding nodes by path rather than NodeId.")]
        public static async Task<string> TranslateBrowsePathsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Starting node ID, e.g. 'i=85' (Objects folder)")] string startingNodeId,
            [Description("Array of browse path segments, e.g. ['2:MyFolder', '2:MyVariable']")] string[] browsePath,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<RelativePathElement> elements = browsePath.Select(segment => new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = OpcUaJsonHelper.ParseQualifiedName(segment)
                }).ToArray();

                ArrayOf<BrowsePath> pathsToTranslate =
                [
                    new BrowsePath
                    {
                        StartingNode = OpcUaJsonHelper.ParseNodeId(startingNodeId),
                        RelativePath = new RelativePath { Elements = elements }
                    }
                ];

                TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    pathsToTranslate,
                    ct).ConfigureAwait(false);

                BrowsePathResult result = response.Results[0];
                List<Dictionary<string, object?>> targets = result.Targets.IsNull
                    ? []
                    : [.. result.Targets.ToArray()!.Select(t => new Dictionary<string, object?>
                {
                    ["targetId"] = t.TargetId.ToString(),
                    ["remainingPathIndex"] = t.RemainingPathIndex
                })];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(result.StatusCode),
                    ["targets"] = targets
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
        /// Register nodes for optimized repeated access.
        /// </summary>
        [McpServerTool(Name = "RegisterNodes")]
        [Description("Register nodes with the server for optimized repeated access. Returns registered node IDs that may be more efficient to use.")]
        public static async Task<string> RegisterNodesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to register")] string[] nodeIds,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<NodeId> nodesToRegister =
                    nodeIds.Select(OpcUaJsonHelper.ParseNodeId).ToArray();

                RegisterNodesResponse response = await session.RegisterNodesAsync(
                    null,
                    nodesToRegister,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["registeredNodeIds"] = response.RegisteredNodeIds.ToArray()!.Select(n => n.ToString()).ToList()
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
        /// Unregister previously registered nodes.
        /// </summary>
        [McpServerTool(Name = "UnregisterNodes")]
        [Description("Unregister nodes that were previously registered with RegisterNodes.")]
        public static async Task<string> UnregisterNodesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of registered node IDs to unregister")] string[] nodeIds,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<NodeId> nodesToUnregister =
                    nodeIds.Select(OpcUaJsonHelper.ParseNodeId).ToArray();

                UnregisterNodesResponse response = await session.UnregisterNodesAsync(
                    null,
                    nodesToUnregister,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader)
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
        /// Query the address space with a filter.
        /// </summary>
        [McpServerTool(Name = "QueryFirst")]
        [Description("Query the OPC UA address space using a type definition and filter criteria. Returns matching nodes.")]
        public static async Task<string> QueryFirstAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID of the type definition to query, e.g. 'i=58' (BaseObjectType)")] string typeDefinitionId,
            [Description("Maximum number of data sets to return (default: 100)")] uint maxDataSetsToReturn = 100,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<NodeTypeDescription> nodeTypes =
                [
                    new NodeTypeDescription
                    {
                        TypeDefinitionNode = OpcUaJsonHelper.ParseExpandedNodeId(typeDefinitionId),
                        IncludeSubTypes = true
                    }
                ];

                QueryFirstResponse response = await session.QueryFirstAsync(
                    null,
                    new ViewDescription(),
                    nodeTypes,
                    null,
                    maxDataSetsToReturn,
                    0,
                    ct).ConfigureAwait(false);

                List<Dictionary<string, object?>> queryResults = response.QueryDataSets.IsNull
                    ? []
                    : [.. response.QueryDataSets.ToArray()!.Select(ds => new Dictionary<string, object?>
                {
                    ["nodeId"] = ds.NodeId.ToString(),
                    ["typeDefinitionNode"] = ds.TypeDefinitionNode.ToString(),
                    ["values"] = ds.Values.IsNull ? null : ds.Values.ToArray()!.Select(v => OpcUaJsonHelper.VariantToObject(v)).ToList()
                })];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["queryDataSets"] = queryResults,
                    ["continuationPoint"] = !response.ContinuationPoint.IsNull
                        ? response.ContinuationPoint.ToBase64() : null
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
        /// Continue a query operation.
        /// </summary>
        [McpServerTool(Name = "QueryNext")]
        [Description("Continue a previously started query using the continuation point from QueryFirst.")]
        public static async Task<string> QueryNextAsync(
            OpcUaSessionManager sessionManager,
            [Description("Continuation point from a previous QueryFirst or QueryNext call (base64 string)")] string continuationPoint,
            [Description("If true, releases the continuation point without returning results (default: false)")] bool releaseContinuationPoint = false,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                QueryNextResponse response = await session.QueryNextAsync(
                    null,
                    releaseContinuationPoint,
                    Convert.FromBase64String(continuationPoint).ToByteString(),
                    ct).ConfigureAwait(false);

                List<Dictionary<string, object?>> queryResults = response.QueryDataSets.IsNull
                    ? []
                    : [.. response.QueryDataSets.ToArray()!.Select(ds => new Dictionary<string, object?>
                {
                    ["nodeId"] = ds.NodeId.ToString(),
                    ["typeDefinitionNode"] = ds.TypeDefinitionNode.ToString(),
                    ["values"] = ds.Values.IsNull ? null : ds.Values.ToArray()!.Select(v => OpcUaJsonHelper.VariantToObject(v)).ToList()
                })];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["queryDataSets"] = queryResults,
                    ["revisedContinuationPoint"] = !response.RevisedContinuationPoint.IsNull
                        ? response.RevisedContinuationPoint.ToBase64() : null
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
