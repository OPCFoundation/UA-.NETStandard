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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Adapter seam over an already authorized, borrowed named session with fixed node/method scope.
    /// Replacements, reconnect generations and namespace remapping fail closed, never silently rebind.
    /// </summary>
    internal sealed class McpPubSubBorrowedSession : IServerSession
    {
        /// <summary>
        /// Captures exact, preauthorized session and target identities before background execution begins.
        /// </summary>
        internal McpPubSubBorrowedSession(
            OpcUaSessionManager sessions, OpcUaSessionManager.SessionInfo info,
            string operation, ArrayOf<NodeId> nodes, McpPubSubOptions options,
            TimeProvider time, NodeId methodId = default)
        {
            m_sessions = sessions;
            m_info = info;
            m_operation = operation;
            m_nodes = nodes.ToList().ToHashSet();
            m_methodId = methodId;
            m_sessionId = info.Session.SessionId;
            m_namespaces = info.Session.NamespaceUris.ToArray();
            m_options = options;
            m_rate = new McpPubSubRateBudget(options.MaxMessagesPerSecond, time);
            m_monitor = MonitorAsync(time);
        }

        /// <summary>
        /// Gets a captured-generation invalidation signal that does not depend on a future tool invocation.
        /// </summary>
        internal Task Invalidated => m_invalidated.Task;

        /// <inheritdoc/>
        public bool IsConnected => !m_disposed && m_info.IsConnected;

        /// <summary>
        /// Static declared metadata does not request a model-change subscription.
        /// </summary>
        public event EventHandler? ModelChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <inheritdoc/>
        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RequireCurrent();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<ReadValueId> nodesToRead, CancellationToken ct = default)
        {
            RequireCurrent();
            if (m_operation != "read" ||
                nodesToRead.Count > m_options.MaxFields ||
                nodesToRead.ToList().Any(node =>
                    !m_nodes.Contains(node.NodeId) || node.AttributeId != Attributes.Value))
            {
                throw McpPubSubTransportProvider.Refused();
            }
            using CancellationTokenSource timeout = Deadline(ct);
            ReadResponse response = await m_info.Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, nodesToRead, timeout.Token).ConfigureAwait(false);
            if (response.Results.Count != nodesToRead.Count)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "The adapter Read result count is invalid.");
            }
            Interlocked.Increment(ref m_reads);
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<StatusCode>> WriteAsync(
            ArrayOf<WriteValue> nodesToWrite, CancellationToken ct = default)
        {
            RequireCurrent();
            if (m_operation != "write" ||
                nodesToWrite.Count > m_options.MaxFields ||
                nodesToWrite.ToList().Any(node => !m_nodes.Contains(node.NodeId) ||
                    node.AttributeId != Attributes.Value ||
                    !string.IsNullOrEmpty(node.IndexRange)))
            {
                throw McpPubSubTransportProvider.Refused();
            }
            _ = McpPubSubDataSetBuffer.Encode([.. nodesToWrite.ToList().Select((value, index) => new DataSetField
            {
                Name = "field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Value = value.Value.WrappedValue
            })], m_options, m_info.Session.MessageContext);
            using CancellationTokenSource timeout = Deadline(ct);
            WriteResponse response = await m_info.Session.WriteAsync(null, nodesToWrite, timeout.Token)
                .ConfigureAwait(false);
            Interlocked.Increment(ref m_writes);
            lock (m_state)
            {
                m_lastWriteResults = response.Results;
            }
            return response.Results;
        }

        /// <inheritdoc/>
        public async ValueTask<RemoteCallResult> CallAsync(
            NodeId objectId, NodeId methodId, ArrayOf<Variant> inputArguments, CancellationToken ct = default)
        {
            RequireCurrent();
            if (m_operation != "call" ||
                !m_nodes.Contains(objectId) ||
                methodId != m_methodId ||
                inputArguments.Count > m_options.MaxFields)
            {
                throw McpPubSubTransportProvider.Refused();
            }
            _ = McpPubSubDataSetBuffer.Encode([.. inputArguments.ToList().Select((value, index) => new DataSetField
            {
                Name = "argument" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Value = value
            })], m_options, m_info.Session.MessageContext);
            using CancellationTokenSource timeout = Deadline(ct);
            CallResponse response = await m_info.Session.CallAsync(null,
                [new CallMethodRequest { ObjectId = objectId, MethodId = methodId, InputArguments = inputArguments }],
                timeout.Token).ConfigureAwait(false);
            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "The adapter Call result count is invalid.");
            }
            Interlocked.Increment(ref m_calls);
            CallMethodResult result = response.Results[0];
            lock (m_state)
            {
                m_lastCallStatus = result.StatusCode;
            }
            if (result.OutputArguments.Count > m_options.MaxFields)
            {
                lock (m_state)
                {
                    m_outputsOmitted = true;
                }
                return new RemoteCallResult(result.StatusCode, []);
            }
            return new RemoteCallResult(result.StatusCode, result.OutputArguments);
        }

        /// <inheritdoc/>
        public ValueTask<IDataChangeSubscription> CreateDataChangeSubscriptionAsync(
            double publishingIntervalMs, CancellationToken ct = default)
        {
            throw new NotSupportedException("This explicit MCP adapter uses cyclic reads, not implicit subscriptions.");
        }

        /// <inheritdoc/>
        public ValueTask StartModelChangeMonitoringAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException("The MCP binding uses an immutable declared field schema.");
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> ResolveNodeIdAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RequireCurrent();
            if (!m_nodes.Contains(nodeId) && nodeId != m_methodId)
            {
                throw McpPubSubTransportProvider.Refused();
            }
            return ValueTask.FromResult(nodeId);
        }

        /// <summary>
        /// Returns actual operation counts and per-write/call statuses without caller credentials or raw output.
        /// </summary>
        internal JsonObject Status()
        {
            lock (m_state)
            {
                var statuses = new JsonArray();
                foreach (StatusCode status in m_lastWriteResults)
                {
                    statuses.Add((JsonNode?)JsonValue.Create((uint?)status.Code));
                }
                return new JsonObject
                {
                    ["sessionName"] = m_info.Name,
                    ["operation"] = m_operation,
                    ["reads"] = Interlocked.Read(ref m_reads),
                    ["writes"] = Interlocked.Read(ref m_writes),
                    ["calls"] = Interlocked.Read(ref m_calls),
                    ["lastWriteResults"] = statuses,
                    ["lastCallStatus"] = m_lastCallStatus.Code,
                    ["outputsOmitted"] = m_outputsOmitted,
                    ["metadataMode"] = "explicit immutable schema",
                    ["ownsSession"] = false
                };
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_state)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        /// <summary>
        /// Cancels only this binding's work and joins its memory-only generation monitor.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            m_disposed = true;
            await m_shutdown.CancelAsync().ConfigureAwait(false);
            await m_monitor.ConfigureAwait(false);
            m_shutdown.Dispose();
        }

        /// <summary>
        /// Revalidates captured generation and namespace identity, never an expired request AsyncLocal.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            OpcUaSessionManager.SessionInfo current = m_sessions.GetSessionInfoOrThrow(m_info.Name);
            if (!ReferenceEquals(current, m_info) ||
                current.Session.SessionId != m_sessionId ||
                !current.Session.NamespaceUris.ToArray().SequenceEqual(m_namespaces, StringComparer.Ordinal))
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                    "The adapter's captured session/model generation is no longer current.");
            }
        }

        /// <summary>
        /// Checks only in-memory host/session generations; it never opens a connection or creates a subscription.
        /// </summary>
        private async Task MonitorAsync(TimeProvider time)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
            try
            {
                while (await timer.WaitForNextTickAsync(m_shutdown.Token).ConfigureAwait(false))
                {
                    try
                    {
                        RequireCurrent();
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or ServiceResultException)
                    {
                        m_invalidated.TrySetResult();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (m_shutdown.IsCancellationRequested)
            {
            }
        }

        /// <summary>
        /// Bounds every background adapter operation independently of the original tool request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private CancellationTokenSource Deadline(CancellationToken ct)
        {
            if (!m_rate.TryTake())
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                    "The adapter operation rate is full.");
            }
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            return deadline;
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly OpcUaSessionManager.SessionInfo m_info;
        private readonly string m_operation;
        private readonly HashSet<NodeId> m_nodes;
        private readonly NodeId m_methodId;
        private readonly NodeId m_sessionId;
        private readonly string[] m_namespaces;
        private readonly McpPubSubOptions m_options;
        private readonly McpPubSubRateBudget m_rate;
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Task m_monitor;
        private readonly TaskCompletionSource m_invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? m_disposal;
        private readonly Lock m_state = new();
        private ArrayOf<StatusCode> m_lastWriteResults;
        private StatusCode m_lastCallStatus;
        private long m_reads;
        private long m_writes;
        private long m_calls;
        private bool m_outputsOmitted;
        private bool m_disposed;
    }
}
