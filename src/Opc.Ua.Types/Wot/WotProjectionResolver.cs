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
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves a projection document (WoT Binding Section 12) into a resolved
    /// view: an ordinary Thing Description or Thing Model that carries no
    /// <c>uav:projection</c> marker, so a consumer needs no projection support
    /// to use it.
    /// </summary>
    /// <remarks>
    /// The resolver performs no network I/O of its own. It obtains source
    /// documents through the supplied <see cref="IWotThingResolver"/> and
    /// bounds the work through a <see cref="WotResolutionContext"/>, exactly as
    /// <see cref="WotNodeSetConverter"/> does. The four carriage rules of
    /// Section 12.4 - forms, security, anchors and context - are applied to the
    /// selected affordances, and every resolved affordance records its origin in
    /// <c>uav:resolvedFrom</c>.
    /// </remarks>
    public sealed partial class WotProjectionResolver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotProjectionResolver"/>
        /// class.
        /// </summary>
        /// <param name="thingResolver">
        /// The resolver used to obtain source documents. Use
        /// <see cref="NullWotResolver.Instance"/> for an explicit "no external
        /// resolution" policy.
        /// </param>
        /// <param name="options">
        /// The bounded conversion options, or <c>null</c> to use the defaults.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="thingResolver"/> is <c>null</c>.
        /// </exception>
        public WotProjectionResolver(
            IWotThingResolver thingResolver,
            WotNodeSetConverterOptions? options = null)
        {
            m_thingResolver = thingResolver ??
                throw new ArgumentNullException(nameof(thingResolver));
            m_options = options ?? new WotNodeSetConverterOptions();
            m_options.Validate();
        }

        /// <summary>
        /// Resolves a projection document into a resolved view.
        /// </summary>
        /// <param name="document">The projection document.</param>
        /// <param name="resolutionContext">
        /// The resolution context that bounds the work, or <c>null</c> to create
        /// one from the configured options.
        /// </param>
        /// <param name="cancellationToken">
        /// A token that cancels the resolution operation.
        /// </param>
        /// <returns>
        /// A result carrying the resolved view together with the diagnostics
        /// produced. The value is <c>null</c> when any error diagnostic was
        /// reported.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public async ValueTask<WotConversionResult<WotDocument>> ResolveAsync(
            WotDocument document,
            WotResolutionContext? resolutionContext = null,
            CancellationToken cancellationToken = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var diagnostics = new List<WotDiagnostic>();
            if (!WotProjection.IsProjection(document))
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ValidationError,
                    "The document is not a projection document; it carries no " +
                    "uav:projection marker and needs no resolution.");
                return new WotConversionResult<WotDocument>(null, diagnostics);
            }

            var projection = WotProjection.Parse(document, diagnostics, m_options.ProjectionCompatibilityMode);
            if (projection is null || HasErrors(diagnostics))
            {
                return new WotConversionResult<WotDocument>(null, diagnostics);
            }

            WotResolutionContext context = resolutionContext ??
                new WotResolutionContext(m_options.ToResolverOptions());
            var resolving = new HashSet<string>(StringComparer.Ordinal);

            byte[]? bytes = await ResolveViewAsync(
                document,
                projection,
                null,
                resolving,
                context,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            await CheckOrganizingAcyclicAsync(
                projection,
                EffectiveBase(document, null),
                context,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            WotDocument? view = null;
            if (bytes is not null && !HasErrors(diagnostics))
            {
                try
                {
#pragma warning disable CA2000 // Ownership of the returned WotDocument transfers to the caller through the result.
                    view = WotDocument.Parse(bytes, m_options);
#pragma warning restore CA2000
                }
                catch (Exception exception) when (
                    exception is JsonException or FormatException)
                {
                    // The assembled view is a merge of N sources, so it can exceed
                    // a limit none of them individually broke. Report it rather
                    // than throwing out of a method whose contract documents only
                    // ArgumentNullException.
                    AddError(
                        diagnostics,
                        WotDiagnosticCode.ValidationError,
                        "The resolved view could not be parsed: " + exception.Message);
                }
            }
            return new WotConversionResult<WotDocument>(view, diagnostics);
        }

        private async ValueTask<byte[]?> ResolveViewAsync(
            WotDocument projectionDocument,
            WotProjection projection,
            string? documentLocation,
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            int errorsAtEntry = CountErrors(diagnostics);
            var openDocuments = new List<WotDocument>();
            try
            {
                string projectionOrigin = documentLocation ?? projectionDocument.Id ?? string.Empty;
                if (!ValidateContexts(projectionDocument, diagnostics) ||
                    !ValidatePredicateIdentities(projectionDocument, projection, projectionOrigin, diagnostics) ||
                    !ValidateSecurityDefinitions(projectionDocument, context.Options.MaxDepth, diagnostics))
                {
                    return null;
                }
                string projectionBase = EffectiveBase(projectionDocument, documentLocation);
                int count = projection.Sources.Count;
                var sources = new ResolvedSource?[count];
                for (int ii = 0; ii < count; ii++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sources[ii] = await ResolveSourceAsync(
                        projection.Sources[ii],
                        ResolveHref(projectionBase, projection.Sources[ii].Href),
                        resolving,
                        context,
                        diagnostics,
                        openDocuments,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!ValidateSourceIdentities(sources, diagnostics))
                {
                    return null;
                }

                JsonObject securityDefinitions =
                    SeedSecurityDefinitions(projectionDocument, projectionOrigin);
                var selection = new Selection(projectionDocument, projectionOrigin, securityDefinitions);

                ArrayOf<WotProjectionReference> references = projection.References;
                int[] referenceOwner = new int[references.Count];
                for (int jj = 0; jj < references.Count; jj++)
                {
                    referenceOwner[jj] = FindSourceIndex(
                        projection.Sources,
                        SplitDocumentPart(references[jj].Reference));
                }

                for (int ii = 0; ii < count; ii++)
                {
                    ResolvedSource? source = sources[ii];
                    if (source is null)
                    {
                        continue;
                    }
                    foreach (WotProjectionReference reference in
                        OrderEnumerated(references, referenceOwner, ii))
                    {
                        SelectEnumerated(source, reference, selection, diagnostics);
                    }
                    if (source.Source.SelectAll || HasFilters(source.Source))
                    {
                        SelectBulk(source, selection, diagnostics);
                    }
                }

                for (int jj = 0; jj < references.Count; jj++)
                {
                    if (referenceOwner[jj] == -1)
                    {
                        AddError(
                            diagnostics,
                            WotDiagnosticCode.ProjectionSourceUnresolved,
                            $"The reference '{references[jj].Reference}' names no " +
                            "source in uav:projects.",
                            references[jj].Reference);
                    }
                }

                if (CountErrors(diagnostics) > errorsAtEntry)
                {
                    return null;
                }

                CloseAffordanceDependencies(selection, diagnostics, cancellationToken);
                if (CountErrors(diagnostics) > errorsAtEntry ||
                    !await SupplyHostFormsAsync(
                        projection, selection, context, openDocuments, diagnostics, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return null;
                }
                bool legacyPlan = m_options.ProjectionCompatibilityMode ==
                    WotProjectionCompatibilityMode.DraftProjection11 &&
                    !projectionDocument.TryGetUav("projectionKind", out _);
                bool hasDocumentContext = projectionDocument.TryGetContext(out _);
                foreach (ResolvedAffordance member in selection.Members)
                {
                    bool hostRouting = member.Source.Source.Routing == WotProjectionRouting.Projection;
                    bool executableSource = projection.ResultKind == WotDocumentKind.ThingDescription &&
                        !member.Supporting &&
                        (member.Kind != WotAffordanceKind.Property || !member.Value.ContainsKey("const"));
                    if ((hostRouting || executableSource) &&
                        (member.Value["forms"] is not JsonArray forms || forms.Count == 0))
                    {
                        if (legacyPlan || (!hostRouting && !hasDocumentContext))
                        {
                            AddWarning(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved,
                                "Draft or context-free structural projection processing did not establish " +
                                "executable forms; the result is not executable-TD admission proof.",
                                "/" + MapName(member.Kind) + "/" + EscapePointer(member.Name) + "/forms");
                            continue;
                        }
                        AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved,
                            hostRouting
                                ? "A projection-routed affordance requires actual host-supplied forms."
                                : "An executable source-routed TD affordance requires non-empty source forms.",
                            "/" + MapName(member.Kind) + "/" + EscapePointer(member.Name) + "/forms");
                    }
                }
                if (CountErrors(diagnostics) > errorsAtEntry)
                {
                    return null;
                }

                if (!ValidateReusableSchemaDefinitions(projectionDocument, diagnostics))
                {
                    return null;
                }
                foreach (ResolvedSource? source in sources)
                {
                    if (source is not null && !ValidateReusableSchemaDefinitions(source.Document, diagnostics))
                    {
                        return null;
                    }
                }

                JsonObject root = AssembleRoot(
                    projectionDocument, projection.ResultKind, securityDefinitions, selection);
                var referencesClosure = new SchemaReferenceClosure(
                    root, projectionDocument, documentLocation, selection, m_options, diagnostics);
                referencesClosure.Close(cancellationToken);
                if (CountErrors(diagnostics) > errorsAtEntry)
                {
                    return null;
                }
                return Serialize(root);
            }
            finally
            {
                for (int ii = 0; ii < openDocuments.Count; ii++)
                {
                    openDocuments[ii].Dispose();
                }
            }
        }

        private static bool ValidateSourceIdentities(ResolvedSource?[] sources, List<WotDiagnostic> diagnostics)
        {
            var seen = new Dictionary<string, WotDocument>(StringComparer.Ordinal);
            foreach (ResolvedSource? source in sources)
            {
                if (source is null)
                {
                    continue;
                }
                if (!seen.TryGetValue(source.DocumentHref, out WotDocument? previous))
                {
                    seen.Add(source.DocumentHref, source.Document);
                    continue;
                }
                if (!WotJsonCanonicalizer.TryCanonicalize(previous.RootElement, out string first, out string error) ||
                    !WotJsonCanonicalizer.TryCanonicalize(source.Document.RootElement, out string second, out error) ||
                    !string.Equals(first, second, StringComparison.Ordinal))
                {
                    AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved,
                        "One source location supplied conflicting or incomparable documents during projection. " + error,
                        source.DocumentHref);
                    return false;
                }
            }
            return true;
        }

        private async ValueTask<ResolvedSource?> ResolveSourceAsync(
            WotProjectionManifestSource source,
            string href,
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            List<WotDocument> openDocuments,
            CancellationToken cancellationToken)
        {
            // Every source counts against the conversion's own bounds, not only
            // the nested-projection ones. An ordinary source is still a document
            // this resolver fetched, and a manifest naming ten thousand of them
            // is exactly the shape a bound exists to refuse.
            if (!context.TryEnter(WotResolutionKind.Thing, href, out WotDiagnostic? blocked))
            {
                AddError(
                    diagnostics,
                    blocked!.Code == WotDiagnosticCode.ResolverCycle
                        ? WotDiagnosticCode.ProjectionCycle
                        : blocked.Code,
                    blocked.Message,
                    href);
                return null;
            }
            try
            {
                return await ResolveSourceBoundedAsync(
                    source, href, resolving, context, diagnostics, openDocuments, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                context.Leave(href);
            }
        }

        private async ValueTask<ResolvedSource?> ResolveSourceBoundedAsync(
            WotProjectionManifestSource source,
            string href,
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            List<WotDocument> openDocuments,
            CancellationToken cancellationToken)
        {
            WotResolverResult result = await m_thingResolver.ResolveThingAsync(
                href, context, cancellationToken).ConfigureAwait(false);
            if (!result.Found)
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionSourceUnresolved,
                    $"The projection source '{source.SourceName}' at '{href}' " +
                    "could not be resolved.",
                    href);
                return null;
            }
            if (!context.TryAddBytes(href, result.Content.Length, out WotDiagnostic? limit))
            {
                diagnostics.Add(limit!);
                return null;
            }
            if (source.SourceDigest is not null &&
                !VerifyDigest(result.Content, source.SourceDigest))
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionDigestMismatch,
                    $"The uav:sourceDigest of source '{source.SourceName}' does " +
                    "not match the retrieved bytes.",
                    href);
                return null;
            }

            WotDocument document;
            try
            {
#pragma warning disable CA2000 // Ownership transfers to openDocuments, disposed in the caller's finally.
                document = WotDocument.Parse(result.Content, m_options);
#pragma warning restore CA2000
            }
            catch (Exception exception) when (
                exception is JsonException or FormatException)
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.MalformedJson,
                    $"The projection source '{source.SourceName}' at '{href}' " +
                    $"is not a well-formed document: {exception.Message}",
                    href);
                return null;
            }
            openDocuments.Add(document);
            if (!MatchesSourceMediaType(source, document))
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionManifestInvalid,
                    $"The declared media type '{source.MediaType}' does not match projection source '{source.SourceName}'.",
                    href);
                return null;
            }
            if (!ValidateContexts(document, diagnostics) ||
                !ValidateSecurityDefinitions(document, context.Options.MaxDepth, diagnostics))
            {
                return null;
            }

            if (WotProjection.IsProjection(document))
            {
                if (resolving.Count >= context.Options.MaxDepth)
                {
                    AddError(
                        diagnostics,
                        WotDiagnosticCode.ResolverDepthExceeded,
                        "Projection source resolution exceeded the maximum depth " +
                        $"of {context.Options.MaxDepth}.",
                        href);
                    return null;
                }
                if (!resolving.Add(href))
                {
                    AddError(
                        diagnostics,
                        WotDiagnosticCode.ProjectionCycle,
                        $"The projection source graph contains a cycle at '{href}'.",
                        href);
                    return null;
                }
                try
                {
                    int errorsBeforeParse = CountErrors(diagnostics);
                    var nested = WotProjection.Parse(document, diagnostics, m_options.ProjectionCompatibilityMode);
                    if (nested is null || CountErrors(diagnostics) != errorsBeforeParse)
                    {
                        return null;
                    }
                    byte[]? nestedBytes = await ResolveViewAsync(
                        document,
                        nested,
                        href,
                        resolving,
                        context,
                        diagnostics,
                        cancellationToken).ConfigureAwait(false);
                    if (nestedBytes is null)
                    {
                        return null;
                    }
                    WotDocument resolvedView;
                    try
                    {
#pragma warning disable CA2000 // Ownership transfers to openDocuments, disposed in the caller's finally.
                        resolvedView = WotDocument.Parse(nestedBytes, m_options);
#pragma warning restore CA2000
                    }
                    catch (Exception exception) when (
                        exception is JsonException or FormatException)
                    {
                        AddError(
                            diagnostics,
                            WotDiagnosticCode.ValidationError,
                            $"The nested resolved view '{href}' could not be parsed: " +
                            exception.Message);
                        return null;
                    }
                    openDocuments.Add(resolvedView);
                    return new ResolvedSource
                    {
                        Source = source,
                        Document = resolvedView,
                        DocumentHref = href,
                        BaseHref = EffectiveBase(resolvedView, href)
                    };
                }
                finally
                {
                    resolving.Remove(href);
                }
            }

            return new ResolvedSource
            {
                Source = source,
                Document = document,
                DocumentHref = href,
                BaseHref = EffectiveBase(document, href)
            };
        }

        private bool MatchesSourceMediaType(WotProjectionManifestSource source, WotDocument document)
        {
            string ordinaryMediaType = document.Kind == WotDocumentKind.ThingModel
                ? "application/tm+json"
                : "application/td+json";
            if (!WotProjection.IsProjection(document))
            {
                return string.Equals(source.MediaType, ordinaryMediaType, StringComparison.Ordinal);
            }
            if (string.Equals(source.MediaType, WotProjection.ContentType, StringComparison.Ordinal))
            {
                return true;
            }
            return m_options.ProjectionCompatibilityMode == WotProjectionCompatibilityMode.DraftProjection11 &&
                !document.TryGetUav("projectionKind", out _) &&
                string.Equals(source.MediaType, ordinaryMediaType, StringComparison.Ordinal);
        }

        private async ValueTask CheckOrganizingAcyclicAsync(
            WotProjection projection,
            string baseHref,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            if (projection.OrganizingLinks.IsNull ||
                projection.OrganizingLinks.Count == 0)
            {
                return;
            }
            var path = new HashSet<string>(StringComparer.Ordinal);
            var completed = new HashSet<string>(StringComparer.Ordinal);
            int[] budget = [context.Options.MaxDocuments, 0];
            for (int ii = 0; ii < projection.OrganizingLinks.Count; ii++)
            {
                await WalkOrganizesAsync(
                    ResolveHref(baseHref, projection.OrganizingLinks[ii].Href),
                    path,
                    completed,
                    budget,
                    diagnostics,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Walks the <c>ua:Organizes</c> graph, bounded by the same document
        /// budget the rest of the conversion runs under.
        /// </summary>
        /// <remarks>
        /// Exhausting the budget stops the walk on a <em>partial</em> graph, so
        /// it is reported. Returning silently would leave the acyclicity check
        /// answering "no cycle found" for a graph it never finished reading,
        /// which is the one answer that cannot be told apart from "no cycle".
        /// The report is emitted once, however many branches run out.
        /// </remarks>
        private async ValueTask WalkOrganizesAsync(
            string href,
            HashSet<string> path,
            HashSet<string> completed,
            int[] budget,
            List<WotDiagnostic> diagnostics,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Contains(href))
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionCycle,
                    $"The ua:Organizes graph contains a cycle at '{href}'.",
                    href);
                return;
            }
            if (completed.Contains(href))
            {
                return;
            }
            if (budget[0]-- <= 0)
            {
                if (budget[1] == 0)
                {
                    budget[1] = 1;
                    AddError(
                        diagnostics,
                        WotDiagnosticCode.TraversalBudgetExhausted,
                        "The ua:Organizes traversal stopped at " +
                        $"'{href}' after the configured maximum of " +
                        $"{context.Options.MaxDocuments} documents, so the graph was " +
                        "only partly read and cannot be reported acyclic.",
                        href);
                }
                return;
            }
            path.Add(href);
            WotResolverResult result = await m_thingResolver.ResolveThingAsync(
                href, context, cancellationToken).ConfigureAwait(false);
            if (result.Found)
            {
                WotDocument? organized = null;
                try
                {
                    organized = WotDocument.Parse(result.Content, m_options);
                    foreach (string next in ReadOrganizesHrefs(organized))
                    {
                        await WalkOrganizesAsync(
                            ResolveHref(EffectiveBase(organized, href), next),
                            path,
                            completed,
                            budget,
                            diagnostics,
                            context,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (
                    exception is JsonException or FormatException)
                {
                    // An organized document that cannot be parsed cannot extend
                    // the graph; materialization is out of scope, so the link is
                    // still carried through and the parse failure is ignored here.
                }
                finally
                {
                    organized?.Dispose();
                }
            }
            path.Remove(href);
            completed.Add(href);
        }

        /// <summary>
        /// Orders the enumerated selections of one source by the total order of
        /// WoT Binding Section 12.4: affordance kind in the fixed order
        /// <c>properties</c>, <c>actions</c>, <c>events</c>; then ascending
        /// Unicode code point of the name the selection takes <em>in the
        /// view</em>; then ascending Unicode code point of the affordance's
        /// name <em>in the source</em>.
        /// </summary>
        /// <remarks>
        /// The order is stated over names rather than over document order
        /// because <c>properties</c>, <c>actions</c> and <c>events</c> are JSON
        /// objects, which RFC 8259 defines as unordered: a rule that ranked
        /// selections by member position would let two conforming consumers
        /// resolve identical bytes into different views, and because the first
        /// selection of a name wins, the difference is observable.
        /// </remarks>
        private static List<WotProjectionReference> OrderEnumerated(
            ArrayOf<WotProjectionReference> references,
            int[] referenceOwner,
            int sourceIndex)
        {
            var owned = new List<WotProjectionReference>();
            for (int jj = 0; jj < references.Count; jj++)
            {
                if (referenceOwner[jj] == sourceIndex)
                {
                    owned.Add(references[jj]);
                }
            }
            owned.Sort(static (left, right) =>
            {
                int comparison = KindRank(left.AffordanceKind)
                    .CompareTo(KindRank(right.AffordanceKind));
                if (comparison != 0)
                {
                    return comparison;
                }
                comparison = WotCodePointComparer.Instance.Compare(left.Name, right.Name);
                return comparison != 0
                    ? comparison
                    : WotCodePointComparer.Instance.Compare(
                        SourceAffordanceName(left.Reference),
                        SourceAffordanceName(right.Reference));
            });
            return owned;
        }

        /// <summary>
        /// The position of an affordance kind in the fixed order of
        /// WoT Binding Section 12.4.
        /// </summary>
        private static int KindRank(WotAffordanceKind kind)
        {
            return kind switch
            {
                WotAffordanceKind.Action => 1,
                WotAffordanceKind.Event => 2,
                _ => 0
            };
        }

        /// <summary>
        /// Gets the affordance's own name in the source, which is the last
        /// token of the <c>tm:ref</c> JSON Pointer and the final tie-break of
        /// WoT Binding Section 12.4.
        /// </summary>
        private static string SourceAffordanceName(string reference)
        {
            int separator = reference.LastIndexOf('/');
            return separator >= 0 && separator + 1 < reference.Length
                ? reference[(separator + 1)..]
                : reference;
        }

        private static void SelectEnumerated(
            ResolvedSource source,
            WotProjectionReference reference,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            string pointer = SplitPointer(reference.Reference);
            string prefix = "/" + MapName(reference.AffordanceKind) + "/";
            if (!pointer.StartsWith(prefix, StringComparison.Ordinal) ||
                pointer.IndexOf('/', prefix.Length) >= 0 ||
                !WotDocument.TryEvaluatePointer(
                    source.Document.RootElement, pointer, out JsonElement definition) ||
                definition.ValueKind != JsonValueKind.Object)
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionSourceUnresolved,
                    $"The reference '{reference.Reference}' must identify a direct " +
                    $"'{MapName(reference.AffordanceKind)}' affordance of its source document.",
                    reference.Reference);
                return;
            }
            if (!selection.Claim(reference.AffordanceKind, reference.Name))
            {
                AddWarning(
                    diagnostics,
                    WotDiagnosticCode.ProjectionSelectionDropped,
                    $"The selection '{reference.Name}' was already made; the later " +
                    "enumerated selection is dropped.",
                    reference.Reference);
                return;
            }

            bool sourceRouting =
                source.Source.Routing == WotProjectionRouting.Source;
            if (sourceRouting && CarriesTransportAnnotation(reference.Annotations, out string member))
            {
                // Section 12.5: under source routing the consumer talks to the
                // source's own endpoint, so a member that restates forms or
                // security makes the document invalid. Dropping it would be
                // worse than reporting it: a dropped form is one the author
                // wrote and the consumer silently did not use, which reads at
                // run time as the source endpoint answering a request the
                // document appeared to address elsewhere.
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionAnnotationNotPermitted,
                    $"The projected affordance '{reference.Name}' carries '{member}' and is " +
                    $"selected from the source-routed source '{source.Source.Href}'. A member " +
                    "selected from a source-routed source shall not carry forms or security of " +
                    "its own; the source's own form is carried and absolutized instead " +
                    "(WoT Binding Sections 12.4 and 12.5).",
                    reference.Reference);
                return;
            }
            JsonObject? target = CloneAffordance(source, definition, pointer, diagnostics);
            if (target is null)
            {
                return;
            }
            if (!sourceRouting)
            {
                target.Remove("forms");
                target.Remove("security");
            }
            MergeAnnotation(target, reference.Annotations, sourceRouting, source, definition, selection, diagnostics);
            if (sourceRouting)
            {
                TransformForms(target, source, selection);
            }
            else
            {
                QualifyProjectionSecurity(target);
            }
            CarryAnchor(target, source.Document);
            CarryProvenance(target, source, definition, pointer, diagnostics);
            selection.Add(
                reference.AffordanceKind, reference.Name, target, source,
                UnescapeAffordanceName(pointer[prefix.Length..]), definition);
        }

        private static void SelectBulk(
            ResolvedSource source,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            var candidates = new List<(WotAffordanceKind Kind, string Name, JsonElement Definition)>();
            foreach ((WotAffordanceKind kind, string name, JsonElement definition)
                in EnumerateAffordances(source.Document))
            {
                candidates.Add((kind, name, definition));
            }

            // Section 12.4: within one source and group, by affordance kind,
            // then by the name the selection takes in the view, then by the
            // affordance's own name in the source. The last key is what makes
            // the order total, because uav:namePrefix upper-cases the first
            // character of the source name and so gives 'serialNumber' and
            // 'SerialNumber' the same name in the view.
            candidates.Sort((left, right) =>
            {
                int comparison = KindRank(left.Kind).CompareTo(KindRank(right.Kind));
                if (comparison != 0)
                {
                    return comparison;
                }
                comparison = WotCodePointComparer.Instance.Compare(
                    ApplyPrefix(source.Source, left.Name),
                    ApplyPrefix(source.Source, right.Name));
                return comparison != 0
                    ? comparison
                    : WotCodePointComparer.Instance.Compare(left.Name, right.Name);
            });

            foreach ((WotAffordanceKind kind, string name, JsonElement definition) in candidates)
            {
                string viewName = ApplyPrefix(source.Source, name);
                if (!selection.IsClaimed(kind, viewName) &&
                    !MatchesSource(source, kind, name, definition, selection, diagnostics))
                {
                    continue;
                }
                if (!selection.Claim(kind, viewName))
                {
                    AddWarning(
                        diagnostics,
                        WotDiagnosticCode.ProjectionSelectionDropped,
                        $"The name '{viewName}' was already selected; the " +
                        "later bulk candidate is dropped.",
                        source.Source.Href);
                    continue;
                }

                bool sourceRouting =
                    source.Source.Routing == WotProjectionRouting.Source;
                string pointer = "/" + MapName(kind) + "/" + EscapePointer(name);
                JsonObject? target = CloneAffordance(source, definition, pointer, diagnostics);
                if (target is null)
                {
                    continue;
                }
                if (sourceRouting)
                {
                    TransformForms(target, source, selection);
                }
                else
                {
                    target.Remove("forms");
                    target.Remove("security");
                }
                CarryAnchor(target, source.Document);
                CarryProvenance(target, source, definition, pointer, diagnostics);
                selection.Add(kind, viewName, target, source, name, definition);
            }
        }

        private void CloseAffordanceDependencies(
            Selection selection, List<WotDiagnostic> diagnostics, CancellationToken cancellationToken)
        {
            if (selection.Members.Count > m_options.MaxNodeCount)
            {
                AddError(diagnostics, WotDiagnosticCode.TraversalBudgetExhausted,
                    $"The projection exceeds the configured maximum of {m_options.MaxNodeCount} affordances.");
                return;
            }
            for (int index = 0; index < selection.Members.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResolvedAffordance member = selection.Members[index];
                if (member.Kind == WotAffordanceKind.Property &&
                    member.Definition.TryGetProperty(WotNodeSetConverter.UnitPropertyTerm, out JsonElement unit))
                {
                    int errors = CountErrors(diagnostics);
                    WotNodeSetConverter.ValidateUnitProperty(
                        member.Source.Document, member.Definition, member.SourceName, member.Pointer, diagnostics);
                    if (CountErrors(diagnostics) != errors)
                    {
                        continue;
                    }
                    string pointer = unit.GetString()!;
                    string name = UnescapeAffordanceName(pointer["/properties/".Length..]);
                    ResolvedAffordance? dependency = CarrySupportingAffordance(
                        member.Source, WotAffordanceKind.Property, name,
                        member.Source.Document.Properties[name], selection, diagnostics);
                    if (dependency is not null)
                    {
                        member.Value[WotNodeSetConverter.UnitPropertyTerm] =
                            "/properties/" + EscapePointer(dependency.Name);
                    }
                }
                if (member.Kind == WotAffordanceKind.Action &&
                    member.Definition.TryGetProperty(WotNodeSetConverter.ActsOnTerm, out JsonElement actsOn))
                {
                    string? name = actsOn.ValueKind == JsonValueKind.String ? actsOn.GetString() : null;
                    if (string.IsNullOrEmpty(name) ||
                        !member.Source.Document.Events.TryGetValue(name!, out JsonElement target) ||
                        target.ValueKind != JsonValueKind.Object ||
                        !target.TryGetProperty(WotNodeSetConverter.ConditionTypeTerm, out JsonElement condition) ||
                        condition.ValueKind != JsonValueKind.String ||
                        string.IsNullOrEmpty(condition.GetString()))
                    {
                        AddError(
                            diagnostics, WotDiagnosticCode.InvalidConditionTarget,
                            "A carried uav:actsOn must name an event with uav:conditionType in its original source.",
                            member.Source.DocumentHref + "#" + member.Pointer);
                        continue;
                    }
                    ResolvedAffordance? dependency = CarrySupportingAffordance(
                        member.Source, WotAffordanceKind.Event, name!, target, selection, diagnostics);
                    if (dependency is not null)
                    {
                        member.Value[WotNodeSetConverter.ActsOnTerm] = dependency.Name;
                    }
                }
            }
        }

        private ResolvedAffordance? CarrySupportingAffordance(
            ResolvedSource source,
            WotAffordanceKind kind,
            string name,
            JsonElement definition,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            string pointer = "/" + MapName(kind) + "/" + EscapePointer(name);
            if (selection.TryLocate(source, pointer, out ResolvedAffordance? selected))
            {
                return selected;
            }
            if (selection.Members.Count >= m_options.MaxNodeCount)
            {
                AddError(diagnostics, WotDiagnosticCode.TraversalBudgetExhausted,
                    "The projection dependency closure exceeds the configured maximum of " +
                    $"{m_options.MaxNodeCount} affordances.", source.DocumentHref + "#" + pointer);
                return null;
            }

            string outputName = selection.AllocateSupportName(kind, source, name, pointer);
            JsonObject? value = CloneAffordance(source, definition, pointer, diagnostics);
            if (value is null)
            {
                return null;
            }
            if (source.Source.Routing == WotProjectionRouting.Source)
            {
                TransformForms(value, source, selection);
            }
            else
            {
                value.Remove("forms");
                value.Remove("security");
            }
            CarryAnchor(value, source.Document);
            CarryProvenance(value, source, definition, pointer, diagnostics);
            return selection.Add(kind, outputName, value, source, name, definition, supporting: true);
        }

        private static void CarryProvenance(
            JsonObject value,
            ResolvedSource source,
            JsonElement definition,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!definition.TryGetProperty("uav:resolvedFrom", out JsonElement provenance))
            {
                value["uav:resolvedFrom"] = source.DocumentHref + "#" + pointer;
                return;
            }
            if (provenance.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(provenance.GetString()))
            {
                AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved,
                    "Carried uav:resolvedFrom provenance must be a non-empty reference.",
                    source.DocumentHref + "#" + pointer);
                return;
            }
            value["uav:resolvedFrom"] = ResolveHref(source.DocumentHref, provenance.GetString()!);
        }

        private static string UnescapeAffordanceName(string token)
        {
            return token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether a projection's annotation on a declared affordance
        /// carries a transport member - <c>forms</c> or <c>security</c> - which
        /// only a <c>projection</c>-routed source may state
        /// (WoT Binding Sections 12.4 and 12.5).
        /// </summary>
        private static bool CarriesTransportAnnotation(JsonElement annotations, out string member)
        {
            if (annotations.ValueKind == JsonValueKind.Object)
            {
                if (annotations.TryGetProperty("forms", out _))
                {
                    member = "forms";
                    return true;
                }
                if (annotations.TryGetProperty("security", out _))
                {
                    member = "security";
                    return true;
                }
            }
            member = string.Empty;
            return false;
        }

        /// <summary>
        /// Merges the members a projection annotated a declared affordance
        /// with, honouring the closed set of WoT Binding Section 12.5.
        /// </summary>
        /// <remarks>
        /// The set is checked again here, and not only where the projection is
        /// parsed, because this is the step that would otherwise write a
        /// restated schema member over the source's own. A member outside the
        /// set is dropped rather than merged; the parse reported it, so the
        /// caller already knows it was there.
        /// </remarks>
        private static void MergeAnnotation(
            JsonObject target,
            JsonElement annotations,
            bool sourceRouting,
            ResolvedSource source,
            JsonElement sourceDefinition,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            if (annotations.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty member in annotations.EnumerateObject())
            {
                if (string.Equals(member.Name, "tm:ref", StringComparison.Ordinal) ||
                    !WotProjection.IsPermittedAnnotation(member.Name))
                {
                    continue;
                }
                if (string.Equals(member.Name, "@type", StringComparison.Ordinal))
                {
                    JsonNode? types = MergeAnnotationTypes(
                        target["@type"], member.Value, sourceDefinition, source, selection, annotations, diagnostics);
                    if (types is null)
                    {
                        return;
                    }
                    target["@type"] = types;
                    continue;
                }
                if (member.Name == "uav:semanticId" && member.Value.ValueKind == JsonValueKind.String)
                {
                    if (!TryPrepareAnnotationIdentity(
                        member.Value.GetString()!, false, source, sourceDefinition,
                        selection, annotations, diagnostics, out string identity))
                    {
                        return;
                    }
                    target[member.Name] = identity;
                    continue;
                }
                if (sourceRouting &&
                    (string.Equals(member.Name, "forms", StringComparison.Ordinal) ||
                        string.Equals(member.Name, "security", StringComparison.Ordinal)))
                {
                    // A member selected under source routing shall not carry its
                    // own forms or security (Section 12.5); the source's own form
                    // is carried and absolutized instead.
                    continue;
                }
                target[member.Name] = IsLiteralMember(member.Name)
                    ? CloneLiteral(member.Value) : CloneNode(member.Value);
                if (member.Name is "title" or "description" &&
                    !CarryAnnotationLocale(
                        target, selection, annotations, member.Name, source, sourceDefinition, diagnostics))
                {
                    return;
                }
            }
        }

        private static void TransformForms(
            JsonObject target,
            ResolvedSource source,
            Selection selection)
        {
            if (target["security"] is JsonNode requirement)
            {
                CopySecurityClosure(
                    source.Source.SourceName,
                    source.Document, source.DocumentHref,
                    NamesFromNode(requirement),
                    selection.SecurityDefinitions,
                    selection.SecurityAdded);
                QualifySecurityRequirement(target, source.Source.SourceName);
            }
            if (!target.TryGetPropertyValue("forms", out JsonNode? formsNode) ||
                formsNode is not JsonArray forms)
            {
                return;
            }
            foreach (JsonNode? node in forms)
            {
                if (node is not JsonObject form)
                {
                    continue;
                }
                if (form.TryGetPropertyValue("href", out JsonNode? hrefNode) &&
                    hrefNode is JsonValue hrefValue &&
                    hrefValue.TryGetValue(out string? href) &&
                    href is not null)
                {
                    form["href"] = ResolveHref(source.BaseHref, href);
                }
                List<string> effective = EffectiveSecurity(form, source.Document);
                if (effective.Count > 0)
                {
                    CopySecurityClosure(
                        source.Source.SourceName,
                        source.Document, source.DocumentHref,
                        effective,
                        selection.SecurityDefinitions,
                        selection.SecurityAdded);
                    var security = new JsonArray();
                    for (int ii = 0; ii < effective.Count; ii++)
                    {
                        security.Add(JsonValue.Create(
                            Qualify(source.Source.SourceName, effective[ii])));
                    }
                    form["security"] = security;
                }
                else
                {
                    form.Remove("security");
                }
            }
        }

        private static void CopySecurityClosure(
            string? sourceName,
            WotDocument document,
            string origin,
            List<string> schemeNames,
            JsonObject securityDefinitions,
            HashSet<string> securityAdded)
        {
            for (int ii = 0; ii < schemeNames.Count; ii++)
            {
                CopyScheme(
                    sourceName,
                    schemeNames[ii],
                    document, origin,
                    securityDefinitions,
                    securityAdded);
            }
        }

        private static void CopyScheme(
            string? sourceName,
            string schemeName,
            WotDocument document,
            string origin,
            JsonObject securityDefinitions,
            HashSet<string> securityAdded)
        {
            if (!document.SecurityDefinitions.TryGetValue(schemeName, out JsonElement definition) ||
                definition.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string qualified = Qualify(sourceName, schemeName);
            if (!securityAdded.Add(qualified))
            {
                return;
            }
            JsonObject copy = CloneOwnedObject(document, definition, origin);
            var children = new List<string>();
            bool combo = definition.TryGetProperty("scheme", out JsonElement scheme) &&
                scheme.ValueKind == JsonValueKind.String &&
                scheme.GetString() == "combo";
            for (int ii = 0; combo && ii < s_comboKeys.Length; ii++)
            {
                if (copy.TryGetPropertyValue(s_comboKeys[ii], out JsonNode? node) &&
                    node is JsonArray references)
                {
                    var rewritten = new JsonArray();
                    foreach (JsonNode? entry in references)
                    {
                        if (entry is JsonValue value &&
                            value.TryGetValue(out string? name) &&
                            name is not null)
                        {
                            rewritten.Add(JsonValue.Create(Qualify(sourceName, name)));
                            children.Add(name);
                        }
                        else
                        {
                            rewritten.Add(entry is null ? null : CloneNode(entry));
                        }
                    }
                    copy[s_comboKeys[ii]] = rewritten;
                }
            }
            securityDefinitions[qualified] = copy;
            for (int ii = 0; ii < children.Count; ii++)
            {
                CopyScheme(
                    sourceName,
                    children[ii],
                    document, origin,
                    securityDefinitions,
                    securityAdded);
            }
        }

        private static List<string> EffectiveSecurity(
            JsonObject form,
            WotDocument sourceDocument)
        {
            if (form.TryGetPropertyValue("security", out JsonNode? formSecurity) &&
                formSecurity is not null)
            {
                return NamesFromNode(formSecurity);
            }
            if (sourceDocument.RootElement.TryGetProperty(
                    "security", out JsonElement thingSecurity))
            {
                return NamesFromElement(thingSecurity);
            }
            return [];
        }

        /// <summary>
        /// Carries the anchor a relative <c>uav:browsePath</c> resolved against
        /// in its source, so the path resolves in the view exactly where it
        /// resolved there (WoT Binding Sections 5.1.4 and 12.4).
        /// </summary>
        /// <remarks>
        /// The source's effective anchor is the nearest enclosing
        /// <c>uav:browsePathAnchor</c> and, failing that, the nearest enclosing
        /// <c>uav:id</c>. For a carried affordance the enclosing scopes are the
        /// affordance itself and the source document's root, so an anchor the
        /// affordance stated needs no carrying - it travels in the clone - while
        /// the source root's anchor outranks the affordance's own identity and
        /// has to be written down. Where the source stated no anchor at all, the
        /// affordance's own <c>uav:id</c> travels with it and only a root
        /// identity has to be carried; without either the path did not resolve
        /// in the source, so nothing is invented for the view.
        /// </remarks>
        private static void CarryAnchor(JsonObject target, WotDocument sourceDocument)
        {
            if (!target.TryGetPropertyValue("uav:browsePath", out JsonNode? pathNode) ||
                pathNode is not JsonValue pathValue ||
                !pathValue.TryGetValue(out string? path) ||
                path is null ||
                path.StartsWith('/'))
            {
                return;
            }
            if (target.ContainsKey(WotAnchorScope.AnchorTerm))
            {
                return;
            }
            string? rootAnchor = WotAnchorScope.ReadTerm(
                sourceDocument.RootElement, WotAnchorScope.AnchorTerm);
            if (rootAnchor is null &&
                WotAnchorScope.ReadTerm(target, WotAnchorScope.IdentityTerm) is not null)
            {
                return;
            }
            string? carried = rootAnchor ??
                WotAnchorScope.ReadTerm(
                    sourceDocument.RootElement, WotAnchorScope.IdentityTerm);
            if (carried is not null)
            {
                target[WotAnchorScope.AnchorTerm] = carried;
            }
        }

        private static JsonObject AssembleRoot(
            WotDocument projectionDocument,
            WotDocumentKind resultKind,
            JsonObject securityDefinitions,
            Selection selection)
        {
            var root = new JsonObject();
            foreach (JsonProperty member in
                projectionDocument.RootElement.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "@context":
                        root["@context"] = CloneContext(member.Value, selection.DocumentHref);
                        break;
                    case "@type":
                        root["@type"] = BuildTypeArray(member.Value, resultKind);
                        break;
                    case "schemaDefinitions":
                        var schemas = new JsonObject();
                        foreach (JsonProperty schema in member.Value.EnumerateObject())
                        {
                            JsonNode? schemaValue = CloneNode(schema.Value);
                            PreserveLiteralValues(schemaValue, projectionDocument, schema.Value);
                            schemas[schema.Name] = schemaValue;
                        }
                        root["schemaDefinitions"] = schemas;
                        break;
                    case "uriVariables":
                        // The variable closure reconciles duplicate declarations before copying their owned schemas.
                        root[member.Name] = CloneNode(member.Value);
                        break;
                    case "uav:projects":
                    case "uav:projectionKind":
                    case "properties":
                    case "actions":
                    case "events":
                    case "securityDefinitions":
                        break;
                    default:
                        JsonNode? cloned = IsLiteralMember(member.Name)
                            ? CloneLiteral(member.Value) : CloneNode(member.Value);
                        PreserveLiteralValues(cloned, projectionDocument, member.Value,
                            projectionDocument.IsContextIndexMap(member.Name, projectionDocument.RootElement));
                        root[member.Name] = cloned;
                        break;
                }
            }
            if (securityDefinitions.Count > 0)
            {
                root["securityDefinitions"] = securityDefinitions;
            }
            AddAffordanceMap(root, "properties", selection.Properties);
            AddAffordanceMap(root, "actions", selection.Actions);
            AddAffordanceMap(root, "events", selection.Events);
            QualifyProjectionSecurity(root);
            return root;
        }

        private static void AddAffordanceMap(
            JsonObject root,
            string name,
            List<ResolvedAffordance> affordances)
        {
            if (affordances.Count == 0)
            {
                return;
            }
            var map = new JsonObject();
            for (int ii = 0; ii < affordances.Count; ii++)
            {
                map[affordances[ii].Name] = affordances[ii].Value;
            }
            root[name] = map;
        }

        private static IEnumerable<(WotAffordanceKind Kind, string Name, JsonElement Definition)>
            EnumerateAffordances(WotDocument document)
        {
            foreach ((WotAffordanceKind Kind, string Name, JsonElement Definition) pair in EnumerateAffordanceMap(
                document, "properties", WotAffordanceKind.Property))
            {
                yield return pair;
            }
            foreach ((WotAffordanceKind Kind, string Name, JsonElement Definition) pair in EnumerateAffordanceMap(
                document, "actions", WotAffordanceKind.Action))
            {
                yield return pair;
            }
            foreach ((WotAffordanceKind Kind, string Name, JsonElement Definition) pair in EnumerateAffordanceMap(
                document, "events", WotAffordanceKind.Event))
            {
                yield return pair;
            }
        }

        private static IEnumerable<(WotAffordanceKind Kind, string Name, JsonElement Definition)>
            EnumerateAffordanceMap(
                WotDocument document,
                string mapName,
                WotAffordanceKind kind)
        {
            if (document.RootElement.TryGetProperty(mapName, out JsonElement map) &&
                map.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in map.EnumerateObject())
                {
                    yield return (kind, member.Name, member.Value);
                }
            }
        }

        private static bool MatchesSource(
            ResolvedSource source,
            WotAffordanceKind kind,
            string name,
            JsonElement definition,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            if (source.Source.SelectAll)
            {
                return true;
            }
            if (source.Source.Filters.IsNull)
            {
                return false;
            }
            bool unknown = false;
            for (int ii = 0; ii < source.Source.Filters.Count; ii++)
            {
                PredicateMatch match = MatchesFilter(source.Source.Filters[ii], kind, definition, source, selection);
                if (match == PredicateMatch.Match)
                {
                    return true;
                }
                unknown |= match == PredicateMatch.Unknown;
            }
            if (unknown)
            {
                AddError(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid,
                    "An affordance's predicate membership cannot be determined in its original semantic context.",
                    source.DocumentHref + "#/" + MapName(kind) + "/" + EscapePointer(name));
            }
            return false;
        }

        private static PredicateMatch MatchesFilter(
            WotProjectionFilter filter,
            WotAffordanceKind kind,
            JsonElement definition,
            ResolvedSource source,
            Selection selection)
        {
            if (filter.AffordanceKind != WotAffordanceKind.Any &&
                filter.AffordanceKind != kind)
            {
                return PredicateMatch.Mismatch;
            }
            bool unknown = false;
            if (filter.SemanticId is not null)
            {
                PredicateMatch match = HasSemanticId(definition, filter, source, selection);
                if (match == PredicateMatch.Mismatch)
                {
                    return match;
                }
                unknown = match == PredicateMatch.Unknown;
            }
            if (!filter.TypeTokens.IsNull)
            {
                for (int ii = 0; ii < filter.TypeTokens.Count; ii++)
                {
                    PredicateMatch match = HasTypeToken(
                        definition, filter.TypeTokens[ii], filter, source, selection);
                    if (match == PredicateMatch.Mismatch)
                    {
                        return match;
                    }
                    unknown |= match == PredicateMatch.Unknown;
                }
            }
            return unknown ? PredicateMatch.Unknown : PredicateMatch.Match;
        }

        private static PredicateMatch HasSemanticId(
            JsonElement definition, WotProjectionFilter filter, ResolvedSource source,
            Selection selection)
        {
            return definition.TryGetProperty(
                    "uav:semanticId", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? MatchesSemanticIdentity(
                    value.GetString()!, filter.SemanticId!, false, definition, filter, source, selection)
                : PredicateMatch.Mismatch;
        }

        private static PredicateMatch HasTypeToken(
            JsonElement definition, string token, WotProjectionFilter filter, ResolvedSource source,
            Selection selection)
        {
            if (!definition.TryGetProperty("@type", out JsonElement types))
            {
                return PredicateMatch.Mismatch;
            }
            if (types.ValueKind == JsonValueKind.String)
            {
                return MatchesSemanticIdentity(
                    types.GetString()!, token, true, definition, filter, source, selection);
            }
            bool unknown = false;
            if (types.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in types.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        PredicateMatch match = MatchesSemanticIdentity(
                            item.GetString()!, token, true, definition, filter, source, selection);
                        if (match == PredicateMatch.Match)
                        {
                            return match;
                        }
                        unknown |= match == PredicateMatch.Unknown;
                    }
                }
            }
            return unknown ? PredicateMatch.Unknown : PredicateMatch.Mismatch;
        }

        private static PredicateMatch MatchesSemanticIdentity(
            string sourceValue, string predicateValue, bool vocabulary, JsonElement definition,
            WotProjectionFilter filter, ResolvedSource source, Selection selection)
        {
            if (!TryExpandSemanticIdentity(
                    predicateValue, selection.Document, filter.ContextOwner, selection.DocumentHref, vocabulary,
                    out string expected) ||
                !TryExpandSemanticIdentity(
                    sourceValue, source.Document, definition, source.DocumentHref, vocabulary, out string actual))
            {
                return PredicateMatch.Unknown;
            }
            return string.Equals(expected, actual, StringComparison.Ordinal)
                ? PredicateMatch.Match : PredicateMatch.Mismatch;
        }

        private static bool ValidatePredicateIdentities(
            WotDocument document, WotProjection projection, string origin, List<WotDiagnostic> diagnostics)
        {
            foreach (WotProjectionManifestSource source in projection.Sources)
            {
                foreach (WotProjectionFilter filter in source.Filters)
                {
                    if (filter.SemanticId is not null &&
                        !Validate(filter.SemanticId, false, filter.ContextOwner))
                    {
                        return false;
                    }
                    foreach (string token in filter.TypeTokens)
                    {
                        if (!Validate(token, true, filter.ContextOwner))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;

            bool Validate(string value, bool vocabulary, JsonElement owner)
            {
                if (TryExpandSemanticIdentity(value, document, owner, origin, vocabulary, out _))
                {
                    return true;
                }
                AddError(diagnostics, WotDiagnosticCode.ProjectionSelectorInvalid,
                    "A selection predicate cannot be resolved in its original semantic context.", value);
                return false;
            }
        }

        private static IEnumerable<string> ReadOrganizesHrefs(WotDocument document)
        {
            foreach (JsonElement link in document.Links)
            {
                if (link.ValueKind == JsonValueKind.Object &&
                    link.TryGetProperty("rel", out JsonElement rel) &&
                    rel.ValueKind == JsonValueKind.String &&
                    string.Equals(
                        rel.GetString(),
                        WotVocabulary.OrganizesRel,
                        StringComparison.Ordinal) &&
                    link.TryGetProperty("href", out JsonElement href) &&
                    href.ValueKind == JsonValueKind.String)
                {
                    string? value = href.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        yield return value!;
                    }
                }
            }
        }

        private static bool ValidateSecurityDefinitions(
            WotDocument document, int maxDepth, List<WotDiagnostic> diagnostics)
        {
            var seen = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            JsonElement previousContainer = default;
            foreach (JsonProperty member in document.RootElement.EnumerateObject())
            {
                if (member.Name != "securityDefinitions")
                {
                    continue;
                }
                JsonElement definitions = member.Value;
                if (definitions.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("securityDefinitions must be an object.");
                }
                if (previousContainer.ValueKind != JsonValueKind.Undefined &&
                    (!WotJsonCanonicalizer.TryCanonicalize(
                        previousContainer, out string firstContainer, out string containerError) ||
                        !WotJsonCanonicalizer.TryCanonicalize(definitions, out string nextContainer, out containerError) ||
                        !string.Equals(firstContainer, nextContainer, StringComparison.Ordinal)))
                {
                    return Invalid("Repeated securityDefinitions containers are contradictory or incomparable. " +
                        containerError);
                }
                previousContainer = definitions;
                foreach (JsonProperty definition in definitions.EnumerateObject())
                {
                    if (definition.Value.ValueKind != JsonValueKind.Object)
                    {
                        return Invalid($"Security definition '{definition.Name}' must be an object.");
                    }
                    if (seen.TryGetValue(definition.Name, out JsonElement previous))
                    {
                        if (!WotJsonCanonicalizer.TryCanonicalize(previous, out string first, out string error) ||
                            !WotJsonCanonicalizer.TryCanonicalize(definition.Value, out string second, out error) ||
                            !string.Equals(first, second, StringComparison.Ordinal))
                        {
                            return Invalid(
                                $"Security definition '{definition.Name}' has contradictory " +
                                "or incomparable duplicates. " +
                                error);
                        }
                    }
                    else
                    {
                        seen.Add(definition.Name, definition.Value);
                    }
                }
            }
            var active = new HashSet<string>(StringComparer.Ordinal);
            var complete = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in seen.Keys)
            {
                if (!Visit(name, 0))
                {
                    return false;
                }
            }
            if (!ValidateOwner(document.RootElement))
            {
                return false;
            }
            foreach ((WotAffordanceKind _, string _, JsonElement definition) in EnumerateAffordances(document))
            {
                if (!ValidateOwner(definition))
                {
                    return false;
                }
            }
            return true;

            bool Visit(string name, int depth)
            {
                if (complete.TryGetValue(name, out int height))
                {
                    return height <= maxDepth - depth ||
                        Invalid($"The security definition graph at '{name}' exceeds the depth bound.");
                }
                if (!seen.TryGetValue(name, out JsonElement definition))
                {
                    return Invalid($"The required security definition '{name}' is not declared by its source.");
                }
                if (depth >= maxDepth || !active.Add(name))
                {
                    return Invalid($"The security definition graph at '{name}' is cyclic or exceeds the depth bound.");
                }
                try
                {
                    height = 1;
                    if (definition.TryGetProperty("scheme", out JsonElement scheme) &&
                        scheme.ValueKind == JsonValueKind.String &&
                        scheme.GetString() == "combo")
                    {
                        foreach (string key in s_comboKeys)
                        {
                            if (definition.TryGetProperty(key, out JsonElement names))
                            {
                                if (names.ValueKind != JsonValueKind.Array)
                                {
                                    return Invalid($"Combo security '{key}' must be an array of scheme names.");
                                }
                                if (!ValidateNames(names, depth + 1))
                                {
                                    return false;
                                }
                                foreach (string child in ElementTokens(names))
                                {
                                    height = Math.Max(height, complete[child] + 1);
                                }
                            }
                        }
                    }
                    complete.Add(name, height);
                    return true;
                }
                finally
                {
                    active.Remove(name);
                }
            }

            bool ValidateNames(JsonElement names, int depth)
            {
                if (names.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(names.GetString()))
                {
                    return Visit(names.GetString()!, depth);
                }
                if (names.ValueKind != JsonValueKind.Array || names.GetArrayLength() == 0)
                {
                    return Invalid("Security requirements must name at least one declared scheme.");
                }
                foreach (JsonElement item in names.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                    {
                        return Invalid("Every security requirement must be a non-empty scheme name.");
                    }
                    if (!Visit(item.GetString()!, depth))
                    {
                        return false;
                    }
                }
                return true;
            }

            bool ValidateOwner(JsonElement owner)
            {
                if (owner.ValueKind != JsonValueKind.Object)
                {
                    return true;
                }
                if (owner.TryGetProperty("security", out JsonElement security) && !ValidateNames(security, 0))
                {
                    return false;
                }
                if (owner.TryGetProperty("forms", out JsonElement forms) && forms.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement form in forms.EnumerateArray())
                    {
                        if (form.ValueKind == JsonValueKind.Object &&
                            form.TryGetProperty("security", out security) &&
                            !ValidateNames(security, 0))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            bool Invalid(string message)
            {
                AddError(diagnostics, WotDiagnosticCode.ValidationError, message, "securityDefinitions");
                return false;
            }
        }

        private static JsonObject SeedSecurityDefinitions(WotDocument document, string origin)
        {
            var definitions = new JsonObject();
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in document.SecurityDefinitions.Keys)
            {
                CopyScheme(null, name, document, origin, definitions, added);
            }
            return definitions;
        }

        private static void QualifyProjectionSecurity(JsonObject target)
        {
            QualifySecurityRequirement(target, null);
            if (target["forms"] is JsonArray forms)
            {
                foreach (JsonNode? form in forms)
                {
                    if (form is JsonObject value)
                    {
                        QualifySecurityRequirement(value, null);
                    }
                }
            }
        }

        private static void QualifySecurityRequirement(JsonObject value, string? sourceName)
        {
            if (value["security"] is JsonValue scalar && scalar.TryGetValue(out string? name))
            {
                value["security"] = Qualify(sourceName, name!);
            }
            else if (value["security"] is JsonArray names)
            {
                for (int index = 0; index < names.Count; index++)
                {
                    if (names[index] is JsonValue item && item.TryGetValue(out string? entry))
                    {
                        names[index] = Qualify(sourceName, entry!);
                    }
                }
            }
        }

        private static JsonArray BuildTypeArray(JsonElement types, WotDocumentKind resultKind)
        {
            var array = new JsonArray();
            foreach (string token in ElementTokens(types))
            {
                if (!string.Equals(
                        token, WotVocabulary.ProjectionAnnotation, StringComparison.Ordinal))
                {
                    array.Add(JsonValue.Create(token));
                }
            }
            string? resultType = resultKind switch
            {
                WotDocumentKind.ThingDescription => "Thing",
                WotDocumentKind.ThingModel => "tm:ThingModel",
                _ => null
            };
            if (resultType is not null)
            {
                bool present = false;
                foreach (string token in ElementTokens(types))
                {
                    present |= token == resultType;
                }
                if (!present)
                {
                    array.Add(JsonValue.Create(resultType));
                }
            }
            return array;
        }

        private static List<string> NamesFromNode(JsonNode node)
        {
            var names = new List<string>();
            if (node is JsonValue value &&
                value.TryGetValue(out string? single) &&
                single is not null)
            {
                names.Add(single);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? entry in array)
                {
                    if (entry is JsonValue item &&
                        item.TryGetValue(out string? name) &&
                        name is not null)
                    {
                        names.Add(name);
                    }
                }
            }
            return names;
        }

        private static List<string> NamesFromElement(JsonElement element)
        {
            var names = new List<string>();
            if (element.ValueKind == JsonValueKind.String)
            {
                names.Add(element.GetString()!);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        names.Add(item.GetString()!);
                    }
                }
            }
            return names;
        }

        private static IEnumerable<string> NodeTokens(JsonNode? node)
        {
            if (node is JsonValue value &&
                value.TryGetValue(out string? single) &&
                single is not null)
            {
                yield return single;
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? entry in array)
                {
                    if (entry is JsonValue item &&
                        item.TryGetValue(out string? name) &&
                        name is not null)
                    {
                        yield return name;
                    }
                }
            }
        }

        private static IEnumerable<string> ElementTokens(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                yield return element.GetString()!;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        yield return item.GetString()!;
                    }
                }
            }
        }

        private static bool VerifyDigest(ReadOnlyMemory<byte> content, string digest)
        {
            const string prefix = "sha-256:";
            if (!digest.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            string expected = digest[prefix.Length..];
            byte[] hash;
#if NET6_0_OR_GREATER
            hash = SHA256.HashData(content.Span);
#else
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(content.ToArray());
            }
#endif
            var builder = new StringBuilder(hash.Length * 2);
            for (int ii = 0; ii < hash.Length; ii++)
            {
                builder.Append(hash[ii].ToString("x2", CultureInfo.InvariantCulture));
            }
            return string.Equals(
                builder.ToString(), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveHref(string baseHref, string href)
        {
            if (HasScheme(href))
            {
                return href;
            }
            if (!TrySplitBase(
                    baseHref, out string scheme, out string? authority, out string basePath, out string? baseQuery))
            {
                return href;
            }
            string prefix = authority is null
                ? scheme + ":"
                : scheme + "://" + authority;
            SplitUriSuffix(href, out string referencePath, out string? query, out string? fragment);
            if (referencePath.StartsWith("//", StringComparison.Ordinal))
            {
                int slash = referencePath.IndexOf('/', 2);
                string networkAuthority = slash < 0 ? referencePath : referencePath[..slash];
                string networkPath = slash < 0 ? string.Empty : referencePath[slash..];
                return scheme + ":" + networkAuthority + RemoveDotSegments(networkPath) + query + fragment;
            }
            string path;
            if (referencePath.Length == 0)
            {
                path = basePath;
                query ??= baseQuery;
            }
            else if (referencePath[0] == '/')
            {
                path = RemoveDotSegments(referencePath);
            }
            else
            {
                path = RemoveDotSegments(MergePath(basePath, referencePath, authority is not null));
            }
            return prefix + path + query + fragment;
        }

        private static bool HasScheme(string value)
        {
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 && Uri.CheckSchemeName(value[..colon]);
        }

        private static bool TrySplitBase(
            string baseHref,
            out string scheme,
            out string? authority,
            out string path,
            out string? query)
        {
            scheme = string.Empty;
            authority = null;
            path = string.Empty;
            query = null;
            if (!HasScheme(baseHref))
            {
                return false;
            }
            SplitUriSuffix(baseHref, out string main, out query, out _);
            int colon = main.IndexOf(':', StringComparison.Ordinal);
            scheme = main[..colon];
            string rest = main[(colon + 1)..];
            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                string afterAuthority = rest[2..];
                int slash = afterAuthority.IndexOf('/', StringComparison.Ordinal);
                if (slash < 0)
                {
                    authority = afterAuthority;
                    path = string.Empty;
                }
                else
                {
                    authority = afterAuthority[..slash];
                    path = afterAuthority[slash..];
                }
            }
            else
            {
                path = rest;
            }
            return true;
        }

        private static void SplitUriSuffix(
            string value, out string path, out string? query, out string? fragment)
        {
            int hash = value.IndexOf('#', StringComparison.Ordinal);
            fragment = hash < 0 ? null : value[hash..];
            string withoutFragment = hash < 0 ? value : value[..hash];
            int question = withoutFragment.IndexOf('?', StringComparison.Ordinal);
            query = question < 0 ? null : withoutFragment[question..];
            path = question < 0 ? withoutFragment : withoutFragment[..question];
        }

        private static string MergePath(string basePath, string reference, bool hasAuthority)
        {
            if (hasAuthority && basePath.Length == 0)
            {
                return "/" + reference;
            }
            int lastSlash = basePath.LastIndexOf('/');
            return lastSlash < 0
                ? reference
                : basePath[..(lastSlash + 1)] + reference;
        }

        private static string RemoveDotSegments(string path)
        {
            var output = new StringBuilder();
            string input = path;
            while (input.Length > 0)
            {
                if (input.StartsWith("../", StringComparison.Ordinal))
                {
                    input = input[3..];
                }
                else if (input.StartsWith("./", StringComparison.Ordinal))
                {
                    input = input[2..];
                }
                else if (input.StartsWith("/./", StringComparison.Ordinal))
                {
                    input = "/" + input[3..];
                }
                else if (string.Equals(input, "/.", StringComparison.Ordinal))
                {
                    input = "/";
                }
                else if (input.StartsWith("/../", StringComparison.Ordinal))
                {
                    input = "/" + input[4..];
                    RemoveLastSegment(output);
                }
                else if (string.Equals(input, "/..", StringComparison.Ordinal))
                {
                    input = "/";
                    RemoveLastSegment(output);
                }
                else if (string.Equals(input, ".", StringComparison.Ordinal) ||
                    string.Equals(input, "..", StringComparison.Ordinal))
                {
                    input = string.Empty;
                }
                else
                {
                    int start = input.StartsWith('/') ? 1 : 0;
                    int next = input.IndexOf('/', start);
                    if (next < 0)
                    {
                        output.Append(input);
                        input = string.Empty;
                    }
                    else
                    {
                        output.Append(input[..next]);
                        input = input[next..];
                    }
                }
            }
            return output.ToString();
        }

        private static void RemoveLastSegment(StringBuilder builder)
        {
            for (int ii = builder.Length - 1; ii >= 0; ii--)
            {
                if (builder[ii] == '/')
                {
                    builder.Length = ii;
                    return;
                }
            }
            builder.Length = 0;
        }

        private static string MapName(WotAffordanceKind kind)
        {
            return kind switch
            {
                WotAffordanceKind.Property => "properties",
                WotAffordanceKind.Action => "actions",
                WotAffordanceKind.Event => "events",
                _ => "properties"
            };
        }

        private static string EscapePointer(string token)
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

        private static string ApplyPrefix(WotProjectionManifestSource source, string name)
        {
            return string.IsNullOrEmpty(source.NamePrefix)
                ? name
                : source.NamePrefix + UpperFirst(name);
        }

        private static string UpperFirst(string name)
        {
            if (name.Length == 0)
            {
                return name;
            }
            int length = char.IsSurrogatePair(name, 0) ? 2 : 1;
            return name[..length].ToUpperInvariant() + name[length..];
        }

        private static string Qualify(string? sourceName, string schemeName)
        {
            return sourceName is null
                ? "q:p:" + EncodeSecurityName(schemeName)
                : "q:s:" + EncodeSecurityName(sourceName) + ":" + EncodeSecurityName(schemeName);
        }

        private static string EncodeSecurityName(string name)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(name))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static int FindSourceIndex(
            ArrayOf<WotProjectionManifestSource> sources,
            string documentPart)
        {
            for (int ii = 0; ii < sources.Count; ii++)
            {
                if (string.Equals(
                        sources[ii].Href, documentPart, StringComparison.Ordinal))
                {
                    return ii;
                }
            }
            return -1;
        }

        private static bool HasFilters(WotProjectionManifestSource source)
        {
            return !source.Filters.IsNull && source.Filters.Count > 0;
        }

        private static string SplitDocumentPart(string reference)
        {
            int hash = reference.IndexOf('#', StringComparison.Ordinal);
            return hash < 0 ? reference : reference[..hash];
        }

        private static string SplitPointer(string reference)
        {
            int hash = reference.IndexOf('#', StringComparison.Ordinal);
            return hash < 0 ? string.Empty : reference[(hash + 1)..];
        }

        private static string? ReadBase(WotDocument document)
        {
            return document.RootElement.TryGetProperty("base", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string EffectiveBase(WotDocument document, string? location)
        {
            string? declared = ReadBase(document);
            if (string.IsNullOrEmpty(declared))
            {
                return location ?? string.Empty;
            }
            return string.IsNullOrEmpty(location)
                ? declared!
                : ResolveHref(location!, declared!);
        }

        private static JsonNode? CloneNode(JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        /// <summary>
        /// Clones a node without losing its immutable raw-value leaves.
        /// </summary>
        private static JsonNode? CloneNode(JsonNode node)
        {
            if (node is JsonObject value)
            {
                var copy = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> member in value)
                {
                    copy[member.Key] = member.Value is null ? null : CloneNode(member.Value);
                }
                return copy;
            }
            if (node is JsonArray array)
            {
                var copy = new JsonArray();
                foreach (JsonNode? item in array)
                {
                    copy.Add(item is null ? null : CloneNode(item));
                }
                return copy;
            }
            return node is JsonValue scalar && scalar.TryGetValue(out PreservedLiteral literal)
                ? CloneLiteral(literal.Value)
                : node.DeepClone();
        }

        private static JsonObject CloneObject(JsonElement element)
        {
            return (JsonObject)JsonNode.Parse(element.GetRawText())!;
        }

        /// <summary>
        /// Serialises a resolved projection.
        /// </summary>
        /// <remarks>
        /// The tree is written node by node rather than through
        /// <see cref="JsonNode.WriteTo(Utf8JsonWriter, JsonSerializerOptions)"/>,
        /// which serialises a CLR-backed value through the default
        /// <see cref="JsonSerializerOptions"/>. Those options carry no type
        /// resolver in a Native AOT application, so writing a plain string
        /// throws there while working in a reflection-enabled test host.
        /// </remarks>
        private static byte[] Serialize(JsonObject root)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = false,
                    SkipValidation = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }))
            {
                WriteNode(writer, root);
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Writes one node of a projection tree without reflection.
        /// </summary>
        private static void WriteNode(
            Utf8JsonWriter writer, JsonNode? node, bool literal = false, bool indexMap = false)
        {
            if (literal && node is not null)
            {
                // Parsed literal nodes retain duplicate members without materializing a unique-key dictionary.
                node.WriteTo(writer);
                return;
            }
            switch (node)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case JsonObject o:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, JsonNode?> member in o)
                    {
                        writer.WritePropertyName(member.Key);
                        WriteNode(writer, member.Value,
                            literal: !indexMap && IsLiteralMember(member.Key),
                            indexMap: !indexMap &&
                                (WotNodeSetConverter.IsSchemaDeclarationMap(member.Key) ||
                                    member.Key == "securityDefinitions"));
                    }
                    writer.WriteEndObject();
                    break;
                case JsonArray a:
                    writer.WriteStartArray();
                    foreach (JsonNode? item in a)
                    {
                        WriteNode(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    WriteValue(writer, (JsonValue)node);
                    break;
            }
        }

        /// <summary>
        /// Writes a leaf value, preferring the parsed representation and
        /// falling back to the CLR types a projection can introduce.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        private static void WriteValue(Utf8JsonWriter writer, JsonValue value)
        {
            if (value.TryGetValue(out PreservedLiteral literal))
            {
                // The complete result is parsed afterward to enforce combined size and nesting limits.
                writer.WriteRawValue(literal.Value.GetRawText(), skipInputValidation: true);
            }
            else if (value.TryGetValue(out JsonElement element))
            {
                element.WriteTo(writer);
            }
            else if (value.TryGetValue(out string? text))
            {
                writer.WriteStringValue(text);
            }
            else if (value.TryGetValue(out bool flag))
            {
                writer.WriteBooleanValue(flag);
            }
            else if (value.TryGetValue(out long integer))
            {
                writer.WriteNumberValue(integer);
            }
            else if (value.TryGetValue(out double number))
            {
                WotJsonCanonicalizer.WriteNumberValue(writer, number);
            }
            else
            {
                // A projection only ever introduces the values above, so
                // reaching this is a defect rather than an input the document
                // could have caused.
                throw new NotSupportedException(
                    $"A projection value of an unexpected kind cannot be written: {value.GetValueKind()}.");
            }
        }

        private static void AddError(
            List<WotDiagnostic> diagnostics,
            WotDiagnosticCode code,
            string message,
            string? reference = null)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                code,
                message,
                reference is null ? null : new WotLocation(reference: reference)));
        }

        private static void AddWarning(
            List<WotDiagnostic> diagnostics,
            WotDiagnosticCode code,
            string message,
            string? reference = null)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Warning,
                code,
                message,
                reference is null ? null : new WotLocation(reference: reference)));
        }

        private static bool HasErrors(List<WotDiagnostic> diagnostics)
        {
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountErrors(List<WotDiagnostic> diagnostics)
        {
            int count = 0;
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    count++;
                }
            }
            return count;
        }

        private enum PredicateMatch
        {
            Mismatch,
            Match,
            Unknown
        }

        private sealed class ResolvedSource
        {
            public WotProjectionManifestSource Source { get; init; } = null!;

            public WotDocument Document { get; init; } = null!;

            public string DocumentHref { get; init; } = string.Empty;

            public string BaseHref { get; init; } = string.Empty;
        }

        private sealed class ResolvedAffordance
        {
            public WotAffordanceKind Kind { get; init; }

            public string Name { get; init; } = string.Empty;

            public JsonObject Value { get; init; } = null!;

            public ResolvedSource Source { get; init; } = null!;

            public string SourceName { get; init; } = string.Empty;

            public string Pointer { get; init; } = string.Empty;

            public JsonElement Definition { get; init; }

            public bool Supporting { get; init; }

            public ReferenceOwner? GeneratedFormOwner { get; set; }
        }

        private sealed class Selection
        {
            public Selection(WotDocument document, string documentHref, JsonObject securityDefinitions)
            {
                Document = document;
                DocumentHref = documentHref;
                SecurityDefinitions = securityDefinitions;
            }

            public WotDocument Document { get; }

            public string DocumentHref { get; }

            public List<ResolvedAffordance> Properties { get; } = [];

            public List<ResolvedAffordance> Actions { get; } = [];

            public List<ResolvedAffordance> Events { get; } = [];

            public List<ResolvedAffordance> Members { get; } = [];

            public JsonObject SecurityDefinitions { get; }

            public HashSet<string> SecurityAdded { get; } =
                new(StringComparer.Ordinal);

            public bool Claim(WotAffordanceKind kind, string name)
            {
                return Taken(kind).Add(name);
            }

            public bool IsClaimed(WotAffordanceKind kind, string name)
            {
                return Taken(kind).Contains(name);
            }

            public ResolvedAffordance Add(
                WotAffordanceKind kind, string name, JsonObject value,
                ResolvedSource source, string sourceName, JsonElement definition, bool supporting = false)
            {
                var member = new ResolvedAffordance
                {
                    Kind = kind,
                    Name = name,
                    Value = value,
                    Source = source,
                    SourceName = sourceName,
                    Pointer = "/" + MapName(kind) + "/" + EscapePointer(sourceName),
                    Definition = definition,
                    Supporting = supporting
                };
                List(kind).Add(member);
                Members.Add(member);
                m_locations.TryAdd((source.DocumentHref, member.Pointer), member);
                return member;
            }

            public bool TryLocate(ResolvedSource source, string pointer, out ResolvedAffordance? member)
            {
                return m_locations.TryGetValue((source.DocumentHref, pointer), out member);
            }

            public string AllocateSupportName(
                WotAffordanceKind kind, ResolvedSource source, string name, string pointer)
            {
                if (Claim(kind, name))
                {
                    return name;
                }
                string stem = "q:d:" + EncodeSecurityName(source.Source.SourceName) + ":" + EncodeSecurityName(pointer);
                string candidate = stem;
                for (int suffix = 1; !Claim(kind, candidate); suffix++)
                {
                    candidate = stem + ":" + suffix.ToString(CultureInfo.InvariantCulture);
                }
                return candidate;
            }

            private List<ResolvedAffordance> List(WotAffordanceKind kind)
            {
                return kind switch
                {
                    WotAffordanceKind.Action => Actions,
                    WotAffordanceKind.Event => Events,
                    _ => Properties
                };
            }

            private HashSet<string> Taken(WotAffordanceKind kind)
            {
                return kind switch
                {
                    WotAffordanceKind.Action => m_takenActions,
                    WotAffordanceKind.Event => m_takenEvents,
                    _ => m_takenProperties
                };
            }

            private readonly HashSet<string> m_takenProperties =
                new(StringComparer.Ordinal);

            private readonly HashSet<string> m_takenActions =
                new(StringComparer.Ordinal);

            private readonly HashSet<string> m_takenEvents =
                new(StringComparer.Ordinal);

            private readonly Dictionary<(string DocumentHref, string Pointer), ResolvedAffordance> m_locations = [];
        }

        private readonly IWotThingResolver m_thingResolver;
        private readonly WotNodeSetConverterOptions m_options;
        private static readonly string[] s_comboKeys = ["allOf", "oneOf"];
    }
}
