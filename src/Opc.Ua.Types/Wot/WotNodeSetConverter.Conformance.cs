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
using System.Globalization;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Validation of the document-level conformance vocabulary: the declared
    /// vocabulary revision and conformance claims (WoT Binding Section 4.1),
    /// the event select-clause list (Section 6.1), the structural bounds on
    /// opaque objects (Section 6.6) and the <c>auto</c> endpoint security floor
    /// (Section 5.7.1).
    /// </summary>
    /// <remarks>
    /// None of these terms is an OPC UA model fact, so none of them projects to
    /// a Node: a revision claim, a select-clause list and a security floor
    /// describe the document and the client's request rather than the
    /// AddressSpace. They are validated here and then carried unchanged through
    /// the residue mechanism of Section 10.2, which is what lets a round trip
    /// restate them exactly as the author wrote them.
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Validates the conformance vocabulary of a document.
        /// </summary>
        /// <remarks>
        /// The rules that apply to one member of the document root or of one
        /// affordance are checked where that member lives; the rules that
        /// depend on <em>where</em> a term appears share a single traversal of
        /// the parsed document, because the document is already in memory and
        /// walking it once per rule would read the same objects four times.
        /// </remarks>
        /// <param name="document">The WoT document being synthesized.</param>
        /// <param name="options">The converter options.</param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        private static void ValidateBindingConformance(
            WotDocument document,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            bool strict = options.ConformanceMode == WotConformanceMode.Strict;
            bool authoring = options.AuthoringValidation;
            WotDiagnosticSeverity opaqueSeverity = strict
                ? WotDiagnosticSeverity.Error
                : WotDiagnosticSeverity.Warning;
            ValidateRevisionClaim(root, authoring, diagnostics);
            ValidateConformanceClaim(root, options, strict, authoring, diagnostics);
            ValidateEventSelectClauses(document, diagnostics);
            ValidateSecurityFloors(document, diagnostics);
            ValidateReferenceTypeProjection(root, diagnostics);
            WotDiagnosticSeverity deprecatedSeverity = authoring
                ? WotDiagnosticSeverity.Error
                : WotDiagnosticSeverity.Warning;
            WalkDocument(
                root,
                string.Empty,
                (element, pointer) =>
                {
                    ValidateSelectClausePlacement(element, pointer, diagnostics);
                    ValidatePropertyTermPlacement(element, pointer, diagnostics);
                    ValidateSecurityFloorPlacement(element, pointer, diagnostics);
                    ValidateOpaqueObjects(document, element, pointer, opaqueSeverity, diagnostics);
                    ValidateNodeClassAnnotation(element, pointer, diagnostics);
                    ValidateUnitQuantityKind(
                        document, element, pointer, deprecatedSeverity, diagnostics);
                    if (strict)
                    {
                        ValidateKnownVocabulary(element, pointer, diagnostics);
                    }
                });
        }

        /// <summary>
        /// The NodeClass annotations of WoT Binding Section 5.2. A Node has
        /// exactly one NodeClass, so a Thing or affordance <c>@type</c> carries
        /// at most one of them.
        /// </summary>
        private static readonly string[] s_nodeClassAnnotations =
        [
            "uav:object",
            "uav:objectType",
            "uav:variable",
            "uav:variableType",
            "uav:method",
            "uav:eventType",
            "uav:referenceType",
            "uav:dataType"
        ];

        /// <summary>
        /// Reports an <c>@type</c> that carries more than one NodeClass
        /// annotation (WoT Binding Sections 5.2 and 7).
        /// </summary>
        /// <remarks>
        /// Two annotations state two NodeClasses for one Node, and a converter
        /// reading the second would recreate a Node the first denies. The rule
        /// grew teeth in revision 1.1, which completed the set with
        /// <c>uav:referenceType</c> and <c>uav:dataType</c>: before them a
        /// document that projected a ReferenceType had no annotation to use,
        /// and after them it has exactly one.
        /// </remarks>
        private static void ValidateNodeClassAnnotation(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty("@type", out JsonElement types))
            {
                return;
            }
            string? first = null;
            foreach (string token in EnumerateTypeTokens(types))
            {
                if (Array.IndexOf(s_nodeClassAnnotations, token) < 0)
                {
                    continue;
                }
                if (first is null)
                {
                    first = token;
                    continue;
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NodeClassAnnotationConflict,
                    $"The @type carries both '{first}' and '{token}'. A Node has exactly one " +
                    "NodeClass, so a Thing or affordance carries at most one NodeClass " +
                    "annotation (WoT Binding Sections 5.2 and 7).",
                    WotLocation.FromPointer(
                        (pointer.Length == 0 ? string.Empty : pointer) + "/@type")));
                return;
            }
        }

        /// <summary>
        /// Validates <c>uav:inverseName</c> and <c>uav:symmetric</c>
        /// (WoT Binding Sections 6.2.1 and 7).
        /// </summary>
        /// <remarks>
        /// The two carry the ReferenceType Attributes no link <c>rel</c>
        /// states, so they belong to a document that projects a ReferenceType
        /// Node and to no other. A symmetric ReferenceType reads the same in
        /// both directions, so a second name would state a direction that does
        /// not exist.
        /// </remarks>
        private static void ValidateReferenceTypeProjection(
            JsonElement root,
            List<WotDiagnostic> diagnostics)
        {
            bool hasInverseName = root.TryGetProperty(InverseNameTerm, out JsonElement inverseName);
            bool hasSymmetric = root.TryGetProperty(SymmetricTerm, out JsonElement symmetric);
            if (!hasInverseName && !hasSymmetric)
            {
                return;
            }
            bool projectsReferenceType = false;
            if (root.TryGetProperty("@type", out JsonElement types))
            {
                foreach (string token in EnumerateTypeTokens(types))
                {
                    if (string.Equals(token, "uav:referenceType", StringComparison.Ordinal))
                    {
                        projectsReferenceType = true;
                        break;
                    }
                }
            }
            if (!projectsReferenceType)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeProjectionInvalid,
                    $"The {(hasInverseName ? InverseNameTerm : SymmetricTerm)} term appears on a " +
                    "document whose @type does not carry 'uav:referenceType'. Both terms carry " +
                    "ReferenceType Attributes, so they belong to a document that projects a " +
                    "ReferenceType Node (WoT Binding Section 6.2.1).",
                    WotLocation.FromPointer(
                        "/" + (hasInverseName ? InverseNameTerm : SymmetricTerm))));
                return;
            }
            if (hasInverseName &&
                (inverseName.ValueKind != JsonValueKind.String ||
                    string.IsNullOrEmpty(inverseName.GetString())))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeProjectionInvalid,
                    $"The {InverseNameTerm} term shall be a non-empty unqualified name in the " +
                    "document's default locale (WoT Binding Section 6.2.1).",
                    WotLocation.FromPointer("/" + InverseNameTerm)));
            }
            if (hasSymmetric && symmetric.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeProjectionInvalid,
                    $"The {SymmetricTerm} term shall be a boolean; it carries the OPC 10000-3 " +
                    "Symmetric Attribute and defaults to false where it is absent " +
                    "(WoT Binding Section 6.2.1).",
                    WotLocation.FromPointer("/" + SymmetricTerm)));
                return;
            }
            if (hasSymmetric && symmetric.ValueKind == JsonValueKind.True && hasInverseName)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ReferenceTypeProjectionInvalid,
                    $"A document that sets {SymmetricTerm} to true shall not carry " +
                    $"{InverseNameTerm}: a symmetric Reference reads the same in both " +
                    "directions, so a second name states a direction that does not exist " +
                    "(WoT Binding Section 6.2.1).",
                    WotLocation.FromPointer("/" + InverseNameTerm)));
            }
        }

        /// <summary>
        /// Reports a DataSchema <c>unit</c> that carries a quantity kind rather
        /// than an engineering unit (WoT Binding Sections 6.4 and 7).
        /// </summary>
        /// <remarks>
        /// A quantity kind says what physical quantity is measured; a unit says
        /// what the measurement is counted in, and neither is recoverable from
        /// the other. Revision 1.0 drew no line, so a consumer reports the value
        /// as deprecated and preserves it rather than rejecting the document,
        /// and never invents an engineering unit in its place; strict authoring
        /// against revision 1.1 rejects it.
        /// <see cref="WotUnitMigration"/> is the helper that moves such a value
        /// to <c>qudt:hasQuantityKind</c>, where the author meant it to be.
        /// </remarks>
        private static void ValidateUnitQuantityKind(
            WotDocument document,
            JsonElement element,
            string pointer,
            WotDiagnosticSeverity severity,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(UnitMember, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { Length: > 0 } unit ||
                !WotUnitMigration.IsQuantityKind(
                    unit,
                    prefix => TryGetContextNamespace(document, prefix, out string ns) ? ns : null))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                severity,
                WotDiagnosticCode.QuantityKindInUnit,
                $"The unit member carries the quantity kind '{unit}'. A unit says what a " +
                "measurement is counted in and a quantity kind says what is measured, so a " +
                $"quantity kind belongs in '{WotUnitMigration.QuantityKindTerm}' " +
                "(WoT Binding Section 6.4). Revision 1.0 permitted it, so the value is " +
                "preserved and reported as deprecated rather than reinterpreted; no " +
                "engineering unit is invented in its place.",
                WotLocation.FromPointer(
                    (pointer.Length == 0 ? string.Empty : pointer) + "/" + UnitMember)));
        }

        /// <summary>
        /// Enumerates the tokens of a <c>@type</c> member, which W3C Thing
        /// Description 1.1 writes as either one string or an array of them.
        /// </summary>
        private static IEnumerable<string> EnumerateTypeTokens(JsonElement types)
        {
            if (types.ValueKind == JsonValueKind.String)
            {
                if (types.GetString() is { Length: > 0 } single)
                {
                    yield return single;
                }
                yield break;
            }
            if (types.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }
            foreach (JsonElement item in types.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    item.GetString() is { Length: > 0 } token)
                {
                    yield return token;
                }
            }
        }

        /// <summary>
        /// Validates <c>uav:bindingVersion</c> (WoT Binding Sections 4.1 and 7).
        /// </summary>
        /// <remarks>
        /// The syntactic rule is what a consumer enforces, and it enforces it
        /// always: a value that is not <c>&lt;major&gt;.&lt;minor&gt;</c> is
        /// malformed and is an error in every mode. A well-formed revision this
        /// document does not publish is a different finding - it is
        /// <em>unsupported</em>, reported as a warning and preserved unchanged,
        /// because Section 4.1 states that a consumer <em>shall not</em> reject
        /// a document for that reason alone. An authoring validator, which
        /// checks a document written against a published revision, reports the
        /// same finding as an error: an author either named a published
        /// revision or made a mistake.
        /// </remarks>
        private static void ValidateRevisionClaim(
            JsonElement root,
            bool authoring,
            List<WotDiagnostic> diagnostics)
        {
            if (!root.TryGetProperty(WotBindingConformance.BindingVersionTerm, out JsonElement value))
            {
                return;
            }
            string pointer = "/" + WotBindingConformance.BindingVersionTerm;
            string? revision = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!WotBindingConformance.IsWellFormedRevision(revision))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidBindingVersion,
                    $"The {WotBindingConformance.BindingVersionTerm} term shall be a " +
                    "<major>.<minor> revision string (WoT Binding Section 4.1).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            if (!WotBindingConformance.IsSupportedRevision(revision))
            {
                diagnostics.Add(new WotDiagnostic(
                    authoring ? WotDiagnosticSeverity.Error : WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.UnsupportedBindingRevision,
                    $"The document declares vocabulary revision '{revision}'; this Binding " +
                    $"publishes {WotBindingConformance.PreviousRevision} and " +
                    $"{WotBindingConformance.CurrentRevision}. A consumer reports the value as " +
                    "unsupported, processes the terms it knows and preserves the claim " +
                    "unchanged; an authoring validator rejects it (WoT Binding Section 4.1).",
                    WotLocation.FromPointer(pointer)));
            }
        }

        /// <summary>
        /// Validates <c>uav:profile</c> (WoT Binding Sections 4.1, 7 and 11).
        /// </summary>
        private static void ValidateConformanceClaim(
            JsonElement root,
            WotNodeSetConverterOptions options,
            bool strict,
            bool authoring,
            List<WotDiagnostic> diagnostics)
        {
            string pointer = "/" + WotBindingConformance.ProfileTerm;
            var claimed = new List<string>();
            if (root.TryGetProperty(WotBindingConformance.ProfileTerm, out JsonElement value))
            {
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidConformanceClaim,
                        $"The {WotBindingConformance.ProfileTerm} term shall be a non-empty " +
                        "array of conformance unit and profile names " +
                        "(WoT Binding Sections 4.1 and 11).",
                        WotLocation.FromPointer(pointer)));
                    return;
                }
                int index = 0;
                foreach (JsonElement entry in value.EnumerateArray())
                {
                    string entryPointer = pointer + "/" +
                        index.ToString(CultureInfo.InvariantCulture);
                    index++;
                    string? name = entry.ValueKind == JsonValueKind.String ? entry.GetString() : null;
                    if (!WotBindingConformance.IsWellFormedConformanceName(name))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.InvalidConformanceClaim,
                            $"'{name}' is not a well-formed conformance claim; a claim is a " +
                            "non-empty 'WoT-<name>' string (WoT Binding Sections 4.1 and 11).",
                            WotLocation.FromPointer(entryPointer)));
                        continue;
                    }
                    if (claimed.Contains(name!))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.InvalidConformanceClaim,
                            $"The claim '{name}' appears twice.",
                            WotLocation.FromPointer(entryPointer)));
                        continue;
                    }
                    claimed.Add(name!);
                    if (!WotBindingConformance.IsConformanceName(name))
                    {
                        diagnostics.Add(new WotDiagnostic(
                            authoring
                                ? WotDiagnosticSeverity.Error
                                : WotDiagnosticSeverity.Warning,
                            WotDiagnosticCode.UnrecognizedConformanceClaim,
                            $"'{name}' is not a conformance unit or profile WoT Binding " +
                            "Section 11 defines. A consumer reports it as unrecognized and " +
                            "preserves it, because a later revision defines further units; an " +
                            "authoring validator rejects it (WoT Binding Section 4.1).",
                            WotLocation.FromPointer(entryPointer)));
                    }
                }
            }

            if (!strict || options.RequiredConformance.Count == 0)
            {
                return;
            }
            foreach (string required in options.RequiredConformance)
            {
                if (!WotBindingConformance.ClaimsSatisfy(claimed, required))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidConformanceClaim,
                        $"The document does not claim '{required}', which the caller requires. " +
                        $"A {WotBindingConformance.ProfileTerm} entry naming a profile claims " +
                        "every unit that profile names (WoT Binding Section 11).",
                        WotLocation.FromPointer(pointer)));
                }
            }
        }

        /// <summary>
        /// Validates <c>uav:eventSelectClauses</c> (WoT Binding Sections 6.1
        /// and 7): its placement, its shape and the portability of every
        /// identifier and path element it carries.
        /// </summary>
        private static void ValidateEventSelectClauses(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object ||
                    !affordance.Value.TryGetProperty(
                        WotEventSelectClauses.Term, out JsonElement value))
                {
                    continue;
                }
                string pointer = "/events/" + EscapeJsonPointerToken(affordance.Key) + "/" +
                    WotEventSelectClauses.Term;
                if (!WotEventSelectClauses.TryParse(
                    value,
                    prefix => TryGetContextNamespace(document, prefix, out string ns) ? ns : null,
                    out ArrayOf<WotEventSelectClause> clauses,
                    out string error,
                    out int errorIndex))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.EventSelectClauseInvalid,
                        error,
                        WotLocation.FromPointer(errorIndex < 0
                            ? pointer
                            : pointer + "/" + errorIndex.ToString(CultureInfo.InvariantCulture))));
                    continue;
                }
                for (int ii = 0; ii < clauses.Count; ii++)
                {
                    ValidateSelectClauseIdentity(
                        clauses[ii],
                        pointer + "/" + ii.ToString(CultureInfo.InvariantCulture),
                        diagnostics);
                }
            }

            // The term belongs only directly on an event affordance: at the
            // document root, on another affordance kind or nested inside a
            // form, a link or a data schema it selects nothing, and a consumer
            // that silently ignored it would subscribe with a field list the
            // author never asked for. That placement rule is checked by the
            // shared traversal in ValidateSelectClausePlacement.
        }

        /// <summary>
        /// Reports a property-affordance term that does not sit directly on a
        /// property affordance (WoT Binding Sections 6.4, 6.4.1 and 7).
        /// </summary>
        /// <remarks>
        /// The five terms describe the Variable an affordance projects: its
        /// EngineeringUnits Property, the instrument range that Property is
        /// bounded by, the sibling affordance that carries it, and the display
        /// scaling a Client applies to its value. A member of an action's
        /// <c>input</c>, of an event's <c>data</c>, or of a property
        /// affordance's own DataSchema is a field of a payload, not a Variable,
        /// so none of that exists for it. Deciding the role by the member's
        /// position rather than by the name of the map it sits in is what stops
        /// a <c>properties</c> map that is a DataSchema from being read as an
        /// affordance map.
        /// </remarks>
        private static void ValidatePropertyTermPlacement(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (IsPropertyAffordancePointer(pointer))
            {
                return;
            }
            foreach (string term in s_propertyAffordanceTerms)
            {
                if (!element.TryGetProperty(term, out _))
                {
                    continue;
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidModelVocabularyValue,
                    $"The {term} term appears at " +
                    $"'{(pointer.Length == 0 ? "/" : pointer)}'. It describes the Variable " +
                    "a property affordance projects and belongs only directly on a member " +
                    "of the root 'properties' map (WoT Binding Sections 6.4, 6.4.1 and 7).",
                    WotLocation.FromPointer(
                        (pointer.Length == 0 ? string.Empty : pointer) + "/" +
                        EscapeJsonPointerToken(term))));
            }
        }

        /// <summary>
        /// The terms that describe the Variable a property affordance projects,
        /// and therefore say nothing anywhere else.
        /// </summary>
        private static readonly string[] s_propertyAffordanceTerms =
        [
            EngineeringUnitsTerm,
            UnitPropertyTerm,
            InstrumentRangeTerm,
            "uav:scaleFactor",
            "uav:decimalPlaces"
        ];

        /// <summary>
        /// Reports a <c>uav:eventSelectClauses</c> term that does not sit
        /// directly on an event affordance (WoT Binding Sections 6.1 and 7).
        /// </summary>
        private static void ValidateSelectClausePlacement(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(WotEventSelectClauses.Term, out _) ||
                IsEventAffordancePointer(pointer))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.EventSelectClauseInvalid,
                $"The {WotEventSelectClauses.Term} term appears at " +
                $"'{(pointer.Length == 0 ? "/" : pointer)}'. It belongs only directly " +
                "on an event affordance (WoT Binding Sections 6.1 and 7).",
                WotLocation.FromPointer(
                    (pointer.Length == 0 ? string.Empty : pointer) + "/" +
                    WotEventSelectClauses.Term)));
        }

        private static void ValidateSelectClauseIdentity(
            WotEventSelectClause clause,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (clause.BrowsePath.Length == 0)
            {
                return;
            }
            foreach (string element in clause.PathElements)
            {
                if (StartsWithNumericPrefix(element))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.NonPortableQualifiedName,
                        $"The select-clause browse path element '{element}' uses a numeric " +
                        "namespace index; a readable path element uses a context-bound prefix " +
                        "or NamespaceUri qualification (WoT Binding Sections 5.1.3 and 7).",
                        WotLocation.FromPointer(pointer + "/" +
                            WotEventSelectClauses.BrowsePathTerm)));
                    return;
                }
            }
        }

        /// <summary>
        /// Validates the four opaque members against the structural rules of
        /// WoT Binding Section 6.6.
        /// </summary>
        /// <remarks>
        /// The contents stay opaque; the shape does not. A consumer that must
        /// carry a value unchanged and must not reject it is otherwise obliged
        /// to carry an unbounded, unattributable value. Revision 1.0 stated no
        /// key rule, so a document whose top-level keys are not namespaced is
        /// reported as deprecated and preserved rather than rejected, exactly
        /// as Section 6.6 requires; strict conformance turns the same finding
        /// into an error so an authoring tool sees it.
        /// </remarks>
        private static void ValidateOpaqueObjects(
            WotDocument document,
            JsonElement element,
            string pointer,
            WotDiagnosticSeverity severity,
            List<WotDiagnostic> diagnostics)
        {
            foreach (string member in WotBindingConformance.OpaqueMembers)
            {
                if (element.TryGetProperty(member, out JsonElement value))
                {
                    ValidateOpaqueObject(
                        document,
                        value,
                        pointer + "/" + member,
                        member,
                        severity,
                        diagnostics);
                }
            }
        }

        private static void ValidateOpaqueObject(
            WotDocument document,
            JsonElement value,
            string pointer,
            string member,
            WotDiagnosticSeverity severity,
            List<WotDiagnostic> diagnostics)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} term shall be a JSON object (WoT Binding Section 6.6).",
                    WotLocation.FromPointer(pointer)));
                return;
            }

            int keys = 0;
            foreach (JsonProperty key in value.EnumerateObject())
            {
                keys++;
                ValidateOpaqueKey(document, key.Name, pointer, member, severity, diagnostics);
            }
            if (keys > WotBindingConformance.OpaqueMaxTopLevelKeys)
            {
                diagnostics.Add(new WotDiagnostic(
                    severity,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} object carries {keys} top-level keys; the bound is " +
                    $"{WotBindingConformance.OpaqueMaxTopLevelKeys} (WoT Binding Section 6.6).",
                    WotLocation.FromPointer(pointer)));
            }

            long octets = MeasureCompactOctets(value);
            if (octets > WotBindingConformance.OpaqueMaxOctets)
            {
                diagnostics.Add(new WotDiagnostic(
                    severity,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} object measures {octets} octets in the compact received " +
                    $"form of WoT Binding Annex G.4; the bound is " +
                    $"{WotBindingConformance.OpaqueMaxOctets} (Section 6.6).",
                    WotLocation.FromPointer(pointer)));
            }

            int depth = MeasureJsonDepth(value);
            if (depth > WotBindingConformance.OpaqueMaxDepth)
            {
                diagnostics.Add(new WotDiagnostic(
                    severity,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} object nests {depth} levels deep; the bound is " +
                    $"{WotBindingConformance.OpaqueMaxDepth} (WoT Binding Section 6.6).",
                    WotLocation.FromPointer(pointer)));
            }
        }

        private static void ValidateOpaqueKey(
            WotDocument document,
            string key,
            string pointer,
            string member,
            WotDiagnosticSeverity severity,
            List<WotDiagnostic> diagnostics)
        {
            // JSON-LD reads 'prefix:name' as a compact IRI when the prefix is
            // bound and as an absolute IRI otherwise, so the two are told apart
            // the same way here.
            if (key.Contains("://", StringComparison.Ordinal) ||
                key.StartsWith("urn:", StringComparison.Ordinal) ||
                key.StartsWith("tag:", StringComparison.Ordinal) ||
                key.StartsWith("did:", StringComparison.Ordinal))
            {
                return;
            }
            int separator = key.IndexOf(':', 0);
            string? prefix = separator > 0 && separator + 1 < key.Length
                ? key.Substring(0, separator)
                : null;
            if (prefix is null || !IsContextPrefix(prefix))
            {
                diagnostics.Add(new WotDiagnostic(
                    severity,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} top-level key '{key}' is neither an absolute IRI nor a " +
                    "compact IRI; an opaque key names its owner (WoT Binding Section 6.6). " +
                    "Revision 1.0 stated no key rule, so the key is preserved unchanged.",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            if (!TryGetContextNamespace(document, prefix, out _))
            {
                diagnostics.Add(new WotDiagnostic(
                    severity,
                    WotDiagnosticCode.OpaqueObjectInvalid,
                    $"The {member} top-level key '{key}' uses the prefix '{prefix}', which the " +
                    "document's @context does not bind (WoT Binding Section 6.6). The key is " +
                    "preserved unchanged.",
                    WotLocation.FromPointer(pointer)));
            }
        }

        /// <summary>
        /// Validates <c>uav:minimumSecurity</c> (WoT Binding Sections 5.7.1
        /// and 7).
        /// </summary>
        private static void ValidateSecurityFloors(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> definition in document.SecurityDefinitions)
            {
                if (definition.Value.ValueKind != JsonValueKind.Object ||
                    !definition.Value.TryGetProperty(
                        WotBindingConformance.MinimumSecurityTerm, out JsonElement floor))
                {
                    continue;
                }
                string pointer = "/securityDefinitions/" +
                    EscapeJsonPointerToken(definition.Key) + "/" +
                    WotBindingConformance.MinimumSecurityTerm;
                string? scheme = GetElementString(definition.Value, "scheme");
                if (!string.Equals(
                    scheme,
                    WotBindingConformance.AutoSecurityScheme,
                    StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidSecurityFloor,
                        $"The {WotBindingConformance.MinimumSecurityTerm} term is carried by a " +
                        $"'{scheme ?? "(none)"}' scheme; it belongs only on an 'auto' scheme, " +
                        "which is the only scheme that leaves the choice to the client " +
                        "(WoT Binding Section 5.7.1).",
                        WotLocation.FromPointer(pointer)));
                    continue;
                }
                if (!WotSecurityFloor.TryParse(floor, out _, out string error))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidSecurityFloor,
                        error,
                        WotLocation.FromPointer(pointer)));
                }
            }
        }

        /// <summary>
        /// Reports a <c>uav:minimumSecurity</c> term that does not sit on a
        /// security scheme definition (WoT Binding Sections 5.7.1 and 7).
        /// </summary>
        private static void ValidateSecurityFloorPlacement(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(WotBindingConformance.MinimumSecurityTerm, out _) ||
                IsSecuritySchemePointer(pointer))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.InvalidSecurityFloor,
                $"The {WotBindingConformance.MinimumSecurityTerm} term appears at " +
                $"'{(pointer.Length == 0 ? "/" : pointer)}'. It belongs only on an " +
                "'auto' security scheme definition (WoT Binding Sections 5.7.1 and 7).",
                WotLocation.FromPointer(
                    (pointer.Length == 0 ? string.Empty : pointer) + "/" +
                    WotBindingConformance.MinimumSecurityTerm)));
        }

        /// <summary>
        /// Reports every <c>uav:</c> member, type annotation and link relation
        /// the implemented revision does not define (WoT Binding Sections 4.1
        /// and 7). Strict conformance only.
        /// </summary>
        private static void ValidateKnownVocabulary(
            JsonElement element,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            foreach (JsonProperty member in element.EnumerateObject())
            {
                if (member.Name.StartsWith(WotDocument.UavPrefix, StringComparison.Ordinal) &&
                    !WotBindingConformance.IsKnownTerm(member.Name))
                {
                    ReportUnknownTerm(
                        member.Name,
                        pointer + "/" + EscapeJsonPointerToken(member.Name),
                        diagnostics);
                    continue;
                }
                if (string.Equals(member.Name, "@type", StringComparison.Ordinal) ||
                    string.Equals(member.Name, "rel", StringComparison.Ordinal) ||
                    string.Equals(member.Name, "scheme", StringComparison.Ordinal))
                {
                    ValidateUnknownVocabularyValue(
                        member.Value,
                        pointer + "/" + member.Name,
                        diagnostics);
                }
            }
        }

        private static void ValidateUnknownVocabularyValue(
            JsonElement value,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                string? token = value.GetString();
                if (token is not null &&
                    token.StartsWith(WotDocument.UavPrefix, StringComparison.Ordinal) &&
                    !WotBindingConformance.IsKnownTerm(token))
                {
                    ReportUnknownTerm(token, pointer, diagnostics);
                }
                return;
            }
            if (value.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateUnknownVocabularyValue(
                    item,
                    pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                    diagnostics);
                index++;
            }
        }

        private static void ReportUnknownTerm(
            string term,
            string pointer,
            List<WotDiagnostic> diagnostics)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.UnknownVocabularyTerm,
                $"'{term}' is not a term of WoT Binding revision " +
                $"{WotBindingConformance.CurrentRevision}. Permissive processing carries an " +
                "unknown member unchanged as residue; strict conformance reports it, because a " +
                "term added by a later revision and a misspelled term look identical.",
                WotLocation.FromPointer(pointer)));
        }

        /// <summary>
        /// Visits every JSON object of a document once, in document order,
        /// handing each its RFC 6901 JSON Pointer.
        /// </summary>
        /// <remarks>
        /// The two structured projections are skipped whole. Inside
        /// <c>uav:nodes</c> and <c>uav:nodeSet</c> the member names are the
        /// grammar of those objects rather than terms of this vocabulary, and
        /// their identifiers are resolved through their own namespace tables
        /// (Sections 10.1 and 10.3). The <c>@context</c> is skipped for the
        /// same reason: its members are prefix bindings, not vocabulary.
        /// </remarks>
        private static void WalkDocument(
            JsonElement element,
            string pointer,
            Action<JsonElement, string> visit)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                visit(element, pointer);
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (member.Name is "@context" or "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    if (IsOpaqueMember(member.Name))
                    {
                        // The contents of an opaque object are the vendor's own
                        // structure: this Binding bounds the shape and never
                        // reads what is inside.
                        continue;
                    }
                    WalkDocument(
                        member.Value,
                        pointer + "/" + EscapeJsonPointerToken(member.Name),
                        visit);
                }
                return;
            }
            if (element.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                WalkDocument(
                    item,
                    pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                    visit);
                index++;
            }
        }

        private static bool IsOpaqueMember(string name)
        {
            foreach (string member in WotBindingConformance.OpaqueMembers)
            {
                if (string.Equals(member, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether a JSON Pointer locates an event affordance, that
        /// is a member of some <c>events</c> map.
        /// </summary>
        /// <remarks>
        /// An <c>events</c> map is an affordance map wherever it appears, not
        /// only at the document root: a link that carries a collection of event
        /// affordances carries event affordances. So the test is on the last
        /// two tokens rather than on the whole pointer - which is also what
        /// keeps the permission and the prohibition routing at the same places.
        /// A rule that admitted a nested map while forbidding a clause inside
        /// it would make one document simultaneously valid and invalid.
        /// </remarks>
        private static bool IsEventAffordancePointer(string pointer)
        {
            return EndsWithMapMemberToken(pointer, "events");
        }

        /// <summary>
        /// Determines whether a JSON Pointer locates a property affordance,
        /// that is a member of the <em>root</em> <c>properties</c> map.
        /// </summary>
        /// <remarks>
        /// Unlike <c>events</c>, this role is decided once and at the root. A
        /// <c>properties</c> map deeper in the document is the member map of a
        /// DataSchema - the fields of an action's input or of an event's
        /// notification - and a field of a payload is not a Variable that
        /// carries an <c>EngineeringUnits</c> Property.
        /// </remarks>
        private static bool IsPropertyAffordancePointer(string pointer)
        {
            return IsTwoTokenPointer(pointer, "properties");
        }

        /// <summary>
        /// Determines whether a JSON Pointer's last two tokens are the named
        /// map and one member of it.
        /// </summary>
        private static bool EndsWithMapMemberToken(string pointer, string map)
        {
            int lastSeparator = pointer.LastIndexOf('/');
            if (lastSeparator <= 0)
            {
                return false;
            }
            int previousSeparator = pointer.LastIndexOf('/', lastSeparator - 1);
            int start = previousSeparator + 1;
            // A member name containing '/' is escaped as '~1' by the walker, so
            // a separator is always a pointer-token boundary.
            return lastSeparator - start == map.Length &&
                string.CompareOrdinal(pointer, start, map, 0, map.Length) == 0;
        }

        /// <summary>
        /// Determines whether a JSON Pointer locates a security scheme
        /// definition, that is <c>/securityDefinitions/&lt;name&gt;</c> exactly.
        /// </summary>
        private static bool IsSecuritySchemePointer(string pointer)
        {
            return IsTwoTokenPointer(pointer, "securityDefinitions");
        }

        private static bool IsTwoTokenPointer(string pointer, string first)
        {
            if (pointer.Length < first.Length + 3 ||
                pointer[0] != '/' ||
                string.CompareOrdinal(pointer, 1, first, 0, first.Length) != 0 ||
                pointer[first.Length + 1] != '/')
            {
                return false;
            }
            // A member name containing '/' is escaped as '~1' by the walker, so
            // a further separator can only be a further pointer token.
            return pointer.IndexOf('/', first.Length + 2) < 0;
        }

        private static bool IsContextPrefix(string prefix)
        {
            if (prefix.Length == 0 ||
                (!char.IsLetter(prefix[0]) && prefix[0] != '_'))
            {
                return false;
            }
            for (int ii = 1; ii < prefix.Length; ii++)
            {
                char c = prefix[ii];
                if (!char.IsLetterOrDigit(c) && c is not ('.' or '_' or '-'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StartsWithNumericPrefix(string element)
        {
            int index = 0;
            while (index < element.Length && element[index] is >= '0' and <= '9')
            {
                index++;
            }
            return index > 0 && index < element.Length && element[index] == ':';
        }

        /// <summary>
        /// Measures a value in the compact received form of WoT Binding
        /// Annex G.4, which is the form the size bound of Section 6.6 is
        /// stated in.
        /// </summary>
        private static long MeasureCompactOctets(JsonElement value)
        {
            return WotDocument.MeasureCompactUtf8(value);
        }

        private static int MeasureJsonDepth(JsonElement value, int depth = 1)
        {
            int deepest = depth;
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in value.EnumerateObject())
                {
                    int child = MeasureJsonDepth(member.Value, depth + 1);
                    if (child > deepest)
                    {
                        deepest = child;
                    }
                }
                return deepest;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    int child = MeasureJsonDepth(item, depth + 1);
                    if (child > deepest)
                    {
                        deepest = child;
                    }
                }
            }
            return deepest;
        }
    }
}
