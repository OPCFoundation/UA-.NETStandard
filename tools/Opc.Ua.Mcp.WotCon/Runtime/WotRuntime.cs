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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// An exact compiled operation with its own resource grant and reference-only credential provider.
    /// </summary>
    internal sealed record WotRuntimeOperation(
        WotCompiledForm Form, WotRuntimeTarget Target, McpNetworkGrant Grant, WotRuntimeCredentials Credentials);

    /// <summary>
    /// Owns lazy executor channels for explicitly requested operations on exactly one immutable authored form.
    /// The shared registry owns this runtime; it never owns the host's OPC UA session.
    /// </summary>
    internal sealed class WotRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Captures a prepared plan and authority, without opening any protocol connection.
        /// </summary>
        internal WotRuntime(
            string documentReference, string sha256, WotRuntimeSelection selection,
            ArrayOf<WotRuntimeOperation> operations, IWotRuntimeExecutorFactory executors,
            IMcpOperationPolicy policy, McpNetworkPolicy network, OpcUaSessionManager sessions,
            McpCallerIdentity owner, WotRuntimeOptions options, ITelemetryContext telemetry,
            OpcUaSessionManager.SessionInfo? sessionInfo = null, SessionRequestContext? sessionContext = null)
        {
            m_documentReference = documentReference;
            m_sha256 = sha256;
            m_selection = selection;
            m_executors = executors;
            m_policy = policy;
            m_network = network;
            m_sessions = sessions;
            m_owner = owner;
            m_options = options;
            m_sessionInfo = sessionInfo;
            m_sessionContext = sessionContext;
            foreach (WotRuntimeOperation operation in operations)
            {
                m_operations.Add(operation.Form.OpToken, operation);
            }
            IServiceMessageContext original = sessionContext?.Session.MessageContext ??
                ServiceMessageContext.CreateEmpty(telemetry);
            Context = new ServiceMessageContext(original, telemetry)
            {
                MaxArrayLength = Bound(original.MaxArrayLength, 4096),
                MaxStringLength = Bound(original.MaxStringLength, options.MaxPayloadBytes),
                MaxByteStringLength = Bound(original.MaxByteStringLength, options.MaxPayloadBytes),
                MaxMessageSize = Bound(original.MaxMessageSize, options.MaxResultBytes),
                MaxEncodingNestingLevels = Bound(original.MaxEncodingNestingLevels, 16)
            };
        }

        /// <summary>
        /// Gets the bounded, cloned value context for this pinned generation.
        /// </summary>
        internal IServiceMessageContext Context { get; }

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
        /// Projects immutable plan identity and current channel counts without activation.
        /// </summary>
        internal async ValueTask<JsonObject> InspectAsync(CancellationToken ct)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                var operations = new JsonArray();
                bool aliases = false;
                foreach ((string name, WotRuntimeOperation operation) in m_operations)
                {
                    JsonNode details = new JsonObject
                    {
                        ["operation"] = name,
                        ["protocolOperation"] = operation.Target.Operation,
                        ["resource"] = operation.Target.Resource,
                        ["credentialResource"] = operation.Target.CredentialResource,
                        ["channelOpen"] = m_connections.ContainsKey(name)
                    };
                    operations.Add(details);
                    aliases |= operation.Credentials.HasAlias;
                }
                return new JsonObject
                {
                    ["documentReference"] = m_documentReference,
                    ["sha256"] = m_sha256,
                    ["affordanceKind"] = m_selection.Kind.ToString(),
                    ["affordance"] = m_selection.Affordance,
                    ["formIndex"] = m_selection.FormIndex,
                    ["bindingId"] = m_selection.BindingId,
                    ["bindingVersion"] = m_selection.BindingVersion,
                    ["sessionName"] = m_sessionInfo?.Name,
                    ["operations"] = operations,
                    ["cleanupFailed"] = m_cleanupFailed,
                    ["hasAliasCredentials"] = aliases,
                    ["aliasRevocation"] = aliases ? "recheckedPerOperation; noAsyncAliasSignal" : "notApplicable"
                };
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Validates and decodes typed input before any channel is activated.
        /// </summary>
        internal Variant Decode(WotRuntimeValue input, CancellationToken ct)
        {
            return WotRuntimeJson.Decode(input, Context, m_options, ct);
        }

        /// <summary>
        /// Rejects excess HTTP/MQTT action arguments instead of letting an executor silently discard them.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal void ValidateArguments(ArrayOf<WotRuntimeValue> inputs)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(inputs.Count, m_options.MaxInputArguments);
            if (m_selection.BindingId is "w3c.http" or "w3c.mqtt" && inputs.Count > 1)
            {
                throw new ArgumentException("HTTP and MQTT actions accept at most one typed body argument.");
            }
            if (inputs.Count > 0 &&
                m_selection.BindingId == "w3c.http" &&
                m_operations.TryGetValue("invokeaction", out WotRuntimeOperation? operation) &&
                operation.Target.Operation is "GET" or "HEAD")
            {
                throw new ArgumentException("A bodyless HTTP operation cannot accept a payload argument.");
            }
        }

        /// <summary>
        /// Rechecks caller, session and exact target authority, then dispatches once on the pinned form.
        /// All channel operations, including finite observations, are serialized inside this runtime.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<JsonObject> ExecuteAsync(
            string operation,
            Func<IWotBindingChannel, IServiceMessageContext, CancellationToken, ValueTask<JsonObject>> execute,
            CancellationToken ct)
        {
            if (m_policy.Require(McpCapability.WotRuntime) != m_owner ||
                m_policy.Require(WotRuntimeTarget.Capability(operation)) != m_owner)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            JsonObject? result = null;
            WotRuntimeConnection? connection = null;
            bool ephemeral = false;
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_cleanupFailed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "An earlier resource cleanup failed. Close this runtime before further execution.");
                }
                if (!m_operations.TryGetValue(operation, out WotRuntimeOperation? selected))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "This operation was not explicitly opened on the pinned form.");
                }
                ValidateSession(selected);
                _ = m_network.Authorize(selected.Target.Endpoint, selected.Target.Operation,
                    selected.Target.Resource, WotRuntimeTarget.Capability(operation));
                await AuthorizeMutationAsync(selected, ct).ConfigureAwait(false);
                ephemeral = selected.Credentials.HasCredentials;
                if (!m_connections.TryGetValue(operation, out connection))
                {
                    connection = await m_executors.OpenAsync(selected.Form, selected.Grant,
                        selected.Credentials, m_sessionContext?.Session, ct).ConfigureAwait(false);
                    m_connections.Add(operation, connection);
                }
                ct.ThrowIfCancellationRequested();
                result = await execute(connection.Channel, Context, ct).ConfigureAwait(false);
                return result;
            }
            finally
            {
                try
                {
                    if (ephemeral && connection is not null)
                    {
                        try
                        {
                            await connection.DisposeAsync().ConfigureAwait(false);
                            m_connections.Remove(operation);
                        }
                        catch (Exception failure) when (
                            failure is not OutOfMemoryException and not StackOverflowException)
                        {
                            // Keep failed cleanup owned by the runtime, but do not mask a committed result.
                            m_cleanupFailed = true;
                            result?["cleanupFailed"] = true;
                        }
                    }
                }
                finally
                {
                    m_gate.Release();
                }
            }
        }

        /// <summary>
        /// Retains a failed subscription cleanup under runtime ownership rather than losing its handle.
        /// Called only while a bounded runtime operation owns the private execution gate.
        /// </summary>
        internal void RetainFailedCleanup(IAsyncDisposable resource)
        {
            m_cleanupFailed = true;
            m_failedResources.Add(resource);
        }

        /// <summary>
        /// Treats unlimited source contexts as subject to the runtime's stricter finite bound.
        /// </summary>
        private static int Bound(int original, int maximum)
        {
            return original > 0 ? Math.Min(original, maximum) : maximum;
        }

        /// <summary>
        /// Refuses named-session replacement or generation invalidation before touching a borrowed channel.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateSession(WotRuntimeOperation operation)
        {
            if (m_sessionInfo is null)
            {
                return;
            }
            _ = m_policy.RequireSession(McpCapability.WotRuntime, m_sessionInfo.Name);
            _ = m_policy.RequireSession(WotRuntimeTarget.Capability(operation.Form.OpToken), m_sessionInfo.Name);
            if (!ReferenceEquals(m_sessions.GetSessionInfoOrThrow(m_sessionInfo.Name), m_sessionInfo) ||
                m_sessionContext is null ||
                m_sessionContext.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid,
                    "The runtime's named OPC UA session or captured generation has changed.");
            }
        }

        /// <summary>
        /// Reuses generic UA protection so a WoT form cannot bypass typed secret-safe administration tools.
        /// Classification and mutation both use the same generation-bound SDK session.
        /// </summary>
        private async ValueTask AuthorizeMutationAsync(WotRuntimeOperation operation, CancellationToken ct)
        {
            if (m_sessionInfo is null ||
                m_sessionContext is null ||
                operation.Form.OpToken is not ("writeproperty" or "invokeaction"))
            {
                return;
            }
            ISession session = m_sessionContext.Session;
            var bound = new OpcUaSessionManager.SessionInfo
            {
                Name = m_sessionInfo.Name,
                Session = session,
                Endpoint = session.ConfiguredEndpoint.Description,
                AuthType = m_sessionInfo.AuthType
            };
            var node = ExpandedNodeId.ToNodeId(
                ExpandedNodeId.Parse(operation.Form.Addressing.Target), session.NamespaceUris);
            bool method = operation.Form.OpToken == "invokeaction";
            await m_sessions.UaOperationGuard.AuthorizeTargetAsync(bound, node, method, ct).ConfigureAwait(false);
            if (method)
            {
                var owner = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse(operation.Form.Addressing.Metadata["componentOf"]), session.NamespaceUris);
                await m_sessions.UaOperationGuard.AuthorizeTargetAsync(bound, owner, genericCall: true, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases every owned channel and local generation context, even when one cleanup fails.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task DisposeCoreAsync()
        {
            await m_gate.WaitAsync().ConfigureAwait(false);
            bool failed = false;
            try
            {
                m_disposed = true;
                foreach (WotRuntimeConnection connection in m_connections.Values)
                {
                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
                    {
                        failed = true;
                    }
                }
                foreach (IAsyncDisposable resource in m_failedResources)
                {
                    try
                    {
                        await resource.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
                    {
                        failed = true;
                    }
                }
                if (!failed)
                {
                    m_connections.Clear();
                    m_failedResources.Clear();
                }
                if (m_sessionContext is not null)
                {
                    try
                    {
                        await m_sessionContext.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
                    {
                        failed = true;
                    }
                }
            }
            finally
            {
                m_gate.Release();
                m_gate.Dispose();
            }
            if (failed)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "A live WoT resource could not be released; the runtime remains faulted.");
            }
        }

        private readonly string m_documentReference;
        private readonly string m_sha256;
        private readonly WotRuntimeSelection m_selection;
        private readonly Dictionary<string, WotRuntimeOperation> m_operations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WotRuntimeConnection> m_connections = new(StringComparer.Ordinal);
        private readonly List<IAsyncDisposable> m_failedResources = [];
        private readonly IWotRuntimeExecutorFactory m_executors;
        private readonly IMcpOperationPolicy m_policy;
        private readonly McpNetworkPolicy m_network;
        private readonly OpcUaSessionManager m_sessions;
        private readonly McpCallerIdentity m_owner;
        private readonly WotRuntimeOptions m_options;
        private readonly OpcUaSessionManager.SessionInfo? m_sessionInfo;
        private readonly SessionRequestContext? m_sessionContext;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_state = new();
        private Task? m_disposal;
        private bool m_disposed;
        private bool m_cleanupFailed;
    }
}
