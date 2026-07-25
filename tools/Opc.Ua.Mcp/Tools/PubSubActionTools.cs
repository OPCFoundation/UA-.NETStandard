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
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools that exercise OPC UA PubSub Actions (Part 14 §7.2.4):
    /// they invoke Action targets and register responder handlers on the active runtime.
    /// </summary>
    [McpServerToolType]
    public sealed class PubSubActionTools
    {
        /// <summary>
        /// Invokes a PubSub Action target and awaits the correlated response.
        /// </summary>
        [McpServerTool(Name = "pubsub_invoke_action")]
        [Description("Send a PubSub Action request and await the correlated response. Returns a JSON-friendly " +
            "summary (connectionName, dataSetWriterId, actionTargetId, actionName, requestId, correlationData, " +
            "statusCode, actionState, outputFields); throws if no response arrives within timeoutMs.")]
        public static async Task<PubSubActionResponseSummary> InvokeActionAsync(
            PubSubRuntimeManager manager,
            [Description("DataSetWriterId that owns the Action target.")] ushort dataSetWriterId,
            [Description("ActionTargetId to invoke; leave 0 when actionName resolves metadata.")]
            ushort actionTargetId = 0,
            [Description("Optional action name used to resolve ActionTargetId from metadata.")]
            string? actionName = null,
            [Description("Optional PubSub connection name used by runtime routing.")] string? connectionName = null,
            [Description("Input fields as JSON object or name[:type]=value pairs separated by ';'.")]
            string? inputFields = null,
            [Description("Optional response address carried on the request.")] string? responseAddress = null,
            [Description("Action response timeout in milliseconds.")] int timeoutMs = 2000,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);

            var request = new PubSubActionRequest
            {
                Target = CreateTarget(connectionName, dataSetWriterId, actionTargetId, actionName),
                InputFields = ParseFields(inputFields),
                ResponseAddress = responseAddress ?? string.Empty,
                TimeoutHint = timeoutMs <= 0 ? 2000 : timeoutMs
            };
            var timeout = TimeSpan.FromMilliseconds(timeoutMs <= 0 ? 2000 : timeoutMs);
            PubSubActionResponse response = await manager.InvokeActionAsync(request, timeout, ct).ConfigureAwait(false);
            return Summarize(response);
        }

        /// <summary>
        /// Registers an echo responder for round-trip Action testing.
        /// </summary>
        [McpServerTool(Name = "pubsub_register_action_responder")]
        [Description("Register a demo PubSub Action responder that echoes input fields to output fields.")]
        public static async Task<PubSubActionResponderRegistration> RegisterActionResponderAsync(
            PubSubRuntimeManager manager,
            [Description("DataSetWriterId that owns the Action target.")] ushort dataSetWriterId,
            [Description("ActionTargetId handled by the responder.")] ushort actionTargetId,
            [Description("Optional action name associated with the target.")] string? actionName = null,
            [Description("Optional PubSub connection name used by runtime routing.")] string? connectionName = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);

            PubSubActionTarget target = CreateTarget(connectionName, dataSetWriterId, actionTargetId, actionName);
            return await manager.RegisterActionResponderAsync(
                target,
                new DelegatePubSubActionHandler(static (invocation, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(new PubSubActionHandlerResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputFields = CopyFields(invocation.InputFields)
                    });
                }),
                "echo",
                "Echoes Action input fields to output fields with StatusCode Good.",
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Binds a PubSub Action responder to an OPC UA server method through an active session.
        /// </summary>
        [McpServerTool(Name = "pubsub_bind_action_method")]
        [Description("Register a PubSub Action responder that calls an OPC UA server method through an active " +
            "session (unlike pubsub_register_action_responder, which only echoes fields). Returns the responder " +
            "registration details (target, handler description) for use with pubsub_list_action_responders.")]
        public static async Task<PubSubActionResponderRegistration> BindActionMethodAsync(
            PubSubRuntimeManager manager,
            OpcUaSessionManager sessionManager,
            [Description("DataSetWriterId that owns the Action target.")] ushort dataSetWriterId,
            [Description("ActionTargetId handled by the responder.")] ushort actionTargetId,
            [Description("Object node ID on which the method is defined.")] string objectId,
            [Description("Method node ID to call.")] string methodId,
            [Description("Optional action name associated with the target.")] string? actionName = null,
            [Description("Optional PubSub connection name used by runtime routing.")] string? connectionName = null,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);
            ArgumentNullException.ThrowIfNull(sessionManager);
            ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodId);

            ISession session = sessionManager.GetSessionOrThrow(sessionName);
            NodeId parsedObjectId = OpcUaJsonHelper.ParseNodeId(objectId);
            NodeId parsedMethodId = OpcUaJsonHelper.ParseNodeId(methodId);
            PubSubActionTarget target = CreateTarget(connectionName, dataSetWriterId, actionTargetId, actionName);

            return await manager.RegisterActionResponderAsync(
                target,
                new DelegatePubSubActionHandler((invocation, cancellationToken) =>
                    CallBoundMethodAsync(session, parsedObjectId, parsedMethodId, invocation, cancellationToken)),
                "method",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Calls objectId={parsedObjectId}, methodId={parsedMethodId}, " +
                    $"sessionName={sessionName ?? string.Empty}."),
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists known PubSub Action targets.
        /// </summary>
        [McpServerTool(Name = "pubsub_list_action_targets")]
        [Description("List Action targets known from the active PubSub configuration and MCP responders.")]
        public static async Task<ArrayOf<PubSubActionTargetInfo>> ListActionTargetsAsync(
            PubSubRuntimeManager manager,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);
            return await manager.ListActionTargetsAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists PubSub Action responders registered through MCP.
        /// </summary>
        [McpServerTool(Name = "pubsub_list_action_responders")]
        [Description("List Action responders registered on the active PubSub runtime through MCP.")]
        public static async Task<ArrayOf<PubSubActionResponderRegistration>> ListActionRespondersAsync(
            PubSubRuntimeManager manager,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);
            return await manager.ListActionRespondersAsync(ct).ConfigureAwait(false);
        }

        private static async ValueTask<PubSubActionHandlerResult> CallBoundMethodAsync(
            ISession session,
            NodeId objectId,
            NodeId methodId,
            PubSubActionInvocation invocation,
            CancellationToken ct)
        {
            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = objectId,
                    MethodId = methodId,
                    InputArguments = CreateInputArguments(invocation.InputFields)
                }
            ];

            try
            {
                CallResponse response = await session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
                CallMethodResult result = response.Results[0];
                return new PubSubActionHandlerResult
                {
                    StatusCode = result.StatusCode,
                    OutputFields = result.OutputArguments.IsNull
                        ? []
                        : CreateOutputFields(result.OutputArguments)
                };
            }
            catch (ServiceResultException ex)
            {
                return new PubSubActionHandlerResult
                {
                    StatusCode = ex.StatusCode
                };
            }
        }

        private static PubSubActionTarget CreateTarget(
            string? connectionName,
            ushort dataSetWriterId,
            ushort actionTargetId,
            string? actionName)
        {
            return new PubSubActionTarget
            {
                ConnectionName = connectionName ?? string.Empty,
                DataSetWriterId = dataSetWriterId,
                ActionTargetId = actionTargetId,
                ActionName = actionName ?? string.Empty
            };
        }

        internal static PubSubActionResponseSummary Summarize(PubSubActionResponse response)
        {
            return new PubSubActionResponseSummary(
                response.Target.ConnectionName,
                response.Target.DataSetWriterId,
                response.Target.ActionTargetId,
                response.Target.ActionName,
                response.RequestId,
                ToBase64(response.CorrelationData),
                response.StatusCode.ToString(),
                response.ActionState.ToString(),
                SummarizeFields(response.OutputFields));
        }

        internal static ArrayOf<DataSetField> ParseFields(string? fieldText)
        {
            if (string.IsNullOrWhiteSpace(fieldText))
            {
                return [];
            }

            string trimmed = fieldText.Trim();
            return trimmed.StartsWith('{')
                ? ParseJsonFields(trimmed)
                : ParseNameValueFields(trimmed);
        }

        private static ArrayOf<DataSetField> ParseJsonFields(string fieldText)
        {
            using var document = JsonDocument.Parse(fieldText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Action input fields JSON must be an object.", nameof(fieldText));
            }

            var fields = new List<DataSetField>();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                string? dataType = null;
                JsonElement value = property.Value;
                if (property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("value", out JsonElement wrappedValue))
                {
                    value = wrappedValue;
                    if (property.Value.TryGetProperty("dataType", out JsonElement dataTypeElement) &&
                        dataTypeElement.ValueKind == JsonValueKind.String)
                    {
                        dataType = dataTypeElement.GetString();
                    }
                }

                fields.Add(new DataSetField
                {
                    Name = property.Name,
                    Value = OpcUaJsonHelper.JsonElementToVariant(value, dataType)
                });
            }

            return [.. fields];
        }

        private static ArrayOf<DataSetField> ParseNameValueFields(string fieldText)
        {
            var fields = new List<DataSetField>();
            foreach (string entry in fieldText.Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException(
                        "Action input fields must be JSON object text or name[:type]=value pairs separated by ';'.",
                        nameof(fieldText));
                }

                string[] nameParts = parts[0].Split(':', 2, StringSplitOptions.TrimEntries);
                fields.Add(new DataSetField
                {
                    Name = nameParts[0],
                    Value = ParseTextVariant(parts[1], nameParts.Length == 2 ? nameParts[1] : "String")
                });
            }

            return [.. fields];
        }

        private static Variant ParseTextVariant(string text, string dataType)
        {
            return dataType.Trim().ToLowerInvariant() switch
            {
                "boolean" or "bool" => new Variant(bool.Parse(text)),
                "sbyte" => new Variant(sbyte.Parse(text, CultureInfo.InvariantCulture)),
                "byte" => new Variant(byte.Parse(text, CultureInfo.InvariantCulture)),
                "int16" => new Variant(short.Parse(text, CultureInfo.InvariantCulture)),
                "uint16" => new Variant(ushort.Parse(text, CultureInfo.InvariantCulture)),
                "int32" or "int" => new Variant(int.Parse(text, CultureInfo.InvariantCulture)),
                "uint32" => new Variant(uint.Parse(text, CultureInfo.InvariantCulture)),
                "int64" or "long" => new Variant(long.Parse(text, CultureInfo.InvariantCulture)),
                "uint64" => new Variant(ulong.Parse(text, CultureInfo.InvariantCulture)),
                "float" => new Variant(float.Parse(text, CultureInfo.InvariantCulture)),
                "double" => new Variant(double.Parse(text, CultureInfo.InvariantCulture)),
                "datetime" => new Variant(DateTime.Parse(text, CultureInfo.InvariantCulture).ToUniversalTime()),
                _ => new Variant(text)
            };
        }

        internal static ArrayOf<DataSetField> CopyFields(ArrayOf<DataSetField> fields)
        {
            if (fields.IsNull)
            {
                return [];
            }

            var copy = new List<DataSetField>();
            foreach (DataSetField field in fields)
            {
                copy.Add(new DataSetField
                {
                    Name = field.Name,
                    Value = field.Value,
                    StatusCode = field.StatusCode,
                    SourceTimestamp = field.SourceTimestamp,
                    SourcePicoSeconds = field.SourcePicoSeconds,
                    ServerTimestamp = field.ServerTimestamp,
                    ServerPicoSeconds = field.ServerPicoSeconds,
                    Encoding = field.Encoding
                });
            }

            return [.. copy];
        }

        internal static ArrayOf<Variant> CreateInputArguments(ArrayOf<DataSetField> fields)
        {
            if (fields.IsNull)
            {
                return [];
            }

            var inputArguments = new List<Variant>();
            foreach (DataSetField field in fields)
            {
                inputArguments.Add(field.Value);
            }

            return [.. inputArguments];
        }

        internal static ArrayOf<DataSetField> CreateOutputFields(ArrayOf<Variant> outputArguments)
        {
            var outputFields = new List<DataSetField>();
            for (int i = 0; i < outputArguments.Count; i++)
            {
                outputFields.Add(new DataSetField
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"output{i}"),
                    Value = outputArguments[i]
                });
            }

            return [.. outputFields];
        }

        internal static ArrayOf<PubSubActionFieldValue> SummarizeFields(ArrayOf<DataSetField> fields)
        {
            if (fields.IsNull)
            {
                return [];
            }

            var values = new List<PubSubActionFieldValue>();
            foreach (DataSetField field in fields)
            {
                values.Add(ToFieldValue(field));
            }

            return [.. values];
        }

        private static PubSubActionFieldValue ToFieldValue(DataSetField field)
        {
            return new PubSubActionFieldValue(
                field.Name,
                field.Value.IsNull ? string.Empty : field.Value.TypeInfo.BuiltInType.ToString(),
                field.Value.IsNull ? string.Empty : field.Value.ToString(),
                field.StatusCode.ToString());
        }

        private static string ToBase64(ByteString value)
        {
            return value.IsNull ? string.Empty : Convert.ToBase64String(value.Span);
        }
    }

    /// <summary>
    /// One JSON-friendly PubSub Action field value.
    /// </summary>
    public sealed record PubSubActionFieldValue(
        string Name,
        string BuiltInType,
        string Value,
        string StatusCode);

    /// <summary>
    /// JSON-friendly PubSub Action response returned to MCP callers.
    /// </summary>
    public sealed record PubSubActionResponseSummary(
        string ConnectionName,
        ushort DataSetWriterId,
        ushort ActionTargetId,
        string ActionName,
        ushort RequestId,
        string CorrelationData,
        string StatusCode,
        string ActionState,
        ArrayOf<PubSubActionFieldValue> OutputFields);
}
