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
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.MachineTool.Client;

namespace Opc.Ua.Mcp.Tools
{
    public sealed partial class MachineToolServiceTools
    {
        /// <summary>
        /// Lists bounded immediate children using the existing remote directory helper.
        /// </summary>
        [McpServerTool(Name = "machinetool_list_files", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Lists immediate remote files/directories. Returns qualified UaPaths and metadata; writable " +
            "flags are advisory. Uses the machine's FileSystem or WorkMasters. No local access or recursive walk; " +
            "record/byte overflow fails without truncation.")]
        public static Task<CallToolResult> ListFilesAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId with the requested file service.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("FileSystem or MachineTool-qualified WorkMasters.")]
            [StringLength(32, MinimumLength = 1)] string root = "FileSystem",
            [Description("Remote UaPath; / is root. Optional namespace prefixes, <=32 safe 255-character segments.")]
            [StringLength(1024, MinimumLength = 1)] string path = "/",
            [Description("Maximum immediate entry count; overflow fails.")]
            [Range(1, 128)] int maximumEntries = 32,
            [Description("Whole UTF-8 JSON response cap, including metadata.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected session name; omit only for a single session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolServiceParameters.Selector(root, ["FileSystem", "WorkMasters"]);
                string target = MachineToolServiceParameters.Path(path, allowRoot: true);
                MachineToolParameters.Bound(maximumEntries, 1, 128);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                FileSystemClient files = await FilesAsync(client, node, root, 1, ct).ConfigureAwait(false);
                UaDirectoryInfo directory = await files.GetDirectoryAsync(target, ct).ConfigureAwait(false);
                var entries = new JsonArray();
                await foreach (UaFileSystemInfo entry in directory.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    Count(entries.Count + 1, maximumEntries);
                    await entry.RefreshAsync(ct).ConfigureAwait(false);
                    entries.Add((JsonNode)MachineToolServiceJson.Entry(entry));
                }
                return MachineToolJson.Result(new JsonObject
                {
                    ["root"] = root,
                    ["path"] = directory.FullPath,
                    ["entries"] = entries
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Downloads one whole bounded file through a call-owned standard stream.
        /// </summary>
        [McpServerTool(Name = "machinetool_read_file", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Reads an existing remote file. Returns complete base64 content and byteCount. Raw and " +
            "whole-JSON caps include base64/escaping expansion; overflow fails. Standard helpers close handles " +
            "on every path. No writes, local paths or persistent handles.")]
        public static Task<CallToolResult> ReadFileAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId with the requested file service.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Existing file's remote UaPath; 1-32 safe 255-character segments, no traversal.")]
            [StringLength(1024, MinimumLength = 1)] string path,
            [Description("FileSystem or MachineTool-qualified WorkMasters.")]
            [StringLength(32, MinimumLength = 1)] string root = "FileSystem",
            [Description("Raw file byte cap; empty files are valid, oversize fails.")]
            [Range(1, 524288)] int maximumBytes = 65536,
            [Description("Whole UTF-8 JSON cap, including base64/escaping.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected session whose server identity controls access.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolServiceParameters.Selector(root, ["FileSystem", "WorkMasters"]);
                string target = MachineToolServiceParameters.Path(path, allowRoot: false);
                MachineToolParameters.Bound(maximumBytes, 1, MachineToolServiceParameters.MaximumFileBytes);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                FileSystemClient files = await FilesAsync(client, node, root, maximumBytes, ct).ConfigureAwait(false);
                UaFileInfo file = await files.GetFileAsync(target, ct).ConfigureAwait(false);
                await file.RefreshAsync(ct).ConfigureAwait(false);
                byte[] bytes = await file.ReadAllBytesAsync(ct).ConfigureAwait(false);
                return MachineToolJson.Result(new JsonObject
                {
                    ["byteCount"] = bytes.Length,
                    ["contentBase64"] = Convert.ToBase64String(bytes)
                }, maximumResponseBytes);
            }, ct);
        }

        /// <summary>
        /// Attempts exactly one explicitly selected file mutation with no implicit creation or compensation.
        /// </summary>
        [McpServerTool(Name = "machinetool_change_file", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Changes remote files with confirm=true and server/provider authorization. Returns a bounded " +
            "completion receipt, never an upload echo. Write replaces an existing file; creates require existing " +
            "parents; Move/Copy stay in one root without overwrite. Delete needs recursive=true for nonempty " +
            "directories. Cancellation may leave outcome unknown; handles close, no retry or compensation.")]
        public static Task<CallToolResult> ChangeFileAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId with the requested file service.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("File operation; related arguments are grouped in options.")]
            [StringLength(32, MinimumLength = 1)] string operation,
            [Description("Remote UaPath; 1-32 safe 255-character segments. New leaves omit namespace prefixes.")]
            [StringLength(1024, MinimumLength = 1)] string path,
            [Description("FileSystem or WorkMasters; applies to both paths.")]
            [StringLength(32, MinimumLength = 1)] string root = "FileSystem",
            [Description("Native file options: Move/Copy destination; Write contentBase64 and maximumBytes; " +
                "Delete recursive. Omit unused fields; empty contentBase64 explicitly clears an existing file.")]
            JsonElement? options = null,
            [Description("Explicit consent; never bypasses server/provider policy.")]
            bool confirm = false,
            [Description("Whole UTF-8 receipt cap, checked before mutation.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected session whose server identity controls changes.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolServiceParameters.Selector(root, ["FileSystem", "WorkMasters"]);
                (string? destination, string? contentBase64, int maximumBytes, bool recursive) =
                    MachineToolServiceParameters.FileOptions(options.GetValueOrDefault());
                MachineToolServiceParameters.Selector(operation,
                    ["Write", "CreateFile", "CreateDirectory", "Delete", "Move", "Copy"]);
                string target = MachineToolServiceParameters.Path(path, allowRoot: false,
                    newLeaf: operation is "CreateFile" or "CreateDirectory");
                MachineToolParameters.Bound(maximumBytes, 1, MachineToolServiceParameters.MaximumFileBytes);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                string? next = null;
                if (operation is "Move" or "Copy")
                {
                    next = MachineToolServiceParameters.Path(destination!, allowRoot: false, newLeaf: true);
                    if (string.Equals(target, next, StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Source and destination must differ.", nameof(options));
                    }
                }
                else if (destination != null)
                {
                    throw new ArgumentException("destination is only valid for Move/Copy.", nameof(options));
                }
                if ((operation != "Write" && contentBase64 != null) || (recursive && operation != "Delete"))
                {
                    throw new ArgumentException("contentBase64 is Write-only; recursive=true is Delete-only.");
                }
                ByteString bytes = operation == "Write"
                    ? MachineToolServiceParameters.Upload(contentBase64!, maximumBytes) : ByteString.Empty;
                MachineToolParameters.Consent(confirm);
                CallToolResult receipt = MachineToolJson.Result(
                    MachineToolServiceJson.FileReceipt(operation, bytes.Length), maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                FileSystemClient files = await FilesAsync(client, node, root, maximumBytes, ct).ConfigureAwait(false);
                switch (operation)
                {
                    case "Write":
                        UaFileInfo file = await files.GetFileAsync(target, ct).ConfigureAwait(false);
                        await file.RefreshAsync(ct).ConfigureAwait(false);
                        await file.WriteAllBytesAsync(bytes.Memory, ct).ConfigureAwait(false);
                        break;
                    case "CreateFile":
                        await files.CreateFileAsync(target, createIntermediate: false, ct).ConfigureAwait(false);
                        break;
                    case "CreateDirectory":
                        string parent = UaPath.GetDirectoryName(target) ?? UaPath.Root;
                        UaDirectoryInfo directory = await files.GetDirectoryAsync(parent, ct).ConfigureAwait(false);
                        await directory.CreateSubdirectoryAsync(UaPath.GetFileName(target).Name!, ct)
                            .ConfigureAwait(false);
                        break;
                    case "Delete":
                        await files.DeleteAsync(target, recursive, ct).ConfigureAwait(false);
                        break;
                    case "Move":
                        await files.MoveAsync(target, next!, ct).ConfigureAwait(false);
                        break;
                    case "Copy":
                        await files.CopyAsync(target, next!, ct).ConfigureAwait(false);
                        break;
                }
                return receipt;
            }, ct);
        }

        /// <summary>
        /// Roots a short-lived helper at an actual machine service; the shared host session remains borrowed.
        /// </summary>
        private static async ValueTask<FileSystemClient> FilesAsync(
            MachineToolClient client, NodeId node, string root, int maximumBytes, CancellationToken ct)
        {
            FileDirectoryTypeClient? directory = root == "WorkMasters"
                ? await client.GetWorkMastersAsync(node, ct).ConfigureAwait(false)
                : await client.GetFileSystemAsync(node, ct).ConfigureAwait(false);
            if (directory == null)
            {
                throw Unsupported(root);
            }
            return new FileSystemClient(client.Session, directory.ObjectId, new FileSystemClientOptions
            {
                ChunkSize = Math.Min(16384, maximumBytes),
                MaxBufferedReadSize = maximumBytes,
                PathCacheSize = 0
            });
        }
    }
}
