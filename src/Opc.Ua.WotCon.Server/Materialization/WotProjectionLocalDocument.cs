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
using System.IO;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Creates the ordinary TD or TM input for materializing only the
    /// affordances locally defined by a projection document.
    /// </summary>
    internal static class WotProjectionLocalDocument
    {
        /// <summary>
        /// Creates a document containing the projection's local affordances and
        /// ordinary document metadata, excluding projection selection controls.
        /// </summary>
        /// <param name="root">The projection document's JSON root.</param>
        /// <param name="localAffordances">The affordances defined by the projection.</param>
        /// <param name="rootNodeId">The distinct NodeId for the local affordance root.</param>
        /// <returns>The serialized ordinary document.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="rootNodeId"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A parsed local affordance is absent from its source map.
        /// </exception>
        public static ByteString Create(
            JsonElement root,
            ArrayOf<WotProjectionLocalAffordance> localAffordances,
            string rootNodeId)
        {
            if (rootNodeId is null)
            {
                throw new ArgumentNullException(nameof(rootNodeId));
            }

            Dictionary<string, JsonElement> properties = Definitions(
                localAffordances, WotAffordanceKind.Property);
            Dictionary<string, JsonElement> actions = Definitions(
                localAffordances, WotAffordanceKind.Action);
            Dictionary<string, JsonElement> events = Definitions(
                localAffordances, WotAffordanceKind.Event);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "@type":
                            WriteDocumentTypes(writer, property.Value);
                            break;
                        case "properties":
                            WriteAffordances(writer, property.Value, "properties", properties);
                            break;
                        case "actions":
                            WriteAffordances(writer, property.Value, "actions", actions);
                            break;
                        case "events":
                            WriteAffordances(writer, property.Value, "events", events);
                            break;
                        case "links":
                            WriteNonOrganizingLinks(writer, property.Value);
                            break;
                        case "uav:id":
                        case "uav:projects":
                        case "uav:scenario":
                            break;
                        default:
                            property.WriteTo(writer);
                            break;
                    }
                }
                ThrowIfLocalAffordancesRemain(properties, "properties");
                ThrowIfLocalAffordancesRemain(actions, "actions");
                ThrowIfLocalAffordancesRemain(events, "events");
                writer.WriteString("uav:id", rootNodeId);
                writer.WriteEndObject();
            }
            return ByteString.From(stream.ToArray());
        }

        private static Dictionary<string, JsonElement> Definitions(
            ArrayOf<WotProjectionLocalAffordance> localAffordances,
            WotAffordanceKind kind)
        {
            var definitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (WotProjectionLocalAffordance local in localAffordances)
            {
                if (local.AffordanceKind == kind)
                {
                    definitions.Add(local.Name, local.Definition);
                }
            }
            return definitions;
        }

        private static void WriteDocumentTypes(Utf8JsonWriter writer, JsonElement types)
        {
            if (types.ValueKind == JsonValueKind.Array)
            {
                bool wroteType = false;
                foreach (JsonElement type in types.EnumerateArray())
                {
                    if (type.ValueKind == JsonValueKind.String &&
                        string.Equals(type.GetString(), "uav:projection", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (!wroteType)
                    {
                        writer.WritePropertyName("@type");
                        writer.WriteStartArray();
                        wroteType = true;
                    }
                    type.WriteTo(writer);
                }
                if (wroteType)
                {
                    writer.WriteEndArray();
                }
                return;
            }
            if (types.ValueKind != JsonValueKind.String ||
                !string.Equals(types.GetString(), "uav:projection", StringComparison.Ordinal))
            {
                writer.WritePropertyName("@type");
                types.WriteTo(writer);
            }
        }

        private static void WriteAffordances(
            Utf8JsonWriter writer,
            JsonElement source,
            string propertyName,
            Dictionary<string, JsonElement> definitions)
        {
            if (definitions.Count == 0)
            {
                return;
            }
            if (source.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"The projection's '{propertyName}' map is not an object.");
            }

            writer.WritePropertyName(propertyName);
            writer.WriteStartObject();
            foreach (JsonProperty affordance in source.EnumerateObject())
            {
                if (definitions.Remove(affordance.Name))
                {
                    affordance.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            if (definitions.Count != 0)
            {
                throw new InvalidOperationException(
                    $"A parsed local affordance is absent from the '{propertyName}' map.");
            }
        }

        private static void ThrowIfLocalAffordancesRemain(
            Dictionary<string, JsonElement> definitions,
            string propertyName)
        {
            if (definitions.Count != 0)
            {
                throw new InvalidOperationException(
                    $"The projection's '{propertyName}' map is absent.");
            }
        }

        private static void WriteNonOrganizingLinks(Utf8JsonWriter writer, JsonElement links)
        {
            if (links.ValueKind != JsonValueKind.Array)
            {
                writer.WritePropertyName("links");
                links.WriteTo(writer);
                return;
            }

            bool wroteLinks = false;
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.ValueKind == JsonValueKind.Object &&
                    link.TryGetProperty("rel", out JsonElement rel) &&
                    rel.ValueKind == JsonValueKind.String &&
                    string.Equals(rel.GetString(), "ua:Organizes", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!wroteLinks)
                {
                    writer.WritePropertyName("links");
                    writer.WriteStartArray();
                    wroteLinks = true;
                }
                link.WriteTo(writer);
            }
            if (wroteLinks)
            {
                writer.WriteEndArray();
            }
        }
    }
}
