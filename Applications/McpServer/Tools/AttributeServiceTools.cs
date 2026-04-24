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
    /// MCP tools for OPC UA Attribute Service Set (Part 4, Section 5.10).
    /// </summary>
    [McpServerToolType]
    public sealed class AttributeServiceTools
    {
        /// <summary>
        /// Read attributes of one or more nodes.
        /// </summary>
        [McpServerTool(Name = "Read")]
        [Description("Read attributes of one or more nodes. Returns the values, status codes, and timestamps for the specified node attributes.")]
        public static async Task<string> ReadAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to read, e.g. ['ns=2;s=MyVariable', 'i=2258']")] string[] nodeIds,
            [Description("Attribute to read: 'Value' (default), 'DisplayName', 'BrowseName', 'NodeClass', 'DataType', etc.")] string? attributeId = null,
            [Description("Max age of cached value in milliseconds (0 = read from device, default: 0)")] double maxAge = 0,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                uint attrId = OpcUaJsonHelper.ParseAttributeId(attributeId);

                ArrayOf<ReadValueId> nodesToRead = nodeIds.Select(nodeIdStr => new ReadValueId
                {
                    NodeId = OpcUaJsonHelper.ParseNodeId(nodeIdStr),
                    AttributeId = attrId
                }).ToArray();

                ReadResponse response = await session.ReadAsync(
                    null,
                    maxAge,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                var results = new List<object>();
                for (int i = 0; i < response.Results.Count; i++)
                {
                    results.Add(new Dictionary<string, object?>
                    {
                        ["nodeId"] = nodeIds[i],
                        ["result"] = OpcUaJsonHelper.DataValueToDict(response.Results[i])
                    });
                }

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
        /// Write attributes of one or more nodes.
        /// </summary>
        [McpServerTool(Name = "Write")]
        [Description("Write values to one or more node attributes. Provide matching arrays of nodeIds and values.")]
        public static async Task<string> WriteAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to write, e.g. ['ns=2;s=MyVariable']")] string[] nodeIds,
            [Description("Array of values to write (matched by index with nodeIds)")] string[] values,
            [Description("Attribute to write (default: 'Value')")] string? attributeId = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                uint attrId = OpcUaJsonHelper.ParseAttributeId(attributeId);

                ArrayOf<WriteValue> nodesToWrite = nodeIds.Select((nodeIdStr, i) => new WriteValue
                {
                    NodeId = OpcUaJsonHelper.ParseNodeId(nodeIdStr),
                    AttributeId = attrId,
                    Value = new DataValue(new Variant(values[i]))
                }).ToArray();

                WriteResponse response = await session.WriteAsync(
                    null,
                    nodesToWrite,
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
        /// Read historical data or events from one or more nodes.
        /// </summary>
        [McpServerTool(Name = "HistoryRead")]
        [Description("Read historical data values from one or more nodes. Specify a time range to retrieve historical data.")]
        public static async Task<string> HistoryReadAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of node IDs to read history for")] string[] nodeIds,
            [Description("Start time for the history range (ISO 8601 format)")] string startTime,
            [Description("End time for the history range (ISO 8601 format)")] string endTime,
            [Description("Maximum number of values to return per node (default: 100)")] int maxValues = 100,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                var historyReadDetails = new ReadRawModifiedDetails
                {
                    StartTime = DateTime.Parse(startTime, System.Globalization.CultureInfo.InvariantCulture),
                    EndTime = DateTime.Parse(endTime, System.Globalization.CultureInfo.InvariantCulture),
                    NumValuesPerNode = (uint)maxValues,
                    IsReadModified = false,
                    ReturnBounds = false
                };

                ArrayOf<HistoryReadValueId> nodesToRead = nodeIds.Select(nodeIdStr => new HistoryReadValueId
                {
                    NodeId = OpcUaJsonHelper.ParseNodeId(nodeIdStr)
                }).ToArray();

                HistoryReadResponse response = await session.HistoryReadAsync(
                    null,
                    new ExtensionObject(historyReadDetails),
                    TimestampsToReturn.Both,
                    false,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                var results = new List<object>();
                for (int i = 0; i < response.Results.Count; i++)
                {
                    HistoryReadResult histResult = response.Results[i];
                    var resultDict = new Dictionary<string, object?>
                    {
                        ["nodeId"] = nodeIds[i],
                        ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(histResult.StatusCode)
                    };

                    if (histResult.HistoryData.TryGetEncodeable(out HistoryData historyData))
                    {
                        resultDict["dataValues"] = historyData.DataValues
                            .ToArray()!.Select(OpcUaJsonHelper.DataValueToDict)
                            .ToList();
                    }

                    results.Add(resultDict);
                }

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
        /// Update historical data or events.
        /// </summary>
        [McpServerTool(Name = "HistoryUpdate")]
        [Description("Update or delete historical data values. Use to insert, replace, or delete historical data points.")]
        public static async Task<string> HistoryUpdateAsync(
            OpcUaSessionManager sessionManager,
            [Description("Node ID to update history for")] string nodeId,
            [Description("Array of timestamps (ISO 8601 format) for the data points")] string[] timestamps,
            [Description("Array of values for the data points (matched by index with timestamps)")] string[] values,
            [Description("Update type: 'Insert', 'Replace', or 'Update' (default: 'Update')")] string updateType = "Update",
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                PerformUpdateType performUpdate = updateType.ToUpperInvariant() switch
                {
                    "INSERT" => PerformUpdateType.Insert,
                    "REPLACE" => PerformUpdateType.Replace,
                    _ => PerformUpdateType.Update
                };

                ArrayOf<DataValue> dataValues = timestamps.Select((ts, i) => new DataValue
                {
                    WrappedValue = new Variant(values[i]),
                    SourceTimestamp = DateTime.Parse(ts, System.Globalization.CultureInfo.InvariantCulture)
                }).ToArray();

                var updateDetails = new UpdateDataDetails
                {
                    NodeId = OpcUaJsonHelper.ParseNodeId(nodeId),
                    PerformInsertReplace = performUpdate,
                    UpdateValues = dataValues
                };

                ArrayOf<ExtensionObject> historyUpdateDetails =
                [
                    new ExtensionObject(updateDetails)
                ];

                HistoryUpdateResponse response = await session.HistoryUpdateAsync(
                    null,
                    historyUpdateDetails,
                    ct).ConfigureAwait(false);

                var results = response.Results.ToArray()!.Select(r => new Dictionary<string, object?>
                {
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(r.StatusCode),
                    ["operationResults"] = OpcUaJsonHelper.StatusCodesToStrings(r.OperationResults)
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
    }
}
