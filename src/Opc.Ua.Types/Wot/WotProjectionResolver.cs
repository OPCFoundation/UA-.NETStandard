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
    public sealed class WotProjectionResolver
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

            WotProjection? projection = WotProjection.Parse(document, diagnostics);
            if (projection is null)
            {
                return new WotConversionResult<WotDocument>(null, diagnostics);
            }

            WotResolutionContext context = resolutionContext ??
                new WotResolutionContext(m_options.ToResolverOptions());
            var resolving = new HashSet<string>(StringComparer.Ordinal);

            byte[]? bytes = await ResolveViewAsync(
                document,
                projection,
                resolving,
                context,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            await CheckOrganizingAcyclicAsync(
                projection,
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
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            int errorsAtEntry = CountErrors(diagnostics);
            var openDocuments = new List<WotDocument>();
            try
            {
                int count = projection.Sources.Count;
                var sources = new ResolvedSource?[count];
                for (int ii = 0; ii < count; ii++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sources[ii] = await ResolveSourceAsync(
                        projection.Sources[ii],
                        resolving,
                        context,
                        diagnostics,
                        openDocuments,
                        cancellationToken).ConfigureAwait(false);
                }

                JsonArray? mergedContext = MergeContext(
                    projectionDocument, sources, diagnostics);
                JsonObject securityDefinitions =
                    SeedSecurityDefinitions(projectionDocument);
                var selection = new Selection(securityDefinitions);

                var references = projection.References;
                var referenceOwner = new int[references.Count];
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

                JsonObject root = AssembleRoot(
                    projectionDocument, mergedContext, securityDefinitions, selection);
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

        private async ValueTask<ResolvedSource?> ResolveSourceAsync(
            WotProjectionManifestSource source,
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            List<WotDocument> openDocuments,
            CancellationToken cancellationToken)
        {
            string href = source.Href;

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
                    source, resolving, context, diagnostics, openDocuments, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                context.Leave(href);
            }
        }

        private async ValueTask<ResolvedSource?> ResolveSourceBoundedAsync(
            WotProjectionManifestSource source,
            HashSet<string> resolving,
            WotResolutionContext context,
            List<WotDiagnostic> diagnostics,
            List<WotDocument> openDocuments,
            CancellationToken cancellationToken)
        {
            string href = source.Href;
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
                    WotProjection? nested =
                        WotProjection.Parse(document, diagnostics);
                    if (nested is null)
                    {
                        return null;
                    }
                    byte[]? nestedBytes = await ResolveViewAsync(
                        document,
                        nested,
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
                        BaseHref = ReadBase(resolvedView) ?? href
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
                BaseHref = ReadBase(document) ?? href
            };
        }

        private async ValueTask CheckOrganizingAcyclicAsync(
            WotProjection projection,
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
            var budget = new int[] { context.Options.MaxDocuments, 0 };
            for (int ii = 0; ii < projection.OrganizingLinks.Count; ii++)
            {
                await WalkOrganizesAsync(
                    projection.OrganizingLinks[ii].Href,
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
                            next,
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
                ? reference.Substring(separator + 1)
                : reference;
        }

        private static void SelectEnumerated(
            ResolvedSource source,
            WotProjectionReference reference,
            Selection selection,
            List<WotDiagnostic> diagnostics)
        {
            string pointer = SplitPointer(reference.Reference);
            if (!WotDocument.TryEvaluatePointer(
                    source.Document.RootElement, pointer, out JsonElement definition) ||
                definition.ValueKind != JsonValueKind.Object)
            {
                AddError(
                    diagnostics,
                    WotDiagnosticCode.ProjectionSourceUnresolved,
                    $"The reference '{reference.Reference}' does not resolve to an " +
                    "affordance definition.",
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
            JsonObject target = CloneObject(definition);
            if (!sourceRouting)
            {
                target.Remove("forms");
                target.Remove("security");
            }
            MergeAnnotation(target, reference.Annotations, sourceRouting);
            if (sourceRouting)
            {
                TransformForms(target, source, selection);
            }
            CarryAnchor(target, source.Document);
            target["uav:resolvedFrom"] = reference.Reference;
            selection.Add(reference.AffordanceKind, reference.Name, target);
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
                if (MatchesSource(source.Source, kind, name, definition))
                {
                    candidates.Add((kind, name, definition));
                }
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
                if (!selection.Claim(kind, viewName))
                {
                    AddWarning(
                        diagnostics,
                        WotDiagnosticCode.ProjectionSelectionDropped,
                        $"The bulk selection '{viewName}' was already made; the " +
                        "later selection is dropped.",
                        source.Source.Href);
                    continue;
                }

                bool sourceRouting =
                    source.Source.Routing == WotProjectionRouting.Source;
                JsonObject target = CloneObject(definition);
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
                target["uav:resolvedFrom"] =
                    BuildBulkProvenance(source.Source.Href, kind, name);
                selection.Add(kind, viewName, target);
            }
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
            bool sourceRouting)
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
                    target["@type"] = UnionTypes(target["@type"], member.Value);
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
                target[member.Name] = CloneNode(member.Value);
            }
        }

        private static void TransformForms(
            JsonObject target,
            ResolvedSource source,
            Selection selection)
        {
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
                        source.Document.SecurityDefinitions,
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
            string sourceName,
            IReadOnlyDictionary<string, JsonElement> sourceDefinitions,
            List<string> schemeNames,
            JsonObject securityDefinitions,
            HashSet<string> securityAdded)
        {
            for (int ii = 0; ii < schemeNames.Count; ii++)
            {
                CopyScheme(
                    sourceName,
                    schemeNames[ii],
                    sourceDefinitions,
                    securityDefinitions,
                    securityAdded);
            }
        }

        private static void CopyScheme(
            string sourceName,
            string schemeName,
            IReadOnlyDictionary<string, JsonElement> sourceDefinitions,
            JsonObject securityDefinitions,
            HashSet<string> securityAdded)
        {
            if (!sourceDefinitions.TryGetValue(schemeName, out JsonElement definition) ||
                definition.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string qualified = Qualify(sourceName, schemeName);
            if (!securityAdded.Add(qualified))
            {
                return;
            }
            JsonObject copy = CloneObject(definition);
            var children = new List<string>();
            for (int ii = 0; ii < s_comboKeys.Length; ii++)
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
                    sourceDefinitions,
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
            string? carried = rootAnchor ?? WotAnchorScope.ReadTerm(
                sourceDocument.RootElement, WotAnchorScope.IdentityTerm);
            if (carried is not null)
            {
                target[WotAnchorScope.AnchorTerm] = carried;
            }
        }

        private static JsonArray? MergeContext(
            WotDocument projectionDocument,
            ResolvedSource?[] sources,
            List<WotDiagnostic> diagnostics)
        {
            var items = new List<string?>();
            var inline = new JsonObject();
            bool inlineAdded = false;
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            var strings = new HashSet<string>(StringComparer.Ordinal);
            var appended = new List<string>();

            if (projectionDocument.TryGetContext(out JsonElement projectionContext))
            {
                foreach (JsonElement entry in ContextEntries(projectionContext))
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        string value = entry.GetString()!;
                        items.Add(value);
                        strings.Add(value);
                    }
                    else if (entry.ValueKind == JsonValueKind.Object)
                    {
                        if (!inlineAdded)
                        {
                            items.Add(null);
                            inlineAdded = true;
                        }
                        foreach (JsonProperty binding in entry.EnumerateObject())
                        {
                            inline[binding.Name] = CloneNode(binding.Value);
                            bindings[binding.Name] = ValueKey(binding.Value);
                        }
                    }
                }
            }

            for (int ii = 0; ii < sources.Length; ii++)
            {
                ResolvedSource? source = sources[ii];
                if (source is null ||
                    !source.Document.TryGetContext(out JsonElement sourceContext))
                {
                    continue;
                }
                foreach (JsonElement entry in ContextEntries(sourceContext))
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        string value = entry.GetString()!;
                        if (strings.Add(value))
                        {
                            appended.Add(value);
                        }
                    }
                    else if (entry.ValueKind == JsonValueKind.Object)
                    {
                        if (!inlineAdded)
                        {
                            items.Add(null);
                            inlineAdded = true;
                        }
                        MergeContextBindings(entry, inline, bindings, diagnostics);
                    }
                }
            }

            if (items.Count == 0 && appended.Count == 0)
            {
                return null;
            }
            // JsonArray.Add<T> converts a CLR value through the default
            // JsonSerializerOptions, which carries no type resolver in a Native
            // AOT application and throws for a plain string. JsonValue.Create
            // builds the node directly, as the rest of this file already does.
            var result = new JsonArray();
            for (int ii = 0; ii < items.Count; ii++)
            {
                if (items[ii] is null)
                {
                    result.Add(inline);
                }
                else
                {
                    result.Add(JsonValue.Create(items[ii]));
                }
            }
            for (int ii = 0; ii < appended.Count; ii++)
            {
                result.Add(JsonValue.Create(appended[ii]));
            }
            return result;
        }

        private static void MergeContextBindings(
            JsonElement entry,
            JsonObject inline,
            Dictionary<string, string> bindings,
            List<WotDiagnostic> diagnostics)
        {
            foreach (JsonProperty binding in entry.EnumerateObject())
            {
                string key = ValueKey(binding.Value);
                if (bindings.TryGetValue(binding.Name, out string? existing))
                {
                    if (!string.Equals(existing, key, StringComparison.Ordinal))
                    {
                        AddError(
                            diagnostics,
                            WotDiagnosticCode.ProjectionContextConflict,
                            $"The context prefix '{binding.Name}' is bound to two " +
                            "different URIs across the projection's sources.",
                            binding.Name);
                    }
                }
                else
                {
                    inline[binding.Name] = CloneNode(binding.Value);
                    bindings[binding.Name] = key;
                }
            }
        }

        private static JsonObject AssembleRoot(
            WotDocument projectionDocument,
            JsonArray? mergedContext,
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
                        if (mergedContext is not null)
                        {
                            root["@context"] = mergedContext;
                        }
                        break;
                    case "@type":
                        root["@type"] = BuildTypeArray(member.Value);
                        break;
                    case "uav:projects":
                    case "properties":
                    case "actions":
                    case "events":
                    case "securityDefinitions":
                        break;
                    default:
                        root[member.Name] = CloneNode(member.Value);
                        break;
                }
            }
            if (!root.ContainsKey("@context") && mergedContext is not null)
            {
                root["@context"] = mergedContext;
            }
            if (securityDefinitions.Count > 0)
            {
                root["securityDefinitions"] = securityDefinitions;
            }
            AddAffordanceMap(root, "properties", selection.Properties);
            AddAffordanceMap(root, "actions", selection.Actions);
            AddAffordanceMap(root, "events", selection.Events);
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
            foreach (var pair in EnumerateAffordanceMap(
                document, "properties", WotAffordanceKind.Property))
            {
                yield return pair;
            }
            foreach (var pair in EnumerateAffordanceMap(
                document, "actions", WotAffordanceKind.Action))
            {
                yield return pair;
            }
            foreach (var pair in EnumerateAffordanceMap(
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
            WotProjectionManifestSource source,
            WotAffordanceKind kind,
            string name,
            JsonElement definition)
        {
            if (source.SelectAll)
            {
                return true;
            }
            if (source.Filters.IsNull)
            {
                return false;
            }
            for (int ii = 0; ii < source.Filters.Count; ii++)
            {
                if (MatchesFilter(source.Filters[ii], kind, definition))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MatchesFilter(
            WotProjectionFilter filter,
            WotAffordanceKind kind,
            JsonElement definition)
        {
            if (filter.AffordanceKind != WotAffordanceKind.Any &&
                filter.AffordanceKind != kind)
            {
                return false;
            }
            if (filter.SemanticId is not null &&
                !HasSemanticId(definition, filter.SemanticId))
            {
                return false;
            }
            if (!filter.TypeTokens.IsNull)
            {
                for (int ii = 0; ii < filter.TypeTokens.Count; ii++)
                {
                    if (!HasTypeToken(definition, filter.TypeTokens[ii]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool HasSemanticId(JsonElement definition, string semanticId)
        {
            return definition.TryGetProperty(
                    "uav:semanticId", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                string.Equals(value.GetString(), semanticId, StringComparison.Ordinal);
        }

        private static bool HasTypeToken(JsonElement definition, string token)
        {
            if (!definition.TryGetProperty("@type", out JsonElement types))
            {
                return false;
            }
            if (types.ValueKind == JsonValueKind.String)
            {
                return string.Equals(types.GetString(), token, StringComparison.Ordinal);
            }
            if (types.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in types.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        string.Equals(item.GetString(), token, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
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

        private static JsonObject SeedSecurityDefinitions(WotDocument document)
        {
            if (document.RootElement.TryGetProperty(
                    "securityDefinitions", out JsonElement definitions) &&
                definitions.ValueKind == JsonValueKind.Object)
            {
                return CloneObject(definitions);
            }
            return new JsonObject();
        }

        private static JsonNode UnionTypes(JsonNode? existing, JsonElement additional)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var tokens = new List<string>();
            foreach (string token in NodeTokens(existing))
            {
                if (seen.Add(token))
                {
                    tokens.Add(token);
                }
            }
            foreach (string token in ElementTokens(additional))
            {
                if (seen.Add(token))
                {
                    tokens.Add(token);
                }
            }
            if (tokens.Count == 1)
            {
                return JsonValue.Create(tokens[0]);
            }
            var array = new JsonArray();
            for (int ii = 0; ii < tokens.Count; ii++)
            {
                array.Add(JsonValue.Create(tokens[ii]));
            }
            return array;
        }

        private static JsonArray BuildTypeArray(JsonElement types)
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
            return array;
        }

        private static List<string> NamesFromNode(JsonNode node)
        {
            var names = new List<string>();
            if (node is JsonValue value && value.TryGetValue(out string? single) &&
                single is not null)
            {
                names.Add(single);
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? entry in array)
                {
                    if (entry is JsonValue item && item.TryGetValue(out string? name) &&
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
            if (node is JsonValue value && value.TryGetValue(out string? single) &&
                single is not null)
            {
                yield return single;
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? entry in array)
                {
                    if (entry is JsonValue item && item.TryGetValue(out string? name) &&
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

        private static IEnumerable<JsonElement> ContextEntries(JsonElement context)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    yield return entry;
                }
            }
            else
            {
                yield return context;
            }
        }

        private static string ValueKey(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String
                ? "s:" + value.GetString()
                : "r:" + value.GetRawText();
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
            if (string.IsNullOrEmpty(href) || HasScheme(href))
            {
                return href;
            }
            if (!TrySplitBase(
                    baseHref, out string scheme, out string? authority, out string basePath))
            {
                return href;
            }
            string prefix = authority is null
                ? scheme + ":"
                : scheme + "://" + authority;
            if (href.StartsWith("//", StringComparison.Ordinal))
            {
                return scheme + ":" + href;
            }
            if (href.StartsWith('/'))
            {
                return prefix + href;
            }
            if (href.StartsWith('?') || href.StartsWith('#'))
            {
                return prefix + basePath + href;
            }
            string merged = MergePath(basePath, href, authority is not null);
            return prefix + RemoveDotSegments(merged);
        }

        private static bool HasScheme(string value)
        {
            if (value.Length == 0 || !char.IsLetter(value[0]))
            {
                return false;
            }
            for (int ii = 0; ii < value.Length; ii++)
            {
                char c = value[ii];
                if (c == ':')
                {
                    return ii > 0;
                }
                if (!char.IsLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                {
                    return false;
                }
            }
            return false;
        }

        private static bool TrySplitBase(
            string baseHref,
            out string scheme,
            out string? authority,
            out string path)
        {
            scheme = string.Empty;
            authority = null;
            path = string.Empty;
            int colon = baseHref.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return false;
            }
            scheme = baseHref[..colon];
            string rest = baseHref[(colon + 1)..];
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

        private static string BuildBulkProvenance(
            string href,
            WotAffordanceKind kind,
            string name)
        {
            return href + "#/" + MapName(kind) + "/" + EscapePointer(name);
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
            return char.ToUpperInvariant(name[0]) + name[1..];
        }

        private static string Qualify(string sourceName, string schemeName)
        {
            return sourceName + "_" + schemeName;
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

        private static JsonNode? CloneNode(JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        /// <summary>
        /// Clones a node by round-tripping it through its serialised form.
        /// </summary>
        /// <remarks>
        /// Written with <see cref="WriteNode"/> rather than
        /// <c>JsonNode.ToJsonString()</c>, which serialises a CLR-backed value
        /// through the default <see cref="JsonSerializerOptions"/>. Those
        /// options carry no type resolver in a Native AOT application, so
        /// cloning a node holding a plain string throws there while working in
        /// a reflection-enabled test host.
        /// </remarks>
        private static JsonNode? CloneNode(JsonNode node)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteNode(writer, node);
            }
            return JsonNode.Parse(buffer.ToArray());
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
        private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
        {
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
                        WriteNode(writer, member.Value);
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
        private static void WriteValue(Utf8JsonWriter writer, JsonValue value)
        {
            if (value.TryGetValue(out JsonElement element))
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
                writer.WriteNumberValue(number);
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

        private sealed class ResolvedSource
        {
            public WotProjectionManifestSource Source { get; init; } = null!;

            public WotDocument Document { get; init; } = null!;

            public string BaseHref { get; init; } = string.Empty;
        }

        private sealed class ResolvedAffordance
        {
            public string Name { get; init; } = string.Empty;

            public JsonObject Value { get; init; } = null!;
        }

        private sealed class Selection
        {
            public Selection(JsonObject securityDefinitions)
            {
                SecurityDefinitions = securityDefinitions;
            }

            public List<ResolvedAffordance> Properties { get; } = [];

            public List<ResolvedAffordance> Actions { get; } = [];

            public List<ResolvedAffordance> Events { get; } = [];

            public JsonObject SecurityDefinitions { get; }

            public HashSet<string> SecurityAdded { get; } =
                new(StringComparer.Ordinal);

            public bool Claim(WotAffordanceKind kind, string name)
            {
                return Taken(kind).Add(name);
            }

            public void Add(WotAffordanceKind kind, string name, JsonObject value)
            {
                List(kind).Add(new ResolvedAffordance { Name = name, Value = value });
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
        }

        private readonly IWotThingResolver m_thingResolver;
        private readonly WotNodeSetConverterOptions m_options;
        private static readonly string[] s_comboKeys = ["allOf", "oneOf"];
    }
}
