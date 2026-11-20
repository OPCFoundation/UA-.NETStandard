// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET8_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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

            // Concurrent first requests wait for one generation instead of
            // each running it: the generator and a custom resolver are not
            // called concurrently, the cache cannot grow past its bound, and
            // a document beyond the bound is generated one at a time.
            lock (m_lock)
            {
                if (m_documents.TryGetValue(key, out cached))
                {
                    return cached;
                }

                ByteString bytes = Generate(generator, serverUrl);
                if (m_documents.Count < kMaxCachedDocuments)
                {
                    m_documents.TryAdd(key, bytes);
                }
                return bytes;
            }
        }

        private ByteString Generate(WebApiOpenApiGenerator generator, string? serverUrl)
        {
            JsonObject document = generator.Generate(m_serviceSet, m_includeSchemas, serverUrl);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                document.WriteTo(writer);
            }
            return new ByteString(stream.ToArray());
        }

        private const int kMaxCachedDocuments = 16;

        private readonly Lock m_lock = new();
        private readonly WebApiServiceSet m_serviceSet;
        private readonly bool m_includeSchemas;
        private readonly ConcurrentDictionary<string, ByteString> m_documents = new(StringComparer.Ordinal);
    }
}
#endif
