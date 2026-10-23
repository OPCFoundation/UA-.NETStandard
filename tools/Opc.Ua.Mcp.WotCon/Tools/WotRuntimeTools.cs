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
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Wot;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Ten explicit live WoT tools, separate from document inspection, planning and conversion.
    /// </summary>
    [McpServerToolType]
    public sealed class WotRuntimeTools
    {
        /// <summary>
        /// Creates live tools over host-owned policy, bounded preparation, existing sessions and executors.
        /// Registration and construction never activate a protocol channel.
        /// </summary>
        public WotRuntimeTools(
            WotBindingPreparation preparation, OpcUaSessionManager sessions, McpRuntimeRegistry runtimes,
            IMcpOperationPolicy policy, McpNetworkPolicy network, IWotRuntimeExecutorFactory executors,
            WotRuntimeOptions options, TimeProvider time, ITelemetryContext telemetry,
            McpSecretReferences? secrets = null)
        {
            m_preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_network = network ?? throw new ArgumentNullException(nameof(network));
            m_executors = executors ?? throw new ArgumentNullException(nameof(executors));
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_secrets = secrets;
        }

        /// <summary>
        /// Lists shipped planner/executor support without accessing a document, credential or endpoint.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_capabilities", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = false)]
        [Description("Returns WoT runtime capabilities and binder versions for HTTP(S), MQTT, Modbus TCP and OPC UA. " +
            "CoAP/BACnet/PROFINET/LoRaWAN remain planning-only. Host opt-in is not caller authorization. No I/O.")]
        public Task<CallToolResult> CapabilitiesAsync(CancellationToken ct = default)
        {
            return ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                _ = m_policy.Require(McpCapability.WotRuntime);
                var bindings = new JsonArray();
                foreach (IWotProtocolBinder binder in WotBuiltInBinders.CreateAll())
                {
                    bool executable = m_executors.Binders.Capabilities.Any(value =>
                        value.BindingUri == binder.Identity.BindingUri && !value.Capabilities.IsEmpty) &&
                        binder.Identity.Id is "w3c.http" or "w3c.mqtt" or "w3c.modbus" or "opc.opcua";
                    var operations = new JsonArray();
                    if (executable)
                    {
                        foreach (WoTBindingCapabilityEnum operation in binder.Capability.Operations)
                        {
                            string name = operation.ToString().ToLowerInvariant();
                            if (name is "readproperty" or "writeproperty" or "invokeaction" or
                                "observeproperty" or "subscribeevent")
                            {
                                operations.Add((JsonNode?)JsonValue.Create(name));
                            }
                        }
                    }
                    JsonNode binding = new JsonObject
                    {
                        ["bindingId"] = binder.Identity.Id,
                        ["version"] = binder.Identity.Version,
                        ["bindingUri"] = binder.Identity.BindingUri,
                        ["maturity"] = binder.Capability.Source.MaturityText,
                        ["executable"] = executable,
                        ["operations"] = operations,
                        ["requiresExistingNamedSession"] = binder.Identity.Id == "opc.opcua"
                    };
                    bindings.Add(binding);
                }
                return ValueTask.FromResult(new JsonObject
                {
                    ["bindings"] = bindings,
                    ["maxPayloadBytes"] = m_options.MaxPayloadBytes,
                    ["maxResultBytes"] = m_options.MaxResultBytes,
                    ["maxNotifications"] = m_options.MaxNotifications,
                    ["maxObservationMilliseconds"] = m_options.MaxObservationMilliseconds,
                    ["selection"] = "document SHA-256 + affordance + form index + binder ID/version",
                    ["redirects"] = false,
                    ["automaticMutationRetries"] = false
                });
            }, ct);
        }

        /// <summary>
        /// Prepares one selected form and reserves a caller-owned runtime; all channels remain lazy.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_open", ReadOnly = false, Destructive = false, Idempotent = false)]
        [Description("Opens a named caller-owned runtime for one SHA-256-pinned TD form and explicit operations. " +
            "Uses inline text <=64 KiB or TransferRoot files plus local dependencies. No remote TD fetching. " +
            "Checks target/operation grants; channels open lazily. Credentials are secret references only. " +
            "Returns the runtime handle/lease and pinned selection with channelsOpened=0.")]
        public Task<CallToolResult> OpenAsync(
            [Description("Caller-unique runtime name, 1..128 characters; never replaces an existing runtime.")]
            string name,
            [Description("Exactly one inline text (<=64 KiB UTF-8) or TransferRoot filePath, e.g. device.td.json. " +
                "Archive mode requires a file; references are logical document identities, not URLs to fetch.")]
            WotDocumentSource document,
            [Description("Exact 64-hex SHA-256 of the original document bytes, case-sensitive affordance, zero-based " +
                "formIndex and binder ID/version from capabilities; documentReference selects an archive member.")]
            WotRuntimeSelection selection,
            [Description("One to five distinct exact operations on the selected form: readproperty, writeproperty, " +
                "invokeaction, observeproperty or subscribeevent. No other operations become authorized.")]
            ArrayOf<string> operations,
            [Description("Explicit local dependency documents; every entry has a logical reference and text or file.")]
            ArrayOf<WotDocumentSource> dependencies = default,
            [Description("Zero or one selected security-definition mapping, containing secret references only.")]
            ArrayOf<WotRuntimeCredentialInput> credentials = default,
            [Description("Logical absolute baseUri for local relative references, documentSetArchive flag, and " +
                "existing authorized sessionName for OPC UA only. bindingProcessingMode explicitly selects the " +
                "interpretation; no option creates a session or raises host limits.")]
            WotRuntimeOpenOptions? options = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                McpCallerIdentity owner = m_policy.Require(McpCapability.WotRuntime);
                WotRuntimeJson.Text(name, 128);
                ValidateSelection(selection, operations);
                foreach (string operation in operations)
                {
                    _ = m_policy.Require(WotRuntimeTarget.Capability(operation));
                }
                options ??= new WotRuntimeOpenOptions();
                if (!Enum.IsDefined(options.BindingProcessingMode))
                {
                    throw new ArgumentOutOfRangeException(nameof(options));
                }
                ArgumentOutOfRangeException.ThrowIfGreaterThan(dependencies.Count, 255);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(credentials.Count, 1);
                var input = new WotDocumentInput
                {
                    Source = CopySource(document),
                    Dependencies = dependencies.IsNull ? [] : dependencies.ConvertAll(CopySource),
                    BaseUri = options.BaseUri,
                    DocumentSetArchive = options.DocumentSetArchive,
                    ResolveRemoteDependencies = false
                };
                WotPreparedBindingDocument prepared = await m_preparation.PrepareAsync(
                    input, WotDocumentKind.ThingDescription, selection.DocumentReference,
                    new WotBindingSelectionContext(
                        [selection.BindingId + "@" + selection.BindingVersion], [],
                        options.BindingProcessingMode),
                    token).ConfigureAwait(false);
                if (!string.Equals(prepared.Sha256, selection.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The selected document digest no longer matches.");
                }
                ValidateSourceEndpoints(prepared.Request, selection);
                WotBindingPlan plan = m_executors.Binders.Prepare(prepared.Request);
                ArrayOf<WotCompiledForm> forms = SelectForms(plan, selection, operations);
                OpcUaSessionManager.SessionInfo? sessionInfo = null;
                SessionRequestContext? sessionContext = null;
                bool transferred = false;
                try
                {
                    if (selection.BindingId == "opc.opcua")
                    {
                        WotRuntimeJson.Text(options.SessionName!, 128);
                        _ = m_policy.RequireSession(McpCapability.WotRuntime, options.SessionName!);
                        foreach (string operation in operations)
                        {
                            _ = m_policy.RequireSession(WotRuntimeTarget.Capability(operation), options.SessionName!);
                        }
                        sessionInfo = m_sessions.GetSessionInfoOrThrow(options.SessionName);
                        ValidateEndpoint(forms[0], sessionInfo);
                        sessionContext = await SessionRequestContext.CaptureAsync(sessionInfo.Session, token)
                            .ConfigureAwait(false);
                    }
                    else if (options.SessionName is not null)
                    {
                        throw new ArgumentException("Only an OPC UA form can select an existing session.");
                    }
                    var pinned = new List<WotRuntimeOperation>();
                    var secretDependencies = new List<McpSecretDependency>();
                    for (int formIndex = 0; formIndex < forms.Count; formIndex++)
                    {
                        WotCompiledForm form = forms[formIndex];
                        if (form.OpToken == "observeproperty" &&
                            form.OperationInfo.PollInterval is { } interval &&
                            interval < TimeSpan.FromMilliseconds(m_options.ObserveIntervalMilliseconds))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                                "The authored polling interval is below the host minimum.");
                        }
                        if (form.Binding.Id == "opc.opcua" &&
                            form.Operation == WoTBindingCapabilityEnum.InvokeAction &&
                            (form.Addressing.Metadata.ContainsKey("componentOfCandidates") ||
                                form.Addressing.BrowsePathTarget is not null))
                        {
                            ISession session = sessionContext?.Session ??
                                throw new ServiceResultException(
                                    StatusCodes.BadSessionIdInvalid, "The selected OPC UA Session is unavailable.");
                            OpcUaWotBindingExecutor.EnforceSessionSecurity(session, form);
                            NamespaceTable namespaceUris = new(session.NamespaceUris);
                            ArrayOf<NodeId> candidates = ResolveCallReceiverCandidates(form, namespaceUris);
                            if (candidates.Count == 0)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadConfigurationError, "The Call receiver candidates are missing.");
                            }
                            if (!Uri.TryCreate(form.Endpoint.BaseUri, UriKind.Absolute, out Uri? callEndpoint))
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadConfigurationError, "The compiled OPC UA endpoint is invalid.");
                            }
                            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                            {
                                McpNetworkGrant admission = m_network.Authorize(
                                    callEndpoint,
                                    form.OperationInfo.Method,
                                    CallPreflightResource(form, candidates[candidateIndex], namespaceUris),
                                    WotRuntimeTarget.Capability(form.OpToken));
                                if (admission.Owner != owner)
                                {
                                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                                        "The caller changed while the runtime was being prepared.");
                                }
                            }
                            NodeId method = await OpcUaWotCallReceiverResolver.ResolveMethodAsync(
                                session, form, namespaceUris, token).ConfigureAwait(false);
                            OpcUaSessionManager.SessionInfo boundSession = sessionInfo!.WithSession(session);
                            await m_sessions.UaOperationGuard.AuthorizeTargetAsync(
                                boundSession, method, genericCall: true, token).ConfigureAwait(false);
                            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                            {
                                await m_sessions.UaOperationGuard.AuthorizeTargetAsync(
                                    boundSession, candidates[candidateIndex], genericCall: true, token)
                                    .ConfigureAwait(false);
                            }
                            NodeId receiver = await OpcUaWotCallReceiverResolver.ResolveAsync(
                                session, form, method, namespaceUris, token).ConfigureAwait(false);
                            form = PinCallTarget(form, method, receiver, namespaceUris);
                        }
                        var target = WotRuntimeTarget.FromForm(form, sessionContext?.Session);
                        McpNetworkGrant grant = m_network.Authorize(
                            target.Endpoint, target.Operation, target.Resource,
                            WotRuntimeTarget.Capability(form.OpToken));
                        if (grant.Owner != owner)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                                "The caller changed while the runtime was being prepared.");
                        }
                        var provider = new WotRuntimeCredentials(form, target, credentials, m_secrets, m_policy);
                        pinned.Add(new WotRuntimeOperation(form, target, grant, provider));
                        secretDependencies.AddRange(provider.Dependencies);
                    }
                    var result = new JsonObject
                    {
                        ["runtime"] = Info(new McpRuntimeInfo(new string('0', 32), name, McpCapability.WotRuntime,
                            McpRuntimeState.Ready, DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
                            DateTimeOffset.MaxValue)),
                        ["documentReference"] = prepared.DocumentReference,
                        ["sha256"] = prepared.Sha256,
                        ["affordance"] = selection.Affordance,
                        ["formIndex"] = selection.FormIndex,
                        ["bindingId"] = selection.BindingId,
                        ["bindingVersion"] = selection.BindingVersion,
                        ["operations"] = Strings(operations),
                        ["channelsOpened"] = 0
                    };
                    _ = WotRuntimeJson.Result(result, m_options.MaxResultBytes);
                    ArrayOf<Task> invalidationSignals = sessionContext is null ? [] : [sessionContext.Invalidated];
                    McpRuntimeInfo info = await m_runtimes.OpenAsync(name, McpCapability.WotRuntime,
                        [.. secretDependencies], invalidationSignals, factoryToken =>
                        {
                            factoryToken.ThrowIfCancellationRequested();
                            var runtime = new WotRuntime(prepared.DocumentReference, prepared.Sha256, selection,
                                [.. pinned], m_executors, m_policy, m_network, m_sessions, owner, m_options,
                                m_telemetry, sessionInfo, sessionContext);
                            transferred = true;
                            return ValueTask.FromResult(runtime);
                        }, token).ConfigureAwait(false);
                    result["runtime"] = Info(info);
                    return result;
                }
                finally
                {
                    if (!transferred && sessionContext is not null)
                    {
                        await sessionContext.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Lists only the current caller's live WoT handles, including cleanup states.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_list", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = false)]
        [Description("Lists only this caller's WoT runtime handles and lease/cleanup states. No endpoint I/O. " +
            "Does not renew leases or expose another caller's runtimes.")]
        public Task<CallToolResult> ListAsync(CancellationToken ct = default)
        {
            return ExecuteAsync(_ =>
            {
                var items = new JsonArray();
                foreach (McpRuntimeInfo runtime in m_runtimes.List())
                {
                    if (runtime.Capability == McpCapability.WotRuntime)
                    {
                        items.Add((JsonNode)Info(runtime));
                    }
                }
                return ValueTask.FromResult(new JsonObject { ["runtimes"] = items });
            }, ct);
        }

        /// <summary>
        /// Inspects a pinned plan and lazy-channel state without invoking any protocol operation.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_inspect", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = false)]
        [Description("Inspects this caller's runtime: pinned digest/form, authorized operation/resource selectors, " +
            "binder version and channel state. Never opens a channel or returns secret material.")]
        public Task<CallToolResult> InspectAsync(
            [Description("Opaque handle returned by wot_runtime_open.")]
            string handle,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                McpRuntimeUse<WotRuntime> use =
                    await m_runtimes.AcquireAsync<WotRuntime>(handle, McpCapability.WotRuntime, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable release = use.ConfigureAwait(false);
                return await use.Runtime.InspectAsync(use.CancellationToken).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Closes one caller-owned runtime and drains its owned resources, even after start grants are revoked.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_close", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Closes this caller's WoT runtime, cancels pending work and waits for owned channel cleanup. " +
            "Never disconnects the existing named OPC UA session. Cleanup is allowed after execution revocation. " +
            "Returns the handle and closed=true after cleanup.")]
        public Task<CallToolResult> CloseAsync(
            [Description("Opaque handle of this caller's WoT runtime.")]
            string handle,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                McpRuntimeInfo? info = m_runtimes.List().Find(value => value.Handle == handle);
                if (info is null || info.Capability != McpCapability.WotRuntime)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                        "The handle is not an owned live WoT runtime.");
                }
                await m_runtimes.CloseAsync(handle, token).ConfigureAwait(false);
                return new JsonObject { ["handle"] = handle, ["closed"] = true };
            }, ct);
        }

        /// <summary>
        /// Reads once through the exact pinned property form.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_read_property", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads the pinned property's explicitly opened readproperty operation once. Returns typed UA " +
            "JSON, quality and timestamps. HTTP/MQTT read waits and payloads are bounded; no form fallback.")]
        public Task<CallToolResult> ReadPropertyAsync(
            [Description("Opaque runtime handle with readproperty explicitly opened.")]
            string handle,
            CancellationToken ct = default)
        {
            return UseAsync(handle, "readproperty", async (channel, context, token) =>
            {
                WotReadResult read = await channel.ReadAsync(token).ConfigureAwait(false);
                JsonObject result = WotRuntimeJson.Status(read.Status);
                result["dataValue"] = WotRuntimeJson.DataValue(read.Value, context, m_options.MaxResultBytes - 512);
                return result;
            }, ct);
        }

        /// <summary>
        /// Performs one explicitly authorized property write without automatic retry.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_write_property", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Writes one explicitly typed value through the pinned writeproperty form. Requires WotWrite " +
            "in addition to WotRuntime and the exact resource grant. Returns the write status and completion " +
            "indicator; no automatic retry or protocol fallback.")]
        public Task<CallToolResult> WritePropertyAsync(
            [Description("Opaque runtime handle with writeproperty explicitly opened.")]
            string handle,
            [Description("Explicit UA built-in type and its standard JSON scalar or array value, e.g. type=Int32 " +
                "with numeric value=7. Arrays remain native JSON arrays; values are not strings containing JSON.")]
            WotRuntimeValue value,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                McpRuntimeUse<WotRuntime> use = await m_runtimes.AcquireAsync<WotRuntime>(
                    handle, McpCapability.WotWrite, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable release = use.ConfigureAwait(false);
                Variant parsed = use.Runtime.Decode(value, use.CancellationToken);
                return await use.Runtime.ExecuteAsync("writeproperty", async (channel, _, operationToken) =>
                {
                    WotWriteResult write = await channel.WriteAsync(new DataValue(parsed), operationToken)
                        .ConfigureAwait(false);
                    return WotRuntimeJson.Status(write.Status);
                }, use.CancellationToken).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Invokes an action once and retains ordered typed outputs without claiming retry-safe business semantics.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_invoke_action", ReadOnly = false, Destructive = true, Idempotent = false)]
        [Description("Invokes the pinned invokeaction operation once with ordered typed inputs. Requires WotInvoke. " +
            "HTTP/MQTT accept at most one body value; OPC UA accepts ordered method arguments. " +
            "Returns operation status and ordered typed outputs, or an explicit output-limit flag. Never retries.")]
        public Task<CallToolResult> InvokeActionAsync(
            [Description("Opaque runtime handle with invokeaction explicitly opened.")]
            string handle,
            [Description("Actual JSON array of typed input values in declaration order; not a JSON string.")]
            ArrayOf<WotRuntimeValue> inputs = default,
            CancellationToken ct = default)
        {
            return ExecuteAsync(async token =>
            {
                McpRuntimeUse<WotRuntime> use = await m_runtimes.AcquireAsync<WotRuntime>(
                    handle, McpCapability.WotInvoke, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable release = use.ConfigureAwait(false);
                use.Runtime.ValidateArguments(inputs);
                using (var buffer = new McpBoundedMemoryStream(m_options.MaxPayloadBytes, use.CancellationToken))
                {
                    JsonSerializer.Serialize(buffer, inputs,
                        WotRuntimeInputJsonContext.Default.ArrayOfWotRuntimeValue);
                }
                ArrayOf<Variant> values = inputs.IsNull
                    ? [] : inputs.ConvertAll(value => use.Runtime.Decode(value, use.CancellationToken));
                return await use.Runtime.ExecuteAsync("invokeaction", async (channel, context, operationToken) =>
                {
                    WotInvokeResult invoked = channel is IWotContextualBindingChannel contextual
                        ? await contextual.InvokeAsync(new WotInvokeRequest(values, context), operationToken)
                            .ConfigureAwait(false)
                        : await channel.InvokeAsync(values.ToList(), operationToken).ConfigureAwait(false);
                    JsonObject result = WotRuntimeJson.Status(invoked.Status);
                    var outputs = new JsonArray();
                    try
                    {
                        if (invoked.Outputs.Count > m_options.MaxInputArguments)
                        {
                            throw new IOException("The output count exceeds the host bound.");
                        }
                        foreach (DataValue output in invoked.Outputs)
                        {
                            outputs.Add(WotRuntimeJson.DataValue(output, invoked.Context ?? context,
                                m_options.MaxResultBytes - 768));
                        }
                        result["outputs"] = outputs;
                        _ = WotRuntimeJson.Result(result, m_options.MaxResultBytes);
                    }
                    catch (Exception failure) when (failure is IOException or ServiceResultException)
                    {
                        result.Remove("outputs");
                        result["outputsOmitted"] = true;
                        result["outputStatusCode"] = "BadEncodingLimitsExceeded";
                    }
                    return result;
                }, use.CancellationToken).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Collects a finite property observation and releases its subscription at the end of the call.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_observe_property", ReadOnly = true, Destructive = false, Idempotent = false)]
        [Description("Collects a count/time/byte-bounded property observation on the pinned observeproperty form. " +
            "Returns typed values with quality/timestamps and the stopping limit. Always releases the subscription.")]
        public Task<CallToolResult> ObservePropertyAsync(
            [Description("Opaque runtime handle with observeproperty explicitly opened.")]
            string handle,
            [Description("Observation window in milliseconds, 1..the advertised host maximum.")]
            int durationMilliseconds = 1000,
            [Description("Maximum returned notifications, 1..the advertised host maximum.")]
            int maxNotifications = 16,
            CancellationToken ct = default)
        {
            return ObserveAsync(handle, events: false, durationMilliseconds, maxNotifications, ct);
        }

        /// <summary>
        /// Collects a finite event subscription with typed fields and releases it before returning.
        /// </summary>
        [McpServerTool(Name = "wot_runtime_subscribe_events", ReadOnly = true, Destructive = false, Idempotent = false)]
        [Description("Collects a finite event batch on the pinned subscribeevent form. Enforces host count/time/byte " +
            "ceilings. Returns typed event fields, quality, timestamps and the stopping limit. " +
            "No subscription survives the call.")]
        public Task<CallToolResult> SubscribeEventsAsync(
            [Description("Opaque runtime handle with subscribeevent explicitly opened.")]
            string handle,
            [Description("Observation window in milliseconds, 1..the advertised host maximum.")]
            int durationMilliseconds = 1000,
            [Description("Maximum returned notifications, 1..the advertised host maximum.")]
            int maxNotifications = 16,
            CancellationToken ct = default)
        {
            return ObserveAsync(handle, events: true, durationMilliseconds, maxNotifications, ct);
        }

        /// <summary>
        /// Validates observation bounds before admission or executor activation.
        /// </summary>
        private Task<CallToolResult> ObserveAsync(
            string handle, bool events, int durationMilliseconds, int maxNotifications, CancellationToken ct)
        {
            return ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(durationMilliseconds, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(
                    durationMilliseconds, m_options.MaxObservationMilliseconds);
                ArgumentOutOfRangeException.ThrowIfLessThan(maxNotifications, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxNotifications, m_options.MaxNotifications);
                McpRuntimeUse<WotRuntime> use = await m_runtimes.AcquireAsync<WotRuntime>(
                    handle, McpCapability.WotRuntime, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable release = use.ConfigureAwait(false);
                return await use.Runtime.ExecuteAsync(events ? "subscribeevent" : "observeproperty",
                    (channel, context, operationToken) => WotRuntimeObservation.CollectAsync(
                        channel, context, events, durationMilliseconds, maxNotifications, m_options, m_time,
                        use.Runtime.RetainFailedCleanup, operationToken),
                    use.CancellationToken).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Obtains a bounded registry use and preserves an already completed executor result.
        /// </summary>
        private Task<CallToolResult> UseAsync(
            string handle, string operation,
            Func<IWotBindingChannel, IServiceMessageContext, CancellationToken, ValueTask<JsonObject>> execute,
            CancellationToken ct)
        {
            return ExecuteAsync(async token =>
            {
                McpRuntimeUse<WotRuntime> use = await m_runtimes.AcquireAsync<WotRuntime>(
                    handle, WotRuntimeTarget.Capability(operation), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable release = use.ConfigureAwait(false);
                return await use.Runtime.ExecuteAsync(operation, execute, use.CancellationToken).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Applies the common bounded, secret-free live-tool result boundary.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, ValueTask<JsonObject>> action, CancellationToken ct)
        {
            return WotRuntimeJson.ExecuteAsync(action, m_options, ct);
        }

        /// <summary>
        /// Validates the complete explicit identity before processing any source document.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateSelection(WotRuntimeSelection selection, ArrayOf<string> operations)
        {
            ArgumentNullException.ThrowIfNull(selection);
            WotRuntimeJson.Text(selection.Sha256, 64);
            if (selection.Sha256.Length != 64 || !selection.Sha256.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("The selected SHA-256 must contain exactly 64 hexadecimal digits.");
            }
            WotRuntimeJson.Text(selection.DocumentReference, 2048, empty: true);
            WotRuntimeJson.Text(selection.Affordance, 256);
            WotRuntimeJson.Text(selection.BindingId, 128);
            WotRuntimeJson.Text(selection.BindingVersion, 128);
            ArgumentOutOfRangeException.ThrowIfNegative(selection.FormIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(selection.FormIndex, 1023);
            if (selection.Kind is not
                (WotAffordanceKind.Property or WotAffordanceKind.Action or WotAffordanceKind.Event) ||
                operations.Count is < 1 or > 5)
            {
                throw new ArgumentException(
                    "Select one property/action/event form and one to five distinct operations.");
            }
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (string operation in operations)
            {
                if (!distinct.Add(operation))
                {
                    throw new ArgumentException("Requested runtime operations must be distinct.");
                }
            }
        }

        /// <summary>
        /// Selects only exact compiled operation pairs from one form; diagnostics never trigger fallback.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static ArrayOf<WotCompiledForm> SelectForms(
            WotBindingPlan plan, WotRuntimeSelection selection, ArrayOf<string> operations)
        {
            string pointer = FormPointer(selection);
            var result = new List<WotCompiledForm>();
            foreach (string operation in operations)
            {
                WotCompiledForm[] matches = [.. plan.CompiledForms.Where(form =>
                    form.JsonPointer == pointer &&
                    form.AffordanceName == selection.Affordance &&
                    form.AffordanceKind == selection.Kind &&
                    form.Binding.Id == selection.BindingId &&
                    form.Binding.Version == selection.BindingVersion &&
                    form.OpToken == operation)];
                if (plan.IsDeclarationContext || matches.Length != 1 || !matches[0].IsExecutable)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The exact selected form, binder version and operation are not executable.");
                }
                result.Add(matches[0]);
            }
            return [.. result];
        }

        /// <summary>
        /// Rejects credentials and fragments before planner normalization can omit non-transmitted URI components.
        /// </summary>
        private static void ValidateSourceEndpoints(WotBindingPlanRequest request, WotRuntimeSelection selection)
        {
            ValidateSourceAddress(request.BaseUri);
            string pointer = FormPointer(selection);
            foreach (WotAffordanceForm form in request.Forms)
            {
                if (form.JsonPointer == pointer)
                {
                    ValidateSourceAddress(form.Href);
                }
            }
        }

        /// <summary>
        /// Validates only absolute transport addresses; relative references remain the existing planner's concern.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateSourceAddress(string? address)
        {
            if (address is not null &&
                Uri.TryCreate(address, UriKind.Absolute, out Uri? endpoint) &&
                (!string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment)))
            {
                throw new ArgumentException("Live target addresses cannot carry inline credentials or fragments.");
            }
        }

        /// <summary>
        /// Resolves every receiver that may be inspected so the host guard runs before ownership browsing.
        /// </summary>
        private static ArrayOf<NodeId> ResolveCallReceiverCandidates(
            WotCompiledForm form, NamespaceTable namespaceUris)
        {
            if (form.Addressing.Metadata.TryGetValue("callObjectId", out string? explicitReceiver))
            {
                return [Resolve(explicitReceiver)];
            }
            if (!form.Addressing.Metadata.TryGetValue("componentOfCandidates", out string? json))
            {
                return [];
            }
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The captured componentOf candidate list is invalid.");
            }
            var result = new List<NodeId>();
            foreach (JsonElement value in document.RootElement.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The captured componentOf candidate list is invalid.");
                }
                result.Add(Resolve(text));
            }
            return [.. result];

            NodeId Resolve(string text)
            {
                if (!WotPortableIdentity.IsPortableNodeId(text))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                        "A Call receiver is not a portable NodeId.");
                }
                ExpandedNodeId expanded = ExpandedNodeId.Parse(text);
                NodeId node = ExpandedNodeId.ToNodeId(expanded, namespaceUris);
                if (expanded.ServerIndex != 0 ||
                    node.IsNull ||
                    namespaceUris.GetString(node.NamespaceIndex) is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A Call receiver is not present in this Session.");
                }
                return node;
            }
        }

        /// <summary>
        /// Builds an exact receiver-bound selector for admission before Method metadata resolution.
        /// </summary>
        private static string CallPreflightResource(
            WotCompiledForm form, NodeId receiver, NamespaceTable namespaceUris)
        {
            string receiverId = NodeId.ToExpandedNodeId(receiver, namespaceUris).ToString();
            if (form.Addressing.BrowsePathTarget is { } path)
            {
                var steps = new JsonArray();
                foreach (WotBrowsePathStep step in path.Elements)
                {
                    string referenceNamespace = step.ReferenceKind is
                        RelativePathFormatter.ElementType.AnyHierarchical or
                        RelativePathFormatter.ElementType.AnyComponent
                            ? Namespaces.OpcUa
                            : step.ReferenceName.NamespaceUri ?? Namespaces.OpcUa;
                    string referenceName = step.ReferenceKind switch
                    {
                        RelativePathFormatter.ElementType.AnyHierarchical => "HierarchicalReferences",
                        RelativePathFormatter.ElementType.AnyComponent => "Aggregates",
                        _ => step.ReferenceName.Name
                    };
                    steps.Add(new JsonArray(
                        JsonValue.Create(step.TargetName.NamespaceUri ?? Namespaces.OpcUa),
                        JsonValue.Create(step.TargetName.Name),
                        JsonValue.Create(referenceNamespace),
                        JsonValue.Create(referenceName),
                        JsonValue.Create(step.ReferenceKind == RelativePathFormatter.ElementType.InverseReference),
                        JsonValue.Create(step.IncludeSubtypes)));
                }
                string resource = "object=" + receiverId +
                    ";browsePath=" + Uri.EscapeDataString(path.AnchorId) +
                    "|" + Uri.EscapeDataString(steps.ToJsonString());
                WotRuntimeJson.Text(resource, 2048);
                return resource;
            }
            string methodAddress = form.Addressing.Target;
            if (string.IsNullOrEmpty(methodAddress) &&
                form.Addressing.Metadata.TryGetValue("pathHrefNodeId", out string? pathHrefNodeId))
            {
                methodAddress = pathHrefNodeId;
            }
            if (!string.IsNullOrEmpty(methodAddress))
            {
                ExpandedNodeId expanded = ExpandedNodeId.Parse(methodAddress);
                NodeId method = ExpandedNodeId.ToNodeId(expanded, namespaceUris);
                if (expanded.ServerIndex != 0 ||
                    method.IsNull ||
                    namespaceUris.GetString(method.NamespaceIndex) is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "The Call Method identity is not present in this Session.");
                }
                string methodId = NodeId.ToExpandedNodeId(method, namespaceUris).ToString();
                string resource = "object=" + receiverId + ";method=" + methodId;
                WotRuntimeJson.Text(resource, 2048);
                return resource;
            }
            throw new ServiceResultException(
                StatusCodes.BadNodeIdInvalid, "The Call form has no resolvable Method identity.");
        }

        /// <summary>
        /// Pins one already-resolved Method and receiver so policy checks and dispatch use identical identities.
        /// </summary>
        private static WotCompiledForm PinCallTarget(
            WotCompiledForm form, NodeId method, NodeId receiver, NamespaceTable namespaceUris)
        {
            ImmutableDictionary<string, string> metadata = form.Addressing.Metadata.SetItem(
                "callObjectId", NodeId.ToExpandedNodeId(receiver, namespaceUris).ToString());
            var addressing = new WotAddressingDescriptor(
                NodeId.ToExpandedNodeId(method, namespaceUris).ToString(), metadata);
            return new WotCompiledForm(
                form.Binding,
                form.AffordanceKind,
                form.AffordanceName,
                form.JsonPointer,
                form.Operation,
                form.OpToken,
                form.Endpoint,
                addressing,
                form.OperationInfo,
                form.Payload,
                form.Security,
                form.IsExecutable,
                form.TargetMapping,
                form.EventSelection,
                form.SecurityFloor)
                .WithConditionInvocation(form.ConditionInvocation)
                .WithOpcUaSecurityRequirements(form.OpcUaSecurityRequirements);
        }

        /// <summary>
        /// Identifies one authored form with the same JSON Pointer escaping used by the existing extractor.
        /// </summary>
        private static string FormPointer(WotRuntimeSelection selection)
        {
            string section = selection.Kind switch
            {
                WotAffordanceKind.Property => "properties",
                WotAffordanceKind.Action => "actions",
                _ => "events"
            };
            string escaped = selection.Affordance.Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
            return "/" +
                section +
                "/" +
                escaped +
                "/forms/" +
                selection.FormIndex.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Ensures the borrowed named session targets the origin the existing OPC UA planner compiled.
        /// The exact named-session grant additionally fixes the server path and configured identity.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateEndpoint(WotCompiledForm form, OpcUaSessionManager.SessionInfo session)
        {
            if (!Uri.TryCreate(form.Endpoint.BaseUri, UriKind.Absolute, out Uri? selected) ||
                !Uri.TryCreate(session.Endpoint.EndpointUrl, UriKind.Absolute, out Uri? actual) ||
                selected.Scheme != actual.Scheme ||
                !string.Equals(selected.IdnHost, actual.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                WotRuntimeTarget.EffectivePort(selected) != WotRuntimeTarget.EffectivePort(actual))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The named session does not match the compiled OPC UA endpoint.");
            }
        }

        /// <summary>
        /// Snapshots caller-owned mutable source DTOs before asynchronous file preparation begins.
        /// </summary>
        private static WotDocumentSource CopySource(WotDocumentSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return new WotDocumentSource
            {
                Reference = source.Reference,
                Text = source.Text,
                FilePath = source.FilePath,
                ContentType = source.ContentType
            };
        }

        /// <summary>
        /// Projects public registry state without revealing runtime implementation or caller identity.
        /// </summary>
        private static JsonObject Info(McpRuntimeInfo info)
        {
            return new JsonObject
            {
                ["handle"] = info.Handle,
                ["name"] = info.Name,
                ["state"] = info.State.ToString(),
                ["createdAt"] = info.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                ["expiresAt"] = info.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
                ["idleExpiresAt"] = info.IdleExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Preserves actual array shape in the fixed result contract.
        /// </summary>
        private static JsonArray Strings(ArrayOf<string> values)
        {
            var result = new JsonArray();
            foreach (string value in values)
            {
                result.Add((JsonNode?)JsonValue.Create(value));
            }
            return result;
        }

        private readonly WotBindingPreparation m_preparation;
        private readonly OpcUaSessionManager m_sessions;
        private readonly McpRuntimeRegistry m_runtimes;
        private readonly IMcpOperationPolicy m_policy;
        private readonly McpNetworkPolicy m_network;
        private readonly IWotRuntimeExecutorFactory m_executors;
        private readonly WotRuntimeOptions m_options;
        private readonly TimeProvider m_time;
        private readonly ITelemetryContext m_telemetry;
        private readonly McpSecretReferences? m_secrets;
    }
}
