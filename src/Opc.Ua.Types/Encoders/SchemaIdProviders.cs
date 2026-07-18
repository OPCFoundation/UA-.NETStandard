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
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Opc.Ua
{
    /// <summary>
    /// Computes the raw on-wire SchemaId fingerprint for one schema format. Providers are
    /// pluggable so a host can register a fingerprint algorithm for any schema format (see
    /// <see cref="SchemaIdProviders"/>), including a JSON Schema provider that is only present
    /// when JSON Schema support is registered.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public interface ISchemaIdProvider
    {
        /// <summary>
        /// Gets the schema format name this provider fingerprints (lower-case, for example avro).
        /// </summary>
        string Format { get; }

        /// <summary>
        /// Gets the SchemaIdAlg name that identifies the (canonicalization, hash) this provider
        /// uses, for example CRC-64-AVRO, SHA-256/ApacheArrow or SHA-256/JCS.
        /// </summary>
        string Algorithm { get; }

        /// <summary>
        /// Computes the raw SchemaId fingerprint bytes for the supplied schema document.
        /// </summary>
        /// <param name="schema">The schema document bytes to fingerprint.</param>
        /// <returns>The raw SchemaId fingerprint bytes.</returns>
        byte[] ComputeSchemaId(ReadOnlySpan<byte> schema);
    }

    /// <summary>
    /// Registry of pluggable per-format <see cref="ISchemaIdProvider"/> fingerprint providers.
    /// The built-in Apache Avro, Apache Arrow and JSON Schema providers are pre-registered; a
    /// host may register additional providers (for example a custom schema format) at startup.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public static class SchemaIdProviders
    {
        /// <summary>
        /// The Apache Avro format name.
        /// </summary>
        public const string AvroFormat = "avro";

        /// <summary>
        /// The Apache Arrow format name.
        /// </summary>
        public const string ArrowFormat = "arrow";

        /// <summary>
        /// The JSON Schema format name.
        /// </summary>
        public const string JsonFormat = "json";

        private static readonly ConcurrentDictionary<string, ISchemaIdProvider> s_providers = CreateDefaults();

        /// <summary>
        /// Registers (or replaces) the provider for its <see cref="ISchemaIdProvider.Format"/>.
        /// </summary>
        /// <param name="provider">The provider to register.</param>
        public static void Register(ISchemaIdProvider provider)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            s_providers[Normalize(provider.Format)] = provider;
        }

        /// <summary>
        /// Attempts to get the provider registered for a schema format.
        /// </summary>
        /// <param name="format">The schema format.</param>
        /// <param name="provider">The registered provider.</param>
        /// <returns><c>true</c> when a provider is registered; otherwise <c>false</c>.</returns>
        public static bool TryGet(string format, [MaybeNullWhen(false)] out ISchemaIdProvider provider)
        {
            return s_providers.TryGetValue(Normalize(format), out provider);
        }

        /// <summary>
        /// Gets the SchemaIdAlg name for a schema format, or <c>null</c> when unregistered.
        /// </summary>
        /// <param name="format">The schema format.</param>
        /// <returns>The algorithm name, or <c>null</c> when no provider is registered.</returns>
        public static string? AlgorithmFor(string format)
        {
            if (TryGet(format, out ISchemaIdProvider? provider))
            {
                return provider.Algorithm;
            }

            return null;
        }

        /// <summary>
        /// Computes the raw SchemaId for a schema and format using the registered provider.
        /// </summary>
        /// <param name="format">The schema format.</param>
        /// <param name="schema">The schema document bytes.</param>
        /// <returns>The raw SchemaId fingerprint bytes.</returns>
        /// <exception cref="ArgumentException">Thrown when no provider is registered for the format.</exception>
        public static byte[] ComputeSchemaId(string format, ReadOnlySpan<byte> schema)
        {
            if (!TryGet(format, out ISchemaIdProvider? provider))
            {
                throw new ArgumentException("The schema format is not supported.", nameof(format));
            }

            return provider.ComputeSchemaId(schema);
        }

        private static ConcurrentDictionary<string, ISchemaIdProvider> CreateDefaults()
        {
            var map = new ConcurrentDictionary<string, ISchemaIdProvider>(StringComparer.Ordinal);
            ISchemaIdProvider[] defaults =
            [
                new AvroSchemaIdProvider(),
                new ArrowSchemaIdProvider(),
                new JsonSchemaIdProvider(),
            ];
            foreach (ISchemaIdProvider provider in defaults)
            {
                map[Normalize(provider.Format)] = provider;
            }

            return map;
        }

        private static string Normalize(string format)
        {
            return (format ?? string.Empty).Trim().ToLower(CultureInfo.InvariantCulture);
        }

        private sealed class AvroSchemaIdProvider : ISchemaIdProvider
        {
            public string Format => AvroFormat;

            public string Algorithm => "CRC-64-AVRO";

            public byte[] ComputeSchemaId(ReadOnlySpan<byte> schema)
            {
                // Canonical Avro SchemaId: CRC-64-AVRO over the Avro Parsing Canonical Form
                // (Part 6 §6.6). Non-schema inputs fall back to the raw bytes.
                ulong fingerprint;
                try
                {
                    string canonical = AvroParsingCanonicalForm.Compute(
                        System.Text.Encoding.UTF8.GetString(schema.ToArray()));
                    fingerprint = SchemaId.RabinCrc64Avro(System.Text.Encoding.UTF8.GetBytes(canonical));
                }
                catch (Exception ex) when (ex is FormatException || ex is System.Text.Json.JsonException)
                {
                    fingerprint = SchemaId.RabinCrc64Avro(schema);
                }

                byte[] id = new byte[8];
                for (int ii = 0; ii < id.Length; ii++)
                {
                    id[ii] = (byte)(fingerprint >> (8 * ii));
                }

                return id;
            }
        }

        private sealed class ArrowSchemaIdProvider : ISchemaIdProvider
        {
            public string Format => ArrowFormat;

            public string Algorithm => "SHA-256/ApacheArrow";

            public byte[] ComputeSchemaId(ReadOnlySpan<byte> schema)
            {
                return SchemaId.Sha256Id(schema, 8);
            }
        }

        private sealed class JsonSchemaIdProvider : ISchemaIdProvider
        {
            public string Format => JsonFormat;

            public string Algorithm => "SHA-256/JCS";

            public byte[] ComputeSchemaId(ReadOnlySpan<byte> schema)
            {
                return SchemaId.JsonSchemaId(schema, 8);
            }
        }
    }
}
