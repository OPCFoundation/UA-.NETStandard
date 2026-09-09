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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Model- and platform-vocabulary validation (WoT Binding Sections 6.1
    /// through 6.8) for the <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Validates the readable model and platform vocabulary of WoT Binding
        /// Section 6 against the per-term domain and range table of Section 7.
        /// </summary>
        /// <remarks>
        /// Most terms validated here have no distinct readable NodeSet structure
        /// in this converter, so a well-formed value is carried verbatim through
        /// the <c>uav:nodes</c> / residue mechanism (Section 9) and survives a
        /// WoT to NodeSet to WoT round-trip unchanged. Only malformed values
        /// are reported; a violation is an error because Section 7 requires a
        /// consumer to treat the document as invalid rather than repair it. The
        /// opaque terms <c>uav:metadata</c>, <c>uav:propertyConfiguration</c>,
        /// <c>uav:actionConfiguration</c> and <c>uav:eventConfiguration</c> are
        /// deliberately not validated: Section 6.7 requires them to be carried
        /// unchanged and never to cause a document to be rejected.
        /// </remarks>
        /// <param name="document">The WoT document being synthesized.</param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        private static void ValidateModelVocabulary(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            string defaultLocale = GetDocumentLocale(document);

            // Type-level terms (WoT Binding Sections 6.1, 6.4, 6.7, 6.8).
            ValidateBooleanTerm(root, "uav:isComposite", string.Empty, diagnostics);
            ValidateBooleanTerm(root, "uav:includeInherited", string.Empty, diagnostics);
            ValidateBooleanTerm(root, "uav:additionalProperties", string.Empty, diagnostics);
            ValidateAbsoluteIriTerm(root, "uav:semanticId", string.Empty, diagnostics);
            ValidateLocalizedText(root, string.Empty, defaultLocale, diagnostics);
            ValidateContains(document, root, diagnostics);
            ValidateContainedIn(document, root, diagnostics);

            // Property-level terms (WoT Binding Sections 6.4, 6.5, 6.7).
            ValidateAffordanceModelVocabulary(
                document, document.Properties, "properties", defaultLocale, diagnostics);
            ValidateAffordanceModelVocabulary(
                document, document.Actions, "actions", defaultLocale, diagnostics);
            ValidateAffordanceModelVocabulary(
                document, document.Events, "events", defaultLocale, diagnostics);
        }

        private static void ValidateAffordanceModelVocabulary(
            WotDocument document,
            IReadOnlyDictionary<string, JsonElement> affordances,
            string section,
            string defaultLocale,
            List<WotDiagnostic> diagnostics)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                JsonElement node = affordance.Value;
                if (node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string parentPointer = "/" + section + "/" + affordance.Key;
                ValidateScaleFactor(node, parentPointer, diagnostics);
                ValidateDecimalPlaces(node, parentPointer, diagnostics);
                ValidateUnitProperty(document, node, affordance.Key, parentPointer, diagnostics);
                ValidateEngineeringUnits(node, parentPointer, diagnostics);
                ValidateRanges(node, parentPointer, diagnostics);
                ValidateValueRank(node, parentPointer, diagnostics);
                ValidateLocalizedText(node, parentPointer, defaultLocale, diagnostics);
                ValidateAbsoluteIriTerm(node, "uav:semanticId", parentPointer, diagnostics);
            }
        }

        private static void ValidateBooleanTerm(
            JsonElement element,
            string term,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(term, out JsonElement value))
            {
                return;
            }
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidModelVocabularyValue,
                    $"The {term} term shall be a boolean (WoT Binding Section 6).",
                    WotLocation.FromPointer(parentPointer + "/" + term)));
            }
        }

        private static void ValidateAbsoluteIriTerm(
            JsonElement element,
            string term,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(term, out JsonElement value))
            {
                return;
            }
            string? iri = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            if (string.IsNullOrEmpty(iri) || !IsAbsoluteIri(iri!))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.NonAbsoluteIri,
                    $"The {term} term shall be an absolute IRI with a scheme " +
                    "(WoT Binding Section 6).",
                    WotLocation.FromPointer(parentPointer + "/" + term)));
            }
        }

        private static void ValidateScaleFactor(
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty("uav:scaleFactor", out JsonElement value))
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDouble(out double factor) ||
                factor == 0.0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidModelVocabularyValue,
                    "The uav:scaleFactor term shall be a non-zero number " +
                    "(WoT Binding Section 6.5).",
                    WotLocation.FromPointer(parentPointer + "/uav:scaleFactor")));
            }
        }

        private static void ValidateDecimalPlaces(
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty("uav:decimalPlaces", out JsonElement value))
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Number ||
                !IsIntegerLiteral(value) ||
                !value.TryGetInt32(out int places) ||
                places < 0)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidModelVocabularyValue,
                    "The uav:decimalPlaces term shall be an integer greater than " +
                    "or equal to zero (WoT Binding Section 6.5).",
                    WotLocation.FromPointer(parentPointer + "/uav:decimalPlaces")));
            }
        }

        /// <summary>
        /// Validates <c>uav:unitProperty</c> against the canonical pointer form
        /// of WoT Binding Sections 6.4 and 7.
        /// </summary>
        /// <remarks>
        /// The OPC UA fact the term records is an <c>EngineeringUnits</c>
        /// Property Node of its own, so it projects to a property affordance of
        /// its own and the pointer names <em>that</em> affordance. A pointer at
        /// the annotated affordance's own <c>unit</c> member names a string
        /// inside the affordance rather than a Node, and a pointer at the
        /// affordance itself names the value the unit belongs to rather than
        /// the unit.
        /// </remarks>
        private static void ValidateUnitProperty(
            WotDocument document,
            JsonElement element,
            string affordanceName,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(UnitPropertyTerm, out JsonElement value))
            {
                return;
            }
            string pointer = parentPointer + "/" + UnitPropertyTerm;
            string? declared = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            string? token = declared is not null &&
                declared.StartsWith(UnitPointerPrefix, StringComparison.Ordinal)
                ? declared.Substring(UnitPointerPrefix.Length)
                : null;
            if (token is null or { Length: 0 } ||
                token.Contains('/', StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidUnitPointer,
                    $"The {UnitPropertyTerm} term shall be a canonical RFC 6901 " +
                    "JSON Pointer of the form '/properties/<name>' (WoT Binding " +
                    "Section 6.4).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            string target = token!
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (string.Equals(target, affordanceName, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidUnitPointer,
                    $"The {UnitPropertyTerm} term names the affordance that " +
                    "carries it; the unit of a value is a sibling Property of " +
                    "that value, not the value itself (WoT Binding Section 6.4).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            if (!document.Properties.TryGetValue(target, out JsonElement sibling) ||
                sibling.ValueKind != JsonValueKind.Object ||
                !string.Equals(
                    GetElementString(sibling, "type"), "string", StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidUnitPointer,
                    $"The {UnitPropertyTerm} term shall resolve, within the same " +
                    "document, to a sibling property affordance whose DataSchema " +
                    "type is 'string' (WoT Binding Section 7).",
                    WotLocation.FromPointer(pointer)));
            }
        }

        /// <summary>
        /// Validates <c>uav:engineeringUnits</c> against WoT Binding Sections
        /// 6.4.1 and 7.
        /// </summary>
        private static void ValidateEngineeringUnits(
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            if (!element.TryGetProperty(EngineeringUnitsTerm, out JsonElement declared))
            {
                return;
            }
            if (TryReadEngineeringUnits(declared, out _))
            {
                return;
            }
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.InvalidEngineeringUnits,
                $"The {EngineeringUnitsTerm} term shall be an object carrying a " +
                "namespaceUri, an integer unitId and a displayName. A display " +
                "string alone is lossy, because the authority's machine-readable " +
                "UnitId cannot be recovered from it (WoT Binding Section 6.4.1).",
                WotLocation.FromPointer(parentPointer + "/" + EngineeringUnitsTerm)));
        }

        /// <summary>
        /// Validates the engineering and instrument ranges of WoT Binding
        /// Sections 6.4.1 and 7.
        /// </summary>
        /// <remarks>
        /// An engineering range outside what the instrument can measure is not
        /// a fact about any instrument, which is why containment is checked
        /// rather than assumed.
        /// </remarks>
        private static void ValidateRanges(
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            bool hasMinimum = element.TryGetProperty(MinimumMember, out JsonElement minimum);
            bool hasMaximum = element.TryGetProperty(MaximumMember, out JsonElement maximum);
            WotRange? euRange = null;
            if (hasMinimum &&
                hasMaximum &&
                minimum.ValueKind == JsonValueKind.Number &&
                maximum.ValueKind == JsonValueKind.Number)
            {
                if (!TryReadRangeMembers(element, out WotRange range))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidRangeValue,
                        "The minimum of a DataSchema shall not exceed its maximum; " +
                        "the two carry the OPC UA EURange of the Variable the " +
                        "affordance projects (WoT Binding Section 7).",
                        WotLocation.FromPointer(parentPointer + "/" + MinimumMember)));
                }
                else
                {
                    euRange = range;
                }
            }
            if (!element.TryGetProperty(InstrumentRangeTerm, out JsonElement declared))
            {
                return;
            }
            string pointer = parentPointer + "/" + InstrumentRangeTerm;
            if (!TryReadRangeMembers(declared, out WotRange instrument))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidRangeValue,
                    $"The {InstrumentRangeTerm} term shall be an object carrying a " +
                    "numeric minimum no greater than its numeric maximum (WoT " +
                    "Binding Section 6.4.1).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            if (euRange is { } engineering &&
                (engineering.Low < instrument.Low || engineering.High > instrument.High))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidRangeValue,
                    "The engineering range the DataSchema states is not contained " +
                    $"in its {InstrumentRangeTerm}. An engineering range outside " +
                    "what the instrument can measure is not a fact about any " +
                    "instrument (WoT Binding Section 6.4.1).",
                    WotLocation.FromPointer(pointer)));
            }
        }

        private static void ValidateContains(
            WotDocument document,
            JsonElement root,
            List<WotDiagnostic> diagnostics)
        {
            if (!root.TryGetProperty("uav:contains", out JsonElement value))
            {
                return;
            }
            if (value.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidContainment,
                    "The uav:contains term shall be an array of uav:refName strings " +
                    "(WoT Binding Section 6.3).",
                    WotLocation.FromPointer("/uav:contains")));
                return;
            }
            HashSet<string> refNames = CollectLinkRefNames(document);
            foreach (JsonElement entry in value.EnumerateArray())
            {
                string? name = entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()
                    : null;
                if (string.IsNullOrEmpty(name))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidContainment,
                        "A uav:contains entry shall be a non-empty uav:refName string " +
                        "(WoT Binding Section 6.3).",
                        WotLocation.FromPointer("/uav:contains")));
                    continue;
                }
                if (!refNames.Contains(name!))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidContainment,
                        $"The uav:contains entry '{name}' does not match any link " +
                        "uav:refName declared on the type (WoT Binding Section 6.3).",
                        WotLocation.FromPointer("/uav:contains")));
                }
            }
        }

        private static void ValidateContainedIn(
            WotDocument document,
            JsonElement root,
            List<WotDiagnostic> diagnostics)
        {
            if (!root.TryGetProperty("uav:containedIn", out JsonElement value))
            {
                return;
            }
            string? composite = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            if (string.IsNullOrEmpty(composite))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidContainment,
                    "The uav:containedIn term shall name exactly one composite type " +
                    "(WoT Binding Section 6.3).",
                    WotLocation.FromPointer("/uav:containedIn")));
                return;
            }
            string? selfName = LocalName(GetUavString(document, "browseName")) ??
                SanitizeName(document.Title);
            if (selfName is not null &&
                string.Equals(selfName, composite, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidContainment,
                    $"The uav:containedIn term names the type itself ('{composite}'); " +
                    "containment shall be acyclic (WoT Binding Section 6.3).",
                    WotLocation.FromPointer("/uav:containedIn")));
            }
        }

        private static HashSet<string> CollectLinkRefNames(WotDocument document)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement link in document.Links)
            {
                string? refName = GetElementString(link, "uav:refName");
                if (!string.IsNullOrEmpty(refName))
                {
                    names.Add(refName!);
                }
            }
            return names;
        }

        /// <summary>
        /// Determines whether a JSON number literal is an integer, that is it
        /// carries neither a fractional part nor an exponent. This is stricter
        /// than <see cref="JsonElement.TryGetInt32"/> alone so that a value such
        /// as <c>2.0</c> is rejected for <c>uav:decimalPlaces</c>.
        /// </summary>
        /// <param name="value">The number element to inspect.</param>
        /// <returns><c>true</c> when the literal is an integer.</returns>
        private static bool IsIntegerLiteral(JsonElement value)
        {
            foreach (char character in value.GetRawText())
            {
                if (character is '.' or 'e' or 'E')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether a string is an absolute IRI, that is it begins with
        /// an RFC 3986 scheme (<c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":"</c>)
        /// followed by a non-empty scheme-specific part. A prefixed name that
        /// resembles a scheme is accepted rather than risk a false rejection of a
        /// valid URN or URL, because a context prefix cannot be told apart from a
        /// registered scheme without the full scheme registry.
        /// </summary>
        /// <param name="value">The candidate IRI.</param>
        /// <returns><c>true</c> when the value carries a scheme.</returns>
        private static bool IsAbsoluteIri(string value)
        {
            if (value.Length == 0 || !IsAsciiLetter(value[0]))
            {
                return false;
            }
            for (int ii = 1; ii < value.Length; ii++)
            {
                char character = value[ii];
                if (character == ':')
                {
                    return ii + 1 < value.Length;
                }
                if (!IsAsciiLetter(character) &&
                    character is not (>= '0' and <= '9') &&
                    character is not ('+' or '-' or '.'))
                {
                    return false;
                }
            }
            return false;
        }
    }
}
