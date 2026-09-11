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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Http
{
    internal static class XRegistryHttpHeaders
    {
        public static ArrayOf<KeyValuePair<string, string>> Encode(
            JsonElement metadata,
            XRegistryHttpShape shape,
            bool request)
        {
            var result = new List<KeyValuePair<string, string>>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (metadata.ValueKind == JsonValueKind.Undefined)
            {
                return [];
            }
            if (metadata.ValueKind != JsonValueKind.Object)
            {
                throw Error("Document header metadata must be a JSON object.");
            }
            foreach (JsonProperty property in metadata.EnumerateObject())
            {
                if (property.Name == "contenttype")
                {
                    continue;
                }
                if (property.Name.Contains('.', StringComparison.Ordinal))
                {
                    throw Error("Dotted top-level attribute names require the metadata body representation.");
                }
                if (property.Name == shape.Singular || property.Name == shape.Singular + "base64")
                {
                    if (request)
                    {
                        throw Error("Inline documents cannot be serialized as xRegistry headers.");
                    }
                    continue;
                }
                string type = shape.AttributeType(property.Name);
                if (property.Value.ValueKind == JsonValueKind.Object && type == "map")
                {
                    if (request && !property.Value.EnumerateObject().MoveNext())
                    {
                        throw Error("An empty map requires the metadata body representation.");
                    }
                    foreach (JsonProperty entry in property.Value.EnumerateObject())
                    {
                        Add(result, names, property.Name + "." + entry.Name, entry.Value,
                            shape.AttributeType(property.Name, mapItem: true), request);
                    }
                }
                else
                {
                    Add(result, names, property.Name, property.Value, type, request);
                }
            }
            return [.. result];
        }

        public static JsonElement Decode(
            ArrayOf<KeyValuePair<string, string>> headers,
            string? contentType,
            XRegistryHttpShape shape,
            bool request,
            XRegistryHttpBody codec)
        {
            var metadata = new JsonObject();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (!header.Key.StartsWith("xRegistry-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string name = header.Key["xRegistry-".Length..];
                ValidateName(name);
                if (!seen.Add(name))
                {
                    throw Error("A document attribute header was repeated.");
                }
                int dot = name.IndexOf('.', StringComparison.Ordinal);
                string attribute = (dot < 0 ? name : name[..dot]).ToLowerInvariant();
                if (attribute == "contenttype" ||
                    attribute == shape.Singular ||
                    attribute == shape.Singular + "base64")
                {
                    throw new XRegistryHttpWireException(400, "extra_xregistry_header",
                        "The attribute is not permitted as an xRegistry header.");
                }
                string type = shape.AttributeType(attribute, dot >= 0);
                JsonNode? value = DecodeValue(header.Value, type);
                if (dot < 0)
                {
                    if (metadata.ContainsKey(attribute))
                    {
                        throw Error("A map and a scalar header target the same attribute.");
                    }
                    metadata.Add(attribute, value);
                }
                else
                {
                    if (shape.AttributeType(attribute) != "map")
                    {
                        throw Error("A dotted attribute header requires a model-defined scalar map.");
                    }
                    if (!metadata.TryGetPropertyValue(attribute, out JsonNode? map))
                    {
                        map = new JsonObject();
                        metadata.Add(attribute, map);
                    }
                    if (map is not JsonObject entries)
                    {
                        throw Error("A map and a scalar header target the same attribute.");
                    }
                    entries.Add(name[(dot + 1)..], value);
                }
            }
            if (request || contentType is not null)
            {
                ValidateContentType(contentType);
                metadata["contenttype"] = contentType;
            }
            return codec.Parse(codec.Encode(metadata));
        }

        public static string EncodeValue(string value)
        {
            byte[] bytes;
            try
            {
                bytes = s_utf8.GetBytes(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException("An attribute contains invalid Unicode.", exception);
            }
            var result = new StringBuilder(bytes.Length);
            foreach (byte item in bytes)
            {
                if (item is >= 0x21 and <= 0x7e and not (0x22 or 0x25))
                {
                    result.Append((char)item);
                }
                else
                {
                    result.Append('%')
                        .Append(item.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return result.ToString();
        }

        public static string DecodeValue(string value)
        {
            value = value.Trim(' ', '\t');
            if (value.StartsWith('"'))
            {
                if (value.Length < 2 || !value.EndsWith('"'))
                {
                    throw Error("An attribute contains an unterminated quoted string.");
                }
                var unquoted = new StringBuilder();
                for (int index = 1; index < value.Length - 1; index++)
                {
                    char character = value[index];
                    if (character == '\\')
                    {
                        if (++index >= value.Length - 1)
                        {
                            throw Error("An attribute contains an incomplete quoted escape.");
                        }
                        character = value[index];
                    }
                    else if (character == '"')
                    {
                        throw Error("An attribute contains an unescaped quote.");
                    }
                    unquoted.Append(character);
                }
                value = unquoted.ToString();
            }
            else if (value.Contains('"', StringComparison.Ordinal))
            {
                throw Error("An attribute contains an unescaped quote.");
            }
            foreach (char character in value)
            {
                if (character is < ' ' or > '~')
                {
                    throw Error("An attribute contains an invalid HTTP header character.");
                }
            }
            try
            {
                return XRegistryHttpAddress.DecodePercent(value);
            }
            catch (InvalidDataException exception)
            {
                throw new XRegistryHttpWireException(400, "header_error",
                    "An attribute header contains invalid UTF-8 percent encoding.", exception);
            }
        }

        public static void ValidateContentType(string? contentType)
        {
            if (contentType is not null &&
                (contentType.IndexOfAny(['\r', '\n']) >= 0 || !MediaTypeHeaderValue.TryParse(contentType, out _)))
            {
                throw Error("The Content-Type header is invalid.");
            }
        }

        public static void ValidateBudget(
            ArrayOf<KeyValuePair<string, string>> headers,
            XRegistryHttpOptions options)
        {
            if (headers.Count > options.MaximumHeaders)
            {
                throw new XRegistryHttpWireException(431, "about:blank", "Too many HTTP headers.");
            }
            long bytes = 0;
            foreach (KeyValuePair<string, string> header in headers)
            {
                ValidateName(header.Key);
                if (header.Value.IndexOfAny(['\r', '\n']) >= 0)
                {
                    throw Error("An HTTP header contains a line break.");
                }
                bytes += s_utf8.GetByteCount(header.Key) + s_utf8.GetByteCount(header.Value) + 4L;
                if (bytes > options.MaximumHeaderBytes)
                {
                    throw new XRegistryHttpWireException(431, "about:blank", "HTTP headers exceed their byte limit.");
                }
            }
        }

        private static JsonNode? DecodeValue(string value, string type)
        {
            value = DecodeValue(value);
            if (value == "null")
            {
                return null;
            }
            if (type == "boolean")
            {
                return value switch
                {
                    "true" => JsonValue.Create(true),
                    "false" => JsonValue.Create(false),
                    _ => throw Error("A boolean attribute header must be true, false or null.")
                };
            }
            if (type is "uinteger" or "integer" or "decimal" or "number")
            {
                try
                {
                    using var number = JsonDocument.Parse(value);
                    if (number.RootElement.ValueKind != JsonValueKind.Number)
                    {
                        throw Error("A numeric attribute header must contain a JSON number.");
                    }
                    if (type is "uinteger" or "integer")
                    {
                        int start = type == "integer" && value.StartsWith('-') ? 1 : 0;
                        for (int index = start; index < value.Length; index++)
                        {
                            if (value[index] is < '0' or > '9')
                            {
                                throw Error("An integer attribute header is not a canonical integer.");
                            }
                        }
                    }
                    return JsonNode.Parse(value);
                }
                catch (JsonException exception)
                {
                    throw new XRegistryHttpWireException(400, "header_error",
                        "A numeric attribute header must contain a canonical JSON number.", exception);
                }
            }
            if (type is "map" or "array" or "object")
            {
                throw Error("Complex attributes require the metadata body representation.");
            }
            return JsonValue.Create(value);
        }

        private static void Add(
            List<KeyValuePair<string, string>> result,
            HashSet<string> names,
            string name,
            JsonElement value,
            string type,
            bool request)
        {
            if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                if (request)
                {
                    throw Error("Complex attributes require the metadata body representation.");
                }
                return;
            }
            ValidateName(name);
            if (!names.Add(name))
            {
                throw Error("Attribute names collide under HTTP case-insensitive header comparison.");
            }
            bool compatible = value.ValueKind == JsonValueKind.Null ||
                type switch
                {
                    "uinteger" or "integer" or "decimal" or "number" => value.ValueKind == JsonValueKind.Number,
                    "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "map" or "array" or "object" => false,
                    _ => value.ValueKind == JsonValueKind.String
                };
            if (!compatible)
            {
                throw Error("The attribute JSON type cannot be preserved in its model-defined HTTP header.");
            }
            string text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (value.ValueKind == JsonValueKind.String && text == "null")
            {
                throw Error("The literal string 'null' requires the metadata body representation.");
            }
            string encoded = EncodeValue(text);
            if (type is "uinteger" or "integer")
            {
                _ = DecodeValue(encoded, type);
            }
            result.Add(new KeyValuePair<string, string>("xRegistry-" + name, encoded));
        }

        private static void ValidateName(string name)
        {
            if (name.Length == 0)
            {
                throw Error("An HTTP header requires a name.");
            }
            foreach (char character in name)
            {
                if (!(character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') ||
                    "!#$%&'*+-.^_`|~".Contains(character, StringComparison.Ordinal)))
                {
                    throw Error("An attribute name cannot be represented as an HTTP header token.");
                }
            }
        }

        private static XRegistryHttpWireException Error(string message)
        {
            return new XRegistryHttpWireException(400, "header_error", message);
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
