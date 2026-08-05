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
    /// WoT Thing Model and Thing Description to NodeSet2 synthesis for the
    /// <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, throwing on any error diagnostic.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        /// <exception cref="FormatException">Thrown when the conversion fails.</exception>
        public static UANodeSet ToNodeSet(
            WotDocument document,
            WotNodeSetConverterOptions? options = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(document, options, null, null, diagnostics);
            ThrowIfErrors(diagnostics);
            return nodeSet
                ?? throw new FormatException("The WoT document could not be converted to a NodeSet.");
        }

        /// <summary>
        /// Parses and restores or synthesizes a NodeSet2 document from UTF-8 WoT
        /// JSON, throwing on any error diagnostic.
        /// </summary>
        /// <param name="utf8Json">The UTF-8 encoded WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The restored or synthesized NodeSet2 document.</returns>
        public static UANodeSet ToNodeSet(
            ReadOnlyMemory<byte> utf8Json,
            WotNodeSetConverterOptions? options = null)
        {
            using WotDocument document = WotDocument.Parse(utf8Json, options);
            return ToNodeSet(document, options);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, returning structured diagnostics together with the result.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">An optional resolver for referenced TD/TM documents.</param>
        /// <param name="resolutionContext">An optional resolution context for cycle and limit tracking.</param>
        /// <param name="cancellationToken">A token that cancels asynchronous resolution.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        public static async ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options = null,
            IWotThingResolver? thingResolver = null,
            WotResolutionContext? resolutionContext = null,
            CancellationToken cancellationToken = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            WotThingCatalog? thingCatalog = null;
            if (thingResolver is not null)
            {
                options ??= new WotNodeSetConverterOptions();
                options.Validate();
                resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());
                thingCatalog = new WotThingCatalog();
                await PreresolveThingReferencesAsync(
                    document,
                    options,
                    thingResolver,
                    resolutionContext,
                    thingCatalog,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
            }
            UANodeSet? nodeSet = ToNodeSetCore(
                document, options, thingCatalog, resolutionContext, diagnostics);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, returning structured diagnostics together with the result.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        public static WotConversionResult<UANodeSet> ToNodeSetResult(
            WotDocument document,
            WotNodeSetConverterOptions? options = null)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            UANodeSet? nodeSet = ToNodeSetCore(
                document, options, null, null, diagnostics);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        private static UANodeSet? ToNodeSetCore(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            WotThingCatalog? thingCatalog,
            WotResolutionContext? resolutionContext,
            List<WotDiagnostic> diagnostics)
        {
            options ??= new WotNodeSetConverterOptions();
            options.Validate();

            // Exactly one resolution context is created per top-level
            // conversion, seeded from the converter options, and threaded
            // through every context/schema/thing/link resolution below. It
            // must never be re-created per link so that depth, document
            // count, cycle and cumulative byte bounds apply across the whole
            // conversion rather than resetting for each resolved reference.
            resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());

            if (document.TryGetEnvelope(out JsonElement envelope))
            {
                UANodeSet? restored = RestoreFromEnvelope(envelope, options, diagnostics);
                if (restored is null)
                {
                    return null;
                }
                if (document.TryGetNativeProjection(out JsonElement projection))
                {
                    ValidateNativeConsistency(restored, projection, options, diagnostics);
                }
                WotJsonResidue.Replace(restored, document, options, diagnostics);
                return restored;
            }

            if (document.TryGetNativeProjection(out JsonElement nativeProjection))
            {
                UANodeSet? restored = WotNativeProjection.Read(
                    nativeProjection,
                    options,
                    diagnostics);
                if (restored is not null)
                {
                    WotJsonResidue.Replace(restored, document, options, diagnostics);
                }
                return restored;
            }

            UANodeSet? synthesized =
                Synthesize(document, options, thingCatalog, resolutionContext, diagnostics);
            if (synthesized is not null)
            {
                WotJsonResidue.Replace(synthesized, document, options, diagnostics);
            }
            return synthesized;
        }

        private static UANodeSet? RestoreFromEnvelope(
            JsonElement envelope,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var location = new WotLocation(jsonPointer: "/uav:nodeSet");

            if (!TryGetString(envelope, "contentType", out string? contentType) ||
                !string.Equals(contentType, WotVocabulary.NodeSetContentType, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedContentType,
                    $"Unsupported NodeSet content type '{contentType}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "encoding", out string? encoding) ||
                !string.Equals(encoding, WotVocabulary.Base64Encoding, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.UnsupportedEncoding,
                    $"Unsupported NodeSet encoding '{encoding}'.",
                    location));
                return null;
            }

            if (!TryGetString(envelope, "data", out string? data) || data is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.EnvelopeInvalid,
                    "The uav:nodeSet data value is required and must be a string.",
                    location));
                return null;
            }

            byte[] nodeSetBytes;
            try
            {
                nodeSetBytes = System.Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidBase64,
                    "The uav:nodeSet data is not valid base64.",
                    location));
                return null;
            }

            if (nodeSetBytes.Length > options.MaxNodeSetSize)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NodeSetTooLarge,
                    $"Decoded NodeSet exceeds the configured {options.MaxNodeSetSize} byte limit.",
                    location));
                return null;
            }

            // uav:nodeSet.sha256 is mandatory: a preservation envelope without
            // an integrity digest cannot be trusted and must not yield a
            // NodeSet, regardless of whether the payload otherwise parses.
            if (!envelope.TryGetProperty("sha256", out JsonElement digestElement) ||
                digestElement.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidDigest,
                    "The uav:nodeSet sha256 value is required and must be a string.",
                    location));
                return null;
            }

            if (!TryParseDigest(digestElement.GetString()!, out byte[] expected))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidDigest,
                    "The uav:nodeSet sha256 value is not a valid SHA-256 digest.",
                    location));
                return null;
            }

            byte[] actual = ComputeSha256(nodeSetBytes);
            if (!FixedEquals(expected, actual))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DigestMismatch,
                    "The uav:nodeSet digest does not match the payload.",
                    location));
                return null;
            }

            UANodeSet? nodeSet;
            try
            {
                using (var stream = new MemoryStream(nodeSetBytes, writable: false))
                {
                    nodeSet = UANodeSet.Read(stream);
                }
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                    System.Xml.XmlException or
                    FormatException)
            {
                // XmlSerializer wraps parse failures in InvalidOperationException;
                // treat any deserialization failure as a structured diagnostic
                // rather than letting the exception escape the converter.
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.MalformedNodeSet,
                    $"The uav:nodeSet payload is not a valid NodeSet2 document: {ex.Message}",
                    location));
                return null;
            }
            if (nodeSet is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.MalformedNodeSet,
                    "The uav:nodeSet payload is not a valid NodeSet2 document.",
                    location));
            }
            return nodeSet;
        }

        private static void ValidateNativeConsistency(
            UANodeSet baseline,
            JsonElement projection,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            var nativeDiagnostics = new List<WotDiagnostic>();
            UANodeSet? projected = WotNativeProjection.Read(
                projection,
                options,
                nativeDiagnostics);
            if (projected is null || HasErrors(nativeDiagnostics))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    FirstDiagnosticMessage(nativeDiagnostics) ??
                    "The native projection could not be reconstructed."));
                return;
            }

            NodeSetComparisonResult comparison =
                NodeSetComparer.Compare(baseline, projected, options);
            if (!comparison.AreEquivalent)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NativeProjectionConflict,
                    comparison.Differences.Count > 0
                        ? comparison.Differences[0]
                        : "The native projection conflicts with the preservation baseline."));
            }
        }

        private static UANodeSet? Synthesize(
            WotDocument document,
            WotNodeSetConverterOptions options,
            WotThingCatalog? thingCatalog,
            WotResolutionContext resolutionContext,
            List<WotDiagnostic> diagnostics)
        {
            WotDocumentKind kind = document.Kind;
            if (kind == WotDocumentKind.Unknown)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NoConvertibleContent,
                    "The document is neither a Thing Model nor a Thing Description and carries no preservation envelope or native projection."));
                return null;
            }

            bool isThingModel = kind == WotDocumentKind.ThingModel;
            bool isEventType = isThingModel && HasEventTypeAnnotation(document);

            // Portable identity and event-annotation validation (WoT Binding
            // Sections 5.1.1 and 5.2). Runs before synthesis so a document that
            // uses the session-local ns=<index> form or contradicts itself is
            // diagnosed; the exact uav:nodeSet envelope and uav:nodes projection
            // are never reached here and keep their own namespace indices.
            ValidatePortableIdentity(document, diagnostics);
            ValidateEventAnnotations(document, diagnostics);
            ValidateModelConceptNames(document, diagnostics);
            ValidateModelVocabulary(document, diagnostics);

            string modelUri = DeriveModelUri(document);
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            string? authoredRootId = GetUavString(document, "id");
            string rootNodeId = GenerateNodeId(rootLocal);

            var nodeSet = new UANodeSet
            {
                NamespaceUris = [modelUri],
                Models =
                [
                    new ModelTableEntry { ModelUri = modelUri }
                ]
            };
            if (authoredRootId is not null)
            {
                rootNodeId = ToNodeSetNodeId(authoredRootId, nodeSet, diagnostics);
            }
            else
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Info,
                    WotDiagnosticCode.GeneratedNodeId,
                    "NodeIds were generated deterministically from the target namespace and browse paths.",
                    new WotLocation(nodeId: rootNodeId)));
            }

            var items = new List<UANode>();
            var rootReferences = new List<Reference>();

            UANode rootNode;
            if (isThingModel)
            {
                rootNode = new UAObjectType { IsAbstract = false };
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    // An event-type Thing Model (@type uav:eventType) derives
                    // from BaseEventType rather than BaseObjectType.
                    Value = isEventType
                        ? WotVocabulary.BaseEventType
                        : WotVocabulary.BaseObjectType
                });
            }
            else
            {
                rootNode = new UAObject();
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = WotVocabulary.BaseObjectType
                });
            }

            rootNode.NodeId = rootNodeId;
            string? rootBrowseName = GetUavString(document, "browseName");
            rootNode.BrowseName = rootBrowseName is null
                ? "1:" + rootLocal
                : ToNodeSetQualifiedName(
                    document,
                    rootBrowseName,
                    nodeSet,
                    diagnostics);
            rootNode.DisplayName = MakeText(document.Title ?? rootLocal);
            string? rootDescription = GetRootString(document, "description");
            if (rootDescription is not null)
            {
                rootNode.Description = MakeText(rootDescription);
            }

            int affordanceCount = 0;

            foreach (KeyValuePair<string, JsonElement> property in document.Properties)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeProperty(
                    document, nodeSet, property.Key, property.Value, rootLocal,
                    rootNodeId, isThingModel,
                    items, rootReferences, diagnostics);
            }

            foreach (KeyValuePair<string, JsonElement> action in document.Actions)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeAction(
                    document, nodeSet, action.Key, action.Value, rootLocal,
                    rootNodeId, items, rootReferences, diagnostics);
            }

            foreach (KeyValuePair<string, JsonElement> eventAffordance in document.Events)
            {
                if (!CheckAffordanceBudget(ref affordanceCount, options, diagnostics))
                {
                    break;
                }
                SynthesizeEvent(
                    document, nodeSet, eventAffordance.Key, eventAffordance.Value,
                    rootLocal, items, rootReferences, diagnostics);
            }

            // A ReferenceType relation whose target is also listed under
            // uav:hasComponent / uav:componentOf pins the exact subtype of that
            // component (WoT Binding Section 5.3). Collect those pins once so the
            // link pass does not also emit a separate generic reference and the
            // component pass recreates the exact ReferenceType.
            Dictionary<string, string> componentTypedRefs =
                CollectComponentTypedRefs(document, diagnostics);
            SynthesizeLinks(
                document, rootReferences, componentTypedRefs, thingCatalog,
                resolutionContext, options, diagnostics);
            SynthesizeComponentArrays(document, rootReferences, componentTypedRefs);

            rootNode.References = [.. rootReferences];
            items.Insert(0, rootNode);
            nodeSet.Items = [.. items];
            return nodeSet;
        }

        private static void SynthesizeProperty(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement schema,
            string rootLocal,
            string rootNodeId,
            bool isThingModel,
            List<UANode> items,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            string local = LocalName(GetElementString(schema, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(schema, "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateNodeId(rootLocal + "/" + local)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(schema, "uav:browseName");
            var variable = new UAVariable
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? "1:" + local
                    : ToNodeSetQualifiedName(
                        document,
                        authoredBrowseName,
                        nodeSet,
                        diagnostics),
                ParentNodeId = rootNodeId,
                DataType = MapJsonSchemaToDataType(schema),
                AccessLevel = MapAccessLevel(schema)
            };
            string? title = GetElementString(schema, "title");
            if (title is not null)
            {
                variable.DisplayName = MakeText(title);
            }
            string? description = GetElementString(schema, "description");
            if (description is not null)
            {
                variable.Description = MakeText(description);
            }

            var references = new List<Reference>
            {
                new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = WotVocabulary.BaseDataVariableType
                },
                new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = false,
                    Value = rootNodeId
                }
            };
            AddModellingRule(schema, references);
            variable.References = [.. references];

            ReportUnsupportedSchema(schema, nodeId, diagnostics);

            items.Add(variable);
            rootReferences.Add(new Reference
            {
                ReferenceType = "HasComponent",
                IsForward = true,
                Value = nodeId
            });
            _ = isThingModel;
        }

        private static void SynthesizeAction(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement action,
            string rootLocal,
            string rootNodeId,
            List<UANode> items,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            string local = LocalName(GetElementString(action, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(action, "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateNodeId(rootLocal + "/" + local)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(action, "uav:browseName");
            var method = new UAMethod
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? "1:" + local
                    : ToNodeSetQualifiedName(
                        document,
                        authoredBrowseName,
                        nodeSet,
                        diagnostics),
                ParentNodeId = rootNodeId
            };
            string? title = GetElementString(action, "title");
            if (title is not null)
            {
                method.DisplayName = MakeText(title);
            }

            var references = new List<Reference>
            {
                new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = false,
                    Value = rootNodeId
                }
            };
            AddModellingRule(action, references);
            method.References = [.. references];

            items.Add(method);
            rootReferences.Add(new Reference
            {
                ReferenceType = "HasComponent",
                IsForward = true,
                Value = nodeId
            });
        }

        private static void SynthesizeEvent(
            WotDocument document,
            UANodeSet nodeSet,
            string key,
            JsonElement eventAffordance,
            string rootLocal,
            List<UANode> items,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            string local = LocalName(GetElementString(eventAffordance, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(
                eventAffordance,
                "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateNodeId(rootLocal + "/" + local)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(
                eventAffordance,
                "uav:browseName");
            var eventType = new UAObjectType
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? "1:" + local
                    : ToNodeSetQualifiedName(
                        document,
                        authoredBrowseName,
                        nodeSet,
                        diagnostics),
                IsAbstract = false
            };
            string? title = GetElementString(eventAffordance, "title");
            if (title is not null)
            {
                eventType.DisplayName = MakeText(title);
            }
            eventType.References =
            [
                new Reference
                {
                    ReferenceType = "HasSubtype",
                    IsForward = false,
                    Value = WotVocabulary.BaseEventType
                }
            ];

            items.Add(eventType);
            rootReferences.Add(new Reference
            {
                ReferenceType = "GeneratesEvent",
                IsForward = true,
                Value = nodeId
            });
        }

        private static void SynthesizeLinks(
            WotDocument document,
            List<Reference> rootReferences,
            Dictionary<string, string> componentTypedRefs,
            WotThingCatalog? thingCatalog,
            WotResolutionContext resolutionContext,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            foreach (ResolvableThingReference thingReference in EnumerateResolvableThingReferences(
                document,
                componentTypedRefs,
                diagnostics))
            {
                if (thingReference.IsExtends)
                {
                    if (TryResolveTargetNodeId(
                        thingReference.Reference,
                        thingCatalog,
                        resolutionContext,
                        options,
                        diagnostics,
                        out string extendsTarget))
                    {
                        SetSuperType(rootReferences, extendsTarget);
                    }
                    continue;
                }

                if (TryResolveTargetNodeId(
                    thingReference.Reference,
                    thingCatalog,
                    resolutionContext,
                    options,
                    diagnostics,
                    out string linkTarget))
                {
                    rootReferences.Add(new Reference
                    {
                        ReferenceType = thingReference.ReferenceType!,
                        IsForward = true,
                        Value = linkTarget
                    });
                }
            }
        }

        private static IEnumerable<ResolvableThingReference>
            EnumerateResolvableThingReferences(
                WotDocument document,
                Dictionary<string, string> componentTypedRefs,
                List<WotDiagnostic> diagnostics)
        {
            foreach (JsonElement link in document.Links)
            {
                string? rel = GetElementString(link, "rel");
                string? href = GetElementString(link, "href");
                if (rel is null || href is null)
                {
                    continue;
                }

                if (string.Equals(rel, "tm:extends", StringComparison.Ordinal))
                {
                    yield return new ResolvableThingReference(href, null, true);
                    continue;
                }

                if (!IsReferenceRel(document, link, rel))
                {
                    continue;
                }

                // A typed link that pins the subtype of a listed component is
                // realized by the component pass, not here, so the component is
                // not emitted twice (WoT Binding Section 5.3).
                if (componentTypedRefs.ContainsKey(href))
                {
                    continue;
                }

                if (TryResolveLinkReferenceType(
                    document,
                    link,
                    rel,
                    diagnostics,
                    out string referenceType))
                {
                    yield return new ResolvableThingReference(href, referenceType, false);
                }
            }
        }

        private static bool TryResolveLinkReferenceType(
            WotDocument document,
            JsonElement link,
            string rel,
            List<WotDiagnostic> diagnostics,
            out string referenceType)
        {
            string? modelName = IsModelConceptRelation(document, link, rel)
                ? rel
                : null;
            string? definitive = GetElementString(link, "uav:refId");
            string? canonicalDefinitive = CanonicalReferenceType(definitive);
            if (definitive is not null && canonicalDefinitive is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The uav:refId value '{definitive}' is not a portable " +
                    "ExpandedNodeId.",
                    new WotLocation(reference: definitive)));
                referenceType = string.Empty;
                return false;
            }

            if (modelName is not null &&
                TryResolveReferenceTypeName(
                    document,
                    modelName,
                    out string resolvedName))
            {
                if (canonicalDefinitive is not null &&
                    !string.Equals(
                        resolvedName,
                        canonicalDefinitive,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ModelConceptConflict,
                        $"The ReferenceType model name '{modelName}' resolves to " +
                        $"'{resolvedName}' but uav:refId is '{definitive}'.",
                        new WotLocation(reference: modelName)));
                    referenceType = string.Empty;
                    return false;
                }
                referenceType = resolvedName;
                return true;
            }

            if (canonicalDefinitive is not null)
            {
                referenceType = canonicalDefinitive;
                return true;
            }

            if (modelName is not null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The ReferenceType relation '{modelName}' could not be " +
                    "resolved and has no ExpandedNodeId fallback.",
                    new WotLocation(reference: modelName)));
                referenceType = string.Empty;
                return false;
            }

            referenceType = DefaultReferenceType(rel);
            return true;
        }

        private static string? CanonicalReferenceType(string? referenceType)
        {
            if (string.IsNullOrEmpty(referenceType))
            {
                return null;
            }
            return IsNodeId(referenceType) ? referenceType : null;
        }

        private static bool TryResolveReferenceTypeName(
            WotDocument document,
            string modelName,
            out string referenceType)
        {
            referenceType = string.Empty;
            if (!TrySplitCompactModelName(
                modelName,
                out string prefix,
                out string browseName) ||
                !TryGetContextNamespace(document, prefix, out string namespaceUri))
            {
                return false;
            }
            return string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal) &&
                WotVocabulary.TryGetReferenceTypeNodeId(
                    browseName,
                    out referenceType);
        }

        private static bool TrySplitCompactModelName(
            string value,
            out string prefix,
            out string browseName)
        {
            prefix = string.Empty;
            browseName = string.Empty;
            int separator = -1;
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == ':')
                {
                    separator = ii;
                    break;
                }
            }
            if (separator <= 0 || separator + 1 >= value.Length)
            {
                return false;
            }
            string candidate = value.Substring(0, separator);
            if (!IsAsciiLetter(candidate[0]) && candidate[0] != '_')
            {
                return false;
            }
            for (int ii = 0; ii < candidate.Length; ii++)
            {
                char character = candidate[ii];
                if (!IsAsciiLetter(character) &&
                    character is not (>= '0' and <= '9') &&
                    character is not ('_' or '.' or '-'))
                {
                    return false;
                }
            }
            prefix = candidate;
            browseName = value.Substring(separator + 1);
            return true;
        }

        private static bool IsAsciiLetter(char value)
        {
            return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        private static bool TryGetContextNamespace(
            WotDocument document,
            string prefix,
            out string namespaceUri)
        {
            if (string.Equals(prefix, "ua", StringComparison.Ordinal))
            {
                namespaceUri = WotVocabulary.OpcUaNamespace;
                return true;
            }
            if (string.Equals(prefix, "uav", StringComparison.Ordinal))
            {
                namespaceUri = WotVocabulary.VocabularyNamespace;
                return true;
            }
            if (document.TryGetContext(out JsonElement context) &&
                TryGetContextNamespace(context, prefix, out namespaceUri))
            {
                return true;
            }
            namespaceUri = string.Empty;
            return false;
        }

        private static bool TryGetContextNamespace(
            JsonElement context,
            string prefix,
            out string namespaceUri)
        {
            if (context.ValueKind == JsonValueKind.Object &&
                context.TryGetProperty(prefix, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                namespaceUri = value.GetString()!;
                return true;
            }
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    if (TryGetContextNamespace(entry, prefix, out namespaceUri))
                    {
                        return true;
                    }
                }
            }
            namespaceUri = string.Empty;
            return false;
        }

        /// <summary>
        /// Collects the subtype pins carried by ReferenceType model-name links
        /// whose target is also listed under <c>uav:hasComponent</c> or
        /// <c>uav:componentOf</c>: target ExpandedNodeId to the exact
        /// ReferenceType named by <c>rel</c> and, when needed,
        /// <c>uav:refId</c>
        /// (WoT Binding Section 5.3).
        /// </summary>
        private static Dictionary<string, string> CollectComponentTypedRefs(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            var pins = new Dictionary<string, string>(StringComparer.Ordinal);
            var componentTargets = new HashSet<string>(StringComparer.Ordinal);
            CollectComponentTargets(document, "hasComponent", componentTargets);
            CollectComponentTargets(document, "componentOf", componentTargets);
            if (componentTargets.Count == 0)
            {
                return pins;
            }
            foreach (JsonElement link in document.Links)
            {
                string? rel = GetElementString(link, "rel");
                if (rel is null ||
                    !IsModelConceptRelation(document, link, rel))
                {
                    continue;
                }
                string? href = GetElementString(link, "href");
                if (href is not null &&
                    componentTargets.Contains(href) &&
                    TryResolveLinkReferenceType(
                        document,
                        link,
                        rel,
                        diagnostics,
                        out string refType))
                {
                    pins[href] = refType;
                }
            }
            return pins;
        }

        private static void CollectComponentTargets(
            WotDocument document,
            string localName,
            HashSet<string> targets)
        {
            if (document.TryGetUav(localName, out JsonElement array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement target in array.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String &&
                        target.GetString() is { Length: > 0 } value)
                    {
                        targets.Add(value);
                    }
                }
            }
        }

        private static void SynthesizeComponentArrays(
            WotDocument document,
            List<Reference> rootReferences,
            Dictionary<string, string> componentTypedRefs)
        {
            if (document.TryGetUav("hasComponent", out JsonElement hasComponent) &&
                hasComponent.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement target in hasComponent.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String)
                    {
                        rootReferences.Add(new Reference
                        {
                            ReferenceType = ComponentReferenceType(target.GetString(), componentTypedRefs),
                            IsForward = true,
                            Value = target.GetString()
                        });
                    }
                }
            }
            if (document.TryGetUav("componentOf", out JsonElement componentOf) &&
                componentOf.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement target in componentOf.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.String)
                    {
                        rootReferences.Add(new Reference
                        {
                            ReferenceType = ComponentReferenceType(target.GetString(), componentTypedRefs),
                            IsForward = false,
                            Value = target.GetString()
                        });
                    }
                }
            }
        }

        private static string ComponentReferenceType(
            string? target,
            Dictionary<string, string> componentTypedRefs)
        {
            // A component whose exact subtype is pinned by a matching
            // ReferenceType link is recreated with that ReferenceType;
            // otherwise plain HasComponent is used (WoT Binding Section 5.3).
            if (target is not null && componentTypedRefs.TryGetValue(target, out string? refType))
            {
                return refType;
            }
            return "HasComponent";
        }

        private static async ValueTask PreresolveThingReferencesAsync(
            WotDocument document,
            WotNodeSetConverterOptions options,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotThingCatalog thingCatalog,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            if (document.TryGetEnvelope(out _) ||
                document.TryGetNativeProjection(out _) ||
                document.Kind == WotDocumentKind.Unknown)
            {
                return;
            }

            var discoveryDiagnostics = new List<WotDiagnostic>();
            Dictionary<string, string> componentTypedRefs =
                CollectComponentTypedRefs(document, discoveryDiagnostics);
            foreach ((string reference, _, _) in EnumerateResolvableThingReferences(
                document,
                componentTypedRefs,
                discoveryDiagnostics))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsNodeId(reference))
                {
                    continue;
                }

                string? nodeId = await ResolveTargetNodeIdAsync(
                    reference,
                    resolver,
                    context,
                    options,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                thingCatalog.Add(reference, nodeId);
            }
        }

        private static async ValueTask<string?> ResolveTargetNodeIdAsync(
            string reference,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var entered = new List<string>();
            try
            {
                string current = reference;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!context.TryEnter(WotResolutionKind.Thing, current, out WotDiagnostic? blocking))
                    {
                        diagnostics.Add(blocking!);
                        return null;
                    }
                    entered.Add(current);

                    WotResolverResult result = await resolver.ResolveThingAsync(
                        current,
                        context,
                        cancellationToken).ConfigureAwait(false);
                    if (!result.Found)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Warning,
                            WotDiagnosticCode.ResolverNotFound,
                            $"The referenced document '{current}' could not be resolved.",
                            new WotLocation(reference: current)));
                        return null;
                    }
                    if (!context.TryAddBytes(current, result.Content.Length, out WotDiagnostic? limit))
                    {
                        diagnostics.Add(limit!);
                        return null;
                    }

                    using WotDocument resolved = WotDocument.Parse(result.Content, options);
                    string? resolvedId = GetUavString(resolved, "id");
                    if (resolvedId is not null)
                    {
                        return resolvedId;
                    }
                    string? congruent = GetUavString(resolved, "congruentType");
                    if (congruent is not null &&
                        !string.Equals(congruent, current, StringComparison.Ordinal))
                    {
                        current = congruent;
                        continue;
                    }
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.UnresolvedReference,
                        $"The referenced document '{current}' does not declare a uav:id.",
                        new WotLocation(reference: current)));
                    return null;
                }
            }
            finally
            {
                for (int ii = entered.Count - 1; ii >= 0; ii--)
                {
                    context.Leave(entered[ii]);
                }
            }
        }

        private static bool TryResolveTargetNodeId(
            string reference,
            WotThingCatalog? thingCatalog,
            WotResolutionContext context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            out string nodeId)
        {
            if (IsNodeId(reference))
            {
                nodeId = reference;
                return true;
            }
            nodeId = string.Empty;
            if (thingCatalog is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnresolvedReference,
                    $"The reference '{reference}' could not be resolved to a NodeId without an external resolver.",
                    new WotLocation(reference: reference)));
                return false;
            }

            if (thingCatalog.TryTake(reference, out string? resolvedNodeId))
            {
                if (resolvedNodeId is null)
                {
                    return false;
                }
                nodeId = resolvedNodeId;
                return true;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Warning,
                WotDiagnosticCode.UnresolvedReference,
                $"The reference '{reference}' was not pre-resolved before synchronous conversion.",
                new WotLocation(reference: reference)));
            _ = context;
            _ = options;
            return false;
        }

        private static void ReportUnsupportedSchema(
            JsonElement schema,
            string nodeId,
            List<WotDiagnostic> diagnostics)
        {
            if (schema.TryGetProperty("uav:externalSchema", out JsonElement external) &&
                external.ValueKind == JsonValueKind.String)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The property references an external schema '{external.GetString()}' that was not inlined.",
                    WotLocation.FromNode(nodeId)));
                return;
            }
            string? type = GetElementString(schema, "type");
            if (string.Equals(type, "object", StringComparison.Ordinal) ||
                string.Equals(type, "array", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedSchema,
                    $"The '{type}' DataSchema was mapped to a generic DataType; a custom DataType may be required.",
                    WotLocation.FromNode(nodeId)));
            }
        }

        private static void AddModellingRule(JsonElement schema, List<Reference> references)
        {
            string? rule = GetElementString(schema, "uav:modellingRule");
            if (rule is not null && WotVocabulary.TryGetModellingRuleNodeId(rule, out string ruleNodeId))
            {
                references.Add(new Reference
                {
                    ReferenceType = "HasModellingRule",
                    IsForward = true,
                    Value = ruleNodeId
                });
            }
        }

        private static void SetSuperType(List<Reference> references, string target)
        {
            for (int ii = 0; ii < references.Count; ii++)
            {
                if (string.Equals(references[ii].ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                    !references[ii].IsForward)
                {
                    references[ii].Value = target;
                    return;
                }
            }
            references.Add(new Reference
            {
                ReferenceType = "HasSubtype",
                IsForward = false,
                Value = target
            });
        }

        private static string ToNodeSetQualifiedName(
            WotDocument document,
            string rawBrowseName,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (rawBrowseName.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = rawBrowseName.IndexOf(';', 4);
                if (delimiter < 0 || delimiter + 1 >= rawBrowseName.Length)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NonPortableQualifiedName,
                        $"The uav:browseName '{rawBrowseName}' is not a valid " +
                        "NamespaceUri-qualified QualifiedName."));
                    return rawBrowseName;
                }
                string namespaceUri = CoreUtils.UnescapeUri(
                    rawBrowseName.AsSpan(4, delimiter - 4));
                string name = rawBrowseName.Substring(delimiter + 1);
                if (string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal))
                {
                    return name;
                }
                int namespaceIndex = GetOrAppendNamespaceUri(nodeSet, namespaceUri);
                return namespaceIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                    ":" +
                    name;
            }

            int separator = -1;
            for (int ii = 0; ii < rawBrowseName.Length; ii++)
            {
                if (rawBrowseName[ii] == ':')
                {
                    separator = ii;
                    break;
                }
            }
            if (separator > 0)
            {
                bool numeric = true;
                for (int ii = 0; ii < separator; ii++)
                {
                    if (rawBrowseName[ii] is not (>= '0' and <= '9'))
                    {
                        numeric = false;
                        break;
                    }
                }
                if (numeric)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.NonPortableQualifiedName,
                        $"The uav:browseName '{rawBrowseName}' uses a numeric " +
                        "NamespaceIndex; persisted documents shall use a " +
                        "context prefix or nsu=<NamespaceUri>;<Name>.",
                        new WotLocation(reference: rawBrowseName)));
                    return rawBrowseName;
                }
                string prefix = rawBrowseName.Substring(0, separator);
                if (!TryGetContextNamespace(document, prefix, out string namespaceUri))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NonPortableQualifiedName,
                        $"The uav:browseName '{rawBrowseName}' uses an unbound " +
                        $"context prefix '{prefix}'.",
                        new WotLocation(reference: rawBrowseName)));
                    return rawBrowseName;
                }
                string name = rawBrowseName.Substring(separator + 1);
                if (string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal))
                {
                    return name;
                }
                int namespaceIndex = GetOrAppendNamespaceUri(nodeSet, namespaceUri);
                return namespaceIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                    ":" +
                    name;
            }
            return rawBrowseName;
        }

        private static string ToNodeSetNodeId(
            string portableNodeId,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (portableNodeId.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = portableNodeId.IndexOf(';', 4);
                if (delimiter < 0 || delimiter + 1 >= portableNodeId.Length)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ValidationError,
                        $"The NodeId '{portableNodeId}' is not a valid " +
                        "NamespaceUri-qualified NodeId."));
                    return portableNodeId;
                }
                string namespaceUri = CoreUtils.UnescapeUri(
                    portableNodeId.AsSpan(4, delimiter - 4));
                string identifier = portableNodeId.Substring(delimiter + 1);
                if (string.Equals(
                    namespaceUri,
                    WotVocabulary.OpcUaNamespace,
                    StringComparison.Ordinal))
                {
                    return identifier;
                }
                int namespaceIndex = GetOrAppendNamespaceUri(nodeSet, namespaceUri);
                return "ns=" +
                    namespaceIndex.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) +
                    ";" +
                    identifier;
            }
            return portableNodeId;
        }

        private static int GetOrAppendNamespaceUri(
            UANodeSet nodeSet,
            string namespaceUri)
        {
            if (nodeSet.NamespaceUris is not null)
            {
                for (int ii = 0; ii < nodeSet.NamespaceUris.Length; ii++)
                {
                    if (string.Equals(
                        nodeSet.NamespaceUris[ii],
                        namespaceUri,
                        StringComparison.Ordinal))
                    {
                        return ii + 1;
                    }
                }
            }
            var uris = nodeSet.NamespaceUris is null
                ? new List<string>()
                : new List<string>(nodeSet.NamespaceUris);
            uris.Add(namespaceUri);
            nodeSet.NamespaceUris = [.. uris];
            return uris.Count;
        }

        private static bool HasEventTypeAnnotation(WotDocument document)
        {
            foreach (string token in document.TypeTokens)
            {
                if (string.Equals(token, WotVocabulary.EventTypeAnnotation, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the projected root ObjectType derives from
        /// BaseEventType and therefore projects a UA EventType, annotated with
        /// <c>uav:eventType</c> (WoT Binding Section 5.2).
        /// </summary>
        private static bool IsEventTypeRoot(UANode? root, UANodeSet nodeSet)
        {
            if (root is not UAObjectType)
            {
                return false;
            }
            Dictionary<string, UANode> index = BuildIndex(nodeSet);
            UANode? current = root;
            int guard = index.Count + 1;
            while (current is UAObjectType && guard-- > 0)
            {
                string? superType = FindSuperTypeId(current);
                if (superType is null)
                {
                    return false;
                }
                if (string.Equals(superType, WotVocabulary.BaseEventType, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!index.TryGetValue(superType, out current))
                {
                    return false;
                }
            }
            return false;
        }

        private static string? FindSuperTypeId(UANode node)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (!reference.IsForward && reference.Value is not null &&
                    (string.Equals(reference.ReferenceType, "HasSubtype", StringComparison.Ordinal) ||
                     string.Equals(reference.ReferenceType, WotVocabulary.HasSubtype, StringComparison.Ordinal)))
                {
                    return reference.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Portable identity validation (WoT Binding Section 5.1.1): every
        /// NodeId-valued term (<c>uav:id</c>, each <c>uav:hasComponent</c> /
        /// <c>uav:componentOf</c> entry, <c>uav:mapToNodeId</c>,
        /// <c>uav:mapToType</c>, <c>uav:refId</c>, the
        /// <c>uav:browsePathAnchor</c> (Section 5.1.4), and a
        /// <c>?id=</c> href) shall be a portable ExpandedNodeId, never the
        /// session-local <c>ns=&lt;index&gt;</c> form. The exact <c>uav:nodeSet</c>
        /// envelope and <c>uav:nodes</c> projection subtrees are skipped so their
        /// own namespace indices - resolved through their own NamespaceUris table -
        /// are unaffected.
        /// </summary>
        private static void ValidatePortableIdentity(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            ValidatePortableIdentity(document.RootElement, diagnostics);
        }

        private static void ValidatePortableIdentity(
            JsonElement element,
            List<WotDiagnostic> diagnostics)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty member in element.EnumerateObject())
                    {
                        if (string.Equals(member.Name, "uav:nodeSet", StringComparison.Ordinal) ||
                            string.Equals(member.Name, "uav:nodes", StringComparison.Ordinal))
                        {
                            // Exact preservation subtrees keep their own indices.
                            continue;
                        }
                        CheckPortableMember(member.Name, member.Value, diagnostics);
                        ValidatePortableIdentity(member.Value, diagnostics);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        ValidatePortableIdentity(item, diagnostics);
                    }
                    break;
            }
        }

        private static void CheckPortableMember(
            string name,
            JsonElement value,
            List<WotDiagnostic> diagnostics)
        {
            switch (name)
            {
                case "uav:id":
                case "uav:mapToNodeId":
                case "uav:mapToType":
                case "uav:browsePathAnchor":
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        CheckPortableValue(name, value.GetString(), diagnostics);
                    }
                    break;
                case "uav:hasComponent":
                case "uav:componentOf":
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement entry in value.EnumerateArray())
                        {
                            if (entry.ValueKind == JsonValueKind.String)
                            {
                                CheckPortableValue(name + " entry", entry.GetString(), diagnostics);
                            }
                        }
                    }
                    break;
                case "uav:refId":
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        CheckPortableValue(name, value.GetString(), diagnostics);
                    }
                    break;
                case "href":
                    if (value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { } href)
                    {
                        int marker = href.IndexOf("?id=", StringComparison.Ordinal);
                        if (marker >= 0)
                        {
                            CheckPortableValue(
                                "href ?id=", href.Substring(marker + 4), diagnostics);
                        }
                    }
                    break;
            }
        }

        private static void CheckPortableValue(
            string term,
            string? value,
            List<WotDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            if (!IsNodeId(value!))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    $"The NodeId-valued term {term} uses '{value}', which is not " +
                    "an ExpandedNodeId.",
                    new WotLocation(reference: value)));
                return;
            }
            int marker = value!.IndexOf("ns=", StringComparison.Ordinal);
            if (marker >= 0 &&
                marker + 3 < value.Length &&
                char.IsDigit(value[marker + 3]) &&
                (marker == 0 || value[marker - 1] is ';' or '='))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.NonPortableIdentity,
                    $"The portable identity term {term} uses the session-local " +
                    $"ns=<index> form ('{value}'); a persisted document shall use an " +
                    "ExpandedNodeId (nsu=<NamespaceUri>;... or namespace-0 i=...) " +
                    "so it survives a namespace-table reordering (WoT Binding Section 5.1.1).",
                    new WotLocation(reference: value)));
            }
        }

        private static void ValidateModelConceptNames(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            ValidateModelConceptNames(
                document,
                document.RootElement,
                diagnostics);
        }

        private static void ValidateModelConceptNames(
            WotDocument document,
            JsonElement element,
            List<WotDiagnostic> diagnostics)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                ValidateReferenceTypeRelation(document, element, diagnostics);
                ValidateModelConceptMember(
                    document,
                    element,
                    "uav:mapToTypeName",
                    requiredDefinitiveMember: "uav:mapToType",
                    diagnostics);
                ValidateModelConceptMember(
                    document,
                    element,
                    "uav:congruentTypeName",
                    requiredDefinitiveMember: "uav:congruentType",
                    diagnostics);
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (member.Name is not ("uav:nodeSet" or "uav:nodes"))
                    {
                        ValidateModelConceptNames(
                            document,
                            member.Value,
                            diagnostics);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ValidateModelConceptNames(document, item, diagnostics);
                }
            }
        }

        private static void ValidateModelConceptMember(
            WotDocument document,
            JsonElement element,
            string memberName,
            string? requiredDefinitiveMember,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(memberName, out JsonElement member))
            {
                return;
            }
            string? value = member.ValueKind == JsonValueKind.String
                ? member.GetString()
                : null;
            if (value is null ||
                !TrySplitCompactModelName(
                    value,
                    out string prefix,
                    out _) ||
                !TryGetContextNamespace(document, prefix, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The {memberName} value '{value}' is not a compact model name " +
                    "whose non-numeric prefix is bound in @context.",
                    new WotLocation(reference: value)));
            }
            if (requiredDefinitiveMember is not null &&
                !element.TryGetProperty(requiredDefinitiveMember, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"{memberName} requires {requiredDefinitiveMember}.",
                    new WotLocation(reference: value)));
            }
        }

        private static void ValidateReferenceTypeRelation(
            WotDocument document,
            JsonElement element,
            List<WotDiagnostic> diagnostics)
        {
            string? rel = GetElementString(element, "rel");
            if (rel is null)
            {
                return;
            }
            if (IsKnownBindingRelation(rel) ||
                rel.StartsWith("http:", StringComparison.Ordinal) ||
                rel.StartsWith("https:", StringComparison.Ordinal) ||
                rel.StartsWith("urn:", StringComparison.Ordinal))
            {
                return;
            }
            if (!TrySplitCompactModelName(
                    rel,
                    out string prefix,
                    out _) ||
                prefix == "tm")
            {
                return;
            }
            if (prefix == "uav")
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The Binding relation '{rel}' is not defined.",
                    new WotLocation(reference: rel)));
                return;
            }
            if (IsExternalRelationPrefix(prefix) ||
                !IsModelConceptCandidate(element, prefix))
            {
                return;
            }
            if (!TryGetContextNamespace(document, prefix, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ModelConceptUnresolved,
                    $"The ReferenceType relation '{rel}' uses a prefix that is " +
                    "not bound in @context.",
                    new WotLocation(reference: rel)));
            }
        }

        /// <summary>
        /// Event-annotation consistency (WoT Binding Section 5.2): an event
        /// affordance annotated <c>@type: uav:eventType</c> shall not set
        /// <c>uav:isEvent: false</c>; the two forms record the same fact.
        /// </summary>
        private static void ValidateEventAnnotations(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                JsonElement node = affordance.Value;
                if (node.ValueKind != JsonValueKind.Object || !HasEventTypeType(node))
                {
                    continue;
                }
                if (node.TryGetProperty("uav:isEvent", out JsonElement isEvent) &&
                    isEvent.ValueKind == JsonValueKind.False)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.EventAnnotationConflict,
                        $"The event affordance '{affordance.Key}' is annotated " +
                        "@type uav:eventType but sets uav:isEvent: false; the two " +
                        "forms record the same EventType projection (WoT Binding Section 5.2).",
                        WotLocation.FromPointer("/events/" + affordance.Key)));
                }
            }
        }

        private static bool HasEventTypeType(JsonElement node)
        {
            if (!node.TryGetProperty("@type", out JsonElement type))
            {
                return false;
            }
            if (type.ValueKind == JsonValueKind.String)
            {
                return string.Equals(
                    type.GetString(), WotVocabulary.EventTypeAnnotation, StringComparison.Ordinal);
            }
            if (type.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement token in type.EnumerateArray())
                {
                    if (token.ValueKind == JsonValueKind.String &&
                        string.Equals(
                            token.GetString(), WotVocabulary.EventTypeAnnotation, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string DeriveModelUri(WotDocument document)
        {
            string? uavId = GetUavString(document, "id");
            if (uavId is not null)
            {
                const string marker = "nsu=";
                if (uavId.StartsWith(marker, StringComparison.Ordinal))
                {
                    int semicolon = uavId.IndexOf(';', marker.Length);
                    string ns = semicolon < 0
                        ? uavId.Substring(marker.Length)
                        : uavId.Substring(marker.Length, semicolon - marker.Length);
                    if (ns.Length > 0)
                    {
                        return ns;
                    }
                }
            }
            string? id = document.Id;
            if (!string.IsNullOrEmpty(id))
            {
                return id!;
            }
            return "urn:opcua:wot:synthesized";
        }

        private static string MapJsonSchemaToDataType(JsonElement schema)
        {
            return WotVocabulary.MapJsonTypeToDataType(GetElementString(schema, "type"));
        }

        private static uint MapAccessLevel(JsonElement schema)
        {
            bool readOnly = GetElementBool(schema, "readOnly");
            bool writeOnly = GetElementBool(schema, "writeOnly");
            uint access = 0;
            if (!writeOnly)
            {
                access |= AccessLevelCurrentRead;
            }
            if (!readOnly)
            {
                access |= AccessLevelCurrentWrite;
            }
            return access == 0 ? AccessLevelCurrentRead : access;
        }

    }
}
