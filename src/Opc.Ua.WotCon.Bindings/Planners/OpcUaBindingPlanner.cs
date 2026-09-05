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
using System.Collections.Immutable;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.Planners
{
    /// <summary>
    /// The OPC UA WoT Connectivity binding planner (OPC 10101). It validates the
    /// portable <c>uav:id</c> / <c>opc.tcp</c> href and the <c>uav:componentOf</c>
    /// containment reference, checks <c>op</c> compatibility, compiles the
    /// event field selection of WoT Binding Section 6.1 and the <c>auto</c>
    /// endpoint security floor of Section 5.7.1, and compiles the
    /// form into immutable endpoint and NodeId addressing metadata. It is
    /// executable when the OPC UA executor is registered. The OPC 10101 §6.5.4
    /// target-mapping terms (<c>uav:mapToNodeId</c> / <c>uav:mapToType</c> /
    /// <c>uav:mapByFieldPath</c>) are property-affordance-level and
    /// protocol-neutral; they are validated centrally by
    /// <see cref="WotProtocolBinderRegistry"/> for every protocol, not parsed
    /// here.
    /// </summary>
    public sealed class OpcUaBindingPlanner : WotProtocolBinderBase
    {
        /// <summary>
        /// The OPC UA WoT binding vocabulary URI.
        /// </summary>
        public const string BindingUri = "http://opcfoundation.org/UA/WoT-Binding/";

        /// <summary>
        /// The superseded spelling this implementation minted for an event's
        /// extra select clauses before WoT Binding Section 6.1 standardized
        /// <see cref="WotEventSelectClauses.Term"/>.
        /// </summary>
        /// <remarks>
        /// It is authored on a form, carries bare browse paths and adds to the
        /// implicit BaseEventType default. It is still read so a document already
        /// authored against this implementation keeps working, and it is never
        /// written: a document this stack produces states the standardized
        /// terms.
        /// </remarks>
        public const string LegacyEventFieldsTerm = "uav:eventFields";

        private static readonly string[] s_schemes = ["opc.tcp", "opc.https", "opc.wss"];

        /// <inheritdoc/>
        public override WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("opc.opcua", "10101", BindingUri, "OPC UA WoT Connectivity Binding");

        /// <inheritdoc/>
        public override WotBindingCapability Capability { get; } = new WotBindingCapability(
            BindingUri,
            "OPC UA WoT Connectivity Binding (OPC 10101)",
            WotBindingSources.OpcUa,
            [
                WoTBindingCapabilityEnum.ReadProperty,
                WoTBindingCapabilityEnum.WriteProperty,
                WoTBindingCapabilityEnum.ObserveProperty,
                WoTBindingCapabilityEnum.InvokeAction,
                WoTBindingCapabilityEnum.SubscribeEvent,
                WoTBindingCapabilityEnum.UnsubscribeEvent
            ],
            ["application/json", "application/opcua+json", "application/octet-stream"],
            isExecutable: true);

        /// <inheritdoc/>
        protected override IReadOnlyCollection<string> Schemes => s_schemes;

        /// <inheritdoc/>
        public override WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context)
        {
            return MatchStandard(form, context, "uav:");
        }

        /// <inheritdoc/>
        public override WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context)
        {
            var diagnostics = new List<WotBindingDiagnostic>();

            WotEndpointDescriptor endpoint;
            string? authority;
            if (!string.IsNullOrEmpty(form.Href) && TryParseUri(form.Href!, out Uri uri))
            {
                if (!IsOpcScheme(uri.Scheme))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.UnsupportedScheme,
                        $"'{uri.Scheme}' is not an OPC UA transport scheme.", form.Pointer("href")));
                    return WotBindingCompilation.Unsupported([.. diagnostics]);
                }
                endpoint = MakeEndpoint(uri);
                authority = ToTransmittedAuthority(uri);
            }
            else if (!string.IsNullOrEmpty(context.BaseUri) &&
                TryParseUri(context.BaseUri!, out Uri baseUri) &&
                IsOpcScheme(baseUri.Scheme))
            {
                endpoint = MakeEndpoint(baseUri);
                authority = ToTransmittedAuthority(baseUri);
            }
            else
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "An OPC UA form requires an opc.tcp href or a Thing base opc.tcp endpoint.",
                    form.Pointer("href")));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            string? nodeId = ResolveNodeId(form);
            if (string.IsNullOrEmpty(nodeId))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingRequiredField,
                    "An OPC UA form requires uav:id or a NodeId in the href path.",
                    form.Pointer("uav:id"), "uav:id"));
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            ImmutableDictionary<string, string> metadata = ImmutableDictionary<string, string>.Empty
                .Add("nodeId", nodeId!);
            metadata = AddIfPresent(form, "uav:componentOf", "componentOf", metadata);

            WotEventSelection? eventSelection = ResolveEventSelection(form, context, diagnostics);
            if (form.Kind == WotAffordanceKind.Event && eventSelection is null)
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            if (!ResolveCodec(form, context, diagnostics, out WotPayloadDescriptor payload))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }
            var addressing = new WotAddressingDescriptor(nodeId!, metadata);
            ImmutableArray<WotCredentialReference> security = ResolveSecurity(form, context, authority, diagnostics);
            if (!TryResolveSecurityFloor(form, context, diagnostics, out WotSecurityFloor? securityFloor))
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }

            ImmutableArray<WotCompiledForm>.Builder entries = ImmutableArray.CreateBuilder<WotCompiledForm>();
            foreach ((string op, WoTBindingCapabilityEnum capability) in ResolveOperations(form, diagnostics))
            {
                var operation = new WotOperationDescriptor(capability, op, OpcUaService(capability));
                entries.Add(new WotCompiledForm(
                    Identity, form.Kind, form.AffordanceName, form.JsonPointer, capability, op,
                    endpoint, addressing, operation, payload, security, Capability.IsExecutable,
                    targetMapping: null, eventSelection, securityFloor));
            }

            if (entries.Count == 0)
            {
                return WotBindingCompilation.Unsupported([.. diagnostics]);
            }
            return WotBindingCompilation.Supported(entries.ToImmutable(), [.. diagnostics]);
        }

        /// <summary>
        /// Compiles the effective event field selection of WoT Binding
        /// Section 6.1 for an event affordance.
        /// </summary>
        /// <remarks>
        /// An affordance states its selection by linking its EventType
        /// definition with <c>tm:ref</c>, by overlaying that baseline with
        /// <c>uav:eventSelectClauses</c>, or with both; an affordance that
        /// states neither takes the implicit <c>BaseEventType</c> default.
        /// Resolving a link follows document references, so it happens before
        /// planning and this method reads the result from
        /// <see cref="WotBindingPlanContext.EventSelections"/>. The superseded
        /// <c>uav:eventFields</c> spelling this implementation minted before the
        /// terms existed adds field names to that default. Where a form carries
        /// both, the standardized terms win and the contradiction is reported:
        /// merging the two would produce a list neither spelling states, and
        /// silently preferring one without saying so would leave the author
        /// unable to tell which was honoured.
        /// </remarks>
        /// <returns>
        /// The effective selection, or <c>null</c> when the affordance is not
        /// an event or the authored selection is invalid or unresolved.
        /// </returns>
        private static WotEventSelection? ResolveEventSelection(
            WotAffordanceForm form,
            WotBindingPlanContext context,
            List<WotBindingDiagnostic> diagnostics)
        {
            bool legacy = form.TryGetStringArray(
                LegacyEventFieldsTerm, out ImmutableArray<string> legacyFields);
            if (form.Kind != WotAffordanceKind.Event)
            {
                if (HasSelectClauses(form.FormElement) || HasSelectClauses(form.AffordanceElement))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.EventSelectClauseInvalid,
                        $"'{WotEventSelectClauses.Term}' selects OPC UA event fields and belongs " +
                        "only on an event affordance (WoT Binding Sections 6.1 and 7).",
                        form.AffordancePointer(WotEventSelectClauses.Term),
                        WotEventSelectClauses.Term));
                }
                return null;
            }

            if (HasSelectClauses(form.FormElement))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.EventSelectClauseInvalid,
                    $"'{WotEventSelectClauses.Term}' is defined by WoT Binding Section 6.1 " +
                    "directly on the event affordance, not on a form.",
                    form.Pointer(WotEventSelectClauses.Term),
                    WotEventSelectClauses.Term));
                return null;
            }

            if (!WotEventSelectionResolver.StatesSelection(form.AffordanceElement))
            {
                if (!legacy)
                {
                    return WotEventSelection.Default;
                }
                diagnostics.Add(WotBindingDiagnostic.Warning(
                    WotBindingDiagnosticCode.UnknownVocabularyTerm,
                    $"'{LegacyEventFieldsTerm}' is the spelling this implementation minted " +
                    $"before WoT Binding Section 6.1 standardized '{WotEventSelectClauses.Term}'. " +
                    "It is still read, and its fields are added to the implicit default " +
                    "selection, but a portable document states the standardized terms instead.",
                    form.Pointer(LegacyEventFieldsTerm),
                    LegacyEventFieldsTerm));
                return BuildLegacySelection(legacyFields);
            }

            if (legacy)
            {
                diagnostics.Add(WotBindingDiagnostic.Warning(
                    WotBindingDiagnosticCode.ConflictingFields,
                    $"The affordance states its selection with the standardized terms of " +
                    $"WoT Binding Section 6.1 and the form states '{LegacyEventFieldsTerm}'. " +
                    "The standardized terms are honoured; the superseded spelling is ignored " +
                    "rather than merged, because a merged list is one neither spelling states.",
                    form.Pointer(LegacyEventFieldsTerm),
                    LegacyEventFieldsTerm));
            }

            string pointer = form.AffordancePointer(WotEventSelectClauses.Term);
            if (!context.EventSelections.TryGetSelection(
                form.AffordanceName, out ArrayOf<WotResolvedEventSelectClause> clauses) ||
                clauses.Count == 0)
            {
                // Planning is synchronous and side-effect free, so a link this
                // request never resolved is reported rather than followed here:
                // an EventType definition is a document, and reading one during
                // planning would make a plan depend on what a host served at the
                // moment it was compiled.
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.EventSelectClauseInvalid,
                    $"The event affordance '{form.AffordanceName}' states its field selection " +
                    $"with '{WotEventSelectClauses.TypeDefinitionReferenceTerm}' or " +
                    $"'{WotEventSelectClauses.Term}', and no resolved selection was supplied " +
                    "with the plan request. Build the request with " +
                    "WotBindingPlanRequest.FromDocumentAsync, or supply a resolved event " +
                    "selection catalog: planning never dereferences a document link " +
                    "(WoT Binding Sections 5.1.5 and 6.1).",
                    pointer,
                    WotEventSelectClauses.Term));
                return null;
            }

            var resolved = new WotResolvedEventSelectClause[clauses.Count];
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                if (!TryResolveClause(
                    clauses[ii], context, out WotResolvedEventSelectClause? clause, out string error))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.UnboundNamespacePrefix,
                        error,
                        pointer + "/" + ii.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        WotEventSelectClauses.Term));
                    return null;
                }
                resolved[ii] = clause!;
            }

            // Section 6.1 states clause uniqueness over the materialized member
            // path, because that member and not the browse path it came from
            // decides the output: two clauses that reach the same member compete
            // for it whatever EventType each names as the declaring type, and
            // nothing in the document says which of them filled it. Resolution
            // has already checked the overlaid selection; this checks the list
            // the planner rewrote into portable form, so a rewrite can never
            // introduce a collision the plan would carry into a subscription.
            if (!WotEventSelectClauses.TryFindMaterializedCollision(
                new ArrayOf<WotResolvedEventSelectClause>(resolved),
                null,
                out string collision,
                out int collisionIndex))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.EventSelectClauseInvalid,
                    collision,
                    collisionIndex < 0
                        ? pointer
                        : pointer + "/" + collisionIndex.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    WotEventSelectClauses.Term));
                return null;
            }
            return new WotEventSelection(resolved, WotEventSelectionOrigin.Standard);
        }

        /// <summary>
        /// Rewrites a clause's compact model names into the portable
        /// <c>nsu=</c> form so a channel can resolve it against a Server's
        /// namespace table without the document (WoT Binding Sections 5.1.2
        /// and 5.8).
        /// </summary>
        private static bool TryResolveClause(
            WotResolvedEventSelectClause clause,
            WotBindingPlanContext context,
            out WotResolvedEventSelectClause? resolved,
            out string error)
        {
            resolved = null;
            if (clause.BrowsePath.Length == 0)
            {
                resolved = clause;
                error = string.Empty;
                return true;
            }
            // The clause carries its parsed elements, so a NamespaceUri that
            // contains '/' - which every http NamespaceUri does - is rewritten
            // as one element rather than torn apart by the path separator.
            ArrayOf<string> parsed = clause.PathElements;
            var elements = new string[parsed.Count];
            bool rewritten = false;
            for (int ii = 0; ii < elements.Length; ii++)
            {
                if (!TryResolvePathElement(parsed[ii], context, out string element, out error))
                {
                    return false;
                }
                rewritten |= !string.Equals(element, parsed[ii], StringComparison.Ordinal);
                elements[ii] = element;
            }
            resolved = rewritten
                ? clause.WithBrowsePath(WotEventSelectClauses.JoinBrowsePath(elements))
                : clause;
            error = string.Empty;
            return true;
        }

        private static bool TryResolvePathElement(
            string element,
            WotBindingPlanContext context,
            out string resolved,
            out string error)
        {
            resolved = element;
            error = string.Empty;
            if (element.Length == 0)
            {
                return true;
            }
            if (element.StartsWith("nsu=", StringComparison.Ordinal) || element[0] == '{')
            {
                // Already NamespaceUri-qualified, in either the OPC 10000-6 or
                // the OPC 10000-4 spelling.
                return true;
            }
            int separator = element.IndexOf(':', 0);
            if (separator <= 0 || separator + 1 >= element.Length)
            {
                // A bare name is a namespace 0 BrowseName.
                return true;
            }
            string prefix = element.Substring(0, separator);
            string name = element.Substring(separator + 1);
            if (string.Equals(prefix, "ua", StringComparison.Ordinal))
            {
                resolved = name;
                return true;
            }
            if (!context.NamespacePrefixes.TryGetValue(prefix, out string? namespaceUri))
            {
                error = $"The select-clause browse path element '{element}' uses the prefix " +
                    $"'{prefix}', which the document's @context does not bind (WoT Binding " +
                    "Section 5.8).";
                return false;
            }
            resolved = string.Equals(
                namespaceUri, WotBindingConformance.OpcUaNamespace, StringComparison.Ordinal)
                ? name
                // ';' terminates the NamespaceUri and '%' starts an escape, so the
                // URI is percent-escaped exactly as every other nsu= producer in
                // this stack escapes it (OPC 10000-6 §5.3.1.11).
                : "nsu=" + CoreUtils.EscapeUri(namespaceUri) + ";" + name;
            return true;
        }

        private static WotEventSelection BuildLegacySelection(ImmutableArray<string> fields)
        {
            var clauses = new List<WotResolvedEventSelectClause>(
                WotEventSelectClauses.Default.Count + fields.Length);
            foreach (WotResolvedEventSelectClause clause in WotEventSelectClauses.Default)
            {
                clauses.Add(clause);
            }
            foreach (string field in fields)
            {
                // A superseded field that reaches a data member the default
                // already fills is not added twice: Section 6.1 lets exactly one
                // clause materialize a member, and the implicit default is the
                // list this spelling extends rather than competes with.
                clauses.Add(new WotResolvedEventSelectClause(
                    WotEventSelectClauses.BaseEventTypeId,
                    field,
                    WotEventSelectClauseSource.Explicit));
                if (!WotEventSelectClauses.TryFindMaterializedCollision(
                    new ArrayOf<WotResolvedEventSelectClause>(clauses.ToArray()),
                    null,
                    out _,
                    out _))
                {
                    clauses.RemoveAt(clauses.Count - 1);
                }
            }
            return new WotEventSelection(clauses.ToArray(), WotEventSelectionOrigin.Legacy);
        }

        private static bool HasSelectClauses(System.Text.Json.JsonElement element)
        {
            return element.ValueKind == System.Text.Json.JsonValueKind.Object &&
                element.TryGetProperty(WotEventSelectClauses.Term, out _);
        }

        /// <summary>
        /// Resolves the security floor an <c>auto</c> scheme referenced by the
        /// form puts on endpoint selection (WoT Binding Section 5.7.1).
        /// </summary>
        /// <remarks>
        /// A floor the document declares but this Binding cannot read - one
        /// carried by a scheme other than <c>auto</c>, or naming a mode or
        /// policy Section 5.7 does not - fails the form rather than compiling
        /// it without the constraint. A floor a client may quietly step below
        /// states nothing, and a floor a client never learned about states
        /// less.
        /// </remarks>
        private static bool TryResolveSecurityFloor(
            WotAffordanceForm form,
            WotBindingPlanContext context,
            List<WotBindingDiagnostic> diagnostics,
            out WotSecurityFloor? floor)
        {
            floor = null;
            WotSecurityFloor? combined = null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            foreach (string schemeName in form.SecuritySchemes)
            {
                pending.Enqueue(schemeName);
            }
            while (pending.Count > 0)
            {
                string schemeName = pending.Dequeue();
                if (!visited.Add(schemeName) ||
                    !context.SecurityDefinitions.TryGetValue(
                        schemeName, out WotSecurityDefinition? definition))
                {
                    continue;
                }
                // A `combo` scheme is the standard way to require a secure
                // channel and a user token together, so a floor stated on the
                // channel scheme it combines is a floor the form is subject to.
                foreach (string referenced in definition.Combines)
                {
                    pending.Enqueue(referenced);
                }
                if (definition.MinimumSecurity is { IsEmpty: false } stated)
                {
                    combined = Combine(combined, stated);
                    continue;
                }
                if (definition.DeclaresMinimumSecurity)
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidSecurityFloor,
                        $"The '{schemeName}' security scheme declares " +
                        $"'{WotBindingConformance.MinimumSecurityTerm}' but the floor cannot be " +
                        "read: WoT Binding Section 5.7.1 carries it only on an 'auto' scheme and " +
                        "only with the mode and policy names Section 5.7 lists.",
                        "/securityDefinitions/" + WotAffordanceForm.EscapePointerToken(schemeName) +
                            "/" + WotBindingConformance.MinimumSecurityTerm,
                        WotBindingConformance.MinimumSecurityTerm));
                    return false;
                }
            }
            floor = combined;
            return true;
        }

        /// <summary>
        /// Combines two floors into the stronger constraint in each dimension,
        /// which is what a form that references two constrained schemes means.
        /// </summary>
        private static WotSecurityFloor Combine(WotSecurityFloor? left, WotSecurityFloor right)
        {
            return left is null ? right : Strongest(left, right);
        }

        /// <summary>
        /// Combines two floors into the stronger constraint in each dimension,
        /// which is what a form that references two constrained schemes means.
        /// </summary>
        private static WotSecurityFloor Strongest(WotSecurityFloor left, WotSecurityFloor right)
        {
            return new WotSecurityFloor(
                StrongerMode(left.SecurityMode, right.SecurityMode),
                StrongerPolicy(left.SecurityPolicy, right.SecurityPolicy));
        }

        private static string? StrongerMode(string? left, string? right)
        {
            if (left is null || right is null)
            {
                return left ?? right;
            }
            WotBindingConformance.TryGetSecurityModeRank(left, out int leftRank);
            WotBindingConformance.TryGetSecurityModeRank(right, out int rightRank);
            return leftRank >= rightRank ? left : right;
        }

        private static string? StrongerPolicy(string? left, string? right)
        {
            if (left is null || right is null)
            {
                return left ?? right;
            }
            WotBindingConformance.TryGetSecurityPolicyRank(left, out int leftRank);
            WotBindingConformance.TryGetSecurityPolicyRank(right, out int rightRank);
            return leftRank >= rightRank ? left : right;
        }

        private static bool IsOpcScheme(string scheme)
        {
            foreach (string handled in s_schemes)
            {
                if (string.Equals(scheme, handled, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string? ResolveNodeId(WotAffordanceForm form)
        {
            if (form.TryGetString("uav:id", out string id) && !string.IsNullOrEmpty(id))
            {
                return id;
            }
            if (!string.IsNullOrEmpty(form.Href) && TryParseUri(form.Href!, out Uri uri))
            {
                string path = uri.AbsolutePath.Trim('/');
                if (LooksLikeNodeId(path))
                {
                    return Uri.UnescapeDataString(path);
                }
                string query = uri.Query.TrimStart('?');
                if (query.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(query[3..]);
                }
            }
            return null;
        }

        private static bool LooksLikeNodeId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            // A textual OPC UA NodeId always carries an identifier assignment
            // (for example "i=", "s=", "g=", "b=" or a namespace "ns=").
            foreach (char c in value)
            {
                if (c == '=')
                {
                    return true;
                }
            }
            return false;
        }

        private static ImmutableDictionary<string, string> AddIfPresent(
            WotAffordanceForm form, string term, string key, ImmutableDictionary<string, string> metadata)
        {
            return form.TryGetString(term, out string value) ? metadata.Add(key, value) : metadata;
        }

        private static string OpcUaService(WoTBindingCapabilityEnum operation)
        {
            return operation switch
            {
                WoTBindingCapabilityEnum.WriteProperty => "Write",
                WoTBindingCapabilityEnum.ObserveProperty => "Subscribe",
                WoTBindingCapabilityEnum.InvokeAction => "Call",
                WoTBindingCapabilityEnum.SubscribeEvent => "EventSubscribe",
                WoTBindingCapabilityEnum.UnsubscribeEvent => "EventSubscribe",
                _ => "Read"
            };
        }
    }
}
