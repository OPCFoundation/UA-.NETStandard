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

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for OPC UA PubSub Action methods (Part 14).
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubActionTools
    {
        /// <summary>
        /// Add a PubSub connection.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_connection")]
        [Description("Call PublishSubscribe.AddConnection with input arguments encoded as strings.")]
        public static Task<string> AddConnectionAsync(
            OpcUaSessionManager sessionManager,
            [Description("Input argument values as strings")] string[] inputArguments,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                ObjectIds.PublishSubscribe,
                MethodIds.PublishSubscribe_AddConnection,
                ToVariants(inputArguments),
                sessionName,
                ct);
        }

        /// <summary>
        /// Remove a PubSub connection.
        /// </summary>
        [McpServerTool(Name = "pubsub_remove_connection")]
        [Description("Call PublishSubscribe.RemoveConnection with a connection NodeId.")]
        public static Task<string> RemoveConnectionAsync(
            OpcUaSessionManager sessionManager,
            [Description("Connection NodeId to remove")] string connectionNodeId,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                ObjectIds.PublishSubscribe,
                MethodIds.PublishSubscribe_RemoveConnection,
                [Variant.From(OpcUaJsonHelper.ParseNodeId(connectionNodeId))],
                sessionName,
                ct);
        }

        /// <summary>
        /// Add a writer group to a PubSub connection.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_writer_group")]
        [Description("Call PubSubConnectionType.AddWriterGroup on a connection object.")]
        public static Task<string> AddWriterGroupAsync(
            OpcUaSessionManager sessionManager,
            [Description("Connection object NodeId")] string connectionNodeId,
            [Description("Input argument values as strings")] string[] inputArguments,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                OpcUaJsonHelper.ParseNodeId(connectionNodeId),
                MethodIds.PubSubConnectionType_AddWriterGroup,
                ToVariants(inputArguments),
                sessionName,
                ct);
        }

        /// <summary>
        /// Add a reader group to a PubSub connection.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_reader_group")]
        [Description("Call PubSubConnectionType.AddReaderGroup on a connection object.")]
        public static Task<string> AddReaderGroupAsync(
            OpcUaSessionManager sessionManager,
            [Description("Connection object NodeId")] string connectionNodeId,
            [Description("Input argument values as strings")] string[] inputArguments,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                OpcUaJsonHelper.ParseNodeId(connectionNodeId),
                MethodIds.PubSubConnectionType_AddReaderGroup,
                ToVariants(inputArguments),
                sessionName,
                ct);
        }

        /// <summary>
        /// Add a data set writer to a writer group.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_dataset_writer")]
        [Description("Call WriterGroupType.AddDataSetWriter on a writer group object.")]
        public static Task<string> AddDataSetWriterAsync(
            OpcUaSessionManager sessionManager,
            [Description("Writer group object NodeId")] string writerGroupNodeId,
            [Description("Input argument values as strings")] string[] inputArguments,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                OpcUaJsonHelper.ParseNodeId(writerGroupNodeId),
                MethodIds.WriterGroupType_AddDataSetWriter,
                ToVariants(inputArguments),
                sessionName,
                ct);
        }

        /// <summary>
        /// Add a data set reader to a reader group.
        /// </summary>
        [McpServerTool(Name = "pubsub_add_dataset_reader")]
        [Description("Call ReaderGroupType.AddDataSetReader on a reader group object.")]
        public static Task<string> AddDataSetReaderAsync(
            OpcUaSessionManager sessionManager,
            [Description("Reader group object NodeId")] string readerGroupNodeId,
            [Description("Input argument values as strings")] string[] inputArguments,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                OpcUaJsonHelper.ParseNodeId(readerGroupNodeId),
                MethodIds.ReaderGroupType_AddDataSetReader,
                ToVariants(inputArguments),
                sessionName,
                ct);
        }

        /// <summary>
        /// Enable a PubSub status object.
        /// </summary>
        [McpServerTool(Name = "pubsub_enable")]
        [Description("Call PubSubStatusType.Enable on the PublishSubscribe Status object or another status object.")]
        public static Task<string> EnableAsync(
            OpcUaSessionManager sessionManager,
            [Description("Status object NodeId to enable (defaults to PublishSubscribe.Status)")] string? statusNodeId = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                GetStatusObjectId(statusNodeId),
                MethodIds.PubSubStatusType_Enable,
                [],
                sessionName,
                ct);
        }

        /// <summary>
        /// Disable a PubSub status object.
        /// </summary>
        [McpServerTool(Name = "pubsub_disable")]
        [Description("Call PubSubStatusType.Disable on the PublishSubscribe Status object or another status object.")]
        public static Task<string> DisableAsync(
            OpcUaSessionManager sessionManager,
            [Description("Status object NodeId to disable (defaults to PublishSubscribe.Status)")] string? statusNodeId = null,
            [Description("Session name to use (defaults to the only active session)")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CallAsync(
                sessionManager,
                GetStatusObjectId(statusNodeId),
                MethodIds.PubSubStatusType_Disable,
                [],
                sessionName,
                ct);
        }

        private static async Task<string> CallAsync(
            OpcUaSessionManager sessionManager,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            string? sessionName,
            CancellationToken ct)
        {
            Client.ISession session = sessionManager.GetSessionOrThrow(sessionName);

            try
            {
                ArrayOf<CallMethodRequest> methodsToCall =
                [
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = inputArguments
                    }
                ];

                CallResponse response = await session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
                CallMethodResult result = response.Results[0];
                List<object?> outputArgs = result.OutputArguments.IsNull
                    ? []
                    : [.. result.OutputArguments.ToArray()!.Select(v => OpcUaJsonHelper.VariantToObject(v))];

                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["responseHeader"] = OpcUaJsonHelper.ResponseHeaderToDict(response.ResponseHeader),
                    ["statusCode"] = OpcUaJsonHelper.StatusCodeToString(result.StatusCode),
                    ["outputArguments"] = outputArgs,
                    ["inputArgumentResults"] = result.InputArgumentResults.IsNull
                        ? null
                        : result.InputArgumentResults.ToArray()!.Select(OpcUaJsonHelper.StatusCodeToString).ToList()
                });
            }
            catch (ServiceResultException ex)
            {
                return OpcUaJsonHelper.Serialize(new Dictionary<string, object?>
                {
                    ["error"] = true,
                    ["statusCode"] = ex.StatusCode.ToString(),
                    ["message"] = ex.Message
                });
            }
        }

        private static NodeId GetStatusObjectId(string? statusNodeId)
        {
            return string.IsNullOrWhiteSpace(statusNodeId)
                ? ObjectIds.PublishSubscribe_Status
                : OpcUaJsonHelper.ParseNodeId(statusNodeId);
        }

        private static ArrayOf<Variant> ToVariants(string[] inputArguments)
        {
            return inputArguments.Select(arg => new Variant(arg)).ToArray();
        }
    }
}
