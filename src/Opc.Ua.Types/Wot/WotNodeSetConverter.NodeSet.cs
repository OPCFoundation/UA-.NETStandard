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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        /// <exception cref="FormatException">
        /// Thrown when the NodeSet could not be converted.
        /// </exception>
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
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="nodeSet"/> is <c>null</c>.
        /// </exception>
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
                title is not null,
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
                            NodeSetComparer.Compare(
                                nodeSet, reconstructed, options.ToComparisonOptions());
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
                    title is not null,
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
                    title is not null,
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
                    "Generated WoT document exceeds the configured " +
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
            bool explicitTitle,
            byte[] nodeSetBytes,
            byte[]? nativeProjection,
            bool emitEnvelope,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            string? parentHref = null,
            IReadOnlyDictionary<string, string>? eventTypeHrefs = null,
            string? documentHref = null)
        {
            byte[]? digest = emitEnvelope ? ComputeSha256(nodeSetBytes) : null;
            using (var output = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(
                    output,
                    new JsonWriterOptions { Indented = true, SkipValidation = false }))
                {
                    writer.WriteStartObject();
                    string? documentLocale = SelectDocumentLocale(root);
                    string defaultLocale = EffectiveLocale(documentLocale);
                    WriteContext(writer, nodeSet, documentLocale);
                    bool rootIsEventType = IsEventTypeRoot(root, nodeSet);
                    WriteRootType(writer, root, rootIsEventType);

                    // Section 4.1: a tool that generates a document against
                    // this revision shall state which revision it emitted. A
                    // generator, unlike a hand author, always knows.
                    writer.WriteString(
                        WotBindingConformance.BindingVersionTerm,
                        WotBindingConformance.CurrentRevision);
                    if (explicitTitle)
                    {
                        writer.WriteString("title", resolvedTitle);
                    }
                    else
                    {
                        WriteLocalizedTitle(
                            writer, root?.DisplayName, defaultLocale, resolvedTitle);
                    }
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
                    WriteReferenceTypeNames(writer, root, defaultLocale);
                    WriteLocalizedDescription(writer, root?.Description, defaultLocale);
                    if (rootIsEventType && root is not null)
                    {
                        // The root projects an EventType Node, so this document
                        // is the EventType definition an event affordance links
                        // to with tm:ref: it states the complete effective field
                        // set, in the order uav:fieldOrder gives, and a consumer
                        // derives one select clause per leaf of it
                        // (WoT Binding Section 6.1).
                        WriteEventTypeDefinitionData(
                            writer,
                            root,
                            nodeSet.NamespaceUris,
                            nodeSet,
                            BuildIndex(nodeSet),
                            defaultLocale);
                    }
                    WriteDataTypeDefinitions(writer, nodeSet, defaultLocale);
                    WriteAffordances(
                        writer, nodeSet, root, diagnostics, options, defaultLocale, parentHref,
                        TypeDefinitionHref(root, nodeSet), eventTypeHrefs, documentHref);

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
            // §9.2 asks whether the readable document reproduces an equivalent
            // NodeSet, not an identically spelled one, so the comparison
            // resolves each side's own aliases first and reads what neither
            // declared through the WoT Binding's alias policy, which the
            // options carry.
            return result.Success &&
                NodeSetComparer.CompareEquivalent(
                    source, result.Value!, options.ToComparisonOptions()).AreEquivalent;
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

        private static void WriteContext(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            string? documentLocale)
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
                        "ns" +
                        (ii + 1).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        nodeSet.NamespaceUris[ii]);
                }
            }

            // Section 9.1.1 makes the @language of the context the default
            // locale of the document, and every localized member is read
            // against it. It is declared only where the source states one, so
            // a NodeSet that names no language does not gain a claim it never
            // made.
            if (!string.IsNullOrEmpty(documentLocale))
            {
                writer.WriteString("@language", documentLocale);
            }
            writer.WriteEndObject();

            // The Binding mints several terms as short members under a
            // type-scoped context, so a document that names the uav prefix but
            // not the context expands those members to nothing. Naming the
            // context is what makes them terms.
            writer.WriteStringValue(WotVocabulary.BindingContext);

            // Section 9.1.1 admits a document whose localized text states no
            // entry for its own default locale. Under a context that declares
            // @language, an unqualified title would then be read as text of a
            // language it is not written in, so the two terms are re-declared
            // without a language. The override is written only where the
            // document actually needs it: adding it unconditionally would drop
            // the language tag from every document, including the ones whose
            // text really is in the default locale.
            if (!string.IsNullOrEmpty(documentLocale) &&
                RequiresLocalizedTextOverride(nodeSet, documentLocale!))
            {
                WriteLocalizedTextOverride(writer);
            }
            writer.WriteEndArray();
        }

        /// <summary>
        /// Writes the <c>title</c> / <c>description</c> override that drops the
        /// document's default language from the two W3C terms.
        /// </summary>
        private static void WriteLocalizedTextOverride(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            foreach ((string member, string iri) in s_localizedTextOverrides)
            {
                writer.WritePropertyName(member);
                writer.WriteStartObject();
                writer.WriteString("@id", iri);
                writer.WriteNull("@language");
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// The two W3C Thing Description terms whose language tag an
        /// unqualified document text must not inherit.
        /// </summary>
        private static readonly (string Member, string Iri)[] s_localizedTextOverrides =
        [
            (TitleMember, "https://www.w3.org/2019/wot/td#title"),
            (DescriptionMember, "https://www.w3.org/2019/wot/td#description")
        ];

        /// <summary>
        /// Gets whether a <c>@context</c> entry is the generated
        /// <c>title</c> / <c>description</c> override.
        /// </summary>
        /// <remarks>
        /// The shape is matched exactly rather than by member name alone: an
        /// author's own override of the same two terms says something different
        /// from the one this converter derives, and treating it as
        /// re-derivable would drop what the author wrote.
        /// </remarks>
        internal static bool IsGeneratedLocalizedTextOverride(JsonElement item)
        {
            int matched = 0;
            foreach (JsonProperty member in item.EnumerateObject())
            {
                string? iri = null;
                foreach ((string name, string candidate) in s_localizedTextOverrides)
                {
                    if (string.Equals(member.Name, name, StringComparison.Ordinal))
                    {
                        iri = candidate;
                        break;
                    }
                }
                if (iri is null || !IsLanguageFreeAlias(member.Value, iri))
                {
                    return false;
                }
                matched++;
            }
            return matched == s_localizedTextOverrides.Length;
        }

        /// <summary>
        /// Gets whether a term definition is exactly
        /// <c>{ "@id": iri, "@language": null }</c>.
        /// </summary>
        internal static bool IsLanguageFreeAlias(JsonElement definition, string iri)
        {
            if (definition.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            bool hasId = false;
            bool hasLanguage = false;
            foreach (JsonProperty member in definition.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "@id":
                        if (member.Value.ValueKind != JsonValueKind.String ||
                            !string.Equals(
                                member.Value.GetString(), iri, StringComparison.Ordinal))
                        {
                            return false;
                        }
                        hasId = true;
                        break;
                    case "@language":
                        if (member.Value.ValueKind != JsonValueKind.Null)
                        {
                            return false;
                        }
                        hasLanguage = true;
                        break;
                    default:
                        return false;
                }
            }
            return hasId && hasLanguage;
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
                case UAReferenceType:
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:referenceType");
                    writer.WriteEndArray();
                    break;
                case UADataType:
                    // The definition itself travels in uav:dataTypeDefinitions
                    // (§6.11). The annotation is still needed so the way back
                    // knows what NodeClass this document projects, or the
                    // DataType returns as an ObjectType.
                    writer.WritePropertyName("@type");
                    writer.WriteStartArray();
                    writer.WriteStringValue(WotVocabulary.ThingModelType);
                    writer.WriteStringValue("uav:dataType");
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

        private static void WriteAffordances(
            Utf8JsonWriter writer,
            UANodeSet nodeSet,
            UANode? root,
            List<WotDiagnostic> diagnostics,
            WotNodeSetConverterOptions options,
            string defaultLocale,
            string? parentHref = null,
            string? typeDefinitionHref = null,
            IReadOnlyDictionary<string, string>? eventTypeHrefs = null,
            string? documentHref = null)
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
            var nestedParents = new Dictionary<string, string>(StringComparer.Ordinal);

            // HasComponent subtypes (for example HasOrderedComponent) are
            // surfaced for discovery under uav:hasComponent / uav:componentOf and
            // additionally pinned by a link whose rel is the semantic
            // ReferenceType model name and whose uav:refId is the definitive
            // ExpandedNodeId (WoT Binding Sections 5.1.2 and 5.3). Every other
            // reference the readable mapping does not carry structurally - a
            // companion model's own ReferenceType, ua:HasInterface, ua:Organizes
            // - is written as the same kind of typed link, in the direction the
            // source states it.
            var componentChildren = new List<string>();
            var componentParents = new List<string>();
            var typedComponentLinks = new List<TypedComponentLink>();
            WotReferenceTypeNames referenceTypeNames = WotReferenceTypeNames.Build(nodeSet);

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
                else if (!IsStructuralReference(reference))
                {
                    WriteArbitraryTypedLink(
                        reference,
                        root,
                        index,
                        namespaceUris,
                        referenceTypeNames,
                        typedComponentLinks,
                        diagnostics);
                }
            }

            bool isThingModel = root is UAObjectType or UAVariableType;
            int affordanceCount = 0;

            // The event affordance names have to be settled before the actions
            // are written: Section 13.4 ties a Condition Method to the event
            // affordance it acts on *by name*, and the actions come first in
            // the object being written. Naming them here, once, is also what
            // keeps the name an action states and the name the event is written
            // under from ever drifting apart.
            var eventKeys = new List<string>(events.Count);
            var eventProjections = new List<WotConditionProjection>(events.Count);
            var eventNames = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < events.Count; ii++)
            {
                eventKeys.Add(UniqueKey(LocalName(events[ii].BrowseName), eventNames));
                eventProjections.Add(ResolveConditionProjection(events[ii], index));
            }

            // A Condition Method OPC 10000-9 declares is a component of the
            // ConditionType, so it hangs off the EventType rather than off the
            // Thing. Collecting it here is what surfaces it as an action at
            // all, and the type that owns it is what names the event the action
            // acts on - definitely, and for any number of Condition events.
            var conditionActions = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int ii = 0; ii < events.Count; ii++)
            {
                foreach (Reference reference in events[ii].References ?? [])
                {
                    if (!reference.IsForward ||
                        reference.Value is null ||
                        !IsComponentReference(reference.ReferenceType) ||
                        !index.TryGetValue(reference.Value, out UANode? owned) ||
                        owned is not UAMethod ownedMethod ||
                        ownedMethod.NodeId is null ||
                        ConditionActionOf(ownedMethod) is null ||
                        conditionActions.ContainsKey(ownedMethod.NodeId))
                    {
                        continue;
                    }
                    actions.Add(ownedMethod);
                    conditionActions[ownedMethod.NodeId] = eventKeys[ii];
                }
            }

            int eventBudget = Math.Max(
                0,
                options.MaxAffordanceCount - properties.Count - actions.Count);
            var conditionEventKeys = new List<string>();
            for (int ii = 0; ii < events.Count && ii < eventBudget; ii++)
            {
                if (eventProjections[ii].IsCondition)
                {
                    conditionEventKeys.Add(eventKeys[ii]);
                }
            }

            // §9.1 maps a Method's arguments to the action's input and output
            // DataSchemas, so an argument Variable the schemas fully represent
            // is not also emitted as a sibling property: the same Node would
            // then be stated twice, once as an argument and once as a property
            // of the Thing.
            var representedArguments = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, WotMethodArguments> methodArguments =
                CollectMethodArguments(actions, index, representedArguments);

            // A Variable may itself hold Variables — EURange and
            // EngineeringUnits sit below an analog Variable, two levels under
            // the Node that roots this document. An argument Variable whose
            // value this direction cannot decode is held the same way. Walking
            // only the root's references leaves all of them unreachable, so they
            // are collected here and stated as properties of the same Thing,
            // each naming the Node it belongs to (§9.1's `uav:componentOf`).
            CollectOwnedVariables(
                actions, properties, index, nestedParents, namespaceUris, representedArguments);
            CollectNestedVariables(properties, index, nestedParents, namespaceUris);

            WriteComponentArray(writer, "uav:hasComponent", componentChildren);
            WriteComponentArray(writer, "uav:componentOf", componentParents);

            // Section 6.4 makes the unit of a Variable a pointer to the
            // affordance its EngineeringUnits Property projects to, so the
            // affordance names have to be settled before anything is written -
            // a pointer at a name chosen later would name nothing.
            var propertyNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var propertyKeys = new List<string>(properties.Count);
            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (UAVariable variable in properties)
            {
                string key = UniqueKey(LocalName(variable.BrowseName), usedPropertyNames);
                propertyKeys.Add(key);
                if (variable.NodeId is { Length: > 0 } propertyNodeId)
                {
                    propertyNames[propertyNodeId] = key;
                }
            }
            Dictionary<string, WotAnalogFacets> analogFacets =
                CollectAnalogFacets(properties, index, propertyNames);

            if (properties.Count > 0)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                for (int ii = 0; ii < properties.Count; ii++)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    UAVariable variable = properties[ii];
                    writer.WritePropertyName(propertyKeys[ii]);
                    nestedParents.TryGetValue(variable.NodeId ?? string.Empty, out string? owner);
                    analogFacets.TryGetValue(
                        variable.NodeId ?? string.Empty, out WotAnalogFacets? facets);
                    WriteVariableAffordance(
                        writer, variable, isThingModel, namespaceUris, nodeSet, defaultLocale,
                        facets, owner);
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
                    methodArguments.TryGetValue(
                        method.NodeId ?? string.Empty, out WotMethodArguments arguments);
                    conditionActions.TryGetValue(
                        method.NodeId ?? string.Empty, out string? owningEventKey);
                    (string, string)? condition = TryResolveConditionAffordance(
                        method, owningEventKey, conditionEventKeys, ref arguments,
                        diagnostics, out string conditionAction, out string actsOn)
                        ? (conditionAction, actsOn)
                        : null;
                    WriteMethodAffordance(
                        writer, method, namespaceUris, nodeSet, defaultLocale, arguments,
                        condition);
                }
                writer.WriteEndObject();
            }

            if (events.Count > 0)
            {
                writer.WritePropertyName("events");
                writer.WriteStartObject();
                for (int ii = 0; ii < events.Count; ii++)
                {
                    if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                    {
                        break;
                    }
                    writer.WritePropertyName(eventKeys[ii]);
                    WriteEventAffordance(
                        writer, events[ii], namespaceUris, nodeSet, index, defaultLocale,
                        eventProjections[ii],
                        ResolveEventTypeHref(events[ii], eventTypeHrefs, documentHref));
                }
                writer.WriteEndObject();
            }

            WriteTypedComponentLinks(
                writer, typedComponentLinks, parentHref, typeDefinitionHref);
        }

        /// <summary>
        /// Writes a Variable's <c>Value</c> as the property's <c>const</c>.
        /// </summary>
        /// <remarks>
        /// A NodeSet <c>Value</c> is a UA-XML fragment. Only the scalar shapes
        /// this converter can rebuild exactly are carried, because a value the
        /// forward direction could not reconstruct would turn a gap the
        /// completeness check reports into a value that is quietly wrong.
        /// </remarks>
        private static void WriteVariableValue(Utf8JsonWriter writer, UAVariable variable)
        {
            System.Xml.XmlElement? value = variable.Value;
            if (value is null)
            {
                return;
            }
            switch (value.LocalName)
            {
                case "Boolean":
                    if (bool.TryParse(value.InnerText, out bool flag))
                    {
                        writer.WriteBoolean("const", flag);
                    }
                    return;
                case "String":
                    writer.WriteString("const", value.InnerText);
                    return;
                case "LocalizedText":
                    // A LocalizedText carries an optional Locale. Only the
                    // Locale-free form maps onto a plain string, so one that
                    // states a Locale is left to the projection.
                    if (value.ChildNodes.Count == 1 &&
                        string.Equals(
                            value.FirstChild?.LocalName, "Text", StringComparison.Ordinal))
                    {
                        writer.WriteString("const", value.FirstChild!.InnerText);
                    }
                    return;
                default:
                    return;
            }
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
            if (!NodeSetDeclaredAliases.FromNodeSet(nodeSet).TryResolve(
                dataType!, out string resolved))
            {
                resolved = dataType!;
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

                // Section 6.2 makes uav:refName the name the target is exposed
                // under where that differs from the BrowseName the target
                // declares for itself, and explicitly optional: a converter
                // uses the target's own BrowseName when it is absent. A target
                // this NodeSet does not hold has no name to state here, and
                // restating its NodeId as a name would state a BrowseName it
                // never had.
                if (link.RefName.Length != 0)
                {
                    writer.WriteString("uav:refName", link.RefName);
                }
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

        /// <summary>
        /// Adds the Variables a Method holds that its action's schemas do not
        /// already represent, as properties naming the Method.
        /// </summary>
        /// <remarks>
        /// §9.1 maps a Method to an action, and its arguments belong in that
        /// action's input and output schemas, which
        /// <see cref="CollectMethodArguments"/> derives from the
        /// <c>Argument</c> structures the argument Variables hold. A Variable
        /// those schemas represent is therefore skipped here. Anything else the
        /// Method holds - an argument list whose value this direction cannot
        /// decode, or a Property of its own - is still carried readably in its
        /// own right, with the Method it belongs to stated, so no Node is lost
        /// and none is re-parented.
        /// </remarks>
        private static void CollectOwnedVariables(
            List<UAMethod> actions,
            List<UAVariable> properties,
            Dictionary<string, UANode> index,
            Dictionary<string, string> nestedParents,
            string[]? namespaceUris,
            HashSet<string> representedArguments)
        {
            foreach (UAMethod method in actions)
            {
                if (method.References is null || method.NodeId is null)
                {
                    continue;
                }
                string? portableOwner = ToPortableNodeId(method.NodeId, namespaceUris);
                foreach (Reference reference in method.References)
                {
                    if (reference.Value is null ||
                        !reference.IsForward ||
                        !IsComponentReference(reference.ReferenceType) ||
                        !index.TryGetValue(reference.Value, out UANode? target) ||
                        target is not UAVariable argument ||
                        argument.NodeId is null ||
                        representedArguments.Contains(argument.NodeId))
                    {
                        continue;
                    }
                    properties.Add(argument);
                    if (!string.IsNullOrEmpty(portableOwner))
                    {
                        nestedParents[argument.NodeId] = portableOwner!;
                    }
                }
            }
        }

        /// <summary>
        /// Adds the Variables held by Variables already collected, to any depth.
        /// </summary>
        /// <remarks>
        /// The walk is breadth-first over the list being built, so a child
        /// discovered here is itself examined for children. Each addition
        /// records the Variable it belongs to, which is what lets the reverse
        /// direction re-parent it rather than hanging it off the Thing.
        /// </remarks>
        private static void CollectNestedVariables(
            List<UAVariable> properties,
            Dictionary<string, UANode> index,
            Dictionary<string, string> nestedParents,
            string[]? namespaceUris)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (UAVariable variable in properties)
            {
                if (variable.NodeId is not null)
                {
                    seen.Add(variable.NodeId);
                }
            }
            for (int ii = 0; ii < properties.Count; ii++)
            {
                UAVariable parent = properties[ii];
                if (parent.References is null || parent.NodeId is null)
                {
                    continue;
                }
                string? portableParent = ToPortableNodeId(parent.NodeId, namespaceUris);
                foreach (Reference reference in parent.References)
                {
                    if (reference.Value is null ||
                        !reference.IsForward ||
                        !IsComponentReference(reference.ReferenceType) ||
                        !index.TryGetValue(reference.Value, out UANode? target) ||
                        target is not UAVariable child ||
                        child.NodeId is null ||
                        !seen.Add(child.NodeId))
                    {
                        continue;
                    }
                    properties.Add(child);
                    if (!string.IsNullOrEmpty(portableParent))
                    {
                        nestedParents[child.NodeId] = portableParent!;
                    }
                }
            }
        }

        private static void WriteVariableAffordance(
            Utf8JsonWriter writer,
            UAVariable variable,
            bool isThingModel,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            string defaultLocale,
            WotAnalogFacets? analogFacets = null,
            string? componentOf = null)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", isThingModel ? "uav:variableType" : "uav:variable");
            if (componentOf is not null)
            {
                writer.WritePropertyName("uav:componentOf");
                writer.WriteStartArray();
                writer.WriteStringValue(componentOf);
                writer.WriteEndArray();
            }
            WriteLocalizedTitle(writer, variable.DisplayName, defaultLocale);
            WriteLocalizedDescription(writer, variable.Description, defaultLocale);
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

            // Section 6.4 makes the affordance a EngineeringUnits Property
            // projects a string-valued one: what a client reads there at run
            // time is the unit string, and the EUInformation structure behind
            // it travels under uav:engineeringUnits with the definitive
            // DataType of Section 5.4 alongside.
            bool isUnitAffordance = IsUnitAffordance(variable);
            string? jsonType = isUnitAffordance
                ? "string"
                : MapDataTypeToJson(variable.DataType);
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

            // §9.1 maps a Variable's DataType together with its ValueRank and
            // ArrayDimensions. The json type says only whether the value is an
            // array, so the rank is what separates a scalar from a
            // one-dimensional array of the same type, and from the three ranks
            // that fix neither.
            WriteVariableRank(writer, variable.ValueRank, variable.ArrayDimensions);

            // §9.1 maps a Variable's Value onto the property's value. Only the
            // shapes this converter can rebuild exactly are written: emitting a
            // value it could not reconstruct would trade a reported gap for a
            // silent corruption.
            WriteVariableValue(writer, variable);

            // Sections 6.4 and 6.4.1: the engineering unit, its authority and
            // identity, and the two ranges that say what the value means.
            WriteAnalogFacets(writer, analogFacets, defaultLocale);
            WriteEngineeringUnits(writer, variable, defaultLocale);

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
            string[]? namespaceUris,
            UANodeSet nodeSet,
            string defaultLocale,
            WotMethodArguments arguments,
            (string Action, string ActsOn)? condition)
        {
            writer.WriteStartObject();
            writer.WriteString("@type", "uav:method");
            WriteLocalizedTitle(writer, method.DisplayName, defaultLocale);
            WriteLocalizedDescription(writer, method.Description, defaultLocale);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(method.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(method.NodeId, namespaceUris));

            // Section 13.4: a Method OPC 10000-9 declares on a ConditionType is
            // invoked through the ordinary WoT action, and the two terms are
            // what say which Method it is and which Condition it acts on.
            if (condition is { } pairing)
            {
                writer.WriteString(ConditionActionTerm, pairing.Action);
                writer.WriteString(ActsOnTerm, pairing.ActsOn);
            }

            // §9.1: the Method's input and output arguments are the action's
            // input and output DataSchemas.
            WriteArgumentSchema(writer, InputMember, arguments.Input, nodeSet, defaultLocale);
            WriteArgumentSchema(writer, OutputMember, arguments.Output, nodeSet, defaultLocale);
            WriteModellingRule(writer, method);
            writer.WriteEndObject();
        }

        private static void WriteEventAffordance(
            Utf8JsonWriter writer,
            UANode eventType,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            Dictionary<string, UANode> index,
            string defaultLocale,
            WotConditionProjection projection,
            string? eventTypeHref = null)
        {
            writer.WriteStartObject();
            // Section 5.2: @type uav:eventType is the sole annotation that
            // records an EventType projection. WoT Binding 1.1 defines no
            // parallel boolean flag, so nothing else states event identity.
            writer.WriteString("@type", WotVocabulary.EventTypeAnnotation);
            WriteLocalizedTitle(writer, eventType.DisplayName, defaultLocale);
            WriteLocalizedDescription(writer, eventType.Description, defaultLocale);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(eventType.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(eventType.NodeId, namespaceUris));

            // Section 6.1: where the set carries a Thing Model of the EventType
            // Node itself, the affordance names it and a consumer derives every
            // select clause from that definition's data. The link is written
            // only where the definition is a document a consumer can reach: a
            // reference to a document the set does not hold would state a fast
            // path nothing can follow.
            if (!string.IsNullOrEmpty(eventTypeHref))
            {
                writer.WriteString(
                    WotEventSelectClauses.TypeDefinitionReferenceTerm, eventTypeHref!);
            }

            // Sections 13.2 and 13.3: the ConditionType the event projects and
            // the notification schema its fields fill.
            WriteEventConditionAndData(
                writer, eventType, projection, namespaceUris, nodeSet, index, defaultLocale);
            WriteModellingRule(writer, eventType);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Names the document that carries the EventType definition of one
        /// event affordance, relative to the document being written
        /// (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// A document that projects the EventType itself does not reference
        /// itself: its own root <em>is</em> the definition, and a self-reference
        /// would be a cycle a consumer has to reject. The hrefs of a document
        /// set are its keys and carry no path structure, so the reference is
        /// the target href as the set states it.
        /// </remarks>
        private static string? ResolveEventTypeHref(
            UANode eventType,
            IReadOnlyDictionary<string, string>? eventTypeHrefs,
            string? documentHref)
        {
            if (eventTypeHrefs is null ||
                eventType.NodeId is not { Length: > 0 } nodeId ||
                !eventTypeHrefs.TryGetValue(nodeId, out string? href) ||
                string.IsNullOrEmpty(href) ||
                string.Equals(href, documentHref, StringComparison.Ordinal))
            {
                return null;
            }
            return href;
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

        /// <summary>
        /// Chooses the Node a NodeSet's readable projection is written about.
        /// </summary>
        /// <remarks>
        /// An EventType another Node in the same document generates is never
        /// that Node: it is a declaration that Node <em>uses</em>, and Section
        /// 9.1 projects it as an event affordance of the Node that generates
        /// it. Without that exclusion a NodeSet holding one Object and the
        /// EventType it raises would be written about the EventType, and the
        /// Object - with its properties, its actions and the event itself -
        /// would not appear at all.
        /// </remarks>
        private static UANode? SelectRootNode(UANodeSet nodeSet)
        {
            if (nodeSet.Items is null || nodeSet.Items.Length == 0)
            {
                return null;
            }
            HashSet<string> generated = CollectGeneratedEventTypes(nodeSet);
            return FirstOf<UAObjectType>(nodeSet, generated)
                ?? FirstOf<UAObject>(nodeSet, generated)
                ?? FirstOf<UAVariableType>(nodeSet, generated)
                ?? FirstOf<UAType>(nodeSet, generated)
                ?? nodeSet.Items[0];
        }

        /// <summary>
        /// Collects the EventTypes some Node of the NodeSet generates.
        /// </summary>
        private static HashSet<string> CollectGeneratedEventTypes(UANodeSet nodeSet)
        {
            var generated = new HashSet<string>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items!)
            {
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.IsForward &&
                        reference.Value is { Length: > 0 } target &&
                        IsGeneratesEventReference(reference.ReferenceType))
                    {
                        generated.Add(target);
                    }
                }
            }
            return generated;
        }

        private static UANode? FirstOf<T>(UANodeSet nodeSet, HashSet<string> generated)
            where T : UANode
        {
            foreach (UANode node in nodeSet.Items!)
            {
                if (node is T && !generated.Contains(node.NodeId ?? string.Empty))
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

        /// <summary>
        /// The InverseName of the ReferenceType a document projects.
        /// </summary>
        /// <remarks>
        /// OPC 10000-3 gives a ReferenceType a second name, and WoT Binding
        /// Section 5.1.2 makes that name the one a link <c>rel</c> uses to state
        /// the relation backwards. A document describing a ReferenceType must
        /// therefore carry it, or the local context built from that document
        /// could resolve only half of what the ReferenceType answers to and
        /// every inverse relation of a companion model would lose its
        /// direction.
        /// </remarks>
        internal const string InverseNameTerm = "uav:inverseName";

        /// <summary>
        /// Whether the ReferenceType a document projects is symmetric, in which
        /// case its two directions are the same relation.
        /// </summary>
        internal const string SymmetricTerm = "uav:symmetric";

        /// <summary>
        /// Writes the names a projected ReferenceType answers to, beyond the
        /// BrowseName every Node carries.
        /// </summary>
        private static void WriteReferenceTypeNames(
            Utf8JsonWriter writer,
            UANode? root,
            string defaultLocale)
        {
            if (root is not UAReferenceType referenceType)
            {
                return;
            }
            string? inverseName = SelectLocalizedValue(referenceType.InverseName, defaultLocale);
            if (!string.IsNullOrEmpty(inverseName))
            {
                writer.WriteString(InverseNameTerm, inverseName);
            }
            if (referenceType.Symmetric)
            {
                writer.WriteBoolean(SymmetricTerm, true);
            }
        }

        /// <summary>
        /// Gets whether a reference is already carried by a readable term, so
        /// restating it as a typed link would state the same fact twice.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 6.2 says a Reference is a single relation and a
        /// document shall not be treated as declaring two, so the references the
        /// mapping already carries structurally - containment as affordances and
        /// <c>uav:hasComponent</c>/<c>uav:componentOf</c>, the type hierarchy as
        /// <c>tm:extends</c>, the type definition as
        /// <c>ua:HasTypeDefinition</c>, the modelling rule as
        /// <c>uav:modellingRule</c>, the event source as an event affordance,
        /// and a DataType's encodings as <c>uav:defaultEncodingId</c> - are not
        /// written again. Everything else is, because nothing else carries it.
        /// </remarks>
        private static bool IsStructuralReference(Reference reference)
        {
            return IsComponentReference(reference.ReferenceType) ||
                IsGeneratesEventReference(reference.ReferenceType) ||
                IsReferenceTypeNamed(reference.ReferenceType, "HasSubtype", WotVocabulary.HasSubtype) ||
                IsReferenceTypeNamed(
                    reference.ReferenceType, "HasTypeDefinition", WotVocabulary.HasTypeDefinition) ||
                IsReferenceTypeNamed(
                    reference.ReferenceType, "HasModellingRule", WotVocabulary.HasModellingRule) ||
                IsReferenceTypeNamed(
                    reference.ReferenceType, "HasEncoding", WotVocabulary.HasEncoding) ||
                IsReferenceTypeNamed(reference.ReferenceType, "HasDescription", "i=39") ||
                IsReferenceTypeNamed(
                    reference.ReferenceType, "AlwaysGeneratesEvent", "i=3065");
        }

        private static bool IsReferenceTypeNamed(
            string? referenceType,
            string browseName,
            string nodeId)
        {
            return string.Equals(referenceType, browseName, StringComparison.Ordinal) ||
                string.Equals(referenceType, nodeId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Writes one reference of an arbitrary ReferenceType as the WoT
        /// Binding Section 6.2 typed link: the ReferenceType's compact model
        /// name in <c>rel</c>, its definitive ExpandedNodeId in
        /// <c>uav:refId</c>, and - where the reference runs inverse - the
        /// ReferenceType's InverseName, which is what states the direction.
        /// </summary>
        /// <remarks>
        /// A ReferenceType the NodeSet neither declares, aliases nor inherits
        /// from the base namespace has no name that a <c>rel</c> could carry,
        /// and neither has the inverse direction of one that states no
        /// InverseName. Both are reported rather than written under a
        /// substitute relation: a link whose <c>rel</c> named a different
        /// ReferenceType, or whose direction was silently reversed, would read
        /// as a fact the source never stated.
        /// </remarks>
        private static void WriteArbitraryTypedLink(
            Reference reference,
            UANode root,
            Dictionary<string, UANode> index,
            string[]? namespaceUris,
            WotReferenceTypeNames referenceTypeNames,
            List<TypedComponentLink> links,
            List<WotDiagnostic> diagnostics)
        {
            string? portableTarget = ToPortableNodeId(reference.Value, namespaceUris);
            if (string.IsNullOrEmpty(portableTarget))
            {
                return;
            }
            if (!referenceTypeNames.TryGetRelation(
                reference.ReferenceType,
                reference.IsForward,
                out string modelName,
                out string refId))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The {(reference.IsForward ? "forward" : "inverse")} reference of " +
                    $"type '{reference.ReferenceType}' from '{root.NodeId}' to " +
                    $"'{portableTarget}' has no compact model name here" +
                    (reference.IsForward
                        ? ", so it is not written as a readable link."
                        : " for its inverse direction, so it is not written as a " +
                        "readable link."),
                    new WotLocation(nodeId: root.NodeId, reference: reference.ReferenceType)));
                return;
            }
            links.Add(new TypedComponentLink(
                portableTarget!,
                modelName,
                refId,
                index.TryGetValue(reference.Value!, out UANode? target) &&
                    LocalName(target.BrowseName) is { Length: > 0 } local
                    ? local
                    : string.Empty));
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
            if (IsKnownBindingRelation(rel) ||
                !TrySplitCompactModelName(
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

        /// <summary>
        /// Gets whether a link's <c>rel</c> is a Binding term rather than a
        /// ReferenceType model name.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 9.1 spells the parent-placement relation
        /// <c>uav:componentOf</c> and declares <c>ua:ComponentOf</c> as an
        /// alias of it, so both name the same term. The alias reads as a
        /// compact model name whose local part is the InverseName of
        /// <c>HasComponent</c>; intercepting it here keeps it a binding term
        /// and stops it being realized a second time as a generic inverse
        /// typed link.
        /// </remarks>
        private static bool IsKnownBindingRelation(string rel)
        {
            return rel is ComponentOfRel or ComponentOfAliasRel;
        }

        /// <summary>
        /// The WoT Binding Section 9.1 parent-placement relation.
        /// </summary>
        internal const string ComponentOfRel = "uav:componentOf";

        /// <summary>
        /// The declared alias of <see cref="ComponentOfRel"/>.
        /// </summary>
        internal const string ComponentOfAliasRel = "ua:ComponentOf";

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

        /// <summary>
        /// Builds the identifier of a Node the conversion synthesizes, by the
        /// Annex G.1 formula every other side of the Binding uses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The identity is <c>ns=1;s=P</c>, where namespace index 1 of the
        /// synthesized NodeSet is the model's own NamespaceUri and <c>P</c> is
        /// the browse path in OPC 10000-4 Annex A.2 relative-path syntax. The
        /// NodeSet-local <c>ns=1</c> spelling is what a NodeSet file carries;
        /// <see cref="ToPortableNodeId"/> renders it as the Annex G.1
        /// <c>nsu=U;s=P</c> when it leaves the file, so the two are one
        /// identity written two ways.
        /// </para>
        /// <para>
        /// The path is built from elements rather than from a joined string,
        /// because the joining is where the algorithm lives: the leading
        /// separator, the per-element namespace qualification, and the Annex
        /// A.2 escaping that stops a name containing <c>/</c> from imitating a
        /// path separator. A member named <c>A/B</c> of <c>Root</c> and a
        /// member named <c>B</c> of <c>Root/A</c> shared one identifier
        /// without it.
        /// </para>
        /// </remarks>
        private static string GenerateNodeId(ArrayOf<WotBrowsePathElement> path)
        {
            return "ns=1;s=" + WotPortableIdentity.GenerateBrowsePath(path);
        }

        /// <summary>
        /// Names one element of a generated browse path in the model's own
        /// namespace.
        /// </summary>
        private static WotBrowsePathElement ModelElement(string modelUri, string name)
        {
            return new WotBrowsePathElement(modelUri, name);
        }

        /// <summary>
        /// Names one element of a generated browse path in the base OPC UA
        /// namespace, which Annex G.1 writes bare.
        /// </summary>
        private static WotBrowsePathElement BaseElement(string name)
        {
            return new WotBrowsePathElement(null, name);
        }

        /// <summary>
        /// Gets the NamespaceUri the synthesized Nodes are created in, which is
        /// the one namespace index 1 of the NodeSet names.
        /// </summary>
        /// <remarks>
        /// Every caller passes a NodeSet this conversion seeded, and seeding
        /// always writes the model's NamespaceUri first, so the table is never
        /// empty here.
        /// </remarks>
        private static string GeneratedNamespaceUri(UANodeSet nodeSet)
        {
            return nodeSet.NamespaceUris![0];
        }

        /// <summary>
        /// Builds the identifier of a member of the projection root in a stated
        /// namespace, for a caller that knows the model URI without holding the
        /// NodeSet the conversion seeds.
        /// </summary>
        private static string GenerateMemberNodeId(
            string modelUri, string rootLocal, string local)
        {
            return GenerateNodeId(new ArrayOf<WotBrowsePathElement>(
                [ModelElement(modelUri, rootLocal), ModelElement(modelUri, local)]));
        }

        /// <summary>
        /// Builds the identifier of the Node a document projects as its root.
        /// </summary>
        private static string GenerateRootNodeId(UANodeSet nodeSet, string rootLocal)
        {
            string modelUri = GeneratedNamespaceUri(nodeSet);
            return GenerateNodeId(new ArrayOf<WotBrowsePathElement>(
                [ModelElement(modelUri, rootLocal)]));
        }

        /// <summary>
        /// Builds the identifier of a member of the projection root, whose
        /// BrowseName is in the model's own namespace.
        /// </summary>
        private static string GenerateMemberNodeId(
            UANodeSet nodeSet, string rootLocal, string local)
        {
            string modelUri = GeneratedNamespaceUri(nodeSet);
            return GenerateNodeId(new ArrayOf<WotBrowsePathElement>(
                [ModelElement(modelUri, rootLocal), ModelElement(modelUri, local)]));
        }

        /// <summary>
        /// Builds the identifier of a Node nested under a member, whose
        /// BrowseName is in the model's own namespace.
        /// </summary>
        private static string GenerateNestedNodeId(
            UANodeSet nodeSet, string rootLocal, string ownerLocal, string local)
        {
            string modelUri = GeneratedNamespaceUri(nodeSet);
            return GenerateNodeId(new ArrayOf<WotBrowsePathElement>(
            [
                ModelElement(modelUri, rootLocal),
                ModelElement(modelUri, ownerLocal),
                ModelElement(modelUri, local)
            ]));
        }

        /// <summary>
        /// Builds the identifier of a standard child - <c>InputArguments</c>,
        /// <c>EURange</c> and the like - whose BrowseName OPC 10000-5 declares
        /// in the base namespace, which Annex G.1 writes bare.
        /// </summary>
        private static string GenerateBaseChildNodeId(
            UANodeSet nodeSet, string rootLocal, string ownerLocal, string baseName)
        {
            string modelUri = GeneratedNamespaceUri(nodeSet);
            return GenerateNodeId(new ArrayOf<WotBrowsePathElement>(
            [
                ModelElement(modelUri, rootLocal),
                ModelElement(modelUri, ownerLocal),
                BaseElement(baseName)
            ]));
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
        internal static string? ToPortableNodeId(string? rawNodeId, string[]? namespaceUris)
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
            catch (ArgumentException)
            {
                // A NodeSet attribute may hold an alias name rather than a
                // NodeId. It is not portable, but it is also not this method's
                // to reject: hand it back unchanged so a caller enriching from
                // an attribute cannot be made to throw by ordinary input.
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

        internal static string? LocalName(string? browseName)
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
            bool IsExtends,
            bool IsForward);
    }
}
