// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers;
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Internal helpers that translate a single <see cref="JsonElement"/>
    /// (the value of one field in the <c>Payload</c> object of a JSON
    /// DataSetMessage) into a <see cref="Variant"/> or
    /// <see cref="DataValue"/> by delegating to the Stack
    /// <see cref="Ua.JsonDecoder"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>. The Stack <see cref="Ua.JsonDecoder"/>
    /// expects the top of its element stack to be a JSON object, so the
    /// helper copies the UTF-8 bytes of the supplied element into a
    /// pooled synthetic <c>{ "v": &lt;element&gt; }</c> envelope before
    /// reading. The copy is written straight from the parsed document,
    /// so no intermediate UTF-16 text is materialised.
    /// </remarks>
    internal static class JsonVariantDecoder
    {
        /// <summary>
        /// Property name of the synthetic splice envelope.
        /// </summary>
        internal const string SpliceFieldName = "v";

        /// <summary>
        /// Decodes a single Variant payload from the supplied element.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="mode">
        /// Detected encoding mode. <see cref="JsonEncodingMode.Verbose"/>
        /// expects the Part 6 §5.4.2.17 <c>{ "UaType", "Value" }</c>
        /// envelope, or the bare value of a field whose FieldMetaData
        /// supplies a concrete type (Part 14 §7.2.5.4.2);
        /// <see cref="JsonEncodingMode.Compact"/> and
        /// <see cref="JsonEncodingMode.RawData"/> expect bare values.
        /// </param>
        /// <param name="typeInfo">
        /// Required for Compact / RawData decoding and for collapsed
        /// Verbose fields when the metadata declares the field's type.
        /// </param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded variant.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Variant DecodeVariant(
            JsonElement element,
            JsonEncodingMode mode,
            TypeInfo? typeInfo,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return Variant.Null;
            }
            if (JsonVariantEncoder.WrapsInVariantEnvelope(mode) &&
                (typeInfo is not { } concrete ||
                    !JsonVariantEncoder.IsCollapsedField(concrete.BuiltInType, concrete.ValueRank) ||
                    IsVariantEnvelope(element)))
            {
                return DecodeSpliced(
                    element,
                    context,
                    static decoder => decoder.ReadVariant(SpliceFieldName));
            }
            if (typeInfo is null)
            {
                return Variant.Null;
            }
            TypeInfo resolved = typeInfo.Value;
            return DecodeSpliced(
                element,
                context,
                decoder => decoder.ReadVariantValue(SpliceFieldName, resolved));
        }

        /// <summary>
        /// Decodes the value of a DataSet field described by
        /// <paramref name="metaData"/>. Besides the Part 6 §5.4.2.17
        /// Variant object, the field shapes of Part 14 §7.2.5.4.3
        /// Table 186 without <c>UaType</c> / <c>UaTypeId</c> are typed by
        /// the FieldMetaData: collapsed values, Enumerations as Verbose
        /// <c>&lt;name&gt;_&lt;value&gt;</c> strings (Part 6 §5.4.4.2) or
        /// numbers, Structures without <c>UaTypeId</c> and the
        /// <c>{ "Value", "Dimensions" }</c> object of fields with a
        /// multi-dimensional or abstract ValueRank.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="mode">Detected encoding mode.</param>
        /// <param name="metaData">Optional FieldMetaData of the field.</param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded variant.</returns>
        /// <exception cref="ServiceResultException">A Verbose field of an
        /// abstract DataType has no <c>UaType</c>.</exception>
        public static Variant DecodeField(
            JsonElement element,
            JsonEncodingMode mode,
            FieldMetaData? metaData,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return Variant.Null;
            }
            if (IsLegacyVariantEnvelope(element))
            {
                return DecodeLegacyVariant(element, context);
            }
            if (metaData is null || IsVariantEnvelope(element))
            {
                return DecodeVariant(
                    element,
                    mode,
                    metaData is null
                        ? null
                        : TypeInfo.Create((BuiltInType)metaData.BuiltInType, metaData.ValueRank),
                    context);
            }
            var builtInType = (BuiltInType)metaData.BuiltInType;
            int valueRank = metaData.ValueRank;
            if (JsonVariantEncoder.IsEnumerationField(metaData))
            {
                // Verbose "<name>_<value>" strings and Compact numbers both
                // decode; the value keeps the Int32 BuiltInType of the field.
                Variant enumeration = DecodeSpliced(
                    element,
                    context,
                    decoder => decoder.ReadVariantValue(
                        SpliceFieldName,
                        TypeInfo.Create(BuiltInType.Enumeration, valueRank)));
                if (valueRank == ValueRanks.Scalar)
                {
                    return enumeration.TryGetValue(out int scalar) ? new Variant(scalar) : Variant.Null;
                }
                return enumeration.TryGetValue(out ArrayOf<int> array) ? new Variant(array) : Variant.Null;
            }
            if (JsonVariantEncoder.IsCollapsedField(builtInType, valueRank))
            {
                return DecodeVariant(element, mode, TypeInfo.Create(builtInType, valueRank), context);
            }
            if (builtInType is BuiltInType.Null or BuiltInType.Variant or
                BuiltInType.Number or BuiltInType.Integer or BuiltInType.UInteger or
                BuiltInType.Enumeration)
            {
                if (JsonVariantEncoder.WrapsInVariantEnvelope(mode))
                {
                    // Part 14 §7.2.5.4.3: with RawData=FALSE the Publisher
                    // always includes the UaType of an abstract DataType.
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "The field of the abstract DataType {0} has no UaType.",
                        builtInType);
                }
                return DecodeVariant(element, mode, TypeInfo.Create(builtInType, valueRank), context);
            }
            return DecodeTypedVariant(element, builtInType, valueRank, metaData.DataType, context);
        }

        /// <summary>
        /// Decodes a Structure field or a field with a multi-dimensional or
        /// abstract ValueRank by supplying the <c>UaType</c> (and, for a
        /// Structure without one, the <c>UaTypeId</c>) that the
        /// FieldMetaData defines (Part 14 §7.2.5.4.3 Table 186).
        /// </summary>
        private static Variant DecodeTypedVariant(
            JsonElement element,
            BuiltInType builtInType,
            int valueRank,
            NodeId dataType,
            IServiceMessageContext context)
        {
            JsonElement value = element;
            JsonElement dimensions = default;
            bool fixedRank = valueRank is ValueRanks.Scalar or ValueRanks.OneDimension;
            if (!fixedRank && IsValueDimensionsObject(element))
            {
                value = element.GetProperty("Value");
                element.TryGetProperty("Dimensions", out dimensions);
            }
            string? typeId = null;
            ExpandedNodeId unregisteredTypeId = ExpandedNodeId.Null;
            if (builtInType == BuiltInType.ExtensionObject && !dataType.IsNull)
            {
                ExpandedNodeId absoluteTypeId = NodeId.ToExpandedNodeId(dataType, context.NamespaceUris);
                if (absoluteTypeId.IsNull)
                {
                    absoluteTypeId = dataType;
                }
                if (context.Factory.TryGetEncodeableType(absoluteTypeId, out _))
                {
                    // The decoder needs the UaTypeId inside each Structure
                    // to create the registered type.
                    typeId = dataType.ToString();
                }
                else
                {
                    // An unregistered type is kept as a JSON body, so the
                    // TypeId is set on the decoded values instead. Copying
                    // the DataType of the (possibly received) metadata into
                    // every element would let a small message expand into
                    // gigabytes.
                    unregisteredTypeId = absoluteTypeId;
                }
            }
            using JsonBufferWriter buffer = new(256);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(SpliceFieldName);
                writer.WriteStartObject();
                writer.WriteNumber("UaType", (int)builtInType);
                writer.WritePropertyName("Value");
                WriteWithTypeId(writer, value, typeId, allowArray: true);
                if (dimensions.ValueKind != JsonValueKind.Undefined)
                {
                    writer.WritePropertyName("Dimensions");
                    dimensions.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            Variant decoded;
            using (Ua.JsonDecoder decoder = new(
                new ReadOnlySequence<byte>(buffer.WrittenMemory),
                context))
            {
                decoded = decoder.ReadVariant(SpliceFieldName);
            }
            return unregisteredTypeId.IsNull
                ? decoded
                : ApplyStructureTypeId(decoded, value, unregisteredTypeId);
        }

        /// <summary>
        /// Sets <paramref name="typeId"/> on the decoded Structures that came
        /// from a JSON object without a <c>UaTypeId</c>: the result is the
        /// same as decoding the object with that <c>UaTypeId</c> added, minus
        /// the <c>UaTypeId</c> member in the kept JSON body.
        /// </summary>
        private static Variant ApplyStructureTypeId(
            Variant decoded,
            JsonElement value,
            ExpandedNodeId typeId)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                return IsUntypedStructure(value) && decoded.TryGetValue(out ExtensionObject scalar)
                    ? new Variant(WithStructureTypeId(scalar, typeId))
                    : decoded;
            }
            if (value.ValueKind != JsonValueKind.Array)
            {
                return decoded;
            }
            if (decoded.TryGetValue(out ArrayOf<ExtensionObject> array) && !array.IsNull)
            {
                ExtensionObject[]? typed = ApplyStructureTypeId(array.Span, value, typeId);
                return typed is null ? decoded : new Variant((ArrayOf<ExtensionObject>)typed);
            }
            if (decoded.TryGetValue(out MatrixOf<ExtensionObject> matrix) && !matrix.IsNull)
            {
                ExtensionObject[]? typed = ApplyStructureTypeId(matrix.Span, value, typeId);
                return typed is null
                    ? decoded
                    : new Variant(new ReadOnlyMemory<ExtensionObject>(typed).ToMatrixOf(matrix.Dimensions));
            }
            return decoded;
        }

        private static ExtensionObject[]? ApplyStructureTypeId(
            ReadOnlySpan<ExtensionObject> values,
            JsonElement items,
            ExpandedNodeId typeId)
        {
            // The type is only added to the items of the outer array, as when
            // the UaTypeId is written into the JSON (see WriteWithTypeId).
            if (items.GetArrayLength() != values.Length)
            {
                return null;
            }
            var typed = new ExtensionObject[values.Length];
            int index = 0;
            foreach (JsonElement item in items.EnumerateArray())
            {
                typed[index] = IsUntypedStructure(item)
                    ? WithStructureTypeId(values[index], typeId)
                    : values[index];
                index++;
            }
            return typed;
        }

        private static bool IsUntypedStructure(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object &&
                !element.TryGetProperty("UaTypeId", out _);
        }

        private static ExtensionObject WithStructureTypeId(ExtensionObject value, ExpandedNodeId typeId)
        {
            if (value.IsNull)
            {
                // An empty JSON object: a Structure of the type without a body.
                return new ExtensionObject(typeId);
            }
            return value.TypeId.IsNull ? value.WithTypeId(typeId) : value;
        }

        /// <summary>
        /// Copies <paramref name="element"/>, adding the <c>UaTypeId</c>
        /// to a Structure object (or to each Structure of an array) that
        /// does not carry one.
        /// </summary>
        private static void WriteWithTypeId(
            Utf8JsonWriter writer,
            JsonElement element,
            string? typeId,
            bool allowArray)
        {
            if (typeId is not null &&
                element.ValueKind == JsonValueKind.Object &&
                !element.TryGetProperty("UaTypeId", out _))
            {
                writer.WriteStartObject();
                writer.WriteString("UaTypeId", typeId);
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    member.WriteTo(writer);
                }
                writer.WriteEndObject();
                return;
            }
            if (typeId is not null && allowArray && element.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteWithTypeId(writer, item, typeId, allowArray: false);
                }
                writer.WriteEndArray();
                return;
            }
            element.WriteTo(writer);
        }

        /// <summary>
        /// Whether <paramref name="element"/> is the
        /// <c>{ "Value", "Dimensions" }</c> object that Part 14 §7.2.5.4.3
        /// Table 186 uses for fields with a multi-dimensional or abstract
        /// ValueRank.
        /// </summary>
        /// <param name="element">Candidate element.</param>
        /// <returns><see langword="true"/> for such an object.</returns>
        internal static bool IsValueDimensionsObject(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("Value", out _))
            {
                return false;
            }
            foreach (JsonProperty member in element.EnumerateObject())
            {
                if (!member.NameEquals("Value") && !member.NameEquals("Dimensions"))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="element"/> is a Variant in the
        /// <c>{ "Type", "Body" }</c> form of the deprecated JSON
        /// ReversibleFieldEncoding (Part 14 §6.3.2.3.1 Table 112,
        /// FieldEncoding1=True, FieldEncoding2=False).
        /// </summary>
        /// <param name="element">Candidate element.</param>
        /// <returns><see langword="true"/> for a legacy Variant.</returns>
        internal static bool IsLegacyVariantEnvelope(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("Type", out JsonElement type) ||
                type.ValueKind != JsonValueKind.Number ||
                !element.TryGetProperty("Body", out _))
            {
                return false;
            }
            foreach (JsonProperty member in element.EnumerateObject())
            {
                if (!member.NameEquals("Type") &&
                    !member.NameEquals("Body") &&
                    !member.NameEquals("Dimensions"))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Decodes a legacy <c>{ "Type", "Body", "Dimensions" }</c> Variant
        /// by reading it as the Part 6 §5.4.2.17
        /// <c>{ "UaType", "Value", "Dimensions" }</c> object.
        /// </summary>
        private static Variant DecodeLegacyVariant(
            JsonElement element,
            IServiceMessageContext context)
        {
            using JsonBufferWriter buffer = new(256);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(SpliceFieldName);
                writer.WriteStartObject();
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    writer.WritePropertyName(
                        member.NameEquals("Type") ? "UaType" :
                        member.NameEquals("Body") ? "Value" :
                        "Dimensions");
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            using Ua.JsonDecoder decoder = new(
                new ReadOnlySequence<byte>(buffer.WrittenMemory),
                context);
            return decoder.ReadVariant(SpliceFieldName);
        }

        /// <summary>
        /// Decodes a single DataValue payload from the supplied element.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded DataValue (never null; may be
        /// <see cref="DataValue.IsNull"/>).</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static DataValue DecodeDataValue(
            JsonElement element,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return DataValue.Null;
            }
            return DecodeSpliced(
                element,
                context,
                static decoder => decoder.ReadDataValue(SpliceFieldName));
        }

        /// <summary>
        /// Copies <paramref name="element"/> into the synthetic
        /// <c>{ "v": &lt;element&gt; }</c> envelope and runs
        /// <paramref name="read"/> against a Stack
        /// <see cref="Ua.JsonDecoder"/> positioned on it.
        /// </summary>
        /// <typeparam name="T">Decoded value type.</typeparam>
        /// <param name="element">Source element.</param>
        /// <param name="context">Stack message context.</param>
        /// <param name="read">Reads the <see cref="SpliceFieldName"/>
        /// property.</param>
        /// <param name="excludedProperty">Optional member of an object
        /// <paramref name="element"/> that is not copied.</param>
        /// <returns>The decoded value.</returns>
        internal static T DecodeSpliced<T>(
            JsonElement element,
            IServiceMessageContext context,
            Func<Ua.JsonDecoder, T> read,
            string? excludedProperty = null)
        {
            using JsonBufferWriter buffer = new(256);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(SpliceFieldName);
                if (excludedProperty is not null && element.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        if (!member.NameEquals(excludedProperty))
                        {
                            member.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    element.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            using Ua.JsonDecoder decoder = new(
                new ReadOnlySequence<byte>(buffer.WrittenMemory),
                context);
            return read(decoder);
        }

        /// <summary>
        /// Whether <paramref name="element"/> is a Part 6 §5.4.2.17
        /// Variant object (it carries <c>UaType</c>).
        /// </summary>
        /// <param name="element">Candidate element.</param>
        /// <returns><see langword="true"/> for a Variant envelope.</returns>
        internal static bool IsVariantEnvelope(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("UaType", out _);
        }
    }
}
