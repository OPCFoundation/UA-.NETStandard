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
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The engineering-unit, EURange and InstrumentRange mapping of WoT Binding
    /// Sections 6.4 and 6.4.1.
    /// </summary>
    /// <remarks>
    /// Three OPC 10000-8 facts make a numeric Variable interpretable, and each
    /// is a Property Node of its own hanging off the Variable rather than an
    /// attribute of it. So each maps to a distinct place in a WoT document:
    /// <c>EngineeringUnits</c> projects to a property affordance of its own,
    /// which the annotated affordance names through the
    /// <c>uav:unitProperty</c> pointer and which carries the full
    /// <c>EUInformation</c> under <c>uav:engineeringUnits</c>; <c>EURange</c>
    /// is the W3C <c>minimum</c> and <c>maximum</c> of the annotated
    /// DataSchema; and <c>InstrumentRange</c>, which W3C WoT has no counterpart
    /// for, is <c>uav:instrumentRange</c>.
    /// <para>
    /// Two things this deliberately does not do. It never derives a range from
    /// the width of a DataType - an <c>Int16</c> reads from -32768 to 32767 and
    /// that is a fact about the machine representation, not the engineering
    /// range a process declares - and it never derives <c>uav:scaleFactor</c>
    /// or <c>uav:decimalPlaces</c> from any of the three, nor any of the three
    /// from them: Section 6.4 calls that pair a static presentation and
    /// transport transform, which is not what OPC 10000-8 models analog scaling
    /// with.
    /// </para>
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The <c>uav</c> term carrying the readable preservation of an
        /// <c>EUInformation</c> (WoT Binding Section 6.4.1).
        /// </summary>
        internal const string EngineeringUnitsTerm = "uav:engineeringUnits";

        /// <summary>
        /// The <c>uav</c> term carrying the OPC UA <c>InstrumentRange</c> (WoT
        /// Binding Section 6.4.1).
        /// </summary>
        internal const string InstrumentRangeTerm = "uav:instrumentRange";

        /// <summary>
        /// The <c>uav</c> pointer naming the sibling property affordance that
        /// carries the engineering unit (WoT Binding Section 6.4).
        /// </summary>
        internal const string UnitPropertyTerm = "uav:unitProperty";

        /// <summary>
        /// The W3C DataSchema members the <c>EURange</c> maps onto.
        /// </summary>
        internal const string MinimumMember = "minimum";

        /// <inheritdoc cref="MinimumMember"/>
        internal const string MaximumMember = "maximum";

        /// <summary>
        /// The W3C DataSchema member carrying the engineering unit.
        /// </summary>
        internal const string UnitMember = "unit";

        /// <summary>
        /// The RFC 6901 pointer prefix Section 6.4 fixes for
        /// <c>uav:unitProperty</c>.
        /// </summary>
        internal const string UnitPointerPrefix = "/properties/";

        /// <summary>
        /// The base-namespace BrowseNames OPC 10000-8 gives the three
        /// Properties of an <c>AnalogUnitType</c> or <c>AnalogItemType</c>.
        /// </summary>
        internal const string EngineeringUnitsBrowseName = "EngineeringUnits";

        /// <inheritdoc cref="EngineeringUnitsBrowseName"/>
        internal const string EuRangeBrowseName = "EURange";

        /// <inheritdoc cref="EngineeringUnitsBrowseName"/>
        internal const string InstrumentRangeBrowseName = "InstrumentRange";

        /// <summary>
        /// The <c>EUInformation</c> DataType and its default XML encoding
        /// (OPC 10000-8).
        /// </summary>
        private const string EuInformationDataType = "i=887";

        /// <inheritdoc cref="EuInformationDataType"/>
        private const string EuInformationXmlEncoding = "i=888";

        /// <summary>
        /// The <c>Range</c> DataType and its default XML encoding
        /// (OPC 10000-8).
        /// </summary>
        private const string RangeDataType = "i=884";

        /// <inheritdoc cref="RangeDataType"/>
        private const string RangeXmlEncoding = "i=885";

        /// <summary>
        /// One decoded <c>EUInformation</c>.
        /// </summary>
        /// <remarks>
        /// The <c>NamespaceUri</c> and <c>UnitId</c> are what make the unit
        /// recoverable: the UNECE/CEFACT common code cannot be read back out of
        /// the display string <c>rpm</c>. Both localized members keep every
        /// locale the source carried, which is what Section 6.4.1's
        /// <c>displayNames</c> and <c>descriptions</c> siblings state.
        /// </remarks>
        private sealed class WotEngineeringUnits
        {
            public string NamespaceUri { get; init; } = string.Empty;

            public int UnitId { get; init; }

            public Opc.Ua.Export.LocalizedText[]? DisplayName { get; init; }

            public Opc.Ua.Export.LocalizedText[]? Description { get; init; }
        }

        /// <summary>
        /// One decoded <c>Range</c>.
        /// </summary>
        private readonly record struct WotRange(double Low, double High);

        /// <summary>
        /// The analog facets one Variable declares through its Property
        /// children, together with the Nodes they were read from.
        /// </summary>
        private sealed class WotAnalogFacets
        {
            /// <summary>The decoded <c>EngineeringUnits</c> value.</summary>
            public WotEngineeringUnits? Units { get; set; }

            /// <summary>The NodeId of the <c>EngineeringUnits</c> Property.</summary>
            public string? UnitsNodeId { get; set; }

            /// <summary>The affordance name the unit Property projects to.</summary>
            public string? UnitsAffordance { get; set; }

            /// <summary>The decoded <c>EURange</c> value.</summary>
            public WotRange? EuRange { get; set; }

            /// <summary>The decoded <c>InstrumentRange</c> value.</summary>
            public WotRange? InstrumentRange { get; set; }
        }

        /// <summary>
        /// Collects the analog facets of every Variable being projected.
        /// </summary>
        /// <remarks>
        /// Only a Property whose value actually decodes contributes. One
        /// holding a foreign encoding, a partial structure or no value at all
        /// is left alone: it stays an ordinary property affordance in its own
        /// right, the readable mapping is then incomplete, and the preservation
        /// projection carries it. Emitting <c>minimum</c> from a value this
        /// direction could not read would turn a reported gap into a number
        /// nobody wrote.
        /// </remarks>
        /// <param name="properties">The Variables being projected.</param>
        /// <param name="index">The NodeId index of the source NodeSet.</param>
        /// <param name="affordanceNames">
        /// The affordance name each projected Variable is written under, keyed
        /// by NodeId. The unit pointer has to name the affordance rather than
        /// the Node, so the names are settled before anything is written.
        /// </param>
        private static Dictionary<string, WotAnalogFacets> CollectAnalogFacets(
            List<UAVariable> properties,
            Dictionary<string, UANode> index,
            Dictionary<string, string> affordanceNames)
        {
            var collected = new Dictionary<string, WotAnalogFacets>(StringComparer.Ordinal);
            foreach (UAVariable variable in properties)
            {
                if (variable.NodeId is null || variable.References is null)
                {
                    continue;
                }
                WotAnalogFacets? facets = null;
                foreach (Reference reference in variable.References)
                {
                    if (!reference.IsForward ||
                        reference.Value is null ||
                        !IsComponentReference(reference.ReferenceType) ||
                        !index.TryGetValue(reference.Value, out UANode? target) ||
                        target is not UAVariable child ||
                        child.NodeId is null)
                    {
                        continue;
                    }
                    if (IsBaseNamespaceBrowseName(
                            child.BrowseName, EngineeringUnitsBrowseName) &&
                        TryDecodeEngineeringUnits(child.Value, out WotEngineeringUnits? units) &&
                        affordanceNames.TryGetValue(child.NodeId, out string? affordance))
                    {
                        facets ??= new WotAnalogFacets();
                        facets.Units = units;
                        facets.UnitsNodeId = child.NodeId;
                        facets.UnitsAffordance = affordance;
                        continue;
                    }
                    if (IsBaseNamespaceBrowseName(child.BrowseName, EuRangeBrowseName) &&
                        TryDecodeRange(child.Value, out WotRange euRange))
                    {
                        facets ??= new WotAnalogFacets();
                        facets.EuRange = euRange;
                        continue;
                    }
                    if (IsBaseNamespaceBrowseName(
                            child.BrowseName, InstrumentRangeBrowseName) &&
                        TryDecodeRange(child.Value, out WotRange instrumentRange))
                    {
                        facets ??= new WotAnalogFacets();
                        facets.InstrumentRange = instrumentRange;
                    }
                }
                if (facets is not null)
                {
                    collected[variable.NodeId] = facets;
                }
            }
            return collected;
        }

        /// <summary>
        /// Writes the analog facets of the annotated Variable (WoT Binding
        /// Sections 6.4 and 6.4.1).
        /// </summary>
        /// <remarks>
        /// <c>unit</c> is the engineering unit the source states, taken from
        /// the <c>EUInformation</c> DisplayName and never from a quantity kind:
        /// Section 6.4 keeps quantity kinds in QUDT precisely so the two cannot
        /// disagree. The locale it is taken in is the document's own default
        /// locale, the same one the <c>displayName</c> of Section 6.4.1 is
        /// written in, so a multi-locale unit states one text in both places
        /// instead of falling back to preservation because they disagree.
        /// <c>minimum</c> and <c>maximum</c> carry the EURange, which
        /// W3C Thing Description 1.1 already defines as both the expected
        /// interval and a validation constraint.
        /// </remarks>
        private static void WriteAnalogFacets(
            Utf8JsonWriter writer,
            WotAnalogFacets? facets,
            string defaultLocale)
        {
            if (facets is null)
            {
                return;
            }
            if (facets.Units is { } units)
            {
                string? display = SelectLocalizedValue(units.DisplayName, defaultLocale);
                if (!string.IsNullOrEmpty(display))
                {
                    writer.WriteString(UnitMember, display);
                }
                if (facets.UnitsAffordance is { Length: > 0 } affordance)
                {
                    writer.WriteString(
                        UnitPropertyTerm, UnitPointerPrefix + EscapePointerToken(affordance));
                }
            }
            if (facets.EuRange is { } range)
            {
                writer.WriteNumber(MinimumMember, range.Low);
                writer.WriteNumber(MaximumMember, range.High);
            }
            if (facets.InstrumentRange is { } instrument)
            {
                writer.WritePropertyName(InstrumentRangeTerm);
                writer.WriteStartObject();
                writer.WriteNumber(MinimumMember, instrument.Low);
                writer.WriteNumber(MaximumMember, instrument.High);
                writer.WriteEndObject();
            }
        }

        /// <summary>
        /// Writes the readable <c>EUInformation</c> preservation on the
        /// property affordance that projects an <c>EngineeringUnits</c>
        /// Property (WoT Binding Section 6.4.1).
        /// </summary>
        private static void WriteEngineeringUnits(
            Utf8JsonWriter writer,
            UAVariable variable,
            string defaultLocale)
        {
            if (!IsBaseNamespaceBrowseName(variable.BrowseName, EngineeringUnitsBrowseName) ||
                !TryDecodeEngineeringUnits(variable.Value, out WotEngineeringUnits? units))
            {
                return;
            }
            writer.WritePropertyName(EngineeringUnitsTerm);
            writer.WriteStartObject();

            // Section 6.4.1 mints displayName and description as short members
            // scoped to this object, so a root-level override cannot reach
            // them: a scoped context is entered here and nowhere else. Where
            // either states no text in the document's default locale, the two
            // are re-declared without a language, so an unqualified value is
            // not read as text of a language it is not written in.
            if (LacksDefaultLocale(units!.DisplayName, defaultLocale) ||
                LacksDefaultLocale(units.Description, defaultLocale))
            {
                WriteUnitLocalizedTextOverride(writer);
            }
            writer.WriteString("namespaceUri", units.NamespaceUri);
            writer.WriteNumber("unitId", units.UnitId);
            WriteLocalizedMember(
                writer, "displayName", "displayNames", units.DisplayName, defaultLocale);
            WriteLocalizedMember(
                writer, "description", "descriptions", units.Description, defaultLocale);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes the node-local override that drops the document's default
        /// language from the two scoped <c>EUInformation</c> text members.
        /// </summary>
        private static void WriteUnitLocalizedTextOverride(Utf8JsonWriter writer)
        {
            writer.WritePropertyName("@context");
            writer.WriteStartObject();
            writer.WritePropertyName("displayName");
            writer.WriteStartObject();
            writer.WriteString("@id", "uav:unitDisplayName");
            writer.WriteNull("@language");
            writer.WriteEndObject();
            writer.WritePropertyName("description");
            writer.WriteStartObject();
            writer.WriteString("@id", "uav:unitDescription");
            writer.WriteNull("@language");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>
        /// Gets whether a Variable projects an <c>EngineeringUnits</c> Property
        /// whose value this direction reads, which is what makes the affordance
        /// a string-valued unit affordance rather than an opaque structure.
        /// </summary>
        private static bool IsUnitAffordance(UAVariable variable)
        {
            return IsBaseNamespaceBrowseName(variable.BrowseName, EngineeringUnitsBrowseName) &&
                TryDecodeEngineeringUnits(variable.Value, out _);
        }

        /// <summary>
        /// Decodes the <c>EUInformation</c> a NodeSet value fragment holds.
        /// </summary>
        /// <remarks>
        /// Only the standard shape is accepted: an ExtensionObject carrying the
        /// <c>EUInformation</c> default XML encoding and a body whose children
        /// are the four fields OPC 10000-8 declares. An unknown child element
        /// means the value states something this mapping would drop, so nothing
        /// is decoded and the Node is left to preservation.
        /// </remarks>
        private static bool TryDecodeEngineeringUnits(
            System.Xml.XmlElement? value,
            out WotEngineeringUnits? units)
        {
            units = null;
            System.Xml.XmlElement? body = TryGetExtensionBody(
                value, EuInformationXmlEncoding, "EUInformation");
            if (body is null)
            {
                return false;
            }
            string? namespaceUri = null;
            int? unitId = null;
            Opc.Ua.Export.LocalizedText[]? displayName = null;
            Opc.Ua.Export.LocalizedText[]? description = null;
            foreach (System.Xml.XmlNode node in body.ChildNodes)
            {
                if (node is not System.Xml.XmlElement field)
                {
                    continue;
                }
                switch (field.LocalName)
                {
                    case "NamespaceUri":
                        namespaceUri = field.InnerText;
                        break;
                    case "UnitId":
                        if (!int.TryParse(
                            field.InnerText.Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed))
                        {
                            return false;
                        }
                        unitId = parsed;
                        break;
                    case "DisplayName":
                        if (!TryDecodeLocalizedText(field, out displayName))
                        {
                            return false;
                        }
                        break;
                    case "Description":
                        if (!TryDecodeLocalizedText(field, out description))
                        {
                            return false;
                        }
                        break;
                    default:
                        return false;
                }
            }
            if (namespaceUri is null || unitId is null || FirstText(displayName) is null)
            {
                return false;
            }
            units = new WotEngineeringUnits
            {
                NamespaceUri = namespaceUri,
                UnitId = unitId.Value,
                DisplayName = displayName,
                Description = description
            };
            return true;
        }

        /// <summary>
        /// Decodes the <c>Range</c> a NodeSet value fragment holds.
        /// </summary>
        private static bool TryDecodeRange(System.Xml.XmlElement? value, out WotRange range)
        {
            range = default;
            System.Xml.XmlElement? body = TryGetExtensionBody(
                value, RangeXmlEncoding, "Range");
            if (body is null)
            {
                return false;
            }
            double? low = null;
            double? high = null;
            foreach (System.Xml.XmlNode node in body.ChildNodes)
            {
                if (node is not System.Xml.XmlElement field)
                {
                    continue;
                }
                if (!double.TryParse(
                    field.InnerText.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed))
                {
                    return false;
                }
                switch (field.LocalName)
                {
                    case "Low":
                        low = parsed;
                        break;
                    case "High":
                        high = parsed;
                        break;
                    default:
                        return false;
                }
            }
            if (low is null || high is null)
            {
                return false;
            }
            range = new WotRange(low.Value, high.Value);
            return true;
        }

        /// <summary>
        /// Unwraps the body of an ExtensionObject carrying one known encoding.
        /// </summary>
        private static System.Xml.XmlElement? TryGetExtensionBody(
            System.Xml.XmlElement? value,
            string encodingId,
            string bodyName)
        {
            if (value is null ||
                !string.Equals(value.LocalName, "ExtensionObject", StringComparison.Ordinal) ||
                !string.Equals(value.NamespaceURI, UaXmlNamespace, StringComparison.Ordinal))
            {
                return null;
            }
            System.Xml.XmlElement? typeId = FindChild(value, "TypeId");
            System.Xml.XmlElement? identifier = typeId is null ? null : FindChild(typeId, "Identifier");
            if (identifier is null ||
                !string.Equals(
                    identifier.InnerText.Trim(), encodingId, StringComparison.Ordinal))
            {
                return null;
            }
            System.Xml.XmlElement? body = FindChild(value, "Body");
            return body is null ? null : FindChild(body, bodyName);
        }

        /// <summary>
        /// Decodes a <c>LocalizedText</c> element of a structure body.
        /// </summary>
        private static bool TryDecodeLocalizedText(
            System.Xml.XmlElement element,
            out Opc.Ua.Export.LocalizedText[]? text)
        {
            text = null;
            string? locale = null;
            string? value = null;
            foreach (System.Xml.XmlNode node in element.ChildNodes)
            {
                if (node is not System.Xml.XmlElement member)
                {
                    continue;
                }
                switch (member.LocalName)
                {
                    case "Locale":
                        locale = member.InnerText;
                        break;
                    case "Text":
                        value = member.InnerText;
                        break;
                    default:
                        return false;
                }
            }
            if (value is null)
            {
                return true;
            }
            text = [new Opc.Ua.Export.LocalizedText { Locale = locale ?? string.Empty, Value = value }];
            return true;
        }

        /// <summary>
        /// Escapes one RFC 6901 reference token.
        /// </summary>
        private static string EscapePointerToken(string token)
        {
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether the converter materializes an affordance's engineering
        /// range, which is what decides whether preservation must also carry
        /// the two members.
        /// </summary>
        internal static bool MapsEuRange(JsonElement affordance)
        {
            return TryReadRangeMembers(affordance, out _);
        }

        /// <summary>
        /// Gets whether the converter materializes an affordance's
        /// <c>uav:instrumentRange</c>.
        /// </summary>
        internal static bool MapsInstrumentRange(JsonElement affordance)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                affordance.TryGetProperty(InstrumentRangeTerm, out JsonElement declared) &&
                TryReadRangeMembers(declared, out _);
        }

        /// <summary>
        /// Gets whether the converter materializes an affordance's
        /// <c>uav:engineeringUnits</c> as an <c>EUInformation</c> value.
        /// </summary>
        internal static bool MapsEngineeringUnits(JsonElement affordance)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                affordance.TryGetProperty(EngineeringUnitsTerm, out JsonElement declared) &&
                TryReadEngineeringUnits(declared, out _);
        }

        /// <summary>
        /// Gets whether the converter maps an affordance's
        /// <c>uav:unitProperty</c> onto the <c>EngineeringUnits</c> Property of
        /// the Variable the affordance projects.
        /// </summary>
        /// <remarks>
        /// The pointer is mapped only where the affordance it names actually
        /// carries an <c>EUInformation</c> and belongs to this Variable, since
        /// that is what makes the Property a child of it and the pointer
        /// re-derivable from the Reference. A pointer at a sibling that carries
        /// no unit identity - a plain string affordance an author wired up
        /// themselves - states something the NodeSet does not, so it is kept
        /// verbatim rather than dropped.
        /// </remarks>
        internal static bool MapsUnitProperty(JsonElement root, JsonElement affordance)
        {
            if (!TryGetUnitTarget(root, affordance, out JsonElement target))
            {
                return false;
            }
            if (!target.TryGetProperty("uav:componentOf", out JsonElement componentOf) ||
                componentOf.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            // A unit affordance that already names its parent keeps it, so the
            // pointer is re-derivable only where that parent is the annotated
            // Variable itself.
            string? owner = GetElementString(affordance, "uav:id");
            foreach (JsonElement entry in componentOf.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String &&
                    owner is not null &&
                    string.Equals(entry.GetString(), owner, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets whether the converter re-derives an affordance's <c>unit</c>
        /// from the <c>EUInformation</c> the affordance its pointer names
        /// carries.
        /// </summary>
        internal static bool MapsUnit(JsonElement root, JsonElement affordance)
        {
            if (!MapsUnitProperty(root, affordance) ||
                !TryGetUnitTarget(root, affordance, out JsonElement target) ||
                !target.TryGetProperty(EngineeringUnitsTerm, out JsonElement units))
            {
                return false;
            }
            string? unit = GetElementString(affordance, UnitMember);
            return unit is not null &&
                string.Equals(
                    GetElementString(units, "displayName"), unit, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the sibling affordance an affordance's unit pointer names,
        /// where that affordance carries a well-formed <c>EUInformation</c>.
        /// </summary>
        private static bool TryGetUnitTarget(
            JsonElement root,
            JsonElement affordance,
            out JsonElement target)
        {
            target = default;
            if (root.ValueKind != JsonValueKind.Object ||
                affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty(UnitPropertyTerm, out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.String ||
                declared.GetString() is not { } pointer ||
                !pointer.StartsWith(UnitPointerPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string token = pointer.Substring(UnitPointerPrefix.Length);
            if (token.Length == 0 || token.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }
            return root.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind == JsonValueKind.Object &&
                properties.TryGetProperty(UnescapePointerToken(token), out target) &&
                MapsEngineeringUnits(target);
        }

        /// <summary>
        /// Reads the numeric <c>minimum</c> and <c>maximum</c> of an object.
        /// </summary>
        /// <remarks>
        /// Both are required and the interval has to be ordered: a
        /// <c>minimum</c> above its <c>maximum</c> names no interval, and a
        /// lone bound names no range at all. Neither is materialized in that
        /// case; the diagnostic reports it and preservation keeps what the
        /// author wrote.
        /// </remarks>
        private static bool TryReadRangeMembers(JsonElement element, out WotRange range)
        {
            range = default;
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(MinimumMember, out JsonElement low) ||
                !element.TryGetProperty(MaximumMember, out JsonElement high) ||
                low.ValueKind != JsonValueKind.Number ||
                high.ValueKind != JsonValueKind.Number ||
                !low.TryGetDouble(out double lowValue) ||
                !high.TryGetDouble(out double highValue) ||
                lowValue > highValue)
            {
                return false;
            }
            range = new WotRange(lowValue, highValue);
            return true;
        }

        /// <summary>
        /// Reads an authored <c>uav:engineeringUnits</c> object.
        /// </summary>
        private static bool TryReadEngineeringUnits(
            JsonElement element,
            out WotEngineeringUnits? units,
            string? declaredLocale = null)
        {
            units = null;
            if (element.ValueKind != JsonValueKind.Object ||
                GetElementString(element, "namespaceUri") is not { Length: > 0 } namespaceUri ||
                !element.TryGetProperty("unitId", out JsonElement unitId) ||
                unitId.ValueKind != JsonValueKind.Number ||
                !IsIntegerLiteral(unitId) ||
                !unitId.TryGetInt32(out int identifier) ||
                GetElementString(element, "displayName") is not { Length: > 0 } displayName)
            {
                return false;
            }
            units = new WotEngineeringUnits
            {
                NamespaceUri = namespaceUri,
                UnitId = identifier,
                DisplayName = ReadLocalizedText(
                    element, "displayName", "displayNames", displayName, declaredLocale),
                Description = ReadLocalizedText(
                    element, "description", "descriptions",
                    GetElementString(element, "description"), declaredLocale)
            };
            return true;
        }

        /// <summary>
        /// Materializes the <c>EngineeringUnits</c>, <c>EURange</c> and
        /// <c>InstrumentRange</c> Properties the document's analog terms state.
        /// </summary>
        /// <remarks>
        /// This runs after every property affordance has become a Variable,
        /// because the terms relate two affordances: <c>uav:unitProperty</c>
        /// names the sibling that carries the unit, and OPC 10000-8 makes that
        /// sibling a Property <em>of</em> the annotated Variable rather than of
        /// the Thing. So the pointer is what re-parents it - an affordance that
        /// states its own <c>uav:componentOf</c> keeps the parent it named,
        /// because an explicit statement outranks an inferred one. The ranges
        /// have no affordance of their own unless the document authored one, in
        /// which case that Node is filled in rather than a second one created.
        /// </remarks>
        private static void SynthesizeAnalogFacets(
            WotDocument document,
            UANodeSet nodeSet,
            string rootLocal,
            string rootNodeId,
            Dictionary<string, string> propertyNodeIds,
            List<UANode> items,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            var index = new Dictionary<string, UANode>(StringComparer.Ordinal);
            foreach (UANode node in items)
            {
                if (node.NodeId is { Length: > 0 } nodeId)
                {
                    index[nodeId] = node;
                }
            }
            foreach (KeyValuePair<string, JsonElement> affordance in document.Properties)
            {
                if (!propertyNodeIds.TryGetValue(affordance.Key, out string? nodeId) ||
                    !index.TryGetValue(nodeId, out UANode? node) ||
                    node is not UAVariable owner)
                {
                    continue;
                }
                string local = LocalName(
                    GetElementString(affordance.Value, "uav:browseName")) ??
                    affordance.Key;
                AttachUnitProperty(
                    document, affordance.Value, owner, propertyNodeIds, index,
                    items, rootNodeId, rootReferences, diagnostics);
                if (TryReadRangeMembers(affordance.Value, out WotRange euRange))
                {
                    MaterializeRange(
                        nodeSet, owner, EuRangeBrowseName, euRange, rootLocal, local,
                        index, items, diagnostics);
                }
                if (affordance.Value.ValueKind == JsonValueKind.Object &&
                    affordance.Value.TryGetProperty(
                        InstrumentRangeTerm, out JsonElement declared) &&
                    TryReadRangeMembers(declared, out WotRange instrumentRange))
                {
                    MaterializeRange(
                        nodeSet, owner, InstrumentRangeBrowseName, instrumentRange,
                        rootLocal, local, index, items, diagnostics);
                }
            }
        }

        /// <summary>
        /// Turns the affordance a <c>uav:unitProperty</c> names into the
        /// annotated Variable's own <c>EngineeringUnits</c> Property.
        /// </summary>
        private static void AttachUnitProperty(
            WotDocument document,
            JsonElement affordance,
            UAVariable owner,
            Dictionary<string, string> propertyNodeIds,
            Dictionary<string, UANode> index,
            List<UANode> items,
            string rootNodeId,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            if (!TryReadUnitPointerTarget(document, affordance, out string target) ||
                !propertyNodeIds.TryGetValue(target, out string? unitNodeId) ||
                !index.TryGetValue(unitNodeId, out UANode? node) ||
                node is not UAVariable unit ||
                owner.NodeId is null ||
                string.Equals(unit.NodeId, owner.NodeId, StringComparison.Ordinal))
            {
                return;
            }
            if (!document.Properties.TryGetValue(target, out JsonElement unitAffordance))
            {
                return;
            }

            // An affordance that states its own parent keeps it. Only one that
            // says nothing is re-parented, and then by the pointer alone.
            if (unitAffordance.ValueKind == JsonValueKind.Object &&
                unitAffordance.TryGetProperty("uav:componentOf", out JsonElement componentOf) &&
                componentOf.ValueKind == JsonValueKind.Array &&
                componentOf.GetArrayLength() > 0)
            {
                return;
            }
            _ = diagnostics;
            Reparent(unit, owner.NodeId!, rootNodeId, rootReferences, items);
        }

        /// <summary>
        /// Reads the affordance name a canonical unit pointer states.
        /// </summary>
        private static bool TryReadUnitPointerTarget(
            WotDocument document,
            JsonElement affordance,
            out string target)
        {
            target = string.Empty;
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty(UnitPropertyTerm, out JsonElement declared) ||
                declared.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string? pointer = declared.GetString();
            if (pointer is null ||
                !pointer.StartsWith(UnitPointerPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string token = pointer.Substring(UnitPointerPrefix.Length);
            if (token.Length == 0 || token.Contains('/', StringComparison.Ordinal))
            {
                return false;
            }
            target = UnescapePointerToken(token);
            return document.Properties.ContainsKey(target);
        }

        /// <summary>
        /// Unescapes one RFC 6901 reference token.
        /// </summary>
        private static string UnescapePointerToken(string token)
        {
            return token
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
        }

        /// <summary>
        /// Moves a Variable from the Thing to the Variable that owns it.
        /// </summary>
        private static void Reparent(
            UAVariable child,
            string ownerNodeId,
            string rootNodeId,
            List<Reference> rootReferences,
            List<UANode> items)
        {
            if (!string.Equals(child.ParentNodeId, rootNodeId, StringComparison.Ordinal))
            {
                return;
            }
            child.ParentNodeId = ownerNodeId;
            var references = new List<Reference>();
            foreach (Reference reference in child.References ?? [])
            {
                if (!reference.IsForward &&
                    IsComponentReference(reference.ReferenceType) &&
                    string.Equals(reference.Value, rootNodeId, StringComparison.Ordinal))
                {
                    references.Add(new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = ownerNodeId
                    });
                    continue;
                }
                references.Add(reference);
            }
            child.References = [.. references];

            for (int ii = rootReferences.Count - 1; ii >= 0; ii--)
            {
                if (rootReferences[ii].IsForward &&
                    IsComponentReference(rootReferences[ii].ReferenceType) &&
                    string.Equals(rootReferences[ii].Value, child.NodeId, StringComparison.Ordinal))
                {
                    rootReferences.RemoveAt(ii);
                }
            }
            AddOwnedProperty(items, ownerNodeId, child.NodeId!);
        }

        /// <summary>
        /// Materializes one range Property of a Variable, or fills in the value
        /// of the Node an authored affordance already produced for it.
        /// </summary>
        private static void MaterializeRange(
            UANodeSet nodeSet,
            UAVariable owner,
            string browseName,
            WotRange range,
            string rootLocal,
            string ownerLocal,
            Dictionary<string, UANode> index,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            System.Xml.XmlElement value = BuildRangeValue(range);
            foreach (Reference reference in owner.References ?? [])
            {
                if (!reference.IsForward ||
                    reference.Value is null ||
                    !IsComponentReference(reference.ReferenceType) ||
                    !index.TryGetValue(reference.Value, out UANode? target) ||
                    target is not UAVariable declared ||
                    !IsBaseNamespaceBrowseName(declared.BrowseName, browseName))
                {
                    continue;
                }

                // The document authored the Node itself, so its NodeId,
                // ModellingRule and references are what the author stated and
                // only the value the range members carry is filled in.
                declared.Value ??= value;
                if (string.IsNullOrEmpty(declared.DataType) ||
                    string.Equals(declared.DataType, WotVocabulary.BaseDataType, StringComparison.Ordinal))
                {
                    declared.DataType = RangeDataType;
                }
                return;
            }
            _ = nodeSet;
            _ = diagnostics;

            string nodeId = GenerateBaseChildNodeId(
                nodeSet, rootLocal, ownerLocal, browseName);
            items.Add(new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = MakeText(browseName),
                ParentNodeId = owner.NodeId,
                DataType = RangeDataType,
                AccessLevel = AccessLevelCurrentRead,
                Value = value,
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition",
                        IsForward = true,
                        Value = WotVocabulary.PropertyType
                    },
                    new Reference
                    {
                        ReferenceType = "HasModellingRule",
                        IsForward = true,

                        // OPC 10000-8 declares EURange Mandatory and
                        // InstrumentRange Optional on AnalogItemType, and a
                        // range a document states is a range the instance has.
                        Value = string.Equals(
                            browseName, EuRangeBrowseName, StringComparison.Ordinal)
                            ? WotVocabulary.ModellingRuleMandatory
                            : WotVocabulary.ModellingRuleOptional
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = owner.NodeId
                    }
                ]
            });
            AddOwnedProperty(items, owner.NodeId!, nodeId);
        }

        /// <summary>
        /// Applies an authored <c>uav:engineeringUnits</c> to the Variable the
        /// affordance projects.
        /// </summary>
        /// <remarks>
        /// The affordance's readable <c>type</c> is <c>string</c>, because that
        /// is what a client reads at run time, but the Node behind it holds an
        /// <c>EUInformation</c>. So the definitive DataType comes from the term
        /// rather than from the json type, unless the document pinned one
        /// itself through Section 5.4's <c>uav:mapToType</c>.
        /// </remarks>
        private static void ApplyEngineeringUnits(UAVariable variable, JsonElement affordance)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty(EngineeringUnitsTerm, out JsonElement declared) ||
                !TryReadEngineeringUnits(declared, out WotEngineeringUnits? units))
            {
                return;
            }
            if (GetElementString(affordance, "uav:mapToType") is null)
            {
                variable.DataType = EuInformationDataType;
            }
            variable.Value = BuildEngineeringUnitsValue(units!);
        }

        /// <summary>
        /// Builds the <c>EUInformation</c> value fragment.
        /// </summary>
        private static System.Xml.XmlElement BuildEngineeringUnitsValue(
            WotEngineeringUnits units)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement body = BuildExtensionObject(
                document, EuInformationXmlEncoding, "EUInformation");

            System.Xml.XmlElement namespaceUri = document.CreateElement(
                "uax", "NamespaceUri", UaXmlNamespace);
            namespaceUri.InnerText = units.NamespaceUri;
            body.AppendChild(namespaceUri);

            System.Xml.XmlElement unitId = document.CreateElement(
                "uax", "UnitId", UaXmlNamespace);
            unitId.InnerText = units.UnitId.ToString(CultureInfo.InvariantCulture);
            body.AppendChild(unitId);

            AppendLocalizedText(document, body, "DisplayName", units.DisplayName);
            AppendLocalizedText(document, body, "Description", units.Description);
            return (System.Xml.XmlElement)body.ParentNode!.ParentNode!;
        }

        /// <summary>
        /// Builds the <c>Range</c> value fragment.
        /// </summary>
        private static System.Xml.XmlElement BuildRangeValue(WotRange range)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement body = BuildExtensionObject(
                document, RangeXmlEncoding, "Range");

            System.Xml.XmlElement low = document.CreateElement("uax", "Low", UaXmlNamespace);
            low.InnerText = range.Low.ToString("R", CultureInfo.InvariantCulture);
            body.AppendChild(low);

            System.Xml.XmlElement high = document.CreateElement("uax", "High", UaXmlNamespace);
            high.InnerText = range.High.ToString("R", CultureInfo.InvariantCulture);
            body.AppendChild(high);
            return (System.Xml.XmlElement)body.ParentNode!.ParentNode!;
        }

        /// <summary>
        /// Builds the ExtensionObject wrapper and returns its body element.
        /// </summary>
        private static System.Xml.XmlElement BuildExtensionObject(
            System.Xml.XmlDocument document,
            string encodingId,
            string bodyName)
        {
            System.Xml.XmlElement extension = document.CreateElement(
                "uax", "ExtensionObject", UaXmlNamespace);
            System.Xml.XmlElement typeId = document.CreateElement(
                "uax", "TypeId", UaXmlNamespace);
            System.Xml.XmlElement identifier = document.CreateElement(
                "uax", "Identifier", UaXmlNamespace);
            identifier.InnerText = encodingId;
            typeId.AppendChild(identifier);
            extension.AppendChild(typeId);

            System.Xml.XmlElement wrapper = document.CreateElement(
                "uax", "Body", UaXmlNamespace);
            System.Xml.XmlElement body = document.CreateElement(
                "uax", bodyName, UaXmlNamespace);
            wrapper.AppendChild(body);
            extension.AppendChild(wrapper);
            return body;
        }

        /// <summary>
        /// Appends a <c>LocalizedText</c> member of a structure body.
        /// </summary>
        private static void AppendLocalizedText(
            System.Xml.XmlDocument document,
            System.Xml.XmlElement body,
            string name,
            Opc.Ua.Export.LocalizedText[]? text)
        {
            string? value = FirstText(text);
            if (value is null)
            {
                return;
            }
            System.Xml.XmlElement element = document.CreateElement("uax", name, UaXmlNamespace);
            string? locale = FirstLocale(text);
            if (!string.IsNullOrEmpty(locale))
            {
                System.Xml.XmlElement localeElement = document.CreateElement(
                    "uax", "Locale", UaXmlNamespace);
                localeElement.InnerText = locale!;
                element.AppendChild(localeElement);
            }
            System.Xml.XmlElement textElement = document.CreateElement(
                "uax", "Text", UaXmlNamespace);
            textElement.InnerText = value;
            element.AppendChild(textElement);
            body.AppendChild(element);
        }

        /// <summary>
        /// Adds the forward Property reference from the owning Variable.
        /// </summary>
        private static void AddOwnedProperty(List<UANode> items, string owner, string nodeId)
        {
            foreach (UANode node in items)
            {
                if (!string.Equals(node.NodeId, owner, StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (Reference existing in node.References ?? [])
                {
                    if (existing.IsForward &&
                        IsComponentReference(existing.ReferenceType) &&
                        string.Equals(existing.Value, nodeId, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                var references = new List<Reference>(node.References ?? [])
                {
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = true,
                        Value = nodeId
                    }
                };
                node.References = [.. references];
                return;
            }
        }
    }
}
