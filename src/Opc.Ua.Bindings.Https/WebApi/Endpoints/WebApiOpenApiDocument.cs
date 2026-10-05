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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Bindings.WebApi.Endpoints
{
    /// <summary>
    /// Generates the OpenAPI document of a REST binding once per server
    /// URL and keeps the UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// The server URL is the path base of the request, set by the host
    /// (for example <c>UsePathBase</c>), never by the client. The cache is
    /// bounded all the same.
    /// </remarks>
    internal sealed class WebApiOpenApiDocument
    {
        public WebApiOpenApiDocument(WebApiServiceSet serviceSet, bool includeSchemas)
        {
            m_serviceSet = serviceSet;
            m_includeSchemas = includeSchemas;
        }

        /// <summary>
        /// Returns the UTF-8 JSON document.
        /// </summary>
        /// <param name="generator">The generator to build the document with.</param>
        /// <param name="serverUrl">
        /// The URL to list under <c>servers</c>, or <c>null</c> to list none.
        /// </param>
        /// <returns>The document bytes.</returns>
        public ByteString Get(WebApiOpenApiGenerator generator, string? serverUrl)
        {
            string key = serverUrl ?? string.Empty;
            if (m_documents.TryGetValue(key, out ByteString cached))
            {
                return cached;
            }

            JsonObject document = generator.Generate(m_serviceSet, m_includeSchemas, serverUrl);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                document.WriteTo(writer);
            }
            var bytes = new ByteString(stream.ToArray());
            if (m_documents.Count < kMaxCachedDocuments)
            {
                m_documents.TryAdd(key, bytes);
            }
            return bytes;
        }

        private const int kMaxCachedDocuments = 16;

        private readonly WebApiServiceSet m_serviceSet;
        private readonly bool m_includeSchemas;
        private readonly ConcurrentDictionary<string, ByteString> m_documents = new(StringComparer.Ordinal);
    }
}
#endif
