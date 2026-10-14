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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded named-runtime workflows, deliberately separate from the legacy until-stop UDP runtime.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubApplicationTools
    {
        /// <summary>
        /// Creates tools over the injectable direct runtime facade.
        /// </summary>
        public PubSubApplicationTools(McpPubSubRuntimeManager runtimes)
        {
            m_runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        }

        /// <summary>
        /// Reports executable backends and missing host infrastructure without network I/O.
        /// </summary>
        [McpServerTool(Name = "pubsub_capabilities", ReadOnly = true)]
        [Description("Return available PubSub backends and host permission without activation. " +
            "Kafka reports its missing all-broker policy seam.")]
        public CallToolResult Capabilities()
        {
            return McpCompanionTools.Result(m_runtimes.Capabilities());
        }

        /// <summary>
        /// Validates only a typed in-memory schema.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_validate", ReadOnly = true)]
        [Description("Return validation and metadata for a typed PubSub plan without network or secret I/O. " +
            "Validation does not grant activation permission.")]
        public Task<CallToolResult> ValidateAsync(
            [Description("Application identity and settings; e.g. mode=Publisher and writerGroupId=1.")]
            PubSubApplicationSettingsInput input,
            [Description("Exact transport endpoint, interface or broker topics; credentials use protected references.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return ValidateAsync(input.ToApplication(connection, fields), ct);
        }

        /// <summary>
        /// Validates an existing direct application plan.
        /// </summary>
        public Task<CallToolResult> ValidateAsync(PubSubApplicationInput input, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(
                _ => ValueTask.FromResult<JsonNode?>(m_runtimes.Validate(input)), ct);
        }

        /// <summary>
        /// Creates a new stopped, caller-owned application.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_create")]
        [Description("Create a named, stopped application. Returns an opaque caller-bound lease; does not open " +
            "a transport or replace the legacy default. Host/caller runtime quotas and credential expiry apply.")]
        public Task<CallToolResult> CreateAsync(
            [Description("Named application settings and publisher identity; creation never implies start.")]
            PubSubApplicationSettingsInput input,
            [Description("Exact transport endpoint and host-authorized connection settings.")]
            PubSubConnectionInput connection,
            [Description(PubSubMcpFilters.FieldsDescription)]
            ArrayOf<PubSubFieldInput> fields, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return CreateAsync(input.ToApplication(connection, fields), ct);
        }

        /// <summary>
        /// Creates an application from an existing direct plan.
        /// </summary>
        public Task<CallToolResult> CreateAsync(PubSubApplicationInput input, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.CreateAsync(input, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Lists only the current caller's application leases.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_list", ReadOnly = true)]
        [Description("List this caller's named PubSub application leases and expiries. A public name is not a handle " +
            "or authorization token; this does not list another caller's applications.")]
        public CallToolResult List()
        {
            return McpCompanionTools.Result(m_runtimes.List());
        }

        /// <summary>
        /// Starts the selected owned application.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_start")]
        [Description("Start one owned application by handle. Enforces real socket/topic/interface/MAC/VLAN policy " +
            "and fails if the selected backend is unavailable. Returns runtime state, metadata and diagnostics; " +
            "does not start a UA server process.")]
        public Task<CallToolResult> StartAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.StartAsync(handle, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Stops one transport without removing its lease or schema.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_stop", Destructive = true)]
        [Description("Stop one owned application's transport. Retains its configuration, sample and bounded " +
            "receive buffer until removal/expiry. Returns runtime state and diagnostics. " +
            "Use remove to release the runtime quota slot.")]
        public Task<CallToolResult> StopAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.StopAsync(handle, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Explicitly removes an owned application.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_remove", Destructive = true)]
        [Description("Remove an owned application and return its handle and removal status. " +
            "Drains owned work and releases quota without closing borrowed sessions or host stores.")]
        public Task<CallToolResult> RemoveAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.RemoveAsync(handle, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Reads exact current state and metadata.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_status", ReadOnly = true)]
        [Description("Return one owned application's real state, PublisherId discriminator, metadata/version, " +
            "buffer counts/bytes, overflow and SDK diagnostics. Never activates a stopped transport.")]
        public Task<CallToolResult> StatusAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.StatusAsync(handle, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Publishes explicit typed fields.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_publish")]
        [Description("Atomically update declared fields in a running publisher. Values must match declared types and " +
            "sizes. Returns local sample-update confirmation; sends on the next sample interval. " +
            "Success is not an end-to-end delivery acknowledgement.")]
        public Task<CallToolResult> PublishAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle,
            [Description("Native array of exact field names and typed JSON values; e.g. name=Count,value=7.")]
            ArrayOf<PubSubFieldValueInput> fields, CancellationToken ct = default)
        {
            return PublishAsync(handle, new PubSubPublishInput { Fields = fields }, ct);
        }

        /// <summary>
        /// Updates a publisher from an existing direct sample input.
        /// </summary>
        public Task<CallToolResult> PublishAsync(
            string handle, PubSubPublishInput input, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.PublishAsync(handle, input, token).ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Reads a bounded receive page.
        /// </summary>
        [McpServerTool(Name = "pubsub_application_read", ReadOnly = true)]
        [Description("Return a bounded subscriber page with UA field quality/timestamps and drop/remaining counts. " +
            "clear removes only the returned page. An empty stopped buffer is explicitly reported as stopped.")]
        public Task<CallToolResult> ReadAsync(
            [Description(PubSubMcpFilters.ApplicationHandleDescription)]
            string handle,
            [Description("Maximum records in the returned page, 1..500; default 64. Host byte limits may shorten it.")]
            int maximum = 64,
            [Description("True removes only returned records from this subscriber's buffer; false leaves them queued.")]
            bool clear = false, CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
                await m_runtimes.ReadAsync(handle, maximum, clear, token).ConfigureAwait(false), ct);
        }

        private readonly McpPubSubRuntimeManager m_runtimes;
    }
}
