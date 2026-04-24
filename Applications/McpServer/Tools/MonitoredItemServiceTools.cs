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
    /// MCP tools for OPC UA MonitoredItem Service Set (Part 4, Section 5.12).
    /// </summary>
    [McpServerToolType]
    public sealed class MonitoredItemServiceTools
    {
        /// <summary>
        /// Create monitored items in a subscription.
        /// </summary>
        [McpServerTool(Name = "CreateMonitoredItems")]
        [Description("Create monitored items in a subscription to receive data change or event notifications.")]
        public static async Task<string> CreateMonitoredItemsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID to add monitored items to")] uint subscriptionId,
            [Description("Array of node IDs to monitor")] string[] nodeIds,
            [Description("Sampling interval in milliseconds (default: 1000)")] double samplingInterval = 1000,
            [Description("Queue size for notifications (default: 10)")] uint queueSize = 10,
            [Description("Discard oldest notifications when queue is full (default: true)")] bool discardOldest = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<MonitoredItemCreateRequest> itemsToCreate =
                    nodeIds.Select(id => new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = OpcUaJsonHelper.ParseNodeId(id),
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            SamplingInterval = samplingInterval,
                            QueueSize = queueSize,
                            DiscardOldest = discardOldest
                        }
                    }).ToArray();

                CreateMonitoredItemsResponse response = await session.CreateMonitoredItemsAsync(
                    null,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    itemsToCreate,
                    ct).ConfigureAwait(false);

                var results = response.Results.ToArray()!.Select(r => new Dictionary<string, object?>
                {
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(r.StatusCode),
                    ["monitoredItemId"] = r.MonitoredItemId,
                    ["revisedSamplingInterval"] = r.RevisedSamplingInterval,
                    ["revisedQueueSize"] = r.RevisedQueueSize
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
        /// Modify monitored items.
        /// </summary>
        [McpServerTool(Name = "ModifyMonitoredItems")]
        [Description("Modify parameters of existing monitored items in a subscription.")]
        public static async Task<string> ModifyMonitoredItemsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID containing the monitored items")] uint subscriptionId,
            [Description("Array of monitored item IDs to modify")] uint[] monitoredItemIds,
            [Description("New sampling interval in milliseconds")] double samplingInterval = 1000,
            [Description("New queue size")] uint queueSize = 10,
            [Description("Discard oldest (default: true)")] bool discardOldest = true,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<MonitoredItemModifyRequest> itemsToModify =
                    monitoredItemIds.Select(id => new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = id,
                        RequestedParameters = new MonitoringParameters
                        {
                            SamplingInterval = samplingInterval,
                            QueueSize = queueSize,
                            DiscardOldest = discardOldest
                        }
                    }).ToArray();

                ModifyMonitoredItemsResponse response = await session.ModifyMonitoredItemsAsync(
                    null,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    itemsToModify,
                    ct).ConfigureAwait(false);

                var results = response.Results.ToArray()!.Select(r => new Dictionary<string, object?>
                {
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(r.StatusCode),
                    ["revisedSamplingInterval"] = r.RevisedSamplingInterval,
                    ["revisedQueueSize"] = r.RevisedQueueSize
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
        /// Set the monitoring mode for monitored items.
        /// </summary>
        [McpServerTool(Name = "SetMonitoringMode")]
        [Description("Set the monitoring mode (Disabled, Sampling, or Reporting) for monitored items.")]
        public static async Task<string> SetMonitoringModeAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID")] uint subscriptionId,
            [Description("Array of monitored item IDs")] uint[] monitoredItemIds,
            [Description("Monitoring mode: 'Disabled', 'Sampling', or 'Reporting' (default: 'Reporting')")] string monitoringMode = "Reporting",
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                MonitoringMode mode = monitoringMode.ToUpperInvariant() switch
                {
                    "DISABLED" => MonitoringMode.Disabled,
                    "SAMPLING" => MonitoringMode.Sampling,
                    _ => MonitoringMode.Reporting
                };

                SetMonitoringModeResponse response = await session.SetMonitoringModeAsync(
                    null,
                    subscriptionId,
                    mode,
                    (ArrayOf<uint>)monitoredItemIds,
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
        /// Set triggering links between monitored items.
        /// </summary>
        [McpServerTool(Name = "SetTriggering")]
        [Description("Create or delete triggering links between a triggering item and linked items in a subscription.")]
        public static async Task<string> SetTriggeringAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID")] uint subscriptionId,
            [Description("Triggering monitored item ID")] uint triggeringItemId,
            [Description("Array of monitored item IDs to add as links (optional)")] uint[]? linksToAdd = null,
            [Description("Array of monitored item IDs to remove as links (optional)")] uint[]? linksToRemove = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                SetTriggeringResponse response = await session.SetTriggeringAsync(
                    null,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd != null ? (ArrayOf<uint>)linksToAdd : [],
                    linksToRemove != null ? (ArrayOf<uint>)linksToRemove : [],
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["addResults"] = OpcUaJsonHelper.StatusCodesToStrings(response.AddResults),
                    ["removeResults"] = OpcUaJsonHelper.StatusCodesToStrings(response.RemoveResults)
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
        /// Delete monitored items from a subscription.
        /// </summary>
        [McpServerTool(Name = "DeleteMonitoredItems")]
        [Description("Delete monitored items from a subscription.")]
        public static async Task<string> DeleteMonitoredItemsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID")] uint subscriptionId,
            [Description("Array of monitored item IDs to delete")] uint[] monitoredItemIds,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                DeleteMonitoredItemsResponse response = await session.DeleteMonitoredItemsAsync(
                    null,
                    subscriptionId,
                    (ArrayOf<uint>)monitoredItemIds,
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
