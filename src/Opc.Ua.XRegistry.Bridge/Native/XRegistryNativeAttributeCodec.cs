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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    /// <summary>
    /// Lossless, model-validated native attribute conversion. Canonical strings are not HTTP headers;
    /// typed complex values require declared native arrays or individually mapped object/map leaves.
    /// </summary>
    public static partial class XRegistryNativeAttributeCodec
    {
        /// <summary>
        /// Decodes a present native value according to its model. BadNoData must be handled as absence by the caller.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static JsonElement Decode(
            in Variant value, JsonElement definition, XRegistryNativeAttributeEncoding encoding,
            BuiltInType nativeType = BuiltInType.Null)
        {
            JsonNode? node;
            if (encoding == XRegistryNativeAttributeEncoding.CanonicalString)
            {
                if (!value.TryGetValue(out string text))
                {
                    throw new InvalidDataException("Canonical native attributes must be OPC UA Strings.");
                }
                node = IsText(Type(definition)) ? JsonValue.Create(text) : JsonNode.Parse(text);
            }
            else if (encoding == XRegistryNativeAttributeEncoding.Typed)
            {
                if (!value.IsNull &&
                    (value.TypeInfo.BuiltInType is < BuiltInType.Boolean or > BuiltInType.DateTime ||
                        (Type(definition) == "array" && value.TypeInfo.ValueRank != ValueRanks.OneDimension) ||
                        (Type(definition) != "array" && value.TypeInfo.ValueRank != ValueRanks.Scalar)))
                {
                    throw new InvalidDataException("The typed native attribute has an unsupported data type or rank.");
                }
                if (!value.IsNull && nativeType != BuiltInType.Null && value.TypeInfo.BuiltInType != nativeType)
                {
                    throw new InvalidDataException("The native value does not match the configured built-in type.");
                }
                node = ToNode(value);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            using var document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
            JsonElement result = document.RootElement.Clone();
            XRegistryAttributeModel.Validate(result, definition);
            return result;
        }

        /// <summary>
        /// Encodes a logical value without boxing or numeric narrowing.
        /// Unsupported values are rejected before publication.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static Variant Encode(
            JsonElement value, JsonElement definition, XRegistryNativeAttributeEncoding encoding,
            BuiltInType nativeType = BuiltInType.Null)
        {
            XRegistryAttributeModel.Validate(value, definition);
            if (encoding == XRegistryNativeAttributeEncoding.CanonicalString)
            {
                if (nativeType is not (BuiltInType.Null or BuiltInType.String))
                {
                    throw new InvalidDataException("Canonical attributes require native String properties.");
                }
                return Variant.From(IsText(Type(definition)) ? value.GetString()! : value.GetRawText());
            }
            if (encoding != XRegistryNativeAttributeEncoding.Typed)
            {
                throw new ArgumentOutOfRangeException(nameof(encoding));
            }
            if (nativeType is < BuiltInType.Null or > BuiltInType.DateTime)
            {
                throw new InvalidDataException("This native type requires an explicit registered structure mapping.");
            }
            if (value.ValueKind == JsonValueKind.Null)
            {
                return Variant.Null;
            }
            BuiltInType type = DataType(definition, encoding, nativeType);
            bool array = Type(definition) == "array";
            try
            {
                if (array)
                {
                    return type switch
                    {
                        BuiltInType.Boolean => new Variant(Array(value, static item => item.GetBoolean())),
                        BuiltInType.SByte => new Variant(Array(value, static item => item.GetSByte())),
                        BuiltInType.Byte => new Variant(Array(value, static item => item.GetByte())),
                        BuiltInType.Int16 => new Variant(Array(value, static item => item.GetInt16())),
                        BuiltInType.UInt16 => new Variant(Array(value, static item => item.GetUInt16())),
                        BuiltInType.Int32 => new Variant(Array(value, static item => item.GetInt32())),
                        BuiltInType.UInt32 => new Variant(Array(value, static item => item.GetUInt32())),
                        BuiltInType.Int64 => new Variant(Array(value, static item => item.GetInt64())),
                        BuiltInType.UInt64 => new Variant(Array(value, static item => item.GetUInt64())),
                        BuiltInType.Float => new Variant(Array(value, Float)),
                        BuiltInType.Double => new Variant(Array(value, Double)),
                        BuiltInType.String => new Variant(Array(value, static item => item.GetString()!)),
                        BuiltInType.DateTime => new Variant(Array(value, Timestamp)),
                        _ => throw new InvalidDataException(
                            "The configured native array type is not a supported attribute type.")
                    };
                }
                return type switch
                {
                    BuiltInType.Boolean => Variant.From(value.GetBoolean()),
                    BuiltInType.SByte => Variant.From(value.GetSByte()),
                    BuiltInType.Byte => Variant.From(value.GetByte()),
                    BuiltInType.Int16 => Variant.From(value.GetInt16()),
                    BuiltInType.UInt16 => Variant.From(value.GetUInt16()),
                    BuiltInType.Int32 => Variant.From(value.GetInt32()),
                    BuiltInType.UInt32 => Variant.From(value.GetUInt32()),
                    BuiltInType.Int64 => Variant.From(value.GetInt64()),
                    BuiltInType.UInt64 => Variant.From(value.GetUInt64()),
                    BuiltInType.Float => Variant.From(Float(value)),
                    BuiltInType.Double => Variant.From(Double(value)),
                    BuiltInType.String => Variant.From(value.GetString()!),
                    BuiltInType.DateTime => Variant.From(Timestamp(value)),
                    _ => throw new InvalidDataException(
                        "Use canonical JSON or typed leaf mappings for this logical value.")
                };
            }
            catch (Exception exception) when (
                exception is FormatException or OverflowException or InvalidOperationException)
            {
                throw new InvalidDataException(
                    "The logical value cannot be represented by its configured native type.", exception);
            }
        }

        /// <summary>
        /// Returns the native element type for a projected property, without creating or changing nodes.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        public static BuiltInType DataType(
            JsonElement definition,
            XRegistryNativeAttributeEncoding encoding,
            BuiltInType nativeType = BuiltInType.Null)
        {
            if (encoding == XRegistryNativeAttributeEncoding.CanonicalString)
            {
                return BuiltInType.String;
            }
            if (nativeType != BuiltInType.Null)
            {
                return nativeType;
            }
            string type = Type(definition);
            if (type == "array")
            {
                definition = definition.GetProperty("item");
                type = Type(definition);
            }
            return type switch
            {
                "boolean" => BuiltInType.Boolean,
                "integer" => BuiltInType.Int64,
                "uinteger" => BuiltInType.UInt64,
                "decimal" => BuiltInType.Double,
                "timestamp" => BuiltInType.DateTime,
                _ when IsText(type) => BuiltInType.String,
                _ => throw new InvalidDataException(
                    "A compound attribute needs canonical String or typed leaf mappings.")
            };
        }

        private static JsonNode? ToNode(in Variant value)
        {
            if (value.IsNull)
            {
                return null;
            }
            if (value.TypeInfo.ValueRank != ValueRanks.Scalar)
            {
                return ToArrayNode(value);
            }
            if (value.TryGetValue(out bool boolean))
            {
                return JsonValue.Create(boolean);
            }
            if (value.TryGetValue(out sbyte signedByte))
            {
                return JsonValue.Create(signedByte);
            }
            if (value.TryGetValue(out byte unsignedByte))
            {
                return JsonValue.Create(unsignedByte);
            }
            if (value.TryGetValue(out short signedShort))
            {
                return JsonValue.Create(signedShort);
            }
            if (value.TryGetValue(out ushort unsignedShort))
            {
                return JsonValue.Create(unsignedShort);
            }
            if (value.TryGetValue(out int integer))
            {
                return JsonValue.Create(integer);
            }
            if (value.TryGetValue(out uint unsigned))
            {
                return JsonValue.Create(unsigned);
            }
            if (value.TryGetValue(out long longValue))
            {
                return JsonValue.Create(longValue);
            }
            if (value.TryGetValue(out ulong unsignedLong))
            {
                return JsonValue.Create(unsignedLong);
            }
            if (value.TryGetValue(out float floatValue))
            {
                return Finite(floatValue);
            }
            if (value.TryGetValue(out double doubleValue))
            {
                return Finite(doubleValue);
            }
            if (value.TryGetValue(out string text))
            {
                return JsonValue.Create(text);
            }
            if (value.TryGetValue(out DateTimeUtc timestamp))
            {
                return JsonValue.Create(timestamp.ToString("O", CultureInfo.InvariantCulture));
            }
            throw new InvalidDataException("The native attribute contains an unsupported scalar value.");
        }

        private static JsonArray ToArrayNode(in Variant value)
        {
            if (value.TryGetValue(out ArrayOf<bool> booleans))
            {
                return ArrayNode(booleans, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<sbyte> signedBytes))
            {
                return ArrayNode(signedBytes, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<byte> unsignedBytes))
            {
                return ArrayNode(unsignedBytes, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<short> shorts))
            {
                return ArrayNode(shorts, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<ushort> unsignedShorts))
            {
                return ArrayNode(unsignedShorts, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<int> integers))
            {
                return ArrayNode(integers, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<uint> unsignedIntegers))
            {
                return ArrayNode(unsignedIntegers, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<long> longs))
            {
                return ArrayNode(longs, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<ulong> unsignedLongs))
            {
                return ArrayNode(unsignedLongs, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<float> floats))
            {
                return ArrayNode(floats, Finite);
            }
            if (value.TryGetValue(out ArrayOf<double> doubles))
            {
                return ArrayNode(doubles, Finite);
            }
            if (value.TryGetValue(out ArrayOf<string> strings))
            {
                return ArrayNode(strings, static item => JsonValue.Create(item));
            }
            if (value.TryGetValue(out ArrayOf<DateTimeUtc> timestamps))
            {
                return ArrayNode(
                    timestamps, static item => JsonValue.Create(item.ToString("O", CultureInfo.InvariantCulture)));
            }
            throw new InvalidDataException("The native attribute contains an unsupported or non-finite value.");
        }

        private static string Type(JsonElement definition)
        {
            return definition.GetProperty("type").GetString()
                ?? throw new InvalidDataException("The attribute model omits its type.");
        }

        private static bool IsText(string type)
        {
            return type is "string" or "timestamp" or "uri" or "url" or "uriabsolute" or "urlabsolute" or
                "urirelative" or "urlrelative" or "uritemplate" or "xid" or "xidtype";
        }

        private static ArrayOf<T> Array<T>(JsonElement value, Func<JsonElement, T> convert)
        {
            return new ArrayOf<T>(value.EnumerateArray().Select(convert).ToArray());
        }

        private static JsonArray ArrayNode<T>(ArrayOf<T> values, Func<T, JsonNode?> convert)
        {
            if (values.IsNull)
            {
                throw new InvalidDataException("A null native array is not an empty logical array.");
            }
            return new JsonArray([.. values.ToList().Select(convert)]);
        }

        private static float Float(JsonElement value)
        {
            float number = value.GetSingle();
            if (float.IsInfinity(number) ||
                float.IsNaN(number) ||
                !XRegistryAttributeModel.NumbersEqual(
                    value.GetRawText(), number.ToString("R", CultureInfo.InvariantCulture)))
            {
                throw new InvalidDataException("The logical number loses precision as a native Float.");
            }
            return number;
        }

        private static JsonNode Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidDataException("A non-finite Float is not a logical registry number.");
            }
            return JsonValue.Create(value)!;
        }

        private static JsonNode Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidDataException("A non-finite Double is not a logical registry number.");
            }
            return JsonValue.Create(value)!;
        }

        private static double Double(JsonElement value)
        {
            double number = value.GetDouble();
            if (double.IsInfinity(number) ||
                double.IsNaN(number) ||
                !XRegistryAttributeModel.NumbersEqual(
                    value.GetRawText(), number.ToString("R", CultureInfo.InvariantCulture)))
            {
                throw new InvalidDataException("The logical number loses precision as a native Double.");
            }
            return number;
        }

        private static DateTimeUtc Timestamp(JsonElement value)
        {
            return (DateTimeUtc)DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime;
        }
    }
}
