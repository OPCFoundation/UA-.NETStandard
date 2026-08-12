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
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Native readable mapping (NodeSet2 to WoT affordances) and WoT to
    /// NodeSet2 synthesis for the <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        private const uint AccessLevelCurrentRead = 1;
        private const uint AccessLevelCurrentWrite = 2;

        private static readonly Dictionary<string, string> s_dataTypeToJsonType =
            new(StringComparer.Ordinal)
            {
                ["i=1"] = "boolean",
                ["Boolean"] = "boolean",
                ["i=2"] = "integer",
                ["SByte"] = "integer",
                ["i=3"] = "integer",
                ["Byte"] = "integer",
                ["i=4"] = "integer",
                ["Int16"] = "integer",
                ["i=5"] = "integer",
                ["UInt16"] = "integer",
                ["i=6"] = "integer",
                ["Int32"] = "integer",
                ["i=7"] = "integer",
                ["UInt32"] = "integer",
                ["i=8"] = "integer",
                ["Int64"] = "integer",
                ["i=9"] = "integer",
                ["UInt64"] = "integer",
                ["i=10"] = "number",
                ["Float"] = "number",
                ["i=11"] = "number",
                ["Double"] = "number",
                ["i=12"] = "string",
                ["String"] = "string"
            };

        /// <summary>
        /// Creates a deterministic WoT Thing Model/Thing Description with
        /// readable affordances and an exceptional complete
        /// <c>uav:nodes</c> projection when required. The preservation envelope is governed by
        /// <see cref="WotNodeSetConverterOptions.PreservationMode"/>.
        /// </summary>
        /// <param name="nodeSet">The NodeSet2 document to convert.</param>
        /// <param name="title">An optional document title.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The generated, byte-preserving WoT document.</returns>
        public static WotDocument FromNodeSet(
            UANodeSet nodeSet,
            string? title = null,
            WotNodeSetConverterOptions? options = null)
        {
            WotConversionResult<WotDocument> result = FromNodeSetResult(nodeSet, title, options);
            ThrowIfErrors(result.Diagnostics);
            return result.Value
                ?? throw new FormatException("The NodeSet could not be converted to a WoT document.");
        }

        /// <summary>
        /// Creates a WoT document from a NodeSet2 document, returning structured
        /// diagnostics together with the result.
        /// </summary>
        /// <param name="nodeSet">The NodeSet2 document to convert.</param>
        /// <param name="title">An optional document title.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        public static WotConversionResult<WotDocument> FromNodeSetResult(
            UANodeSet nodeSet,
            string? title = null,
            WotNodeSetConverterOptions? options = null)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            var diagnostics = new List<WotDiagnostic>();

            byte[] nodeSetBytes;
            using (var nodeSetStream = new MemoryStream())
            {
                nodeSet.Write(nodeSetStream);
                if (nodeSetStream.Length > options.MaxNodeSetSize)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NodeSetTooLarge,
                        $"NodeSet exceeds the configured {options.MaxNodeSetSize} byte limit."));
                    return new WotConversionResult<WotDocument>(null, diagnostics);
                }
                nodeSetBytes = nodeSetStream.ToArray();
            }

            UANode? root = SelectRootNode(nodeSet);
            string resolvedTitle = title
                ?? FirstText(root?.DisplayName)
                ?? (nodeSet.Models is { Length: > 0 } &&
                    !string.IsNullOrEmpty(nodeSet.Models[0].ModelUri)
                    ? nodeSet.Models[0].ModelUri!
                    : "OPC UA NodeSet");

            byte[] json = WriteReadableDocument(
                nodeSet,
                root,
                resolvedTitle,
                nodeSetBytes,
                null,
                emitEnvelope: false,
                options,
                diagnostics);
            json = WotJsonResidue.Apply(json, nodeSet, options, diagnostics);

            if (!IsReadableMappingComplete(json, nodeSet, options))
            {
                var nativeDiagnostics = new List<WotDiagnostic>();
                byte[] nativeProjection = WotNativeProjection.Write(
                    nodeSet,
                    options,
                    nativeDiagnostics);
                bool nativeComplete = false;
                string? nativeDifference = null;
                if (!HasErrors(nativeDiagnostics))
                {
                    using JsonDocument nativeDocument = JsonDocument.Parse(nativeProjection);
                    var reconstructionDiagnostics = new List<WotDiagnostic>();
                    UANodeSet? reconstructed = WotNativeProjection.Read(
                        nativeDocument.RootElement,
                        options,
                        reconstructionDiagnostics);
                    if (reconstructed is not null && !HasErrors(reconstructionDiagnostics))
                    {
                        NodeSetComparisonResult comparison =
                            NodeSetComparer.Compare(nodeSet, reconstructed, options);
                        nativeComplete = comparison.AreEquivalent;
                        if (!nativeComplete && comparison.Differences.Count > 0)
                        {
                            nativeDifference = comparison.Differences[0];
                        }
                    }
                    else
                    {
                        nativeDifference = FirstDiagnosticMessage(reconstructionDiagnostics);
                    }
                }
                else
                {
                    nativeDifference = FirstDiagnosticMessage(nativeDiagnostics);
                }

                bool emitEnvelope = options.PreservationMode ==
                    WotNodeSetPreservationMode.Always;
                if (!nativeComplete)
                {
                    string reason = nativeDifference ??
                        "The structured native projection did not reproduce the source NodeSet.";
                    if (options.PreservationMode == WotNodeSetPreservationMode.Never)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.NativeProjectionIncomplete,
                            reason));
                        return new WotConversionResult<WotDocument>(null, diagnostics);
                    }
                    emitEnvelope = true;
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.NativeProjectionIncomplete,
                        reason + " The uav:nodeSet fallback was emitted."));
                }

                json = WriteReadableDocument(
                    nodeSet,
                    root,
                    resolvedTitle,
                    nodeSetBytes,
                    nativeProjection,
                    emitEnvelope,
                    options,
                    diagnostics);
                json = WotJsonResidue.Apply(json, nodeSet, options, diagnostics);
            }
            else if (options.PreservationMode == WotNodeSetPreservationMode.Always)
            {
                json = WriteReadableDocument(
                    nodeSet,
                    root,
                    resolvedTitle,
                    nodeSetBytes,
                    null,
                    emitEnvelope: true,
                    options,
                    diagnostics);
                json = WotJsonResidue.Apply(json, nodeSet, options, diagnostics);
            }

            if (json.Length > options.MaxJsonDocumentSize)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.JsonDocumentTooLarge,
                    $"Generated WoT document exceeds the configured " +
                    $"{options.MaxJsonDocumentSize} byte limit."));
                return new WotConversionResult<WotDocument>(null, diagnostics);
            }
