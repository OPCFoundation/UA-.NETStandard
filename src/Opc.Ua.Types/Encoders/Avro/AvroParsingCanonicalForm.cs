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
using System.Text;
using System.Text.Json;

namespace Opc.Ua
{
    /// <summary>
    /// Computes the Avro <b>Parsing Canonical Form</b> (PCF) of a schema document, per the Apache
    /// Avro specification. The PCF is the canonical string a spec-conformant Avro SchemaId is the
    /// <c>CRC-64-AVRO</c> Rabin fingerprint of (Part 6 §6.6): so
    /// <c>SchemaId.RabinCrc64Avro(UTF8(AvroParsingCanonicalForm.Compute(schemaJson)))</c> yields a
    /// SchemaId that is byte-identical across implementations for the same logical schema. The
    /// transform applies the PCF rules: PRIMITIVES (fold <c>{"type":"int"}</c> to <c>"int"</c>),
    /// FULLNAMES (fold namespaces into names and resolve references to fullnames), STRIP (keep only
    /// <c>type</c>/<c>name</c>/<c>fields</c>/<c>symbols</c>/<c>items</c>/<c>values</c>/<c>size</c>),
    /// ORDER (name, type, fields, symbols, items, values, size), STRINGS/INTEGERS/WHITESPACE.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public static class AvroParsingCanonicalForm
    {
        private static readonly HashSet<string> s_primitives = new(StringComparer.Ordinal)
        {
            "null", "boolean", "int", "long", "float", "double", "bytes", "string"
        };

        /// <summary>
        /// Computes the Avro Parsing Canonical Form of a schema JSON document.
        /// </summary>
        /// <param name="schemaJson">A valid Avro schema JSON document.</param>
        /// <returns>The Parsing Canonical Form string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="schemaJson"/> is null.</exception>
        /// <exception cref="FormatException">The document is not a valid Avro schema.</exception>
        public static string Compute(string schemaJson)
        {
            if (schemaJson is null)
            {
                throw new ArgumentNullException(nameof(schemaJson));
            }

            using var document = JsonDocument.Parse(schemaJson);
            var builder = new StringBuilder();
            Emit(builder, document.RootElement, null, new HashSet<string>(StringComparer.Ordinal));
            return builder.ToString();
        }

        private static void Emit(
            StringBuilder builder, JsonElement node, string? enclosingNamespace, HashSet<string> named)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.String:
                    EmitTypeName(builder, node.GetString()!, enclosingNamespace, named);
                    break;
                case JsonValueKind.Array: // union
                    builder.Append('[');
                    bool firstBranch = true;
                    foreach (JsonElement branch in node.EnumerateArray())
                    {
                        if (!firstBranch)
                        {
                            builder.Append(',');
                        }
                        firstBranch = false;
                        Emit(builder, branch, enclosingNamespace, named);
                    }
                    builder.Append(']');
                    break;
                case JsonValueKind.Object:
                    EmitObject(builder, node, enclosingNamespace, named);
                    break;
                default:
                    throw new FormatException("Invalid Avro schema node.");
            }
        }

        private static void EmitTypeName(
            StringBuilder builder, string name, string? enclosingNamespace, HashSet<string> named)
        {
            if (s_primitives.Contains(name))
            {
                AppendString(builder, name);
                return;
            }

            AppendString(builder, ResolveFullName(name, enclosingNamespace, named));
        }

        private static void EmitObject(
            StringBuilder builder, JsonElement node, string? enclosingNamespace, HashSet<string> named)
        {
            if (!node.TryGetProperty("type", out JsonElement typeElement))
            {
                throw new FormatException("Avro schema object is missing its 'type'.");
            }

            if (typeElement.ValueKind != JsonValueKind.String)
            {
                // The type is itself a complex schema (a nested object or a union).
                Emit(builder, typeElement, enclosingNamespace, named);
                return;
            }

            string type = typeElement.GetString()!;
            switch (type)
            {
                case "record":
                case "error":
                    EmitNamed(builder, node, "record", enclosingNamespace, named, EmitFields);
                    break;
                case "enum":
                    EmitNamed(builder, node, "enum", enclosingNamespace, named, EmitSymbols);
                    break;
                case "fixed":
                    EmitNamed(builder, node, "fixed", enclosingNamespace, named, EmitSize);
                    break;
                case "array":
                    builder.Append("{\"type\":\"array\",\"items\":");
                    Emit(builder, node.GetProperty("items"), enclosingNamespace, named);
                    builder.Append('}');
                    break;
                case "map":
                    builder.Append("{\"type\":\"map\",\"values\":");
                    Emit(builder, node.GetProperty("values"), enclosingNamespace, named);
                    builder.Append('}');
                    break;
                default:
                    // A primitive expressed as an object: {"type":"int", ...} -> "int".
                    EmitTypeName(builder, type, enclosingNamespace, named);
                    break;
            }
        }

        private static void EmitNamed(
            StringBuilder builder,
            JsonElement node,
            string kind,
            string? enclosingNamespace,
            HashSet<string> named,
            Action<StringBuilder, JsonElement, string?, HashSet<string>> emitExtra)
        {
            string name = node.GetProperty("name").GetString()!;
            string? typeNamespace;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                typeNamespace = name.Substring(0, dot);
                name = name.Substring(dot + 1);
            }
            else if (node.TryGetProperty("namespace", out JsonElement nsElement)
                && nsElement.ValueKind == JsonValueKind.String)
            {
                typeNamespace = nsElement.GetString();
            }
            else
            {
                typeNamespace = enclosingNamespace;
            }

            string fullName = string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
            named.Add(fullName);

            builder.Append("{\"name\":");
            AppendString(builder, fullName);
            builder.Append(",\"type\":");
            AppendString(builder, kind);
            emitExtra(builder, node, typeNamespace, named);
            builder.Append('}');
        }

        private static void EmitFields(
            StringBuilder builder, JsonElement node, string? childNamespace, HashSet<string> named)
        {
            builder.Append(",\"fields\":[");
            bool first = true;
            if (node.TryGetProperty("fields", out JsonElement fields))
            {
                foreach (JsonElement field in fields.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    builder.Append("{\"name\":");
                    AppendString(builder, field.GetProperty("name").GetString()!);
                    builder.Append(",\"type\":");
                    Emit(builder, field.GetProperty("type"), childNamespace, named);
                    builder.Append('}');
                }
            }
            builder.Append(']');
        }

        private static void EmitSymbols(
            StringBuilder builder, JsonElement node, string? childNamespace, HashSet<string> named)
        {
            builder.Append(",\"symbols\":[");
            bool first = true;
            foreach (JsonElement symbol in node.GetProperty("symbols").EnumerateArray())
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                AppendString(builder, symbol.GetString()!);
            }
            builder.Append(']');
        }

        private static void EmitSize(
            StringBuilder builder, JsonElement node, string? childNamespace, HashSet<string> named)
        {
            builder.Append(",\"size\":");
            builder.Append(node.GetProperty("size").GetInt64().ToString(CultureInfo.InvariantCulture));
        }

        private static string ResolveFullName(string name, string? enclosingNamespace, HashSet<string> named)
        {
            if (name.LastIndexOf('.') >= 0)
            {
                return name;
            }
            if (!string.IsNullOrEmpty(enclosingNamespace))
            {
                string candidate = enclosingNamespace + "." + name;
                if (named.Contains(candidate))
                {
                    return candidate;
                }
            }
            if (named.Contains(name))
            {
                return name;
            }
            return string.IsNullOrEmpty(enclosingNamespace) ? name : enclosingNamespace + "." + name;
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
