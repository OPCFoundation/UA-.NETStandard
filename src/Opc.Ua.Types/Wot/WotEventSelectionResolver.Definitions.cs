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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolution of the EventType definitions a <c>tm:ref</c> names
    /// (WoT Binding Section 6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reference names a definition in one of three shapes: a document
    /// location with an optional RFC 6901 JSON Pointer, the logical identifier
    /// of a document whose root <em>is</em> an EventType Thing Model, or the
    /// logical identifier a nested event affordance carries in <c>@id</c>. The
    /// logical shapes exist because a definition's identity outlives the file
    /// it happens to sit in: a document set that is re-arranged, mirrored or
    /// bundled keeps the identifiers and loses the paths.
    /// </para>
    /// <para>
    /// A logical identifier is a JSON-LD term, so a compact IRI is expanded in
    /// the active context of the node that <em>wrote</em> it. The same short
    /// form written in two documents that bind the prefix differently names two
    /// different definitions, and expanding in the referring document is what
    /// keeps them apart.
    /// </para>
    /// <para>
    /// Sources are consulted in a fixed order: the documents held together with
    /// the referring one, then each configured resolver in the order it was
    /// given, then the small well-known catalog this library carries. The order
    /// is total and the outcome of each stage is a set rather than a first
    /// match, so a reference that two sources answer differently is an error
    /// and never a race. The built-in catalog is last for the same reason: a
    /// definition this library carries must never shadow the one an author
    /// actually shipped.
    /// </para>
    /// </remarks>
    public sealed partial class WotEventSelectionResolver
    {
        /// <summary>
        /// The JSON-LD member that carries a node's logical identifier.
        /// </summary>
        private const string LogicalIdMember = "@id";

        /// <summary>
        /// The member that carries a Thing Description's logical identifier.
        /// </summary>
        /// <remarks>
        /// A Thing Description states <c>id</c> where a Thing Model states
        /// <c>@id</c>; both are the same fact for a document whose root is an
        /// EventType definition, so both are indexed.
        /// </remarks>
        private const string ThingIdMember = "id";

        /// <summary>
        /// One definition a document declares, and where it was found.
        /// </summary>
        private readonly struct DefinitionCandidate
        {
            public DefinitionCandidate(string origin, string pointer, JsonElement element)
            {
                Origin = origin;
                Pointer = pointer;
                Element = element;
            }

            /// <summary>
            /// The document URI the definition came from, empty for the
            /// document being resolved.
            /// </summary>
            public string Origin { get; }

            /// <summary>
            /// The RFC 6901 JSON Pointer of the definition inside that
            /// document, empty for its root.
            /// </summary>
            public string Pointer { get; }

            /// <summary>
            /// The definition object.
            /// </summary>
            public JsonElement Element { get; }

            /// <summary>
            /// The reference a diagnostic names this candidate by: the
            /// document it came from and the pointer into it, either of which
            /// is empty for the document being resolved and for its root.
            /// </summary>
            public string Describe()
            {
                return Origin + "#" + Pointer;
            }
        }

        /// <summary>
        /// Indexes every definition a document declares under the logical
        /// identifiers it can be named by.
        /// </summary>
        /// <remarks>
        /// Only two shapes are definitions. A root whose <c>@type</c> carries
        /// <c>uav:eventType</c> is the EventType Thing Model of a whole
        /// document, and an entry of <c>events</c> that carries <c>@id</c> is a
        /// definition another document may name. An affordance that carries
        /// only <c>uav:id</c> is not: <c>uav:id</c> identifies the OPC UA Node
        /// the affordance projects, which every event affordance has, and
        /// treating it as a definition identifier would make every event in
        /// every document a globally addressable definition.
        /// </remarks>
        private static void IndexDefinitions(
            WotDocument document,
            string origin,
            Dictionary<string, List<DefinitionCandidate>> index)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            if (CarriesEventTypeAnnotation(root))
            {
                AddCandidate(
                    document,
                    ReadLogicalId(root),
                    new DefinitionCandidate(origin, string.Empty, root),
                    index);
            }
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string? logicalId = ReadNestedLogicalId(affordance.Value);
                if (logicalId is null)
                {
                    continue;
                }
                AddCandidate(
                    document,
                    logicalId,
                    new DefinitionCandidate(
                        origin,
                        "/events/" + EscapePointerToken(affordance.Key),
                        affordance.Value),
                    index);
            }
        }

        /// <summary>
        /// Reads the logical identifier of a document root.
        /// </summary>
        private static string? ReadLogicalId(JsonElement root)
        {
            if (root.TryGetProperty(LogicalIdMember, out JsonElement id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(id.GetString()))
            {
                return id.GetString();
            }
            if (root.TryGetProperty(ThingIdMember, out JsonElement thingId) &&
                thingId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(thingId.GetString()))
            {
                return thingId.GetString();
            }
            return null;
        }

        /// <summary>
        /// Reads the logical identifier a nested event affordance carries.
        /// </summary>
        private static string? ReadNestedLogicalId(JsonElement affordance)
        {
            return affordance.TryGetProperty(LogicalIdMember, out JsonElement id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(id.GetString())
                    ? id.GetString()
                    : null;
        }

        private static void AddCandidate(
            WotDocument document,
            string? logicalId,
            DefinitionCandidate candidate,
            Dictionary<string, List<DefinitionCandidate>> index)
        {
            if (string.IsNullOrEmpty(logicalId))
            {
                return;
            }
            // A definition is indexed under the expansion of the identifier it
            // declares, in its own document's context. Indexing the written
            // form as well would let a document that binds 'evt:' to something
            // else reach this definition by writing the same short form, which
            // is precisely what expanding in the writer's own context prevents.
            // A declared '@id' that is not an identifier at all - a bare name -
            // indexes nothing, because nothing decides what it names.
            if (TryExpandLogicalId(document, logicalId!, out string expanded))
            {
                AddUnique(index, expanded, candidate);
            }
        }

        private static void AddUnique(
            Dictionary<string, List<DefinitionCandidate>> index,
            string key,
            DefinitionCandidate candidate)
        {
            if (!index.TryGetValue(key, out List<DefinitionCandidate>? candidates))
            {
                candidates = [];
                index[key] = candidates;
            }
            for (int ii = 0; ii < candidates.Count; ii++)
            {
                if (string.Equals(
                        candidates[ii].Origin, candidate.Origin, StringComparison.Ordinal) &&
                    string.Equals(
                        candidates[ii].Pointer, candidate.Pointer, StringComparison.Ordinal))
                {
                    return;
                }
            }
            candidates.Add(candidate);
        }

        /// <summary>
        /// Expands a logical identifier through the active context of the node
        /// that wrote it, or reports that the value is not an identifier at
        /// all.
        /// </summary>
        /// <remarks>
        /// Three shapes are not identifiers, and saying so is the point.
        /// A bare name has no scheme and no bound prefix, so nothing decides
        /// what it names; a value containing whitespace is not an IRI; and the
        /// empty string names nothing. A prefix the context does <em>not</em>
        /// bind is read as an absolute IRI, exactly as JSON-LD reads it - the
        /// two cannot be told apart by spelling, and pretending otherwise would
        /// make the same document mean different things to different readers.
        /// </remarks>
        internal static bool TryExpandLogicalId(
            WotDocument document, string value, out string expanded)
        {
            expanded = string.Empty;
            if (value.Length == 0)
            {
                return false;
            }
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (char.IsWhiteSpace(value[ii]))
                {
                    return false;
                }
            }
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon + 1 >= value.Length)
            {
                return false;
            }
            if (value[colon + 1] == '/')
            {
                // JSON-LD does not read 'scheme://...' as a compact IRI, so a
                // context that happens to bind the scheme cannot rewrite it.
                expanded = value;
                return true;
            }
            string prefix = value.Substring(0, colon);
            string? namespaceUri = ResolvePrefix(document, prefix);
            if (namespaceUri is null)
            {
                expanded = value;
                return true;
            }
            string local = value.Substring(colon + 1);
            expanded = namespaceUri + local;
            return true;
        }

        /// <summary>
        /// Loads and indexes every document a reference in this document names
        /// by location, so a logical identifier can be resolved against the
        /// documents held together with this one.
        /// </summary>
        /// <remarks>
        /// A failure here is not reported: a location that does not resolve is
        /// only an error when something actually needs it, and reporting it
        /// during indexing would turn an unused reference into a rejected
        /// document.
        /// </remarks>
        private async ValueTask BuildDefinitionIndexAsync(
            WotDocument document,
            ResolutionScope scope,
            CancellationToken cancellationToken)
        {
            IndexDefinitions(document, string.Empty, scope.Definitions);

            var locations = new List<string>();
            CollectReferencedLocations(document, locations);
            foreach (string location in locations)
            {
                if (!WotEventSelectClauses.TrySplitEventTypeReference(
                        location, out string documentUri, out _) ||
                    documentUri.Length == 0)
                {
                    continue;
                }
                WotDocument? sibling = await LoadAsync(
                        documentUri, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (sibling is not null)
                {
                    IndexDefinitions(sibling, documentUri, scope.Definitions);
                }
            }
        }

        /// <summary>
        /// Collects every reference in a document that names a document
        /// location rather than a logical identifier.
        /// </summary>
        private static void CollectReferencedLocations(
            WotDocument document, List<string> locations)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                AddLocation(affordance.Value, locations);
                if (!affordance.Value.TryGetProperty(
                        WotEventSelectClauses.Term, out JsonElement clauses) ||
                    clauses.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (JsonElement clause in clauses.EnumerateArray())
                {
                    if (clause.ValueKind == JsonValueKind.Object)
                    {
                        AddLocation(clause, locations);
                    }
                }
            }
        }

        private static void AddLocation(JsonElement node, List<string> locations)
        {
            if (!node.TryGetProperty(
                    WotEventSelectClauses.TypeDefinitionReferenceTerm,
                    out JsonElement reference) ||
                reference.ValueKind != JsonValueKind.String)
            {
                return;
            }
            string value = reference.GetString()!;
            if (!NamesDocumentLocation(value) || locations.Contains(value))
            {
                return;
            }
            locations.Add(value);
        }

        /// <summary>
        /// Gets whether a reference names a document location.
        /// </summary>
        /// <remarks>
        /// A location has a path: it carries a JSON Pointer fragment, contains
        /// a path separator, or ends in a document suffix. A bare compact IRI
        /// such as <c>evt:highTemperatureAlarm</c> has none of these and names
        /// a definition by identity, which is exactly the distinction that
        /// keeps this resolver from trying to fetch an identifier as if it were
        /// a file.
        /// </remarks>
        private static bool NamesDocumentLocation(string reference)
        {
            if (reference.Length == 0)
            {
                return false;
            }
            if (reference.Contains('#', StringComparison.Ordinal) ||
                reference.Contains('/', StringComparison.Ordinal))
            {
                return true;
            }
            foreach (string suffix in s_documentSuffixes)
            {
                if (reference.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The document suffixes that make a reference without a path
        /// separator a location all the same.
        /// </summary>
        private static readonly string[] s_documentSuffixes =
        [
            ".jsonld",
            ".json"
        ];

        /// <summary>
        /// Resolves one reference to the definition it names, consulting the
        /// held documents, then the configured resolvers, then the well-known
        /// catalog.
        /// </summary>
        private async ValueTask<JsonElement?> ResolveReferenceTargetAsync(
            WotDocument document,
            string reference,
            string where,
            ResolutionScope scope,
            CancellationToken cancellationToken)
        {
            List<WotDiagnostic> diagnostics = scope.Diagnostics;

            // Held documents, by logical identifier, expanded in the active
            // context of the node that wrote the reference.
            if (TryLookupHeld(
                    document, reference, where, scope, out JsonElement held, out bool ambiguous))
            {
                return held;
            }
            if (ambiguous)
            {
                return null;
            }

            // Held documents and configured resolvers, by location. The attempt
            // is made for any reference that splits, because a document set may
            // name its documents with bare tokens; the failure it produces is
            // held back until the well-known catalog has also been consulted,
            // so a well-known identifier is not reported as a missing file.
            bool located = false;
            JsonElement target = default;
            string? unresolvedDocument = null;
            bool pointerMissed = false;
            if (WotEventSelectClauses.TrySplitEventTypeReference(
                reference, out string documentUri, out string pointer))
            {
                // TrySplitEventTypeReference rejects a fragment-only reference,
                // so a reference that splits always names a document.
                WotDocument? resolved = await LoadAsync(
                        documentUri, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (resolved is null)
                {
                    unresolvedDocument = documentUri;
                }
                else
                {
                    // A resolver that answers a logical identifier hands back a
                    // whole document, whose root may not be the definition at
                    // all. Indexing it and asking the identifier again is what
                    // makes the answer verifiable: the definition that comes
                    // back is the one that declares the identifier that was
                    // asked for, rather than whatever the resolver returned.
                    IndexDefinitions(resolved, documentUri, scope.Definitions);
                    if (TryLookupHeld(
                            document,
                            reference,
                            where,
                            scope,
                            out JsonElement identified,
                            out bool nowAmbiguous))
                    {
                        return identified;
                    }
                    if (nowAmbiguous)
                    {
                        return null;
                    }
                    if (WotDocument.TryEvaluatePointer(
                            resolved.RootElement, pointer, out target) &&
                        target.ValueKind == JsonValueKind.Object)
                    {
                        located = true;
                    }
                    else
                    {
                        pointerMissed = true;
                    }
                }
            }
            else if (NamesDocumentLocation(reference))
            {
                AddError(
                    diagnostics,
                    $"The EventType reference '{reference}' is not a document URI with an " +
                    "optional RFC 6901 JSON Pointer (WoT Binding Section 6.1).",
                    where);
                return null;
            }
            if (located)
            {
                return target;
            }

            // The well-known catalog, last: a definition this library carries
            // shall never shadow one an author shipped.
            if (TryLookupWellKnown(document, reference, out JsonElement builtIn))
            {
                return builtIn;
            }

            if (pointerMissed)
            {
                AddError(
                    diagnostics,
                    $"The EventType reference '{reference}' does not resolve to a " +
                    "definition of the document it names (WoT Binding Section 6.1).",
                    where);
                return null;
            }
            if (unresolvedDocument is not null && NamesDocumentLocation(reference))
            {
                // Reported once per call: the second affordance that names the
                // same missing document is already failed by the first report,
                // and restating it would turn one fault into as many
                // diagnostics as there are readers.
                if (scope.TryMarkReported(unresolvedDocument))
                {
                    AddError(
                        diagnostics,
                        $"The EventType reference to '{unresolvedDocument}' does not " +
                        "resolve in the local document set; such a reference is " +
                        "resolved against the documents held together with this one " +
                        "and is never dereferenced over the network " +
                        "(WoT Binding Sections 5.1.5 and 6.1).",
                        where);
                }
                return null;
            }

            AddError(
                diagnostics,
                $"The EventType reference '{reference}' does not resolve to a definition. A " +
                "logical identifier names a definition the documents held together with this " +
                "one declare, or one of the well-known base types; it is never dereferenced " +
                "over the network (WoT Binding Sections 5.1.5 and 6.1).",
                where);
            return null;
        }

        /// <summary>
        /// Looks a reference up among the definitions the held documents
        /// declare.
        /// </summary>
        private static bool TryLookupHeld(
            WotDocument document,
            string reference,
            string where,
            ResolutionScope scope,
            out JsonElement definition,
            out bool ambiguous)
        {
            definition = default;
            ambiguous = false;

            var matches = new List<DefinitionCandidate>();
            if (TryExpandLogicalId(document, reference, out string expanded))
            {
                Collect(scope.Definitions, expanded, matches);
            }
            if (matches.Count == 0)
            {
                return false;
            }
            if (matches.Count > 1)
            {
                AddError(
                    scope.Diagnostics,
                    $"The EventType reference '{reference}' names {matches.Count} different " +
                    "definitions among the documents held together with this one (" +
                    string.Join(", ", Describe(matches)) +
                    "); a logical identifier names exactly one definition " +
                    "(WoT Binding Section 6.1).",
                    where);
                ambiguous = true;
                return false;
            }
            definition = matches[0].Element;
            return true;
        }

        /// <summary>
        /// Adds every candidate a key names to the match set.
        /// </summary>
        private static void Collect(
            Dictionary<string, List<DefinitionCandidate>> index,
            string key,
            List<DefinitionCandidate> matches)
        {
            if (index.TryGetValue(key, out List<DefinitionCandidate>? candidates))
            {
                matches.AddRange(candidates);
            }
        }

        private static IEnumerable<string> Describe(List<DefinitionCandidate> matches)
        {
            foreach (DefinitionCandidate candidate in matches)
            {
                yield return "'" + candidate.Describe() + "'";
            }
        }

        /// <summary>
        /// Looks a reference up in the small catalog of definitions this
        /// library carries for the OPC UA base types.
        /// </summary>
        private static bool TryLookupWellKnown(
            WotDocument document, string reference, out JsonElement definition)
        {
            definition = default;
            // The raw form is compared as well as the expansion: two of the
            // aliases are ExpandedNodeId spellings, which are not identifiers
            // at all and expand to nothing.
            TryExpandLogicalId(document, reference, out string expanded);
            foreach (string alias in s_baseEventTypeAliases)
            {
                if (string.Equals(reference, alias, StringComparison.Ordinal) ||
                    string.Equals(expanded, alias, StringComparison.Ordinal))
                {
                    definition = BaseEventTypeDefinition;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The identifiers the well-known <c>BaseEventType</c> definition
        /// answers to.
        /// </summary>
        /// <remarks>
        /// The set is closed and pinned to the type's OPC 10000-5 identity: a
        /// version-independent BrowseName, the ExpandedNodeId in both of its
        /// written forms, and the IRI the base namespace mints for the type.
        /// Nothing here is derived at run time, so no document can add an alias
        /// and no alias can drift.
        /// </remarks>
        private static readonly string[] s_baseEventTypeAliases =
        [
            "ua:BaseEventType",
            "http://opcfoundation.org/UA/BaseEventType",
            WotEventSelectClauses.BaseEventTypeId,
            "nsu=http://opcfoundation.org/UA/;" + WotEventSelectClauses.BaseEventTypeId
        ];

        /// <summary>
        /// The <c>BaseEventType</c> definition this library carries
        /// (OPC 10000-5, WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// The definition declares the eight mandatory fields <em>and</em>
        /// <c>LocalTime</c>, which OPC 10000-5 declares on the type as
        /// optional. The two are not the same set on purpose: a definition
        /// states what an EventType <em>has</em>, while the implicit selection
        /// an affordance falls back to states what a consumer subscribes to
        /// when the document says nothing, and OPC 10000-5 makes only the eight
        /// mandatory. So a document that names this definition may select
        /// <c>LocalTime</c>, and a document that names nothing still gets the
        /// eight.
        /// </remarks>
        private static JsonElement BaseEventTypeDefinition => s_baseEventType.RootElement;

        private static readonly JsonDocument s_baseEventType = JsonDocument.Parse(
            """
            {
              "@id": "ua:BaseEventType",
              "@type": "uav:eventType",
              "title": "BaseEventType",
              "uav:id": "i=2041",
              "uav:browseName": "BaseEventType",
              "data": {
                "type": "object",
                "uav:fieldOrder": [
                  "EventId",
                  "EventType",
                  "SourceNode",
                  "SourceName",
                  "Time",
                  "ReceiveTime",
                  "LocalTime",
                  "Message",
                  "Severity"
                ],
                "required": [
                  "EventId",
                  "EventType",
                  "SourceNode",
                  "SourceName",
                  "Time",
                  "ReceiveTime",
                  "Message",
                  "Severity"
                ],
                "properties": {
                  "EventId": { "type": "string", "contentEncoding": "base64" },
                  "EventType": { "type": "string" },
                  "SourceNode": { "type": "string" },
                  "SourceName": { "type": "string" },
                  "Time": { "type": "string", "format": "date-time" },
                  "ReceiveTime": { "type": "string", "format": "date-time" },
                  "LocalTime": { "type": "object" },
                  "Message": { "type": "string" },
                  "Severity": { "type": "integer", "minimum": 1, "maximum": 1000 }
                }
              }
            }
            """);
    }
}
