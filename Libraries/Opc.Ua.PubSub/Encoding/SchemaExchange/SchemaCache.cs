/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Opc.Ua;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Thread-safe cache and per-destination announcement tracker for SchemaId handshakes.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_1")]
    public sealed class SchemaCache
    {
        /// <summary>
        /// Gets the Avro format name used by the cache.
        /// </summary>
        public const string AvroFormat = "avro";

        /// <summary>
        /// Gets the Arrow format name used by the cache.
        /// </summary>
        public const string ArrowFormat = "arrow";

        private readonly ConcurrentDictionary<string, SchemaCacheEntry> _schemas = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _announced =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Attempts to get a cached schema by SchemaId.
        /// </summary>
        /// <param name="schemaId">The raw schema identifier.</param>
        /// <param name="entry">The cached schema entry.</param>
        /// <returns><c>true</c> when a cache entry exists; otherwise <c>false</c>.</returns>
        public bool TryGet(ByteString schemaId, out SchemaCacheEntry entry)
        {
            return _schemas.TryGetValue(ToKey(schemaId), out entry);
        }

        /// <summary>
        /// Adds a schema after verifying that the recomputed SchemaId matches.
        /// </summary>
        /// <param name="schemaId">The announced raw schema identifier.</param>
        /// <param name="schema">The schema bytes.</param>
        /// <param name="format">The schema format.</param>
        /// <exception cref="InvalidOperationException">Thrown when SchemaId verification fails.</exception>
        public void Add(ByteString schemaId, ByteString schema, string format)
        {
            ByteString actual = ComputeSchemaId(schema, format);
            if (!schemaId.Span.SequenceEqual(actual.Span))
            {
                throw new InvalidOperationException("The announced SchemaId does not match the schema fingerprint.");
            }
            _schemas[ToKey(schemaId)] = new SchemaCacheEntry(schemaId, schema, NormalizeFormat(format));
        }

        /// <summary>
        /// Adds an Avro schema announcement after verifying the announced SchemaId.
        /// </summary>
        /// <param name="announcement">The Avro schema announcement.</param>
        public void Add(AvroSchemaAnnouncement announcement)
        {
            if (announcement is null)
            {
                throw new ArgumentNullException(nameof(announcement));
            }
            ByteString schema = ByteString.From(System.Text.Encoding.UTF8.GetBytes(announcement.SchemaJson));
            Add(announcement.SchemaId, schema, AvroFormat);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Adds an Arrow schema announcement after verifying the announced SchemaId.
        /// </summary>
        /// <param name="announcement">The Arrow schema announcement.</param>
        public void Add(ArrowSchemaAnnouncement announcement)
        {
            if (announcement is null)
            {
                throw new ArgumentNullException(nameof(announcement));
            }
            Add(announcement.SchemaId, announcement.Schema, ArrowFormat);
        }
#endif

        /// <summary>
        /// Marks a schema as announced to a destination if this is the first announcement.
        /// </summary>
        /// <param name="destinationId">The destination identity.</param>
        /// <param name="schemaId">The raw schema identifier.</param>
        /// <returns><c>true</c> when the schema was not previously announced.</returns>
        public bool MarkAnnounced(string destinationId, ByteString schemaId)
        {
            string destinationKey = destinationId ?? string.Empty;
            ConcurrentDictionary<string, byte> set = _announced.GetOrAdd(
                destinationKey,
                _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            return set.TryAdd(ToKey(schemaId), 0);
        }

        /// <summary>
        /// Resolves a cached schema or invokes the supplied resolver on a cache miss.
        /// </summary>
        /// <param name="schemaId">The raw schema identifier.</param>
        /// <param name="resolver">The optional cache-miss resolver.</param>
        /// <param name="entry">The cached or resolved entry.</param>
        /// <returns><c>true</c> when the schema is available; otherwise <c>false</c>.</returns>
        public bool TryGetOrResolve(ByteString schemaId, ISchemaResolver? resolver, out SchemaCacheEntry entry)
        {
            if (TryGet(schemaId, out entry))
            {
                return true;
            }
            if (resolver is not null && resolver.TryResolve(schemaId, out (ByteString schema, string format) result))
            {
                try
                {
                    Add(schemaId, result.schema, result.format);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    // A resolver that returns bytes which re-fingerprint to a different SchemaId, or
                    // an unsupported format, is treated as "schema not available" so a Try* decoder
                    // returns null and records diagnostics instead of letting the exception escape.
                    entry = default;
                    return false;
                }
                return TryGet(schemaId, out entry);
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Computes the raw SchemaId for a schema and format.
        /// </summary>
        /// <param name="schema">The schema bytes.</param>
        /// <param name="format">The schema format.</param>
        /// <returns>The raw SchemaId bytes.</returns>
        public static ByteString ComputeSchemaId(ByteString schema, string format)
        {
            string normalized = NormalizeFormat(format);
            if (normalized == AvroFormat)
            {
                ulong fingerprint = SchemaId.RabinCrc64Avro(schema.Span);
                return ByteString.From(SchemaId.AvroSingleObjectPrefix(fingerprint).AsSpan(2, 8));
            }
            if (normalized == ArrowFormat)
            {
                return ByteString.From(SchemaId.Sha256Id(schema.Span, 8));
            }
            throw new ArgumentException("The schema format is not supported.", nameof(format));
        }

        /// <summary>
        /// Converts raw bytes to a lowercase diagnostic hexadecimal key.
        /// </summary>
        /// <param name="schemaId">The raw schema identifier.</param>
        /// <returns>The lowercase hexadecimal representation.</returns>
        public static string ToKey(ByteString schemaId)
        {
            if (schemaId.IsNull)
            {
                return string.Empty;
            }
#if NETFRAMEWORK
            // Utils.ToHexString only exposes the ReadOnlySpan overload on .NET Standard 2.1
            // and .NET 6+; the .NET Framework targets fall back to the array overload.
            return Utils.ToHexString(schemaId.Span.ToArray()).ToLower(CultureInfo.InvariantCulture);
#else
            return Utils.ToHexString(schemaId.Span).ToLower(CultureInfo.InvariantCulture);
#endif
        }

        /// <summary>
        /// Parses a lowercase or uppercase hexadecimal SchemaId.
        /// </summary>
        /// <param name="text">The hexadecimal SchemaId.</param>
        /// <param name="schemaId">The parsed raw SchemaId.</param>
        /// <returns><c>true</c> when parsing succeeds; otherwise <c>false</c>.</returns>
        public static bool TryParseKey(string? text, out ByteString schemaId)
        {
            schemaId = default;
            if (string.IsNullOrWhiteSpace(text) || text.Length != 16)
            {
                return false;
            }
            try
            {
                schemaId = ByteString.From(Utils.FromHexString(text)!);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string NormalizeFormat(string format)
        {
            return (format ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
        }
    }
}
