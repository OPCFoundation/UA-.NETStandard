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

#pragma warning disable CA1307, CA1845, CA1846, CA1865
// TODO: remove when all TFMs agree on the preferred string slicing and single-character overloads.

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Aas.WoT
{
    /// <summary>
    /// Projects AAS environments to Annex F Thing Description bundles and reads them back.
    /// </summary>
    public static class AasWotBridge
    {
        /// <summary>
        /// Projects an AAS environment into one Thing Description per projected OPC UA Object.
        /// </summary>
        /// <param name="environment">The source environment.</param>
        /// <param name="options">Optional projection settings.</param>
        /// <returns>The generated publication bundle.</returns>
        public static AasWotProjectionBundle Project(
            AasEnvironment environment,
            AasWotBridgeOptions? options = null)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            options ??= new AasWotBridgeOptions();
            AasMaterializationResult materialized = AasEnvironmentMaterializer.Materialize(environment);
            var diagnostics = new List<AasWotBridgeDiagnostic>();
            foreach (AasMaterializationDiagnostic diagnostic in materialized.Diagnostics)
            {
                diagnostics.Add(new AasWotBridgeDiagnostic(
                    diagnostic.Severity == AasMaterializationDiagnosticSeverity.Error
                        ? AasWotBridgeDiagnosticSeverity.Error
                        : AasWotBridgeDiagnosticSeverity.Warning,
                    diagnostic.Code.ToString(),
                    diagnostic.Message));
            }

            Dictionary<string, UANode> nodes = Index(materialized.NodeSet);
            var documents = new List<string>();
            if (materialized.NodeSet.Items is not null)
            {
                foreach (UANode node in materialized.NodeSet.Items)
                {
                    if (node is UAObject && !IsEnvironmentNode(node))
                    {
                        documents.Add(WriteThingDescription(materialized.NodeSet, node, nodes, options));
                    }
                }
            }

            return new AasWotProjectionBundle(
                new ArrayOf<string>(documents.ToArray()),
                new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
        }

        /// <summary>
        /// Reads an AAS-carrying Thing Description publication bundle into an OPC UA NodeSet.
        /// </summary>
        /// <param name="documents">The Thing Description documents.</param>
        /// <returns>The materialized NodeSet and diagnostics.</returns>
        public static AasWotReadResult Read(ArrayOf<string> documents)
        {
            var diagnostics = new List<AasWotBridgeDiagnostic>();
            if (documents.Count == 0)
            {
                diagnostics.Add(Error("EmptyBundle", "The publication bundle does not contain a Thing Description."));
                return new AasWotReadResult(null, new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
            }

            var parsed = new List<JsonDocument>();
            try
            {
                for (int ii = 0; ii < documents.Count; ii++)
                {
                    JsonDocument document = JsonDocument.Parse(documents[ii]);
                    parsed.Add(document);
                    ValidateDocument(document.RootElement, diagnostics);
                }

                if (HasErrors(diagnostics))
                {
                    return new AasWotReadResult(null, new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
                }

                return new AasWotReadResult(
                    SynthesizeNodeSet(parsed.Select(static document => document.RootElement).ToArray(), diagnostics),
                    new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
            }
            catch (JsonException ex)
            {
                diagnostics.Add(Error("MalformedJson", "A Thing Description is not valid JSON: " + ex.Message));
                return new AasWotReadResult(null, new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
            }
            catch (FormatException ex)
            {
                diagnostics.Add(Error("MalformedNodeSet", ex.Message));
                return new AasWotReadResult(null, new ArrayOf<AasWotBridgeDiagnostic>(diagnostics.ToArray()));
            }
            finally
            {
                for (int ii = 0; ii < parsed.Count; ii++)
                {
                    parsed[ii].Dispose();
                }
            }
        }

        /// <summary>
        /// Resolves the Annex F.6 type binding carried by a Thing Description.
        /// </summary>
        /// <param name="document">The document root.</param>
        /// <returns>The resolved ObjectType NodeId, or <c>i=58</c> when neither form is present.</returns>
        public static string ResolveTypeBinding(JsonElement document)
        {
            if (!TryResolveTypeBinding(document, out string? nodeId, out string? error))
            {
                throw new FormatException(error ?? "The type binding is invalid.");
            }
            return nodeId!;
        }

        private static string WriteThingDescription(
            UANodeSet nodeSet,
            UANode node,
            IReadOnlyDictionary<string, UANode> nodes,
            AasWotBridgeOptions options)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                string portableNodeId = PortableNodeId(nodeSet, node.NodeId);
                string documentId = options.DocumentBaseIri + Base64Url(portableNodeId);
                string title = FirstText(node.DisplayName) ?? BrowseNameLocal(node.BrowseName) ?? "AAS Object";
                string typeId = TypeDefinition(node) ?? "i=58";
                string portableTypeId = PortableNodeId(nodeSet, typeId);
                string? typeName = AasWotTypeTable.NameFromNodeId(portableTypeId);

                writer.WriteStartObject();
                writer.WritePropertyName("@context");
                writer.WriteStartArray();
                writer.WriteStringValue("https://www.w3.org/2022/wot/td/v1.1");
                writer.WriteStringValue("../../aas.context.jsonld");
                writer.WriteStringValue("../../tools/jsonld/vendor/opc-ua-wot-binding.context.jsonld");
                writer.WriteStartObject();
                writer.WriteString("id", "@id");
                writer.WriteString("i4aas", Opc.Ua.Aas.V3.Namespaces.AasV3);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WritePropertyName("@type");
                writer.WriteStartArray();
                writer.WriteStringValue("Thing");
                writer.WriteStringValue("uav:object");
                WriteAasClassType(writer, typeName);
                if (typeName is not null)
                {
                    writer.WriteStringValue("i4aas:" + typeName);
                }
                writer.WriteEndArray();
                writer.WriteString("id", ReadStringProperty(nodes, node, "Id") ?? documentId);
                writer.WriteString("title", title);
                writer.WriteString("uav:id", portableNodeId);
                writer.WriteString("uav:browseName", PortableBrowseName(nodeSet, node.BrowseName));
                WriteAasTerms(writer, nodes, node);
                WriteContainmentTerms(writer, nodeSet, node);
                writer.WritePropertyName("forms");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("href", FormHref(options.Endpoint, portableNodeId));
                writer.WriteString("contentType", "application/octet-stream");
                writer.WriteString("op", "readallproperties");
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WritePropertyName("links");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("rel", "self");
                writer.WriteString("href", documentId);
                writer.WriteString("type", "application/td+json");
                writer.WriteEndObject();
                writer.WriteStartObject();
                writer.WriteString("rel", "ua:HasTypeDefinition");
                writer.WriteString("href", portableTypeId);
                writer.WriteEndObject();
                WriteContainmentLinks(writer, nodeSet, node);
                WriteParentLinks(writer, nodeSet, node);
                writer.WriteEndArray();
                writer.WriteStartObject("securityDefinitions");
                writer.WriteStartObject("nosec_sc");
                writer.WriteString("scheme", "nosec");
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteString("security", "nosec_sc");
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static UANodeSet SynthesizeNodeSet(
            IReadOnlyList<JsonElement> documents,
            List<AasWotBridgeDiagnostic> diagnostics)
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = [Opc.Ua.Aas.V3.Namespaces.AasV3],
                Models = [new ModelTableEntry { ModelUri = Opc.Ua.Aas.V3.Namespaces.AasV3 }]
            };
            var items = new List<UANode>
            {
                new UAObject
                {
                    NodeId = "ns=1;s=i4aas3:ENV",
                    BrowseName = "1:AASEnvironment",
                    DisplayName = [new Opc.Ua.Export.LocalizedText { Value = "AASEnvironment" }],
                    References = [new Reference { ReferenceType = "HasTypeDefinition", IsForward = true, Value = "ns=1;i=1010" }]
                }
            };
            var byId = new Dictionary<string, UAObject>(StringComparer.Ordinal);
            foreach (JsonElement document in documents)
            {
                if (!TryGetString(document, "uav:id", out string? portableId) || string.IsNullOrEmpty(portableId))
                {
                    diagnostics.Add(new AasWotBridgeDiagnostic(
                        AasWotBridgeDiagnosticSeverity.Error,
                        "MissingNodeId",
                        "AAS WoT documents must carry uav:id; browse-path fallback is not clause 6.1.3."));
                    continue;
                }

                string nodeId = NodeSetNodeId(portableId);
                string browseName = TryGetString(document, "uav:browseName", out string? browse)
                    ? NodeSetBrowseName(browse!)
                    : "1:" + BrowseNameLocalFromPortable(portableId);
                string typeId = ResolveTypeBinding(document);
                var node = new UAObject
                {
                    NodeId = nodeId,
                    BrowseName = browseName,
                    DisplayName = [new Opc.Ua.Export.LocalizedText { Value = BrowseNameLocal(browseName) }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasTypeDefinition",
                            IsForward = true,
                            Value = NodeSetNodeId(typeId)
                        }
                    ]
                };
                byId[portableId] = node;
                items.Add(node);
            }

            foreach (JsonElement document in documents)
            {
                if (!TryGetString(document, "uav:id", out string? source) || source is null ||
                    !byId.TryGetValue(source, out UAObject? sourceNode) ||
                    !document.TryGetProperty("links", out JsonElement links) ||
                    links.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement link in links.EnumerateArray())
                {
                    if (!TryGetString(link, "rel", out string? rel) ||
                        !IsContainmentRel(rel) ||
                        !TryGetString(link, "uav:refId", out string? refId) ||
                        !TryGetString(link, "href", out _))
                    {
                        continue;
                    }

                    string? targetId = FindTargetByLink(documents, link);
                    if (targetId is null || !byId.TryGetValue(targetId, out UAObject? targetNode))
                    {
                        continue;
                    }

                    string referenceType = string.Equals(refId, "i=49", StringComparison.Ordinal)
                        ? "HasOrderedComponent"
                        : "HasComponent";
                    AddReference(sourceNode, referenceType, targetNode.NodeId, true);
                    AddReference(targetNode, referenceType, sourceNode.NodeId, false);
                    targetNode.ParentNodeId = sourceNode.NodeId;
                }
            }

            foreach (UAObject node in byId.Values)
            {
                if (node.References is null ||
                    !node.References.Any(static reference => !reference.IsForward && IsHierarchical(reference.ReferenceType)))
                {
                    AddReference((UAObject)items[0], "Organizes", node.NodeId, true);
                    AddReference(node, "Organizes", items[0].NodeId, false);
                    node.ParentNodeId = items[0].NodeId;
                }
            }

            nodeSet.Items = items.ToArray();
            return nodeSet;
        }

        private static void ValidateDocument(JsonElement document, List<AasWotBridgeDiagnostic> diagnostics)
        {
            if (!TryResolveTypeBinding(document, out _, out string? error))
            {
                diagnostics.Add(Error("TypeBindingMismatch", error ?? "The type binding is invalid."));
            }
            if (!TryGetString(document, "uav:id", out _))
            {
                diagnostics.Add(new AasWotBridgeDiagnostic(
                    AasWotBridgeDiagnosticSeverity.Error,
                    "MissingNodeId",
                    "AAS WoT documents must carry uav:id; browse-path fallback is not clause 6.1.3."));
            }
        }

        private static bool TryResolveTypeBinding(JsonElement document, out string? nodeId, out string? error)
        {
            string? compact = null;
            if (document.TryGetProperty("@type", out JsonElement types))
            {
                foreach (string token in ReadTokens(types))
                {
                    if (AasWotTypeTable.TryGetNodeId(token, out string? typeId))
                    {
                        compact = typeId;
                    }
                }
            }

            string? linkForm = null;
            if (document.TryGetProperty("links", out JsonElement links) && links.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement link in links.EnumerateArray())
                {
                    if (TryGetString(link, "rel", out string? rel) &&
                        string.Equals(rel, "ua:HasTypeDefinition", StringComparison.Ordinal) &&
                        TryGetString(link, "href", out string? href))
                    {
                        linkForm = NormalizeTypeNodeId(href);
                    }
                }
            }

            if (compact is not null && linkForm is not null &&
                !string.Equals(compact, linkForm, StringComparison.Ordinal))
            {
                nodeId = null;
                error = "The compact @type binding resolves to " + compact +
                    " but the ua:HasTypeDefinition link resolves to " + linkForm + ".";
                return false;
            }

            nodeId = compact ?? linkForm ?? "i=58";
            error = null;
            return true;
        }

        private static void WriteAasTerms(
            Utf8JsonWriter writer,
            IReadOnlyDictionary<string, UANode> nodes,
            UANode node)
        {
            WriteOptionalLiteral(writer, "aas:Identifiable/id", ReadStringProperty(nodes, node, "Id"));
            WriteOptionalLiteral(writer, "aas:Referable/idShort", ReadStringProperty(nodes, node, "IdShort"));
            WriteOptionalLiteral(writer, "aas:Referable/category", ReadStringProperty(nodes, node, "Category"));
            WriteOptionalLiteral(writer, "aas:Property/value", ReadStringProperty(nodes, node, "Value"));
            string? modelType = ReadStringProperty(nodes, node, "ModelType");
            if (modelType is not null)
            {
                writer.WriteString("aas:modelType", modelType);
            }
        }

        private static void WriteOptionalLiteral(Utf8JsonWriter writer, string name, string? value)
        {
            if (value is null)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WriteString("@type", "http://www.w3.org/2001/XMLSchema#string");
            writer.WriteString("@value", value);
            writer.WriteEndObject();
        }

        private static void WriteAasClassType(Utf8JsonWriter writer, string? typeName)
        {
            string? aasType = AasWotTypeTable.AasTypeFromObjectType(typeName);
            if (aasType is not null)
            {
                writer.WriteStringValue("aas:" + aasType);
            }
        }

        private static void WriteContainmentTerms(Utf8JsonWriter writer, UANodeSet nodeSet, UANode node)
        {
            List<string> parents = [];
            List<string> children = [];
            if (node.References is not null)
            {
                foreach (Reference reference in node.References)
                {
                    if (reference.Value is null ||
                        !IsHierarchical(reference.ReferenceType) ||
                        IsEnvironmentNodeId(reference.Value))
                    {
                        continue;
                    }
                    if (reference.IsForward)
                    {
                        children.Add(PortableNodeId(nodeSet, reference.Value));
                    }
                    else
                    {
                        parents.Add(PortableNodeId(nodeSet, reference.Value));
                    }
                }
            }
            WriteStringArray(writer, "uav:componentOf", parents);
            WriteStringArray(writer, "uav:hasComponent", children);
            string? index = ReadStringProperty(Index(nodeSet), node, "Index");
            if (index is not null && uint.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out uint value))
            {
                writer.WriteNumber("uav:index", value);
            }
        }

        private static void WriteContainmentLinks(Utf8JsonWriter writer, UANodeSet nodeSet, UANode node)
        {
            if (node.References is null)
            {
                return;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.Value is null || !reference.IsForward || !IsHierarchical(reference.ReferenceType))
                {
                    continue;
                }
                writer.WriteStartObject();
                string referenceType = string.Equals(reference.ReferenceType, "HasOrderedComponent", StringComparison.Ordinal)
                    ? "ua:HasOrderedComponent"
                    : "ua:HasComponent";
                writer.WriteString("rel", referenceType);
                writer.WriteString("href", SiblingDocumentHref(nodeSet, reference.Value));
                writer.WriteString("type", "application/td+json");
                writer.WriteString(
                    "uav:refId",
                    string.Equals(reference.ReferenceType, "HasOrderedComponent", StringComparison.Ordinal)
                        ? "i=49"
                        : "i=47");
                writer.WriteString("uav:refName", BrowseNameLocalFromReference(nodeSet, reference.Value));
                writer.WriteEndObject();
            }
        }

        private static void WriteParentLinks(Utf8JsonWriter writer, UANodeSet nodeSet, UANode node)
        {
            if (node.References is null)
            {
                return;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.Value is null ||
                    reference.IsForward ||
                    !IsHierarchical(reference.ReferenceType) ||
                    IsEnvironmentNodeId(reference.Value))
                {
                    continue;
                }
                writer.WriteStartObject();
                writer.WriteString("rel", "uav:componentOf");
                writer.WriteString("href", SiblingDocumentHref(nodeSet, reference.Value));
                writer.WriteString("type", "application/td+json");
                writer.WriteEndObject();
            }
        }

        private static void WriteStringArray(Utf8JsonWriter writer, string name, List<string> values)
        {
            if (values.Count == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            for (int ii = 0; ii < values.Count; ii++)
            {
                writer.WriteStringValue(values[ii]);
            }
            writer.WriteEndArray();
        }

        private static string? ReadStringProperty(IReadOnlyDictionary<string, UANode> nodes, UANode node, string browseName)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.IsForward &&
                    string.Equals(reference.ReferenceType, "HasProperty", StringComparison.Ordinal) &&
                    reference.Value is not null &&
                    nodes.TryGetValue(reference.Value, out UANode? target) &&
                    string.Equals(BrowseNameLocal(target.BrowseName), browseName, StringComparison.Ordinal))
                {
                    return ReadUaVariableString(target as UAVariable);
                }
            }
            return null;
        }

        private static string? ReadUaVariableString(UAVariable? variable)
        {
            if (variable?.Value?.InnerText is null)
            {
                return null;
            }
            return variable.Value.InnerText.Trim();
        }

        private static Dictionary<string, UANode> Index(UANodeSet nodeSet)
        {
            var result = new Dictionary<string, UANode>(StringComparer.Ordinal);
            if (nodeSet.Items is not null)
            {
                foreach (UANode node in nodeSet.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        result[node.NodeId] = node;
                    }
                }
            }
            return result;
        }

        private static string? TypeDefinition(UANode node)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (reference.IsForward &&
                    string.Equals(reference.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal))
                {
                    return reference.Value;
                }
            }
            return null;
        }

        private static string PortableNodeId(UANodeSet nodeSet, string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return string.Empty;
            }
            if (nodeId.StartsWith("nsu=", StringComparison.Ordinal) || nodeId.StartsWith("i=", StringComparison.Ordinal))
            {
                return nodeId;
            }
            if (nodeId.StartsWith("ns=", StringComparison.Ordinal))
            {
                int separator = nodeId.IndexOf(";", StringComparison.Ordinal);
                if (separator > 3 &&
                    int.TryParse(nodeId.Substring(3, separator - 3), NumberStyles.None, CultureInfo.InvariantCulture, out int ns) &&
                    ns > 0 &&
                    nodeSet.NamespaceUris is not null &&
                    ns <= nodeSet.NamespaceUris.Length)
                {
                    return "nsu=" + nodeSet.NamespaceUris[ns - 1] + ";" + nodeId.Substring(separator + 1);
                }
            }
            return nodeId;
        }

        private static string PortableBrowseName(UANodeSet nodeSet, string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return string.Empty;
            }
            int separator = browseName.IndexOf(":", StringComparison.Ordinal);
            if (separator > 0 &&
                int.TryParse(browseName.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int ns) &&
                ns > 0 &&
                nodeSet.NamespaceUris is not null &&
                ns <= nodeSet.NamespaceUris.Length)
            {
                return "nsu=" + nodeSet.NamespaceUris[ns - 1] + ";" + browseName.Substring(separator + 1);
            }
            return browseName;
        }

        private static string NodeSetNodeId(string portable)
        {
            if (portable.StartsWith("nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";", StringComparison.Ordinal))
            {
                return "ns=1;" + portable.Substring(("nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";").Length);
            }
            if (portable.StartsWith("i=", StringComparison.Ordinal))
            {
                return portable;
            }
            int separator = portable.IndexOf(";", StringComparison.Ordinal);
            return separator >= 0 ? "ns=1;" + portable.Substring(separator + 1) : portable;
        }

        private static string NodeSetBrowseName(string portable)
        {
            if (portable.StartsWith("nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";", StringComparison.Ordinal))
            {
                return "1:" + portable.Substring(("nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";").Length);
            }
            int separator = portable.IndexOf(";", StringComparison.Ordinal);
            return separator >= 0 ? "1:" + portable.Substring(separator + 1) : portable;
        }

        private static string NormalizeTypeNodeId(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return "i=58";
            }
            if (nodeId.StartsWith("nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";", StringComparison.Ordinal))
            {
                return nodeId;
            }
            if (nodeId.StartsWith("ns=1;", StringComparison.Ordinal))
            {
                return "nsu=" + Opc.Ua.Aas.V3.Namespaces.AasV3 + ";" + nodeId.Substring(5);
            }
            return nodeId;
        }

        private static string FormHref(string endpoint, string nodeId)
        {
            string prefix = endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/";
            return prefix + "?id=" + Uri.EscapeDataString(nodeId);
        }

        private static string SiblingDocumentHref(UANodeSet nodeSet, string nodeId)
        {
            return "https://w3id.org/aas-jsonld/td/v1/" + Base64Url(PortableNodeId(nodeSet, nodeId));
        }

        private static string Base64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string? FirstText(Opc.Ua.Export.LocalizedText[]? text)
        {
            return text is { Length: > 0 } ? text[0].Value : null;
        }

        private static string? BrowseNameLocal(string? browseName)
        {
            if (browseName is null)
            {
                return null;
            }
            int separator = browseName.IndexOf(":", StringComparison.Ordinal);
            return separator >= 0 ? browseName.Substring(separator + 1) : browseName;
        }

        private static string BrowseNameLocalFromPortable(string portable)
        {
            int separator = portable.LastIndexOf(";", StringComparison.Ordinal);
            string identifier = separator >= 0 ? portable.Substring(separator + 1) : portable;
            if (identifier.StartsWith("s=", StringComparison.Ordinal))
            {
                return "Object";
            }
            return identifier;
        }

        private static string BrowseNameLocalFromReference(UANodeSet nodeSet, string nodeId)
        {
            if (nodeSet.Items is not null)
            {
                foreach (UANode node in nodeSet.Items)
                {
                    if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
                    {
                        return BrowseNameLocal(node.BrowseName) ?? string.Empty;
                    }
                }
            }
            return string.Empty;
        }

        private static bool TryGetString(JsonElement element, string name, out string? value)
        {
            if (element.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return true;
            }
            value = null;
            return false;
        }

        private static IEnumerable<string> ReadTokens(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string? token = element.GetString();
                if (token is not null)
                {
                    yield return token;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string? token = item.GetString();
                        if (token is not null)
                        {
                            yield return token;
                        }
                    }
                }
            }
        }

        private static string? FindTargetByLink(IReadOnlyList<JsonElement> documents, JsonElement link)
        {
            if (!TryGetString(link, "href", out string? href) || href is null)
            {
                return null;
            }
            foreach (JsonElement document in documents)
            {
                if (document.TryGetProperty("links", out JsonElement links) &&
                    links.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement candidate in links.EnumerateArray())
                    {
                        if (TryGetString(candidate, "rel", out string? rel) &&
                            string.Equals(rel, "self", StringComparison.Ordinal) &&
                            TryGetString(candidate, "href", out string? self) &&
                            string.Equals(self, href, StringComparison.Ordinal) &&
                            TryGetString(document, "uav:id", out string? id))
                        {
                            return id;
                        }
                    }
                }
            }
            return null;
        }

        private static void AddReference(UANode node, string referenceType, string? value, bool isForward)
        {
            var references = new List<Reference>();
            if (node.References is not null)
            {
                references.AddRange(node.References);
            }
            references.Add(new Reference { ReferenceType = referenceType, IsForward = isForward, Value = value });
            node.References = references.ToArray();
        }

        private static bool IsEnvironmentNode(UANode node)
        {
            return string.Equals(BrowseNameLocal(node.BrowseName), "AASEnvironment", StringComparison.Ordinal);
        }

        private static bool IsEnvironmentNodeId(string nodeId)
        {
            return string.Equals(nodeId, "ns=1;s=i4aas3:ENV", StringComparison.Ordinal) ||
                string.Equals(nodeId, "nsu=http://opcfoundation.org/UA/I4AAS/v3/;s=i4aas3:ENV", StringComparison.Ordinal);
        }

        private static bool IsContainmentRel(string? rel)
        {
            return string.Equals(rel, "ua:HasComponent", StringComparison.Ordinal) ||
                string.Equals(rel, "ua:HasOrderedComponent", StringComparison.Ordinal);
        }

        private static bool IsHierarchical(string? referenceType)
        {
            return string.Equals(referenceType, "HasComponent", StringComparison.Ordinal) ||
                string.Equals(referenceType, "HasOrderedComponent", StringComparison.Ordinal) ||
                string.Equals(referenceType, "Organizes", StringComparison.Ordinal);
        }

        private static bool HasErrors(List<AasWotBridgeDiagnostic> diagnostics)
        {
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == AasWotBridgeDiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }

        private static AasWotBridgeDiagnostic Error(string code, string message)
        {
            return new AasWotBridgeDiagnostic(AasWotBridgeDiagnosticSeverity.Error, code, message);
        }
    }
}
#pragma warning restore CA1307, CA1845, CA1846, CA1865
