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
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Named declarative routes with independent authority, quotas and owned endpoint lifecycles.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubBridgeTools
    {
        /// <summary>
        /// Creates bridge tools without activating either transport.
        /// </summary>
        public PubSubBridgeTools(
            McpPubSubRuntimeManager runtimes, McpRuntimeRegistry registry, IMcpOperationPolicy policy,
            McpPubSubOptions options, ITelemetryContext telemetry, TimeProvider time)
        {
            m_runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>
        /// Validates an entirely declarative route without network I/O.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_validate", ReadOnly = true)]
        [Description("Validate a PubSub route without activation; returns validity. " +
            "Downgrades require host and route consent.")]
        public Task<CallToolResult> ValidateAsync(
            [Description("Route, e.g. name='route'; selectFields/excludeFields exclusive; omitted remaps retain ids.")]
            PubSubBridgeSettingsInput input,
            [Description("Subscriber settings, e.g. name='in'; secret references only.")]
            PubSubApplicationSettingsInput source,
            [Description("Source interface/endpoint, e.g. 'opc.udp://localhost:4849'.")]
            PubSubConnectionInput sourceConnection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> sourceFields,
            [Description("Publisher settings, e.g. name='out'; secret references only.")]
            PubSubApplicationSettingsInput target,
            [Description("Target topics/endpoint, e.g. 'mqtts://broker:8883'.")]
            PubSubConnectionInput targetConnection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> targetFields,
            [Description("Exact field rename pairs; no expressions.")]
            ArrayOf<PubSubFieldRenameInput> renameFields = default, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return ValidateAsync(input.ToBridge(
                source, sourceConnection, sourceFields, target, targetConnection, targetFields, renameFields), ct);
        }

        /// <summary>
        /// Validates an existing direct route.
        /// </summary>
        public Task<CallToolResult> ValidateAsync(PubSubBridgeInput input, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(_ =>
            {
                McpPubSubBridge.Validate(input, m_options);
                return ValueTask.FromResult<JsonNode?>(new JsonObject { ["valid"] = true, ["started"] = false });
            }, ct);
        }

        /// <summary>
        /// Creates a stopped bridge and reserves one bounded shared lease.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_create")]
        [Description("Create a stopped bridge owning source and target; returns a lease. " +
            "Requires Bridge and Runtime grants.")]
        public Task<CallToolResult> CreateAsync(
            [Description("Route, e.g. name='route'; selectFields/excludeFields exclusive; omitted remaps retain ids.")]
            PubSubBridgeSettingsInput input,
            [Description("Subscriber settings, e.g. name='in'; secret references only.")]
            PubSubApplicationSettingsInput source,
            [Description("Source interface/endpoint, e.g. 'opc.udp://localhost:4849'.")]
            PubSubConnectionInput sourceConnection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> sourceFields,
            [Description("Publisher settings, e.g. name='out'; secret references only.")]
            PubSubApplicationSettingsInput target,
            [Description("Target topics/endpoint, e.g. 'mqtts://broker:8883'.")]
            PubSubConnectionInput targetConnection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> targetFields,
            [Description("Exact field rename pairs; no expressions.")]
            ArrayOf<PubSubFieldRenameInput> renameFields = default, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return CreateAsync(input.ToBridge(
                source, sourceConnection, sourceFields, target, targetConnection, targetFields, renameFields), ct);
        }

        /// <summary>
        /// Creates a stopped bridge from an existing direct route.
        /// </summary>
        public Task<CallToolResult> CreateAsync(PubSubBridgeInput input, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                _ = m_policy.Require(McpCapability.PubSubBridge);
                _ = m_policy.Require(McpCapability.PubSubRuntime);
                McpPubSubBridge.Validate(input, m_options);
                PubSubBridgeInput snapshot = input with
                {
                    Source = m_runtimes.Snapshot(input.Source),
                    Target = m_runtimes.Snapshot(input.Target),
                    RenameFields = new ArrayOf<PubSubFieldRenameInput>(input.RenameFields.ToArray()),
                    SelectFields = new ArrayOf<string>(input.SelectFields.ToArray()),
                    ExcludeFields = new ArrayOf<string>(input.ExcludeFields.ToArray()),
                    PromoteFields = new ArrayOf<string>(input.PromoteFields.ToArray())
                };
                var dependencies = new ArrayOf<McpSecretDependency>(
                    m_runtimes.Dependencies(snapshot.Source).ToList()
                        .Concat(m_runtimes.Dependencies(snapshot.Target).ToList()).ToArray());
                McpRuntimeInfo info = await m_registry.OpenAsync(
                    snapshot.Name, McpCapability.PubSubBridge, dependencies,
                    _ => ValueTask.FromResult(new McpPubSubBridge(
                        snapshot, m_runtimes, m_options, m_telemetry, m_time)), token).ConfigureAwait(false);
                JsonObject result = McpPubSubRuntimeManager.Lease(info);
                result["started"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Lists only the current caller's bridge leases.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_list", ReadOnly = true)]
        [Description("List this caller's named bridge leases and expiries without activating either endpoint.")]
        public CallToolResult List()
        {
            var result = new JsonArray();
            foreach (McpRuntimeInfo info in m_registry.List().ToList()
                .Where(info => info.Capability == McpCapability.PubSubBridge))
            {
                result.Add((JsonNode)McpPubSubRuntimeManager.Lease(info));
            }
            return McpCompanionTools.Result(result);
        }

        /// <summary>
        /// Starts one explicit owned bridge.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_start", Destructive = true)]
        [Description("Start one owned bridge by handle after exact endpoint/topic/interface checks. Uses the shipped " +
            "transcoder; target cyclic publishing is disabled so only forwarded records are emitted. " +
            "Returns route state and source/target diagnostics.")]
        public Task<CallToolResult> StartAsync(
            [Description(PubSubMcpFilters.BridgeHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return ExecuteAsync(handle, (bridge, token) => bridge.StartAsync(token), ct);
        }

        /// <summary>
        /// Stops forwarding and drains both owned transports.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_stop", Destructive = true)]
        [Description("Stop one bridge: detach forwarding, drain the source then close the target. Retains the named " +
            "lease for explicit restart; does not close host-owned UA sessions. " +
            "Returns route state and source/target diagnostics.")]
        public Task<CallToolResult> StopAsync(
            [Description(PubSubMcpFilters.BridgeHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return ExecuteAsync(handle, (bridge, token) => bridge.StopAsync(token), ct);
        }

        /// <summary>
        /// Reads exact route and endpoint state.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_status", ReadOnly = true)]
        [Description("Return bridge state, source/target diagnostics, successfully forwarded records and bounded " +
            "output refusals without network activation.")]
        public Task<CallToolResult> StatusAsync(
            [Description(PubSubMcpFilters.BridgeHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return ExecuteAsync(handle, (bridge, token) => bridge.StatusAsync(token), ct);
        }

        /// <summary>
        /// Removes one bridge and releases its shared quota slot.
        /// </summary>
        [McpServerTool(Name = "pubsub_bridge_remove", Destructive = true)]
        [Description("Remove one owned bridge and return its handle and removal status. " +
            "Drains both private applications without affecting another caller's lease.")]
        public Task<CallToolResult> RemoveAsync(
            [Description(PubSubMcpFilters.BridgeHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                m_runtimes.RequireHandleKind(handle, McpCapability.PubSubBridge);
                await m_registry.CloseAsync(handle, token).ConfigureAwait(false);
                return new JsonObject { ["removed"] = true, ["handle"] = handle };
            }, ct);
        }

        /// <summary>
        /// Acquires only the exact owned bridge implementation under the shared deadline.
        /// </summary>
        private Task<CallToolResult> ExecuteAsync(
            string handle, Func<McpPubSubBridge, CancellationToken, ValueTask<JsonObject>> operation,
            CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                McpRuntimeUse<McpPubSubBridge> use = await m_registry.AcquireAsync<McpPubSubBridge>(
                    handle, McpCapability.PubSubBridge, token).ConfigureAwait(false);
                await using (use.ConfigureAwait(false))
                {
                    return await operation(use.Runtime, use.CancellationToken).ConfigureAwait(false);
                }
            }, ct);
        }

        private readonly McpPubSubRuntimeManager m_runtimes;
        private readonly McpRuntimeRegistry m_registry;
        private readonly IMcpOperationPolicy m_policy;
        private readonly McpPubSubOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_time;
    }
}
