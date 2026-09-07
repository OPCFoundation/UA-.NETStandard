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
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Recreates the aggregation documents from their authoritative NodeSets.
    /// </summary>
    internal static partial class WotAggregationDocumentGenerator
    {
        public const string PumpInstanceNamespace =
            "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";

        public const string PumpModelDirectory = "sample-pump";

        public static byte[] GenerateThingModel(string sourcePath, string title)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                ReadNodeSet(sourcePath), title);
            return FormatJson(document).ToArray();
        }

        /// <summary>
        /// Keeps the synchronous, unverified exporter available to the existing
        /// converter regression fixtures. The demo uses the verified async path.
        /// </summary>
        public static IReadOnlyList<GeneratedDocument> GenerateThingModelSet(
            string sourcePath,
            string modelPrefix,
            string title)
        {
            WotConversionResult<WotDocumentSet> result = WotNodeSetConverter.FromNodeSetDocuments(
                ReadNodeSet(sourcePath), modelPrefix, title, CreateLargeDocumentOptions());
            using WotDocumentSet set = RequireValue(result, sourcePath);
            return set.Entries.ToList().Select(entry => new GeneratedDocument(
                entry.Href, FormatJson(entry.Document).ToArray())).ToArray();
        }

        /// <summary>
        /// Retains the standalone native-preservation regression asset without
        /// overlaying unrelated readable properties, actions or events.
        /// </summary>
        public static byte[] GeneratePumpThingDescription(string sourcePath)
        {
            return GenerateThingModel(sourcePath, "Sample Pump Aggregate");
        }

        public static WotNodeSetConverterOptions CreateLargeDocumentOptions()
        {
            return new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = 64 * 1024 * 1024,
                MaxResolverDocuments = 2048,
                MaxResolverDepth = 128
            };
        }

        public static UANodeSet ReadNodeSet(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return UANodeSet.Read(stream)
                ?? throw new InvalidOperationException($"Could not read '{path}'.");
        }

        public static string LocalNodeId(string pumpName, string path)
        {
            return $"nsu={PumpInstanceNamespace};s={pumpName}" +
                (path.Length == 0 ? string.Empty : "." + path);
        }

        public static ByteString FormatJson(WotDocument document)
        {
            using var canonical = WotDocument.Parse(document.ToCanonicalUtf8(), CreateLargeDocumentOptions());
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true,
                NewLine = "\n"
            }))
            {
                canonical.RootElement.WriteTo(writer);
            }
            stream.WriteByte((byte)'\n');
            return ByteString.From(stream.ToArray());
        }

        private static T RequireValue<T>(WotConversionResult<T> result, string origin)
            where T : class
        {
            if (!result.Success || result.Value is null)
            {
                throw new InvalidOperationException(
                    $"{origin}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
            }
            return result.Value;
        }

        private static ByteString FormatJson(JsonNode root)
        {
            using var document = WotDocument.Parse(
                JsonSerializer.SerializeToUtf8Bytes(root), CreateLargeDocumentOptions());
            return FormatJson(document);
        }

        private static JsonArray StringArray(IEnumerable<string> values)
        {
            var array = new JsonArray();
            foreach (string value in values)
            {
                array.Add(value);
            }
            return array;
        }

        private static string SourceNamespace(string source)
        {
            return source switch
            {
                "SourceA" => SourceANamespace,
                "SourceB" => SourceBNamespace,
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
            };
        }

        private static string SourceEndpoint(string source)
        {
            return source switch
            {
                "SourceA" => "${SOURCE_A_ENDPOINT}",
                "SourceB" => "${SOURCE_B_ENDPOINT}",
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
            };
        }

        internal sealed record GeneratedDocument(string Href, byte[] Json);

        internal sealed record SampleDocument(
            string ResourceId,
            string Path,
            WoTDocumentKindEnum DocumentKind,
            ByteString Json);

        internal sealed record ManifestEntry(
            string ResourceId,
            string Path,
            WoTDocumentKindEnum DocumentKind,
            ArrayOf<string> DependsOn);

        private const string SourceANamespace = "urn:opcfoundation.org:UA:WotAggregation:SourceA";
        private const string SourceBNamespace = "urn:opcfoundation.org:UA:WotAggregation:SourceB";
    }
}
