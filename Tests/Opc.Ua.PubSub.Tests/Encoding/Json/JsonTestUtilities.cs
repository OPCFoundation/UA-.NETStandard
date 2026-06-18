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
 *
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
using Microsoft.Extensions.Time.Testing;
using Opc.Ua;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Shared helpers used across the JSON PubSub encoder/decoder
    /// fixtures.
    /// </summary>
    internal static class JsonTestUtilities
    {
        /// <summary>
        /// Builds a <see cref="PubSubNetworkMessageContext"/> bound to a
        /// fresh metadata registry, a low-verbosity diagnostics sink and
        /// a fixed-time provider so test assertions stay deterministic.
        /// </summary>
        /// <param name="registry">Optional registry override.</param>
        /// <param name="diagnostics">Optional diagnostics override.</param>
        /// <param name="timeProvider">Optional clock override.</param>
        /// <returns>A ready-to-use context instance.</returns>
        public static PubSubNetworkMessageContext NewContext(
            IDataSetMetaDataRegistry? registry = null,
            IPubSubDiagnostics? diagnostics = null,
            TimeProvider? timeProvider = null)
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry ?? new DataSetMetaDataRegistry(),
                diagnostics ?? new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                timeProvider ?? new FakeTimeProvider(
                    new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        }

        /// <summary>
        /// Constructs a small metadata description used by all JSON
        /// encoder/decoder fixtures.
        /// </summary>
        /// <param name="name">Display name of the dataset.</param>
        /// <returns>A configured <see cref="DataSetMetaDataType"/>.</returns>
        public static DataSetMetaDataType CreateMetaData(string name = "TestDataSet")
        {
            FieldMetaData[] fields =
            [
                new FieldMetaData
                {
                    Name = "BoolField",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "IntField",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "StringField",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                }
            ];
            return new DataSetMetaDataType
            {
                Name = name,
                Fields = new ArrayOf<FieldMetaData>(fields.AsMemory()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }

        /// <summary>
        /// Three matching fields that align with <see cref="CreateMetaData"/>.
        /// </summary>
        /// <param name="encoding">Encoding selected for each field.</param>
        /// <returns>Field list.</returns>
        public static ArrayOf<DataSetField> CreateFields(
            PubSubFieldEncoding encoding = PubSubFieldEncoding.Variant)
        {
            return new[]
            {
                new DataSetField
                {
                    Name = "BoolField",
                    Value = new Variant(true),
                    Encoding = encoding
                },
                new DataSetField
                {
                    Name = "IntField",
                    Value = new Variant(42),
                    Encoding = encoding
                },
                new DataSetField
                {
                    Name = "StringField",
                    Value = new Variant("hello"),
                    Encoding = encoding
                }
            };
        }

        /// <summary>
        /// Helper for tests that need to verify a counter increment in
        /// the diagnostics sink attached to the supplied context.
        /// </summary>
        /// <param name="context">Context whose diagnostics are read.</param>
        /// <param name="kind">Counter identity.</param>
        /// <returns>Current counter value.</returns>
        public static long Read(
            PubSubNetworkMessageContext context,
            PubSubDiagnosticsCounterKind kind)
        {
            return context.Diagnostics.Read(kind);
        }

        /// <summary>
        /// Decodes the supplied byte payload as UTF-8 and returns it for
        /// assertion failure messages.
        /// </summary>
        /// <param name="payload">Encoded bytes.</param>
        /// <returns>Decoded UTF-8 string.</returns>
        public static string ToText(ReadOnlyMemory<byte> payload)
        {
            return Encoding.UTF8.GetString(payload.ToArray());
        }

        /// <summary>
        /// Canonicalises the supplied JSON text by sorting object
        /// property names recursively. Used by parity tests to ignore
        /// ordering differences between encoders.
        /// </summary>
        /// <param name="text">JSON input.</param>
        /// <returns>Canonical JSON text.</returns>
        public static string Canonicalise(string text)
        {
            using JsonDocument document = JsonDocument.Parse(text);
            using var stream = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(stream,
                new JsonWriterOptions { Indented = false }))
            {
                WriteSorted(writer, document.RootElement);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteSorted(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    var props = new List<JsonProperty>();
                    foreach (JsonProperty p in element.EnumerateObject())
                    {
                        props.Add(p);
                    }
                    props.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    foreach (JsonProperty p in props)
                    {
                        writer.WritePropertyName(p.Name);
                        WriteSorted(writer, p.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement child in element.EnumerateArray())
                    {
                        WriteSorted(writer, child);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }
    }
}
