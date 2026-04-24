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
    /// MCP tools for OPC UA Subscription Service Set (Part 4, Section 5.13).
    /// </summary>
    [McpServerToolType]
    public sealed class SubscriptionServiceTools
    {
        /// <summary>
        /// Create a subscription.
        /// </summary>
        [McpServerTool(Name = "CreateSubscription")]
        [Description("Create a subscription for receiving data change and event notifications.")]
        public static async Task<string> CreateSubscriptionAsync(
            OpcUaSessionManager sessionManager,
            [Description("Requested publishing interval in milliseconds (default: 1000)")] double publishingInterval = 1000,
            [Description("Requested lifetime count (default: 10)")] uint lifetimeCount = 10,
            [Description("Requested max keep-alive count (default: 2)")] uint maxKeepAliveCount = 2,
            [Description("Maximum number of notifications per publish (default: 0 = no limit)")] uint maxNotificationsPerPublish = 0,
            [Description("Whether publishing is enabled (default: true)")] bool publishingEnabled = true,
            [Description("Priority (0-255, default: 0)")] byte priority = 0,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                CreateSubscriptionResponse response = await session.CreateSubscriptionAsync(
                    null,
                    publishingInterval,
                    lifetimeCount,
                    maxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["subscriptionId"] = response.SubscriptionId,
                    ["revisedPublishingInterval"] = response.RevisedPublishingInterval,
                    ["revisedLifetimeCount"] = response.RevisedLifetimeCount,
                    ["revisedMaxKeepAliveCount"] = response.RevisedMaxKeepAliveCount
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
        /// Modify an existing subscription.
        /// </summary>
        [McpServerTool(Name = "ModifySubscription")]
        [Description("Modify parameters of an existing subscription.")]
        public static async Task<string> ModifySubscriptionAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID to modify")] uint subscriptionId,
            [Description("New publishing interval in milliseconds")] double publishingInterval = 1000,
            [Description("New lifetime count")] uint lifetimeCount = 10,
            [Description("New max keep-alive count")] uint maxKeepAliveCount = 2,
            [Description("Maximum notifications per publish (0 = no limit)")] uint maxNotificationsPerPublish = 0,
            [Description("Priority (0-255)")] byte priority = 0,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                ModifySubscriptionResponse response = await session.ModifySubscriptionAsync(
                    null,
                    subscriptionId,
                    publishingInterval,
                    lifetimeCount,
                    maxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["revisedPublishingInterval"] = response.RevisedPublishingInterval,
                    ["revisedLifetimeCount"] = response.RevisedLifetimeCount,
                    ["revisedMaxKeepAliveCount"] = response.RevisedMaxKeepAliveCount
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
        /// Set the publishing mode for one or more subscriptions.
        /// </summary>
        [McpServerTool(Name = "SetPublishingMode")]
        [Description("Enable or disable publishing for one or more subscriptions.")]
        public static async Task<string> SetPublishingModeAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of subscription IDs")] uint[] subscriptionIds,
            [Description("Whether to enable publishing (true) or disable it (false)")] bool publishingEnabled,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                SetPublishingModeResponse response = await session.SetPublishingModeAsync(
                    null,
                    publishingEnabled,
                    (ArrayOf<uint>)subscriptionIds,
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
        /// Send a publish request to get notifications.
        /// </summary>
        [McpServerTool(Name = "Publish")]
        [Description("Send a publish request to collect queued notification messages from subscriptions.")]
        public static async Task<string> PublishAsync(
            OpcUaSessionManager sessionManager,
            [Description(
                "Subscription acknowledgements - array of subscription IDs with sequence numbers to acknowledge (optional)")] uint[]? ackSubscriptionIds = null,
            [Description("Corresponding sequence numbers for the acknowledgements (matched by index)")] uint[]? ackSequenceNumbers = null,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                ArrayOf<SubscriptionAcknowledgement> acks = [];
                if (ackSubscriptionIds != null && ackSequenceNumbers != null)
                {
                    acks = Enumerable.Range(0, Math.Min(ackSubscriptionIds.Length, ackSequenceNumbers.Length))
                        .Select(i => new SubscriptionAcknowledgement
                        {
                            SubscriptionId = ackSubscriptionIds[i],
                            SequenceNumber = ackSequenceNumbers[i]
                        }).ToArray();
                }

                PublishResponse response = await session.PublishAsync(
                    null,
                    acks,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["subscriptionId"] = response.SubscriptionId,
                    ["moreNotifications"] = response.MoreNotifications,
                    ["notificationMessage"] = new Dictionary<string, object?>
                    {
                        ["sequenceNumber"] = response.NotificationMessage.SequenceNumber,
                        ["publishTime"] = response.NotificationMessage.PublishTime.ToString("o",
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["notificationDataCount"] = response.NotificationMessage.NotificationData.Count
                    },
                    ["availableSequenceNumbers"] = response.AvailableSequenceNumbers.IsNull
                        ? null : response.AvailableSequenceNumbers.ToArray()
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
        /// Republish a notification message.
        /// </summary>
        [McpServerTool(Name = "Republish")]
        [Description("Request the server to republish a previously sent notification message.")]
        public static async Task<string> RepublishAsync(
            OpcUaSessionManager sessionManager,
            [Description("Subscription ID")] uint subscriptionId,
            [Description("Sequence number of the notification message to republish")] uint retransmitSequenceNumber,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                RepublishResponse response = await session.RepublishAsync(
                    null,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct).ConfigureAwait(false);

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["notificationMessage"] = new Dictionary<string, object?>
                    {
                        ["sequenceNumber"] = response.NotificationMessage.SequenceNumber,
                        ["publishTime"] = response.NotificationMessage.PublishTime.ToString("o",
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["notificationDataCount"] = response.NotificationMessage.NotificationData.Count
                    }
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
        /// Delete subscriptions.
        /// </summary>
        [McpServerTool(Name = "DeleteSubscriptions")]
        [Description("Delete one or more subscriptions.")]
        public static async Task<string> DeleteSubscriptionsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of subscription IDs to delete")] uint[] subscriptionIds,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                DeleteSubscriptionsResponse response = await session.DeleteSubscriptionsAsync(
                    null,
                    (ArrayOf<uint>)subscriptionIds,
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
        /// Transfer subscriptions from another session.
        /// </summary>
        [McpServerTool(Name = "TransferSubscriptions")]
        [Description("Transfer subscriptions from another session to the current session.")]
        public static async Task<string> TransferSubscriptionsAsync(
            OpcUaSessionManager sessionManager,
            [Description("Array of subscription IDs to transfer")] uint[] subscriptionIds,
            [Description("Whether to send initial data change notifications (default: true)")] bool sendInitialValues = true,
            CancellationToken ct = default)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow();

            try
            {
                TransferSubscriptionsResponse response = await session.TransferSubscriptionsAsync(
                    null,
                    (ArrayOf<uint>)subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false);

                var results = response.Results.ToArray()!.Select(r => new Dictionary<string, object?>
                {
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(r.StatusCode),
                    ["availableSequenceNumbers"] = r.AvailableSequenceNumbers.IsNull
                        ? null : r.AvailableSequenceNumbers.ToArray()
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
