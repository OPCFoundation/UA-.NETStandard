// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Refuses a nested-only DataType selected directly by a projected
        /// Variable, Method argument or Event field.
        /// </summary>
        /// <remarks>
        /// A concrete Structure or Union that states
        /// <c>uav:hasDefaultEncoding: false</c> has a null
        /// <c>DefaultEncodingId</c> and no Binary encoding. Optional XML/JSON
        /// encoding Objects do not make it directly selectable. It exists only
        /// as a field of another Structure. Selecting it as the DataType of a
        /// Variable, an Argument or an Event field produces a Node whose value
        /// no client can encode, and the failure surfaces at run time as an
        /// unresolvable encoding rather than here, where the document that
        /// caused it is still in hand.
        /// </remarks>
        /// <param name="nestedOnly">The nested only.</param>
        /// <param name="items">The selected items checked against the nested-only DataTypes.</param>
        /// <param name="diagnostics">Collects a diagnostic for each refused selection.</param>
        internal static void ValidateNestedOnlySelection(
            HashSet<string> nestedOnly,
            List<UANode> items,
            List<WotDiagnostic> diagnostics)
        {
            if (nestedOnly.Count == 0)
            {
                return;
            }

            HashSet<string> refused = nestedOnly;
            foreach (UANode node in items)
            {
                if (node is not UAVariable variable)
                {
                    continue;
                }
                if (variable.DataType is not null && refused.Contains(variable.DataType))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DataTypeDefinitionInvalid,
                        $"'{variable.BrowseName}' selects the DataType " +
                        $"'{variable.DataType}' directly, but that type states " +
                        "uav:hasDefaultEncoding false: it has a null DefaultEncodingId " +
                        "and exists only as a field of another Structure, so no value " +
                        "of it can be encoded on its own.",
                        new WotLocation(reference: variable.BrowseName)));
                    continue;
                }
                ValidateArgumentSelection(variable, refused, diagnostics);
            }
        }

        /// <summary>
        /// Refuses a nested-only DataType named by one of the Arguments an
        /// InputArguments or OutputArguments Property carries.
        /// </summary>
        /// <remarks>
        /// An argument's DataType is not an attribute of any Node: it lives
        /// inside the encoded <c>Argument</c> structure, so the only place to
        /// see it is the value the Property holds.
        /// </remarks>
        private static void ValidateArgumentSelection(
            UAVariable variable,
            HashSet<string> refused,
            List<WotDiagnostic> diagnostics)
        {
            if (variable.Value is not System.Xml.XmlElement value)
            {
                return;
            }
            foreach (System.Xml.XmlElement argument in Descendants(value, "Argument"))
            {
                System.Xml.XmlElement? dataType = FirstChild(argument, "DataType");
                System.Xml.XmlElement? identifier =
                    dataType is null ? null : FirstChild(dataType, "Identifier");
                string? selected = identifier?.InnerText.Trim();
                if (selected is null || !refused.Contains(selected))
                {
                    continue;
                }
                string name = FirstChild(argument, "Name")?.InnerText ?? "?";
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DataTypeDefinitionInvalid,
                    $"The argument '{name}' of '{variable.BrowseName}' selects the " +
                    $"DataType '{selected}' directly, but that type states " +
                    "uav:hasDefaultEncoding false: it has a null DefaultEncodingId " +
                    "and exists only as a field of another Structure, so no value " +
                    "of it can be encoded on its own.",
                    new WotLocation(reference: variable.BrowseName)));
            }
        }

        private static List<System.Xml.XmlElement> Descendants(System.Xml.XmlElement root, string localName)
        {
            var found = new List<System.Xml.XmlElement>();
            var pending = new Stack<System.Xml.XmlElement>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                System.Xml.XmlElement current = pending.Pop();
                if (string.Equals(current.LocalName, localName, StringComparison.Ordinal))
                {
                    found.Add(current);
                }
                foreach (System.Xml.XmlNode child in current.ChildNodes)
                {
                    if (child is System.Xml.XmlElement element)
                    {
                        pending.Push(element);
                    }
                }
            }
            return found;
        }

        private static System.Xml.XmlElement? FirstChild(System.Xml.XmlElement parent, string localName)
        {
            foreach (System.Xml.XmlNode child in parent.ChildNodes)
            {
                if (child is System.Xml.XmlElement element &&
                    string.Equals(element.LocalName, localName, StringComparison.Ordinal))
                {
                    return element;
                }
            }
            return null;
        }
    }
}
