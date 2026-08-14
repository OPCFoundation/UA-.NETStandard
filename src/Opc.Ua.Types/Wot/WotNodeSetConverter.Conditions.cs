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
    /// The Alarms and Conditions mapping of WoT Binding Section 13.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The <c>uav</c> term naming the ConditionType an event projects, as a
        /// compact model name (WoT Binding Section 13.2).
        /// </summary>
        internal const string ConditionTypeTerm = "uav:conditionType";

        /// <summary>
        /// The <c>uav</c> term pinning the same ConditionType to a definitive
        /// ExpandedNodeId (WoT Binding Section 13.2).
        /// </summary>
        internal const string ConditionTypeIdTerm = "uav:conditionTypeId";

        /// <summary>
        /// The <c>uav</c> term naming the Condition Method an action invokes
        /// (WoT Binding Section 13.2).
        /// </summary>
        internal const string ConditionActionTerm = "uav:conditionAction";

        /// <summary>
        /// The <c>uav</c> term naming the event affordance whose Condition an
        /// action acts on (WoT Binding Section 13.2).
        /// </summary>
        internal const string ActsOnTerm = "uav:actsOn";

        /// <summary>
        /// The BrowseName of the field that names an Event occurrence.
        /// </summary>
        private const string EventIdField = "EventId";

        /// <summary>
        /// The closed set of Condition Methods this Binding maps (WoT Binding
        /// Section 13.2). It is closed, so anything else is a defect rather
        /// than an extension point.
        /// </summary>
        private static readonly string[] s_conditionActions =
            ["Acknowledge", "Confirm", "AddComment", "Enable", "Disable"];

        /// <summary>
        /// The Condition Methods that act on one Event occurrence and therefore
        /// need an <c>EventId</c> input (WoT Binding Section 13.4).
        /// <c>Enable</c> and <c>Disable</c> act on the Condition instance
        /// identified by the action target instead.
        /// </summary>
        private static readonly string[] s_occurrenceActions =
            ["Acknowledge", "Confirm", "AddComment"];

        /// <summary>
        /// Validates the Alarms and Conditions mapping of WoT Binding Section
        /// 13.
        /// </summary>
        /// <remarks>
        /// Every rule here exists because breaking it yields a document a
        /// consumer can read but cannot act on: a notification whose occurrence
        /// cannot be named, or an action with nothing to act upon. Each is an
        /// error rather than a warning, because Section 7 requires a consumer to
        /// reject an invalid document rather than repair it.
        /// </remarks>
        /// <param name="document">The document being converted.</param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        private static void ValidateConditions(
            WotDocument document,
            List<WotDiagnostic> diagnostics)
        {
            var conditionEvents = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object ||
                    !HasNonEmptyString(affordance.Value, ConditionTypeTerm) &&
                    !HasNonEmptyString(affordance.Value, ConditionTypeIdTerm))
                {
                    continue;
                }

                conditionEvents.Add(affordance.Key);
                string pointer = "/events/" + affordance.Key;

                // Section 13.3: a Condition notification a consumer cannot tie
                // back to an occurrence cannot be acknowledged, confirmed or
                // commented on, so EventId is what makes the event actionable.
                if (!DeclaresDataField(affordance.Value, EventIdField))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionEventIdMissing,
                        $"An event affordance carrying '{ConditionTypeTerm}' shall declare " +
                        $"'{EventIdField}' in its 'data' object (WoT Binding Section 13.3).",
                        WotLocation.FromPointer(pointer)));
                }
            }

            foreach (KeyValuePair<string, JsonElement> affordance in document.Actions)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object ||
                    !TryGetNonEmptyString(
                        affordance.Value, ConditionActionTerm, out string action))
                {
                    continue;
                }

                string pointer = "/actions/" + affordance.Key;

                if (Array.IndexOf(s_conditionActions, action) < 0)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidConditionAction,
                        $"'{action}' is not a Condition Method this Binding maps. The set is " +
                        $"closed: {string.Join(", ", s_conditionActions)} " +
                        "(WoT Binding Section 13.2).",
                        WotLocation.FromPointer(pointer + "/" + ConditionActionTerm)));
                }

                // Section 13.4: a Condition Method acts on a Condition, so an
                // action that does not name one cannot be invoked at all.
                if (!TryGetNonEmptyString(affordance.Value, ActsOnTerm, out string actsOn))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidConditionTarget,
                        $"An action carrying '{ConditionActionTerm}' shall carry '{ActsOnTerm}' " +
                        "naming the event affordance whose Condition it acts on " +
                        "(WoT Binding Section 13.4).",
                        WotLocation.FromPointer(pointer)));
                }
                else if (!conditionEvents.Contains(actsOn))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.InvalidConditionTarget,
                        $"'{ActsOnTerm}' names '{actsOn}', which is not an event affordance in " +
                        $"this document carrying '{ConditionTypeTerm}' " +
                        "(WoT Binding Section 13.4).",
                        WotLocation.FromPointer(pointer + "/" + ActsOnTerm)));
                }

                if (Array.IndexOf(s_occurrenceActions, action) >= 0 &&
                    !DeclaresInput(affordance.Value, EventIdField))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionActionInputMissing,
                        $"A '{action}' action shall declare '{EventIdField}' as an input, which " +
                        "binds the invocation to the notification the consumer received " +
                        "(WoT Binding Section 13.4).",
                        WotLocation.FromPointer(pointer)));
                }
            }
        }

        /// <summary>
        /// Gets whether an affordance declares a named field in its
        /// <c>data</c> schema.
        /// </summary>
        private static bool DeclaresDataField(JsonElement affordance, string field)
        {
            return affordance.TryGetProperty("data", out JsonElement data) &&
                DeclaresSchemaMember(data, field);
        }

        /// <summary>
        /// Gets whether an action declares a named field in its <c>input</c>
        /// schema.
        /// </summary>
        private static bool DeclaresInput(JsonElement affordance, string field)
        {
            return affordance.TryGetProperty("input", out JsonElement input) &&
                DeclaresSchemaMember(input, field);
        }

        /// <summary>
        /// Gets whether a DataSchema declares a named member.
        /// </summary>
        /// <remarks>
        /// A single-argument input may be written as the bare schema rather than
        /// as an object with <c>properties</c>, so a schema whose
        /// <c>uav:browseName</c> or <c>title</c> is the field name counts too.
        /// </remarks>
        private static bool DeclaresSchemaMember(JsonElement schema, string field)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (schema.TryGetProperty("properties", out JsonElement properties) &&
                properties.ValueKind == JsonValueKind.Object &&
                properties.TryGetProperty(field, out _))
            {
                return true;
            }
            return NameMatches(schema, "uav:browseName", field) ||
                NameMatches(schema, "title", field);
        }

        /// <summary>
        /// Gets whether a schema member names the supplied field, allowing the
        /// prefixed and NamespaceUri-qualified BrowseName forms.
        /// </summary>
        private static bool NameMatches(JsonElement schema, string term, string field)
        {
            return TryGetNonEmptyString(schema, term, out string value) &&
                string.Equals(LocalName(value), field, StringComparison.Ordinal);
        }

        private static bool HasNonEmptyString(JsonElement element, string term)
        {
            return TryGetNonEmptyString(element, term, out _);
        }

        private static bool TryGetNonEmptyString(
            JsonElement element,
            string term,
            out string value)
        {
            if (element.TryGetProperty(term, out JsonElement found) &&
                found.ValueKind == JsonValueKind.String &&
                found.GetString() is { Length: > 0 } text &&
                !string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
