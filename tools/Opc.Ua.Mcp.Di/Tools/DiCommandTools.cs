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
using Opc.Ua.Client;
using Opc.Ua.Di;
using Opc.Ua.Di.Client;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit DI tag writes, lock methods and parameter transfers.
    /// </summary>
    [McpServerToolType]
    public sealed class DiCommandTools
    {
        /// <summary>
        /// Initializes commands using the host's current named sessions.
        /// </summary>
        public DiCommandTools(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Writes only an explicitly selected user-configurable nameplate property.
        /// </summary>
        [McpServerTool(Name = "di_write_property", ReadOnly = false, Destructive = true)]
        [Description("Explicitly writes AssetId (String) or ComponentName (LocalizedText) on a DI component/device. " +
            "An empty value explicitly clears the field. The server enforces authorization and required locks; " +
            "this tool never acquires a lock or modifies other nameplate fields. locale is only for ComponentName.")]
        public Task<CallToolResult> WritePropertyAsync(
            [Description("Local device NodeId returned by DI discovery; e.g. ns=2;s=Device1.")]
            string deviceNodeId,
            DiWritableProperty property,
            [Description("New value in the selected property's type, without a Variant wrapper.")]
            string value,
            [Description("Optional locale for localized text; e.g. en-US.")]
            string? locale = null,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Variant typedValue = DiJson.PropertyWriteValue(property, value, locale);
                NodeId device = DiJson.ParseNodeId(deviceNodeId);
                ISession session = DiModelAccess.Session(m_sessionManager, sessionName);
                DiDeviceClient client = await DiDeviceClient.ForDeviceAsync(
                    session, device, m_sessionManager.Telemetry, token).ConfigureAwait(false);
                NodeId propertyId = await DiModelAccess.ResolveChildAsync(
                    client.Proxy, property.ToString(), token).ConfigureAwait(false);
                if (propertyId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound, "The selected DI nameplate property is not exposed.");
                }
                // DI's generated proxies expose methods and object children, but no variable-write helper.
                ArrayOf<WriteValue> writes =
                [
                    new WriteValue
                    {
                        NodeId = propertyId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(typedValue)
                    }
                ];
                WriteResponse response = await session.WriteAsync(null, writes, token).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, writes);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, writes);
                StatusCode status = response.Results[0];
                return new JsonObject
                {
                    ["error"] = !StatusCode.IsGood(status),
                    ["succeeded"] = StatusCode.IsGood(status),
                    ["deviceNodeId"] = device.ToString(),
                    ["propertyNodeId"] = propertyId.ToString(),
                    ["property"] = property.ToString(),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(status),
                    ["statusCodeValue"] = status.Code
                };
            }, ct);
        }

        /// <summary>
        /// Explicitly invokes InitLock once.
        /// </summary>
        [McpServerTool(Name = "di_acquire_lock", ReadOnly = false, Destructive = true)]
        [Description("Explicitly calls DI InitLock on the Lock child, not the device. Pass the actual lockNodeId " +
            "from di_read_device or Scales Products. Returns raw integer returnStatus; nonzero is a tool error. " +
            "No automatic renewal, retry, lock stealing or later release.")]
        public Task<CallToolResult> AcquireLockAsync(
            [Description("Actual DI Lock object NodeId, not the device or product NodeId.")]
            string lockNodeId,
            [Description("Lock context to record on the device; may be empty.")] string context,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(context);
                DiLockClient client = CreateLockClient(lockNodeId, sessionName);
                int status = await client.InitLockAsync(context, token).ConfigureAwait(false);
                return DiJson.MethodStatus(client.LockNodeId, "InitLock", status);
            }, ct);
        }

        /// <summary>
        /// Explicitly renews a lock belonging to the current session.
        /// </summary>
        [McpServerTool(Name = "di_renew_lock", ReadOnly = false, Destructive = true)]
        [Description("Explicitly calls DI RenewLock once on a Lock child. Uses the named session's ownership. " +
            "Returns the raw returnStatus; nonzero, including WrongClient, is a tool error.")]
        public Task<CallToolResult> RenewLockAsync(
            [Description("Actual DI Lock object NodeId, not the device or product NodeId.")]
            string lockNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiLockClient client = CreateLockClient(lockNodeId, sessionName);
                int status = await client.RenewLockAsync(token).ConfigureAwait(false);
                return DiJson.MethodStatus(client.LockNodeId, "RenewLock", status);
            }, ct);
        }

        /// <summary>
        /// Explicitly invokes ExitLock without attempting another client's lock.
        /// </summary>
        [McpServerTool(Name = "di_release_lock", ReadOnly = false, Destructive = true)]
        [Description("Explicitly calls DI ExitLock on a Lock child using the owning session. " +
            "Returns the raw returnStatus; nonzero is a tool error. Does not fall back to BreakLock.")]
        public Task<CallToolResult> ReleaseLockAsync(
            [Description("Actual DI Lock object NodeId, not the device or product NodeId.")]
            string lockNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiLockClient client = CreateLockClient(lockNodeId, sessionName);
                int status = await client.ExitLockAsync(token).ConfigureAwait(false);
                return DiJson.MethodStatus(client.LockNodeId, "ExitLock", status);
            }, ct);
        }

        /// <summary>
        /// Explicitly breaks a lock, subject to the server's administrative authorization.
        /// </summary>
        [McpServerTool(Name = "di_break_lock", ReadOnly = false, Destructive = true)]
        [Description("Administrative recovery: explicitly calls DI BreakLock, potentially removing another " +
            "client's lock. Requires deliberate authorization. Raw returnStatus is preserved; nonzero is an error.")]
        public Task<CallToolResult> BreakLockAsync(
            [Description("Actual DI Lock object NodeId, not the device or product NodeId.")]
            string lockNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiLockClient client = CreateLockClient(lockNodeId, sessionName);
                int status = await client.BreakLockAsync(token).ConfigureAwait(false);
                return DiJson.MethodStatus(client.LockNodeId, "BreakLock", status);
            }, ct);
        }

        /// <summary>
        /// Explicitly initiates a parameter transfer to the device.
        /// </summary>
        [McpServerTool(Name = "di_transfer_to_device", ReadOnly = false, Destructive = true)]
        [Description("Explicitly starts DI TransferToDevice on the TransferServices child, applying the offline " +
            "parameter set to the device. Uses DiTransferClient; initialization refusals remain errors. " +
            "Returns transferId for di_fetch_transfer_results. Does not acquire a lock or wait for completion.")]
        public Task<CallToolResult> TransferToDeviceAsync(
            [Description("Actual DI TransferServices child NodeId, not the device.")]
            string transferServicesNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiTransferClient client = CreateTransferClient(transferServicesNodeId, sessionName);
                int transferId = await client.TransferToDeviceAsync(token).ConfigureAwait(false);
                return TransferStarted(client, transferId);
            }, ct);
        }

        /// <summary>
        /// Explicitly initiates a transfer from the device into the offline parameter set.
        /// </summary>
        [McpServerTool(Name = "di_transfer_from_device", ReadOnly = false, Destructive = true)]
        [Description("Explicitly starts DI TransferFromDevice, updating the offline parameter set from the device. " +
            "Uses DiTransferClient; initialization refusals remain errors. Returns transferId for bounded result " +
            "reads. This is a mutation even though its direction is from the device. No automatic locking.")]
        public Task<CallToolResult> TransferFromDeviceAsync(
            [Description("Actual DI TransferServices child NodeId, not the device.")]
            string transferServicesNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiTransferClient client = CreateTransferClient(transferServicesNodeId, sessionName);
                int transferId = await client.TransferFromDeviceAsync(token).ConfigureAwait(false);
                return TransferStarted(client, transferId);
            }, ct);
        }

        /// <summary>
        /// Fetches one bounded result chunk with an explicit sequence continuation.
        /// </summary>
        [McpServerTool(Name = "di_fetch_transfer_results", ReadOnly = true, Destructive = false)]
        [Description("Fetches exactly one bounded DI transfer-result chunk using the generated proxy. Start with " +
            "sequenceNumber=0, then use nextSequenceNumber until complete=true. Preserves parameter diagnostics " +
            "and raw model error returnStatus. Does not start a transfer, poll indefinitely or retain a cursor.")]
        public Task<CallToolResult> FetchTransferResultsAsync(
            [Description("Actual DI TransferServices child NodeId, not the device.")]
            string transferServicesNodeId,
            [Description("Transfer identifier returned by di_transfer_to_device or di_transfer_from_device.")]
            int transferId,
            [Description("Sequence number from the preceding response, initially zero.")] int sequenceNumber = 0,
            [Description("Maximum parameter results in this chunk, 1..500.")] int maxResults = 100,
            [Description("Omit successful parameter-transfer entries and retain failures.")]
            bool omitGoodResults = false,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfNegative(sequenceNumber);
                ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 500);
                DiTransferClient client = CreateTransferClient(transferServicesNodeId, sessionName);
                // StreamAsync hides chunk sequence numbers; use the generated method for resumable bounded reads.
                var proxy = new TransferServicesTypeClient(
                    client.Session, client.TransferServicesNodeId, client.Telemetry);
                ExtensionObject payload = await proxy.FetchTransferResultDataAsync(
                    transferId, sequenceNumber, maxResults, omitGoodResults, token).ConfigureAwait(false);
                JsonObject result = DiJson.TransferChunk(
                    payload, sequenceNumber, maxResults, client.Session.MessageContext);
                result["transferServicesNodeId"] = client.TransferServicesNodeId.ToString();
                result["transferId"] = transferId;
                return result;
            }, ct);
        }

        /// <summary>
        /// Creates a lock client without retaining a proxy or automatically changing ownership.
        /// </summary>
        private DiLockClient CreateLockClient(string lockNodeId, string? sessionName)
        {
            NodeId nodeId = DiJson.ParseNodeId(lockNodeId);
            return new DiLockClient(
                DiModelAccess.Session(m_sessionManager, sessionName), nodeId, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Creates a transfer client on the current named session.
        /// </summary>
        private DiTransferClient CreateTransferClient(string transferServicesNodeId, string? sessionName)
        {
            NodeId nodeId = DiJson.ParseNodeId(transferServicesNodeId);
            return new DiTransferClient(
                DiModelAccess.Session(m_sessionManager, sessionName), nodeId, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Reports transfer initiation separately from eventual completion.
        /// </summary>
        private static JsonObject TransferStarted(DiTransferClient client, int transferId)
        {
            return new JsonObject
            {
                ["started"] = true,
                ["transferServicesNodeId"] = client.TransferServicesNodeId.ToString(),
                ["transferId"] = transferId,
                ["nextSequenceNumber"] = 0
            };
        }

        /// <summary>
        /// The host-owned registry; no session, proxy, lock or transfer is retained.
        /// </summary>
        private readonly OpcUaSessionManager m_sessionManager;
    }
}
