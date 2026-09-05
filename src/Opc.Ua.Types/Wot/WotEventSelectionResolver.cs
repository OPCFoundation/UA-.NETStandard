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
    /// The resolved event field selections of one WoT document, keyed by event
    /// affordance name (WoT Binding Section 6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An affordance appears here exactly when it <em>states</em> a selection —
    /// a <c>tm:ref</c> to an EventType definition, an explicit
    /// <c>uav:eventSelectClauses</c> array, or both. An affordance that states
    /// neither takes the implicit <c>BaseEventType</c> default of
    /// <see cref="WotEventSelectClauses.Default"/>, which needs no resolution
    /// and is therefore not carried.
    /// </para>
    /// <para>
    /// The catalog is what a synchronous consumer reads instead of resolving:
    /// resolution follows document links and is asynchronous, so it happens
    /// once, before planning, and what planning sees is this immutable result.
    /// </para>
    /// </remarks>
    public sealed class WotEventSelectionCatalog
    {
        /// <summary>
        /// An empty catalog: no affordance states a resolvable selection.
        /// </summary>
        public static WotEventSelectionCatalog Empty { get; } =
            new WotEventSelectionCatalog(
                new Dictionary<string, Entry>(StringComparer.Ordinal));

        /// <summary>
        /// Gets the names of the affordances the catalog resolved, in ordinal
        /// order.
        /// </summary>
        public ArrayOf<string> AffordanceNames
        {
            get
            {
                var names = new string[m_selections.Count];
                m_selections.Keys.CopyTo(names, 0);
                Array.Sort(names, StringComparer.Ordinal);
                return names;
            }
        }

        /// <summary>
        /// Gets whether the catalog resolved nothing.
        /// </summary>
        public bool IsEmpty => m_selections.Count == 0;

        /// <summary>
        /// Builds a catalog from resolved selections.
        /// </summary>
        /// <param name="selections">The selections, keyed by affordance name.</param>
        /// <returns>The immutable catalog.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="selections"/> is <c>null</c>.
        /// </exception>
        public static WotEventSelectionCatalog Create(
            IEnumerable<KeyValuePair<string, ArrayOf<WotResolvedEventSelectClause>>> selections)
        {
            if (selections is null)
            {
                throw new ArgumentNullException(nameof(selections));
            }
            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ArrayOf<WotResolvedEventSelectClause>> entry in selections)
            {
                entries[entry.Key] = new Entry(entry.Value, default);
            }
            return entries.Count == 0 ? Empty : new WotEventSelectionCatalog(entries);
        }

        /// <summary>
        /// Gets the resolved selection of one event affordance.
        /// </summary>
        /// <param name="affordanceName">The event affordance name.</param>
        /// <param name="clauses">The ordered resolved clauses.</param>
        /// <returns><c>true</c> when the affordance stated a resolved selection.</returns>
        public bool TryGetSelection(
            string affordanceName, out ArrayOf<WotResolvedEventSelectClause> clauses)
        {
            if (affordanceName is not null &&
                m_selections.TryGetValue(affordanceName, out Entry found))
            {
                clauses = found.Clauses;
                return true;
            }
            clauses = ArrayOf<WotResolvedEventSelectClause>.Empty;
            return false;
        }

        /// <summary>
        /// Gets the <c>data</c> DataSchema of the EventType definition an
        /// affordance links to, where the affordance itself declares none.
        /// </summary>
        /// <remarks>
        /// The linked definition is the affordance's <em>effective</em> schema
        /// (WoT Binding Section 6.1), so a consumer that materializes the
        /// EventType's fields reads it rather than inventing a field set. It is
        /// carried as the raw UTF-8 JSON of the schema because the document it
        /// came from is closed once resolution finishes.
        /// </remarks>
        /// <param name="affordanceName">The event affordance name.</param>
        /// <param name="utf8Json">The linked <c>data</c> schema.</param>
        /// <returns><c>true</c> when a linked schema was carried.</returns>
        public bool TryGetLinkedData(string affordanceName, out ReadOnlyMemory<byte> utf8Json)
        {
            if (affordanceName is not null &&
                m_selections.TryGetValue(affordanceName, out Entry found) &&
                !found.LinkedData.IsEmpty)
            {
                utf8Json = found.LinkedData;
                return true;
            }
            utf8Json = default;
            return false;
        }

        internal static WotEventSelectionCatalog Create(Dictionary<string, Entry> entries)
        {
            return entries.Count == 0 ? Empty : new WotEventSelectionCatalog(entries);
        }

        private WotEventSelectionCatalog(Dictionary<string, Entry> selections)
        {
            m_selections = selections;
        }

        /// <summary>
        /// One affordance's resolved selection and, where the affordance
        /// declared no <c>data</c> of its own, the linked definition's schema.
        /// </summary>
        internal readonly struct Entry
        {
            public Entry(
                ArrayOf<WotResolvedEventSelectClause> clauses, ReadOnlyMemory<byte> linkedData)
            {
                Clauses = clauses;
                LinkedData = linkedData;
            }

            public ArrayOf<WotResolvedEventSelectClause> Clauses { get; }

            public ReadOnlyMemory<byte> LinkedData { get; }
        }

        private readonly Dictionary<string, Entry> m_selections;
    }

    /// <summary>
    /// Resolves the EventType definitions a document's event affordances link
    /// to with <c>tm:ref</c>, derives the baseline select clauses from them,
    /// overlays the explicit <c>uav:eventSelectClauses</c> entries and returns
    /// the final ordered selection of every affordance
    /// (WoT Binding Section 6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolution is local: a reference is resolved through the sibling
    /// documents the caller's <see cref="IWotThingResolver"/> holds, and this
    /// class never dereferences a URI over the network, because a description
    /// that fetched a URI while it was being read would mean whatever a host
    /// served at the moment it was read. Depth, document count and byte budgets
    /// come from the shared <see cref="WotResolutionContext"/>, so an EventType
    /// chain is bounded by the same limits every other resolution in this
    /// library is.
    /// </para>
    /// <para>
    /// Derivation is total: a definition this class cannot walk — a
    /// <c>data</c> that is not an object, a walked object with no
    /// <c>uav:fieldOrder</c>, a member name that is neither a legal unqualified
    /// BrowseName nor annotated with <c>uav:browseName</c> — is reported and no
    /// selection is produced for that affordance, because a partial selection
    /// is a subscription that silently omits fields.
    /// </para>
    /// <para>
    /// An instance holds nothing a call leaves behind: the documents a
    /// reference opens, the references that failed to open and the linked
    /// schemas carried out of them all live for exactly one
    /// <see cref="ResolveAsync"/>, which is what lets one instance resolve
    /// document after document — and resolve several at once, each with its own
    /// <see cref="WotResolutionContext"/> — instead of handing the second
    /// caller a document the first one closed.
    /// </para>
    /// </remarks>
    public sealed partial class WotEventSelectionResolver
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="WotEventSelectionResolver"/> class.
        /// </summary>
        /// <param name="thingResolver">
        /// The resolver used to obtain the documents an EventType reference
        /// names. Use <see cref="NullWotResolver.Instance"/> for an explicit
        /// "no external resolution" policy.
        /// </param>
        /// <param name="options">
        /// The bounded conversion options, or <c>null</c> to use the defaults.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="thingResolver"/> is <c>null</c>.
        /// </exception>
        public WotEventSelectionResolver(
            IWotThingResolver thingResolver,
            WotNodeSetConverterOptions? options = null)
        {
            m_thingResolver = thingResolver ??
                throw new ArgumentNullException(nameof(thingResolver));
            m_options = options ?? new WotNodeSetConverterOptions();
            m_options.Validate();
        }

        /// <summary>
        /// Gets whether an event affordance states a selection that has to be
        /// resolved before it can be planned (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// The answer is a fact about the document alone, so a synchronous
        /// caller can ask it without performing any I/O: it is what lets a
        /// synchronous planner accept the implicit <c>BaseEventType</c> default
        /// and reject an unresolved link instead of quietly resolving one.
        /// </remarks>
        /// <param name="affordance">The event affordance object.</param>
        /// <returns><c>true</c> when the affordance states a selection.</returns>
        public static bool StatesSelection(JsonElement affordance)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                (affordance.TryGetProperty(
                        WotEventSelectClauses.TypeDefinitionReferenceTerm, out _) ||
                    affordance.TryGetProperty(WotEventSelectClauses.Term, out _));
        }

        /// <summary>
        /// Resolves every event affordance selection a document states.
        /// </summary>
        /// <remarks>
        /// Every document opened, every reference that failed to open and every
        /// linked schema carried out belongs to this call alone, so the same
        /// instance may resolve one document after another, and may resolve
        /// several concurrently where each call is given its own
        /// <paramref name="resolutionContext"/>.
        /// </remarks>
        /// <param name="document">The document.</param>
        /// <param name="resolutionContext">
        /// The resolution context that bounds the work, or <c>null</c> to
        /// create one from the configured options.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// A result carrying the catalog together with the diagnostics this
        /// call produced. The value is <c>null</c> when this call reported an
        /// error.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public async System.Threading.Tasks.ValueTask<WotConversionResult<WotEventSelectionCatalog>>
            ResolveAsync(
                WotDocument document,
                WotResolutionContext? resolutionContext = null,
                System.Threading.CancellationToken cancellationToken = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var selections = new Dictionary<string, WotEventSelectionCatalog.Entry>(
                StringComparer.Ordinal);
            using var scope = new ResolutionScope(
                resolutionContext ??
                    new WotResolutionContext(m_options.ToResolverOptions()));
            await BuildDefinitionIndexAsync(document, scope, cancellationToken)
                .ConfigureAwait(false);
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (!StatesSelection(affordance.Value))
                {
                    continue;
                }
                ArrayOf<WotResolvedEventSelectClause> resolved = await ResolveAffordanceAsync(
                        document,
                        affordance.Key,
                        affordance.Value,
                        scope,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!resolved.IsNull && resolved.Count > 0)
                {
                    selections[affordance.Key] = new WotEventSelectionCatalog.Entry(
                        resolved, scope.TakeLinkedData(affordance.Key));
                }
            }

            // Only what this call reported decides this call's outcome. The
            // context accumulates the diagnostics of every phase that shares
            // it, so reading it here would both re-emit what another phase has
            // already surfaced and fail an otherwise clean selection over an
            // error that has nothing to do with it. Every limit the context
            // blocks on is returned by TryEnter and TryAddBytes and is recorded
            // locally where it happens.
            bool failed = false;
            List<WotDiagnostic> diagnostics = scope.Diagnostics;
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    failed = true;
                    break;
                }
            }
            return new WotConversionResult<WotEventSelectionCatalog>(
                failed ? null : WotEventSelectionCatalog.Create(selections), diagnostics);
        }

        private async System.Threading.Tasks.ValueTask<ArrayOf<WotResolvedEventSelectClause>>
            ResolveAffordanceAsync(
                WotDocument document,
                string affordanceName,
                JsonElement affordance,
                ResolutionScope scope,
                System.Threading.CancellationToken cancellationToken)
        {
            List<WotDiagnostic> diagnostics = scope.Diagnostics;
            string where = "/events/" + EscapePointerToken(affordanceName);
            int errorsBefore = CountErrors(diagnostics);

            var baseline = new List<WotResolvedEventSelectClause>();
            JsonElement effectiveData = default;
            bool hasEffectiveData = affordance.TryGetProperty(DataMember, out effectiveData) &&
                effectiveData.ValueKind == JsonValueKind.Object;

            if (affordance.TryGetProperty(
                    WotEventSelectClauses.TypeDefinitionReferenceTerm, out JsonElement reference))
            {
                if (reference.ValueKind != JsonValueKind.String)
                {
                    AddError(
                        diagnostics,
                        $"The {WotEventSelectClauses.TypeDefinitionReferenceTerm} of an event " +
                        "affordance shall be a document URI with an optional RFC 6901 JSON " +
                        "Pointer (WoT Binding Section 6.1).",
                        where + "/" + WotEventSelectClauses.TypeDefinitionReferenceTerm);
                    return ArrayOf<WotResolvedEventSelectClause>.Empty;
                }
                string link = reference.GetString() ?? string.Empty;
                EventTypeDefinition? definition = await ResolveDefinitionAsync(
                        document, link, where, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (definition is null)
                {
                    return ArrayOf<WotResolvedEventSelectClause>.Empty;
                }
                if (!hasEffectiveData)
                {
                    effectiveData = definition.Data;
                    hasEffectiveData = true;
                    scope.SetLinkedData(
                        affordanceName,
                        System.Text.Encoding.UTF8.GetBytes(definition.Data.GetRawText()));
                }
                if (!TryDeriveBaseline(document, definition, where, diagnostics, baseline))
                {
                    return ArrayOf<WotResolvedEventSelectClause>.Empty;
                }
            }

            // No affordance-level tm:ref: the clauses the affordance writes are
            // the complete selection, not an overlay on the implicit default.
            // The implicit eight-field default is what an affordance that
            // states nothing at all falls back to, and StatesSelection keeps
            // that case out of here entirely. Seeding it as a baseline would
            // silently subscribe to eight fields the author did not ask for -
            // and would make a document that deliberately selects one field
            // return nine.

            var explicitClauses = new List<WotResolvedEventSelectClause>();
            if (affordance.TryGetProperty(WotEventSelectClauses.Term, out JsonElement authored))
            {
                if (!WotEventSelectClauses.TryParse(
                    authored,
                    prefix => ResolvePrefix(document, prefix),
                    out ArrayOf<WotEventSelectClause> parsed,
                    out string parseError,
                    out int parseIndex))
                {
                    AddError(
                        diagnostics,
                        parseError,
                        parseIndex < 0
                            ? where + "/" + WotEventSelectClauses.Term
                            : where + "/" + WotEventSelectClauses.Term + "/" + Index(parseIndex));
                    return ArrayOf<WotResolvedEventSelectClause>.Empty;
                }
                for (int ii = 0; ii < parsed.Count; ii++)
                {
                    WotEventSelectClause clause = parsed[ii];
                    string at = where + "/" + WotEventSelectClauses.Term + "/" + Index(ii);
                    EventTypeDefinition? target = await ResolveDefinitionAsync(
                            document,
                            clause.TypeDefinitionReference,
                            at,
                            scope,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (target is null)
                    {
                        return ArrayOf<WotResolvedEventSelectClause>.Empty;
                    }
                    if (!clause.IsConditionIdSelection &&
                        !DeclaresMember(target.Data, clause.MemberPath) &&
                        !DeclaresMember(target.Data, WithoutStateName(clause.MemberPath)))
                    {
                        AddError(
                            diagnostics,
                            $"The clause selects '{clause.BrowsePath}', which the EventType " +
                            $"definition it references ('{clause.TypeDefinitionReference}') " +
                            "does not declare; a clause names a field of the type it names " +
                            "(WoT Binding Section 6.1).",
                            at);
                        return ArrayOf<WotResolvedEventSelectClause>.Empty;
                    }
                    explicitClauses.Add(new WotResolvedEventSelectClause(
                        target.TypeDefinitionId,
                        clause.BrowsePath,
                        WotEventSelectClauseSource.Explicit,
                        clause.TypeDefinitionReference));
                }
            }

            ArrayOf<WotResolvedEventSelectClause> final = Overlay(baseline, explicitClauses);
            if (final.Count == 0)
            {
                AddError(
                    diagnostics,
                    "The affordance states a selection that is empty; an event MonitoredItem " +
                    "created with no select clause returns nothing (WoT Binding Section 6.1).",
                    where);
                return ArrayOf<WotResolvedEventSelectClause>.Empty;
            }

            if (!WotEventSelectClauses.TryFindMaterializedCollision(
                final, prefix => ResolvePrefix(document, prefix),
                out string collision, out int collisionIndex))
            {
                AddError(
                    diagnostics,
                    collision,
                    collisionIndex < 0 ? where : where + "/" + WotEventSelectClauses.Term);
                return ArrayOf<WotResolvedEventSelectClause>.Empty;
            }

            if (hasEffectiveData && effectiveData.TryGetProperty(PropertiesMember, out _))
            {
                ArrayOf<ArrayOf<string>> members =
                    WotEventSelectClauses.GetMaterializedMemberPaths(final);
                for (int ii = 0; ii < final.Count; ii++)
                {
                    if (DeclaresMember(effectiveData, members[ii]))
                    {
                        continue;
                    }
                    AddError(
                        diagnostics,
                        $"The clause selects '{final[ii].BrowsePath}', which materializes the " +
                        $"data member '{WotEventSelectClauses.FormatMemberPath(members[ii])}'; " +
                        "the affordance's effective data schema declares no such member " +
                        "(WoT Binding Section 6.1).",
                        where);
                    return ArrayOf<WotResolvedEventSelectClause>.Empty;
                }
            }

            return CountErrors(diagnostics) == errorsBefore
                ? final
                : ArrayOf<WotResolvedEventSelectClause>.Empty;
        }

        /// <summary>
        /// Applies the overlay of WoT Binding Section 6.1: the materialized
        /// member paths are computed over the baseline and the explicit clauses
        /// together, every baseline clause an explicit clause names is removed,
        /// and the explicit clauses are appended in the order they are written.
        /// </summary>
        internal static ArrayOf<WotResolvedEventSelectClause> Overlay(
            List<WotResolvedEventSelectClause> baseline,
            List<WotResolvedEventSelectClause> explicitClauses)
        {
            if (explicitClauses.Count == 0)
            {
                return baseline.ToArray();
            }
            var combined = new List<WotResolvedEventSelectClause>(
                baseline.Count + explicitClauses.Count);
            combined.AddRange(baseline);
            combined.AddRange(explicitClauses);
            ArrayOf<ArrayOf<string>> members =
                WotEventSelectClauses.GetMaterializedMemberPaths<WotResolvedEventSelectClause>(
                    combined.ToArray());

            var replaced = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = baseline.Count; ii < combined.Count; ii++)
            {
                replaced.Add(WotEventSelectClauses.FormatMemberPath(members[ii]));
            }
            var result = new List<WotResolvedEventSelectClause>(combined.Count);
            for (int ii = 0; ii < baseline.Count; ii++)
            {
                if (!replaced.Contains(WotEventSelectClauses.FormatMemberPath(members[ii])))
                {
                    result.Add(baseline[ii]);
                }
            }
            result.AddRange(explicitClauses);
            return result.ToArray();
        }

        /// <summary>
        /// Derives one clause per leaf of a linked definition's <c>data</c>
        /// schema (WoT Binding Section 6.1).
        /// </summary>
        private static bool TryDeriveBaseline(
            WotDocument document,
            EventTypeDefinition definition,
            string where,
            List<WotDiagnostic> diagnostics,
            List<WotResolvedEventSelectClause> baseline)
        {
            var leaves = new List<Leaf>();
            if (!Walk(document, definition, definition.Data, [], [], string.Empty, where,
                diagnostics, leaves))
            {
                return false;
            }
            if (leaves.Count == 0)
            {
                AddError(
                    diagnostics,
                    $"The EventType definition '{definition.Reference}' declares no field, so " +
                    "an affordance that links to it selects nothing (WoT Binding Section 6.1).",
                    where);
                return false;
            }

            for (int ii = 0; ii < leaves.Count; ii++)
            {
                Leaf leaf = leaves[ii];
                int take = leaf.Members.Length;
                if (leaf.Members.Length == 1 &&
                    string.Equals(
                        leaf.Members[0],
                        WotEventSelectClauses.ConditionIdFieldName,
                        StringComparison.Ordinal))
                {
                    take = 0;
                }
                else if (leaf.Members.Length > 1 &&
                    string.Equals(
                        leaf.Members[leaf.Members.Length - 1],
                        WotEventSelectClauses.StateNameMember,
                        StringComparison.Ordinal) &&
                    (WotEventSelectClauses.IsStateVariableFieldName(
                            leaf.Members[leaf.Members.Length - 2]) ||
                        ReachesThroughParent(leaves, ii)))
                {
                    take = leaf.Members.Length - 1;
                }
                var elements = new string[take];
                Array.Copy(leaf.Elements, elements, take);
                baseline.Add(new WotResolvedEventSelectClause(
                    definition.TypeDefinitionId,
                    WotEventSelectClauses.JoinBrowsePath(elements),
                    WotEventSelectClauseSource.LinkedEventType,
                    definition.Reference));
            }
            return true;
        }

        private static bool Walk(
            WotDocument document,
            EventTypeDefinition definition,
            JsonElement schema,
            string[] members,
            string[] elements,
            string at,
            string where,
            List<WotDiagnostic> diagnostics,
            List<Leaf> leaves)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty(PropertiesMember, out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                leaves.Add(new Leaf(members, elements));
                return true;
            }

            var names = new List<string>();
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                names.Add(property.Name);
            }
            if (names.Count == 0)
            {
                leaves.Add(new Leaf(members, elements));
                return true;
            }
            if (names.Count > 1 &&
                !TryReadFieldOrder(schema, names, out names!))
            {
                AddError(
                    diagnostics,
                    $"The EventType definition '{definition.Reference}' walks the object " +
                    $"'{(at.Length == 0 ? "data" : at)}', which declares more than one " +
                    $"property and states no {WotEventSelectClauses.FieldOrderTerm} listing " +
                    "each of them exactly once; JSON member order is not an order " +
                    "(WoT Binding Section 6.1).",
                    where);
                return false;
            }

            foreach (string name in names)
            {
                if (!properties.TryGetProperty(name, out JsonElement child))
                {
                    continue;
                }
                if (!TryResolveElement(document, name, child, out string element))
                {
                    AddError(
                        diagnostics,
                        $"The EventType definition '{definition.Reference}' walks the member " +
                        $"'{(at.Length == 0 ? name : at + "/" + name)}', whose name is not a " +
                        "legal unqualified BrowseName and which declares no uav:browseName " +
                        "(WoT Binding Section 6.1).",
                        where);
                    return false;
                }
                if (!Walk(
                    document,
                    definition,
                    child,
                    Append(members, name),
                    Append(elements, element),
                    at.Length == 0 ? name : at + "/" + name,
                    where,
                    diagnostics,
                    leaves))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets whether another leaf reaches through this leaf's parent, which
        /// makes that parent a state Variable whose own <c>Name</c> member this
        /// leaf supplies (WoT Binding Section 6.1).
        /// </summary>
        private static bool ReachesThroughParent(List<Leaf> leaves, int index)
        {
            string[] members = leaves[index].Members;
            int parentLength = members.Length - 1;
            for (int ii = 0; ii < leaves.Count; ii++)
            {
                if (ii == index)
                {
                    continue;
                }
                string[] other = leaves[ii].Members;
                if (other.Length <= parentLength)
                {
                    continue;
                }
                bool matches = true;
                for (int jj = 0; jj < parentLength; jj++)
                {
                    if (!string.Equals(other[jj], members[jj], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return true;
                }
            }
            return false;
        }

        private async System.Threading.Tasks.ValueTask<EventTypeDefinition?> ResolveDefinitionAsync(
            WotDocument document,
            string reference,
            string where,
            ResolutionScope scope,
            System.Threading.CancellationToken cancellationToken)
        {
            List<WotDiagnostic> diagnostics = scope.Diagnostics;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string current = reference;
            bool carriesAnnotation = false;
            bool sawAnnotation = false;
            string? typeDefinitionId = null;
            JsonElement data = default;
            bool hasData = false;
            int maxDepth = Math.Max(1, scope.Context.Options.MaxDepth);

            for (int depth = 0; depth < maxDepth; depth++)
            {
                JsonElement? located = await ResolveReferenceTargetAsync(
                        document, current, where, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (located is null)
                {
                    return null;
                }
                JsonElement definition = located.Value;
                if (!seen.Add(current))
                {
                    AddError(
                        diagnostics,
                        $"The EventType reference '{reference}' revisits a definition already " +
                        "on the reference chain; the chain shall be acyclic " +
                        "(WoT Binding Section 6.1).",
                        where);
                    return null;
                }

                // A nearer definition's own members override the ones it
                // references, as in Section 12.4, so the first declaration the
                // chain reaches is the one that counts.
                if (!sawAnnotation && definition.TryGetProperty("@type", out _))
                {
                    sawAnnotation = true;
                    carriesAnnotation = CarriesEventTypeAnnotation(definition);
                }
                if (definition.TryGetProperty(WotEventSelectClauses.Term, out _))
                {
                    AddError(
                        diagnostics,
                        $"The EventType reference '{reference}' names a definition that " +
                        $"carries {WotEventSelectClauses.Term}; a definition states the fields " +
                        "an EventType has and does not select them " +
                        "(WoT Binding Section 6.1).",
                        where);
                    return null;
                }
                if (typeDefinitionId is null &&
                    definition.TryGetProperty(IdTerm, out JsonElement id) &&
                    id.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(id.GetString()))
                {
                    typeDefinitionId = id.GetString();
                }
                if (!hasData && definition.TryGetProperty(DataMember, out JsonElement candidate))
                {
                    hasData = true;
                    data = candidate;
                }

                if (!definition.TryGetProperty(
                        WotEventSelectClauses.TypeDefinitionReferenceTerm,
                        out JsonElement chained) ||
                    chained.ValueKind != JsonValueKind.String)
                {
                    return Validate(
                        reference, carriesAnnotation, typeDefinitionId, hasData, data,
                        where, diagnostics);
                }
                current = chained.GetString() ?? string.Empty;
            }

            AddError(
                diagnostics,
                $"The EventType reference '{reference}' follows more than {maxDepth} " +
                "definitions; the chain a consumer follows is bounded " +
                "(WoT Binding Section 6.1).",
                where);
            return null;
        }

        private static EventTypeDefinition? Validate(
            string reference,
            bool carriesAnnotation,
            string? typeDefinitionId,
            bool hasData,
            JsonElement data,
            string where,
            List<WotDiagnostic> diagnostics)
        {
            if (!carriesAnnotation)
            {
                AddError(
                    diagnostics,
                    $"The EventType reference '{reference}' names a definition whose @type " +
                    $"does not carry {WotEventSelectClauses.EventTypeAnnotation}; such a " +
                    "reference resolves to an EventType definition and to nothing else " +
                    "(WoT Binding Section 6.1).",
                    where);
                return null;
            }
            if (typeDefinitionId is null)
            {
                AddError(
                    diagnostics,
                    $"The EventType reference '{reference}' names a definition that carries no " +
                    $"{IdTerm}; the definition's identity is the TypeDefinitionId of every " +
                    "clause taken from it, and a consumer shall not invent one " +
                    "(WoT Binding Section 6.1).",
                    where);
                return null;
            }
            if (!hasData ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty(PropertiesMember, out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                AddError(
                    diagnostics,
                    $"The EventType reference '{reference}' names a definition whose data is " +
                    "not an object DataSchema declaring properties " +
                    "(WoT Binding Section 6.1).",
                    where);
                return null;
            }
            return new EventTypeDefinition(reference, typeDefinitionId, data);
        }

        private async System.Threading.Tasks.ValueTask<WotDocument?> LoadAsync(
            string documentUri,
            ResolutionScope scope,
            System.Threading.CancellationToken cancellationToken)
        {
            List<WotDiagnostic> diagnostics = scope.Diagnostics;
            if (scope.TryGetLoaded(documentUri, out WotDocument? cached))
            {
                return cached;
            }
            if (scope.IsUnresolvable(documentUri))
            {
                return null;
            }
            WotResolutionContext context = scope.Context;
            if (!context.TryEnter(
                WotResolutionKind.Thing, documentUri, out WotDiagnostic? blocked))
            {
                diagnostics.Add(blocked!);
                scope.MarkUnresolvable(documentUri);
                return null;
            }
            try
            {
                WotResolverResult result = await m_thingResolver
                    .ResolveThingAsync(documentUri, context, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Found)
                {
                    // Not found is not reported here: the same document is
                    // probed speculatively while the definition index is built,
                    // and only the caller knows whether anything actually
                    // needed it. Reporting here would fail a document over a
                    // reference nothing followed.
                    scope.MarkUnresolvable(documentUri);
                    return null;
                }
                if (!context.TryAddBytes(
                    documentUri, result.Content.Length, out WotDiagnostic? limit))
                {
                    diagnostics.Add(limit!);
                    scope.MarkUnresolvable(documentUri);
                    return null;
                }
                WotDocument parsed;
                try
                {
#pragma warning disable CA2000 // Ownership transfers to the scope, which disposes it.
                    parsed = WotDocument.Parse(result.Content, m_options);
#pragma warning restore CA2000
                }
                catch (Exception exception) when (
                    exception is JsonException or FormatException)
                {
                    // Same reason as above: a document that cannot be parsed
                    // resolves to nothing, and the caller reports the reference
                    // that needed it.
                    scope.MarkUnresolvable(documentUri);
                    return null;
                }
                scope.AddLoaded(documentUri, parsed);
                return parsed;
            }
            finally
            {
                context.Leave(documentUri);
            }
        }

        private static bool TryReadFieldOrder(
            JsonElement schema, List<string> names, out List<string>? ordered)
        {
            ordered = null;
            if (!schema.TryGetProperty(WotEventSelectClauses.FieldOrderTerm, out JsonElement order) ||
                order.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var stated = new List<string>(names.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in order.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                string name = entry.GetString() ?? string.Empty;
                if (!names.Contains(name) || !seen.Add(name))
                {
                    return false;
                }
                stated.Add(name);
            }
            if (stated.Count != names.Count)
            {
                return false;
            }
            ordered = stated;
            return true;
        }

        /// <summary>
        /// Gets the browse-path element a <c>data</c> member contributes: the
        /// member's <c>uav:browseName</c>, or the member's own name where that
        /// name is a legal unqualified BrowseName (WoT Binding Section 6.1).
        /// </summary>
        private static bool TryResolveElement(
            WotDocument document, string name, JsonElement schema, out string element)
        {
            element = string.Empty;
            if (schema.ValueKind == JsonValueKind.Object &&
                schema.TryGetProperty(BrowseNameTerm, out JsonElement browseName))
            {
                if (browseName.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                string value = browseName.GetString() ?? string.Empty;
                if (value.Length == 0)
                {
                    return false;
                }
                element = value;
                return true;
            }
            if (name.Length == 0 ||
                name.Contains(':', StringComparison.Ordinal) ||
                name.Contains('/', StringComparison.Ordinal))
            {
                // A bare name cannot say which NamespaceUri qualifies it, so a
                // member of any other namespace declares uav:browseName.
                return false;
            }
            element = name;
            return true;
        }

        private static bool CarriesEventTypeAnnotation(JsonElement definition)
        {
            if (!definition.TryGetProperty("@type", out JsonElement types))
            {
                return false;
            }
            if (types.ValueKind == JsonValueKind.String)
            {
                return string.Equals(
                    types.GetString(),
                    WotEventSelectClauses.EventTypeAnnotation,
                    StringComparison.Ordinal);
            }
            if (types.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (JsonElement entry in types.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String &&
                    string.Equals(
                        entry.GetString(),
                        WotEventSelectClauses.EventTypeAnnotation,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool DeclaresMember(JsonElement schema, ArrayOf<string> memberPath)
        {
            if (memberPath.IsNull || memberPath.Count == 0)
            {
                return false;
            }
            JsonElement node = schema;
            for (int ii = 0; ii < memberPath.Count; ii++)
            {
                if (node.ValueKind != JsonValueKind.Object ||
                    !node.TryGetProperty(PropertiesMember, out JsonElement properties) ||
                    properties.ValueKind != JsonValueKind.Object ||
                    !properties.TryGetProperty(memberPath[ii], out node))
                {
                    return false;
                }
            }
            return true;
        }

        private static ArrayOf<string> WithoutStateName(ArrayOf<string> memberPath)
        {
            if (memberPath.Count < 2 ||
                !string.Equals(
                    memberPath[memberPath.Count - 1],
                    WotEventSelectClauses.StateNameMember,
                    StringComparison.Ordinal))
            {
                return memberPath;
            }
            var trimmed = new string[memberPath.Count - 1];
            for (int ii = 0; ii < trimmed.Length; ii++)
            {
                trimmed[ii] = memberPath[ii];
            }
            return trimmed;
        }

        private static string? ResolvePrefix(WotDocument document, string prefix)
        {
            return document.TryGetContextPrefix(prefix, out string uri) ? uri : null;
        }

        private static string[] Append(string[] values, string value)
        {
            var appended = new string[values.Length + 1];
            Array.Copy(values, appended, values.Length);
            appended[values.Length] = value;
            return appended;
        }

        private static string Index(int index)
        {
            return index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string EscapePointerToken(string token)
        {
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        private static void AddError(
            List<WotDiagnostic> diagnostics, string message, string pointer)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.EventSelectClauseInvalid,
                message,
                WotLocation.FromPointer(pointer)));
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

        private const string DataMember = "data";
        private const string PropertiesMember = "properties";
        private const string IdTerm = "uav:id";
        private const string BrowseNameTerm = "uav:browseName";

        /// <summary>
        /// One leaf of a walked EventType <c>data</c> schema: the member names
        /// that reach it and the browse-path elements those members contribute.
        /// </summary>
        private readonly struct Leaf
        {
            public Leaf(string[] members, string[] elements)
            {
                Members = members;
                Elements = elements;
            }

            public string[] Members { get; }

            public string[] Elements { get; }
        }

        /// <summary>
        /// A resolved EventType definition: the reference that named it, the
        /// portable identity every clause taken from it carries, and the
        /// <c>data</c> schema the derivation walks.
        /// </summary>
        private sealed class EventTypeDefinition
        {
            public EventTypeDefinition(string reference, string typeDefinitionId, JsonElement data)
            {
                Reference = reference;
                TypeDefinitionId = typeDefinitionId;
                Data = data;
            }

            public string Reference { get; }

            public string TypeDefinitionId { get; }

            public JsonElement Data { get; }
        }

        /// <summary>
        /// Everything one <see cref="ResolveAsync"/> accumulates: the bounds it
        /// resolves under, the diagnostics it reports, the documents it opened,
        /// the references it already found unresolvable and the linked schemas
        /// it carries out.
        /// </summary>
        /// <remarks>
        /// The two caches are what keeps a document that several affordances
        /// reference read once, and what keeps one unresolvable reference from
        /// being reported once per affordance that names it. They are scoped to
        /// the call rather than to the instance because the documents they hold
        /// are closed when the call ends: an instance-wide cache would hand the
        /// next caller a <see cref="WotDocument"/> that this one disposed.
        /// </remarks>
        private sealed class ResolutionScope : IDisposable
        {
            public ResolutionScope(WotResolutionContext context)
            {
                Context = context;
            }

            public WotResolutionContext Context { get; }

            public List<WotDiagnostic> Diagnostics { get; } = [];

            /// <summary>
            /// Every definition the held documents declare, indexed by the
            /// logical identifiers it can be named by.
            /// </summary>
            public Dictionary<string, List<DefinitionCandidate>> Definitions { get; } =
                new(StringComparer.Ordinal);

            public bool TryGetLoaded(string documentUri, out WotDocument? document)
            {
                return m_loaded.TryGetValue(documentUri, out document);
            }

            public void AddLoaded(string documentUri, WotDocument document)
            {
                m_loaded.Add(documentUri, document);
            }

            public bool IsUnresolvable(string documentUri)
            {
                return m_unresolvable.Contains(documentUri);
            }

            public void MarkUnresolvable(string documentUri)
            {
                m_unresolvable.Add(documentUri);
            }

            /// <summary>
            /// Records that a document's failure to resolve has been reported,
            /// answering <c>true</c> only for the first reader that asks.
            /// </summary>
            public bool TryMarkReported(string documentUri)
            {
                return m_reported.Add(documentUri);
            }

            public void SetLinkedData(string affordanceName, byte[] utf8Json)
            {
                m_linkedData[affordanceName] = utf8Json;
            }

            public ReadOnlyMemory<byte> TakeLinkedData(string affordanceName)
            {
                return m_linkedData.TryGetValue(affordanceName, out byte[]? data)
                    ? data
                    : default;
            }

            public void Dispose()
            {
                foreach (WotDocument opened in m_loaded.Values)
                {
                    opened.Dispose();
                }
                m_loaded.Clear();
                m_unresolvable.Clear();
                m_reported.Clear();
                m_linkedData.Clear();
                Definitions.Clear();
            }

            private readonly Dictionary<string, WotDocument> m_loaded = new(StringComparer.Ordinal);
            private readonly HashSet<string> m_unresolvable = new(StringComparer.Ordinal);
            private readonly HashSet<string> m_reported = new(StringComparer.Ordinal);
            private readonly Dictionary<string, byte[]> m_linkedData = new(StringComparer.Ordinal);
        }

        private readonly IWotThingResolver m_thingResolver;
        private readonly WotNodeSetConverterOptions m_options;
    }
}
