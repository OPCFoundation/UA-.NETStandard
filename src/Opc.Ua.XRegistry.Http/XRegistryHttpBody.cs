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
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal sealed class XRegistryHttpBody
    {
        public XRegistryHttpBody(XRegistryHttpOptions options)
        {
            m_options = options;
            m_jsonCodec = new XRegistryProtocolCodec(options.MaximumBodyBytes, options.MaximumJsonDepth);
        }

        public ByteString Encode(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Undefined)
            {
                return default;
            }
            // The shared bounded writer handles open JSON values without CLR reflection.
            var node = JsonNode.Parse(value.GetRawText(),
                documentOptions: new JsonDocumentOptions { MaxDepth = m_options.MaximumJsonDepth });
            if (node is null)
            {
                CheckLength(4);
                return new ByteString("null"u8.ToArray());
            }
            return m_jsonCodec.EncodeJson(node);
        }

        public ByteString Encode(JsonNode node)
        {
            return m_jsonCodec.EncodeJson(node);
        }

        public JsonElement Parse(ByteString bytes)
        {
            CheckLength(bytes.Length);
            using var document = JsonDocument.Parse(bytes.Memory,
                new JsonDocumentOptions { MaxDepth = m_options.MaximumJsonDepth });
            CheckUniqueNames(document.RootElement);
            return document.RootElement.Clone();
        }

        public async ValueTask<ByteString> ReadAsync(
            Stream stream,
            long? contentLength,
            ArrayOf<string> encodings,
            CancellationToken cancellationToken)
        {
            if (contentLength.HasValue)
            {
                CheckLength(contentLength.Value);
            }
            if (encodings.Count > 2)
            {
                throw new XRegistryHttpWireException(415, "about:blank", "Too many HTTP content encodings.");
            }
            ByteString bytes = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
            for (int index = encodings.Count - 1; index >= 0; index--)
            {
                string encoding = encodings[index].Trim();
                if (encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                using var input = new MemoryStream(bytes.Span.ToArray(), writable: false);
                using Stream decoder = encoding.ToLowerInvariant() switch
                {
                    "gzip" => new GZipStream(input, CompressionMode.Decompress),
                    "deflate" => new DeflateStream(input, CompressionMode.Decompress),
                    _ => throw new XRegistryHttpWireException(
                        415, "about:blank", "The HTTP content encoding is not supported.")
                };
                bytes = await ReadBoundedAsync(decoder, cancellationToken).ConfigureAwait(false);
            }
            return bytes;
        }

        public void CheckLength(long length)
        {
            if (length < 0 || length > m_options.MaximumBodyBytes)
            {
                throw new XRegistryHttpWireException(413, "about:blank", "The HTTP body exceeds its configured limit.");
            }
        }

        private async ValueTask<ByteString> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[Math.Min(81_920, m_options.MaximumBodyBytes)];
            using var output = new MemoryStream();
            while (true)
            {
                int count = (int)Math.Min(buffer.Length, m_options.MaximumBodyBytes - output.Length + 1);
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
                int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
#endif
                cancellationToken.ThrowIfCancellationRequested();
                if (read == 0)
                {
                    return new ByteString(output.ToArray());
                }
                CheckLength(output.Length + read);
                output.Write(buffer, 0, read);
            }
        }

        private static void CheckUniqueNames(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new JsonException("A JSON object contains a duplicate property.");
                    }
                    CheckUniqueNames(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    CheckUniqueNames(item);
                }
            }
        }

        private readonly XRegistryHttpOptions m_options;
        private readonly XRegistryProtocolCodec m_jsonCodec;
    }

    internal sealed class XRegistryHttpWireException : IOException
    {
        public XRegistryHttpWireException()
        {
        }

        public XRegistryHttpWireException(string message)
            : base(message)
        {
        }

        public XRegistryHttpWireException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public XRegistryHttpWireException(int statusCode, string code, string message)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }

        public XRegistryHttpWireException(int statusCode, string code, string message, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Code = code;
        }

        public XRegistryHttpWireException(string? message, int hresult)
            : base(message, hresult)
        {
        }

        public int StatusCode { get; } = 400;

        public string Code { get; } = "about:blank";

        public XRegistryResponse ToResponse(string path)
        {
            return new XRegistryResponse(StatusCode)
            {
                Error = new XRegistryError(Code, Message) { Subject = path }
            };
        }
    }
}
