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
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Shared bounded enumeration and error translation for companion tools.
    /// Does not dispatch operations or own sessions and subscriptions.
    /// </summary>
    public static class McpCompanionTools
    {
        /// <summary>
        /// Executes one explicit operation and preserves actionable failures as MCP tool errors.
        /// A model refusal can return an object with <c>error: true</c> and its original return code.
        /// Caller cancellation is propagated, not converted to a successful or partial result.
        /// </summary>
        public static async Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, ValueTask<JsonNode?>> operation,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            try
            {
                ct.ThrowIfCancellationRequested();
                JsonNode? result = await operation(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return Result(result);
            }
            catch (ServiceResultException exception)
            {
                return Result(new JsonObject
                {
                    ["error"] = true,
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(exception.StatusCode),
                    ["statusCodeValue"] = exception.StatusCode.Code,
                    ["message"] = exception.Message
                });
            }
            catch (Exception exception) when (
                exception is ArgumentException or FormatException or InvalidOperationException or
                    NotSupportedException or IOException or UnauthorizedAccessException or TimeoutException)
            {
                return Result(new JsonObject
                {
                    ["error"] = true,
                    ["errorType"] = exception.GetType().Name,
                    ["message"] = exception.Message
                });
            }
        }

        /// <summary>
        /// Creates matching structured and textual JSON content without JSON reflection.
        /// </summary>
        public static CallToolResult Result(JsonNode? data)
        {
            JsonObject payload = data as JsonObject ?? new JsonObject { ["data"] = data };
            bool isError = payload["error"] is JsonValue flag &&
                flag.TryGetValue(out bool error) && error;
            string json = payload.ToJsonString();
            using JsonDocument document = JsonDocument.Parse(json);
            return new CallToolResult
            {
                IsError = isError,
                StructuredContent = document.RootElement.Clone(),
                Content = [new TextContentBlock { Text = json }]
            };
        }

        /// <summary>
        /// Reads one page from a live enumeration without retaining a cursor or a proxy.
        /// Offsets refer to the current enumeration, not a durable snapshot of a changing server.
        /// </summary>
        /// <typeparam name="T">The client entry type.</typeparam>
        public static async ValueTask<JsonObject> PageAsync<T>(
            IAsyncEnumerable<T> source,
            Func<T, JsonNode?> project,
            int offset = 0,
            int maxResults = 100,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(project);
            ValidatePage(offset, maxResults);
            var items = new JsonArray();
            int index = 0;
            bool hasMore = false;
            await foreach (T entry in source.WithCancellation(ct).ConfigureAwait(false))
            {
                if (index++ < offset)
                {
                    continue;
                }
                if (items.Count == maxResults)
                {
                    hasMore = true;
                    break;
                }
                items.Add(project(entry));
            }
            return PageResult(items, offset, hasMore);
        }

        /// <summary>
        /// Projects one page of a materialized client result with explicit continuation metadata.
        /// </summary>
        /// <typeparam name="T">The client entry type.</typeparam>
        public static JsonObject Page<T>(
            ArrayOf<T> source,
            Func<T, JsonNode?> project,
            int offset = 0,
            int maxResults = 100)
        {
            ArgumentNullException.ThrowIfNull(project);
            ValidatePage(offset, maxResults);
            var items = new JsonArray();
            int end = Math.Min(source.Count, offset + maxResults);
            for (int index = offset; index < end; index++)
            {
                items.Add(project(source[index]));
            }
            return PageResult(items, offset, end < source.Count);
        }

        /// <summary>
        /// Collects a finite observation, releasing its enumerator on every exit.
        /// A duration or item limit is reported explicitly; caller cancellation still throws.
        /// </summary>
        /// <typeparam name="T">The notification type.</typeparam>
        public static async ValueTask<JsonObject> ObserveAsync<T>(
            Func<CancellationToken, IAsyncEnumerable<T>> source,
            Func<T, JsonNode?> project,
            int durationMs = 1000,
            int maxItems = 100,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(project);
            ArgumentOutOfRangeException.ThrowIfLessThan(durationMs, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(durationMs, 30_000);
            ValidatePage(0, maxItems);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(durationMs);
            var items = new JsonArray();
            string stoppedBy = "sourceCompleted";
            try
            {
                await foreach (T entry in source(window.Token).WithCancellation(window.Token).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    items.Add(project(entry));
                    if (items.Count == maxItems)
                    {
                        stoppedBy = "itemLimit";
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                stoppedBy = "durationLimit";
            }
            ct.ThrowIfCancellationRequested();
            return new JsonObject
            {
                ["items"] = items,
                ["stoppedBy"] = stoppedBy,
                ["complete"] = stoppedBy == "sourceCompleted"
            };
        }

        /// <summary>
        /// Borrows the current managed session's streaming subscription.
        /// Dispose observation enumerators, not this shared subscription.
        /// </summary>
        public static IStreamingSubscription GetStreaming(ISession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            return session is ManagedSession managed
                ? managed.DefaultStreaming
                : throw new NotSupportedException(
                    "Companion observations require a ManagedSession. Connect through the MCP connection tools " +
                    "or register an existing ManagedSession.");
        }

        /// <summary>
        /// Validates the bounded live-page parameters before enumeration.
        /// </summary>
        private static void ValidatePage(int offset, int maxResults)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, 1_000_000);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 500);
        }

        /// <summary>
        /// Builds the common live-page envelope.
        /// </summary>
        private static JsonObject PageResult(JsonArray items, int offset, bool hasMore)
        {
            return new JsonObject
            {
                ["items"] = items,
                ["offset"] = offset,
                ["nextOffset"] = hasMore ? offset + items.Count : null,
                ["hasMore"] = hasMore,
                ["consistency"] = "live"
            };
        }
    }
}
