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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using TransferTypes = Opc.Ua.Vision.MachineVision.ObjectTypeIds;
using UaTypes = Opc.Ua.ObjectTypeIds;

namespace Opc.Ua.Mcp.Tools
{
    public sealed partial class VisionMachineTools
    {
        /// <summary>
        /// Keeps untrusted MCP requests on verified transfers and opaque caller-owned file handles.
        /// Trusted direct embedding retains its existing low-level FileType contract.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The request attempts an unverified file open or uses a handle for a different target.
        /// </exception>
        private static async Task<JsonElement> CallerFileAsync(
            VisionClientAccessor accessor, string nodeId, string operation, string kind, string id,
            uint handle, int length, string? dataBase64, ulong position, string? sessionName, CancellationToken ct)
        {
            OpcUaSessionManager manager = accessor.SessionManager;
            McpCallerIdentity caller = manager.OperationPolicy.RequireCaller();
            OpcUaSessionManager.SessionInfo registration = manager.GetSessionInfoOrThrow(sessionName);
            NodeId target = OpcUaJsonHelper.ParseNodeId(nodeId);
            if (operation is "GenerateRead" or "GenerateWrite")
            {
                return await GenerateCallerFileAsync(
                    accessor, registration, caller, target, operation == "GenerateWrite", kind, id, ct)
                    .ConfigureAwait(false);
            }
            if (operation == "Open")
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "MCP file operations require an already-open file returned by a verified Vision transfer.");
            }
            VisionFileHandles.Entry entry = accessor.FileHandles.Get(handle, caller, registration);
            if (operation == "Close")
            {
                if (target != entry.File)
                {
                    throw FileHandleDenied();
                }
                entry.Scope.VerifyBeforeDispatch(ct);
                await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(entry.Scope.Access, target, ct)
                    .ConfigureAwait(false);
                var client = new VisionClient(entry.Scope.Access.Session, manager.Telemetry);
                JsonElement response = Response(client, ("Status", Variant.From(StatusCodes.Good)));
                try
                {
                    await entry.CloseAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    if (entry.Closed)
                    {
                        accessor.FileHandles.Remove(handle, entry);
                    }
                }
                return response;
            }
            return await entry.UseAsync(async () =>
            {
                OpcUaSessionManager.SessionInfo access = entry.Scope.Access;
                var client = new VisionClient(access.Session, manager.Telemetry);
                if (operation is "ReadCompletion" or "WaitForCompletion")
                {
                    if (entry.Completion.IsNull || target != entry.Completion)
                    {
                        throw FileHandleDenied();
                    }
                    await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, target, ct)
                        .ConfigureAwait(false);
                    entry.Scope.VerifyBeforeDispatch(ct);
                    var completion = new FileTransferStateMachineTypeClient(access.Session, target, manager.Telemetry);
                    FiniteStateSnapshot snapshot = operation == "ReadCompletion"
                        ? await completion.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false)
                        : await completion.WaitForCompletionAsync(
                            TimeSpan.FromSeconds(60), accessor.TimeProvider, ct).ConfigureAwait(false);
                    return Element(State(snapshot));
                }
                if (operation == "Commit")
                {
                    if (target != entry.Transfer || !entry.Writable || entry.Committed)
                    {
                        throw FileHandleDenied();
                    }
                    await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, entry.Transfer, ct)
                        .ConfigureAwait(false);
                    await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, entry.File, ct)
                        .ConfigureAwait(false);
                    entry.Scope.VerifyBeforeDispatch(ct);
                    var transfer = new TemporaryFileTransferTypeClient(
                        access.Session, entry.Transfer, manager.Telemetry);
                    NodeId completion = await transfer.CloseAndCommitAsync(entry.ServerHandle, ct)
                        .ConfigureAwait(false);
                    entry.Committed = true;
                    if (!completion.IsNull)
                    {
                        await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, completion, ct)
                            .ConfigureAwait(false);
                        _ = await McpModelAccess.RequireTypeAsync(
                            access.Session, completion, UaTypes.FileTransferStateMachineType, ct)
                            .ConfigureAwait(false);
                    }
                    entry.Completion = completion;
                    return Response(client, ("CompletionStateMachine", Variant.From(completion)));
                }
                if (target != entry.File || entry.Committed)
                {
                    throw FileHandleDenied();
                }
                await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, entry.File, ct)
                    .ConfigureAwait(false);
                entry.Scope.VerifyBeforeDispatch(ct);
                var file = new FileTypeClient(access.Session, entry.File, manager.Telemetry);
                switch (operation)
                {
                    case "Read":
                        return Response(client, ("Data",
                            Variant.From(await file.ReadAsync(entry.ServerHandle, length, ct).ConfigureAwait(false))));
                    case "Write":
                        if (!entry.Writable)
                        {
                            throw FileHandleDenied();
                        }
                        if (dataBase64 is null || dataBase64.Length > 1398104)
                        {
                            throw new ArgumentException(
                                "Supply at most 1 MiB of base64-encoded data.", nameof(dataBase64));
                        }
                        byte[] bytes = Convert.FromBase64String(dataBase64);
                        if (bytes.Length > 1048576)
                        {
                            throw new ArgumentOutOfRangeException(nameof(dataBase64));
                        }
                        await file.WriteAsync(entry.ServerHandle, ByteString.From(bytes), ct).ConfigureAwait(false);
                        break;
                    case "GetPosition":
                        return Response(client, ("Position",
                            Variant.From(await file.GetPositionAsync(entry.ServerHandle, ct).ConfigureAwait(false))));
                    case "SetPosition":
                        await file.SetPositionAsync(entry.ServerHandle, position, ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentException("Unknown file operation.", nameof(operation));
                }
                return Response(client, ("Status", Variant.From(StatusCodes.Good)));
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies both the specialized transfer and returned file before publishing any MCP handle.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="kind"/> does not identify a supported document kind.
        /// </exception>
        private static async Task<JsonElement> GenerateCallerFileAsync(
            VisionClientAccessor accessor, OpcUaSessionManager.SessionInfo registration, McpCallerIdentity caller,
            NodeId target, bool writable, string kind, string id, CancellationToken ct)
        {
            OpcUaSessionManager manager = accessor.SessionManager;
            ExpandedNodeId expected = kind switch
            {
                "Recipe" => TransferTypes.RecipeTransferType,
                "Configuration" => TransferTypes.ConfigurationTransferType,
                "Result" => TransferTypes.ResultTransferType,
                _ => throw new ArgumentException("Unknown document kind.", nameof(kind))
            };
            accessor.FileHandles.Reserve(caller);
            McpMutationSession? scope = null;
            VisionFileHandles.Entry? entry = null;
            uint handle = 0;
            bool published = false;
            try
            {
                scope = await McpMutationSession.CaptureAsync(manager, registration, ct).ConfigureAwait(false);
                OpcUaSessionManager.SessionInfo access = scope.Access;
                await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, target, ct)
                    .ConfigureAwait(false);
                _ = await McpModelAccess.RequireTypeAsync(access.Session, target, expected, ct).ConfigureAwait(false);
                Variant options = kind switch
                {
                    "Recipe" => Variant.FromStructure(new RecipeTransferOptions
                    {
                        InternalId = new RecipeIdInternalDataType { Id = id }
                    }),
                    "Configuration" => Variant.FromStructure(new ConfigurationTransferOptions
                    {
                        InternalId = new ConfigurationIdDataType { Id = id }
                    }),
                    _ => Variant.FromStructure(new ResultTransferOptions
                    {
                        Id = new ResultIdDataType { Id = id }
                    })
                };
                scope.VerifyBeforeDispatch(ct);
                var transfer = new TemporaryFileTransferTypeClient(access.Session, target, manager.Telemetry);
                NodeId file;
                uint serverHandle;
                NodeId completion = NodeId.Null;
                if (writable)
                {
                    (file, serverHandle) = await transfer.GenerateFileForWriteAsync(options, ct).ConfigureAwait(false);
                }
                else
                {
                    (file, serverHandle, completion) = await transfer.GenerateFileForReadAsync(options, ct)
                        .ConfigureAwait(false);
                }
                await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, file, ct).ConfigureAwait(false);
                _ = await McpModelAccess.RequireTypeAsync(access.Session, file, UaTypes.FileType, ct)
                    .ConfigureAwait(false);
                McpMutationSession fileScope = scope;
                scope = null;
                (handle, entry) = await accessor.FileHandles.AddAsync(
                    manager, caller, registration, fileScope, target, file, serverHandle, writable, completion)
                    .ConfigureAwait(false);
                if (!completion.IsNull)
                {
                    await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(access, completion, ct)
                        .ConfigureAwait(false);
                    _ = await McpModelAccess.RequireTypeAsync(
                        access.Session, completion, UaTypes.FileTransferStateMachineType, ct)
                        .ConfigureAwait(false);
                }
                var client = new VisionClient(access.Session, manager.Telemetry);
                JsonElement response = Response(client,
                    ("FileNodeId", Variant.From(file)), ("Handle", Variant.From(handle)),
                    ("CompletionStateMachine", Variant.From(completion)));
                published = true;
                return response;
            }
            finally
            {
                if (!published)
                {
                    try
                    {
                        if (entry is not null)
                        {
                            await entry.DisposeAsync().ConfigureAwait(false);
                        }
                        else if (scope is not null)
                        {
                            await scope.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (handle != 0 && entry is not null)
                        {
                            accessor.FileHandles.Remove(handle, entry);
                        }
                        else
                        {
                            accessor.FileHandles.Release(caller);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refuses raw, stolen, cross-target and already-committed file authority without server dispatch.
        /// </summary>
        private static ServiceResultException FileHandleDenied()
        {
            return new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The operation does not match this caller's verified Vision transfer handle.");
        }
    }
}
