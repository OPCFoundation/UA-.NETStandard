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
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Mcp.Tools
{
    public sealed partial class VisionMachineTools
    {
        /// <summary>
        /// Executes standard file operations over the released specialized transfer types.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_file")]
        [Description("Chunked OPC 40100 file operation. GenerateRead/GenerateWrite/Commit use a RecipeTransfer, " +
            "ConfigurationTransfer, or ResultTransfer node. " +
            "Read/Write/Close/GetPosition/SetPosition use the returned FileType node and opaque MCP handle. " +
            "MCP callers cannot Open arbitrary files or use raw server handle numbers. " +
            "kind is Recipe, Configuration, or Result; id is the internal document ID for Generate*. " +
            "Generated handles are already open. Commit publishes a write; " +
            "Close without Commit aborts a temporary upload. Chunks are capped at 1 MiB. " +
            "ReadCompletion/WaitForCompletion require the transfer's handle and returned completion NodeId; " +
            "WaitForCompletion waits at most 60 seconds and fails on Error, timeout, or unavailable state.")]
        public static async Task<JsonElement> FileAsync(
            VisionClientAccessor accessor,
            [Description("Transfer, file, or completion-state-machine NodeId selected as described by operation.")]
            string nodeId,
            [Description("GenerateRead, GenerateWrite, Commit, Open, Read, Write, Close, GetPosition, SetPosition, " +
                "ReadCompletion, or WaitForCompletion; names are case-sensitive.")]
            string operation,
            [Description("Document kind for GenerateRead/GenerateWrite: Recipe, Configuration, or Result.")]
            string kind = "Recipe",
            [Description("Internal document identifier for GenerateRead/GenerateWrite; not a NodeId or filename.")]
            string id = "",
            [Description("Caller-bound opaque MCP handle returned by GenerateRead/GenerateWrite; not a UA handle.")]
            uint handle = 0,
            [Description("Requested Read chunk size in bytes, from 1 to 1048576; default 65536.")]
            [Range(1, 1048576)] int length = 65536,
            [Description("Base64 data for Write, with at most 1048576 decoded bytes; for example AQID encodes 1,2,3.")]
            string? dataBase64 = null,
            [Description("Absolute byte offset for SetPosition; zero denotes the start of the file.")]
            ulong position = 0,
            [Description("Open bitmask: Read=1, Write=2, EraseExisting=4, Append=8; combine compatible flags.")]
            byte mode = 1,
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            if (length is <= 0 or > 1048576)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            ArgumentNullException.ThrowIfNull(accessor);
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            if (!accessor.SessionManager.IsTrustedHostCall)
            {
                return await CallerFileAsync(
                    accessor, nodeId, operation, kind, id, handle, length, dataBase64, position, sessionName, ct)
                    .ConfigureAwait(false);
            }
            VisionClient client = accessor.CreateClient(sessionName);
            NodeId target = OpcUaJsonHelper.ParseNodeId(nodeId);
            if (operation is "ReadCompletion" or "WaitForCompletion")
            {
                var completion = new FileTransferStateMachineTypeClient(client.Session, target, client.Telemetry);
                FiniteStateSnapshot snapshot = operation == "ReadCompletion"
                    ? await completion.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false)
                    : await completion.WaitForCompletionAsync(
                        TimeSpan.FromSeconds(60), accessor.TimeProvider, ct).ConfigureAwait(false);
                return Element(State(snapshot));
            }
            if (operation is "GenerateRead" or "GenerateWrite" or "Commit")
            {
                var transfer = new TemporaryFileTransferTypeClient(client.Session, target, client.Telemetry);
                if (operation == "Commit")
                {
                    return Response(client, ("CompletionStateMachine",
                        Variant.From(await transfer.CloseAndCommitAsync(handle, ct).ConfigureAwait(false))));
                }
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
                    "Result" => Variant.FromStructure(new ResultTransferOptions
                    {
                        Id = new ResultIdDataType { Id = id }
                    }),
                    _ => throw new ArgumentException("Unknown document kind.", nameof(kind))
                };
                if (operation == "GenerateRead")
                {
                    (NodeId fileNodeId, uint fileHandle, NodeId completionStateMachine) =
                        await transfer.GenerateFileForReadAsync(options, ct).ConfigureAwait(false);
                    return Response(client, ("FileNodeId", Variant.From(fileNodeId)),
                        ("Handle", Variant.From(fileHandle)),
                        ("CompletionStateMachine", Variant.From(completionStateMachine)));
                }
                (NodeId fileNodeId, uint fileHandle) created =
                    await transfer.GenerateFileForWriteAsync(options, ct).ConfigureAwait(false);
                return Response(client, ("FileNodeId", Variant.From(created.fileNodeId)),
                    ("Handle", Variant.From(created.fileHandle)));
            }
            var file = new FileTypeClient(client.Session, target, client.Telemetry);
            switch (operation)
            {
                case "Open":
                    return Response(client, ("Handle",
                        Variant.From(await file.OpenAsync(mode, ct).ConfigureAwait(false))));
                case "Read":
                    return Response(client, ("Data",
                        Variant.From(await file.ReadAsync(handle, length, ct).ConfigureAwait(false))));
                case "Write":
                    if (dataBase64 == null || dataBase64.Length > 1398104)
                    {
                        throw new ArgumentException(
                            "Supply at most 1 MiB of base64-encoded data.", nameof(dataBase64));
                    }
                    byte[] bytes = Convert.FromBase64String(dataBase64);
                    if (bytes.Length > 1048576)
                    {
                        throw new ArgumentOutOfRangeException(nameof(dataBase64));
                    }
                    await file.WriteAsync(handle, ByteString.From(bytes), ct).ConfigureAwait(false);
                    break;
                case "Close":
                    await file.CloseAsync(handle, ct).ConfigureAwait(false);
                    break;
                case "GetPosition":
                    return Response(client, ("Position",
                        Variant.From(await file.GetPositionAsync(handle, ct).ConfigureAwait(false))));
                case "SetPosition":
                    await file.SetPositionAsync(handle, position, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException("Unknown file operation.", nameof(operation));
            }
            return Response(client, ("Status", Variant.From(StatusCodes.Good)));
        }

        /// <summary>
        /// Invokes methods on an address-space recipe object.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_recipe_object")]
        [Description("Invokes Prepare, Unprepare, LinkProduct, or UnlinkProduct " +
            "on a published OPC 40100 RecipeType object. " +
            "Use the RecipeNodeId returned by Add; this is not the recipe's internal ID string.")]
        public static async Task<JsonElement> RecipeObjectAsync(
            VisionClientAccessor accessor,
            [Description("Recipe object NodeId returned as RecipeNodeId by Add; not the recipe's internal ID.")]
            string recipeNodeId,
            [Description("Prepare, Unprepare, LinkProduct, or UnlinkProduct; operation names are case-sensitive.")]
            string operation,
            [Description("Product identifier for LinkProduct or UnlinkProduct; ignored by preparation operations.")]
            string productId = "",
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            var recipe = new RecipeTypeClient(
                client.Session, OpcUaJsonHelper.ParseNodeId(recipeNodeId), client.Telemetry);
            if (operation == "Prepare")
            {
                (bool isCompleted, int error) prepared = await recipe.PrepareAsync(ct).ConfigureAwait(false);
                return Response(client, ("IsCompleted", Variant.From(prepared.isCompleted)),
                    ("Error", Variant.From(prepared.error)));
            }
            int error = operation switch
            {
                "Unprepare" => await recipe.UnprepareAsync(ct).ConfigureAwait(false),
                "LinkProduct" => await recipe.LinkProductAsync(new ProductIdDataType { Id = productId }, ct)
                    .ConfigureAwait(false),
                "UnlinkProduct" => await recipe.UnlinkProductAsync(new ProductIdDataType { Id = productId }, ct)
                    .ConfigureAwait(false),
                _ => throw new ArgumentException("Unknown recipe-object operation.", nameof(operation))
            };
            return Response(client, ("Error", Variant.From(error)));
        }

        /// <summary>
        /// Acknowledges or confirms a specific condition event.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool(Name = "vision_machine_condition")]
        [Description("Acknowledges or confirms a vision condition " +
            "using the exact EventId received from its notification. " +
            "operation is Acknowledge or Confirm. Confirmation does not imply that the physical error is resolved.")]
        public static async Task<JsonElement> ConditionAsync(
            VisionClientAccessor accessor,
            [Description("Condition instance NodeId returned as conditionNodeId by vision_machine_observe.")]
            string conditionNodeId,
            [Description("Exact eventIdBase64 from the condition notification being acknowledged or confirmed.")]
            string eventIdBase64,
            [Description("Acknowledge or Confirm (case-sensitive); neither operation clears the physical cause.")]
            string operation,
            [Description("Operator comment attached to this acknowledgement or confirmation; default empty text.")]
            string comment = "",
            [Description("Connected OPC UA session name; omit when exactly one session is available.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            if (eventIdBase64.Length > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(eventIdBase64));
            }
            VisionClient client = accessor.CreateClient(sessionName);
            var condition = new AcknowledgeableConditionTypeClient(
                client.Session, OpcUaJsonHelper.ParseNodeId(conditionNodeId), client.Telemetry);
            var eventId = ByteString.From(Convert.FromBase64String(eventIdBase64));
            if (operation == "Acknowledge")
            {
                await condition.AcknowledgeAsync(eventId, new LocalizedText(comment), ct).ConfigureAwait(false);
            }
            else if (operation == "Confirm")
            {
                await condition.ConfirmAsync(eventId, new LocalizedText(comment), ct).ConfigureAwait(false);
            }
            else
            {
                throw new ArgumentException("Unknown condition operation.", nameof(operation));
            }
            return Response(client, ("Status", Variant.From(StatusCodes.Good)));
        }
    }
}
