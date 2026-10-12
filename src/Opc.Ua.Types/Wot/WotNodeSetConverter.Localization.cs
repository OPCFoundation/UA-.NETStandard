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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The localized-text mapping of WoT Binding Section 9.1.1.
    /// </summary>
    /// <remarks>
    /// OPC UA <c>DisplayName</c> and <c>Description</c> are
    /// <c>LocalizedText</c>, and a Node may carry a translation of each per
    /// locale. W3C Thing Description 1.1 already defines the plural members
    /// <c>titles</c> and <c>descriptions</c>, so no term is added: a converter
    /// writes the singular member as the default-locale projection and the
    /// plural member alongside it, and every locale of the source survives.
    /// <para>
    /// The two are only safe to state together while they agree, so the
    /// singular member is the plural member's default-locale entry wherever the
    /// source has one. Where it does not - a Node authored in the plant's
    /// language and read against another default locale - the plural member is
    /// still written in full and the singular member carries the
    /// code-point-first entry as a display fallback that asserts no locale.
    /// Section 9.1.1 states it that way on purpose: requiring the default
    /// locale would make the commonest real NodeSet unrepresentable in the
    /// readable mapping and push an ordinary document into the exceptional
    /// native projection to say something the plural member already says.
    /// </para>
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The locale a document is read in when its <c>@context</c> declares
        /// none (WoT Binding Section 9.1.1).
        /// </summary>
        internal const string FallbackLocale = "en";

        /// <summary>
        /// The W3C Thing Description members carrying localized text.
        /// </summary>
        internal const string TitleMember = "title";

        /// <inheritdoc cref="TitleMember"/>
        internal const string TitlesMember = "titles";

        /// <inheritdoc cref="TitleMember"/>
        internal const string DescriptionMember = "description";

        /// <inheritdoc cref="TitleMember"/>
        internal const string DescriptionsMember = "descriptions";

        private static void WriteLocalizedTextContext(
            Utf8JsonWriter writer,
            Export.LocalizedText[]? displayName,
            Export.LocalizedText[]? description,
            string defaultLocale,
            bool inheritedLanguageMayDiffer = false)
        {
            bool titleOverride = (inheritedLanguageMayDiffer && FirstText(displayName) is not null) ||
                NeedsLocalizedTextOverride(displayName, defaultLocale, FallbackLocale);
            bool descriptionOverride = (inheritedLanguageMayDiffer && FirstText(description) is not null) ||
                NeedsLocalizedTextOverride(description, defaultLocale, FallbackLocale);
            if (titleOverride || descriptionOverride)
            {
                writer.WritePropertyName("@context");
                WriteLocalizedTextOverride(
                    writer, titleOverride, descriptionOverride,
                    ProjectedTextLocale(displayName, defaultLocale), ProjectedTextLocale(description, defaultLocale));
            }
        }

        private static bool NeedsLocalizedTextOverride(
            Export.LocalizedText[]? texts, string defaultLocale, string? inheritedLanguage)
        {
            return FirstText(texts) is not null &&
                (LacksDefaultLocale(texts, defaultLocale) ||
                    ProjectedTextLocale(texts, defaultLocale) != inheritedLanguage);
        }

        private static string? ProjectedTextLocale(Export.LocalizedText[]? texts, string defaultLocale)
        {
            foreach (Export.LocalizedText text in texts ?? [])
            {
                if (!string.IsNullOrEmpty(text.Value) &&
                    (string.IsNullOrEmpty(text.Locale) || text.Locale == defaultLocale))
                {
                    return string.IsNullOrEmpty(text.Locale) ? null : text.Locale;
                }
            }
            return null;
        }

        private static bool LacksDefaultLocale(
            Export.LocalizedText[]? texts, string defaultLocale)
        {
            List<KeyValuePair<string, string>> entries =
                CollectLocales(texts, defaultLocale);
            if (entries.Count == 0)
            {
                return false;
            }
            // A locale-free NodeSet text is the document's own language by
            // definition, and CollectLocales has already keyed it that way, so
            // what remains is text every entry of which states some other
            // language. That is the text the singular member falls back to, and
            // the one a context declaring @language would mis-tag.
            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (string.Equals(entry.Key, defaultLocale, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string? SelectDocumentLocale(UANode? root)
        {
            return FirstLocale(root?.DisplayName) ?? FirstLocale(root?.Description);
        }

        /// <summary>
        /// Gets display-selection metadata that the root's native text does not encode.
        /// </summary>
        internal static bool TryGetUnrepresentedDocumentLocale(
            WotDocument document, UANodeSet nodeSet, out string? locale)
        {
            locale = GetDeclaredLocale(document);
            return (locale is not null || HasLocalizedMaps(document.RootElement)) &&
                EffectiveLocale(locale) != EffectiveLocale(SelectDocumentLocale(SelectRootNode(nodeSet)));
        }

        private static bool HasLocalizedMaps(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (MapsLocalizedText(element, TitlesMember) ||
                    MapsLocalizedText(element, DescriptionsMember) ||
                    MapsLocalizedText(element, "displayNames"))
                {
                    return true;
                }
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    if (!WotDocument.IsSemanticBoundary(member.Name) && HasLocalizedMaps(member.Value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (HasLocalizedMaps(item))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the effective default locale of a generated document.
        /// </summary>
        private static string EffectiveLocale(string? declared)
        {
            return string.IsNullOrEmpty(declared) ? FallbackLocale : declared!;
        }

        /// <summary>
        /// Gets the default locale a document declares through the
        /// <c>@language</c> of its <c>@context</c>, or <c>null</c> where it
        /// declares none (WoT Binding Section 9.1.1).
        /// </summary>
        /// <remarks>
        /// Declaring nothing and declaring <c>en</c> are different facts even
        /// though Section 9.1.1 reads both against <c>en</c>: the first leaves
        /// the Nodes' <c>LocalizedText</c> without a locale tag, which is what
        /// a UANodeSet writes when it names one language and does not say
        /// which, and the second states the tag the Nodes carry.
        /// </remarks>
        internal static string? GetDeclaredLocale(
            WotDocument document, JsonElement carryingNode = default, string? term = null)
        {
            if (term is not null &&
                document.TryGetContextTerm(term, out JsonElement definition, carryingNode) &&
                definition.ValueKind == JsonValueKind.Object &&
                definition.TryGetProperty("@language", out JsonElement localLanguage))
            {
                return localLanguage.ValueKind == JsonValueKind.String
                    ? localLanguage.GetString()
                    : null;
            }
            return document.TryGetContextPrefix("@language", out string language, carryingNode) &&
                language.Length > 0
                ? language
                : null;
        }

        /// <summary>
        /// Gets the effective default locale of a document, which is <c>en</c>
        /// where it declares none (WoT Binding Section 9.1.1).
        /// </summary>
        private static string GetDocumentLocale(WotDocument document, JsonElement carryingNode = default)
        {
            return EffectiveLocale(GetDeclaredLocale(document, carryingNode));
        }

        /// <summary>
        /// Writes a Node's <c>DisplayName</c> as <c>title</c> and carries
        /// non-default translations in <c>titles</c>.
        /// </summary>
        private static void WriteLocalizedTitle(
            Utf8JsonWriter writer,
            Export.LocalizedText[]? displayName,
            string defaultLocale,
            string? fallback = null)
        {
            if (displayName is null || FirstText(displayName) is null)
            {
                if (fallback is { Length: > 0 })
                {
                    writer.WriteString(TitleMember, fallback);
                }
                return;
            }
            WriteLocalizedMember(
                writer, TitleMember, TitlesMember, displayName, defaultLocale);
        }

        /// <summary>
        /// Writes a Node's <c>Description</c> as <c>description</c> and carries
        /// non-default translations in <c>descriptions</c>.
        /// </summary>
        private static void WriteLocalizedDescription(
            Utf8JsonWriter writer,
            Export.LocalizedText[]? description,
            string defaultLocale)
        {
            WriteLocalizedMember(
                writer, DescriptionMember, DescriptionsMember, description, defaultLocale);
        }

        /// <summary>
        /// Writes one localized value as its singular and plural members.
        /// </summary>
        /// <remarks>
        /// A locale-free entry is the default-locale text: a UANodeSet writes
        /// <c>&lt;DisplayName&gt;Pump&lt;/DisplayName&gt;</c> for a Node whose
        /// name is stated once, and that one statement is the document's own
        /// language.
        /// </remarks>
        private static void WriteLocalizedMember(
            Utf8JsonWriter writer,
            string singular,
            string plural,
            Export.LocalizedText[]? texts,
            string defaultLocale,
            bool forceMap = false)
        {
            List<KeyValuePair<string, string>> entries = CollectLocales(texts, defaultLocale);
            if (entries.Count == 0)
            {
                return;
            }
            bool explicitNonEnglish = defaultLocale != FallbackLocale &&
                ProjectedTextLocale(texts, defaultLocale) is not null;
            if (entries.Count == 1 &&
                string.Equals(entries[0].Key, defaultLocale, StringComparison.Ordinal) &&
                !forceMap &&
                !explicitNonEnglish)
            {
                writer.WriteString(singular, entries[0].Value);
                return;
            }
            string? preferred = null;
            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (string.Equals(entry.Key, defaultLocale, StringComparison.Ordinal))
                {
                    preferred = entry.Value;
                    break;
                }
            }
            preferred ??= CodePointFirst(entries).Value;
            writer.WriteString(singular, preferred);
            writer.WritePropertyName(plural);
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> entry in entries)
            {
                writer.WriteString(entry.Key, entry.Value);
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Gets the entry whose BCP 47 language tag is first in ascending
        /// Unicode code-point order, which is the display fallback WoT Binding
        /// Section 9.1.1 names where a plural member has no entry for the
        /// document's default locale.
        /// </summary>
        /// <remarks>
        /// The value is a text a consumer can show and not a claim about the
        /// default locale: a NodeSet authored in the plant's language is an
        /// ordinary document, and inventing an <c>en</c> entry for it would
        /// state a translation nobody wrote. Ordering by code point rather than
        /// by source order is what makes two converters agree on which text the
        /// singular member carries.
        /// </remarks>
        private static KeyValuePair<string, string> CodePointFirst(
            List<KeyValuePair<string, string>> entries)
        {
            KeyValuePair<string, string> first = entries[0];
            for (int ii = 1; ii < entries.Count; ii++)
            {
                if (WotCodePointComparer.Instance.Compare(entries[ii].Key, first.Key) < 0)
                {
                    first = entries[ii];
                }
            }
            return first;
        }

        /// <summary>
        /// Reduces a <c>LocalizedText</c> array to the single value of the
        /// document's default locale, for a term whose readable form is one
        /// string rather than a singular/plural pair.
        /// </summary>
        /// <remarks>
        /// A ReferenceType's InverseName is such a term: it is a name a link
        /// <c>rel</c> uses, and a <c>rel</c> is not localized. The default
        /// locale's text is the one the document's own <c>@language</c> names,
        /// and the code-point-first entry of Section 9.1.1 is used where the
        /// source states no text for it - the same entry the singular member of
        /// a plural pair would carry, so the two never disagree.
        /// </remarks>
        private static string? SelectLocalizedValue(
            Export.LocalizedText[]? texts,
            string defaultLocale)
        {
            List<KeyValuePair<string, string>> entries = CollectLocales(texts, defaultLocale);
            if (entries.Count == 0)
            {
                return null;
            }
            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (string.Equals(entry.Key, defaultLocale, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }
            return CodePointFirst(entries).Value;
        }

        /// <summary>
        /// Reduces a <c>LocalizedText</c> array to one entry per locale, in
        /// source order.
        /// </summary>
        private static List<KeyValuePair<string, string>> CollectLocales(
            Export.LocalizedText[]? texts,
            string defaultLocale)
        {
            var entries = new List<KeyValuePair<string, string>>();
            if (texts is null)
            {
                return entries;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Export.LocalizedText text in texts)
            {
                if (string.IsNullOrEmpty(text.Value))
                {
                    continue;
                }
                string locale = string.IsNullOrEmpty(text.Locale)
                    ? defaultLocale
                    : text.Locale!;
                if (seen.Add(locale))
                {
                    entries.Add(new KeyValuePair<string, string>(locale, text.Value!));
                }
            }
            return entries;
        }

        /// <summary>
        /// Reads the singular and plural members of a localized value back into
        /// a <c>LocalizedText</c> array.
        /// </summary>
        /// <remarks>
        /// Section 9.1.1 makes the entry written to the Node's own
        /// <c>DisplayName</c> or <c>Description</c> the default locale's entry
        /// where the map has one and the code-point-first entry otherwise - the
        /// same entry the singular member carries - so it comes first. A
        /// document that states only the singular member round-trips through it
        /// alone and the Node keeps the locale-free form a NodeSet writes when
        /// it names one language.
        /// </remarks>
        private static Export.LocalizedText[]? ReadLocalizedText(
            JsonElement element,
            string singular,
            string plural,
            string? singularValue,
            string? declaredLocale,
            string? singularLocale)
        {
            string defaultLocale = EffectiveLocale(declaredLocale);
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(plural, out JsonElement declared) &&
                declared.ValueKind == JsonValueKind.Object)
            {
                var texts = new List<Export.LocalizedText>();
                string? leading = null;
                foreach (JsonProperty entry in declared.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String ||
                        entry.Name.Length == 0)
                    {
                        continue;
                    }
                    texts.Add(new Export.LocalizedText
                    {
                        Locale = entry.Name,
                        Value = entry.Value.GetString()
                    });
                    if (string.Equals(entry.Name, defaultLocale, StringComparison.Ordinal))
                    {
                        leading = entry.Name;
                    }
                }
                if (texts.Count > 0)
                {
                    MoveLeadingLocale(texts, leading ?? CodePointFirstLocale(texts));
                    return [.. texts];
                }
            }
            _ = singular;
            if (singularValue is null)
            {
                return null;
            }

            // A singular term's language is independent of the plural map's display-selection locale.
            return
            [
                new Export.LocalizedText
                {
                    Locale = singularLocale ?? string.Empty,
                    Value = singularValue
                }
            ];
        }

        /// <summary>
        /// Gets the language tag first in ascending Unicode code-point order
        /// among a set of <c>LocalizedText</c> entries (WoT Binding
        /// Section 9.1.1 and Annex G.3).
        /// </summary>
        private static string CodePointFirstLocale(List<Export.LocalizedText> texts)
        {
            string first = texts[0].Locale ?? string.Empty;
            for (int ii = 1; ii < texts.Count; ii++)
            {
                string locale = texts[ii].Locale ?? string.Empty;
                if (WotCodePointComparer.Instance.Compare(locale, first) < 0)
                {
                    first = locale;
                }
            }
            return first;
        }

        /// <summary>
        /// Moves the entry of one locale to the front, which is the entry
        /// written to the Node's own <c>DisplayName</c> or <c>Description</c>
        /// (WoT Binding Section 9.1.1).
        /// </summary>
        private static void MoveLeadingLocale(
            List<Export.LocalizedText> texts,
            string locale)
        {
            for (int ii = 0; ii < texts.Count; ii++)
            {
                if (!string.Equals(texts[ii].Locale, locale, StringComparison.Ordinal))
                {
                    continue;
                }
                if (ii > 0)
                {
                    Export.LocalizedText leading = texts[ii];
                    texts.RemoveAt(ii);
                    texts.Insert(0, leading);
                }
                return;
            }
        }

        /// <summary>
        /// Reads an affordance's <c>title</c> and <c>titles</c>.
        /// </summary>
        private static Export.LocalizedText[]? ReadTitle(
            WotDocument document,
            JsonElement element,
            string? fallback = null)
        {
            return ReadLocalizedText(
                element,
                TitleMember,
                TitlesMember,
                GetElementString(element, TitleMember) ?? fallback,
                GetDeclaredLocale(document, element),
                GetDeclaredLocale(document, element, TitleMember));
        }

        /// <summary>
        /// Reads an affordance's <c>description</c> and <c>descriptions</c>.
        /// </summary>
        private static Export.LocalizedText[]? ReadDescription(
            WotDocument document,
            JsonElement element)
        {
            return ReadLocalizedText(
                element,
                DescriptionMember,
                DescriptionsMember,
                GetElementString(element, DescriptionMember),
                GetDeclaredLocale(document, element),
                GetDeclaredLocale(document, element, DescriptionMember));
        }

        /// <summary>
        /// Gets the locale of the native text selected from a localized member.
        /// </summary>
        internal static string? ReadSelectedLocalizedTextLocale(
            WotDocument document, JsonElement element, string singular, string plural)
        {
            Export.LocalizedText[]? texts = ReadLocalizedText(
                element, singular, plural, GetElementString(element, singular),
                GetDeclaredLocale(document, element), GetDeclaredLocale(document, element, singular));
            return texts is { Length: > 0 } ? texts[0].Locale : null;
        }

        /// <summary>
        /// Gets the term language that preserves the selected value across generated TD scopes.
        /// </summary>
        internal static string? PreservedTextLanguage(
            WotDocument document, JsonElement element, string singular, string plural)
        {
            if (MapsLocalizedText(element, plural) &&
                element.GetProperty(plural).EnumerateObject().MoveNext())
            {
                string locale = GetDocumentLocale(document, element);
                return element.GetProperty(plural).TryGetProperty(locale, out _) ? locale : null;
            }
            return GetDeclaredLocale(document, element, singular);
        }

        /// <summary>
        /// Gets the first non-empty locale a <c>LocalizedText</c> array names.
        /// </summary>
        private static string? FirstLocale(Export.LocalizedText[]? texts)
        {
            foreach (Export.LocalizedText text in texts ?? [])
            {
                if (!string.IsNullOrEmpty(text.Locale) && !string.IsNullOrEmpty(text.Value))
                {
                    return text.Locale;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets whether the converter maps an affordance's plural localized
        /// member, which is what decides whether preservation must also carry
        /// it.
        /// </summary>
        internal static bool MapsLocalizedText(JsonElement element, string plural)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(plural, out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            foreach (JsonProperty entry in declared.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String || entry.Name.Length == 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Validates the localized-text rules of WoT Binding Sections 7 and
        /// 9.1.1 for one element.
        /// </summary>
        private static void ValidateLocalizedText(
            WotDocument document,
            JsonElement element,
            string parentPointer,
            List<WotDiagnostic> diagnostics)
        {
            ValidateLocalizedMember(
                element, parentPointer, TitleMember, TitlesMember,
                GetDocumentLocale(document, element), diagnostics);
            ValidateLocalizedMember(
                element, parentPointer, DescriptionMember, DescriptionsMember,
                GetDocumentLocale(document, element), diagnostics);
        }

        private static void ValidateLocalizedMember(
            JsonElement element,
            string parentPointer,
            string singular,
            string plural,
            string defaultLocale,
            List<WotDiagnostic> diagnostics)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(plural, out JsonElement declared))
            {
                return;
            }
            string pointer = parentPointer + "/" + plural;
            if (declared.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidLocalizedText,
                    $"The {plural} member shall be a map of BCP 47 language tags " +
                    "to strings (WoT Binding Section 9.1.1).",
                    WotLocation.FromPointer(pointer)));
                return;
            }
            string? defaultText = null;
            string? codePointFirstLocale = null;
            string? codePointFirstText = null;
            foreach (JsonProperty entry in declared.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String || entry.Name.Length == 0)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidLocalizedText,
                        $"The {plural} entry '{entry.Name}' shall be a non-empty " +
                        "language tag naming a string (WoT Binding Section 9.1.1).",
                        WotLocation.FromPointer(pointer)));
                    continue;
                }
                if (string.Equals(entry.Name, defaultLocale, StringComparison.Ordinal))
                {
                    defaultText = entry.Value.GetString();
                }
                if (codePointFirstLocale is null ||
                    WotCodePointComparer.Instance.Compare(
                        entry.Name, codePointFirstLocale) < 0)
                {
                    codePointFirstLocale = entry.Name;
                    codePointFirstText = entry.Value.GetString();
                }
            }
            string? singularText = GetElementString(element, singular);
            if (singularText is null)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidLocalizedText,
                    $"A document that carries {plural} shall carry {singular}; the " +
                    "singular member remains the default projection so a consumer " +
                    "that knows nothing of the plural member reads what it read " +
                    "before (WoT Binding Section 9.1.1).",
                    WotLocation.FromPointer(parentPointer + "/" + singular)));
                return;
            }
            if (defaultText is null)
            {
                // Section 9.1.1: a plural member with no entry for the default
                // locale is not invalid. The singular member is then the
                // code-point-first entry and is a display fallback that asserts
                // no locale - which is what makes a NodeSet authored in the
                // plant's language an ordinary document rather than one that
                // has to fall back to the native projection to say something the
                // plural member already says.
                if (codePointFirstText is not null &&
                    !string.Equals(codePointFirstText, singularText, StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidLocalizedText,
                        $"The {plural} member carries no entry for the document's default " +
                        $"locale '{defaultLocale}', so the {singular} member shall equal the " +
                        "entry whose language tag is first in ascending Unicode code-point " +
                        $"order ('{codePointFirstLocale}'). The value is a display fallback " +
                        "and asserts no locale (WoT Binding Section 9.1.1).",
                        WotLocation.FromPointer(parentPointer + "/" + singular)));
                }
                return;
            }
            if (!string.Equals(defaultText, singularText, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.InvalidLocalizedText,
                    $"The {singular} member shall equal the {plural} entry for the " +
                    $"document's default locale '{defaultLocale}'. Restating one " +
                    "value in two places is only safe while the two agree (WoT " +
                    "Binding Section 9.1.1).",
                    WotLocation.FromPointer(parentPointer + "/" + singular)));
            }
        }
    }
}
