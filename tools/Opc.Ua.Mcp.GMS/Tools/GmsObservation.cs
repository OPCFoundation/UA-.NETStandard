// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.GMS.Client;
using Opc.Ua.MachineTool;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounds consumption of the typed intermediate-result stream without implementing another subscription protocol.
    /// </summary>
    internal static class GmsObservation
    {
        /// <summary>
        /// Distinguishes setup failure from a genuinely quiet observation and disposes only the call-owned stream.
        /// </summary>
        internal static async Task<CallToolResult> IntermediateAsync(
            GmsClient client, NodeId node, ArrayOf<QualifiedName> characteristics,
            MachineToolObservationOptions options, int maximumResponseBytes,
            TimeProvider timeProvider, CancellationToken ct)
        {
            using CancellationTokenSource timeout = timeProvider.CreateCancellationTokenSource(options.Window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            if (!client.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Intermediate observation requires the shared subscription engine.");
            }
            var streaming = new StreamingSubscription(manager,
                new SubscriptionOptions { PublishingInterval = TimeSpan.FromMilliseconds(100) });
            await using var subscriptionLifetime = streaming.ConfigureAwait(false);
            var events = new JsonArray();
            var json = new GmsJson(client.Session.MessageContext);
            int encodedBytes = 0;
            bool ready = false;
            try
            {
                _ = await client.ReadSnapshotAsync(node, linked.Token).ConfigureAwait(false);
                GmsResultClient results = client.GetResults(node,
                    GmsParameters.Results(options.MaximumNotifications, maximumResponseBytes));
                await foreach (GmsIntermediateResultNotification notification in
                    results.ObserveIntermediateResultsAsync(streaming, options.MaximumNotifications, token =>
                    {
                        token.ThrowIfCancellationRequested();
                        ready = true;
                        return default;
                    }, characteristics, linked.Token).ConfigureAwait(false))
                {
                    JsonObject value = json.Intermediate(notification);
                    encodedBytes += JsonSerializer.SerializeToUtf8Bytes(
                        value, GmsJsonContext.Default.JsonObject).Length;
                    if (encodedBytes > maximumResponseBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The intermediate-event response exceeds maximumResponseBytes.");
                    }
                    events.Add((JsonNode)value);
                }
            }
            catch (OperationCanceledException error) when (
                timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (!ready)
                {
                    throw new TimeoutException(
                        "The observation window expired before the event item was ready.", error);
                }
            }
            catch (ServiceResultException error) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested &&
                (error.StatusCode == StatusCodes.BadRequestInterrupted ||
                    error.StatusCode == StatusCodes.BadRequestCancelledByClient))
            {
                if (!ready)
                {
                    throw new TimeoutException("The observation window expired during event setup.", error);
                }
            }
            ct.ThrowIfCancellationRequested();
            return GmsJson.Result(new JsonObject
            {
                ["completionReason"] = events.Count == options.MaximumNotifications ? "LimitReached" : "WindowElapsed",
                ["limitReached"] = events.Count == options.MaximumNotifications,
                ["subscriptionReady"] = ready,
                ["droppedNotifications"] = streaming.DroppedNotificationCount,
                ["events"] = events
            }, maximumResponseBytes, isError: streaming.DroppedNotificationCount != 0);
        }
    }
}
