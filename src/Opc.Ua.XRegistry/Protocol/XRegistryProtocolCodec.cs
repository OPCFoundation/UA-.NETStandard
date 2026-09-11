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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// Bounded, reflection-free envelopes for the experimental native extension and
    /// durable journals. This format is not the xRegistry HTTP JSON representation.
    /// Caller context is deliberately never encoded and must be supplied by the host.
    /// </summary>
    public sealed class XRegistryProtocolCodec
    {
        public XRegistryProtocolCodec(int maximumBytes = 33_554_432, int maximumDepth = 64)
        {
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            if (maximumDepth is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            }
            MaximumBytes = maximumBytes;
            MaximumDepth = maximumDepth;
        }

        public int MaximumBytes { get; }

        public int MaximumDepth { get; }

        /// <summary>
        /// Serializes a registry generation with the same exact byte/depth bounds
        /// as transport envelopes, without materializing an unbounded intermediate string.
        /// </summary>
        public ByteString EncodeJson(JsonNode value)
        {
            value.ThrowIfNull(nameof(value));
            var buffer = new BoundedBufferWriter(MaximumBytes);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaximumDepth }))
            {
                value.WriteTo(writer);
                writer.Flush();
            }
            return ByteString.From(buffer.WrittenMemory.Span);
        }

        public ByteString EncodeRequest(XRegistryRequest request)
        {
            request.ThrowIfNull(nameof(request));
            return Encode(writer =>
            {
                writer.WriteNumber("action", (int)request.Action);
                writer.WriteString("path", request.Path);
                writer.WriteNumber("view", (int)request.View);
                WriteMetadata(writer, "metadata", request.Metadata);
                WriteDocument(writer, request.Document);
                WriteOptionalString(writer, "contentType", request.ContentType);
                WriteOptionalString(writer, "operationId", request.OperationId);
                writer.WriteStartArray("parameters");
                for (int index = 0; index < request.Parameters.Count; index++)
                {
                    XRegistryParameter parameter = request.Parameters[index];
                    if (parameter is null || string.IsNullOrEmpty(parameter.Name))
                    {
                        throw new ArgumentException("A parameter must have a name.", nameof(request));
                    }
                    writer.WriteStartObject();
                    writer.WriteString("name", parameter.Name);
                    writer.WriteString("value", parameter.Value);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            });
        }

        public XRegistryRequest DecodeRequest(ByteString data, XRegistryCallContext context)
        {
            context.ThrowIfNull(nameof(context));
            using JsonDocument document = Parse(data);
            JsonElement root = document.RootElement;
            var action = (XRegistryAction)ReadInt(root, "action");
            var view = (XRegistryView)ReadInt(root, "view");
            if (action is < XRegistryAction.Read or > XRegistryAction.Describe ||
                view is < XRegistryView.Default or > XRegistryView.Metadata)
            {
                throw new JsonException("Unknown registry action or view.");
            }

            var parameters = new List<XRegistryParameter>();
            if (root.TryGetProperty("parameters", out JsonElement parameterArray))
            {
                RequireKind(parameterArray, JsonValueKind.Array);
                foreach (JsonElement parameter in parameterArray.EnumerateArray())
                {
                    RequireUniqueObject(parameter);
                    string name = ReadString(parameter, "name");
                    if (name.Length == 0)
                    {
                        throw new JsonException("A parameter must have a name.");
                    }
                    parameters.Add(new XRegistryParameter(name, ReadOptionalString(parameter, "value")));
                }
            }

            return new XRegistryRequest(action, ReadString(root, "path"))
            {
                View = view,
                Metadata = ReadMetadata(root, "metadata"),
                Document = ReadDocument(root),
                ContentType = ReadOptionalString(root, "contentType"),
                OperationId = ReadOptionalString(root, "operationId"),
                Parameters = [.. parameters],
                Context = context
            };
        }

        public ByteString EncodeResponse(XRegistryResponse response)
        {
            response.ThrowIfNull(nameof(response));
            return Encode(writer =>
            {
                writer.WriteNumber("status", response.StatusCode);
                WriteMetadata(writer, "metadata", response.Metadata);
                WriteDocument(writer, response.Document);
                WriteOptionalString(writer, "contentType", response.ContentType);
                WriteOptionalString(writer, "location", response.Location);
                WriteOptionalString(writer, "contentLocation", response.ContentLocation);
                WriteOptionalString(writer, "correlationId", response.CorrelationId);
                if (response.Error is not null)
                {
                    writer.WriteStartObject("error");
                    writer.WriteString("code", response.Error.Code);
                    writer.WriteString("detail", response.Error.Detail);
                    WriteOptionalString(writer, "subject", response.Error.Subject);
                    writer.WriteEndObject();
                }
                writer.WriteStartArray("links");
                for (int index = 0; index < response.Links.Count; index++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("relation", response.Links[index].Relation);
                    writer.WriteString("target", response.Links[index].Target);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("allowedActions");
                for (int index = 0; index < response.AllowedActions.Count; index++)
                {
                    writer.WriteNumberValue((int)response.AllowedActions[index]);
                }
                writer.WriteEndArray();
            });
        }

        public XRegistryResponse DecodeResponse(ByteString data)
        {
            using JsonDocument document = Parse(data);
            JsonElement root = document.RootElement;
            XRegistryError? error = null;
            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                RequireUniqueObject(errorElement);
                error = new XRegistryError(ReadString(errorElement, "code"), ReadString(errorElement, "detail"))
                {
                    Subject = ReadOptionalString(errorElement, "subject")
                };
            }

            var links = new List<XRegistryLink>();
            if (root.TryGetProperty("links", out JsonElement linkArray))
            {
                RequireKind(linkArray, JsonValueKind.Array);
                foreach (JsonElement link in linkArray.EnumerateArray())
                {
                    RequireUniqueObject(link);
                    links.Add(new XRegistryLink(ReadString(link, "relation"), ReadString(link, "target")));
                }
            }

            var actions = new List<XRegistryAction>();
            if (root.TryGetProperty("allowedActions", out JsonElement actionArray))
            {
                RequireKind(actionArray, JsonValueKind.Array);
                foreach (JsonElement element in actionArray.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Number ||
                        !element.TryGetInt32(out int number) ||
                        number is < (int)XRegistryAction.Read or > (int)XRegistryAction.Describe)
                    {
                        throw new JsonException("Unknown allowed action.");
                    }
                    actions.Add((XRegistryAction)number);
                }
            }

            return new XRegistryResponse(ReadInt(root, "status"))
            {
                Metadata = ReadMetadata(root, "metadata"),
                Document = ReadDocument(root),
                ContentType = ReadOptionalString(root, "contentType"),
                Location = ReadOptionalString(root, "location"),
                ContentLocation = ReadOptionalString(root, "contentLocation"),
                CorrelationId = ReadOptionalString(root, "correlationId"),
                Links = [.. links],
                AllowedActions = [.. actions],
                Error = error
            };
        }

        public ByteString EncodeDescription(XRegistryEndpointDescription description)
        {
            description.ThrowIfNull(nameof(description));
            return Encode(writer =>
            {
                writer.WriteString("registryId", description.RegistryId);
                writer.WriteString("profile", description.Profile);
                WriteOptionalString(writer, "publicRoot", description.PublicRoot?.AbsoluteUri);
                WriteMetadata(writer, "model", description.Model);
                WriteMetadata(writer, "capabilities", description.Capabilities);
                writer.WriteBoolean("atomicMutations", description.SupportsAtomicMutations);
                writer.WriteBoolean("conditionalMutations", description.SupportsConditionalMutations);
                writer.WriteBoolean("writeTouch", description.SupportsWriteTouch);
                writer.WriteBoolean("operationReplay", description.SupportsOperationReplay);
                if (description.SupportsPreparedMutations)
                {
                    writer.WriteBoolean("preparedMutations", true);
                }
            });
        }

        public XRegistryEndpointDescription DecodeDescription(ByteString data)
        {
            using JsonDocument document = Parse(data);
            JsonElement root = document.RootElement;
            return new XRegistryEndpointDescription(ReadString(root, "registryId"))
            {
                Profile = ReadString(root, "profile"),
                PublicRoot = ReadOptionalString(root, "publicRoot") is string rootUri
                    ? new Uri(rootUri, UriKind.Absolute) : null,
                Model = ReadMetadata(root, "model"),
                Capabilities = ReadMetadata(root, "capabilities"),
                SupportsAtomicMutations = ReadBoolean(root, "atomicMutations"),
                SupportsConditionalMutations = ReadBoolean(root, "conditionalMutations"),
                SupportsWriteTouch = ReadBoolean(root, "writeTouch"),
                SupportsOperationReplay = ReadBoolean(root, "operationReplay"),
                SupportsPreparedMutations = root.TryGetProperty("preparedMutations", out _) &&
                    ReadBoolean(root, "preparedMutations")
            };
        }

        /// <summary>
        /// Hashes the complete encoded request, including response parameters but not
        /// credentials or context. Journals must additionally scope IDs by caller.
        /// </summary>
        public string ComputeRequestDigest(XRegistryRequest request)
        {
#if NET5_0_OR_GREATER
            byte[] digest = SHA256.HashData(EncodeRequest(request).Span);
#else
            using var algorithm = SHA256.Create();
            byte[] digest = algorithm.ComputeHash(EncodeRequest(request).Span.ToArray());
#endif
            var result = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        private ByteString Encode(Action<Utf8JsonWriter> write)
        {
            var buffer = new BoundedBufferWriter(MaximumBytes);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaximumDepth }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("format", 1);
                write(writer);
                writer.WriteEndObject();
                writer.Flush();
            }
            return ByteString.From(buffer.WrittenMemory.Span);
        }

        private JsonDocument Parse(ByteString data)
        {
            if (data.IsNull || data.Length > MaximumBytes)
            {
                throw new JsonException("The registry message is absent or exceeds its byte limit.");
            }
            var document = JsonDocument.Parse(data.Memory,
                new JsonDocumentOptions { MaxDepth = MaximumDepth });
            try
            {
                RequireUniqueObject(document.RootElement);
                if (ReadInt(document.RootElement, "format") != 1)
                {
                    throw new JsonException("Unsupported registry envelope format.");
                }
                return document;
            }
            catch (JsonException)
            {
                document.Dispose();
                throw;
            }
        }

        private static void WriteMetadata(Utf8JsonWriter writer, string name, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Undefined)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
        }

        private static void WriteDocument(Utf8JsonWriter writer, ByteString value)
        {
            if (!value.IsNull)
            {
                writer.WriteBase64String("document", value.Span);
            }
        }

        private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
        {
            if (value is not null)
            {
                writer.WriteString(name, value);
            }
        }

        private static JsonElement ReadMetadata(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) ? value : default;
        }

        private static ByteString ReadDocument(JsonElement root)
        {
            if (!root.TryGetProperty("document", out JsonElement value))
            {
                return default;
            }
            if (value.ValueKind != JsonValueKind.String || !value.TryGetBytesFromBase64(out byte[]? bytes))
            {
                throw new JsonException("Document must be a base64 string; omission represents no document.");
            }
            return new ByteString(bytes);
        }

        private static string ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"The registry message requires string '{name}'.");
            }
            return value.GetString()!;
        }

        private static string? ReadOptionalString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }
            RequireKind(value, JsonValueKind.String);
            return value.GetString();
        }

        private static int ReadInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out int result))
            {
                throw new JsonException($"The registry message requires integer '{name}'.");
            }
            return result;
        }

        private static bool ReadBoolean(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new JsonException($"The registry message requires boolean '{name}'.");
            }
            return value.GetBoolean();
        }

        private static void RequireKind(JsonElement value, JsonValueKind expected)
        {
            if (value.ValueKind != expected)
            {
                throw new JsonException($"Expected a JSON {expected}.");
            }
        }

        private static void RequireUniqueObject(JsonElement value)
        {
            RequireKind(value, JsonValueKind.Object);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("Duplicate registry envelope property.");
                }
            }
        }

        private sealed class BoundedBufferWriter : IBufferWriter<byte>
        {
            public BoundedBufferWriter(int maximumBytes)
            {
                m_maximumBytes = maximumBytes;
            }

            public ReadOnlyMemory<byte> WrittenMemory => m_buffer.AsMemory(0, m_written);

            public void Advance(int count)
            {
                if (count < 0 || count > m_buffer.Length - m_written)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }
                if (count > m_maximumBytes - m_written)
                {
                    throw new ArgumentException("The encoded registry message exceeds its byte limit.", nameof(count));
                }
                m_written += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return m_buffer.AsMemory(m_written);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return m_buffer.AsSpan(m_written);
            }

            private void EnsureCapacity(int sizeHint)
            {
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }
                sizeHint = Math.Max(1, sizeHint);
                // The writer reserves up to six bytes per input character for escaping.
                // Bound that scratch reservation separately from Advance's actual-byte cap.
                int allocationLimit = (int)Math.Min(int.MaxValue,
                    m_written + (6L * (m_maximumBytes - m_written)) + 4096);
                if (sizeHint > allocationLimit - m_written)
                {
                    throw new ArgumentException("The encoded registry message exceeds its byte limit.",
                        nameof(sizeHint));
                }
                if (sizeHint > m_buffer.Length - m_written)
                {
                    int capacity = (int)Math.Min(allocationLimit,
                        Math.Max((long)m_written + sizeHint, (long)m_buffer.Length * 2));
                    Array.Resize(ref m_buffer, capacity);
                }
            }

            private readonly int m_maximumBytes;
            private byte[] m_buffer = [];
            private int m_written;
        }
    }
}
