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
using System.IO;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    public sealed partial class JsonWotPayloadCodec
    {
        /// <inheritdoc/>
        public WotEncodeResult EncodeArguments(
            WotInvokeRequest request, WotPayloadDescriptor payload, WotBindingBounds bounds)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (bounds is null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }
            try
            {
                WotMethodArgumentLayout layout = RequireLayout(payload.InputLayout);
                if (request.Inputs.Count != layout.ArgumentCount)
                {
                    return WotEncodeResult.Fail(
                        $"The action declares {layout.ArgumentCount} input arguments, not {request.Inputs.Count}.",
                        StatusCodes.BadInvalidArgument);
                }
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    if (layout.Kind == WotMethodArgumentLayoutKind.None)
                    {
                        if (layout.Schema.ValueKind != JsonValueKind.Undefined)
                        {
                            writer.WriteStartObject();
                            writer.WriteEndObject();
                        }
                    }
                    else
                    {
                        WotPayloadSchema schema = payload.GetActionSchema();
                        bool named = layout.Kind == WotMethodArgumentLayoutKind.Named;
                        if (named)
                        {
                            writer.WriteStartObject();
                        }
                        for (int index = 0; index < request.Inputs.Count; index++)
                        {
                            if (named)
                            {
                                writer.WritePropertyName(layout.FieldOrder[index]);
                            }
                            ValueContract contract = ArgumentContract(layout, schema, "input", index, request.Context);
                            WriteTypedValue(writer, request.Inputs[index], contract, request.Context);
                        }
                        if (named)
                        {
                            writer.WriteEndObject();
                        }
                    }
                }
                ByteString data = new(buffer.ToArray());
                CheckPayload(data, bounds, request.Context);
                return WotEncodeResult.Ok(data.Memory);
            }
            catch (ServiceResultException exception)
            {
                StatusCode status = exception.StatusCode;
                return WotEncodeResult.Fail(exception.Message,
                    status == StatusCodes.BadEncodingLimitsExceeded ||
                    status == StatusCodes.BadNotSupported ||
                    status == StatusCodes.BadInvalidArgument ||
                    status == StatusCodes.BadNodeIdInvalid
                        ? status : StatusCodes.BadEncodingError);
            }
            catch (Exception exception) when (IsPayloadException(exception))
            {
                return WotEncodeResult.Fail(exception.Message);
            }
        }

        /// <inheritdoc/>
        public WotInvokeResult DecodeArguments(
            ByteString data, WotPayloadDescriptor payload, IServiceMessageContext context, WotBindingBounds bounds)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (bounds is null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }
            try
            {
                WotMethodArgumentLayout layout = RequireLayout(payload.OutputLayout);
                if (layout.Kind == WotMethodArgumentLayoutKind.None)
                {
                    if (layout.Schema.ValueKind != JsonValueKind.Undefined || !data.IsEmpty)
                    {
                        using JsonDocument empty = ParsePayload(data, bounds, context);
                        if (layout.Schema.ValueKind == JsonValueKind.Undefined ||
                            empty.RootElement.ValueKind != JsonValueKind.Object ||
                            empty.RootElement.EnumerateObject().MoveNext())
                        {
                            throw InvalidPayload("The action declares no output values.");
                        }
                    }
                    return new WotInvokeResult(StatusCodes.Good, []).WithContext(context);
                }
                using JsonDocument document = ParsePayload(data, bounds, context);
                JsonElement root = document.RootElement;
                bool named = layout.Kind == WotMethodArgumentLayoutKind.Named;
                if (named)
                {
                    RequireMembers(root, layout.FieldOrder);
                }
                WotPayloadSchema schema = payload.GetActionSchema();
                var valueContext = new ServiceMessageContext(context, context.Telemetry);
                var outputs = new DataValue[layout.ArgumentCount];
                DateTimeUtc now = DateTimeUtc.Now;
                for (int index = 0; index < outputs.Length; index++)
                {
                    JsonElement value = named ? root.GetProperty(layout.FieldOrder[index]) : root;
                    ValueContract contract = ArgumentContract(layout, schema, "output", index, valueContext);
                    outputs[index] = new DataValue(
                        ReadTypedValue(value, contract, valueContext), StatusCodes.Good, now, now);
                }
                return new WotInvokeResult(StatusCodes.Good, outputs).WithContext(valueContext);
            }
            catch (ServiceResultException exception)
            {
                return new WotInvokeResult(
                    DecodeStatus(exception.StatusCode), null, exception.Message);
            }
            catch (Exception exception) when (IsPayloadException(exception))
            {
                return new WotInvokeResult(StatusCodes.BadDecodingError, null, exception.Message);
            }
        }

        /// <inheritdoc/>
        public WotNotification DecodeEvent(
            ByteString data,
            WotPayloadDescriptor payload,
            WotEventSelection selection,
            IServiceMessageContext context,
            WotBindingBounds bounds)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            if (selection is null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (bounds is null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }
            try
            {
                using JsonDocument document = ParsePayload(data, bounds, context);
                var valueContext = new ServiceMessageContext(context, context.Telemetry);
                ArrayOf<ArrayOf<string>> paths = WotEventSelectClauses.GetMaterializedMemberPaths(selection.Clauses);
                var values = new Variant[selection.Clauses.Count];
                DateTimeUtc sourceTime = DateTimeUtc.Now;
                DateTimeUtc receiveTime = sourceTime;
                for (int index = 0; index < values.Length; index++)
                {
                    WotResolvedEventSelectClause clause = selection.Clauses[index];
                    JsonElement field = ReadMemberPath(document.RootElement, paths[index], false);
                    ValueContract contract = EventContract(payload, clause, paths[index], valueContext);
                    values[index] = ReadTypedValue(field, contract, valueContext);
                    if (IsBaseField(clause, "Time") && values[index].TryGetValue(out DateTimeUtc time))
                    {
                        sourceTime = time;
                    }
                    if (IsBaseField(clause, "ReceiveTime") && values[index].TryGetValue(out time))
                    {
                        receiveTime = time;
                    }
                }
                var builder = new WotEventDataBuilder();
                var fields = new Dictionary<string, DataValue>(StringComparer.Ordinal);
                for (int index = 0; index < values.Length; index++)
                {
                    WotResolvedEventSelectClause clause = selection.Clauses[index];
                    var field = new DataValue(values[index], StatusCodes.Good, sourceTime, receiveTime);
                    if (!builder.Add(paths[index], field))
                    {
                        throw InvalidPayload("Two event fields materialize the same data member.");
                    }
                    fields.Add(clause.IsConditionIdSelection
                        ? WotEventSelectClauses.ConditionIdFieldName : clause.BrowsePath, field);
                }
                return new WotNotification(
                    new DataValue(new Variant(new ArrayOf<Variant>(values)), StatusCodes.Good, sourceTime, receiveTime),
                    fields, builder.Build()).WithContext(valueContext);
            }
            catch (ServiceResultException exception)
            {
                return new WotNotification(DataValue.FromStatusCode(DecodeStatus(exception.StatusCode)))
                    .WithContext(context);
            }
            catch (Exception exception) when (IsPayloadException(exception))
            {
                return new WotNotification(DataValue.FromStatusCode(StatusCodes.BadDecodingError)).WithContext(context);
            }
        }

        private static WotMethodArgumentLayout RequireLayout(WotMethodArgumentLayout? layout)
        {
            return layout ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The codec requires the action's resolved argument layouts.");
        }

        private static ValueContract ArgumentContract(
            WotMethodArgumentLayout layout,
            WotPayloadSchema schema,
            string member,
            int index,
            IServiceMessageContext context)
        {
            string pointer = "/" + member;
            if (layout.Kind == WotMethodArgumentLayoutKind.Named)
            {
                pointer += "/properties/" + WotAffordanceForm.EscapePointerToken(layout.FieldOrder[index]);
            }
            return ResolveContract(layout.GetArgumentSchema(index), pointer, schema, context);
        }

        private static ValueContract EventContract(
            WotPayloadDescriptor payload,
            WotResolvedEventSelectClause clause,
            ArrayOf<string> path,
            IServiceMessageContext context)
        {
            JsonElement schema = default;
            WotPayloadSchema? capturedSchema = clause.PayloadSchema ?? payload.Schema;
            string pointer = clause.PayloadSchema is null ? "/data" : string.Empty;
            if (clause.PayloadSchema is { } selected)
            {
                schema = ReadMemberPath(selected.Definition, path, true);
            }
            else if (payload.Schema is { } captured &&
                captured.Definition.TryGetProperty("data", out JsonElement dataSchema))
            {
                schema = ReadMemberPath(dataSchema, path, true);
            }
            foreach (string member in path)
            {
                pointer += "/properties/" + WotAffordanceForm.EscapePointerToken(member);
            }
            BuiltInType type = WotPayloadDescriptor.GetStandardEventFieldType(clause);
            return ResolveContract(schema, pointer, capturedSchema, context, TypeInfo.Create(type, ValueRanks.Scalar),
                preferFallback: type != BuiltInType.Null);
        }

        private static bool IsBaseField(WotResolvedEventSelectClause clause, string name)
        {
            return clause.TypeDefinitionId == WotEventSelectClauses.BaseEventTypeId &&
                clause.PathElements.Count == 1 && clause.PathElements[0] == name;
        }

        private static JsonElement ReadMemberPath(JsonElement root, ArrayOf<string> path, bool schema)
        {
            JsonElement current = root;
            foreach (string name in path)
            {
                if (schema)
                {
                    if (current.ValueKind != JsonValueKind.Object ||
                        !current.TryGetProperty("properties", out current))
                    {
                        return default;
                    }
                }
                if (current.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidPayload($"The event member '{name}' has no object parent.");
                }
                EnsureUniqueMembers(current);
                if (!current.TryGetProperty(name, out current))
                {
                    if (schema)
                    {
                        return default;
                    }
                    throw InvalidPayload($"The event omitted selected member '{name}'.");
                }
            }
            return current;
        }

        private static void RequireMembers(JsonElement value, ArrayOf<string> names)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw InvalidPayload("Named arguments require a JSON object.");
            }
            int count = 0;
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Contains(property.Name))
                {
                    throw InvalidPayload($"The payload contains undeclared argument '{property.Name}'.");
                }
                count++;
            }
            EnsureUniqueMembers(value);
            if (count != names.Count)
            {
                throw InvalidPayload("The payload does not contain every declared argument.");
            }
        }

        private static void EnsureUniqueMembers(JsonElement value)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw InvalidPayload($"The payload repeats member '{property.Name}'.");
                }
            }
        }

        private static JsonDocument ParsePayload(
            ByteString data, WotBindingBounds bounds, IServiceMessageContext context)
        {
            CheckPayload(data, bounds, context);
            return JsonDocument.Parse(data.Memory, new JsonDocumentOptions { MaxDepth = bounds.MaxPayloadDepth });
        }

        private static void CheckPayload(ByteString data, WotBindingBounds bounds, IServiceMessageContext context)
        {
            WotBindingBounds.EnsurePositive(bounds.MaxPayloadBytes, nameof(bounds.MaxPayloadBytes));
            WotBindingBounds.EnsurePositive(bounds.MaxPayloadDepth, nameof(bounds.MaxPayloadDepth));
            if (data.Length > bounds.MaxPayloadBytes ||
                (context.MaxMessageSize > 0 && data.Length > context.MaxMessageSize))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The payload byte limit was exceeded.");
            }
            if (data.IsEmpty)
            {
                return;
            }
            int depth = Math.Min(bounds.MaxPayloadDepth, context.MaxEncodingNestingLevels);
            var reader = new Utf8JsonReader(data.Span, new JsonReaderOptions
            {
                MaxDepth = depth < int.MaxValue ? depth + 1 : depth
            });
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject &&
                    reader.CurrentDepth >= depth)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The payload nesting limit was exceeded.");
                }
            }
        }

        private static StatusCode DecodeStatus(StatusCode status)
        {
            return status == StatusCodes.BadEncodingLimitsExceeded || status == StatusCodes.BadNotSupported
                ? status : StatusCodes.BadDecodingError;
        }

        private static bool IsPayloadException(Exception exception)
        {
            return exception is JsonException or FormatException or InvalidOperationException or
                ArgumentException or OverflowException or NotSupportedException;
        }

        private static ServiceResultException InvalidPayload(string message)
        {
            return new ServiceResultException(StatusCodes.BadDecodingError, message);
        }
    }
}
