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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Higher-level convenience MCP tools for common OPC UA operations.
    /// These provide simpler interfaces than the raw Part 4 service tools.
    /// </summary>
    [McpServerToolType]
    public sealed class ConvenienceTools
    {
        /// <summary>
        /// Read a single variable value by NodeId.
        /// </summary>
        [McpServerTool(Name = "ReadValue")]
        [Description("Read the Value attribute of a single variable node. Simpler than the full Read tool for common read operations.")]
        public static async Task<string> ReadValueAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID of the variable to read, e.g. 'ns=2;s=MyVariable' or 'i=2258' (ServerStatus/CurrentTime)")] string nodeId,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                DataValue dataValue = await session.ReadValueAsync(
                    OpcUaJsonHelper.ParseNodeId(nodeId), ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["nodeId"] = nodeId,
                    ["value"] = OpcUaJsonHelper.VariantToObject(dataValue.WrappedValue),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(dataValue.StatusCode),
                    ["sourceTimestamp"] = dataValue.SourceTimestamp != DateTime.MinValue
                        ? dataValue.SourceTimestamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture) : null,
                    ["serverTimestamp"] = dataValue.ServerTimestamp != DateTime.MinValue
                        ? dataValue.ServerTimestamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture) : null
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
        /// Read multiple variable values by NodeId.
        /// </summary>
        [McpServerTool(Name = "ReadValues")]
        [Description("Read the Value attribute of multiple variable nodes at once.")]
        public static async Task<string> ReadValuesAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to read values from")] string[] nodeIds,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                var parsedNodeIds = nodeIds.Select(OpcUaJsonHelper.ParseNodeId).ToList();
                (ArrayOf<DataValue> dataValues, ArrayOf<ServiceResult> serviceResults) = await session.ReadValuesAsync(
                    parsedNodeIds, ct).ConfigureAwait(false);

                var results = new List<object>();
                for (int i = 0; i < nodeIds.Length; i++)
                {
                    results.Add(new Dictionary<string, object?>
                    {
                        ["nodeId"] = nodeIds[i],
                        ["value"] = OpcUaJsonHelper.VariantToObject(dataValues[i].WrappedValue),
                        ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(dataValues[i].StatusCode),
                        ["serviceResult"] = OpcUaJsonHelper.StatusCodeToString(serviceResults[i].StatusCode)
                    });
                }

                return OpcUaJsonHelper.Serialize(results);
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
        /// Write a single value to a variable node.
        /// </summary>
        [McpServerTool(Name = "WriteValue")]
        [Description("Write a value to a single variable node. Simpler than the full Write tool for common write operations.")]
        public static async Task<string> WriteValueAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID of the variable to write")] string nodeId,
            [Description("Value to write (as JSON)")] string value,
            [Description(
                "Optional data type hint: 'Boolean', 'Int32', 'UInt32', 'Int16', 'UInt16', 'Int64', 'UInt64', 'Float', 'Double', 'String', 'DateTime', 'Byte', 'SByte'")]
                 string? dataType = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                JsonElement jsonElement = JsonDocument.Parse(value).RootElement;
                Variant variant = OpcUaJsonHelper.JsonElementToVariant(jsonElement, dataType);

                ArrayOf<WriteValue> nodesToWrite =
                [
                    new WriteValue
                    {
                        NodeId = OpcUaJsonHelper.ParseNodeId(nodeId),
                        AttributeId = Attributes.Value,
                        Value = new DataValue(variant)
                    }
                ];

                WriteResponse response = await session.WriteAsync(null, nodesToWrite, ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["nodeId"] = nodeId,
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(response.Results[0])
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
        /// Recursively browse the address space from a starting node.
        /// </summary>
        [McpServerTool(Name = "BrowseAll")]
        [Description("Recursively browse the OPC UA address space from a starting node up to a specified depth. Returns a tree of references.")]
        public static async Task<string> BrowseAllAsync(
            OpcUaSessionManager sessionManager,
            [Description("Starting node ID (default: 'i=85' for Objects folder)")] string? nodeId = null,
            [Description("Maximum depth to browse (default: 2)")] int maxDepth = 2,
            [Description("Maximum total references to return (default: 100)")] int maxResults = 100,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                NodeId startNode = nodeId != null
                    ? OpcUaJsonHelper.ParseNodeId(nodeId)
                    : ObjectIds.ObjectsFolder;

                var allReferences = new List<Dictionary<string, object?>>();
                var visited = new HashSet<string>();
                var queue = new Queue<(NodeId Node, int Depth)>();
                queue.Enqueue((startNode, 0));

                while (queue.Count > 0 && allReferences.Count < maxResults)
                {
                    (NodeId currentNode, int depth) = queue.Dequeue();
                    string nodeKey = currentNode.ToString();
                    if (!visited.Add(nodeKey))
                    {
                        continue;
                    }

                    ArrayOf<BrowseDescription> browseDescription =
                    [
                        new BrowseDescription
                        {
                            NodeId = currentNode,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ];

                    BrowseResponse response = await session.BrowseAsync(
                        null, null, 0, browseDescription, ct).ConfigureAwait(false);

                    if (response.Results.Count > 0 && !response.Results[0].References.IsNull)
                    {
                        foreach (ReferenceDescription reference in response.Results[0].References)
                        {
                            if (allReferences.Count >= maxResults)
                            {
                                break;
                            }

                            Dictionary<string, object?> refDict = OpcUaJsonHelper.ReferenceDescriptionToDict(reference);
                            refDict["parentNodeId"] = nodeKey;
                            refDict["depth"] = depth + 1;
                            allReferences.Add(refDict);

                            if (depth + 1 < maxDepth)
                            {
                                var childNodeId = ExpandedNodeId.ToNodeId(
                                    reference.NodeId, session.NamespaceUris);
                                if (childNodeId != null)
                                {
                                    queue.Enqueue((childNodeId, depth + 1));
                                }
                            }
                        }
                    }
                }

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["startingNode"] = startNode.ToString(),
                    ["totalReferences"] = allReferences.Count,
                    ["references"] = allReferences
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
        /// Call a method with simplified parameters.
        /// </summary>
        [McpServerTool(Name = "CallMethod")]
        [Description("Call a method on an OPC UA server with simplified parameters. Returns output argument values.")]
        public static async Task<string> CallMethodAsync(
            OpcUaSessionManager sessionManager,
            [Description("Object node ID on which the method is defined")] string objectId,
            [Description("Method node ID to call")] string methodId,
            [Description("Input argument values as strings (optional)")] string[]? inputArguments = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                var inputArgs = new List<Variant>();
                if (inputArguments != null)
                {
                    foreach (string arg in inputArguments)
                    {
                        inputArgs.Add(new Variant(arg));
                    }
                }

                var request = new CallMethodRequest
                {
                    ObjectId = OpcUaJsonHelper.ParseNodeId(objectId),
                    MethodId = OpcUaJsonHelper.ParseNodeId(methodId),
                    InputArguments = inputArgs.ToArray(),
                };

                CallResponse response = await session.CallAsync(
                    null,
                    new CallMethodRequest[] { request },
                    ct).ConfigureAwait(false);

                CallMethodResult result = response.Results[0];

                if (StatusCode.IsBad(result.StatusCode))
                {
                    return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                    {
                        ["error"] = true,
                        ["statusCode"] = result.StatusCode.SymbolicId,
                        ["message"] = $"Method call failed: {result.StatusCode}",
                        ["inputArgumentResults"] = result.InputArgumentResults.ToArray()?
                            .Select(s => s.SymbolicId).ToList(),
                    });
                }

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["objectId"] = objectId,
                    ["methodId"] = methodId,
                    ["outputArguments"] = result.OutputArguments.ToArray()?
                        .Select(v => OpcUaJsonHelper.VariantToObject(v)).ToList() ?? [],
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
        /// Read all attributes of a single node.
        /// </summary>
        [McpServerTool(Name = "ReadNode")]
        [Description("Read all standard attributes of a single node (NodeId, BrowseName, DisplayName, Description, NodeClass, etc.).")]
        public static async Task<string> ReadNodeAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID to read, e.g. 'i=85' or 'ns=2;s=MyNode'")] string nodeId,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                NodeId parsedNodeId = OpcUaJsonHelper.ParseNodeId(nodeId);
                Node node = await session.ReadNodeAsync(parsedNodeId, ct).ConfigureAwait(false);

                var result = new Dictionary<string, object?>
                {
                    ["nodeId"] = node.NodeId.ToString(),
                    ["nodeClass"] = node.NodeClass.ToString(),
                    ["browseName"] = node.BrowseName.ToString(),
                    ["displayName"] = node.DisplayName.Text,
                    ["description"] = node.Description.Text,
                    ["writeMask"] = node.WriteMask,
                    ["userWriteMask"] = node.UserWriteMask
                };

                if (node is VariableNode variable)
                {
                    result["dataType"] = variable.DataType.ToString();
                    result["valueRank"] = variable.ValueRank;
                    result["accessLevel"] = variable.AccessLevel;
                    result["userAccessLevel"] = variable.UserAccessLevel;
                    result["historizing"] = variable.Historizing;
                    result["minimumSamplingInterval"] = variable.MinimumSamplingInterval;
                }
                else if (node is ObjectNode obj)
                {
                    result["eventNotifier"] = obj.EventNotifier;
                }
                else if (node is MethodNode method)
                {
                    result["executable"] = method.Executable;
                    result["userExecutable"] = method.UserExecutable;
                }

                return OpcUaJsonHelper.Serialize(result);
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
        /// Cancel an outstanding service request.
        /// </summary>
        [McpServerTool(Name = "Cancel")]
        [Description("Cancel an outstanding service request using its request handle.")]
        public static async Task<string> CancelAsync(
            OpcUaSessionManager sessionManager,
            [Description("The request handle of the request to cancel")] uint requestHandle,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);
            try
            {
                CancelResponse response = await session.CancelAsync(
                    null,
                    requestHandle,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["cancelCount"] = response.CancelCount
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