#pragma warning disable CA2000 // Ownership of the returned WotDocument transfers to the caller through the result.
            WotDocument document = WotDocument.FromOwnedBytes(json, options);
#pragma warning restore CA2000
            return new WotConversionResult<WotDocument>(document, diagnostics);
        }

        private static byte[] WriteReadableDocument(
            UANodeSet nodeSet,
            UANode? root,
            string resolvedTitle,
            byte[] nodeSetBytes,
            byte[]? nativeProjection,
            bool emitEnvelope,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            string? parentHref = null)
        {
            byte[]? digest = emitEnvelope ? ComputeSha256(nodeSetBytes) : null;
            using (var output = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(
                    output,
                    new JsonWriterOptions { Indented = true, SkipValidation = false }))
                {
                    writer.WriteStartObject();
                    WriteContext(writer, nodeSet);
                    bool rootIsEventType = IsEventTypeRoot(root, nodeSet);
                    WriteRootType(writer, root, rootIsEventType);
                    writer.WriteString("title", resolvedTitle);
                    if (!string.IsNullOrEmpty(root?.BrowseName))
                    {
                        writer.WriteString(
                            "uav:browseName",
                            ToPortableQualifiedName(
                                root!.BrowseName,
                                nodeSet.NamespaceUris));
                    }
                    if (!string.IsNullOrEmpty(root?.NodeId))
                    {
                        string? portableId = ToPortableNodeId(root!.NodeId, nodeSet.NamespaceUris);
                        if (!string.IsNullOrEmpty(portableId))
                        {
                            writer.WriteString("uav:id", portableId);
                        }
                    }
                    if (rootIsEventType)
                    {
                        writer.WriteBoolean("uav:isEvent", true);
                    }
                    WriteDescription(writer, root?.Description);
                    WriteAffordances(
                        writer, nodeSet, root, diagnostics, options, parentHref,
                        TypeDefinitionHref(root, nodeSet));

                    if (emitEnvelope)
                    {
                        writer.WritePropertyName("uav:nodeSet");
                        writer.WriteStartObject();
                        writer.WriteString("@type", WotVocabulary.EnvelopeType);
                        writer.WriteString("contentType", WotVocabulary.NodeSetContentType);
                        writer.WriteString("encoding", WotVocabulary.Base64Encoding);
                        writer.WriteString("sha256", CoreUtils.ToHexString(digest!).ToLowerInvariant());
                        writer.WriteString("data", System.Convert.ToBase64String(nodeSetBytes));
                        writer.WriteString("profileVersion", WotVocabulary.ProfileVersion);
                        writer.WriteEndObject();
                    }

                    if (nativeProjection is not null)
                    {
                        writer.WritePropertyName("uav:nodes");
                        using (JsonDocument nativeDocument = JsonDocument.Parse(nativeProjection))
                        {
                            nativeDocument.RootElement.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                }
                return output.ToArray();
            }
        }

        private static bool IsReadableMappingComplete(
            byte[] json,
            UANodeSet source,
            WotNodeSetConverterOptions options)
        {
            byte[] readable = RemoveRootMembers(
                json,
                options,
                "uav:nodes",
                "uav:nodeSet");
            using WotDocument document = WotDocument.Parse(readable, options);
            WotConversionResult<UANodeSet> result =
                ToNodeSetResult(document, options);
            return result.Success &&
                NodeSetComparer.Compare(source, result.Value!, options).AreEquivalent;
        }

        private static byte[] RemoveRootMembers(
            byte[] json,
            WotNodeSetConverterOptions options,
            params string[] names)
        {
            using WotDocument document = WotDocument.Parse(json, options);
            var excluded = new HashSet<string>(names, StringComparer.Ordinal);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                output,
                new JsonWriterOptions { Indented = true, SkipValidation = false }))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (!excluded.Contains(member.Name))
                    {
                        writer.WritePropertyName(member.Name);
                        member.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            return output.ToArray();
        }

        /// <summary>
        /// Selects the root node of a projected NodeSet2 - the ObjectType or
        /// VariableType a Thing Model materializes, or the top-level Object a
        /// Thing Description projects - and returns it as an absolute
        /// <see cref="ExpandedNodeId"/> whose <see cref="ExpandedNodeId.NamespaceUri"/>
        /// is resolved from the NodeSet's own namespace table. Returns
        /// <c>ExpandedNodeId.Null</c> when the NodeSet carries no nodes or the
        /// root NodeId cannot be parsed.
        /// </summary>
        /// <param name="nodeSet">The projected NodeSet2 document.</param>
        /// <returns>
        /// The root node as an absolute ExpandedNodeId, or
        /// <c>ExpandedNodeId.Null</c> when the NodeSet has no identifiable root.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="nodeSet"/> is <c>null</c>.
        /// </exception>
        public static ExpandedNodeId TrySelectProjectionRoot(UANodeSet nodeSet)
        {
            if (nodeSet is null)
            {
                throw new ArgumentNullException(nameof(nodeSet));
            }
            UANode? root = SelectRootNode(nodeSet);
            if (root?.NodeId is not { Length: > 0 } rawNodeId)
            {
                return ExpandedNodeId.Null;
            }
            NodeId parsed;
            try
            {
                parsed = NodeId.Parse(rawNodeId);
            }
            catch (ServiceResultException)
            {
                return ExpandedNodeId.Null;
            }
            ushort localIndex = parsed.NamespaceIndex;
            string namespaceUri;
            if (localIndex == 0)
            {
                namespaceUri = Opc.Ua.Types.Namespaces.OpcUa;
            }
            else if (nodeSet.NamespaceUris is { Length: > 0 } uris &&
                localIndex - 1 < uris.Length)
            {
                namespaceUri = uris[localIndex - 1];
            }
            else
            {
                return ExpandedNodeId.Null;
            }
            return new ExpandedNodeId(parsed, namespaceUri);
        }

        private static void WriteContext(Utf8JsonWriter writer, UANodeSet nodeSet)
        {
            writer.WritePropertyName("@context");
            writer.WriteStartArray();
            writer.WriteStringValue(WotVocabulary.WotContext);
            writer.WriteStartObject();
            writer.WriteString("uav", WotVocabulary.VocabularyNamespace);
            writer.WriteString("ua", WotVocabulary.OpcUaNamespace);
            if (nodeSet.NamespaceUris is not null)
            {
                for (int ii = 0; ii < nodeSet.NamespaceUris.Length; ii++)
                {
                    writer.WriteString(
                        "ns" + (ii + 1).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        nodeSet.NamespaceUris[ii]);
                }
            }
            writer.WriteEndObject();
            writer.WriteEndArray();
        }

        private static void WriteRootType(Utf8JsonWriter writer, UANode? root, bool isEventType)
        {
            switch (root)
            {
                case UAObjectType when isEventType:
                    // An ObjectType derived from BaseEventType projects a UA
                    // EventType, annotated with uav:eventType (WoT Binding
                    // Section 5.2) rather than the generic uav:objectType.
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue(WotVocabulary.EventTypeAnnotation);
                    writer.WriteEndArray();
                    break;
                case UAObjectType:
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:objectType");
                    writer.WriteEndArray();
                    break;
                case UAVariableType:
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:variableType");
                    writer.WriteEndArray();
                    break;
                case UAObject:
                    writer.WriteString("@type", "uav:object");
                    break;
                case UAVariable:
                    writer.WriteString("@type", "uav:variable");
                    break;
                default:
                    writer.WriteString("@type", WotVocabulary.ThingModelType);
                    break;
            }
        }

        private static void WriteDescription(Utf8JsonWriter writer, Opc.Ua.Export.LocalizedText[]? description)
        {
            string? text = FirstText(description);
            if (!string.IsNullOrEmpty(text))
            {
                writer.WriteString("description", text);
            }
        }

        private static void WriteAffordances(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            UANode? root,
            List<WotDiagnostic> diagnostics,
            WotNodeSetConverterOptions options,
            string? parentHref = null,
            string? typeDefinitionHref = null)
        {
            if (root?.References is null)
            {
                WriteTypedComponentLinks(writer, [], parentHref, typeDefinitionHref);
                return;
            }

            Dictionary<string, UANode> index = BuildIndex(nodeSet);
            string[]? namespaceUris = nodeSet.NamespaceUris;
            var properties = new List<UAVariable>();
            var actions = new List<UAMethod>();
            var events = new List<UANode>();

            // HasComponent subtypes (for example HasOrderedComponent) are
            // surfaced for discovery under uav:hasComponent / uav:componentOf and
            // additionally pinned by a link whose rel is the semantic
            // ReferenceType model name and whose uav:refId is the definitive
            // ExpandedNodeId (WoT Binding Sections 5.1.2 and 5.3).
            var componentChildren = new List<string>();
            var componentParents = new List<string>();
            var typedComponentLinks = new List<TypedComponentLink>();

            foreach (Reference reference in root.References)
            {
                if (reference.Value is null)
                {
                    continue;
                }
                if (reference.IsForward && IsComponentReference(reference.ReferenceType))
                {
                    if (index.TryGetValue(reference.Value, out UANode? target))
                    {
                        if (target is UAVariable variable)
                        {
                            properties.Add(variable);
                        }
                        else if (target is UAMethod method)
                        {
                            actions.Add(method);
                        }
                    }
                }
                else if (reference.IsForward &&
                    IsGeneratesEventReference(reference.ReferenceType) &&
                    index.TryGetValue(reference.Value, out UANode? eventType))
                {
                    events.Add(eventType);
                }
                else if (WotVocabulary.TryGetHasComponentSubtype(
                    reference.ReferenceType, out string subtypeNodeId))
                {
                    string? portableTarget = ToPortableNodeId(reference.Value, namespaceUris);
                    if (string.IsNullOrEmpty(portableTarget))
                    {
                        continue;
                    }
                    (reference.IsForward ? componentChildren : componentParents)
                        .Add(portableTarget!);
                    typedComponentLinks.Add(new TypedComponentLink(
                        portableTarget!,
                        ToReferenceTypeModelName(reference.ReferenceType)
                            ?? "ua:HasOrderedComponent",
                        subtypeNodeId,
                        ComponentRefName(reference.Value, index)));
                }
            }

            bool isThingModel = root is UAObjectType or UAVariableType;
            int affordanceCount = 0;

            WriteComponentArray(writer, "uav:hasComponent", componentChildren);
            WriteComponentArray(writer, "uav:componentOf", componentParents);

            if (properties.Count > 0)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UAVariable variable in properties)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(variable.BrowseName), used));
                    WriteVariableAffordance(writer, variable, isThingModel, namespaceUris, nodeSet);
                }
                writer.WriteEndObject();
            }

            if (actions.Count > 0)
            {
                writer.WritePropertyName("actions");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UAMethod method in actions)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(method.BrowseName), used));
                    WriteMethodAffordance(writer, method, namespaceUris);
                }
                writer.WriteEndObject();
            }

            if (events.Count > 0)
            {
                writer.WritePropertyName("events");
                writer.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (UANode eventType in events)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(UniqueKey(LocalName(eventType.BrowseName), used));
                    WriteEventAffordance(writer, eventType, namespaceUris);
                }
                writer.WriteEndObject();
            }

            WriteTypedComponentLinks(
                writer, typedComponentLinks, parentHref, typeDefinitionHref);
        }

        /// <summary>
        /// Resolves a Variable's <c>DataType</c> attribute — which a NodeSet may
        /// write as an alias such as <c>Boolean</c> — to a portable
        /// ExpandedNodeId, or <c>null</c> when it states no identifier.
        /// </summary>
        /// <remarks>
        /// A <c>DataType</c> attribute is free text in the schema, so one that
        /// names an alias no <c>Aliases</c> table declares, or that starts like
        /// an identifier and is not one, states nothing definitive. The
        /// DataSchema's json type stays the only claim rather than the
        /// conversion failing over a value it was only trying to enrich.
        /// </remarks>
        private static string? ToPortableDataTypeId(string? dataType, UANodeSet nodeSet)
        {
            if (string.IsNullOrEmpty(dataType))
            {
                return null;
            }
            string resolved = dataType!;
            foreach (NodeIdAlias alias in nodeSet.Aliases ?? [])
            {
                if (string.Equals(alias.Alias, dataType, StringComparison.Ordinal) &&
                    alias.Value is { Length: > 0 })
                {
                    resolved = alias.Value;
                    break;
                }
            }
            if (!LooksLikeNodeId(resolved))
            {
                return null;
            }
            try
            {
                return ToPortableNodeId(resolved, nodeSet.NamespaceUris);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private static bool LooksLikeNodeId(string value)
        {
            return value.StartsWith("i=", StringComparison.Ordinal) ||
                value.StartsWith("ns=", StringComparison.Ordinal) ||
                value.StartsWith("nsu=", StringComparison.Ordinal) ||
                value.StartsWith("s=", StringComparison.Ordinal) ||
                value.StartsWith("g=", StringComparison.Ordinal) ||
                value.StartsWith("b=", StringComparison.Ordinal);
        }

        /// <summary>
        /// Writes an affordance's definitive <c>ua:HasTypeDefinition</c> link.
        /// </summary>
        private static void WriteTypeDefinitionLink(Utf8JsonWriter writer, string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return;
            }
            writer.WritePropertyName("links");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("rel", "ua:HasTypeDefinition");
            writer.WriteString("href", href);
            writer.WriteEndObject();
            writer.WriteEndArray();
        }

        /// <summary>
        /// Reads the portable ExpandedNodeId of a Node's <c>HasTypeDefinition</c>
        /// target, or <c>null</c> when it declares none.
        /// </summary>
        private static string? TypeDefinitionHref(UANode? node, UANodeSet nodeSet)
        {
            foreach (Reference reference in node?.References ?? [])
            {
                if (reference.IsForward &&
                    string.Equals(
                        reference.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal) &&
                    reference.Value is { Length: > 0 })
                {
                    return ToPortableNodeId(reference.Value, nodeSet.NamespaceUris);
                }
            }
            return null;
        }

        private static void WriteComponentArray(
            Utf8JsonWriter writer,
            string name,
            List<string> targets)
        {
            if (targets.Count == 0)
            {
                return;
            }
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            foreach (string target in targets)
            {
                writer.WriteStringValue(target);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Writes the document's <c>links</c> array: the typed component links a
        /// Node carries, the <c>uav:componentOf</c> link naming the document that
        /// owns its parent when this document describes a nested Object of a
        /// document set, and the definitive <c>ua:HasTypeDefinition</c> link of
        /// §5.2.1. They share one array because a JSON object cannot carry
        /// <c>links</c> twice.
        /// </summary>
        private static void WriteTypedComponentLinks(
            Utf8JsonWriter writer,
            List<TypedComponentLink> links,
            string? parentHref = null,
            string? typeDefinitionHref = null)
        {
            if (links.Count == 0 &&
                string.IsNullOrEmpty(parentHref) &&
                string.IsNullOrEmpty(typeDefinitionHref))
            {
                return;
            }
            writer.WritePropertyName("links");
            writer.WriteStartArray();
            if (!string.IsNullOrEmpty(typeDefinitionHref))
            {
                writer.WriteStartObject();
                writer.WriteString("rel", "ua:HasTypeDefinition");
                writer.WriteString("href", typeDefinitionHref);
                writer.WriteEndObject();
            }
            if (!string.IsNullOrEmpty(parentHref))
            {
                writer.WriteStartObject();
                writer.WriteString("rel", "uav:componentOf");
                writer.WriteString("href", parentHref);
                writer.WriteString("type", "application/td+json");
                writer.WriteEndObject();
            }
            foreach (TypedComponentLink link in links)
            {
                writer.WriteStartObject();
                writer.WriteString("rel", link.Rel);
                writer.WriteString("href", link.Target);
                writer.WriteString("uav:refId", link.RefType);
                writer.WriteString("uav:refName", link.RefName);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static string ComponentRefName(string rawTarget, Dictionary<string, UANode> index)
        {
            if (index.TryGetValue(rawTarget, out UANode? node) &&
                LocalName(node.BrowseName) is { Length: > 0 } local)
            {
                return local;
            }
            return rawTarget;
        }

        /// <summary>
        /// Maps a HasComponent-subtype reference onto its compact model name.
        /// Only reference types accepted by
        /// <see cref="WotVocabulary.TryGetHasComponentSubtype"/> reach this
        /// method, and every one of those is a well-known reference type, so the
        /// standard vocabulary lookup always resolves. Null is returned if that
        /// invariant is ever broken; the caller substitutes a default relation.
        /// </summary>
        private static string? ToReferenceTypeModelName(string? referenceType)
        {
            if (WotVocabulary.TryGetReferenceTypeBrowseName(
                referenceType,
                out string browseName))
            {
                return "ua:" + browseName;
            }
            return null;
        }

        private static void WriteVariableAffordance(
            Utf8JsonWriter writer,
            UAVariable variable,
            bool isThingModel,
            string[]? namespaceUris,
            UANodeSet nodeSet)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", isThingModel ? "uav:variableType" : "uav:variable");
            WriteOptional(writer, "title", FirstText(variable.DisplayName));
            WriteDescription(writer, variable.Description);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(variable.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(variable.NodeId, namespaceUris));

            // Section 5.2.1 puts the definitive type-binding link on an
            // affordance as well as on the Thing. Without it every Variable
            // converts back as a BaseDataVariableType, and a Client browsing for
            // AnalogUnitType, PropertyType or TwoStateDiscreteType finds none.
            WriteTypeDefinitionLink(writer, TypeDefinitionHref(variable, nodeSet));

            string? jsonType = MapDataTypeToJson(variable.DataType);
            if (jsonType is not null)
            {
                writer.WriteString("type", jsonType);
            }

            // §9.1 gives a DataType one readable channel, the DataSchema's json
            // type, and that channel carries six types — so a LocalizedText and
            // a String come back the same. §5.4 states the definitive DataType
            // at property level, and the reverse direction prefers it.
            WriteOptional(
                writer,
                "uav:mapToType",
                ToPortableDataTypeId(variable.DataType, nodeSet));

            bool readable = (variable.AccessLevel & AccessLevelCurrentRead) != 0;
            bool writable = (variable.AccessLevel & AccessLevelCurrentWrite) != 0;
            if (readable && !writable)
            {
                writer.WriteBoolean("readOnly", true);
            }
            else if (writable && !readable)
            {
                writer.WriteBoolean("writeOnly", true);
            }
            if (readable)
            {
                // This advertises observation through the WoT binding. It does
                // not define core UA monitorability; any Variable may be a
                // MonitoredItem when the Server grants access.
                writer.WriteBoolean("observable", true);
            }

            WriteModellingRule(writer, variable);
            writer.WriteEndObject();
        }

        private static void WriteMethodAffordance(
            Utf8JsonWriter writer,
            UAMethod method,
            string[]? namespaceUris)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "uav:method");
            WriteOptional(writer, "title", FirstText(method.DisplayName));
            WriteDescription(writer, method.Description);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(method.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(method.NodeId, namespaceUris));
            WriteModellingRule(writer, method);
            writer.WriteEndObject();
        }

        private static void WriteEventAffordance(
            Utf8JsonWriter writer,
            UANode eventType,
            string[]? namespaceUris)
        {
            writer.WriteStartObject();
            // uav:eventType is the @type annotation counterpart of the uav:isEvent
            // flag; an EventType projection carries both (WoT Binding Section 5.2).
            writer.WriteString("@type", WotVocabulary.EventTypeAnnotation);
            WriteOptional(writer, "title", FirstText(eventType.DisplayName));
            WriteDescription(writer, eventType.Description);
            writer.WriteBoolean("uav:isEvent", true);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(eventType.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(eventType.NodeId, namespaceUris));
            WriteModellingRule(writer, eventType);
            writer.WriteEndObject();
        }

        private static void WriteModellingRule(Utf8JsonWriter writer, UANode node)
        {
            string? rule = GetBaselineModellingRule(node);
            if (rule is not null)
            {
                writer.WriteString("uav:modellingRule", rule);
            }
        }

        private static Dictionary<string, UANode> BuildIndex(UANodeSet nodeSet)
        {
            var index = new Dictionary<string, UANode>(StringComparer.Ordinal);
            if (nodeSet.Items is not null)
            {
                foreach (UANode node in nodeSet.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        index[node.NodeId!] = node;
                    }
                }
            }
            return index;
        }

        private static UANode? SelectRootNode(UANodeSet nodeSet)
        {
            if (nodeSet.Items is null || nodeSet.Items.Length == 0)
            {
                return null;
            }
            return FirstOf<UAObjectType>(nodeSet)
                ?? FirstOf<UAObject>(nodeSet)
                ?? FirstOf<UAVariableType>(nodeSet)
                ?? FirstOf<UAType>(nodeSet)
                ?? nodeSet.Items[0];
        }

        private static UANode? FirstOf<T>(UANodeSet nodeSet) where T : UANode
        {
            foreach (UANode node in nodeSet.Items!)
            {
                if (node is T)
                {
                    return node;
                }
            }
            return null;
        }

        private static bool CheckAffordanceBudget(
            ref int count,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            if (count >= options.MaxAffordanceCount)
            {
                if (count == options.MaxAffordanceCount)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.AffordanceCountExceeded,
                        $"The affordance count exceeded the configured limit of {options.MaxAffordanceCount}."));
                    count++;
                }
                return false;
            }
            count++;
            return true;
        }

        private static bool IsComponentReference(string? referenceType)
        {
            return string.Equals(referenceType, "HasComponent", StringComparison.Ordinal) ||
                string.Equals(referenceType, "HasProperty", StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.HasComponent, StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.HasProperty, StringComparison.Ordinal);
        }

        private static bool IsGeneratesEventReference(string? referenceType)
        {
            return string.Equals(referenceType, "GeneratesEvent", StringComparison.Ordinal) ||
                string.Equals(referenceType, WotVocabulary.GeneratesEvent, StringComparison.Ordinal);
        }

        private static bool IsReferenceRel(
            WotDocument document,
            JsonElement link,
            string rel)
        {
            return IsModelConceptRelation(document, link, rel);
        }

        /// <summary>
        /// Gets whether a link's <c>rel</c> is a compact model name naming a
        /// ReferenceType, rather than a Binding term or an external relation.
        /// </summary>
        /// <remarks>
        /// Only the reserved prefixes are matched as literals; every other
        /// prefix is resolved through the document's <c>@context</c> by
        /// <see cref="TryGetContextNamespace(WotDocument, string, out string)"/>,
        /// because an author chooses those freely. The literals are exact
        /// because WoT Binding Section 4 requires a conforming document to bind
        /// <c>uav</c> to the Binding namespace and forbids rebinding it,
        /// Section 6.5.1 reserves <c>ua</c> for
        /// <c>http://opcfoundation.org/UA/</c>, and <c>tm</c> is fixed by the
        /// W3C WoT Thing Description 1.1 context. JSON-LD terms are
        /// case-sensitive, so every comparison is ordinal and never
        /// ignore-case.
        /// </remarks>
        private static bool IsModelConceptRelation(
            WotDocument document,
            JsonElement link,
            string rel)
        {
            if (!TrySplitCompactModelName(
                    rel,
                    out string prefix,
                    out _) ||
                string.Equals(prefix, "uav", StringComparison.Ordinal) ||
                string.Equals(prefix, "tm", StringComparison.Ordinal) ||
                IsExternalRelationPrefix(prefix) ||
                !IsModelConceptCandidate(link, prefix) ||
                !TryGetContextNamespace(document, prefix, out _))
            {
                return false;
            }
            return true;
        }

        private static bool IsKnownBindingRelation(string rel)
        {
            return rel is "uav:componentOf";
        }

        private static bool IsModelConceptCandidate(
            JsonElement link,
            string prefix)
        {
            return string.Equals(prefix, "ua", StringComparison.Ordinal) ||
                StartsWithGeneratedNamespacePrefix(prefix) ||
                link.TryGetProperty("uav:refId", out _) ||
                link.TryGetProperty("uav:refName", out _);
        }

        private static bool StartsWithGeneratedNamespacePrefix(string prefix)
        {
            if (!prefix.StartsWith("ns", StringComparison.Ordinal) ||
                prefix.Length == 2)
            {
                return false;
            }
            for (int ii = 2; ii < prefix.Length; ii++)
            {
                if (prefix[ii] is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsExternalRelationPrefix(string prefix)
        {
            return prefix is "http" or "https" or "urn";
        }

        private static bool IsNodeId(string reference)
        {
            return reference.StartsWith("ns=", StringComparison.Ordinal) ||
                reference.StartsWith("nsu=", StringComparison.Ordinal) ||
                reference.StartsWith("svr=", StringComparison.Ordinal) ||
                reference.StartsWith("i=", StringComparison.Ordinal) ||
                reference.StartsWith("s=", StringComparison.Ordinal) ||
                reference.StartsWith("g=", StringComparison.Ordinal) ||
                reference.StartsWith("b=", StringComparison.Ordinal);
        }

        private static string GenerateNodeId(string browsePath)
        {
            return "ns=1;s=" + browsePath;
        }

        /// <summary>
        /// Renders a NodeSet-local NodeId string as a portable OPC 10000-6
        /// ExpandedNodeId (WoT Binding Section 5.1.1): namespace 0 keeps its
        /// canonical <c>i=</c>/<c>s=</c> form, while a higher namespace index is
        /// resolved to <c>nsu=&lt;NamespaceUri&gt;;...</c> through the source
        /// NodeSet's <c>NamespaceUris</c> table so the value survives a
        /// namespace-table reordering. The session-local <c>ns=&lt;index&gt;</c>
        /// form is never emitted. An unparseable or unresolvable value is left
        /// untouched.
        /// </summary>
        private static string? ToPortableNodeId(string? rawNodeId, string[]? namespaceUris)
        {
            if (string.IsNullOrEmpty(rawNodeId))
            {
                return rawNodeId;
            }
            NodeId parsed;
            try
            {
                parsed = NodeId.Parse(rawNodeId!);
            }
            catch (ServiceResultException)
            {
                return rawNodeId;
            }
            var buffer = new System.Text.StringBuilder();
            ushort index = parsed.NamespaceIndex;
            if (index != 0)
            {
                if (namespaceUris is null || index - 1 >= namespaceUris.Length)
                {
                    return rawNodeId;
                }
                buffer.Append("nsu=")
                    .Append(CoreUtils.EscapeUri(namespaceUris[index - 1]))
                    .Append(';');
            }
            NodeId.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                buffer,
                parsed.IdentifierAsString,
                parsed.IdType,
                0);
            return buffer.ToString();
        }

        private static string? ToPortableQualifiedName(
            string? rawBrowseName,
            string[]? namespaceUris)
        {
            if (string.IsNullOrEmpty(rawBrowseName) ||
                rawBrowseName!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                return rawBrowseName;
            }
            int separator = -1;
            for (int ii = 0; ii < rawBrowseName.Length; ii++)
            {
                if (rawBrowseName[ii] == ':')
                {
                    separator = ii;
                    break;
                }
                if (rawBrowseName[ii] is not (>= '0' and <= '9'))
                {
                    return rawBrowseName;
                }
            }
            if (separator <= 0 || separator + 1 >= rawBrowseName.Length)
            {
                return rawBrowseName;
            }
            int namespaceIndex = 0;
            for (int ii = 0; ii < separator; ii++)
            {
                int digit = rawBrowseName[ii] - '0';
                if (namespaceIndex > (int.MaxValue - digit) / 10)
                {
                    return rawBrowseName;
                }
                namespaceIndex = (namespaceIndex * 10) + digit;
            }
            string name = rawBrowseName.Substring(separator + 1);
            if (namespaceIndex == 0)
            {
                return name;
            }
            if (namespaceUris is null || namespaceIndex > namespaceUris.Length)
            {
                return rawBrowseName;
            }
            return "ns" +
                namespaceIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                name;
        }

        private static string? MapDataTypeToJson(string? dataType)
        {
            if (dataType is not null &&
                s_dataTypeToJsonType.TryGetValue(dataType, out string? jsonType))
            {
                return jsonType;
            }
            return null;
        }

        private static string UniqueKey(string? candidate, HashSet<string> used)
        {
            string key = string.IsNullOrEmpty(candidate) ? "member" : candidate!;
            if (used.Add(key))
            {
                return key;
            }
            int suffix = 2;
            string unique = key + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (!used.Add(unique))
            {
                suffix++;
                unique = key + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return unique;
        }

        private static string? LocalName(string? browseName)
        {
            if (string.IsNullOrEmpty(browseName))
            {
                return null;
            }
            if (browseName!.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = -1;
                for (int ii = 4; ii < browseName.Length; ii++)
                {
                    if (browseName[ii] == ';')
                    {
                        delimiter = ii;
                        break;
                    }
                }
                return delimiter >= 0 && delimiter + 1 < browseName.Length
                    ? browseName.Substring(delimiter + 1)
                    : null;
            }
            int colon = browseName!.IndexOf(':', StringComparison.Ordinal);
            return colon >= 0 && colon + 1 < browseName.Length
                ? browseName.Substring(colon + 1)
                : browseName;
        }

        private static string? SanitizeName(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }
            var builder = new System.Text.StringBuilder(title!.Length);
            foreach (char character in title!)
            {
                if (char.IsLetterOrDigit(character) || character is '_' or '-')
                {
                    builder.Append(character);
                }
            }
            return builder.Length == 0 ? null : builder.ToString();
        }

        private static Opc.Ua.Export.LocalizedText[] MakeText(string value)
        {
            return [new Opc.Ua.Export.LocalizedText { Value = value }];
        }

        private static string? FirstText(Opc.Ua.Export.LocalizedText[]? texts)
        {
            if (texts is null)
            {
                return null;
            }
            foreach (Opc.Ua.Export.LocalizedText text in texts)
            {
                if (!string.IsNullOrEmpty(text.Value))
                {
                    return text.Value;
                }
            }
            return null;
        }

        private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                writer.WriteString(name, value);
            }
        }

        private static string? GetUavString(WotDocument document, string localName)
        {
            return document.TryGetUav(localName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetRootString(WotDocument document, string name)
        {
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetElementString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetElementBool(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.True;
        }

        private readonly record struct TypedComponentLink(
            string Target,
            string Rel,
            string RefType,
            string RefName);

        private readonly record struct ResolvableThingReference(
            string Reference,
            string? ReferenceType,
            bool IsExtends);
    }
}
