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
            ApplyIdentifierLeniency(diagnostics, options);
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
        public static ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options = null,
            IWotThingResolver? thingResolver = null,
            WotResolutionContext? resolutionContext = null,
            CancellationToken cancellationToken = default)
        {
            return ToNodeSetResultAsync(
                document, options, thingResolver, resolutionContext, null, cancellationToken);
        }

        /// <summary>
        /// Restores or synthesizes the NodeSet2 document described by a WoT
        /// document, resolving names against the WoT Binding Section 5.1.5
        /// local context.
        /// </summary>
        /// <param name="document">The WoT document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <param name="thingResolver">Resolves referenced TD/TM documents.</param>
        /// <param name="resolutionContext">The active resolution context.</param>
        /// <param name="nodeResolver">
        /// Resolves a name or identifier to the OPC UA Node it names. When
        /// omitted nothing is held, so a document that binds to an existing
        /// type is reported as unresolved rather than silently mistyped.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The conversion result and its diagnostics.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<WotConversionResult<UANodeSet>> ToNodeSetResultAsync(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            IWotThingResolver? thingResolver,
            WotResolutionContext? resolutionContext,
            IWotNodeResolver? nodeResolver,
            CancellationToken cancellationToken = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var diagnostics = new List<WotDiagnostic>();
            WotThingCatalog? thingCatalog = null;
            WotThingCatalog? parentCatalog = null;
            if (thingResolver is not null)
            {
                options ??= new WotNodeSetConverterOptions();
                options.Validate();
                resolutionContext ??= new WotResolutionContext(options.ToResolverOptions());
                thingCatalog = new WotThingCatalog();
                parentCatalog = new WotThingCatalog();
                await PreresolveThingReferencesAsync(
                    document,
                    options,
                    thingResolver,
                    resolutionContext,
                    thingCatalog,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                await PreresolveParentReferencesAsync(
                    document,
                    options,
                    thingResolver,
                    resolutionContext,
                    parentCatalog,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
            }

            // WoT Binding Section 5.2.1 only applies to synthesized Nodes.
            WotTypeBinding? typeBinding = null;
            WotParentPlacement? parentPlacement = null;
            if (!TakesRestorePath(document))
            {
                typeBinding = await ResolveTypeBindingAsync(
                    document,
                    nodeResolver ?? NullWotNodeResolver.Instance,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                parentPlacement = await ResolveParentPlacementAsync(
                    document,
                    parentCatalog,
                    nodeResolver ?? NullWotNodeResolver.Instance,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
            }

            UANodeSet? nodeSet = ToNodeSetCore(
                document,
                options,
                thingCatalog,
                resolutionContext,
                diagnostics,
                typeBinding,
                parentPlacement);
            ApplyIdentifierLeniency(diagnostics, options);
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
            ApplyIdentifierLeniency(diagnostics, options);
            return new WotConversionResult<UANodeSet>(nodeSet, diagnostics);
        }

        /// <summary>
        /// Relaxes the portable-identity rules when the caller opted in.
        /// </summary>
        /// <remarks>
        /// WoT Binding Sections 5.1.1 and 5.1.3 make the session-local
        /// <c>ns=&lt;index&gt;</c> NodeId form and a numeric namespace prefix in a
        /// browse name errors in release 1.1, where OPC 10101 v1.00 permitted
        /// both. A caller migrating a v1.00 document can set
        /// <see cref="WotNodeSetConverterOptions.AllowNonPortableIdentifiers"/>
        /// to keep reading it; the occurrences are then reported as warnings so
        /// the non-portable values still surface without failing the conversion.
        /// </remarks>
        /// <param name="diagnostics">The diagnostics collected so far.</param>
        /// <param name="options">The effective converter options.</param>
        private static void ApplyIdentifierLeniency(
            List<WotDiagnostic> diagnostics,
            WotNodeSetConverterOptions? options)
        {
            if (options?.AllowNonPortableIdentifiers != true)
            {
                return;
            }
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                WotDiagnostic diagnostic = diagnostics[ii];
                if (diagnostic.Severity != WotDiagnosticSeverity.Error ||
                    diagnostic.Code is not (WotDiagnosticCode.NonPortableIdentity
                        or WotDiagnosticCode.NonPortableQualifiedName))
                {
                    continue;
                }
                diagnostics[ii] = new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location);
            }
        }

        private static bool TakesRestorePath(WotDocument document)
        {
            return document.TryGetEnvelope(out _) || document.TryGetNativeProjection(out _);
        }

        /// <summary>
        /// Describes the ObjectType a Thing Model projects, without converting
        /// it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A Thing Model projects its root as a <c>UAObjectType</c>, so it is
        /// what a WoT Binding Section 5.2.1 type binding in a sibling document
        /// names. A Thing Description projects an instance and is therefore
        /// never a type-binding target, so this returns <c>false</c> for one.
        /// </para>
        /// <para>
        /// This exists so an <see cref="IWotNodeResolver"/> over a set of
        /// sibling documents can index them by identity without paying for a
        /// full conversion, and so that identity is derived by exactly the same
        /// rules the conversion uses rather than by a copy that can drift.
        /// </para>
        /// </remarks>
        /// <param name="document">The document to describe.</param>
        /// <param name="namespaceUri">
        /// The NamespaceUri the document projects into.
        /// </param>
        /// <param name="browseName">The projected type's unqualified BrowseName.</param>
        /// <param name="nodeId">
        /// The projected type's identity, as a portable ExpandedNodeId string.
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="document"/> is a Thing Model.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static bool TryDescribeProjectedType(
            WotDocument document,
            out string namespaceUri,
            out string browseName,
            out string nodeId)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            namespaceUri = string.Empty;
            browseName = string.Empty;
            nodeId = string.Empty;
            if (document.Kind != WotDocumentKind.ThingModel)
            {
                return false;
            }

            namespaceUri = DeriveModelUri(document);
            browseName = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";

            // An authored uav:id is already the portable form; otherwise the
            // conversion generates "ns=1;s=<root>" against a namespace table
            // whose index 1 is the model URI.
            nodeId = GetUavString(document, "id") ??
                "nsu=" + namespaceUri + ";s=" + browseName;
            return true;
        }

        private static bool TryDescribeProjectionRoot(WotDocument document, out string nodeId)
        {
            nodeId = string.Empty;
            if (document.Kind is not (WotDocumentKind.ThingModel or WotDocumentKind.ThingDescription))
            {
                return false;
            }

            string namespaceUri = DeriveModelUri(document);
            string browseName = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            nodeId = GetUavString(document, "id") ??
                "nsu=" + namespaceUri + ";s=" + browseName;
            return true;
        }

        private static UANodeSet? ToNodeSetCore(
            WotDocument document,
            WotNodeSetConverterOptions? options,
            WotThingCatalog? thingCatalog,
            WotResolutionContext? resolutionContext,
            List<WotDiagnostic> diagnostics,
            WotTypeBinding? typeBinding = null,
            WotParentPlacement? parentPlacement = null)
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
                    UANodeSet? projected = ValidateNativeConsistency(restored, projection, options, diagnostics);
                    if (projected is not null)
                    {
                        ValidateNativeAffordanceCoverage(document, projected, diagnostics);
                    }
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
                    ValidateNativeAffordanceCoverage(document, restored, diagnostics);
                    WotJsonResidue.Replace(restored, document, options, diagnostics);
                }
                return restored;
            }

            UANodeSet? synthesized =
                Synthesize(
                    document,
                    options,
                    thingCatalog,
                    resolutionContext,
                    diagnostics,
                    typeBinding,
                    parentPlacement);
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

        private static UANodeSet? ValidateNativeConsistency(
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
                return null;
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
            return projected;
        }

        private static void ValidateNativeAffordanceCoverage(
            WotDocument document,
            UANodeSet projected,
            List<WotDiagnostic> diagnostics)
        {
            if (document.Properties.Count == 0 &&
                document.Actions.Count == 0 &&
                document.Events.Count == 0)
            {
                return;
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var browseNames = new HashSet<string>(StringComparer.Ordinal);
            if (projected.Items is not null)
            {
                foreach (UANode node in projected.Items)
                {
                    if (!string.IsNullOrEmpty(node.NodeId))
                    {
                        nodeIds.Add(node.NodeId);
                    }
                    if (!string.IsNullOrEmpty(node.BrowseName))
                    {
                        browseNames.Add(node.BrowseName);
                    }
                }
            }

            var identityContext = new UANodeSet
            {
                NamespaceUris = projected.NamespaceUris is null
                    ? null
                    : (string[])projected.NamespaceUris.Clone()
            };
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";

            ValidateNativeAffordanceCoverage(
                document,
                document.Properties,
                "properties",
                "property",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
            ValidateNativeAffordanceCoverage(
                document,
                document.Actions,
                "actions",
                "action",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
            ValidateNativeAffordanceCoverage(
                document,
                document.Events,
                "events",
                "event",
                rootLocal,
                identityContext,
                nodeIds,
                browseNames,
                diagnostics);
        }

        private static void ValidateNativeAffordanceCoverage(
            WotDocument document,
            IReadOnlyDictionary<string, JsonElement> affordances,
            string collectionName,
            string affordanceKind,
            string rootLocal,
            UANodeSet identityContext,
            HashSet<string> nodeIds,
            HashSet<string> browseNames,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                if (IsNativeAffordanceCovered(
                    document,
                    affordance.Key,
                    affordance.Value,
                    rootLocal,
                    identityContext,
                    nodeIds,
                    browseNames))
                {
                    continue;
                }

                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.NativeProjectionUncoveredAffordance,
                    "The document carries uav:nodes, but its native projection " +
                    $"does not contain a node for the {affordanceKind} affordance " +
                    $"'{affordance.Key}'. The projection is authoritative, so " +
                    "that readable affordance does not contribute to the restored NodeSet.",
                    WotLocation.FromPointer(
                        "/" + collectionName + "/" + EscapeJsonPointerToken(affordance.Key))));
            }
        }

        private static bool IsNativeAffordanceCovered(
            WotDocument document,
            string key,
            JsonElement affordance,
            string rootLocal,
            UANodeSet identityContext,
            HashSet<string> nodeIds,
            HashSet<string> browseNames)
        {
            string local = LocalName(GetElementString(affordance, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(affordance, "uav:id");
            string expectedNodeId = authoredNodeId is null
                ? GenerateNodeId(rootLocal + "/" + local)
                : ToNodeSetNodeId(authoredNodeId, identityContext, []);
            if (authoredNodeId is not null)
            {
                return nodeIds.Contains(expectedNodeId);
            }

            string? authoredBrowseName = GetElementString(affordance, "uav:browseName");
            string expectedBrowseName = authoredBrowseName is null
                ? "1:" + local
                : ToNodeSetQualifiedName(document, authoredBrowseName, identityContext, []);
            return nodeIds.Contains(expectedNodeId) || browseNames.Contains(expectedBrowseName);
        }

        private static string EscapeJsonPointerToken(string token)
        {
            if (!token.Contains('~', StringComparison.Ordinal) &&
                !token.Contains('/', StringComparison.Ordinal))
            {
                return token;
            }
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        private static UANodeSet? Synthesize(
            WotDocument document,
            WotNodeSetConverterOptions options,
            WotThingCatalog? thingCatalog,
            WotResolutionContext resolutionContext,
            List<WotDiagnostic> diagnostics,
            WotTypeBinding? typeBinding,
            WotParentPlacement? parentPlacement)
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
            ValidateConditions(document, diagnostics);

            string modelUri = DeriveModelUri(document);
            string rootLocal = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title) ?? "Thing";
            string? authoredRootId = GetUavString(document, "id");
            string rootNodeId = GenerateNodeId(rootLocal);

            var nodeSet = new UANodeSet
            {
                NamespaceUris = SeedNamespaceUris(document, modelUri),
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
            string? boundType = null;
            if (isThingModel)
            {
                rootNode = new UAObjectType { IsAbstract = false };

                // WoT Binding Section 5.2.1 makes invalid and unresolved
                // type-binding outcomes document-wide. A Thing Model still
                // projects a type and derives through HasSubtype, so any
                // successfully bound NodeId is intentionally ignored here.
                boundType = ApplyTypeBinding(document, typeBinding, diagnostics);
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

                // WoT Binding Section 5.2.1: a document may bind its projected
                // node to a type that already exists. Only fall back to
                // BaseObjectType when it declares no binding at all - an
                // unresolved or invalid binding is reported and must not be
                // silently mistyped as BaseObjectType.
                boundType = ApplyTypeBinding(document, typeBinding, diagnostics);
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasTypeDefinition",
                    IsForward = true,
                    Value = boundType is not null
                        ? ToNodeSetNodeId(boundType, nodeSet, diagnostics)
                        : WotVocabulary.BaseObjectType
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
                resolutionContext, options, boundType, diagnostics);
            SynthesizeComponentArrays(document, rootReferences, componentTypedRefs);
            if (parentPlacement is { } placement)
            {
                rootReferences.Add(new Reference
                {
                    ReferenceType = "HasComponent",
                    IsForward = false,
                    Value = placement.ParentNodeId
                });
            }

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
                DataType = MapJsonSchemaToDataType(schema, nodeSet, diagnostics),
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
                    Value = ReadAffordanceTypeDefinition(schema, nodeSet, diagnostics)
                        ?? WotVocabulary.BaseDataVariableType
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

        /// <summary>
        /// Reads the definitive type-binding link an affordance carries.
        /// </summary>
        /// <remarks>
        /// <i>OPC UA — WoT Binding</i> §5.2.1 allows the link on an affordance
        /// as well as on the Thing. It is honoured here without a local-context
        /// lookup only where it names a Node of the OPC UA namespace, which is
        /// always loaded — <c>PropertyType</c>, <c>AnalogUnitType</c> and
        /// <c>TwoStateDiscreteType</c> are the ordinary cases. A link into any
        /// other namespace is a companion type that has to be resolved before it
        /// can be trusted, so it is left to the document-level path rather than
        /// written as a reference that may dangle.
        /// </remarks>
        private static string? ReadAffordanceTypeDefinition(
            JsonElement affordance,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("links", out JsonElement links) ||
                links.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object ||
                    !string.Equals(
                        GetElementString(link, "rel"), TypeBindingRel, StringComparison.Ordinal))
                {
                    continue;
                }
                string? href = GetElementString(link, "href");
                if (href is null || href.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    continue;
                }
                return ToNodeSetNodeId(href, nodeSet, diagnostics);
            }
            return null;
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
                    Value = ResolveConditionSupertype(
                        document, eventAffordance, key, nodeSet, diagnostics)
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
            string? boundType,
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
                        ValidateInstantiatedThingModelType(document, boundType, extendsTarget, diagnostics);
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

        private static void ValidateInstantiatedThingModelType(
            WotDocument document,
            string? boundType,
            string extendsTarget,
            List<WotDiagnostic> diagnostics)
        {
            if (document.Kind == WotDocumentKind.ThingModel ||
                boundType is null ||
                AreSameExpandedNodeId(boundType, extendsTarget))
            {
                return;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.InvalidTypeBinding,
                "The document instantiates a Thing Model that projects '" + extendsTarget +
                "', but its type binding resolves to '" + boundType + "'. The two must " +
                "resolve to the same type node (WoT Binding Section 5.2.1).",
                new WotLocation(jsonPointer: "/links")));
        }

        private static bool AreSameExpandedNodeId(string first, string second)
        {
            return string.Equals(
                NormalizeExpandedNodeId(first),
                NormalizeExpandedNodeId(second),
                StringComparison.Ordinal);
        }

        private static string NormalizeExpandedNodeId(string nodeId)
        {
            if (nodeId.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = nodeId.IndexOf(';', 4);
                if (delimiter > 4 && delimiter + 1 < nodeId.Length)
                {
                    string namespaceUri = CoreUtils.UnescapeUri(nodeId.AsSpan(4, delimiter - 4));
                    string identifier = NormalizeNamespaceZeroNodeId(nodeId.Substring(delimiter + 1));
                    return "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";" + identifier;
                }
            }

            return NormalizeNamespaceZeroNodeId(nodeId);
        }

        private static string NormalizeNamespaceZeroNodeId(string nodeId)
        {
            NodeId parsed;
            try
            {
                parsed = NodeId.Parse(nodeId);
            }
            catch (ServiceResultException)
            {
                return nodeId;
            }

            if (parsed.NamespaceIndex != 0)
            {
                return nodeId;
            }

            var buffer = new System.Text.StringBuilder();
            NodeId.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                buffer,
                parsed.IdentifierAsString,
                parsed.IdType,
                0);
            return buffer.ToString();
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

            // With uav:componentModel gone (spec PR #19), a binding link that
            // resolves to no ReferenceType organizes rather than composes.
            // Emitted as a NodeId, not a browse name: a NodeSet may only use a
            // name that it declares in <Aliases>, and the converter declares
            // none, so a bare name would fail to load.
            referenceType = WotVocabulary.Organizes;
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

        /// <summary>
        /// Seeds the target namespace table from the document's <c>@context</c>.
        /// </summary>
        /// <remarks>
        /// <i>OPC UA — WoT Binding</i> §9.1 maps the NodeSet namespace table onto
        /// the <c>@context</c> prefix bindings keyed by namespace index, so a
        /// document written from a NodeSet carries <c>ns1</c>…<c>nsN</c> in that
        /// order. Reading them back reproduces the source table instead of
        /// rebuilding it on demand from whichever identifiers happen to be
        /// converted, which is what makes a BrowseName keep the namespace it was
        /// written with and what lets the documents of one set agree on index.
        /// A gap in the sequence stops the seed: an index is only meaningful if
        /// every index below it is bound.
        /// </remarks>
        private static string[] SeedNamespaceUris(WotDocument document, string modelUri)
        {
            var uris = new List<string>();
            for (int index = 1; ; index++)
            {
                string prefix = "ns" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!TryGetContextNamespace(document, prefix, out string namespaceUri) ||
                    namespaceUri.Length == 0)
                {
                    break;
                }
                uris.Add(namespaceUri);
            }
            if (uris.Count == 0)
            {
                return [modelUri];
            }
            if (!uris.Contains(modelUri))
            {
                uris.Insert(0, modelUri);
            }
            return [.. uris];
        }

        private static bool TryGetContextNamespace(
            WotDocument document,
            string prefix,
            out string namespaceUri)
        {            if (string.Equals(prefix, "ua", StringComparison.Ordinal))
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

        /// <summary>
        /// Pre-resolves registry-document parent links without consuming the
        /// catalog used by ordinary authored references.
        /// </summary>
        private static async ValueTask PreresolveParentReferencesAsync(
            WotDocument document,
            WotNodeSetConverterOptions options,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotThingCatalog parentCatalog,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            foreach (JsonElement link in EnumerateComponentOfLinks(document))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? href = GetElementString(link, "href");
                if (href is null || IsNodeId(href) || LooksLikeBrowsePath(href))
                {
                    continue;
                }

                string? nodeId = await ResolveTargetNodeIdAsync(
                    href,
                    resolver,
                    context,
                    options,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                parentCatalog.Add(href, nodeId);
            }
        }

        private static async ValueTask<WotParentPlacement?> ResolveParentPlacementAsync(
            WotDocument document,
            WotThingCatalog? parentCatalog,
            IWotNodeResolver nodeResolver,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            JsonElement? componentOf = null;
            foreach (JsonElement link in EnumerateComponentOfLinks(document))
            {
                componentOf = link;
                break;
            }
            if (componentOf is null)
            {
                return null;
            }

            string? href = GetElementString(componentOf.Value, "href");
            if (string.IsNullOrEmpty(href))
            {
                AddUnresolvedParentPlacement(diagnostics, href, "The uav:componentOf link has no href.");
                return null;
            }

            if (parentCatalog is not null &&
                parentCatalog.TryTake(href, out string? registryNodeId) &&
                registryNodeId is not null)
            {
                return new WotParentPlacement(registryNodeId);
            }

            if (IsNodeId(href))
            {
                WotResolvedNode? addressSpaceNode = await nodeResolver
                    .ResolveByNodeIdAsync(href, cancellationToken).ConfigureAwait(false);
                if (addressSpaceNode is not null)
                {
                    return new WotParentPlacement(addressSpaceNode.Value.NodeId);
                }
            }

            AddUnresolvedParentPlacement(
                diagnostics,
                href,
                $"The uav:componentOf parent target '{href}' could not be resolved.");
            return null;
        }

        private static IEnumerable<JsonElement> EnumerateComponentOfLinks(WotDocument document)
        {
            foreach (JsonElement link in document.Links)
            {
                if (string.Equals(
                    GetElementString(link, "rel"),
                    "uav:componentOf",
                    StringComparison.Ordinal))
                {
                    yield return link;
                }
            }
        }

        private static void AddUnresolvedParentPlacement(
            List<WotDiagnostic> diagnostics,
            string? href,
            string message)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.UnresolvedParentPlacement,
                message,
                new WotLocation(jsonPointer: "/links", reference: href)));
        }

        private static bool LooksLikeBrowsePath(string value)
        {
            return value.Contains('/', StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves a link target to the projection root the referenced document
        /// declares or synthesizes.
        /// </summary>
        /// <remarks>
        /// A single resolution: spec PR #19 removed <c>uav:congruentType</c>,
        /// which was the only term that redirected one reference to another, so
        /// a target either declares its own identity or is unresolved. The
        /// resolution context is still entered and the bytes still counted, so
        /// the per-conversion document, depth and byte bounds continue to apply.
        /// </remarks>
        private static async ValueTask<string?> ResolveTargetNodeIdAsync(
            string reference,
            IWotThingResolver resolver,
            WotResolutionContext context,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.TryEnter(WotResolutionKind.Thing, reference, out WotDiagnostic? blocking))
            {
                diagnostics.Add(blocking!);
                return null;
            }

            try
            {
                WotResolverResult result = await resolver.ResolveThingAsync(
                    reference,
                    context,
                    cancellationToken).ConfigureAwait(false);
                if (!result.Found)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Warning,
                        WotDiagnosticCode.ResolverNotFound,
                        $"The referenced document '{reference}' could not be resolved.",
                        new WotLocation(reference: reference)));
                    return null;
                }
                if (!context.TryAddBytes(reference, result.Content.Length, out WotDiagnostic? limit))
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

                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnresolvedReference,
                    $"The referenced document '{reference}' does not declare a uav:id.",
                    new WotLocation(reference: reference)));
                return null;
            }
            finally
            {
                context.Leave(reference);
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
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NonPortableQualifiedName,
                        $"The uav:browseName '{rawBrowseName}' uses a numeric " +
                        "NamespaceIndex, which is not permitted in a persisted " +
                        "document; use a context prefix or " +
                        "nsu=<NamespaceUri>;<Name>.",
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

        /// <summary>
        /// Converts an authored portable NodeId into the NodeSet-local form.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 5.1.1 forbids the session-local
        /// <c>ns=&lt;index&gt;</c> form in every NodeId-valued term, because a
        /// namespace index is only meaningful for the session that read the
        /// namespace table. OPC 10101 v1.00 permitted it; release 1.1 rejects it
        /// so a document cannot silently bind to the wrong namespace when the
        /// table is reordered. Authors shall use
        /// <c>nsu=&lt;NamespaceUri&gt;;&lt;idtype&gt;=&lt;id&gt;</c> instead.
        /// </remarks>
        private static string ToNodeSetNodeId(
            string portableNodeId,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (IsSessionLocalNodeId(portableNodeId))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NonPortableIdentity,
                    $"The NodeId '{portableNodeId}' uses the session-local " +
                    "ns=<index> form, which is not permitted in a persisted " +
                    "document; use nsu=<NamespaceUri>;<idtype>=<id>.",
                    new WotLocation(reference: portableNodeId)));
                return portableNodeId;
            }
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

        /// <summary>
        /// Determines whether a NodeId string uses the session-local
        /// <c>ns=&lt;index&gt;</c> namespace form.
        /// </summary>
        /// <param name="nodeId">The authored NodeId string.</param>
        /// <returns>
        /// <c>true</c> when the value starts with <c>ns=</c> followed by at
        /// least one digit and a <c>;</c> separator.
        /// </returns>
        private static bool IsSessionLocalNodeId(string nodeId)
        {
            const string prefix = "ns=";
            if (!nodeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            int index = prefix.Length;
            int start = index;
            while (index < nodeId.Length && nodeId[index] is >= '0' and <= '9')
            {
                index++;
            }
            return index > start && index < nodeId.Length && nodeId[index] == ';';
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
        /// Resolves the EventType a projected event derives from.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WoT Binding Section 13 projects a Condition notification as an event
        /// affordance, and the type it derives from is the ConditionType the
        /// affordance names rather than <c>BaseEventType</c>. Getting this
        /// wrong would lose the Condition state model entirely: a Client
        /// browsing the projected type would see none of the Condition fields
        /// and could not tell an alarm from an ordinary event.
        /// </para>
        /// <para>
        /// Section 13.2 uses the hint-plus-pin pattern of Section 5.3.
        /// <c>uav:conditionTypeId</c> is definitive and wins; the compact name
        /// in <c>uav:conditionType</c> is a hint, resolved here for the four
        /// ConditionTypes Section 13.1 scopes. A name outside that set that
        /// carries no pin is reported rather than guessed, because deriving
        /// from the wrong supertype is worse than saying so.
        /// </para>
        /// </remarks>
        private static string ResolveConditionSupertype(
            WotDocument document,
            JsonElement eventAffordance,
            string key,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? pinned = GetElementString(eventAffordance, ConditionTypeIdTerm);
            if (pinned is not null)
            {
                return ToNodeSetNodeId(pinned, nodeSet, diagnostics);
            }

            string? hint = GetElementString(eventAffordance, ConditionTypeTerm);
            if (hint is null)
            {
                return WotVocabulary.BaseEventType;
            }

            // §13.2 names the ConditionType with a compact model name, which
            // §5.1.2 resolves through the document's @context rather than by
            // its literal prefix - an author may bind a second prefix to the
            // OPC UA namespace. Only that namespace resolves without a local
            // context; a companion ConditionType has to be pinned.
            if (TrySplitCompactModelName(hint, out string prefix, out string local) &&
                TryGetContextNamespace(document, prefix, out string namespaceUri) &&
                string.Equals(
                    namespaceUri, WotVocabulary.OpcUaNamespace, StringComparison.Ordinal) &&
                WotVocabulary.TryGetConditionTypeNodeId(local, out string nodeId))
            {
                return nodeId;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.UnresolvedConditionType,
                $"'{hint}' is not a ConditionType this Binding resolves. Pin it with " +
                $"'{ConditionTypeIdTerm}' (WoT Binding Section 13.2).",
                WotLocation.FromPointer("/events/" + key + "/" + ConditionTypeTerm)));
            return WotVocabulary.BaseEventType;
        }

        /// <summary>
        /// Reads the definitive type binding a document declares through
        /// <c>ua:HasTypeDefinition</c> links (WoT Binding Section 5.2.1).
        /// </summary>
        /// <remarks>
        /// Section 5.2.1 names a type in either or both of two forms. This
        /// reads the definitive one: a link whose <c>rel</c> is the
        /// <c>ua:HasTypeDefinition</c> ReferenceType compact model name and
        /// whose <c>href</c> is the ExpandedNodeId of the type. An
        /// ExpandedNodeId matches exactly one Node or none, so it needs no
        /// lookup here; the readable <c>@type</c> form is a hint that has to be
        /// resolved against the local context of Section 5.1.5 and is handled
        /// separately.
        /// <para>
        /// A Node has exactly one <c>HasTypeDefinition</c>, so more than one
        /// such link makes the document invalid rather than picking a winner.
        /// </para>
        /// </remarks>
        /// <returns>
        /// The authored ExpandedNodeId, or <c>null</c> when the document
        /// declares no definitive binding or the binding is invalid.
        /// </returns>
        private static string? ReadDefinitiveTypeBinding(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            JsonElement candidate = default;
            int count = 0;

            // Count every candidate before judging any of them. Ambiguity
            // dominates: where a document declares more than one such link it
            // is ambiguous, and validating one arbitrary candidate on top of
            // that would report a second, misleading error about a link the
            // converter was never entitled to choose.
            foreach (JsonElement link in document.Links)
            {
                if (link.ValueKind != JsonValueKind.Object ||
                    !link.TryGetProperty("rel", out JsonElement rel) ||
                    rel.ValueKind != JsonValueKind.String ||
                    !string.Equals(rel.GetString(), TypeBindingRel, StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
                if (count == 1)
                {
                    candidate = link;
                }
            }

            if (count > 1)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.AmbiguousTypeBinding,
                    $"A document declares {count} '{TypeBindingRel}' links, but a Node has exactly " +
                    "one HasTypeDefinition (WoT Binding Section 5.2.1).",
                    new WotLocation(jsonPointer: "/links")));
                return null;
            }

            if (count == 0)
            {
                return null;
            }

            string? href = candidate.TryGetProperty("href", out JsonElement hrefElement) &&
                hrefElement.ValueKind == JsonValueKind.String
                    ? hrefElement.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(href))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidTypeBinding,
                    $"A '{TypeBindingRel}' link (WoT Binding Section 5.2.1) must carry the " +
                    "ExpandedNodeId of the type in its 'href'.",
                    new WotLocation(jsonPointer: "/links")));
                return null;
            }

            return href;
        }

        /// <summary>
        /// The ReferenceType compact model name that carries a definitive type
        /// binding, per WoT Binding Section 5.2.1. It is an ordinary
        /// ReferenceType name used directly in <c>rel</c>, so it adds no
        /// vocabulary of its own.
        /// </summary>
        private const string TypeBindingRel = "ua:HasTypeDefinition";

        /// <summary>
        /// Turns a resolved type binding into the NodeId to emit, reporting an
        /// unresolved or invalid one.
        /// </summary>
        /// <remarks>
        /// Both forms of Section 5.2.1 resolve through the Section 5.1.5 local
        /// context, including the definitive <c>ua:HasTypeDefinition</c> link:
        /// its outcome table fails the projection for a link that "resolves to
        /// nothing" just as it does for an unresolved name. Emitting an
        /// unverified identifier would leave a dangling HasTypeDefinition,
        /// which is the silently mistyped node the clause exists to prevent.
        /// A caller with no local context therefore fails such a document
        /// rather than trusting the author, and the synchronous and
        /// asynchronous entry points agree on every document.
        /// </remarks>
        private static string? ApplyTypeBinding(
            WotDocument document,
            WotTypeBinding? typeBinding,
            List<WotDiagnostic> diagnostics)
        {
            if (typeBinding is null)
            {
                // The synchronous entry point has no local context, so a
                // declared binding can only be unresolved.
                string? link = ReadDefinitiveTypeBinding(document, diagnostics);
                if (link is not null)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.UnresolvedTypeBinding,
                        $"The '{TypeBindingRel}' link names '{link}', which cannot be resolved " +
                        "without a local context (WoT Binding Section 5.1.5). Convert through " +
                        "an entry point that supplies one.",
                        new WotLocation(jsonPointer: "/links")));
                }
                return null;
            }

            switch (typeBinding.Outcome)
            {
                case WotTypeBindingOutcome.Bound:
                    return typeBinding.NodeId;
                case WotTypeBindingOutcome.Invalid:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        // Section 5.2.1 lists an invalid document and an
                        // ambiguous name as separate outcomes, so a NodeClass
                        // mismatch or a name and link that disagree must not be
                        // reported as ambiguity.
                        typeBinding.IsAmbiguous
                            ? WotDiagnosticCode.AmbiguousTypeBinding
                            : WotDiagnosticCode.InvalidTypeBinding,
                        typeBinding.Detail!,
                        new WotLocation(jsonPointer: "/@type")));
                    return null;
                case WotTypeBindingOutcome.Unresolved:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.UnresolvedTypeBinding,
                        typeBinding.Detail!,
                        new WotLocation(jsonPointer: "/@type")));
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads the compact model names in <c>@type</c> that are type
        /// bindings, per WoT Binding Section 5.2.1.
        /// </summary>
        /// <remarks>
        /// A member is a binding when its namespace is one the local context
        /// holds; every other member is ordinary semantic annotation and is
        /// retained as residue. The test is the namespace, not whether the
        /// lookup succeeds, so a name in a held namespace that resolves to
        /// nothing is a reported mistake rather than a silently ignored
        /// annotation.
        /// </remarks>
        private static async ValueTask<List<string>> ReadTypeBindingNamesAsync(
            WotDocument document,
            IWotNodeResolver resolver,
            CancellationToken cancellationToken)
        {
            var names = new List<string>();
            foreach (string token in document.TypeTokens)
            {
                if (!TrySplitCompactModelName(token, out string prefix, out _) ||
                    string.Equals(prefix, "uav", StringComparison.Ordinal) ||
                    string.Equals(prefix, "tm", StringComparison.Ordinal) ||
                    !TryGetContextNamespace(document, prefix, out string namespaceUri))
                {
                    continue;
                }

                if (await resolver.HoldsNamespaceAsync(namespaceUri, cancellationToken)
                    .ConfigureAwait(false))
                {
                    names.Add(token);
                }
            }

            return names;
        }

        /// <summary>
        /// Resolves the type binding a document declares, applying the table in
        /// WoT Binding Section 5.2.1.
        /// </summary>
        private static async ValueTask<WotTypeBinding> ResolveTypeBindingAsync(
            WotDocument document,
            IWotNodeResolver resolver,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            string? link = ReadDefinitiveTypeBinding(document, diagnostics);
            List<string> names = await ReadTypeBindingNamesAsync(
                document, resolver, cancellationToken).ConfigureAwait(false);

            if (names.Count > 1)
            {
                return WotTypeBinding.Ambiguous(
                    $"{names.Count} members of '@type' are type bindings, but a Node has " +
                    "exactly one HasTypeDefinition.");
            }

            WotExpectedNodeClass expected = document.Kind == WotDocumentKind.ThingModel
                ? WotExpectedNodeClass.Any
                : WotExpectedNodeClass.ObjectType;

            WotResolvedNode? byLink = null;
            if (link is not null)
            {
                byLink = await resolver.ResolveByNodeIdAsync(link, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (names.Count == 0)
            {
                if (link is null)
                {
                    return WotTypeBinding.None;
                }
                return byLink is null
                    ? WotTypeBinding.Unresolved(
                        $"The '{TypeBindingRel}' link names '{link}', which the local context " +
                        "does not hold.")
                    : Accept(byLink.Value, expected, link);
            }

            string name = names[0];
            TrySplitCompactModelName(name, out string prefix, out string browseName);
            TryGetContextNamespace(document, prefix, out string namespaceUri);
            ArrayOf<WotResolvedNode> byName = await resolver
                .ResolveByBrowseNameAsync(namespaceUri, browseName, expected, cancellationToken)
                .ConfigureAwait(false);

            if (link is null)
            {
                return byName.Count switch
                {
                    1 => Accept(byName[0], expected, name),
                    0 => WotTypeBinding.Unresolved(
                        $"'@type' names '{name}', which the local context does not hold."),
                    _ => WotTypeBinding.Ambiguous(
                        $"'@type' names '{name}', which is ambiguous ({byName.Count} matches) and " +
                        $"carries no '{TypeBindingRel}' link to settle it.")
                };
            }

            if (byLink is null)
            {
                return WotTypeBinding.Unresolved(
                    $"The '{TypeBindingRel}' link names '{link}', which the local context does " +
                    "not hold.");
            }

            if (byName.Count == 0)
            {
                // The identifier resolves and the name does not: a name that
                // resolves to nothing is a mistake in the name rather than a
                // shorthand for the identifier.
                return WotTypeBinding.Invalid(
                    $"'@type' names '{name}', which the local context does not hold, while the " +
                    $"'{TypeBindingRel}' link resolves. The two disagree.");
            }

            foreach (WotResolvedNode candidate in byName)
            {
                if (string.Equals(candidate.NodeId, byLink.Value.NodeId, StringComparison.Ordinal))
                {
                    // Either the two agree, or the link settles an ambiguous
                    // name. Both bind to the identified type.
                    return Accept(byLink.Value, expected, link);
                }
            }

            return WotTypeBinding.Invalid(
                $"'@type' names '{name}' and the '{TypeBindingRel}' link names '{link}', which " +
                "resolve to different Nodes.");
        }

        /// <summary>
        /// Accepts a resolved type, or rejects it for the wrong NodeClass.
        /// </summary>
        private static WotTypeBinding Accept(
            WotResolvedNode node,
            WotExpectedNodeClass expected,
            string named)
        {
            if (expected != WotExpectedNodeClass.Any &&
                node.NodeClass != WotExpectedNodeClass.Any &&
                node.NodeClass != expected)
            {
                return WotTypeBinding.Invalid(
                    $"'{named}' resolves to a {node.NodeClass}, but the document projects a node " +
                    $"that requires a {expected}.");
            }

            return WotTypeBinding.Bound(node.NodeId);
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

        /// <summary>
        /// Resolves the DataType a property affordance declares.
        /// </summary>
        /// <remarks>
        /// §9.1 gives the readable mapping one channel for a DataType, the
        /// DataSchema's json type, and that channel carries six types — so a
        /// LocalizedText and a String are the same thing by the time it is read
        /// back. §5.4 states the definitive DataType at property level, so it
        /// wins where it is present. It is resolved through
        /// <see cref="ToNodeSetNodeId"/> rather than taken verbatim because that
        /// is what registers its namespace in the table and rewrites the
        /// portable <c>nsu=</c> form into the <c>ns=&lt;index&gt;</c> form a
        /// NodeSet2 <c>DataType</c> attribute is allowed to carry.
        /// </remarks>
        private static string MapJsonSchemaToDataType(
            JsonElement schema,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? definitive = GetElementString(schema, "uav:mapToType");
            if (definitive is not null)
            {
                return ToNodeSetNodeId(definitive, nodeSet, diagnostics);
            }
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

        private readonly record struct WotParentPlacement(string ParentNodeId);
    }
}
