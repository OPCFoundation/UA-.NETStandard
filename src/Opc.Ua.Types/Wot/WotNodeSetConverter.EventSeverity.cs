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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The event-severity mapping of WoT Binding Section 6.6.
    /// </summary>
    /// <remarks>
    /// <c>uav:severity</c> is the default OPC 10000-5 <c>Severity</c> a server
    /// publishes for occurrences of an event when the source of the occurrence
    /// supplies none. In an OPC UA model that default is not free-standing
    /// metadata: it is the value of the EventType's own <c>Severity</c>
    /// Property, which <c>BaseEventType</c> declares and every derived type
    /// inherits. So the term maps to exactly one semantic fact in each
    /// direction - the Property's authored default value - and is emitted only
    /// where the source NodeSet supplies it.
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The <c>uav</c> term carrying an event affordance's default
        /// <c>Severity</c> (WoT Binding Section 6.6).
        /// </summary>
        internal const string SeverityTerm = "uav:severity";

        /// <summary>
        /// The BrowseName of the <c>BaseEventType</c> Property that holds the
        /// severity, in the base OPC UA namespace (OPC 10000-5).
        /// </summary>
        internal const string SeverityBrowseName = "Severity";

        /// <summary>
        /// The inclusive range OPC 10000-5 defines for
        /// <c>BaseEventType.Severity</c>.
        /// </summary>
        /// <remarks>
        /// The same range the legacy asset registry enforces. A value outside
        /// it is invalid rather than clamped: clamping would publish a severity
        /// the author never wrote, and WoT Binding Section 7 requires a
        /// consumer to reject an invalid document rather than repair it.
        /// </remarks>
        internal const int MinimumSeverity = 1;

        /// <inheritdoc cref="MinimumSeverity"/>
        internal const int MaximumSeverity = 1000;

        /// <summary>
        /// The DataType of <c>BaseEventType.Severity</c> (<c>UInt16</c>).
        /// </summary>
        private const string SeverityDataType = "i=5";

        /// <summary>
        /// Gets whether a value is a severity OPC 10000-5 admits.
        /// </summary>
        internal static bool IsSeverityInRange(int severity)
        {
            return severity is >= MinimumSeverity and <= MaximumSeverity;
        }

        /// <summary>
        /// Reads the authored <c>uav:severity</c> of an affordance, when it
        /// states one this converter maps.
        /// </summary>
        /// <remarks>
        /// A value outside the OPC 10000-5 range, or one that is not an integer
        /// at all, is not mapped here. It is reported by
        /// <see cref="ValidateSeverity"/> and left to residue preservation, so
        /// an invalid document neither loses the value nor materializes it.
        /// </remarks>
        internal static bool TryReadSeverity(JsonElement affordance, out ushort severity)
        {
            severity = 0;
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty(SeverityTerm, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number ||
                !IsIntegerLiteral(value) ||
                !value.TryGetInt32(out int authored) ||
                !IsSeverityInRange(authored))
            {
                return false;
            }
            severity = (ushort)authored;
            return true;
        }

        /// <summary>
        /// Writes the default severity an EventType authors, where it authors
        /// one (WoT Binding Section 6.6).
        /// </summary>
        /// <remarks>
        /// The fact lives on the EventType's <c>Severity</c> Property, so the
        /// term is emitted only when that Property exists, carries a value and
        /// that value is in range. A Property whose value is out of range is
        /// not written as a term the specification would then make the document
        /// invalid for; it stays a Node the preservation projection carries.
        /// </remarks>
        private static void WriteEventSeverity(
            Utf8JsonWriter writer,
            UANode eventType,
            Dictionary<string, UANode> index)
        {
            if (TryReadSeverityProperty(eventType, index, out ushort severity))
            {
                writer.WriteNumber(SeverityTerm, severity);
            }
        }

        /// <summary>
        /// Gets whether a NodeSet BrowseName names the given Node of the base
        /// OPC UA namespace.
        /// </summary>
        /// <remarks>
        /// A NodeSet writes a base-namespace BrowseName without a prefix, or
        /// with the explicit index <c>0</c>. Comparing the local name alone
        /// would accept a vendor's own <c>1:Severity</c> or
        /// <c>1:InputArguments</c>, which is a different QualifiedName standing
        /// for something this converter knows nothing about.
        /// </remarks>
        private static bool IsBaseNamespaceBrowseName(string? browseName, string name)
        {
            return string.Equals(browseName, name, StringComparison.Ordinal) ||
                string.Equals(browseName, "0:" + name, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads the authored value of an EventType's <c>Severity</c> Property.
        /// </summary>
        private static bool TryReadSeverityProperty(
            UANode eventType,
            Dictionary<string, UANode> index,
            out ushort severity)
        {
            severity = 0;
            foreach (Reference reference in eventType.References ?? [])
            {
                if (!reference.IsForward ||
                    reference.Value is null ||
                    !IsComponentReference(reference.ReferenceType) ||
                    !index.TryGetValue(reference.Value, out UANode? target) ||
                    target is not UAVariable property ||
                    !IsBaseNamespaceBrowseName(property.BrowseName, SeverityBrowseName))
                {
                    continue;
                }
                return TryReadSeverityValue(property.Value, out severity);
            }
            return false;
        }

        /// <summary>
        /// Reads a <c>UInt16</c> NodeSet value fragment as a severity.
        /// </summary>
        private static bool TryReadSeverityValue(
            System.Xml.XmlElement? value,
            out ushort severity)
        {
            severity = 0;
            if (value is null ||
                !string.Equals(value.LocalName, "UInt16", StringComparison.Ordinal) ||
                !ushort.TryParse(
                    value.InnerText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort authored) ||
                !IsSeverityInRange(authored))
            {
                return false;
            }
            severity = authored;
            return true;
        }

        /// <summary>
        /// Materializes an event affordance's authored severity as the
        /// EventType's <c>Severity</c> Property.
        /// </summary>
        /// <remarks>
        /// <c>BaseEventType</c> already declares the Property, so what is
        /// created here is the derived type's own declaration carrying the
        /// authored default. Nothing is created for an affordance that authors
        /// no severity: an EventType without one inherits the Property and the
        /// server applies its own default, which is what Section 6.6 says an
        /// omitted term means.
        /// </remarks>
        private static void SynthesizeEventSeverity(
            JsonElement eventAffordance,
            string eventNodeId,
            string eventLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> eventReferences)
        {
            if (!TryReadSeverity(eventAffordance, out ushort severity))
            {
                return;
            }

            string nodeId = GenerateNodeId(
                rootLocal + "/" + eventLocal + "/" + SeverityBrowseName);
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement value = document.CreateElement(
                "uax", "UInt16", UaXmlNamespace);
            value.InnerText = severity.ToString(CultureInfo.InvariantCulture);

            items.Add(new UAVariable
            {
                NodeId = nodeId,
                BrowseName = SeverityBrowseName,
                DisplayName = MakeText(SeverityBrowseName),
                ParentNodeId = eventNodeId,
                DataType = SeverityDataType,
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
                        Value = WotVocabulary.ModellingRuleMandatory
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = eventNodeId
                    }
                ]
            });

            eventReferences.Add(new Reference
            {
                ReferenceType = "HasProperty",
                IsForward = true,
                Value = nodeId
            });
        }
    }
}
