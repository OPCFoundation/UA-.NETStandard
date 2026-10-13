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
using System.Text;
using System.Text.Json;
using Opc.Ua.Client.FileSystem;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounds native job payloads and namespace-qualified remote paths before any OPC UA request.
    /// </summary>
    internal static class MachineToolServiceParameters
    {
        /// <summary>
        /// Rejects unknown or incorrectly cased selectors instead of choosing an operation implicitly.
        /// </summary>
        internal static void Selector(string value, ArrayOf<string> choices)
        {
            foreach (string choice in choices)
            {
                if (string.Equals(value, choice, StringComparison.Ordinal))
                {
                    return;
                }
            }
            throw new ArgumentException("An exact, documented operation or root selector is required.", nameof(value));
        }

        /// <summary>
        /// Distinguishes an omitted optional native value from a supplied value.
        /// </summary>
        internal static bool Present(JsonElement value)
        {
            return value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        }

        /// <summary>
        /// Checks the query's discriminated input before resolving a named session.
        /// </summary>
        internal static void Query(string query, string? jobOrderId, JsonElement states)
        {
            Selector(query, ["Orders", "ResponseById", "ResponsesByState"]);
            if (query == "ResponseById")
            {
                MachineToolParameters.Text(jobOrderId!, 1024);
            }
            else if (jobOrderId != null)
            {
                throw new ArgumentException("jobOrderId is only valid for ResponseById.", nameof(jobOrderId));
            }
            if (query == "ResponsesByState")
            {
                NativeArray(states, 1, 16);
                foreach (JsonElement state in states.EnumerateArray())
                {
                    if (state.ValueKind != JsonValueKind.Object ||
                        !state.TryGetProperty("StateNumber", out JsonElement number) ||
                        number.ValueKind != JsonValueKind.Number ||
                        !number.TryGetUInt32(out _))
                    {
                        throw new ArgumentException("Each native state requires a UInt32 StateNumber.", nameof(states));
                    }
                }
            }
            else if (Present(states))
            {
                throw new ArgumentException("states is only valid for ResponsesByState.", nameof(states));
            }
        }

        /// <summary>
        /// Checks all command selectors and payload bounds before consent is allowed to reach the server.
        /// </summary>
        internal static void Job(string operation, string? jobOrderId, JsonElement jobOrder, JsonElement comments)
        {
            Selector(operation,
                ["Store", "StoreAndStart", "Start", "Stop", "Abort", "Pause", "Resume", "Clear", "Cancel",
                    "Update", "RevokeStart"]);
            bool download = operation is "Store" or "StoreAndStart" or "Update";
            if (download)
            {
                if (jobOrderId != null || jobOrder.ValueKind != JsonValueKind.Object ||
                    !jobOrder.TryGetProperty("JobOrderID", out JsonElement id) || id.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException(
                        "Store, StoreAndStart and Update require native jobOrder with JobOrderID, not jobOrderId.");
                }
                MachineToolParameters.Text(id.GetString()!, 1024);
                Native(jobOrder);
                if (jobOrder.TryGetProperty("EncodingMask", out JsonElement mask) &&
                    (mask.ValueKind != JsonValueKind.Number || !mask.TryGetUInt32(out uint bits) || bits > 1023))
                {
                    throw new ArgumentException("EncodingMask must use only the ten standard optional-field bits.");
                }
            }
            else
            {
                MachineToolParameters.Text(jobOrderId!, 1024);
                if (Present(jobOrder))
                {
                    throw new ArgumentException("This operation takes jobOrderId, not jobOrder.", nameof(jobOrder));
                }
            }
            if (Present(comments))
            {
                NativeArray(comments, 0, 8);
            }
        }

        /// <summary>
        /// Uses the stack's native codec, including optional encoding masks, variants and nested arrays.
        /// </summary>
        internal static V2.ISA95JobOrderDataType DecodeOrder(JsonElement value, IServiceMessageContext context)
        {
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", context);
            return decoder.ReadEncodeable<V2.ISA95JobOrderDataType>("value");
        }

        /// <summary>
        /// Preserves the complete standard state path rather than inventing an MCP state enumeration.
        /// </summary>
        internal static ArrayOf<V2.ISA95StateDataType> DecodeStates(JsonElement value, IServiceMessageContext context)
        {
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", context);
            return decoder.ReadEncodeableArray<V2.ISA95StateDataType>("value");
        }

        /// <summary>
        /// Decodes standard multilingual comments, with an omitted collection meaning no comments.
        /// </summary>
        internal static ArrayOf<LocalizedText> DecodeComments(JsonElement value, IServiceMessageContext context)
        {
            if (!Present(value))
            {
                return [];
            }
            using var decoder = new JsonDecoder("{\"value\":" + value.GetRawText() + "}", context);
            ArrayOf<LocalizedText> comments = decoder.ReadLocalizedTextArray("value");
            foreach (LocalizedText comment in comments)
            {
                if (comment.Text?.Length > 1024 || comment.Locale?.Length > 64)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Comment text/locale exceeds 1024/64 chars.");
                }
            }
            return comments;
        }

        /// <summary>
        /// Parses only provider-rooted UaPath names, rejecting traversal and local-path ambiguities after unescaping.
        /// </summary>
        internal static string Path(string value, bool allowRoot, bool newLeaf = false)
        {
            MachineToolParameters.Text(value, 1024);
            if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException("Only paths inside the selected remote root are accepted.", nameof(value));
            }
            QualifiedName[] segments = UaPath.Parse(value);
            if (((!allowRoot || newLeaf) && segments.Length == 0) || segments.Length > 32)
            {
                throw new ArgumentException("A non-root path with at most 32 segments is required.", nameof(value));
            }
            foreach (QualifiedName segment in segments)
            {
                string name = segment.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." ||
                    name.EndsWith(' ') || name.EndsWith('.') ||
                    name.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
                {
                    throw new ArgumentException("Unsafe or oversized remote path segment.", nameof(value));
                }
                foreach (char character in name)
                {
                    if (char.IsControl(character))
                    {
                        throw new ArgumentException(
                            "Control characters are not allowed in remote paths.", nameof(value));
                    }
                }
            }
            if (newLeaf && segments[^1].NamespaceIndex != 0)
            {
                throw new ArgumentException("A new leaf has no namespace prefix; its namespace is server-assigned.");
            }
            return UaPath.Format(segments);
        }

        /// <summary>
        /// Decodes a bounded standard base64 upload without accepting whitespace or unbounded allocation.
        /// </summary>
        internal static ByteString Upload(string contentBase64, int maximumBytes)
        {
            MachineToolParameters.Bound(maximumBytes, 1, MaximumFileBytes);
            ArgumentNullException.ThrowIfNull(contentBase64);
            if (contentBase64.Length > 4 * ((maximumBytes + 2) / 3))
            {
                throw new ArgumentOutOfRangeException(nameof(contentBase64), "The upload exceeds maximumBytes.");
            }
            foreach (char character in contentBase64)
            {
                if (char.IsWhiteSpace(character))
                {
                    throw new ArgumentException("Base64 must not contain whitespace.", nameof(contentBase64));
                }
            }
            byte[] bytes = Convert.FromBase64String(contentBase64);
            if (bytes.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(contentBase64), "The upload exceeds maximumBytes.");
            }
            return ByteString.From(bytes);
        }

        /// <summary>
        /// Reads the small native file-options object without coercion or silently ignored fields.
        /// </summary>
        internal static (string? Destination, string? ContentBase64, int MaximumBytes, bool Recursive) FileOptions(
            JsonElement value)
        {
            string? destination = null;
            string? content = null;
            int maximum = 65536;
            bool recursive = false;
            if (!Present(value))
            {
                return (destination, content, maximum, recursive);
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("options must be a native JSON object.", nameof(value));
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new ArgumentException("Duplicate file option.", nameof(value));
                }
                switch (property.Name)
                {
                    case "destination":
                    case "contentBase64":
                        if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        {
                            throw new ArgumentException("File destination/content must be strings or null.");
                        }
                        string? text = property.Value.GetString();
                        if (property.Name == "destination")
                        {
                            destination = text;
                        }
                        else
                        {
                            content = text;
                        }
                        break;
                    case "maximumBytes":
                        if (property.Value.ValueKind != JsonValueKind.Number ||
                            !property.Value.TryGetInt32(out maximum))
                        {
                            throw new ArgumentException("maximumBytes must be an integer.", nameof(value));
                        }
                        break;
                    case "recursive":
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        {
                            throw new ArgumentException("recursive must be a Boolean.", nameof(value));
                        }
                        recursive = property.Value.GetBoolean();
                        break;
                    default:
                        throw new ArgumentException("Unknown file option.", nameof(value));
                }
            }
            return (destination, content, maximum, recursive);
        }

        /// <summary>
        /// Caps both native payload bytes and structural amplification independently of session-wide quotas.
        /// </summary>
        private static void Native(JsonElement value)
        {
            string raw = value.GetRawText();
            if (raw.Length > 65536 || Encoding.UTF8.GetByteCount(raw) > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Native JSON is limited to 65536 UTF-8 bytes.");
            }
            int remaining = 4096;
            Visit(value, 0, ref remaining);
        }

        /// <summary>
        /// Validates native array cardinality without replacing its generated element codec.
        /// </summary>
        private static void NativeArray(JsonElement value, int minimum, int maximum)
        {
            if (value.ValueKind != JsonValueKind.Array ||
                value.GetArrayLength() < minimum || value.GetArrayLength() > maximum)
            {
                throw new ArgumentException($"A native JSON array with {minimum}-{maximum} entries is required.");
            }
            Native(value);
        }

        /// <summary>
        /// Rejects deep structures, excessive nested collections and non-finite JSON numbers.
        /// </summary>
        private static void Visit(JsonElement value, int depth, ref int remaining)
        {
            if (depth > 32 || --remaining < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Native JSON exceeds depth or element limits.");
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                MachineToolParameters.Bound(value.GetArrayLength(), 0, 1024);
                foreach (JsonElement child in value.EnumerateArray())
                {
                    Visit(child, depth + 1, ref remaining);
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    Visit(property.Value, depth + 1, ref remaining);
                }
            }
            else if (value.ValueKind == JsonValueKind.Number &&
                (!value.TryGetDouble(out double number) || !double.IsFinite(number)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
            }
        }

        /// <summary>Per-call raw file cap, independent of the whole encoded response cap.</summary>
        internal const int MaximumFileBytes = 524288;
    }
}
