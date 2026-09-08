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
            if (!form.TryResolveHref(
                context.BaseUri, out WotAffordanceForm resolved, out WotBindingDiagnostic? addressDiagnostic))
            {
                return WotBindingCompilation.Unsupported([addressDiagnostic]);
            }
            form = resolved;

            string? nodeId = ResolveNodeId(form, out bool nodeIdInPath);
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
                endpoint = MakeOpcUaEndpoint(uri, nodeIdInPath);
                authority = ToTransmittedAuthority(uri);
            }
            else if (!string.IsNullOrEmpty(context.BaseUri) &&
                TryParseUri(context.BaseUri!, out Uri baseUri) &&
                IsOpcScheme(baseUri.Scheme))
            {
                endpoint = MakeOpcUaEndpoint(baseUri, nodeIdInPath: false);
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
            if (!TryResolveSecurityRequirements(
                form, context, authority, diagnostics, out ArrayOf<WotOpcUaSecurityRequirement> exact,
                out WotSecurityFloor? securityFloor))
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
                    targetMapping: null, eventSelection, securityFloor)
                    .WithOpcUaSecurityRequirements(exact)
                    .WithConditionInvocation(WotConditionInvocation.FromAffordance(form)));
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

        private static bool TryResolveSecurityRequirements(
            WotAffordanceForm form,
            WotBindingPlanContext context,
            string? endpoint,
            List<WotBindingDiagnostic> diagnostics,
            out ArrayOf<WotOpcUaSecurityRequirement> requirements,
            out WotSecurityFloor? commonFloor)
        {
            requirements = [];
            commonFloor = null;
            if (context.Bounds.MaxSecurityAlternatives <= 0 || context.Bounds.MaxSecurityDepth <= 0)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded, "Security compilation bounds must be positive."));
                return false;
            }
            List<WotOpcUaSecurityRequirement> result = [new(null, null)];
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in form.SecuritySchemes)
            {
                List<WotOpcUaSecurityRequirement>? expanded = Expand(name);
                if (expanded is null)
                {
                    return false;
                }
                List<WotOpcUaSecurityRequirement>? merged = CombineAlternatives(result, expanded);
                if (merged is null)
                {
                    return false;
                }
                result = merged;
            }
            if (result.Exists(requirement => requirement.SecurityMode.HasValue ||
                requirement.SecurityPolicyUri is not null || requirement.UserIdentityToken.HasValue ||
                requirement.MinimumSecurity is { IsEmpty: false }))
            {
                requirements = result.ToArrayOf();
            }
            commonFloor = result[0].MinimumSecurity;
            foreach (WotOpcUaSecurityRequirement requirement in result)
            {
                if (requirement.MinimumSecurity?.SecurityMode != commonFloor?.SecurityMode ||
                    requirement.MinimumSecurity?.SecurityPolicy != commonFloor?.SecurityPolicy)
                {
                    commonFloor = null;
                    break;
                }
            }
            return true;

            List<WotOpcUaSecurityRequirement>? Expand(string name)
            {
                string pointer = "/securityDefinitions/" + WotAffordanceForm.EscapePointerToken(name);
                if (active.Count >= context.Bounds.MaxSecurityDepth || !active.Add(name))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.BoundsExceeded,
                        "The security scheme graph is cyclic or exceeds the configured depth.", pointer));
                    return null;
                }
                try
                {
                    if (!context.SecurityDefinitions.TryGetValue(name, out WotSecurityDefinition? definition))
                    {
                        if (name == "nosec_sc")
                        {
                            return [new(null, null)];
                        }
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.UnknownSecurityScheme,
                            $"The required security scheme '{name}' is not declared.", pointer));
                        return null;
                    }
                    if (definition.CombinationError is not null)
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.InvalidFieldValue, definition.CombinationError, pointer));
                        return null;
                    }
                    if (definition.DeclaresIssueToken && definition.Scheme != WotSecurityScheme.OpcUaAuthentication)
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.InvalidFieldValue,
                            "Only an OPC UA authentication scheme can reference token acquisition.",
                            pointer + "/uav:issueToken"));
                        return null;
                    }
                    if (definition.DeclaresMinimumSecurity &&
                        (definition.Scheme != WotSecurityScheme.Auto ||
                         definition.MinimumSecurity is not { IsEmpty: false }))
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.InvalidSecurityFloor,
                            $"The '{name}' security scheme declares an invalid minimum security floor; " +
                            "only an auto scheme can carry a floor with recognized mode and policy names.",
                            pointer + "/" + WotBindingConformance.MinimumSecurityTerm,
                            WotBindingConformance.MinimumSecurityTerm));
                        return null;
                    }
                    if (definition.Scheme == WotSecurityScheme.Combo)
                    {
                        List<WotOpcUaSecurityRequirement> combinations = definition.CombinesAlternatives
                            ? [] : [new(null, null)];
                        foreach (string child in definition.Combines)
                        {
                            List<WotOpcUaSecurityRequirement>? nested = Expand(child);
                            if (nested is null)
                            {
                                return null;
                            }
                            if (definition.CombinesAlternatives)
                            {
                                combinations.AddRange(nested);
                                if (!CheckBound(combinations.Count))
                                {
                                    return null;
                                }
                            }
                            else
                            {
                                List<WotOpcUaSecurityRequirement>? merged = CombineAlternatives(combinations, nested);
                                if (merged is null)
                                {
                                    return null;
                                }
                                combinations = merged;
                            }
                        }
                        return combinations;
                    }
                    if (definition.Scheme == WotSecurityScheme.OpcUaAuthentication)
                    {
                        UserTokenType? requiredToken = definition.OpcUaUserIdentityToken switch
                        {
                            "Anonymous" => UserTokenType.Anonymous,
                            "UserName" => UserTokenType.UserName,
                            "Certificate" => UserTokenType.Certificate,
                            "IssuedToken" => UserTokenType.IssuedToken,
                            _ => null
                        };
                        if (requiredToken.HasValue)
                        {
                            WotCredentialReference? issuer = null;
                            if (definition.DeclaresIssueToken)
                            {
                                if (requiredToken != UserTokenType.IssuedToken ||
                                    string.IsNullOrEmpty(definition.OpcUaIssueToken) ||
                                    !context.SecurityDefinitions.TryGetValue(
                                        definition.OpcUaIssueToken!, out WotSecurityDefinition? issuerDefinition))
                                {
                                    diagnostics.Add(WotBindingDiagnostic.Error(
                                        WotBindingDiagnosticCode.InvalidFieldValue,
                                        "IssuedToken acquisition requires a declared security scheme name.",
                                        pointer + "/uav:issueToken"));
                                    return null;
                                }
                                if (Expand(definition.OpcUaIssueToken!) is null)
                                {
                                    return null;
                                }
                                issuer = WotCredentialReference.FromDefinition(issuerDefinition, BindingUri, endpoint);
                            }
                            return [new(null, null, requiredToken, null, issuer)];
                        }
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.InvalidFieldValue,
                            "The OPC UA authentication scheme requires a valid user token kind.", pointer));
                        return null;
                    }
                    if (definition.Scheme != WotSecurityScheme.OpcUaChannelSecurity)
                    {
                        return [new(null, null, null, definition.MinimumSecurity)];
                    }
                    if (!WotBindingConformance.IsSecurityMode(definition.OpcUaSecurityMode) ||
                        !WotBindingConformance.IsSecurityPolicy(definition.OpcUaSecurityPolicy))
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.InvalidFieldValue,
                            "An OPC UA channel scheme requires a valid exact security mode and policy.", pointer));
                        return null;
                    }
                    MessageSecurityMode mode = definition.OpcUaSecurityMode switch
                    {
                        "None" => MessageSecurityMode.None,
                        "Sign" => MessageSecurityMode.Sign,
                        _ => MessageSecurityMode.SignAndEncrypt
                    };
                    var channel = new WotOpcUaSecurityRequirement(
                        mode, WotOpcUaSecurityRequirement.PolicyPrefix + definition.OpcUaSecurityPolicy);
                    if (!channel.IsConsistent)
                    {
                        diagnostics.Add(WotBindingDiagnostic.Error(
                            WotBindingDiagnosticCode.ConflictingFields,
                            "Security mode None requires policy None, and a secured mode requires a secured policy.",
                            pointer));
                        return null;
                    }
                    return [channel];
                }
                finally
                {
                    active.Remove(name);
                }
            }

            List<WotOpcUaSecurityRequirement>? CombineAlternatives(
                List<WotOpcUaSecurityRequirement> left, List<WotOpcUaSecurityRequirement> right)
            {
                var combinations = new List<WotOpcUaSecurityRequirement>();
                foreach (WotOpcUaSecurityRequirement first in left)
                {
                    foreach (WotOpcUaSecurityRequirement second in right)
                    {
                        if ((first.SecurityMode.HasValue && second.SecurityMode.HasValue &&
                             first.SecurityMode != second.SecurityMode) ||
                            (first.SecurityPolicyUri is not null && second.SecurityPolicyUri is not null &&
                             first.SecurityPolicyUri != second.SecurityPolicyUri) ||
                            (first.UserIdentityToken.HasValue && second.UserIdentityToken.HasValue &&
                             first.UserIdentityToken != second.UserIdentityToken) ||
                            (first.IssueTokenReference is not null && second.IssueTokenReference is not null &&
                             first.IssueTokenReference.SchemeName != second.IssueTokenReference.SchemeName))
                        {
                            continue;
                        }
                        var combined = new WotOpcUaSecurityRequirement(
                            first.SecurityMode ?? second.SecurityMode,
                            first.SecurityPolicyUri ?? second.SecurityPolicyUri,
                            first.UserIdentityToken ?? second.UserIdentityToken,
                            second.MinimumSecurity is null ? first.MinimumSecurity :
                                Combine(first.MinimumSecurity, second.MinimumSecurity),
                            first.IssueTokenReference ?? second.IssueTokenReference);
                        if (!combined.IsConsistent)
                        {
                            continue;
                        }
                        combinations.Add(combined);
                        if (!CheckBound(combinations.Count))
                        {
                            return null;
                        }
                    }
                }
                if (combinations.Count == 0)
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.ConflictingFields,
                        "The combined OPC UA schemes have no compatible mode, policy and identity combination.",
                        form.Pointer("security")));
                    return null;
                }
                return combinations;
            }

            bool CheckBound(int count)
            {
                if (count <= context.Bounds.MaxSecurityAlternatives)
                {
                    return true;
                }
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    "The security combinations exceed the configured alternative limit.", form.Pointer("security")));
                return false;
            }
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

        private static WotEndpointDescriptor MakeOpcUaEndpoint(Uri uri, bool nodeIdInPath)
        {
            WotEndpointDescriptor endpoint = MakeEndpoint(uri);
            if (nodeIdInPath || uri.AbsolutePath is "" or "/")
            {
                return endpoint;
            }
            string address = ToTransmittedUri(uri);
            int query = address.IndexOf('?', StringComparison.Ordinal);
            int fragment = address.IndexOf('#', StringComparison.Ordinal);
            int end = query < 0 ? fragment : fragment < 0 ? query : Math.Min(query, fragment);
            if (end >= 0)
            {
                address = address.Substring(0, end);
            }
            return new WotEndpointDescriptor(
                endpoint.Scheme, endpoint.Host, endpoint.Port, address, endpoint.Metadata);
        }

        private static string? ResolveNodeId(WotAffordanceForm form, out bool nodeIdInPath)
        {
            nodeIdInPath = false;
            if (form.TryGetString("uav:id", out string id) && !string.IsNullOrEmpty(id))
            {
                return id;
            }
            if (!string.IsNullOrEmpty(form.Href) && TryParseUri(form.Href!, out Uri uri))
            {
                string query = uri.Query.TrimStart('?');
                if (query.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(query[3..]);
                }
                string path = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
                if (ExpandedNodeId.TryParse(path, out _))
                {
                    nodeIdInPath = true;
                    return path;
                }
            }
            return null;
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
