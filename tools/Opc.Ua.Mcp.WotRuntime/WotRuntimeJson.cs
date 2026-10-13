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
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Bounded UA JSON projection with secret-free errors and no late-cancellation result masking.
    /// </summary>
    internal static class WotRuntimeJson
    {
        /// <summary>
        /// Validates a caller-provided text field without echoing its contents in errors.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void Text(string value, int maximum, bool empty = false)
        {
            ArgumentNullException.ThrowIfNull(value);
            if ((!empty && string.IsNullOrWhiteSpace(value)) ||
                value.Length > maximum ||
                value.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("A live WoT input is empty, invalid or exceeds its length limit.");
            }
        }

        /// <summary>
        /// Decodes an explicitly typed value through the existing UA JSON codec, within host limits.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static Variant Decode(
            WotRuntimeValue input, IServiceMessageContext context, WotRuntimeOptions options, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!Enum.IsDefined(input.Type) ||
                input.Type > BuiltInType.DiagnosticInfo ||
                input.Value.ValueKind == JsonValueKind.Undefined ||
                (input.Type == BuiltInType.Null && input.Value.ValueKind != JsonValueKind.Null))
            {
                throw new ArgumentException("The typed input requires a supported UA type and an explicit value.");
            }
            using var buffer = new McpBoundedMemoryStream(options.MaxPayloadBytes, ct);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("value");
                writer.WriteNumber("UaType", (int)input.Type);
                writer.WritePropertyName("Value");
                input.Value.WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            buffer.Position = 0;
            using var decoder = new JsonDecoder(buffer, context);
            return decoder.ReadVariant("value");
        }

        /// <summary>
        /// Encodes a DataValue without boxing its Variant or losing UA quality and source timestamps.
        /// </summary>
        internal static JsonNode? DataValue(in DataValue value, IServiceMessageContext context, int maximum)
        {
            using var buffer = new McpBoundedMemoryStream(maximum);
            using (var encoder = new JsonEncoder(buffer, context, JsonEncoderOptions.Verbose))
            {
                encoder.WriteDataValue("value", value);
                _ = encoder.Close();
            }
            var root = JsonNode.Parse(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
            return root?["value"]?.DeepClone();
        }

        /// <summary>
        /// Preserves an operation status without trusting a protocol or provider's error text.
        /// </summary>
        internal static JsonObject Status(StatusCode status)
        {
            return new JsonObject
            {
                ["error"] = StatusCode.IsBad(status),
                ["statusCode"] = status.ToString(),
                ["statusCodeValue"] = status.Code,
                ["completion"] = StatusCode.IsGood(status) ? "completed" : "reportedFailure",
                ["automaticallyRetried"] = false
            };
        }

        /// <summary>
        /// Projects one notification, including its full typed event field envelope when available.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static JsonObject Notification(
            WotNotification notification, IServiceMessageContext context, int maximum)
        {
            context = notification.Context ?? context;
            if (notification.Context is null && !notification.NamespaceUris.IsEmpty)
            {
                if (notification.NamespaceUris.Count > 256)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The notification namespace table exceeds the live WoT ceiling.");
                }
                context = new ServiceMessageContext(context, context.Telemetry)
                {
                    NamespaceUris = new NamespaceTable(notification.NamespaceUris.ToList())
                };
            }
            if (notification.EventFields.Count > 64)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The event field count exceeds the live WoT ceiling.");
            }
            using var buffer = new McpBoundedMemoryStream(maximum);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("dataValue");
                WriteDataValue(writer, notification.Value, context, maximum);
                writer.WriteStartObject("eventFields");
                foreach ((string name, DataValue value) in notification.EventFields)
                {
                    Text(name, 2048);
                    writer.WritePropertyName(name);
                    WriteDataValue(writer, value, context, maximum);
                }
                writer.WriteEndObject();
                writer.WritePropertyName("data");
                int members = 0;
                WriteEventData(writer, notification.Data, context, maximum, 0, ref members);
                writer.WriteEndObject();
            }
            return JsonNode.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))!.AsObject();
        }

        /// <summary>
        /// Writes a JSON tree into a bounded buffer before returning matching text and structured content.
        /// </summary>
        internal static CallToolResult Result(JsonObject payload, int maximum)
        {
            using var buffer = new McpBoundedMemoryStream(maximum);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                payload.WriteTo(writer);
            }
            using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
            return new CallToolResult
            {
                IsError = payload["error"] is JsonValue value && value.TryGetValue(out bool error) && error,
                StructuredContent = document.RootElement.Clone(),
                Content = [new TextContentBlock { Text = document.RootElement.GetRawText() }]
            };
        }

        /// <summary>
        /// Sanitizes every provider/executor failure; completed mutations are projected without rechecking ct.
        /// </summary>
        internal static async Task<CallToolResult> ExecuteAsync(
            Func<CancellationToken, ValueTask<JsonObject>> action, WotRuntimeOptions options, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                JsonObject result = await action(ct).ConfigureAwait(false);
                return Result(result, options.MaxResultBytes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException and not StackOverflowException)
            {
                StatusCode status = failure switch
                {
                    ServiceResultException service => service.StatusCode,
                    OperationCanceledException or TimeoutException => StatusCodes.BadTimeout,
                    ArgumentException or FormatException or JsonException => StatusCodes.BadInvalidArgument,
                    IOException => StatusCodes.BadEncodingLimitsExceeded,
                    NotSupportedException => StatusCodes.BadNotSupported,
                    _ => StatusCodes.BadUnexpectedError
                };
                JsonObject result = Status(status);
                result["completion"] = "notConfirmed";
                result["message"] = "Live WoT execution was refused or failed. Check host grants, the pinned form, " +
                    "secret references and runtime state. Do not automatically repeat a mutation.";
                return Result(result, options.MaxResultBytes);
            }
        }

        /// <summary>
        /// Writes a typed value within the remaining complete-notification budget.
        /// </summary>
        private static void WriteDataValue(
            Utf8JsonWriter writer, in DataValue value, IServiceMessageContext context, int maximum)
        {
            int remaining = maximum - checked((int)(writer.BytesCommitted + writer.BytesPending));
            JsonNode? encoded = DataValue(value, context, Math.Max(0, remaining));
            if (encoded is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                encoded.WriteTo(writer);
            }
            writer.Flush();
        }

        /// <summary>
        /// Preserves the existing nested WoT event-data shape without reconstructing slash-separated paths.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void WriteEventData(
            Utf8JsonWriter writer, WotEventData data, IServiceMessageContext context, int maximum,
            int depth, ref int members)
        {
            if (depth > 16 || ++members > 128)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The event data nesting or member count exceeds the live WoT ceiling.");
            }
            if (data.HasValue)
            {
                WriteDataValue(writer, data.Value, context, maximum);
                return;
            }
            writer.WriteStartObject();
            foreach ((string name, WotEventData member) in data.Members)
            {
                Text(name, 2048);
                writer.WritePropertyName(name);
                WriteEventData(writer, member, context, maximum, depth + 1, ref members);
            }
            writer.WriteEndObject();
            writer.Flush();
        }
    }
}
