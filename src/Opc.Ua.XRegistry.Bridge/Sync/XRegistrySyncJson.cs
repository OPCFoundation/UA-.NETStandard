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
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    internal static class XRegistrySyncJson
    {
        public static ByteString Encode(JsonNode value, int maximumBytes, int maximumDepth = 64)
        {
            try
            {
                return new XRegistryProtocolCodec(maximumBytes, maximumDepth).EncodeJson(value);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Synchronization JSON exceeds its configured quota.", exception);
            }
        }

        public static JsonDocument Parse(ByteString bytes, int maximumBytes, int maximumDepth = 64)
        {
            if (bytes.IsNull || bytes.Length == 0 || bytes.Length > maximumBytes)
            {
                throw new InvalidDataException("Synchronization JSON is absent, empty, or exceeds its quota.");
            }
            var document = JsonDocument.Parse(bytes.Memory,
                new JsonDocumentOptions { MaxDepth = maximumDepth });
            try
            {
                ValidateUnique(document.RootElement);
                return document;
            }
            catch (JsonException)
            {
                document.Dispose();
                throw;
            }
        }

        public static JsonNode? Canonicalize(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    var result = new JsonObject();
                    foreach (JsonProperty property in value.EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (result.ContainsKey(property.Name))
                        {
                            throw new JsonException("Duplicate JSON property.");
                        }
                        result.Add(property.Name, Canonicalize(property.Value));
                    }
                    return result;
                case JsonValueKind.Array:
                    var array = new JsonArray();
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        array.Add(Canonicalize(item));
                    }
                    return array;
                case JsonValueKind.String:
                    return JsonValue.Create(value.GetString());
                case JsonValueKind.Number:
                    return JsonNode.Parse(NormalizeNumber(value.GetRawText()));
                case JsonValueKind.True:
                    return JsonValue.Create(true);
                case JsonValueKind.False:
                    return JsonValue.Create(false);
                case JsonValueKind.Null:
                    return null;
                default:
                    throw new JsonException("An undefined JSON value cannot be fingerprinted.");
            }
        }

        public static JsonObject Object(JsonElement value)
        {
            return Canonicalize(value) as JsonObject ?? throw new JsonException("Expected a JSON object.");
        }

        public static JsonElement Element(JsonNode node, int maximumBytes = 67_108_864, int maximumDepth = 64)
        {
            using JsonDocument document = Parse(Encode(node, maximumBytes, maximumDepth), maximumBytes, maximumDepth);
            return document.RootElement.Clone();
        }

        public static string Fingerprint(JsonNode node, int maximumBytes = 67_108_864, int maximumDepth = 64)
        {
            JsonElement value = Element(node, maximumBytes, maximumDepth);
            return Hash(Encode(Canonicalize(value)!, maximumBytes, maximumDepth));
        }

        public static string Hash(ByteString bytes)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(bytes.Span);
#else
            using var algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(bytes.ToArray());
#endif
            var result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        public static string String(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String ||
                value.GetString() is not string text)
            {
                throw new JsonException($"Expected string '{name}'.");
            }
            return text;
        }

        public static string? OptionalString(JsonElement element, string name)
        {
            return !element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null
                ? null
                : String(element, name);
        }

        public static int Integer(JsonElement element, string name, int minimum, int maximum)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                !value.TryGetInt32(out int number) ||
                number < minimum ||
                number > maximum)
            {
                throw new JsonException($"Invalid integer '{name}'.");
            }
            return number;
        }

        public static string Epoch(JsonElement metadata)
        {
            if (!metadata.TryGetProperty("epoch", out JsonElement epoch) || epoch.ValueKind != JsonValueKind.Number)
            {
                throw new JsonException("A synchronization observation requires its own unsigned epoch.");
            }
            string value = epoch.GetRawText();
            ValidateEpoch(value);
            return value;
        }

        public static void ValidateEpoch(string value)
        {
            if (value.Length == 0 ||
                (value.Length > 1 && value[0] == '0') ||
                value.Any(c => c is < '0' or > '9'))
            {
                throw new JsonException("An epoch must be an exact, nonnegative integer.");
            }
        }

        private static void ValidateUnique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new JsonException("Duplicate JSON property.");
                    }
                    ValidateUnique(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    ValidateUnique(child);
                }
            }
        }

        private static string NormalizeNumber(string number)
        {
            int exponentIndex = number.IndexOfAny(['e', 'E']);
            string mantissa = exponentIndex < 0 ? number : number[..exponentIndex];
            BigInteger exponent = exponentIndex < 0
                ? BigInteger.Zero
#if NETSTANDARD2_1_OR_GREATER || NET
                : BigInteger.Parse(number.AsSpan(exponentIndex + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
#else
                : BigInteger.Parse(number.Substring(exponentIndex + 1), CultureInfo.InvariantCulture);
#endif
            bool negative = mantissa[0] == '-';
            if (negative)
            {
                mantissa = mantissa[1..];
            }
            int point = mantissa.AsSpan().IndexOf('.');
            if (point >= 0)
            {
                exponent -= mantissa.Length - point - 1;
                mantissa = mantissa.Remove(point, 1);
            }
            mantissa = mantissa.TrimStart('0');
            if (mantissa.Length == 0)
            {
                return "0";
            }
            int length = mantissa.Length;
            mantissa = mantissa.TrimEnd('0');
            exponent += length - mantissa.Length;
            return (negative ? "-" : string.Empty) +
                mantissa +
                (exponent.IsZero ? string.Empty : "e" + exponent.ToString(CultureInfo.InvariantCulture));
        }
    }
}
