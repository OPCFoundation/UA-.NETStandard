/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Converter for extension objects
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    public class ExtensionObjectConverter : JsonConverter<ExtensionObject>
    {
        /// <inheritdoc/>
        public override ExtensionObject Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            ExpandedNodeId typeId = JsonSerializer.Deserialize<ExpandedNodeId>(
                root.GetProperty("TypeId").GetRawText(), options);
            string body = root.GetProperty("Body").GetRawText();
            return new ExtensionObject(typeId, body);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ExtensionObject value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
#pragma warning disable CS0618 // Type or member is obsolete
            writer.WritePropertyName("Body");
            JsonSerializer.Serialize(writer, value.Body, options);
            writer.WritePropertyName("TypeId");
            JsonSerializer.Serialize(writer, value.TypeId, options);
#pragma warning restore CS0618 // Type or member is obsolete
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Converter for numeric range.
    /// </summary>
    public class NumericRangeConverter : JsonConverter<NumericRange>
    {
        /// <inheritdoc/>
        public override NumericRange Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            int begin = root.GetProperty("Begin").GetInt32();
            int end = root.GetProperty("End").GetInt32();

            if (begin == -1)
            {
                return NumericRange.Null;
            }

            return new NumericRange(begin, end);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            NumericRange value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Begin", value.Begin);
            writer.WriteNumber("End", value.End);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Converter for <see cref="DateTimeUtc"/> which is a struct with a
    /// <c>NowIfDefault</c> property returning <see cref="DateTimeUtc"/>,
    /// causing infinite recursion in default STJ serialization.
    /// </summary>
    public class DateTimeUtcConverter : JsonConverter<DateTimeUtc>
    {
        /// <inheritdoc/>
        public override DateTimeUtc Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            long ticks = reader.GetInt64();
            return new DateTimeUtc(ticks);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            DateTimeUtc value,
            JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.Value);
        }
    }

    /// <summary>
    /// Factory for creating <see cref="ArrayOfConverter{T}"/> instances.
    /// <see cref="ArrayOf{T}"/> has a <c>[JsonConstructor]</c> with a parameter name
    /// that does not match its property name, preventing default STJ deserialization.
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "System.Text.Json converter factory for known OPC UA ArrayOf<T>.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "System.Text.Json converter factory for known OPC UA ArrayOf<T>.")]
    public class ArrayOfConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType &&
                typeToConvert.GetGenericTypeDefinition() == typeof(ArrayOf<>);
        }

        /// <inheritdoc/>
        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(ArrayOfConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }
    }

    /// <summary>
    /// Converter for <see cref="ArrayOf{T}"/> that serializes as a JSON array.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    public class ArrayOfConverter<T> : JsonConverter<ArrayOf<T>>
    {
        /// <inheritdoc/>
        public override ArrayOf<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            T[] array = JsonSerializer.Deserialize<T[]>(ref reader, options);
            return array == null ? ArrayOf<T>.Null : new ArrayOf<T>(array.AsMemory());
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ArrayOf<T> value,
            JsonSerializerOptions options)
        {
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.ToArray(), options);
        }
    }

    /// <summary>
    /// Converter for node ids
    /// </summary>
    public class NodeIdConverter : JsonConverter<NodeId>
    {
        /// <inheritdoc/>
        public override NodeId Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string text = reader.GetString();
            return text == null ? NodeId.Null : NodeId.Parse(text);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            NodeId value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.IsNull ? null : value.ToString());
        }
    }

    /// <summary>
    /// Expanded node id converter
    /// </summary>
    public class ExpandedNodeIdConverter : JsonConverter<ExpandedNodeId>
    {
        /// <inheritdoc/>
        public override ExpandedNodeId Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string text = reader.GetString();
            return text == null ? ExpandedNodeId.Null : ExpandedNodeId.Parse(text);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ExpandedNodeId value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.IsNull ? null : value.ToString());
        }
    }

    /// <summary>
    /// Qualified name converter
    /// </summary>
    public class QualifiedNameConverter : JsonConverter<QualifiedName>
    {
        /// <inheritdoc/>
        public override QualifiedName Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string text = reader.GetString();
            return text == null ? QualifiedName.Null : QualifiedName.Parse(text);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            QualifiedName value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.IsNull ? null : value.ToString());
        }
    }

    /// <summary>
    /// Status code converter
    /// </summary>
    public class StatusCodeConverter : JsonConverter<StatusCode>
    {
        /// <inheritdoc/>
        public override StatusCode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new StatusCode(reader.GetUInt32());
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            StatusCode value,
            JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.Code);
        }
    }

    /// <summary>
    /// Converter for <see cref="Opc.Ua.Variant"/> that stores the raw value
    /// as a number (if numeric) or a string representation.
    /// Since <see cref="Opc.Ua.Variant"/> can hold any OPC UA value, the converter
    /// stores the built-in type alongside the value for round-tripping.
    /// </summary>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "System.Text.Json converter for known OPC UA types.")]
    public class VariantConverter : JsonConverter<Variant>
    {
        /// <inheritdoc/>
        public override Variant Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return Variant.Null;
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            using var decoder = new JsonDecoder(
                doc,
                AmbientMessageContext.CurrentContext);
            return decoder.ReadVariant("Value");
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            Variant value,
            JsonSerializerOptions options)
        {
            if (value.IsNull)
            {
                writer.WriteNullValue();
                return;
            }

            using var encoder = new JsonEncoder(
                writer,
                AmbientMessageContext.CurrentContext);
            encoder.WriteVariant("Value", value);
        }
    }

    /// <summary>
    /// Converter for <see cref="ServiceResult"/> that serializes as a JSON object
    /// with StatusCode and optional AdditionalInfo.
    /// </summary>
    public class ServiceResultConverter : JsonConverter<ServiceResult>
    {
        /// <inheritdoc/>
        public override ServiceResult Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            JsonElement root = doc.RootElement;

            uint code = root.GetProperty("StatusCode").GetUInt32();
            return new ServiceResult(new StatusCode(code));
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ServiceResult value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteNumber("StatusCode", value.StatusCode.Code);
            writer.WriteEndObject();
        }
    }
}
