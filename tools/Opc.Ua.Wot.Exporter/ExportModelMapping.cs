// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter
{
    /// <summary>
    /// Adds publisher-facing mapping facts to the converter's lossless native projection.
    /// </summary>
    internal sealed partial class ExportModelMapping
    {
        /// <summary>
        /// Indexes the batch before deriving names so collisions are independent of file order.
        /// </summary>
        public ExportModelMapping(IEnumerable<UANodeSet> sources, ExportMetadata metadata, bool identifierSafeKeys)
        {
            m_metadata = metadata;
            m_identifierSafeKeys = identifierSafeKeys;
            foreach (UANodeSet source in sources)
            {
                foreach (UANode node in source.Items ?? [])
                {
                    if (node.NodeId is { Length: > 0 })
                    {
                        string identity = Portable(node.NodeId, source);
                        m_nodes.TryAdd(identity, new NativeNode(node, source, identity));
                    }
                }
            }
            foreach (string identity in Enumerable.Range(1, 29).Select(value => "i=" +
                value.ToString(CultureInfo.InvariantCulture)).Concat(s_standardTypeIds))
            {
                if (!m_nodes.ContainsKey(identity) && DeriveName(identity, []) is { } standardName &&
                    !m_nameOwners.TryAdd(standardName, identity))
                {
                    m_collisions.Add(standardName);
                }
            }
            foreach (NativeNode node in m_nodes.Values)
            {
                foreach (Reference reference in node.Node.References ?? [])
                {
                    string type = Portable(reference.ReferenceType, node.Source);
                    string target = Portable(reference.Value, node.Source);
                    if (IsOwnership(type, []))
                    {
                        string child = reference.IsForward ? target : node.Identity;
                        string parent = reference.IsForward ? node.Identity : target;
                        if (!m_parents.TryGetValue(child, out HashSet<string>? parents))
                        {
                            parents = new HashSet<string>(StringComparer.Ordinal);
                            m_parents.Add(child, parents);
                        }
                        parents.Add(parent);
                    }
                }
            }
            foreach (NativeNode node in m_nodes.Values)
            {
                var browseName = QualifiedName.Parse(node.Node.BrowseName);
                if (node.Node is UAVariable && browseName.NamespaceIndex == 0 && browseName.Name == "EURange" &&
                    m_parents.TryGetValue(node.Identity, out HashSet<string>? owners))
                {
                    foreach (string variableIdentity in owners)
                    {
                        if (!m_ranges.TryAdd(variableIdentity, node))
                        {
                            m_ranges[variableIdentity] = null;
                        }
                    }
                }
                string? name = DeriveName(node.Identity, []);
                if (name is null)
                {
                    continue;
                }
                m_names[node.Identity] = name;
                if (m_nameOwners.TryGetValue(name, out string? owner) && owner != node.Identity)
                {
                    m_collisions.Add(name);
                }
                else
                {
                    m_nameOwners[name] = node.Identity;
                }
            }
        }

        /// <summary>
        /// Retains readable definitions already expanded by the shared converter.
        /// </summary>
        public void AddDefinitions(WotDocumentSet documents)
        {
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                IndexKeys(entry.Document);
                foreach (WotDataTypeDefinitionSource source in
                    WotNodeSetConverter.ReadDataTypeDefinitions(entry.Document))
                {
                    JsonElement definition = source.Definition;
                    if (definition.TryGetProperty("uav:dataTypeId", out JsonElement id) &&
                        id.ValueKind == JsonValueKind.String)
                    {
                        m_definitions.TryAdd(CanonicalIdentity(id.GetString()!),
                            JsonNode.Parse(definition.GetRawText())!.AsObject());
                    }
                }
            }
        }

        /// <summary>
        /// Applies revision 1.1 export rules without walking native payloads or literal values.
        /// </summary>
        public ReadOnlyMemory<byte> Apply(
            ReadOnlyMemory<byte> json, string href, List<string> diagnostics)
        {
            JsonObject root = JsonNode.Parse(json.Span)!.AsObject();
            m_schemaNodes = 0;
            NativeNode? rootNode = null;
            if (String(root, "uav:id") is { } id && m_nodes.TryGetValue(CanonicalIdentity(id), out NativeNode? node))
            {
                rootNode = node;
                if (IsType(node.Node))
                {
                    root["uav:fullName"] = Name(node.Identity);
                    root["uav:typeName"] = Name(node.Identity);
                }
                else
                {
                    ApplyOptionalDeclarationName(root, node.Identity, href, diagnostics);
                    string? typeDefinition = TypeDefinition(node);
                    if (typeDefinition is not null)
                    {
                        root["uav:typeName"] = Name(typeDefinition);
                    }
                }
                JsonObject? metadata = m_metadata.Find(node.Identity);
                if (metadata?["isStandalone"] is { } standalone)
                {
                    if (node.Node is not UAObjectType || HasType(root, "uav:eventType"))
                    {
                        throw new FormatException($"isStandalone is not permitted on '{node.Identity}'.");
                    }
                    root["uav:isStandalone"] = standalone.DeepClone();
                }
            }
            if (root["uav:isStandalone"] is { } declaredStandalone &&
                (rootNode?.Node is not UAObjectType || HasType(root, "uav:eventType") ||
                    declaredStandalone.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)))
            {
                throw new FormatException("uav:isStandalone requires a Boolean on a non-event ObjectType TM root.");
            }
            foreach (string collection in s_affordanceMaps)
            {
                if (root[collection] is not JsonObject affordances)
                {
                    continue;
                }
                foreach ((string key, JsonNode? value) in affordances)
                {
                    if (value is not JsonObject affordance)
                    {
                        continue;
                    }
                    string pointer = "/" + collection + "/" + Escape(key);
                    if (String(affordance, "uav:id") is { } nativeId)
                    {
                        string identity = CanonicalIdentity(nativeId);
                        if (collection != "events")
                        {
                            affordance["uav:fullName"] = Name(identity);
                        }
                        else if (m_nodes.TryGetValue(identity, out NativeNode? eventNode) && !IsType(eventNode.Node))
                        {
                            ApplyOptionalDeclarationName(affordance, identity, href + "#" + pointer, diagnostics);
                            if (TypeDefinition(eventNode) is { } eventType)
                            {
                                affordance["uav:typeName"] = Name(eventType);
                            }
                        }
                        else
                        {
                            affordance["uav:typeName"] = Name(identity);
                        }
                        if (m_metadata.Find(identity)?["eventContractType"] is { } contracts)
                        {
                            affordance["uav:eventContractType"] = contracts.DeepClone();
                        }
                    }
                    if (collection == "properties")
                    {
                        ApplyEngineeringRange(affordance, href, pointer, diagnostics);
                        ApplySchema(affordance, pointer, true, href, diagnostics);
                    }
                    else
                    {
                        VisitSchemas(affordance, pointer, href, diagnostics);
                    }
                }
            }
            VisitRootSchemas(root, href, diagnostics);
            ApplyUnits(root, href, diagnostics);
            if (m_identifierSafeKeys)
            {
                RenameAffordances(root);
            }
            byte[] output = Encoding.UTF8.GetBytes(root.ToJsonString(s_jsonOptions));
            if (output.Length > 16 * 1024 * 1024)
            {
                throw new FormatException($"The enriched document '{href}' exceeds the 16 MiB JSON limit.");
            }
            return output;
        }

        /// <summary>
        /// Uses one canonical NamespaceUri-based spelling for metadata, identity ordering and lookup.
        /// </summary>
        public static string CanonicalIdentity(string identity)
        {
            if (!ExpandedNodeId.TryParse(identity, out ExpandedNodeId parsed) ||
                parsed.ServerIndex != 0 || parsed.InnerNodeId.NamespaceIndex != 0)
            {
                throw new FormatException($"A portable native NodeId is required: '{identity}'.");
            }
            string identifier = parsed.InnerNodeId.ToString();
            return string.IsNullOrEmpty(parsed.NamespaceUri) || parsed.NamespaceUri == Types.Namespaces.OpcUa
                ? identifier
                : "nsu=" + EscapeNamespace(parsed.NamespaceUri) + ";" + identifier;
        }

        /// <summary>
        /// Resolves aliases and namespace indexes through the owning NodeSet.
        /// </summary>
        private static string Portable(string? value, UANodeSet source)
        {
            string identity = value ?? "i=0";
            foreach (NodeIdAlias alias in source.Aliases ?? [])
            {
                if (alias.Alias == identity)
                {
                    identity = alias.Value!;
                    break;
                }
            }
            if (WotNodeSetAliases.Instance.TryResolve(identity, out string standard))
            {
                identity = standard;
            }
            if (identity.StartsWith("nsu=", StringComparison.Ordinal))
            {
                return CanonicalIdentity(identity);
            }
            var id = NodeId.Parse(identity);
            return id.NamespaceIndex == 0
                ? id.ToString()
                : CanonicalIdentity(new ExpandedNodeId(
                    id.WithNamespaceIndex(0), source.NamespaceUris[id.NamespaceIndex - 1]).ToString());
        }

        /// <summary>
        /// Requires an unambiguous publisher or derived name for a native identity.
        /// </summary>
        private string Name(string identity)
        {
            identity = CanonicalIdentity(identity);
            string? name = m_names.GetValueOrDefault(identity) ?? DeriveName(identity, []);
            if (name is null || m_collisions.Contains(name))
            {
                throw new FormatException(
                    $"An explicit, unambiguous name mapping is required for '{identity}'" +
                    (name is null ? "." : $"; unresolved reverse-DNS name collision '{name}'."));
            }
            return name;
        }

        /// <summary>
        /// Omits unavailable or colliding optional declaration names without failing unrelated type names.
        /// </summary>
        private void ApplyOptionalDeclarationName(
            JsonObject target, string identity, string location, List<string> diagnostics)
        {
            string? name = DeriveName(identity, []);
            if (name is null || m_collisions.Contains(name))
            {
                target.Remove("uav:fullName");
                if (name is not null)
                {
                    diagnostics.Add($"Unresolved reverse-DNS name collision '{name}' at {location}; " +
                        "the optional declaration name is omitted.");
                }
                return;
            }
            target["uav:fullName"] = name;
        }

        /// <summary>
        /// Derives names from native declaring paths without sanitizing or disambiguating them.
        /// </summary>
        private string? DeriveName(string identity, HashSet<string> path)
        {
            if (m_metadata.Find(identity) is { } metadata && String(metadata, "name") is { } explicitName)
            {
                return explicitName;
            }
            if (!path.Add(identity) || path.Count > 128)
            {
                return null;
            }
            try
            {
                if (!m_nodes.TryGetValue(identity, out NativeNode? node))
                {
                    string? standard = StandardName(identity);
                    return standard is null ? null : "org.opcfoundation.UA." + standard;
                }
                string? local = QualifiedName.Parse(node.Node.BrowseName).Name;
                if (string.IsNullOrEmpty(local) || !ExportMetadata.IsName(local) ||
                    local.Contains('.', StringComparison.Ordinal))
                {
                    return null;
                }
                if (!IsType(node.Node))
                {
                    if (!m_parents.TryGetValue(identity, out HashSet<string>? parents) || parents.Count != 1)
                    {
                        return null;
                    }
                    string? owner = DeriveName(parents.Single(), path);
                    return owner is null ? null : owner + "." + local;
                }
                string namespaceUri = ExpandedNodeId.Parse(identity).NamespaceUri ?? Types.Namespaces.OpcUa;
                if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme is not ("http" or "https") || uri.HostNameType != UriHostNameType.Dns ||
                    uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    namespaceUri.Split('/')[2].Contains(':', StringComparison.Ordinal) || uri.Host.EndsWith('.') ||
                    uri.Host.Any(character => character > 127))
                {
                    return null;
                }
                string name = string.Join('.', uri.Host.ToLowerInvariant().Split('.').Reverse()) +
                    "." + string.Join('.', uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Append(local));
                return ExportMetadata.IsName(name) ? name : null;
            }
            finally
            {
                path.Remove(identity);
            }
        }

        /// <summary>
        /// Recognizes native ownership references, including locally declared reference subtypes.
        /// </summary>
        private bool IsOwnership(string identity, HashSet<string> visited)
        {
            if (identity is "i=46" or "i=47" or "i=49")
            {
                return true;
            }
            if (!visited.Add(identity) || visited.Count > 128 ||
                !m_nodes.TryGetValue(identity, out NativeNode? node))
            {
                return false;
            }
            return node.Node.References?.Any(reference =>
                !reference.IsForward && Portable(reference.ReferenceType, node.Source) == "i=45" &&
                IsOwnership(Portable(reference.Value, node.Source), visited)) == true;
        }

        /// <summary>
        /// Resolves an instance's declared native type without inferring one.
        /// </summary>
        private static string? TypeDefinition(NativeNode node)
        {
            Reference? reference = node.Node.References?.FirstOrDefault(reference =>
                reference.IsForward && Portable(reference.ReferenceType, node.Source) == "i=40");
            return reference is null ? null : Portable(reference.Value, node.Source);
        }

        /// <summary>
        /// Identifies native type nodes rather than instance or declaration nodes.
        /// </summary>
        private static bool IsType(UANode node)
        {
            return node is UAObjectType or UAVariableType or UADataType or UAReferenceType;
        }

        /// <summary>
        /// Supplies known standard names when the base NodeSet is not part of the input.
        /// </summary>
        private static string? StandardName(string identity)
        {
            return identity switch
            {
                "i=22" => "Structure",
                "i=24" => "BaseDataType",
                "i=26" => "Number",
                "i=27" => "Integer",
                "i=28" => "UInteger",
                "i=29" => "Enumeration",
                "i=50" => "Decimal",
                "i=58" => "BaseObjectType",
                "i=62" => "BaseVariableType",
                "i=63" => "BaseDataVariableType",
                "i=68" => "PropertyType",
                "i=2041" => "BaseEventType",
                "i=2782" => "ConditionType",
                "i=2915" => "AlarmConditionType",
                "i=884" => "Range",
                "i=887" => "EUInformation",
                "i=296" => "Argument",
                "i=12756" => "Union",
                _ => NodeId.TryParse(identity, out NodeId id) && id.NamespaceIndex == 0 &&
                    id.TryGetValue(out uint numeric) && numeric is > 0 and <= 25
                    ? TypeInfo.GetBuiltInType(id).ToString() : null
            };
        }

        /// <summary>
        /// Encodes the namespace URI once for canonical identity ordering.
        /// </summary>
        private static string EscapeNamespace(string namespaceUri)
        {
            var result = new StringBuilder();
            foreach (byte value in Encoding.UTF8.GetBytes(namespaceUri))
            {
                char character = (char)value;
                if (char.IsAsciiLetterOrDigit(character) ||
                    "-._~:/?#[]@!$&'()*+,=".Contains(character, StringComparison.Ordinal))
                {
                    result.Append(character);
                }
                else
                {
                    result.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// Reads an optional JSON string without coercing other value kinds.
        /// </summary>
        private static string? String(JsonObject node, string member)
        {
            return node[member] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        }

        /// <summary>
        /// Checks both compact JSON-LD type spellings.
        /// </summary>
        private static bool HasType(JsonObject node, string type)
        {
            return String(node, "@type") == type ||
                (node["@type"] is JsonArray types && types.Any(value => value?.GetValue<string>() == type));
        }

        /// <summary>
        /// Escapes one plain RFC 6901 pointer token.
        /// </summary>
        private static string Escape(string token)
        {
            return token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        }

        /// <summary>
        /// Retains native source context without rebasing its namespace table.
        /// </summary>
        private sealed record NativeNode(UANode Node, UANodeSet Source, string Identity);

        /// <summary>Names the three interaction maps, not nested structure fields.</summary>
        private static readonly string[] s_affordanceMaps = ["properties", "actions", "events"];

        /// <summary>Names standard types whose numeric identifiers are outside the built-in type range.</summary>
        private static readonly string[] s_standardTypeIds =
            ["i=50", "i=58", "i=62", "i=63", "i=68", "i=2041", "i=2782", "i=2915",
                "i=884", "i=887", "i=296", "i=12756"];

        /// <summary>Writes JSON without reflection-based type metadata.</summary>
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        /// <summary>Holds explicit per-identity assertions.</summary>
        private readonly ExportMetadata m_metadata;

        /// <summary>Selects optional identifier-safe affordance naming.</summary>
        private readonly bool m_identifierSafeKeys;

        /// <summary>Indexes all successfully read input nodes.</summary>
        private readonly Dictionary<string, NativeNode> m_nodes = new(StringComparer.Ordinal);

        /// <summary>Records native declaration owners in either reference direction.</summary>
        private readonly Dictionary<string, HashSet<string>> m_parents = new(StringComparer.Ordinal);

        /// <summary>Indexes established native engineering ranges by the owning Variable.</summary>
        private readonly Dictionary<string, NativeNode?> m_ranges = new(StringComparer.Ordinal);

        /// <summary>Caches derived or explicitly mapped names.</summary>
        private readonly Dictionary<string, string> m_names = new(StringComparer.Ordinal);

        /// <summary>Tracks name claims across the complete input batch.</summary>
        private readonly Dictionary<string, string> m_nameOwners = new(StringComparer.Ordinal);

        /// <summary>Suppresses every unresolved colliding name.</summary>
        private readonly HashSet<string> m_collisions = new(StringComparer.Ordinal);

        /// <summary>Uses the shared converter's readable datatype definitions.</summary>
        private readonly Dictionary<string, JsonObject> m_definitions = new(StringComparer.Ordinal);

        /// <summary>Bounds expansion of repeated nested definitions within one exported document.</summary>
        private int m_schemaNodes;
    }
}
