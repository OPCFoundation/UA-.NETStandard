// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot.Exporter
{
    internal sealed partial class ExportModelMapping
    {
        /// <summary>
        /// Rewrites a model-relative reference using keys allocated before registry addressing.
        /// </summary>
        public string RewriteFragment(ExpandedNodeId identity, string fragment)
        {
            string key = CanonicalIdentity(identity.ToString());
            return m_keyMaps.TryGetValue(key, out Dictionary<string, string>? names)
                ? RewritePointer(fragment, names) : fragment;
        }

        /// <summary>
        /// Allocates keys before registry references are written.
        /// </summary>
        private void IndexKeys(WotDocument document)
        {
            if (!m_identifierSafeKeys ||
                !document.RootElement.TryGetProperty("uav:id", out JsonElement rootId) ||
                rootId.ValueKind != JsonValueKind.String)
            {
                return;
            }
            string identity = CanonicalIdentity(rootId.GetString()!);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string collection in s_affordanceMaps)
            {
                if (!document.RootElement.TryGetProperty(collection, out JsonElement map) ||
                    map.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var occurrences = new List<(string Identity, string Candidate, string Original)>();
                foreach (JsonProperty property in map.EnumerateObject())
                {
                    if (!property.Value.TryGetProperty("uav:id", out JsonElement nativeId) ||
                        nativeId.ValueKind != JsonValueKind.String)
                    {
                        throw new FormatException($"No native identity for identifier-safe key '{property.Name}'.");
                    }
                    string childIdentity = CanonicalIdentity(nativeId.GetString()!);
                    if (!m_nodes.TryGetValue(childIdentity, out NativeNode? child))
                    {
                        throw new FormatException($"No native naming metadata for '{childIdentity}'.");
                    }
                    string candidate = string.IsNullOrEmpty(child.Node.SymbolicName)
                        ? QualifiedName.Parse(child.Node.BrowseName).Name ?? string.Empty
                        : child.Node.SymbolicName;
                    occurrences.Add((childIdentity, Identifier(candidate), property.Name));
                }
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach ((string _, string candidate, string original) in occurrences
                    .OrderBy(item => item.Identity, CodePointComparer.Instance)
                    .ThenBy(item => item.Candidate, CodePointComparer.Instance)
                    .ThenBy(item => "/" + collection + "/" + Escape(item.Original), CodePointComparer.Instance))
                {
                    string key = candidate;
                    int suffix = 2;
                    while (!used.Add(key))
                    {
                        key = candidate + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                        suffix++;
                    }
                    names.Add("/" + collection + "/" + Escape(original),
                        "/" + collection + "/" + Escape(key));
                }
            }
            m_keyMaps[identity] = names;
        }

        /// <summary>
        /// Applies preallocated keys after updating same-document affordance references.
        /// </summary>
        private void RenameAffordances(JsonObject root)
        {
            if (String(root, "uav:id") is not { } nativeId ||
                !m_keyMaps.TryGetValue(CanonicalIdentity(nativeId), out Dictionary<string, string>? names))
            {
                return;
            }
            RewriteReferences(root, names);
            foreach (string collection in s_affordanceMaps)
            {
                if (root[collection] is not JsonObject map)
                {
                    continue;
                }
                var renamed = new JsonObject();
                foreach ((string key, JsonNode? value) in map)
                {
                    string pointer = names["/" + collection + "/" + Escape(key)];
                    string name = pointer[(pointer.LastIndexOf('/') + 1)..];
                    renamed.Add(name, value?.DeepClone());
                }
                root[collection] = renamed;
            }
        }

        /// <summary>
        /// Visits reference-bearing positions without changing literal payload values.
        /// </summary>
        private static void RewriteReferences(JsonObject node, Dictionary<string, string> names)
        {
            foreach ((string member, JsonNode? value) in node.ToArray())
            {
                if (member is "tm:ref" or "$ref" or "uav:withUnit" or "uav:actsOn" or "uav:resolvedFrom" &&
                    String(node, member) is { } reference)
                {
                    if (member == "uav:actsOn" && !reference.StartsWith('/'))
                    {
                        string pointer = "/events/" + Escape(reference);
                        node[member] = names.TryGetValue(pointer, out string? target)
                            ? target["/events/".Length..] : reference;
                    }
                    else
                    {
                        node[member] = RewritePointer(reference, names);
                    }
                }
                else if (member is "tm:optional" or "uav:instanceOfDeclaration" && value is JsonArray optional)
                {
                    for (int i = 0; i < optional.Count; i++)
                    {
                        optional[i] = RewritePointer(optional[i]!.GetValue<string>(), names);
                    }
                }
                else if (member is "properties" or "actions" or "events" or "schemaDefinitions" or
                    "uriVariables" or "$defs" or "definitions" or "patternProperties" && value is JsonObject map)
                {
                    foreach (JsonObject child in map.Select(entry => entry.Value).OfType<JsonObject>())
                    {
                        RewriteReferences(child, names);
                    }
                }
                else if (s_schemaMembers.Contains(member, StringComparer.Ordinal) && value is JsonObject child)
                {
                    RewriteReferences(child, names);
                }
                else if (member is "links" or "forms" or "uav:eventSelectClauses" ||
                    s_schemaArrays.Contains(member, StringComparer.Ordinal))
                {
                    if (value is JsonArray children)
                    {
                        foreach (JsonObject item in children.OfType<JsonObject>())
                        {
                            if (member == "links" && String(item, "href") is { } href)
                            {
                                item["href"] = RewritePointer(href, names);
                            }
                            RewriteReferences(item, names);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Changes an affordance pointer prefix while retaining any nested schema path.
        /// </summary>
        private static string RewritePointer(string reference, Dictionary<string, string> names)
        {
            bool fragment = reference.StartsWith('#');
            string pointer = fragment ? Uri.UnescapeDataString(reference[1..]) : reference;
            foreach ((string source, string destination) in names)
            {
                if (pointer == source || pointer.StartsWith(source + "/", StringComparison.Ordinal))
                {
                    return (fragment ? "#" : string.Empty) + destination + pointer[source.Length..];
                }
            }
            return reference;
        }

        /// <summary>
        /// Applies the ASCII identifier-key transformation.
        /// </summary>
        private static string Identifier(string source)
        {
            var result = new StringBuilder();
            bool replacing = false;
            foreach (char character in source)
            {
                bool allowed = char.IsAsciiLetterOrDigit(character) || character == '_';
                if (allowed)
                {
                    result.Append(character);
                }
                else if (!replacing)
                {
                    result.Append('_');
                }
                replacing = !allowed;
            }
            if (result.Length == 0 || !char.IsAsciiLetter(result[0]))
            {
                result.Insert(0, "A_");
            }
            return result.ToString();
        }

        /// <summary>
        /// Compares Unicode scalar values rather than UTF-16 code units.
        /// </summary>
        private sealed class CodePointComparer : IComparer<string>
        {
            /// <summary>
            /// Gets the stateless comparer.
            /// </summary>
            public static CodePointComparer Instance { get; } = new();

            /// <inheritdoc/>
            public int Compare(string? x, string? y)
            {
                if (x is null || y is null)
                {
                    return string.CompareOrdinal(x, y);
                }
                StringRuneEnumerator left = x.EnumerateRunes();
                StringRuneEnumerator right = y.EnumerateRunes();
                while (left.MoveNext())
                {
                    if (!right.MoveNext())
                    {
                        return 1;
                    }
                    int difference = left.Current.Value.CompareTo(right.Current.Value);
                    if (difference != 0)
                    {
                        return difference;
                    }
                }
                return right.MoveNext() ? -1 : 0;
            }
        }

        /// <summary>Maps original complete-affordance pointers to emitted pointers for each native root.</summary>
        private readonly Dictionary<string, Dictionary<string, string>> m_keyMaps = new(StringComparer.Ordinal);
    }
}
