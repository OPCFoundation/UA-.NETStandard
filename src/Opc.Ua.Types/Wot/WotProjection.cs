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

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A projection document: a Thing Description or Thing Model that declares,
    /// rather than defines, its affordances.
    /// </summary>
    /// <remarks>
    /// See WoT Binding Section 12. A projection document names one or more
    /// source documents and states which of their affordances the view is
    /// assembled from. It carries references and annotations only, so there is
    /// nothing to drift from the sources it projects.
    /// </remarks>
    public sealed class WotProjection
    {
        /// <summary>
        /// Gets the declared kind of the resolved document.
        /// </summary>
        public WotDocumentKind ResultKind { get; private set; }

        /// <summary>
        /// Gets the machine-readable purpose the view serves.
        /// </summary>
        /// <remarks>
        /// An absolute IRI. A projection document shall declare one: a view
        /// whose purpose is not machine-readable is not reusable, because a
        /// consumer cannot tell whether the view it found is the view it needs.
        /// </remarks>
        public string Scenario { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the source manifest, in authored order.
        /// </summary>
        /// <remarks>
        /// Selection order follows this order, so it is significant.
        /// </remarks>
        public ArrayOf<WotProjectionManifestSource> Sources { get; private set; }

        /// <summary>
        /// Gets the enumerated selections, in the projection document's own
        /// member order within each affordance kind.
        /// </summary>
        public ArrayOf<WotProjectionReference> References { get; private set; }

        /// <summary>
        /// Gets the <c>ua:Organizes</c> links, in authored order, that name the
        /// groups this view is shaped from.
        /// </summary>
        /// <remarks>
        /// See WoT Binding Section 12.7. Organizing is not selecting: these
        /// links carry through to the resolved view unchanged and their
        /// affordances stay in the groups that organize them.
        /// </remarks>
        public ArrayOf<WotOrganizingLink> OrganizingLinks { get; private set; }

        /// <summary>
        /// Determines whether a document is a projection document.
        /// </summary>
        /// <param name="document">The document to test.</param>
        /// <returns>
        /// <c>true</c> when the document carries <c>uav:projection</c> in its
        /// <c>@type</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static bool IsProjection(WotDocument document)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("@type", out JsonElement type) &&
                IsProjectionType(type);
        }

        /// <summary>
        /// Determines whether a JSON <c>@type</c> value carries the projection role.
        /// </summary>
        /// <param name="type">The string or array from the document's <c>@type</c> member.</param>
        /// <returns><c>true</c> when the value contains <c>uav:projection</c>.</returns>
        public static bool IsProjectionType(JsonElement type)
        {
            if (type.ValueKind == JsonValueKind.String)
            {
                return string.Equals(type.GetString(), WotVocabulary.ProjectionAnnotation, StringComparison.Ordinal);
            }
            if (type.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement token in type.EnumerateArray())
                {
                    if (token.ValueKind == JsonValueKind.String &&
                        string.Equals(token.GetString(), WotVocabulary.ProjectionAnnotation, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Reads the projection declared by a document, reporting every rule
        /// the document breaks.
        /// </summary>
        /// <param name="document">The projection document.</param>
        /// <param name="diagnostics">Receives the diagnostics.</param>
        /// <returns>
        /// The parsed projection, or <c>null</c> when the document is not a
        /// projection document or is too malformed to interpret.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> or <paramref name="diagnostics"/> is
        /// <c>null</c>.
        /// </exception>
        public static WotProjection? Parse(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            return Parse(document, diagnostics, WotProjectionCompatibilityMode.None);
        }

        /// <summary>
        /// Reads a projection plan with an explicitly selected compatibility mode.
        /// </summary>
        /// <param name="document">The projection document.</param>
        /// <param name="diagnostics">Receives plan validation diagnostics.</param>
        /// <param name="compatibilityMode">
        /// The permitted legacy plan syntax. Selecting compatibility does not relax current-plan validation.
        /// </param>
        /// <returns>
        /// The parsed plan, or <c>null</c> if the document has no projection role. Callers must reject
        /// error diagnostics before resolving or materializing the plan.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> or <paramref name="diagnostics"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="compatibilityMode"/> is not a defined mode.
        /// </exception>
        public static WotProjection? Parse(
            WotDocument document,
            List<WotDiagnostic> diagnostics,
            WotProjectionCompatibilityMode compatibilityMode)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (compatibilityMode is not (
                WotProjectionCompatibilityMode.None or WotProjectionCompatibilityMode.DraftProjection11))
            {
                throw new ArgumentOutOfRangeException(nameof(compatibilityMode));
            }
            if (!IsProjection(document))
            {
                return null;
            }

            return new WotProjection
            {
                ResultKind = ReadResultKind(document, compatibilityMode, diagnostics),
                Scenario = ReadScenario(document, diagnostics),
                Sources = ReadSources(document, diagnostics),
                References = ReadReferences(document, diagnostics),
                OrganizingLinks = ReadOrganizingLinks(document)
            };
        }

        private static WotDocumentKind ReadResultKind(
            WotDocument document, WotProjectionCompatibilityMode compatibilityMode, List<WotDiagnostic> diagnostics)
        {
            var tokens = new List<string>();
            if (!document.RootElement.TryGetProperty("@type", out JsonElement types) ||
                !TryAppendTypeTokens(types, tokens))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ProjectionManifestInvalid,
                    "A projection plan's @type must be a non-empty string or an array of non-empty strings.",
                    new WotLocation(reference: "@type")));
                return WotDocumentKind.Unknown;
            }
            foreach (string token in tokens)
            {
                if (WotNodeSetConverter.IsNodeClassAnnotation(token))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ProjectionManifestInvalid,
                        "A projection plan must not claim another OPC UA NodeClass role.",
                        new WotLocation(reference: "@type")));
                    return WotDocumentKind.Unknown;
                }
            }
            if (!document.TryGetUav("projectionKind", out JsonElement kind))
            {
                if (compatibilityMode == WotProjectionCompatibilityMode.DraftProjection11)
                {
                    bool td = false;
                    bool tm = false;
                    foreach (string token in tokens)
                    {
                        td |= token == "Thing";
                        tm |= token == "tm:ThingModel";
                    }
                    if (td != tm)
                    {
                        return tm ? WotDocumentKind.ThingModel : WotDocumentKind.ThingDescription;
                    }
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ProjectionManifestInvalid,
                    "A projection plan requires uav:projectionKind; DraftProjection1.1 requires " +
                    "explicit compatibility processing and exactly one old TD or TM marker.",
                    new WotLocation(reference: "uav:projectionKind")));
                return WotDocumentKind.Unknown;
            }
            WotDocumentKind result = kind.ValueKind == JsonValueKind.String
                ? kind.GetString() switch
                {
                    "ThingDescription" => WotDocumentKind.ThingDescription,
                    "ThingModel" => WotDocumentKind.ThingModel,
                    _ => WotDocumentKind.Unknown
                }
                : WotDocumentKind.Unknown;
            if (result == WotDocumentKind.Unknown)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.ProjectionManifestInvalid,
                    "uav:projectionKind must be ThingDescription or ThingModel.",
                    new WotLocation(reference: "uav:projectionKind")));
            }
            foreach (string token in tokens)
            {
                if (token is "Thing" or "tm:ThingModel")
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error, WotDiagnosticCode.ProjectionManifestInvalid,
                        "A projection plan must not claim an already resolved TD, TM, or OPC UA NodeClass role.",
                        new WotLocation(reference: "@type")));
                    break;
                }
            }
            return result;
        }

        private static string ReadScenario(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            if (!document.TryGetUav("scenario", out JsonElement scenario) ||
                scenario.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionScenarioMissing,
                    "A projection document shall declare uav:scenario as an " +
                    "absolute IRI naming the purpose the view serves."));
                return string.Empty;
            }
            string value = scenario.GetString() ?? string.Empty;
            if (!IsAbsoluteIri(value))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionScenarioMissing,
                    $"The uav:scenario '{value}' is not an absolute IRI.",
                    new WotLocation(reference: value)));
            }
            return value;
        }

        private static ArrayOf<WotProjectionManifestSource> ReadSources(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            if (!document.TryGetUav("projects", out JsonElement projects) ||
                projects.ValueKind != JsonValueKind.Array ||
                projects.GetArrayLength() == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionManifestInvalid,
                    "A projection document shall declare a non-empty " +
                    "uav:projects manifest naming the documents it projects."));
                return default;
            }

            var sources = new List<WotProjectionManifestSource>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in projects.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionManifestInvalid,
                        "Every uav:projects entry shall be an object."));
                    continue;
                }
                WotProjectionManifestSource? source = ReadSource(entry, diagnostics);
                if (source is null)
                {
                    continue;
                }
                if (!names.Add(source.SourceName))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionManifestInvalid,
                        $"The uav:sourceName '{source.SourceName}' is declared " +
                        "more than once; it shall be unique in the manifest.",
                        new WotLocation(reference: source.SourceName)));
                    continue;
                }
                sources.Add(source);
            }
            return new ArrayOf<WotProjectionManifestSource>([.. sources]);
        }

        private static WotProjectionManifestSource? ReadSource(
            JsonElement entry,
            List<WotDiagnostic> diagnostics)
        {
            string? sourceName = GetString(entry, "uav:sourceName");
            string? href = GetString(entry, "href");
            string? mediaType = GetString(entry, "type");
            if (string.IsNullOrEmpty(sourceName) ||
                string.IsNullOrEmpty(href) ||
                string.IsNullOrEmpty(mediaType))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionManifestInvalid,
                    "A uav:projects entry shall declare uav:sourceName, href " +
                    "and type."));
                return null;
            }
            if (!string.Equals(mediaType, ThingDescriptionMediaType, StringComparison.Ordinal) &&
                !string.Equals(mediaType, ThingModelMediaType, StringComparison.Ordinal) &&
                !string.Equals(mediaType, ContentType, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionManifestInvalid,
                    $"The source type '{mediaType}' shall be " +
                    $"'{ThingDescriptionMediaType}', '{ThingModelMediaType}', or '{ContentType}'.",
                    new WotLocation(reference: sourceName)));
                return null;
            }

            WotProjectionRouting routing = WotProjectionRouting.Source;
            string? routingValue = ReadOptionalString(entry, "uav:routing", sourceName!, diagnostics);
            if (routingValue is not null)
            {
                if (string.Equals(routingValue, "projection", StringComparison.Ordinal))
                {
                    routing = WotProjectionRouting.Projection;
                }
                else if (!string.Equals(routingValue, "source", StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionManifestInvalid,
                        $"The uav:routing '{routingValue}' shall be 'source' " +
                        "or 'projection'.",
                        new WotLocation(reference: sourceName)));
                }
            }

            string? digest = ReadOptionalString(entry, "uav:sourceDigest", sourceName!, diagnostics);
            if (digest is not null && !IsSha256Digest(digest))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionManifestInvalid,
                    $"The uav:sourceDigest '{digest}' shall have the form " +
                    "sha-256:<hex>.",
                    new WotLocation(reference: sourceName)));
            }

            bool selectAll = false;
            if (entry.TryGetProperty("uav:selectAll", out JsonElement selectAllValue))
            {
                if (selectAllValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    selectAll = selectAllValue.ValueKind == JsonValueKind.True;
                }
                else
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionSelectorInvalid,
                        "uav:selectAll shall be a boolean.",
                        new WotLocation(reference: sourceName)));
                }
            }

            return new WotProjectionManifestSource
            {
                SourceName = sourceName!,
                Href = href!,
                MediaType = mediaType!,
                Routing = routing,
                SourceDigest = digest,
                NamePrefix = ReadOptionalString(entry, "uav:namePrefix", sourceName!, diagnostics),
                SelectAll = selectAll,
                Filters = ReadFilters(entry, sourceName!, diagnostics)
            };
        }

        private static ArrayOf<WotProjectionFilter> ReadFilters(
            JsonElement entry,
            string sourceName,
            List<WotDiagnostic> diagnostics)
        {
            if (!entry.TryGetProperty("uav:select", out JsonElement select))
            {
                return default;
            }
            if (select.ValueKind != JsonValueKind.Array || select.GetArrayLength() == 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionSelectorInvalid,
                    "uav:select shall be a non-empty array of filter objects.",
                    new WotLocation(reference: sourceName)));
                return default;
            }

            var filters = new List<WotProjectionFilter>();
            foreach (JsonElement filter in select.EnumerateArray())
            {
                if (filter.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionSelectorInvalid,
                        "Every uav:select entry shall be an object.",
                        new WotLocation(reference: sourceName)));
                    continue;
                }
                WotProjectionFilter? parsed = ReadFilter(filter, sourceName, diagnostics);
                if (parsed is not null)
                {
                    filters.Add(parsed);
                }
            }
            return new ArrayOf<WotProjectionFilter>([.. filters]);
        }

        private static WotProjectionFilter? ReadFilter(
            JsonElement filter,
            string sourceName,
            List<WotDiagnostic> diagnostics)
        {
            WotAffordanceKind kind = WotAffordanceKind.Any;
            string? semanticId = null;
            var typeTokens = new List<string>();
            bool valid = true;
            bool hasPredicate = false;

            foreach (JsonProperty member in filter.EnumerateObject())
            {
                hasPredicate = true;
                switch (member.Name)
                {
                    case "uav:affordanceKind":
                        if (!TryReadAffordanceKind(member.Value, out kind))
                        {
                            valid = false;
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.ProjectionSelectorInvalid,
                                "uav:affordanceKind shall be 'property', " +
                                "'action' or 'event'.",
                                new WotLocation(reference: sourceName)));
                        }
                        break;
                    case "uav:semanticId":
                        if (member.Value.ValueKind == JsonValueKind.String &&
                            IsAbsoluteIri(member.Value.GetString()!))
                        {
                            semanticId = member.Value.GetString();
                        }
                        else
                        {
                            valid = false;
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.ProjectionSelectorInvalid,
                                "uav:semanticId in a filter shall be an absolute IRI string.",
                                new WotLocation(reference: sourceName)));
                        }
                        break;
                    case "@type":
                        if (!TryAppendTypeTokens(member.Value, typeTokens))
                        {
                            valid = false;
                            diagnostics.Add(new WotDiagnostic(
                                WotDiagnosticSeverity.Error,
                                WotDiagnosticCode.ProjectionSelectorInvalid,
                                "@type in a filter shall be a non-empty string or a non-empty array " +
                                "containing only non-empty strings.",
                                new WotLocation(reference: sourceName)));
                        }
                        break;
                    default:
                        // The predicate set is closed: a filter admits no key
                        // beyond the three above, so that it stays decidable by
                        // inspection.
                        valid = false;
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.ProjectionSelectorInvalid,
                            $"A uav:select filter shall not carry '{member.Name}'; " +
                            "only uav:affordanceKind, uav:semanticId and @type " +
                            "are admitted.",
                            new WotLocation(reference: sourceName)));
                        break;
                }
            }
            if (!hasPredicate)
            {
                valid = false;
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionSelectorInvalid,
                    "A uav:select filter shall declare at least one predicate.",
                    new WotLocation(reference: sourceName)));
            }

            return valid
                ? new WotProjectionFilter
                {
                    AffordanceKind = kind,
                    SemanticId = semanticId,
                    TypeTokens = new ArrayOf<string>([.. typeTokens])
                }
                : null;
        }

        private static ArrayOf<WotProjectionReference> ReadReferences(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            var references = new List<WotProjectionReference>();
            ReadReferences(
                document.Properties, WotAffordanceKind.Property, references, diagnostics);
            ReadReferences(
                document.Actions, WotAffordanceKind.Action, references, diagnostics);
            ReadReferences(
                document.Events, WotAffordanceKind.Event, references, diagnostics);
            return new ArrayOf<WotProjectionReference>([.. references]);
        }

        private static void ReadReferences(
            IReadOnlyDictionary<string, JsonElement> affordances,
            WotAffordanceKind kind,
            List<WotProjectionReference> references,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object ||
                    !affordance.Value.TryGetProperty("tm:ref", out JsonElement reference) ||
                    reference.ValueKind != JsonValueKind.String)
                {
                    // A projection document declares affordances, it does not
                    // define them, so every member shall carry tm:ref.
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ProjectionDefinesAffordance,
                        $"The projected affordance '{affordance.Key}' shall " +
                        "carry tm:ref; a projection document declares " +
                        "affordances rather than defining them.",
                        new WotLocation(reference: affordance.Key)));
                    continue;
                }
                references.Add(new WotProjectionReference
                {
                    AffordanceKind = kind,
                    Name = affordance.Key,
                    Reference = reference.GetString() ?? string.Empty,
                    Annotations = affordance.Value
                });
                ValidateAnnotations(affordance.Key, affordance.Value, diagnostics);
            }
        }

        /// <summary>
        /// Enforces the closed set of members a projection may annotate a
        /// declared affordance with (WoT Binding Section 12.5).
        /// </summary>
        /// <remarks>
        /// The set is closed for the same reason the <c>uav:select</c> filter
        /// set is: a projection declares affordances and does not define them,
        /// so anything it restates about the source's schema either repeats the
        /// source - in which case it says nothing - or contradicts it, in which
        /// case a consumer merging it would publish a view that disagrees with
        /// the Node it projects. Presentation, semantics and, where the source
        /// is routed through the projection, the transport binding are the only
        /// things an annotation may carry.
        /// </remarks>
        private static void ValidateAnnotations(
            string name,
            JsonElement affordance,
            List<WotDiagnostic> diagnostics)
        {
            foreach (JsonProperty member in affordance.EnumerateObject())
            {
                if (IsPermittedAnnotation(member.Name))
                {
                    continue;
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionAnnotationNotPermitted,
                    $"The projected affordance '{name}' shall not annotate " +
                    $"'{member.Name}'; a projection may restate only title, " +
                    "titles, description, descriptions, @type, uav:semanticId, " +
                    "uav:metadata and - under projection routing - forms and " +
                    "security.",
                    new WotLocation(reference: name)));
            }
        }

        /// <summary>
        /// Gets whether a member may appear beside <c>tm:ref</c> on a declared
        /// affordance.
        /// </summary>
        internal static bool IsPermittedAnnotation(string name)
        {
            return name is "tm:ref"
                or "title"
                or "titles"
                or "description"
                or "descriptions"
                or "@type"
                or "uav:semanticId"
                or "uav:metadata"
                or "forms"
                or "security";
        }

        private static ArrayOf<WotOrganizingLink> ReadOrganizingLinks(WotDocument document)
        {
            var links = new List<WotOrganizingLink>();
            foreach (JsonElement link in document.Links)
            {
                if (link.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string? rel = GetString(link, "rel");
                if (!string.Equals(rel, WotVocabulary.OrganizesRel, StringComparison.Ordinal))
                {
                    continue;
                }
                string? href = GetString(link, "href");
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }
                links.Add(new WotOrganizingLink
                {
                    Href = href!,
                    RefName = GetString(link, WotVocabulary.RefNameAnnotation) ?? string.Empty,
                    MediaType = GetString(link, "type") ?? string.Empty
                });
            }
            return new ArrayOf<WotOrganizingLink>([.. links]);
        }

        private static bool TryReadAffordanceKind(
            JsonElement value,
            out WotAffordanceKind kind)
        {
            kind = WotAffordanceKind.Any;
            if (value.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            switch (value.GetString())
            {
                case "property":
                    kind = WotAffordanceKind.Property;
                    return true;
                case "action":
                    kind = WotAffordanceKind.Action;
                    return true;
                case "event":
                    kind = WotAffordanceKind.Event;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryAppendTypeTokens(JsonElement value, List<string> tokens)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string? token = value.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    tokens.Add(token!);
                    return true;
                }
                return false;
            }
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                return false;
            }
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                {
                    return false;
                }
                tokens.Add(item.GetString()!);
            }
            return true;
        }

        private static string? GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? ReadOptionalString(
            JsonElement element,
            string name,
            string sourceName,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
            {
                return value.GetString();
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.ProjectionManifestInvalid,
                $"{name} shall be a non-empty string when present.",
                new WotLocation(reference: sourceName)));
            return null;
        }

        private static bool IsAbsoluteIri(string value)
        {
            int scheme = value.IndexOf(':', StringComparison.Ordinal);
            if (scheme <= 0)
            {
                return false;
            }
            for (int ii = 0; ii < scheme; ii++)
            {
                char c = value[ii];
                if (!char.IsLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                {
                    return false;
                }
            }
            return char.IsLetter(value[0]);
        }

        private static bool IsSha256Digest(string value)
        {
            const string prefix = "sha-256:";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            int digits = value.Length - prefix.Length;
            if (digits != 64)
            {
                return false;
            }
            for (int ii = prefix.Length; ii < value.Length; ii++)
            {
                if (!Uri.IsHexDigit(value[ii]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The registry Format identifier for a current projection plan.
        /// </summary>
        public const string Format = "WoT-Projection/1.2";

        /// <summary>
        /// The registry ContentType and source-manifest media type for a current projection plan.
        /// </summary>
        public const string ContentType =
            "application/ld+json; profile=\"http://opcfoundation.org/UA/WoT-Binding/v1.2/projection\"";

        private const string ThingDescriptionMediaType = "application/td+json";
        private const string ThingModelMediaType = "application/tm+json";
    }
}
