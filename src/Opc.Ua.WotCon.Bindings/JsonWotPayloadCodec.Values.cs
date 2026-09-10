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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    public sealed partial class JsonWotPayloadCodec
    {
        private static ValueContract ResolveContract(
            JsonElement schema,
            string pointer,
            WotPayloadSchema? captured,
            IServiceMessageContext context,
            TypeInfo fallback = default,
            ExpandedNodeId fallbackId = default,
            bool preferFallback = false)
        {
            TypeInfo type = fallback;
            ExpandedNodeId identity = fallbackId;
            if (captured?.TryGetTypeBinding(pointer, out WotPayloadTypeBinding? binding) == true)
            {
                if (!preferFallback)
                {
                    type = binding.TypeInfo;
                    identity = binding.DataTypeId;
                }
                else if (identity.IsNull)
                {
                    identity = new ExpandedNodeId((uint)fallback.BuiltInType);
                }
            }
            if (ReadSchemaString(schema, "type") == "null")
            {
                type = default;
            }
            else if (type.BuiltInType == BuiltInType.Null && !identity.IsNull &&
                context.Factory.TryGetEncodeableType(identity, out _))
            {
                type = TypeInfo.Create(BuiltInType.ExtensionObject, type.ValueRank);
            }
            else if (type.BuiltInType == BuiltInType.Null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, $"The native payload type at '{pointer}' is unresolved.");
            }
            return new ValueContract(schema, pointer, captured, identity, type);
        }

        private static void WriteTypedValue(
            Utf8JsonWriter writer, Variant value, ValueContract contract, IServiceMessageContext context)
        {
            if (IsAbstractNumeric(contract.TypeInfo.BuiltInType))
            {
                if (!IsNumericSubtype(value.TypeInfo.BuiltInType, contract.TypeInfo.BuiltInType))
                {
                    throw InvalidPayload("The input does not have a permitted numeric subtype.");
                }
                contract = contract with { TypeInfo = value.TypeInfo };
            }
            if (value.TryGetValue(out ExtensionObject extension))
            {
                ValidateStructureIdentity(extension, contract, context);
            }
            else if (value.TryGetValue(out ArrayOf<ExtensionObject> structures))
            {
                foreach (ExtensionObject structure in structures)
                {
                    ValidateStructureIdentity(structure, contract, context);
                }
            }
            Variant checkedValue = WotBindingValueMapper.RequiresContext(value)
                ? WotBindingValueMapper.Translate(value, context, context) : value;
            using var encoder = new JsonEncoder(context, JsonEncoderOptions.RawData);
            encoder.WriteVariantValue("Value", checkedValue);
            using JsonDocument native = JsonDocument.Parse(encoder.CloseAndReturnText());
            using var buffer = new MemoryStream();
            using (var adapted = new Utf8JsonWriter(buffer))
            {
                AdaptValue(adapted, native.RootElement.GetProperty("Value"), contract, context, toNative: false);
            }
            using JsonDocument encoded = JsonDocument.Parse(buffer.ToArray());
            _ = ReadTypedValue(encoded.RootElement, contract, context);
            encoded.RootElement.WriteTo(writer);
        }

        private static void ValidateStructureIdentity(
            ExtensionObject value, ValueContract contract, IServiceMessageContext context)
        {
            if (!value.IsNull && value.TryGetValue(out IEncodeable? body, context) &&
                Portable(body.TypeId, context) != Portable(contract.DataTypeId, context))
            {
                throw InvalidPayload("The structured input does not have the declared DataType.");
            }
        }

        private static Variant ReadTypedValue(
            JsonElement value, ValueContract contract, IServiceMessageContext context)
        {
            ValidateSchema(value, contract.Schema);
            if (IsAbstractNumeric(contract.TypeInfo.BuiltInType))
            {
                contract = NumericWireContract(value, contract);
            }
            if (value.ValueKind == JsonValueKind.Null && contract.TypeInfo.BuiltInType == BuiltInType.Null)
            {
                return Variant.Null;
            }
            bool array = IsArray(contract.TypeInfo.ValueRank, value);
            if (array && contract.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return new Variant(ArrayOf<ExtensionObject>.Null);
                }
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidPayload("An array argument requires a JSON array.");
                }
                var values = new ExtensionObject[value.GetArrayLength()];
                int index = 0;
                ValueContract elementContract = ElementContract(contract, context);
                foreach (JsonElement element in value.EnumerateArray())
                {
                    Variant decoded = ReadTypedValue(element, elementContract, context);
                    if (!decoded.TryGetValue(out values[index]) && !decoded.IsNull)
                    {
                        throw InvalidPayload("A structured array contains a non-structure value.");
                    }
                    index++;
                }
                return new Variant(new ArrayOf<ExtensionObject>(values));
            }
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("Value");
                AdaptValue(writer, value, contract, context, toNative: true);
                writer.WriteEndObject();
            }
            using var decoder = new JsonDecoder(Encoding.UTF8.GetString(buffer.ToArray()), context,
                new JsonDecoderOptions { ParseStrict = true, UpdateNamespaceTable = true });
            if (!array && contract.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return new Variant(ExtensionObject.Null);
                }
                IEncodeable result = decoder.ReadEncodeable<IEncodeable>("Value", contract.DataTypeId);
                Variant decoded = new(new ExtensionObject(result));
                return WotBindingValueMapper.Translate(decoded, context, context);
            }
            TypeInfo type = TypeInfo.Create(
                contract.TypeInfo.BuiltInType, array ? Math.Max(1, contract.TypeInfo.ValueRank) : ValueRanks.Scalar);
            return decoder.ReadVariantValue("Value", type);
        }

        private static void AdaptValue(
            Utf8JsonWriter writer,
            JsonElement value,
            ValueContract contract,
            IServiceMessageContext context,
            bool toNative)
        {
            if (toNative)
            {
                ValidateSchema(value, contract.Schema);
            }
            if (value.ValueKind == JsonValueKind.Null)
            {
                writer.WriteNullValue();
                return;
            }
            if (IsArray(contract.TypeInfo.ValueRank, value))
            {
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidPayload("The declared array requires a JSON array.");
                }
                if (context.MaxArrayLength > 0 && value.GetArrayLength() > context.MaxArrayLength)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded, "The array limit was exceeded.");
                }
                writer.WriteStartArray();
                ValueContract elementContract = ElementContract(contract, context);
                foreach (JsonElement element in value.EnumerateArray())
                {
                    AdaptValue(writer, element, elementContract, context, toNative);
                }
                writer.WriteEndArray();
                return;
            }
            if (contract.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                AdaptStructure(writer, value, contract, context, toNative);
                return;
            }
            if (toNative)
            {
                // Core accepts numeric strings, but only after the TD schema has checked
                // the wire kind. Normalizing an integral exponent also preserves integer TD values.
                if (IsInteger(contract.TypeInfo.BuiltInType) && value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDecimal(out decimal integer) && decimal.Truncate(integer) == integer)
                {
                    if (contract.TypeInfo.BuiltInType is BuiltInType.Int64 or BuiltInType.UInt64)
                    {
                        writer.WriteStringValue(integer.ToString("0", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.WriteNumberValue(checked((long)integer));
                    }
                    return;
                }
                value.WriteTo(writer);
                return;
            }
            string? jsonType = ReadSchemaString(contract.Schema, "type");
            if (contract.TypeInfo.BuiltInType == BuiltInType.LocalizedText &&
                jsonType is null or "string" && value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStringValue(value.TryGetProperty("Text", out JsonElement text) ? text.GetString() : null);
                return;
            }
            if (value.ValueKind == JsonValueKind.String && jsonType != "string" &&
                contract.TypeInfo.BuiltInType is >= BuiltInType.SByte and <= BuiltInType.Double)
            {
                string text = value.GetString()!;
                if (contract.TypeInfo.BuiltInType == BuiltInType.UInt64)
                {
                    writer.WriteNumberValue(ulong.Parse(text, CultureInfo.InvariantCulture));
                }
                else if (IsInteger(contract.TypeInfo.BuiltInType))
                {
                    writer.WriteNumberValue(long.Parse(text, CultureInfo.InvariantCulture));
                }
                else
                {
                    double number = double.Parse(text, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw InvalidPayload("A JSON number must be finite.");
                    }
                    writer.WriteNumberValue(number);
                }
                return;
            }
            value.WriteTo(writer);
        }

        private static void AdaptStructure(
            Utf8JsonWriter writer,
            JsonElement value,
            ValueContract contract,
            IServiceMessageContext context,
            bool toNative)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw InvalidPayload("The declared Structure requires a JSON object, not an encoded string.");
            }
            EnsureUniqueMembers(value);
            if (!context.Factory.TryGetEncodeableType(contract.DataTypeId, out IEncodeableType? activator) ||
                activator.CreateInstance() is not IStructure structure ||
                activator is not IDataTypeDefinitionSource definitionSource ||
                definitionSource.GetDataTypeDefinition(context.NamespaceUris) is not StructureDefinition definition)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"The declared Structure '{contract.DataTypeId}' requires a registered field-aware factory.");
            }
            var fields = new Dictionary<string, IStructureField>(StringComparer.Ordinal);
            foreach (IStructureField field in structure.GetFields())
            {
                if (field.Name is not { Length: > 0 } name)
                {
                    throw InvalidPayload("The registered Structure has an unnamed field.");
                }
                fields.Add(name, field);
            }
            int present = 0;
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!toNative && property.Name == "UaTypeId")
                {
                    if (!ExpandedNodeId.TryParse(property.Value.GetString()!, out ExpandedNodeId typeId) ||
                        Portable(typeId, context) != Portable(contract.DataTypeId, context))
                    {
                        throw InvalidPayload("A nested Structure does not have its declared DataType.");
                    }
                    continue;
                }
                if (!fields.ContainsKey(property.Name))
                {
                    throw InvalidPayload($"The Structure contains undeclared field '{property.Name}'.");
                }
                present++;
            }
            bool union = definition.StructureType is StructureType.Union or StructureType.UnionWithSubtypedValues;
            if (union && present > 1)
            {
                throw InvalidPayload("A Union payload selects more than one field.");
            }
            writer.WriteStartObject();
            if (toNative)
            {
                writer.WriteString("UaTypeId", Portable(contract.DataTypeId, context).ToString());
            }
            foreach (StructureField field in definition.Fields)
            {
                string name = field.Name ?? throw InvalidPayload("The registered Structure has an unnamed field.");
                if (!value.TryGetProperty(name, out JsonElement member))
                {
                    if (!field.IsOptional && !union)
                    {
                        throw InvalidPayload($"The Structure omitted mandatory field '{name}'.");
                    }
                    continue;
                }
                JsonElement schema = default;
                if (contract.Schema.ValueKind == JsonValueKind.Object &&
                    contract.Schema.TryGetProperty("properties", out JsonElement properties))
                {
                    properties.TryGetProperty(name, out schema);
                }
                if (!fields.TryGetValue(name, out IStructureField? nativeField))
                {
                    throw InvalidPayload($"The registered Structure has no field metadata for '{name}'.");
                }
                ValueContract child = ResolveContract(
                    schema, contract.Pointer + "/properties/" + WotAffordanceForm.EscapePointerToken(name),
                    contract.Captured, context, nativeField.TypeInfo,
                    NodeId.ToExpandedNodeId(field.DataType, context.NamespaceUris), preferFallback: true);
                writer.WritePropertyName(name);
                AdaptValue(writer, member, child, context, toNative);
            }
            writer.WriteEndObject();
        }

        private static ValueContract ElementContract(ValueContract parent, IServiceMessageContext context)
        {
            JsonElement schema = default;
            if (parent.Schema.ValueKind == JsonValueKind.Object)
            {
                parent.Schema.TryGetProperty("items", out schema);
            }
            int rank = parent.TypeInfo.ValueRank > 1 ? parent.TypeInfo.ValueRank - 1 : ValueRanks.Scalar;
            return ResolveContract(
                schema, parent.Pointer + "/items", parent.Captured, context,
                TypeInfo.Create(parent.TypeInfo.BuiltInType, rank), parent.DataTypeId, preferFallback: true);
        }

        private static void ValidateSchema(JsonElement value, JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            if (schema.TryGetProperty("type", out JsonElement type))
            {
                bool matches = type.ValueKind == JsonValueKind.String && MatchesType(value, type.GetString());
                if (type.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement choice in type.EnumerateArray())
                    {
                        matches |= choice.ValueKind == JsonValueKind.String && MatchesType(value, choice.GetString());
                    }
                }
                if (!matches)
                {
                    throw InvalidPayload("The JSON value has the wrong kind for its DataSchema.");
                }
            }
            if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(value, constant))
            {
                throw InvalidPayload("The value does not match the schema's constant.");
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
            {
                if ((schema.TryGetProperty("minimum", out JsonElement minimum) &&
                        minimum.TryGetDecimal(out decimal lower) && number < lower) ||
                    (schema.TryGetProperty("maximum", out JsonElement maximum) &&
                        maximum.TryGetDecimal(out decimal upper) && number > upper))
                {
                    throw InvalidPayload("The number is outside its DataSchema bounds.");
                }
            }
            if (value.ValueKind == JsonValueKind.Object &&
                schema.TryGetProperty("required", out JsonElement required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement name in required.EnumerateArray())
                {
                    if (name.ValueKind != JsonValueKind.String || !value.TryGetProperty(name.GetString()!, out _))
                    {
                        throw InvalidPayload("The value omitted a required DataSchema member.");
                    }
                }
            }
        }

        private static bool MatchesType(JsonElement value, string? type)
        {
            return type switch
            {
                "null" => value.ValueKind == JsonValueKind.Null,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind == JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDecimal(out decimal integer) && decimal.Truncate(integer) == integer,
                "array" => value.ValueKind == JsonValueKind.Array,
                "object" => value.ValueKind == JsonValueKind.Object,
                _ => false
            };
        }

        private static string? ReadSchemaString(JsonElement schema, string name)
        {
            return schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static bool IsInteger(BuiltInType type)
        {
            return type is >= BuiltInType.SByte and <= BuiltInType.UInt64 or BuiltInType.Enumeration;
        }

        private static bool IsAbstractNumeric(BuiltInType type)
        {
            return type is BuiltInType.Number or BuiltInType.Integer or BuiltInType.UInteger;
        }

        private static bool IsNumericSubtype(BuiltInType concrete, BuiltInType declared)
        {
            return declared switch
            {
                BuiltInType.Integer => concrete is BuiltInType.SByte or BuiltInType.Int16 or
                    BuiltInType.Int32 or BuiltInType.Int64,
                BuiltInType.UInteger => concrete is BuiltInType.Byte or BuiltInType.UInt16 or
                    BuiltInType.UInt32 or BuiltInType.UInt64,
                _ => concrete is >= BuiltInType.SByte and <= BuiltInType.Double
            };
        }

        private static ValueContract NumericWireContract(JsonElement value, ValueContract contract)
        {
            BuiltInType concrete = contract.TypeInfo.BuiltInType switch
            {
                BuiltInType.Integer => BuiltInType.Int64,
                BuiltInType.UInteger => BuiltInType.UInt64,
                _ => SelectNumberType(value)
            };
            return contract with { TypeInfo = TypeInfo.Create(concrete, contract.TypeInfo.ValueRank) };

            static BuiltInType SelectNumberType(JsonElement number)
            {
                if (number.ValueKind == JsonValueKind.Array)
                {
                    BuiltInType type = BuiltInType.Int64;
                    foreach (JsonElement element in number.EnumerateArray())
                    {
                        BuiltInType item = SelectNumberType(element);
                        if (item == BuiltInType.Double)
                        {
                            type = item;
                        }
                        else if (item == BuiltInType.UInt64 && type != BuiltInType.Double)
                        {
                            type = item;
                        }
                    }
                    return type;
                }
                if (number.ValueKind != JsonValueKind.Number)
                {
                    throw InvalidPayload("An abstract numeric value requires a JSON number.");
                }
                return number.TryGetInt64(out _) ? BuiltInType.Int64 :
                    number.TryGetUInt64(out _) ? BuiltInType.UInt64 : BuiltInType.Double;
            }
        }

        private static bool IsArray(int rank, JsonElement value)
        {
            return rank >= 0 || (rank is ValueRanks.Any or ValueRanks.ScalarOrOneDimension &&
                value.ValueKind == JsonValueKind.Array);
        }

        private static ExpandedNodeId Portable(ExpandedNodeId identity, IServiceMessageContext context)
        {
            return identity.IsAbsolute
                ? identity : NodeId.ToExpandedNodeId(identity.InnerNodeId, context.NamespaceUris);
        }

        private readonly record struct ValueContract(
            JsonElement Schema,
            string Pointer,
            WotPayloadSchema? Captured,
            ExpandedNodeId DataTypeId,
            TypeInfo TypeInfo);
    }
}
