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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Di;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Read-only software-update inspection and separate, explicit update mutations.
    /// </summary>
    [McpServerToolType]
    public sealed class DiSoftwareUpdateTools
    {
        /// <summary>
        /// Initializes tools using current named sessions and the host's shared file-transfer policy.
        /// </summary>
        public DiSoftwareUpdateTools(OpcUaSessionManager sessionManager, McpFileTransfers fileTransfers)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_fileTransfers = fileTransfers ?? throw new ArgumentNullException(nameof(fileTransfers));
        }

        /// <summary>
        /// Reads software versions and all four optional finite-state-machine snapshots.
        /// </summary>
        [McpServerTool(Name = "di_read_software_update", ReadOnly = true, Destructive = false)]
        [Description("Reads a SoftwareUpdate child: current/pending/fallback package versions, UpdateStatus, " +
            "VendorErrorCode, and PrepareForUpdate/Installation/Confirmation/PowerCycle state snapshots. " +
            "Each absent optional facet is supported=false, not an empty version or event stream. " +
            "Reads are sequential, not atomic. This tool never prepares, uploads, installs or reboots.")]
        public Task<CallToolResult> ReadSoftwareUpdateAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                await DiModelAccess.RequireObjectAsync(client.Session, client.SoftwareUpdateNodeId, token)
                    .ConfigureAwait(false);
                var proxy = new SoftwareUpdateTypeClient(
                    client.Session, client.SoftwareUpdateNodeId, client.Telemetry);
                SoftwareLoadingTypeClient? loading = await proxy.GetLoadingAsync(client.Telemetry, token)
                    .ConfigureAwait(false);
                return new JsonObject
                {
                    ["softwareUpdateNodeId"] = client.SoftwareUpdateNodeId.ToString(),
                    ["loading"] = await ReadLoadingAsync(client, loading, token).ConfigureAwait(false),
                    ["updateStatus"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.UpdateStatus, token)
                        .ConfigureAwait(false),
                    ["vendorErrorCode"] = await DiModelAccess.ReadValueAsync(
                        proxy, Di.BrowseNames.VendorErrorCode, token).ConfigureAwait(false),
                    ["prepareForUpdate"] = DiJson.State(
                        await client.GetPrepareForUpdateStateAsync(token).ConfigureAwait(false)),
                    ["installation"] = DiJson.State(
                        await client.GetInstallationStateAsync(token).ConfigureAwait(false)),
                    ["confirmation"] = DiJson.State(
                        await client.GetConfirmationStateAsync(token).ConfigureAwait(false)),
                    ["powerCycle"] = DiJson.State(
                        await client.GetPowerCycleStateAsync(token).ConfigureAwait(false))
                };
            }, ct);
        }

        /// <summary>
        /// Observes one state machine for a finite interval without owning the shared subscription.
        /// </summary>
        [McpServerTool(Name = "di_observe_software_update", ReadOnly = true, Destructive = false)]
        [Description("Observes transitions of one DI software-update state machine for a bounded interval. " +
            "Requires a managed session. Missing state machines are explicit errors, not empty success. " +
            "Returns stoppedBy and releases its monitored-item enumerator, never the shared subscription. " +
            "PowerCycle is observable only; this tool never reboots or invokes update methods.")]
        public Task<CallToolResult> ObserveSoftwareUpdateAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            DiSoftwareStateMachine stateMachine,
            [Description("Observation window in milliseconds, 1..30000.")] int durationMs = 1000,
            [Description("Maximum snapshots, 1..500.")] int maxItems = 100,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                DiJson.RequireDefined(stateMachine, nameof(stateMachine));
                ArgumentOutOfRangeException.ThrowIfLessThan(durationMs, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(durationMs, 30_000);
                ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(maxItems, 500);
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                FiniteStateSnapshot? initial = await DiModelAccess.StateAsync(client, stateMachine, token)
                    .ConfigureAwait(false);
                if (initial is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, $"The server does not expose {stateMachine}.");
                }
                IStreamingSubscription streaming = McpCompanionTools.GetStreaming(client.Session);
                Func<CancellationToken, IAsyncEnumerable<FiniteStateSnapshot>> source = stateMachine switch
                {
                    DiSoftwareStateMachine.PrepareForUpdate => cancellation =>
                        client.ObservePrepareForUpdateTransitionsAsync(streaming, ct: cancellation),
                    DiSoftwareStateMachine.Installation => cancellation =>
                        client.ObserveInstallationTransitionsAsync(streaming, ct: cancellation),
                    DiSoftwareStateMachine.Confirmation => cancellation =>
                        client.ObserveConfirmationTransitionsAsync(streaming, ct: cancellation),
                    DiSoftwareStateMachine.PowerCycle => cancellation =>
                        client.ObservePowerCycleTransitionsAsync(streaming, ct: cancellation),
                    _ => throw new ArgumentOutOfRangeException(nameof(stateMachine))
                };
                JsonObject result = await McpCompanionTools.ObserveAsync(
                    source, DiJson.State, durationMs, maxItems, token).ConfigureAwait(false);
                result["softwareUpdateNodeId"] = client.SoftwareUpdateNodeId.ToString();
                result["stateMachine"] = stateMachine.ToString();
                result["initialState"] = DiJson.State(initial);
                return result;
            }, ct);
        }

        /// <summary>
        /// Explicitly requests preparation, without chaining any other update step.
        /// </summary>
        [McpServerTool(Name = "di_prepare_software_update", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes PrepareForUpdate.Prepare. The device may stop normal operation. " +
            "Does not acquire a lock, upload or install. Read/observe the state to determine completion.")]
        public Task<CallToolResult> PrepareAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteCommandAsync(
                softwareUpdateNodeId, "Prepare", static (client, token) => client.PrepareAsync(token), sessionName, ct);
        }

        /// <summary>
        /// Explicitly aborts preparation without attempting to abort an installation.
        /// </summary>
        [McpServerTool(Name = "di_abort_prepare", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes PrepareForUpdate.Abort. Does not abort installation, delete a package, " +
            "or release a lock. The server decides whether this transition is currently allowed.")]
        public Task<CallToolResult> AbortPrepareAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteCommandAsync(
                softwareUpdateNodeId, "AbortPrepare",
                static (client, token) => client.AbortPrepareAsync(token), sessionName, ct);
        }

        /// <summary>
        /// Streams a host-approved file through the existing software package upload helper.
        /// </summary>
        [McpServerTool(Name = "di_upload_software_package", ReadOnly = false, Destructive = true)]
        [Description("Uploads and commits a package through Loading.FileTransfer using the DI streaming helper. " +
            "filePath must be under the host TransferRoot and within its byte limit (64 MiB by default). " +
            "No root means uploads are disabled. Does not invoke Installation; a server's DirectLoading profile " +
            "may apply software at commit. Does not lock or prepare automatically.")]
        public Task<CallToolResult> UploadSoftwarePackageAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Transfer-root-relative file name; e.g. result.bin. Never a URL.")]
            string filePath,
            [Description("Optional vendor-defined package ID suggestion, not a confirmed resulting ID.")]
            string? suggestedPackageId = null,
            [Description("Upload block size in bytes, 1..1048576; defaults to 8192.")]
            int chunkSizeBytes = SoftwareUpdateClient.DefaultUploadChunkSizeBytes,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(chunkSizeBytes, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSizeBytes, 1_048_576);
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                JsonObject result = await m_fileTransfers.UploadAsync(
                    filePath,
                    (stream, cancellation) =>
                        client.UploadPackageAsync(stream, suggestedPackageId, chunkSizeBytes, cancellation),
                    token).ConfigureAwait(false);
                result["softwareUpdateNodeId"] = client.SoftwareUpdateNodeId.ToString();
                result["uploadCompleted"] = true;
                result["suggestedPackageId"] = suggestedPackageId;
                return result;
            }, ct);
        }

        /// <summary>
        /// Explicitly requests installation of the identified package.
        /// </summary>
        [McpServerTool(Name = "di_install_software_package", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes Installation.InstallSoftwarePackage with typed metadata, a JSON array " +
            "of patch identifiers and a base64 hash. Does not upload, prepare, lock, confirm or reboot. " +
            "Method completion is not installation completion; read or observe the Installation state.")]
        public Task<CallToolResult> InstallSoftwarePackageAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Typed request using the advertised fields and explicit value types.")]
            DiInstallPackageRequest input,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ByteString hash = DiJson.PackageHash(input);
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                await client.InstallSoftwarePackageAsync(
                    input.ManufacturerUri, input.SoftwareRevision, input.PatchIdentifiers, hash, token)
                    .ConfigureAwait(false);
                return CommandCompleted(client, "InstallSoftwarePackage");
            }, ct);
        }

        /// <summary>
        /// Explicitly requests installation of already uploaded server files.
        /// </summary>
        [McpServerTool(Name = "di_install_files", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes Installation.InstallFiles with 1..500 server-side File object NodeIds. " +
            "These identities are not host file paths. Does not upload, prepare, lock, confirm or reboot. " +
            "Read or observe the state to determine whether installation actually finishes.")]
        public Task<CallToolResult> InstallFilesAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Typed request using the advertised fields and explicit value types.")]
            DiInstallFilesRequest input,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArrayOf<NodeId> files = DiJson.FileNodeIds(input);
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                await client.InstallFilesAsync(files, token).ConfigureAwait(false);
                return CommandCompleted(client, "InstallFiles");
            }, ct);
        }

        /// <summary>
        /// Explicitly invokes software uninstallation.
        /// </summary>
        [McpServerTool(Name = "di_uninstall_software", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes Installation.Uninstall. Can remove operational software; the device " +
            "enforces supported transitions and authorization. Does not silently install a fallback or reboot.")]
        public Task<CallToolResult> UninstallAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteCommandAsync(
                softwareUpdateNodeId, "Uninstall",
                static (client, token) => client.UninstallAsync(token), sessionName, ct);
        }

        /// <summary>
        /// Explicitly resumes an interrupted installation.
        /// </summary>
        [McpServerTool(Name = "di_resume_installation", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes Installation.Resume for an interrupted installation. Does not restart " +
            "preparation, retry another operation or confirm the software. Server state restrictions apply.")]
        public Task<CallToolResult> ResumeInstallationAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteCommandAsync(
                softwareUpdateNodeId, "ResumeInstallation",
                static (client, token) => client.ResumeInstallationAsync(token), sessionName, ct);
        }

        /// <summary>
        /// Explicitly confirms the installed software.
        /// </summary>
        [McpServerTool(Name = "di_confirm_software_update", ReadOnly = false, Destructive = true)]
        [Description("Explicitly invokes Confirmation.Confirm, acknowledging the installed software according " +
            "to the device's update protocol. Never invoked automatically after upload or installation.")]
        public Task<CallToolResult> ConfirmAsync(
            [Description("Actual DI SoftwareUpdate child NodeId, not the owning device.")]
            string softwareUpdateNodeId,
            [Description("Session name; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteCommandAsync(
                softwareUpdateNodeId, "Confirm", static (client, token) => client.ConfirmAsync(token), sessionName, ct);
        }

        /// <summary>
        /// Creates an update client without retaining its lazy child-proxy cache between tool calls.
        /// </summary>
        private SoftwareUpdateClient CreateClient(string softwareUpdateNodeId, string? sessionName)
        {
            NodeId nodeId = DiJson.ParseNodeId(softwareUpdateNodeId);
            return new SoftwareUpdateClient(
                DiModelAccess.Session(m_sessionManager, sessionName), nodeId, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Executes precisely one selected software-update command.
        /// </summary>
        private Task<CallToolResult> ExecuteCommandAsync(
            string softwareUpdateNodeId,
            string operation,
            Func<SoftwareUpdateClient, CancellationToken, ValueTask> command,
            string? sessionName,
            CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                SoftwareUpdateClient client = CreateClient(softwareUpdateNodeId, sessionName);
                await command(client, token).ConfigureAwait(false);
                return CommandCompleted(client, operation);
            }, ct);
        }

        /// <summary>
        /// Reports method completion without claiming the asynchronous device operation has finished.
        /// </summary>
        private static JsonObject CommandCompleted(SoftwareUpdateClient client, string operation)
        {
            return new JsonObject
            {
                ["softwareUpdateNodeId"] = client.SoftwareUpdateNodeId.ToString(),
                ["operation"] = operation,
                ["methodCompleted"] = true
            };
        }

        /// <summary>
        /// Resolves package version objects through the existing generated child proxies.
        /// </summary>
        private static async ValueTask<JsonObject> ReadLoadingAsync(
            SoftwareUpdateClient client,
            SoftwareLoadingTypeClient? loading,
            CancellationToken ct)
        {
            if (loading is null)
            {
                return DiJson.Facet(NodeId.Null);
            }
            var package = new PackageLoadingTypeClient(client.Session, loading.ObjectId, client.Telemetry);
            var cached = new CachedLoadingTypeClient(client.Session, loading.ObjectId, client.Telemetry);
            TemporaryFileTransferTypeClient? transfer = await package.GetFileTransferAsync(client.Telemetry, ct)
                .ConfigureAwait(false);
            JsonObject result = DiJson.Facet(loading.ObjectId);
            result["fileTransferNodeId"] = transfer is null ? null : transfer.ObjectId.ToString();
            result["currentVersion"] = await ReadVersionAsync(
                await package.GetCurrentVersionAsync(client.Telemetry, ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            result["pendingVersion"] = await ReadVersionAsync(
                await cached.GetPendingVersionAsync(client.Telemetry, ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            result["fallbackVersion"] = await ReadVersionAsync(
                await cached.GetFallbackVersionAsync(client.Telemetry, ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Reads named SoftwareVersionType variables with their exact UA value types.
        /// </summary>
        private static async ValueTask<JsonObject> ReadVersionAsync(
            SoftwareVersionTypeClient? proxy,
            CancellationToken ct)
        {
            if (proxy is null)
            {
                return DiJson.Facet(NodeId.Null);
            }
            JsonObject result = DiJson.Facet(proxy.ObjectId);
            result["manufacturer"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.Manufacturer, ct)
                .ConfigureAwait(false);
            result["manufacturerUri"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.ManufacturerUri, ct)
                .ConfigureAwait(false);
            result["softwareRevision"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.SoftwareRevision, ct)
                .ConfigureAwait(false);
            result["patchIdentifiers"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.PatchIdentifiers, ct)
                .ConfigureAwait(false);
            result["releaseDate"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.ReleaseDate, ct)
                .ConfigureAwait(false);
            result["changeLogReference"] = await DiModelAccess.ReadValueAsync(
                proxy, Di.BrowseNames.ChangeLogReference, ct).ConfigureAwait(false);
            result["hash"] = await DiModelAccess.ReadValueAsync(proxy, Di.BrowseNames.Hash, ct).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// The host-owned session registry.
        /// </summary>
        private readonly OpcUaSessionManager m_sessionManager;

        /// <summary>
        /// The injected, bounded local file-transfer policy.
        /// </summary>
        private readonly McpFileTransfers m_fileTransfers;
    }
}
