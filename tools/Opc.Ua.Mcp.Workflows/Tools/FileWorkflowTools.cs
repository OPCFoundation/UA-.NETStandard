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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Descriptions = Opc.Ua.Mcp.WorkflowParameterDescriptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Confined, bounded general-purpose files; protected configuration files require their dedicated tools.
    /// </summary>
    [McpServerToolType]
    public sealed class FileWorkflowTools
    {
        /// <summary>
        /// Creates file tools without owning another UA session.
        /// </summary>
        public FileWorkflowTools(
            OpcUaSessionManager sessions, ClientWorkflowOptions options,
            OpcUaMcpOptions hostOptions, McpFileTransfers transfers, IClientWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
            m_transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
            m_scopes = scopes ?? new ClientWorkflowSessionFactory();
            options.Validate();
        }

        /// <summary>
        /// Discovers actual FileDirectoryType instances rather than assuming a standard node exists.
        /// </summary>
        [McpServerTool(Name = "files_roots", ReadOnly = true)]
        [Description("Discover actual FileDirectoryType roots/subtypes beneath an explicit browse root. " +
            "Returns namespace-qualified identities and live paging. Example: rootNodeId='i=85', maxDepth=3.")]
        public Task<CallToolResult> RootsAsync(
            [Description(Descriptions.RootNodeId)] string rootNodeId = "i=85",
            [Description(Descriptions.MaxDepth)] int maxDepth = 3,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                    m_sessions.GetSessionInfoOrThrow(sessionName), token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                ISession session = scope.Access.Session;
                return await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(session, WorkflowJson.Node(session, rootNodeId),
                        ObjectTypeIds.FileDirectoryType, maxDepth, token),
                    reference => WorkflowJson.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Streams a directory's children with no recursive enumeration or full materialization.
        /// </summary>
        [McpServerTool(Name = "files_list", ReadOnly = true)]
        [Description("List one directory's immediate files/subdirectories with exact ids and browse namespaces. " +
            "Example: directoryId='i=16314', maxResults=25. Returns live page metadata; no recursive traversal.")]
        public Task<CallToolResult> ListAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.MaxResults)] int maxResults = 100,
            [Description(Descriptions.Offset)] int offset = 0,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WorkflowJson.Page(m_options, maxResults, offset);
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(directoryId, ObjectTypeIds.FileDirectoryType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                return await McpCompanionTools.PageAsync(ChildrenAsync(info.Session, node, token),
                    entry => WorkflowJson.Reference(info.Session, entry), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads FileType metadata without discarding quality or optional-member absence.
        /// </summary>
        [McpServerTool(Name = "files_metadata", ReadOnly = true)]
        [Description("Read FileType metadata and return size/writable/open-count/MIME/chunk-size/modified quality. " +
            "Example: fileId='ns=2;s=Report'. Missing optional properties are reported as unsupported.")]
        public Task<CallToolResult> MetadataAsync(
            [Description(Descriptions.FileId)] string fileId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(fileId, ObjectTypeIds.FileType, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                return await WorkflowJson.PropertiesAsync(info.Session, node,
                    [
                        new QualifiedName(BrowseNames.Size), new QualifiedName(BrowseNames.Writable),
                        new QualifiedName(BrowseNames.UserWritable), new QualifiedName(BrowseNames.OpenCount),
                        new QualifiedName(BrowseNames.MimeType), new QualifiedName(BrowseNames.MaxByteStringLength),
                        new QualifiedName(BrowseNames.LastModifiedTime)
                    ], token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Creates an unopened file and never exposes its server handle.
        /// </summary>
        [McpServerTool(Name = "files_create_file", Destructive = true)]
        [Description("Create one empty unopened file beneath an existing directory. Never replaces an existing " +
            "entry or creates parents. Example: name='report.bin'. Returns actual status and created node id.")]
        public Task<CallToolResult> CreateFileAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.Name)] string name,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CreateAsync(directoryId, name, directory: false, sessionName, ct);
        }

        /// <summary>
        /// Creates one directory without silently creating ancestors.
        /// </summary>
        [McpServerTool(Name = "files_create_directory", Destructive = true)]
        [Description("Create one direct subdirectory, without overwrite or recursive parent creation. " +
            "Example: name='Reports'. Returns actual status and created node identity.")]
        public Task<CallToolResult> CreateDirectoryAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.Name)] string name,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CreateAsync(directoryId, name, directory: true, sessionName, ct);
        }

        /// <summary>
        /// Copies one named child into another explicitly selected directory.
        /// </summary>
        [McpServerTool(Name = "files_copy", Destructive = true)]
        [Description("Copy a verified child to an explicit destination and new name, without overwrite. " +
            "Copying nonempty directories requires includeChildren=true. Example: newName='report-copy.bin'. " +
            "Returns actual status/new node; server subtree effects are not claimed atomic.")]
        public Task<CallToolResult> CopyAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.SourceId)] string sourceId,
            [Description(Descriptions.TargetDirectoryId)] string targetDirectoryId,
            [Description(Descriptions.NewName)] string newName,
            [Description(Descriptions.IncludeChildren)] bool includeChildren = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return MoveOrCopyAsync(directoryId, sourceId, targetDirectoryId, newName,
                copy: true, includeChildren, sessionName, ct);
        }

        /// <summary>
        /// Moves one verified child without automatically replacing a destination.
        /// </summary>
        [McpServerTool(Name = "files_move", Destructive = true)]
        [Description("Move a verified child to an explicit destination and new name, without overwrite. " +
            "Moving a nonempty directory requires includeChildren=true. Example: newName='archived.bin'. " +
            "Returns actual status/new identity; there is no fabricated transaction.")]
        public Task<CallToolResult> MoveAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.SourceId)] string sourceId,
            [Description(Descriptions.TargetDirectoryId)] string targetDirectoryId,
            [Description(Descriptions.NewName)] string newName,
            [Description(Descriptions.IncludeChildren)] bool includeChildren = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return MoveOrCopyAsync(directoryId, sourceId, targetDirectoryId, newName,
                copy: false, includeChildren, sessionName, ct);
        }

        /// <summary>
        /// Deletes only a verified child of the specified directory.
        /// </summary>
        [McpServerTool(Name = "files_delete", Destructive = true)]
        [Description("Delete an explicit child of an explicit directory. Nonempty directory deletion requires " +
            "recursive=true; no client-side recursive walk or retry. Returns actual status and partial-effect " +
            "warning on refusal. Example: sourceId='ns=2;s=obsolete', recursive=false.")]
        public Task<CallToolResult> DeleteAsync(
            [Description(Descriptions.DirectoryId)] string directoryId,
            [Description(Descriptions.SourceId)] string sourceId,
            [Description(Descriptions.Recursive)] bool recursive = false,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (IClientWorkflowSessionScope scope, NodeId parent) =
                    await TargetAsync(directoryId, ObjectTypeIds.FileDirectoryType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                NodeId source = WorkflowJson.Node(info.Session, sourceId);
                await RequireChildAsync(info, parent, source, recursive, token).ConfigureAwait(false);
                var client = new FileSystemClient(info.Session, parent, FileOptions());
                CallMethodResult call = await client.Root.Proxy.CallWithResultAsync(
                    MethodIds.FileDirectoryType_DeleteFileSystemObject, [Variant.From(source)], token)
                    .ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(info.Session, source, call);
                result["recursiveRequested"] = recursive;
                result["atomic"] = false;
                result["remoteEffectsMayBePartial"] = recursive && StatusCode.IsBad(call.StatusCode);
                return result;
            }, ct);
        }

        /// <summary>
        /// Replaces a file only after the caller explicitly requests destructive replacement.
        /// </summary>
        [McpServerTool(Name = "files_upload", Destructive = true)]
        [Description("Stream a TransferRoot artifact into an existing FileType. replaceExistingContent=true is " +
            "required before truncation; no automatic overwrite. Example: filePath='reports/new.bin'. " +
            "Returns byte count/final Close status. Failures may leave a partial remote file; never auto-retry.")]
        public Task<CallToolResult> UploadAsync(
            [Description(Descriptions.FileId)] string fileId,
            [Description(Descriptions.FilePath)] string filePath,
            [Description(Descriptions.ReplaceExistingContent)] bool replaceExistingContent,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!replaceExistingContent)
                {
                    throw new ArgumentException("Explicit replaceExistingContent=true is required for an upload.");
                }
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(fileId, ObjectTypeIds.FileType, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var proxy = new FileTypeClient(info.Session, node, info.Session.MessageContext.Telemetry);
                CallMethodResult? completed = null;
                bool started = false;
                JsonObject artifact;
                try
                {
                    artifact = await m_transfers.UploadAsync(filePath, async (source, cancellation) =>
                    {
                        started = true;
                        (long bytes, CallMethodResult result) = await proxy.UploadFromWithResultAsync(
                            source, TransferLimit(), ct: cancellation).ConfigureAwait(false);
                        completed = result;
                        return bytes;
                    }, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsTransferFailure(exception))
                {
                    return TransferFailure(info.Session, node, exception, completed, started);
                }
                JsonObject response = WorkflowJson.Method(info.Session, node, completed!);
                response["artifact"] = artifact;
                response["remoteState"] = "closed";
                return response;
            }, ct);
        }

        /// <summary>
        /// Streams a remote file into a new confined local artifact.
        /// </summary>
        [McpServerTool(Name = "files_download", Destructive = false)]
        [Description("Stream a FileType into a new file beneath TransferRoot, without whole-file buffering or " +
            "local overwrite. Example: filePath='downloads/report.bin'. Returns root-relative path/byte count; " +
            "partial local artifacts and this call's own server handle are cleaned up on failure.")]
        public Task<CallToolResult> DownloadAsync(
            [Description(Descriptions.FileId)] string fileId,
            [Description(Descriptions.FilePath)] string filePath,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                m_transfers.ValidateDownloadPath(filePath);
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(fileId, ObjectTypeIds.FileType, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                var proxy = new FileTypeClient(info.Session, node, info.Session.MessageContext.Telemetry);
                JsonObject artifact = await m_transfers.DownloadAsync(
                    filePath, async (destination, cancellation) => _ = await proxy.DownloadToStreamAsync(
                        destination, TransferLimit(), ct: cancellation)
                        .ConfigureAwait(false), token).ConfigureAwait(false);
                JsonObject response = WorkflowJson.Identity(info.Session, node);
                response["artifact"] = artifact;
                response["remoteState"] = "closed";
                return response;
            }, ct);
        }

        /// <summary>
        /// Generates, streams and closes one temporary read file within a host-bounded window.
        /// </summary>
        [McpServerTool(Name = "files_read_temporary", Destructive = false)]
        [Description("Generate a temporary read file, wait at most the host's 30-second ceiling and stream it to a " +
            "new TransferRoot artifact. Example: filePath='downloads/generated.bin', optionType='String', " +
            "optionValue='report'. Returns generation status/completion identity and bytes; no raw handle is exposed.")]
        public Task<CallToolResult> ReadTemporaryAsync(
            [Description(Descriptions.TransferId)] string transferId,
            [Description(Descriptions.FilePath)] string filePath,
            [Description(Descriptions.OptionType)] BuiltInType optionType = BuiltInType.Null,
            [Description(Descriptions.OptionValue)] JsonElement? optionValue = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                m_transfers.ValidateDownloadPath(filePath);
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(transferId, ObjectTypeIds.TemporaryFileTransferType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                Variant generate = GenerationOptions(info.Session, optionType, optionValue);
                var client = new TemporaryFileTransferClient(info.Session, node, FileOptions());
                CallMethodResult call = await client.Proxy.CallWithResultAsync(
                    MethodIds.TemporaryFileTransferType_GenerateFileForRead, [generate], token).ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(info.Session, node, call, includeOutputs: false);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    return result;
                }
                (NodeId file, uint handle) = GeneratedFile(call);
                NodeId completion = call.OutputArguments.Count > 2 &&
                    call.OutputArguments[2].TryGetValue(out NodeId state) ? state : NodeId.Null;
                result["generationStatusCodeValue"] = call.StatusCode.Code;
                result["completionStateMachine"] = completion.IsNull ? null :
                    WorkflowJson.Identity(info.Session, completion);
                var proxy = new FileTypeClient(info.Session, file, info.Session.MessageContext.Telemetry);
                try
                {
                    await GuardAsync(info, file, token).ConfigureAwait(false);
                    using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
                    window.CancelAfter(m_options.MaxDurationMilliseconds);
                    if (!completion.IsNull)
                    {
                        await AwaitReadReadyAsync(info.Session, completion, window.Token).ConfigureAwait(false);
                    }
                    result["artifact"] = await m_transfers.DownloadAsync(filePath,
                        (destination, cancellation) => ReadOpenAsync(proxy, handle, destination, cancellation),
                        window.Token).ConfigureAwait(false);
                    result["remoteState"] = "readComplete";
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result["error"] = true;
                    result["statusCode"] = "BadTimeout";
                    result["statusCodeValue"] = StatusCodes.BadTimeout.Code;
                    result["remoteState"] = "readWindowExpired";
                    result["message"] = "The host-bounded temporary-file window expired; cleanup was attempted.";
                    return result;
                }
                finally
                {
                    await proxy.CloseAfterFailureAsync(handle).ConfigureAwait(false);
                }
            }, ct);
        }

        /// <summary>
        /// Generates one temporary write file, streams the artifact and explicitly closes and commits once.
        /// </summary>
        [McpServerTool(Name = "files_write_temporary", Destructive = true)]
        [Description("Generate a temporary write file, stream one TransferRoot artifact, then CloseAndCommit once. " +
            "Example: filePath='uploads/config.bin'. Returns actual commit status/completion state-machine id; " +
            "a non-null completion id means processing may be pending. No automatic polling, apply or retry.")]
        public Task<CallToolResult> WriteTemporaryAsync(
            [Description(Descriptions.TransferId)] string transferId,
            [Description(Descriptions.FilePath)] string filePath,
            [Description(Descriptions.OptionType)] BuiltInType optionType = BuiltInType.Null,
            [Description(Descriptions.OptionValue)] JsonElement? optionValue = null,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(transferId, ObjectTypeIds.TemporaryFileTransferType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                Variant generate = GenerationOptions(info.Session, optionType, optionValue);
                var client = new TemporaryFileTransferClient(info.Session, node, FileOptions());
                CallMethodResult? committed = null;
                NodeId generated = default;
                bool started = false;
                JsonObject artifact;
                try
                {
                    artifact = await m_transfers.UploadAsync(filePath, async (source, cancellation) =>
                    {
                        started = true;
                        CallMethodResult allocate = await client.Proxy.CallWithResultAsync(
                            MethodIds.TemporaryFileTransferType_GenerateFileForWrite, [generate], cancellation)
                            .ConfigureAwait(false);
                        if (StatusCode.IsBad(allocate.StatusCode))
                        {
                            throw new ServiceResultException(allocate.StatusCode);
                        }
                        (NodeId file, uint handle) = GeneratedFile(allocate);
                        generated = file;
                        var proxy = new FileTypeClient(info.Session, file, info.Session.MessageContext.Telemetry);
                        bool terminated = false;
                        try
                        {
                            await GuardAsync(info, file, cancellation).ConfigureAwait(false);
                            long bytes = await proxy.WriteStreamAsync(
                                handle, source, TransferLimit(), ct: cancellation).ConfigureAwait(false);
                            CallMethodResult close = await client.Proxy.CallWithResultAsync(
                                MethodIds.TemporaryFileTransferType_CloseAndCommit,
                                [Variant.From(handle)], cancellation)
                                .ConfigureAwait(false);
                            if (StatusCode.IsBad(close.StatusCode))
                            {
                                throw new ServiceResultException(close.StatusCode);
                            }
                            committed = close;
                            terminated = true;
                            return bytes;
                        }
                        finally
                        {
                            if (!terminated)
                            {
                                await proxy.CloseAfterFailureAsync(handle).ConfigureAwait(false);
                            }
                        }
                    }, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsTransferFailure(exception))
                {
                    return TransferFailure(info.Session, node, exception, committed, started);
                }
                JsonObject result = WorkflowJson.Method(info.Session, node, committed!, includeOutputs: false);
                result["artifact"] = artifact;
                result["file"] = WorkflowJson.Identity(info.Session, generated);
                NodeId completion = committed!.OutputArguments.Count == 1 &&
                    committed.OutputArguments[0].TryGetValue(out NodeId state) ? state : NodeId.Null;
                result["completionStateMachine"] = completion.IsNull ? null :
                    WorkflowJson.Identity(info.Session, completion);
                result["remoteState"] = completion.IsNull ? "committed" : "commitAcceptedProcessingMayBePending";
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads completion metadata without driving the remote state machine.
        /// </summary>
        [McpServerTool(Name = "files_completion", ReadOnly = true)]
        [Description("Inspect a FileTransferStateMachine returned by generation/commit. Returns current state and " +
            "last transition with optional support/quality; it never advances or retries a commit. " +
            "Example: completionId='ns=2;s=TransferCompletion'.")]
        public Task<CallToolResult> CompletionAsync(
            [Description(Descriptions.CompletionId)] string completionId,
            [Description(Descriptions.SessionName)] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(completionId, ObjectTypeIds.FileTransferStateMachineType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                return await WorkflowJson.PropertiesAsync(info.Session, node,
                    [new QualifiedName(BrowseNames.CurrentState), new QualifiedName(BrowseNames.LastTransition)], token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Invokes a fixed create method on the existing FileSystemClient proxy.
        /// </summary>
        private Task<CallToolResult> CreateAsync(
            string directoryId, string name, bool directory, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Leaf(name);
                (IClientWorkflowSessionScope scope, NodeId node) =
                    await TargetAsync(directoryId, ObjectTypeIds.FileDirectoryType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                await RequireNewNameAsync(info.Session, node, name, token).ConfigureAwait(false);
                var client = new FileSystemClient(info.Session, node, FileOptions());
                CallMethodResult call = await client.Root.Proxy.CallWithResultAsync(
                    directory ? MethodIds.FileDirectoryType_CreateDirectory : MethodIds.FileDirectoryType_CreateFile,
                    directory ? [Variant.From(name)] : [Variant.From(name), Variant.From(false)], token)
                    .ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(info.Session, node, call, includeOutputs: false);
                if (!StatusCode.IsBad(call.StatusCode) &&
                    call.OutputArguments.Count > 0 &&
                    call.OutputArguments[0].TryGetValue(out NodeId created))
                {
                    result["created"] = WorkflowJson.Identity(info.Session, created);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Invokes one standard MoveOrCopy method with explicit source and destination guards.
        /// </summary>
        private Task<CallToolResult> MoveOrCopyAsync(
            string directoryId, string sourceId, string targetDirectoryId, string name, bool copy,
            bool includeChildren, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Leaf(name);
                (IClientWorkflowSessionScope scope, NodeId parent) =
                    await TargetAsync(directoryId, ObjectTypeIds.FileDirectoryType, sessionName, token)
                        .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable lease = scope.ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo info = scope.Access;
                NodeId source = WorkflowJson.Node(info.Session, sourceId);
                NodeId target = WorkflowJson.Node(info.Session, targetDirectoryId);
                _ = await McpModelAccess.RequireTypeAsync(
                    info.Session, target, ObjectTypeIds.FileDirectoryType, token).ConfigureAwait(false);
                await GuardAsync(info, target, token).ConfigureAwait(false);
                await RequireChildAsync(info, parent, source, includeChildren, token).ConfigureAwait(false);
                await RequireNewNameAsync(info.Session, target, name, token).ConfigureAwait(false);
                var client = new FileSystemClient(info.Session, parent, FileOptions());
                CallMethodResult call = await client.Root.Proxy.CallWithResultAsync(
                    MethodIds.FileDirectoryType_MoveOrCopy,
                    [Variant.From(source), Variant.From(target), Variant.From(copy), Variant.From(name)], token)
                    .ConfigureAwait(false);
                JsonObject result = WorkflowJson.Method(info.Session, source, call);
                if (!StatusCode.IsBad(call.StatusCode) &&
                    call.OutputArguments.Count == 1 &&
                    call.OutputArguments[0].TryGetValue(out NodeId resultingNode))
                {
                    result["resultingNode"] = WorkflowJson.Identity(info.Session, resultingNode);
                }
                result["atomic"] = false;
                result["remoteEffectsMayBePartial"] = StatusCode.IsBad(call.StatusCode);
                return result;
            }, ct);
        }

        /// <summary>
        /// Resolves and classifies the exact operation target.
        /// </summary>
        private async ValueTask<(IClientWorkflowSessionScope Scope, NodeId Node)> TargetAsync(
            string identifier, ExpandedNodeId type, string? sessionName, CancellationToken ct)
        {
            IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(
                m_sessions.GetSessionInfoOrThrow(sessionName), ct).ConfigureAwait(false);
            try
            {
                OpcUaSessionManager.SessionInfo info = scope.Access;
                NodeId node = WorkflowJson.Node(info.Session, identifier);
                await GuardAsync(info, node, ct).ConfigureAwait(false);
                _ = await McpModelAccess.RequireTypeAsync(info.Session, node, type, ct).ConfigureAwait(false);
                return (scope, node);
            }
            catch
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Applies the shared policy and refuses trust/configuration files even when another capability is enabled.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask GuardAsync(
            OpcUaSessionManager.SessionInfo info, NodeId node, CancellationToken ct)
        {
            await m_sessions.UaOperationGuard.AuthorizeTargetAsync(info, node, genericCall: false, ct)
                .ConfigureAwait(false);
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(node);
            int references = 0;
            while (pending.TryDequeue(out NodeId current))
            {
                if (!visited.Add(current))
                {
                    continue;
                }
                if (visited.Count > 128)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "Protected file ownership could not be classified within host limits.");
                }
                await foreach (ReferenceDescription type in info.Session.EnumerateReferencesAsync(
                    current, ReferenceTypeIds.HasTypeDefinition, ct: ct).ConfigureAwait(false))
                {
                    NodeId actual = McpModelAccess.ModelNode(info.Session, type.NodeId);
                    if (actual == ObjectTypeIds.TrustListType ||
                        actual == ObjectTypeIds.ServerConfigurationType ||
                        await info.Session.NodeCache.IsTypeOfAsync(actual, ObjectTypeIds.TrustListType, ct)
                            .ConfigureAwait(false) ||
                        await info.Session.NodeCache.IsTypeOfAsync(actual, ObjectTypeIds.ServerConfigurationType, ct)
                            .ConfigureAwait(false))
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                            "Use the dedicated certificate/configuration workflow for this protected file.");
                    }
                }
                await foreach (ReferenceDescription parent in info.Session.EnumerateReferencesAsync(
                    current, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Inverse, ct: ct)
                    .ConfigureAwait(false))
                {
                    if (++references > 512)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                    pending.Enqueue(McpModelAccess.ModelNode(info.Session, parent.NodeId));
                }
            }
        }

        /// <summary>
        /// Proves child ownership and requires explicit subtree scope before a directory operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask RequireChildAsync(
            OpcUaSessionManager.SessionInfo info, NodeId parent, NodeId child, bool subtree, CancellationToken ct)
        {
            bool found = false;
            int count = 0;
            await foreach (ReferenceDescription entry in info.Session.EnumerateReferencesAsync(
                parent, ReferenceTypeIds.HasComponent, ct: ct).ConfigureAwait(false))
            {
                if (++count > 10_000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if (McpModelAccess.ModelNode(info.Session, entry.NodeId) == child)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The source is not an immediate child of the selected directory.");
            }
            await GuardAsync(info, child, ct).ConfigureAwait(false);
            NodeId actual = await McpModelAccess.TypeDefinitionAsync(info.Session, child, ct).ConfigureAwait(false);
            bool directory = actual == ObjectTypeIds.FileDirectoryType ||
                await info.Session.NodeCache.IsTypeOfAsync(actual, ObjectTypeIds.FileDirectoryType, ct)
                    .ConfigureAwait(false);
            if (!directory &&
                actual != ObjectTypeIds.FileType &&
                !await info.Session.NodeCache.IsTypeOfAsync(actual, ObjectTypeIds.FileType, ct).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch);
            }
            if (directory && !subtree)
            {
                await foreach (ReferenceDescription unused in info.Session.EnumerateReferencesAsync(
                    child, ReferenceTypeIds.HasComponent, nodeClassMask: (uint)NodeClass.Object, ct: ct)
                    .ConfigureAwait(false))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "The directory is nonempty; explicit subtree scope is required.");
                }
            }
        }

        /// <summary>
        /// Uses bounded SDK browse pages and advertised subtypes without the filesystem helper's unlimited page hint.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async IAsyncEnumerable<ReferenceDescription> ChildrenAsync(
            ISession session, NodeId directory, [EnumeratorCancellation] CancellationToken ct)
        {
            int examined = 0;
            await foreach (ReferenceDescription entry in session.EnumerateReferencesAsync(
                directory, ReferenceTypeIds.HierarchicalReferences,
                nodeClassMask: (uint)NodeClass.Object, pageSize: 100, ct: ct).ConfigureAwait(false))
            {
                if (++examined > 1_001_000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                NodeId type = entry.TypeDefinition.IsNull
                    ? await McpModelAccess.TypeDefinitionAsync(
                        session, McpModelAccess.ModelNode(session, entry.NodeId), ct).ConfigureAwait(false)
                    : McpModelAccess.ModelNode(session, entry.TypeDefinition);
                if (type == ObjectTypeIds.FileType ||
                    type == ObjectTypeIds.FileDirectoryType ||
                    await session.NodeCache.IsTypeOfAsync(type, ObjectTypeIds.FileType, ct).ConfigureAwait(false) ||
                    await session.NodeCache.IsTypeOfAsync(type, ObjectTypeIds.FileDirectoryType, ct)
                        .ConfigureAwait(false))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// Refuses a currently visible collision without pretending that the check and remote call are atomic.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask RequireNewNameAsync(
            ISession session, NodeId parent, string name, CancellationToken ct)
        {
            int count = 0;
            await foreach (ReferenceDescription entry in session.EnumerateReferencesAsync(
                parent, ReferenceTypeIds.HasComponent, nodeClassMask: (uint)NodeClass.Object, ct: ct)
                .ConfigureAwait(false))
            {
                if (++count > 10_000)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if (entry.BrowseName.Name == name)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated,
                        "The destination already exists; file workflows do not overwrite entries.");
                }
            }
        }

        /// <summary>
        /// Rejects paths where the server expects one leaf name.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static void Leaf(string name)
        {
            WorkflowJson.Text(name, 256);
            if (name is "." or ".." ||
                name.Contains('/', StringComparison.Ordinal) ||
                name.Contains('\\', StringComparison.Ordinal) ||
                name.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException("The file name must be one unqualified leaf, not a path.");
            }
        }

        /// <summary>
        /// Returns a bounded configuration for the existing SDK filesystem client.
        /// </summary>
        private FileSystemClientOptions FileOptions()
        {
            return new FileSystemClientOptions { ChunkSize = 4096, MaxBufferedReadSize = m_options.MaxInlineBytes };
        }

        /// <summary>
        /// Keeps the optional CLR default serializable while preserving explicit typed generation values.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private Variant GenerationOptions(ISession session, BuiltInType type, JsonElement? option)
        {
            if (option is not { } value || value.ValueKind == JsonValueKind.Undefined)
            {
                if (type == BuiltInType.Null)
                {
                    return Variant.Null;
                }
                throw new ArgumentException("optionValue is required when optionType is not Null.");
            }
            return WorkflowValues.Parse(session, new WorkflowValueInput { Type = type, Value = value }, m_options);
        }

        /// <summary>
        /// Validates the host's existing transfer ceiling, which is never supplied as a tool argument.
        /// </summary>
        private long TransferLimit()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(m_hostOptions.MaxTransferBytes, 1L);
            return m_hostOptions.MaxTransferBytes;
        }

        /// <summary>
        /// Decodes only the handle allocated by this operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static (NodeId File, uint Handle) GeneratedFile(CallMethodResult result)
        {
            if (result.OutputArguments.Count < 2 ||
                !result.OutputArguments[0].TryGetValue(out NodeId file) ||
                file.IsNull ||
                !result.OutputArguments[1].TryGetValue(out uint handle))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "Temporary file generation returned invalid output arguments.");
            }
            return (file, handle);
        }

        /// <summary>
        /// Reads only the private generated handle, checking each chunk before writing.
        /// </summary>
        /// <exception cref="IOException"></exception>
        private async ValueTask ReadOpenAsync(
            FileTypeClient file, uint handle, Stream destination, CancellationToken ct)
        {
            long total = 0;
            while (true)
            {
                ByteString chunk = await file.ReadAsync(handle, 4096, ct).ConfigureAwait(false);
                if (chunk.IsEmpty)
                {
                    return;
                }
                if (chunk.Length > 4096 || chunk.Length > TransferLimit() - total)
                {
                    throw new IOException("The generated download exceeds a requested chunk or host byte ceiling.");
                }
                await destination.WriteAsync(chunk.Memory, ct).ConfigureAwait(false);
                total += chunk.Length;
            }
        }

        /// <summary>
        /// Waits for the real generation state with the caller's finite cancellation window.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async ValueTask AwaitReadReadyAsync(ISession session, NodeId stateMachine, CancellationToken ct)
        {
            NodeId current = await WorkflowJson.ChildAsync(
                session, stateMachine, BrowseNames.CurrentState, ct).ConfigureAwait(false);
            NodeId id = await WorkflowJson.ChildAsync(session, current, BrowseNames.Id, ct).ConfigureAwait(false);
            while (true)
            {
                DataValue value = await McpModelAccess.ReadDataValueAsync(session, id, ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode);
                }
                if (!value.WrappedValue.TryGetValue(out NodeId state))
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError);
                }
                if (state == ObjectIds.FileTransferStateMachineType_ReadTransfer)
                {
                    return;
                }
                if (state == ObjectIds.FileTransferStateMachineType_Error)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The server reported an error while generating the temporary file.");
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Identifies expected transport and local-file failures without catching process-fatal failures.
        /// </summary>
        private static bool IsTransferFailure(Exception exception)
        {
            return exception is ServiceResultException or IOException or OperationCanceledException or
                UnauthorizedAccessException or InvalidOperationException or ArgumentException;
        }

        /// <summary>
        /// Reports uncertain partial effects, preserving a completed commit even after late local cancellation.
        /// </summary>
        private static JsonObject TransferFailure(
            ISession session, NodeId node, Exception failure, CallMethodResult? committed, bool started)
        {
            JsonObject result = committed is null ? WorkflowJson.Identity(session, node) :
                WorkflowJson.Method(session, node, committed, includeOutputs: false);
            result["error"] = committed is null;
            result["remoteState"] = committed is not null ? "committed" :
                started ? "notConfirmedMayBePartial" : "notStarted";
            result["remoteEffectsMayBePartial"] = started && committed is null;
            result["localFailure"] = failure.GetType().Name;
            if (committed is null && failure is ServiceResultException service)
            {
                result["statusCode"] = service.StatusCode.ToString();
                result["statusCodeValue"] = service.StatusCode.Code;
            }
            result["message"] = started
                ? "Do not automatically retry: remote file effects cannot be rolled back by this tool."
                : "Local transfer preflight failed; no remote file operation was started.";
            return result;
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly ClientWorkflowOptions m_options;
        private readonly OpcUaMcpOptions m_hostOptions;
        private readonly McpFileTransfers m_transfers;
        private readonly IClientWorkflowSessionFactory m_scopes;
    }
}
