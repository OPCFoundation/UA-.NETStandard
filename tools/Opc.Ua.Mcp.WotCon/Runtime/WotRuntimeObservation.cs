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
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Collects a finite count/time/byte-bounded observation and always releases its existing executor subscription.
    /// </summary>
    internal sealed class WotRuntimeObservation
    {
        /// <summary>
        /// Creates a bounded callback queue with a complete-batch byte budget.
        /// </summary>
        private WotRuntimeObservation(
            IServiceMessageContext context, int maxItems, int maxBytes, CancellationToken ct)
        {
            m_context = context;
            m_maxItems = maxItems;
            m_remainingBytes = maxBytes;
            m_token = ct;
            m_queue = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(maxItems)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        /// <summary>
        /// Runs one finite observation; notifications never outlive the owning runtime operation.
        /// </summary>
        internal static async ValueTask<JsonObject> CollectAsync(
            IWotBindingChannel channel, IServiceMessageContext context, bool events,
            int durationMilliseconds, int maxItems, WotRuntimeOptions options, TimeProvider time,
            Action<IAsyncDisposable> retainFailedCleanup, CancellationToken ct)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(durationMilliseconds), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var observation = new WotRuntimeObservation(context, maxItems, options.MaxResultBytes - 768, linked.Token);
            IWotSubscription? subscription = null;
            JsonObject? result = null;
            try
            {
                subscription = events
                    ? await channel.SubscribeEventAsync(observation.Accept, linked.Token).ConfigureAwait(false)
                    : await channel.ObserveAsync(observation.Accept, linked.Token).ConfigureAwait(false);
                var items = new JsonArray();
                string stoppedBy = "sourceCompleted";
                try
                {
                    await foreach (JsonObject item in observation.m_queue.Reader.ReadAllAsync(linked.Token)
                        .ConfigureAwait(false))
                    {
                        items.Add((JsonNode)item);
                        if (items.Count == maxItems)
                        {
                            stoppedBy = "itemLimit";
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (
                    deadline.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    stoppedBy = "durationLimit";
                }
                ct.ThrowIfCancellationRequested();
                lock (observation.m_state)
                {
                    stoppedBy = observation.m_byteLimit ? "byteLimit" : stoppedBy;
                    result = WotRuntimeJson.Status(observation.m_byteLimit
                        ? StatusCodes.BadEncodingLimitsExceeded : StatusCodes.Good);
                    result["items"] = items;
                    result["stoppedBy"] = stoppedBy;
                    result["discardedNotifications"] = observation.m_discarded;
                    result["subscriptionRetained"] = false;
                }
                return result;
            }
            finally
            {
                observation.Stop();
                if (subscription is not null)
                {
                    try
                    {
                        await subscription.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception failure) when (
                        failure is not OutOfMemoryException and not StackOverflowException)
                    {
                        retainFailedCleanup(subscription);
                        if (result is not null)
                        {
                            result["cleanupFailed"] = true;
                            result["subscriptionRetained"] = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Accepts only the bounded batch; oversize events complete the queue without retaining their bodies.
        /// </summary>
        private void Accept(WotNotification notification)
        {
            lock (m_state)
            {
                if (m_stopped || m_token.IsCancellationRequested)
                {
                    return;
                }
                if (m_accepted >= m_maxItems)
                {
                    if (m_discarded < int.MaxValue)
                    {
                        m_discarded++;
                    }
                    return;
                }
                try
                {
                    JsonObject item = WotRuntimeJson.Notification(notification, m_context, m_remainingBytes);
                    using var buffer = new McpBoundedMemoryStream(m_remainingBytes, m_token);
                    using (var writer = new Utf8JsonWriter(buffer))
                    {
                        item.WriteTo(writer);
                    }
                    m_remainingBytes -= checked((int)buffer.Length) + 1;
                    if (!m_queue.Writer.TryWrite(item))
                    {
                        m_discarded++;
                        return;
                    }
                    m_accepted++;
                    if (m_accepted == m_maxItems)
                    {
                        m_queue.Writer.TryComplete();
                    }
                }
                catch (Exception failure) when (failure is IOException or ArgumentException or
                    ServiceResultException or JsonException or OperationCanceledException)
                {
                    m_byteLimit = !m_token.IsCancellationRequested;
                    m_stopped = true;
                    m_queue.Writer.TryComplete();
                }
            }
        }

        /// <summary>
        /// Detaches the callback before subscription cleanup, including failed or cancelled activation.
        /// </summary>
        private void Stop()
        {
            lock (m_state)
            {
                m_stopped = true;
                m_queue.Writer.TryComplete();
            }
        }

        private readonly IServiceMessageContext m_context;
        private readonly int m_maxItems;
        private readonly CancellationToken m_token;
        private readonly Channel<JsonObject> m_queue;
        private readonly Lock m_state = new();
        private int m_remainingBytes;
        private int m_accepted;
        private int m_discarded;
        private bool m_byteLimit;
        private bool m_stopped;
    }
}
