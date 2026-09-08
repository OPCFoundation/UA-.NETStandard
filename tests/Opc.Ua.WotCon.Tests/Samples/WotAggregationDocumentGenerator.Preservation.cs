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
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Samples
{
    internal static partial class WotAggregationDocumentGenerator
    {
        private static UANodeSet WithBindingResidue(
            UANodeSet source,
            UANodeSet reconstructed,
            ArrayOf<SampleDocument> declarations,
            ArrayOf<SampleDocument> bound)
        {
            Dictionary<string, JsonObject> original = ParseRoots(declarations);
            Dictionary<string, JsonObject> enriched = ParseRoots(bound);
            var permitted = new Dictionary<string, List<JsonNode>>(StringComparer.Ordinal);
            foreach ((string resourceId, JsonObject root) in enriched)
            {
                JsonObject baseline = original[resourceId];
                AddIfChanged("/security", baseline["security"], root["security"]);
                AddIfChanged("/securityDefinitions", baseline["securityDefinitions"], root["securityDefinitions"]);
                AddIfChanged("/schemaDefinitions", baseline["schemaDefinitions"], root["schemaDefinitions"]);
                foreach (string mapName in s_affordanceMaps)
                {
                    if (root[mapName] is JsonObject map && baseline[mapName] is JsonObject originalMap)
                    {
                        AddMap("/" + mapName, originalMap, map);
                    }
                }
            }

            var extensions = new List<System.Xml.XmlElement>(source.Extensions ?? []);
            var originalExtensions = new List<System.Xml.XmlElement>(extensions);
            foreach (System.Xml.XmlElement extension in reconstructed.Extensions ?? [])
            {
                int originalIndex = originalExtensions.FindIndex(item => item.OuterXml == extension.OuterXml);
                if (originalIndex >= 0)
                {
                    originalExtensions.RemoveAt(originalIndex);
                    continue;
                }
                if (extension.LocalName != "WoTJsonResidue" ||
                    extension.NamespaceURI != WotNodeSetConverter.VocabularyNamespace ||
                    extension.GetAttribute("Version") != "1.0")
                {
                    throw new InvalidOperationException("Binding enrichment added an unexpected NodeSet extension.");
                }
                foreach (XmlNode child in extension.ChildNodes)
                {
                    if (child is not System.Xml.XmlElement member || member.LocalName != "Member" ||
                        member.NamespaceURI != WotNodeSetConverter.VocabularyNamespace ||
                        member.GetAttribute("Encoding") != "base64")
                    {
                        throw new InvalidOperationException("Binding enrichment added malformed routing residue.");
                    }
                    string pointer = member.GetAttribute("Pointer");
                    JsonNode? value = JsonNode.Parse(Convert.FromBase64String(member.InnerText));
                    if (!permitted.TryGetValue(pointer, out List<JsonNode>? candidates) ||
                        !candidates.Any(candidate => JsonNode.DeepEquals(candidate, value)))
                    {
                        throw new InvalidOperationException(
                            $"Binding enrichment added unaccounted residue at '{pointer}'.");
                    }
                }
                extensions.Add(extension);
            }

            // Retain every original source fact. Only the explicitly matched
            // routing metadata joins the expected model's extension inventory.
            return new UANodeSet
            {
                NamespaceUris = source.NamespaceUris,
                ServerUris = source.ServerUris,
                Models = source.Models,
                Aliases = source.Aliases,
                Extensions = extensions.Count == 0 ? null : [.. extensions],
                LastModified = source.LastModified,
                LastModifiedSpecified = source.LastModifiedSpecified,
                Items = source.Items
            };

            void AddMap(string pointer, JsonObject baseline, JsonObject map)
            {
                foreach ((string name, JsonNode? value) in map)
                {
                    if (value is not JsonObject affordance || baseline[name] is not JsonObject originalAffordance)
                    {
                        continue;
                    }
                    string path = pointer + "/" + EscapePointer(name);
                    foreach (string term in s_bindingResidueTerms)
                    {
                        AddIfChanged(path + "/" + EscapePointer(term), originalAffordance[term], affordance[term]);
                    }
                    if (affordance["properties"] is JsonObject children &&
                        originalAffordance["properties"] is JsonObject originalChildren)
                    {
                        AddMap(path + "/properties", originalChildren, children);
                    }
                }
            }

            void AddIfChanged(string pointer, JsonNode? baseline, JsonNode? value)
            {
                if (value is null || JsonNode.DeepEquals(baseline, value))
                {
                    return;
                }
                if (!permitted.TryGetValue(pointer, out List<JsonNode>? values))
                {
                    values = [];
                    permitted.Add(pointer, values);
                }
                values.Add(value);
            }
        }

        private static string EscapePointer(string token)
        {
            return token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        }

        private static readonly string[] s_affordanceMaps = ["properties", "actions", "events"];
        private static readonly string[] s_bindingResidueTerms =
        [
            "forms", "uav:mapToNodeId", "input",
            "uav:eventSelectClauses", "uav:conditionAction", "uav:actsOn",
            "uav:conditionType", "uav:conditionTypeId"
        ];
    }
}
