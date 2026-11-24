// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter
{
    internal sealed partial class ExportModelMapping
    {
        /// <summary>
        /// Separates established native engineering bounds from payload validation constraints.
        /// </summary>
        private void ApplyEngineeringRange(
            JsonObject property, string href, string pointer, List<string> diagnostics)
        {
            if (String(property, "uav:id") is not { } identity)
            {
                return;
            }
            identity = CanonicalIdentity(identity);
            if (!m_ranges.TryGetValue(identity, out NativeNode? node))
            {
                return;
            }
            if (node is null)
            {
                throw new FormatException($"Multiple native EURange properties for '{identity}'.");
            }
            if (node.Node is UAVariable { Value: { } value })
            {
                var range = new JsonObject();
                foreach ((string xmlName, string member) in
                    (ValueTuple<string, string>[])[("Low", "minimum"), ("High", "maximum")])
                {
                    XmlNode? endpoint = value.GetElementsByTagName(xmlName,
                        "http://opcfoundation.org/UA/2008/02/Types.xsd").Item(0);
                    if (endpoint is null)
                    {
                        continue;
                    }
                    double number = XmlConvert.ToDouble(endpoint.InnerText);
                    if (double.IsNaN(number))
                    {
                        continue;
                    }
                    if (!double.IsFinite(number))
                    {
                        diagnostics.Add($"Unrepresentable EURange endpoint at {href}#{pointer}/uav:euRange/{member}; " +
                            $"the native value at '{node.Identity}' is retained.");
                        continue;
                    }
                    range[member] = number;
                    if (property[member] is { } existing &&
                        double.TryParse(existing.ToJsonString(), CultureInfo.InvariantCulture, out double bound) &&
                        bound == number)
                    {
                        property.Remove(member);
                    }
                }
                if (range["minimum"] is { } minimum && range["maximum"] is { } maximum &&
                    minimum.GetValue<double>() > maximum.GetValue<double>())
                {
                    throw new FormatException($"EURange minimum exceeds maximum at '{node.Identity}'.");
                }
                property["uav:euRange"] = range;
                return;
            }
        }

        /// <summary>
        /// Replaces display-valued unit pointers with explicit code-vocabulary associations.
        /// </summary>
        private void ApplyUnits(JsonObject root, string href, List<string> diagnostics)
        {
            if (root["properties"] is not JsonObject properties)
            {
                return;
            }
            foreach ((string key, JsonNode? value) in properties)
            {
                if (value is not JsonObject unit || unit["uav:engineeringUnits"] is not JsonObject information)
                {
                    continue;
                }
                const string cefact = "http://www.opcfoundation.org/UA/units/un/cefact";
                string? authority = String(information, "namespaceUri");
                unit.Remove("default");
                JsonObject? configured = String(unit, "uav:id") is { } unitIdentity
                    ? m_metadata.Find(CanonicalIdentity(unitIdentity)) : null;
                if (configured?["unitCodeSystem"] is { } vocabulary)
                {
                    unit["uav:unitCodeSystem"] = vocabulary.DeepClone();
                    unit["default"] = configured["unitCode"]!.DeepClone();
                }
                else if (authority == cefact)
                {
                    unit["uav:unitCodeSystem"] = cefact;
                    int unitId = information["unitId"]?.GetValue<int>() ?? -1;
                    if (unitId is > 0 and <= 0xFFFFFF)
                    {
                        var code = new StringBuilder();
                        for (int shift = 16; shift >= 0; shift -= 8)
                        {
                            int character = (unitId >> shift) & 0xFF;
                            if (character != 0 || code.Length != 0)
                            {
                                code.Append((char)character);
                            }
                        }
                        if (code.Length is >= 1 and <= 3 &&
                            code.ToString().All(char.IsAsciiLetterOrDigit))
                        {
                            unit["default"] = code.ToString();
                        }
                    }
                    if (!unit.ContainsKey("default"))
                    {
                        diagnostics.Add($"Unknown engineering-unit code at {href}#/properties/{Escape(key)}; " +
                            "localized display text is not a unit code.");
                    }
                }
                else
                {
                    diagnostics.Add($"Unavailable engineering-unit code vocabulary at " +
                        $"{href}#/properties/{Escape(key)}. No run-time unit association is asserted.");
                }
            }
            foreach ((string _, JsonNode? value) in properties)
            {
                if (value is not JsonObject property ||
                    String(property, "uav:unitProperty") is not { } pointer)
                {
                    continue;
                }
                property.Remove("uav:unitProperty");
                string key = pointer["/properties/".Length..]
                    .Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (properties[key] is JsonObject target && target.ContainsKey("uav:unitCodeSystem"))
                {
                    property["uav:withUnit"] = pointer;
                }
            }
        }
    }
}
