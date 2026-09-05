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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The Alarms and Conditions mapping of WoT Binding Section 13.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 13 ties two affordance kinds together. The notification is an
    /// event affordance whose <c>data</c> object carries the Condition state,
    /// and each Condition Method is an action affordance that names the event
    /// it acts on. This file owns both directions of that pairing: it derives
    /// the <c>data</c> schema, <c>uav:conditionType</c> and the action
    /// annotations from a NodeSet, and it materializes the EventType fields
    /// and Method declarations from an authored document.
    /// </para>
    /// <para>
    /// The Condition state fields are not attributes of the EventType being
    /// converted: OPC 10000-9 declares them on <c>ConditionType</c> and its
    /// subtypes, which a converted NodeSet almost never contains. So the
    /// fields a type inherits come from the table below - the same table both
    /// directions read - and only the fields a type <em>adds</em> are read
    /// from, or written to, Nodes.
    /// </para>
    /// </remarks>
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
        /// The BrowseName of the <c>BaseEventType</c> field that carries the
        /// occurrence severity, in the base OPC UA namespace (OPC 10000-5).
        /// </summary>
        /// <remarks>
        /// <c>Severity</c> is one of the eight mandatory <c>BaseEventType</c>
        /// fields, so it is part of the notification data schema and of the
        /// implicit select-clause set. It is a field of an occurrence, not
        /// free-standing affordance metadata: WoT Binding 1.1 defines no term
        /// that states a default severity, and none is synthesized.
        /// </remarks>
        internal const string SeverityBrowseName = "Severity";

        /// <summary>
        /// The inclusive range OPC 10000-5 defines for
        /// <c>BaseEventType.Severity</c>, which bounds the generated schema of
        /// the <c>Severity</c> notification field.
        /// </summary>
        internal const int MinimumSeverity = 1;

        /// <inheritdoc cref="MinimumSeverity"/>
        internal const int MaximumSeverity = 1000;

        /// <summary>
        /// The WoT member carrying an event affordance's notification schema.
        /// </summary>
        internal const string DataMember = "data";

        /// <summary>
        /// The W3C DataSchema member holding an object schema's members, which
        /// is what a select clause materializes into (WoT Binding Section 6.1).
        /// </summary>
        internal const string PropertiesMember = "properties";

        /// <summary>
        /// How a standard event field is rendered as a WoT DataSchema.
        /// </summary>
        /// <remarks>
        /// The kind, not the DataType NodeId, is what the readable schema
        /// states: Section 9.1 gives a DataType one readable channel, and
        /// several OPC UA DataTypes share it. A NodeId, a String, a
        /// LocalizedText and a StatusCode are all a json <c>string</c>; what
        /// separates a ByteString or a UtcTime from them is the
        /// <c>contentEncoding</c> or <c>format</c> refinement, which is why
        /// those are kinds of their own.
        /// </remarks>
        private enum WotEventFieldKind
        {
            /// <summary>A ByteString, carried base64-encoded.</summary>
            ByteString,

            /// <summary>A NodeId, String, LocalizedText or StatusCode.</summary>
            Text,

            /// <summary>A UtcTime.</summary>
            UtcTime,

            /// <summary>A Severity: an integer OPC 10000-5 bounds 1..1000.</summary>
            Severity,

            /// <summary>A Boolean.</summary>
            Flag,

            /// <summary>A Double.</summary>
            Analog,

            /// <summary>
            /// A two-state Variable, whose own value is the localized state
            /// name and whose <c>Id</c> sub-Variable carries the Boolean
            /// (WoT Binding Section 6.1).
            /// </summary>
            TwoState
        }

        /// <summary>
        /// One field a standard EventType declares.
        /// </summary>
        /// <param name="Name">The BrowseName, in the base OPC UA namespace.</param>
        /// <param name="Kind">How the field is rendered as a DataSchema.</param>
        /// <param name="Required">
        /// Whether every notification of the type carries the field. Section
        /// 13.3 makes the eight mandatory <c>BaseEventType</c> fields and the
        /// Condition identity and state fields required; the state a subtype
        /// adds is present but not required, exactly as the worked example of
        /// Section 13.5 states it.
        /// </param>
        private readonly record struct WotStandardEventField(
            string Name,
            WotEventFieldKind Kind,
            bool Required);

        /// <summary>
        /// One standard EventType and the fields it declares.
        /// </summary>
        private readonly record struct WotStandardEventType(
            string NodeId,
            string BrowseName,
            string SuperTypeNodeId,
            WotStandardEventField[] Fields);

        /// <summary>
        /// The EventTypes whose fields this Binding knows without being handed
        /// the OPC UA base NodeSet: <c>BaseEventType</c> and the four
        /// ConditionTypes Section 13.1 scopes the mapping to.
        /// </summary>
        /// <remarks>
        /// Each entry lists only what its own type declares, so the effective
        /// field list of a type is the concatenation of its supertype chain,
        /// base first. That is the "stable declaration and inheritance order"
        /// both directions rely on: it does not depend on dictionary order,
        /// on JSON member order, or on which Nodes a particular NodeSet
        /// happens to contain.
        /// </remarks>
        private static readonly WotStandardEventType[] s_standardEventTypes =
        [
            new(
                WotVocabulary.BaseEventType,
                "BaseEventType",
                string.Empty,
                [
                    new(EventIdField, WotEventFieldKind.ByteString, true),
                    new("EventType", WotEventFieldKind.Text, true),
                    new("SourceNode", WotEventFieldKind.Text, true),
                    new("SourceName", WotEventFieldKind.Text, true),
                    new("Time", WotEventFieldKind.UtcTime, true),
                    new("ReceiveTime", WotEventFieldKind.UtcTime, true),
                    new("Message", WotEventFieldKind.Text, true),
                    new(SeverityBrowseName, WotEventFieldKind.Severity, true)
                ]),
            new(
                WotVocabulary.ConditionType,
                "ConditionType",
                WotVocabulary.BaseEventType,
                [
                    // ConditionId is the NodeId Attribute of the Condition
                    // instance rather than a Variable of the type, which is
                    // why Section 6.1 selects it with the empty browse path.
                    // It is a data member all the same, and the one a consumer
                    // needs to address the Condition.
                    new(ConditionIdField, WotEventFieldKind.Text, true),
                    new("ConditionName", WotEventFieldKind.Text, true),
                    new("BranchId", WotEventFieldKind.Text, true),
                    new("Retain", WotEventFieldKind.Flag, true),
                    new("ConditionClassId", WotEventFieldKind.Text, true),
                    new("ConditionClassName", WotEventFieldKind.Text, true),
                    new("Quality", WotEventFieldKind.Text, true),
                    new("LastSeverity", WotEventFieldKind.Severity, true),
                    new(CommentField, WotEventFieldKind.Text, true),
                    new("ClientUserId", WotEventFieldKind.Text, true),
                    new("EnabledState", WotEventFieldKind.TwoState, true)
                ]),
            new(
                WotVocabulary.AcknowledgeableConditionType,
                "AcknowledgeableConditionType",
                WotVocabulary.ConditionType,
                [
                    new("AckedState", WotEventFieldKind.TwoState, false),
                    new("ConfirmedState", WotEventFieldKind.TwoState, false)
                ]),
            new(
                WotVocabulary.AlarmConditionType,
                "AlarmConditionType",
                WotVocabulary.AcknowledgeableConditionType,
                [
                    new("ActiveState", WotEventFieldKind.TwoState, false),
                    new("InputNode", WotEventFieldKind.Text, false),
                    new("SuppressedOrShelved", WotEventFieldKind.Flag, false)
                ]),
            new(
                WotVocabulary.LimitAlarmType,
                "LimitAlarmType",
                WotVocabulary.AlarmConditionType,
                [
                    new("HighHighLimit", WotEventFieldKind.Analog, false),
                    new("HighLimit", WotEventFieldKind.Analog, false),
                    new("LowLimit", WotEventFieldKind.Analog, false),
                    new("LowLowLimit", WotEventFieldKind.Analog, false)
                ])
        ];

        /// <summary>
        /// The BrowseName of the Condition field, and Condition Method
        /// argument, carrying the LocalizedText a client applies to a
        /// Condition.
        /// </summary>
        private const string CommentField = "Comment";

        /// <summary>
        /// The <c>data</c> member naming the Condition instance an occurrence
        /// belongs to (WoT Binding Section 13.3).
        /// </summary>
        private const string ConditionIdField = "ConditionId";

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
        /// The argument order OPC 10000-9 gives every occurrence-level
        /// Condition Method: <c>EventId</c> then <c>Comment</c>.
        /// </summary>
        /// <remarks>
        /// <c>Acknowledge</c>, <c>Confirm</c> and <c>AddComment</c> have the
        /// same fixed signature, so an action that invokes one states which
        /// arguments it declares but never has to restate the order OPC 10000-9
        /// already fixes. That is what lets Section 13.4's own shape - an
        /// <c>input</c> declaring <c>EventId</c> and an optional
        /// <c>Comment</c> - materialize without a <c>uav:fieldOrder</c>, while
        /// an ordinary Method with two arguments still has to state one.
        /// </remarks>
        private static readonly string[] s_conditionMethodArguments = [EventIdField, CommentField];

        /// <summary>
        /// The Method declaration OPC 10000-9 gives each Condition Method, and
        /// the ConditionType that declares it.
        /// </summary>
        /// <param name="Action">The <c>uav:conditionAction</c> value.</param>
        /// <param name="DeclaringTypeNodeId">
        /// The ConditionType that declares the Method. An event whose projected
        /// ConditionType is neither this type nor a subtype of it does not have
        /// the Method at all, so pairing an action with it is a contradiction
        /// rather than a detail.
        /// </param>
        /// <param name="MethodNodeId">
        /// The declaration's own NodeId, which a materialized instance Method
        /// carries as its <c>MethodDeclarationId</c>.
        /// </param>
        private readonly record struct WotConditionMethod(
            string Action,
            string DeclaringTypeNodeId,
            string MethodNodeId);

        /// <inheritdoc cref="WotConditionMethod"/>
        private static readonly WotConditionMethod[] s_conditionMethods =
        [
            new(
                "Acknowledge",
                WotVocabulary.AcknowledgeableConditionType,
                WotVocabulary.AcknowledgeableConditionTypeAcknowledgeMethod),
            new(
                "Confirm",
                WotVocabulary.AcknowledgeableConditionType,
                WotVocabulary.AcknowledgeableConditionTypeConfirmMethod),
            new(
                "AddComment",
                WotVocabulary.ConditionType,
                WotVocabulary.ConditionTypeAddCommentMethod),
            new(
                "Enable",
                WotVocabulary.ConditionType,
                WotVocabulary.ConditionTypeEnableMethod),
            new(
                "Disable",
                WotVocabulary.ConditionType,
                WotVocabulary.ConditionTypeDisableMethod)
        ];

        /// <summary>
        /// Gets whether the converter maps an event affordance's <c>data</c>
        /// member onto EventType fields, which is what decides whether
        /// preservation must also carry it.
        /// </summary>
        /// <remarks>
        /// A member the converter materializes must not also be captured as
        /// residue, or the same fields would be stated twice - once as the
        /// Nodes the NodeSet gained and once as an Extension re-applied over
        /// the document generated from it. The predicate mirrors what
        /// <c>SynthesizeEventFields</c> actually materializes and nothing
        /// wider: that materializer reads <c>data.properties</c>, so a
        /// <c>data</c> that is a scalar, that is an object without
        /// <c>properties</c>, or whose <c>properties</c> is not an object,
        /// produces no Node at all and is kept verbatim. A predicate that
        /// answered for the shape rather than for the materializer would
        /// silently drop exactly those documents.
        /// </remarks>
        internal static bool MapsEventDataSchema(JsonElement affordance)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                affordance.TryGetProperty(DataMember, out JsonElement data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(PropertiesMember, out JsonElement properties) &&
                properties.ValueKind == JsonValueKind.Object;
        }

        /// <summary>
        /// Gets whether the converter restates an event affordance's Condition
        /// annotation exactly as the document writes it.
        /// </summary>
        /// <remarks>
        /// The forward direction derives the annotation from the supertype of
        /// the projected EventType, which it can only name for the four
        /// ConditionTypes Section 13.1 scopes, and it always writes the compact
        /// name in the <c>ua:</c> form together with the base-namespace pin. A
        /// document that says exactly that is re-derived byte for byte and is
        /// not preserved; anything else - a companion ConditionType, a second
        /// prefix bound to the OPC UA namespace, a pin that disagrees - is not
        /// re-derivable and is carried verbatim.
        /// </remarks>
        internal static bool MapsConditionType(JsonElement affordance)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !TryGetNonEmptyString(affordance, ConditionTypeTerm, out string compactName) ||
                !compactName.StartsWith("ua:", StringComparison.Ordinal) ||
                !WotVocabulary.TryGetConditionTypeNodeId(
                    compactName.Substring(3), out string nodeId))
            {
                return false;
            }
            return !TryGetNonEmptyString(affordance, ConditionTypeIdTerm, out string pinned) ||
                string.Equals(pinned, nodeId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether the converter restates an action affordance's Condition
        /// pairing from the NodeSet it materializes.
        /// </summary>
        /// <remarks>
        /// The pairing is recorded structurally: the Method takes the standard
        /// BrowseName OPC 10000-9 declares the named Condition Method with, and
        /// it is a component of the EventType the pairing names. Both terms are
        /// read back from that structure. A <c>uav:conditionAction</c> outside
        /// the closed set of Section 13.2, or one the document overrides with a
        /// BrowseName of its own, is not recorded that way and is carried
        /// verbatim instead of being lost along with the diagnostic that
        /// reports it.
        /// </remarks>
        internal static bool MapsConditionAction(JsonElement affordance)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                TryGetNonEmptyString(affordance, ConditionActionTerm, out string action) &&
                IsMappedConditionAction(action) &&
                HasNonEmptyString(affordance, ActsOnTerm) &&
                IsStandardConditionMethodName(affordance, action);
        }

        /// <summary>
        /// Gets whether a <c>uav:conditionAction</c> names one of the Condition
        /// Methods Section 13.2 closes the set to.
        /// </summary>
        private static bool IsMappedConditionAction(string action)
        {
            return Array.IndexOf(s_conditionActions, action) >= 0;
        }

        /// <summary>
        /// Resolves the argument order of a Condition Method from the Method
        /// OPC 10000-9 defines, where the action invokes one.
        /// </summary>
        /// <param name="action">The action affordance.</param>
        /// <param name="declared">The members its argument schema declares.</param>
        /// <param name="order">The declared members in OPC 10000-9 order.</param>
        /// <returns>
        /// <c>true</c> when the action invokes an occurrence-level Condition
        /// Method and declares only arguments that Method takes.
        /// </returns>
        private static bool TryGetConditionArgumentOrder(
            JsonElement action,
            List<string> declared,
            out List<string> order)
        {
            order = [];
            if (!TryGetNonEmptyString(action, ConditionActionTerm, out string conditionAction) ||
                Array.IndexOf(s_occurrenceActions, conditionAction) < 0)
            {
                return false;
            }
            foreach (string name in s_conditionMethodArguments)
            {
                if (declared.Contains(name))
                {
                    order.Add(name);
                }
            }
            if (order.Count != declared.Count)
            {
                // The action declares something the Condition Method does not
                // take, so OPC 10000-9 says nothing about where it goes.
                order = [];
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reports every event affordance whose field selection this conversion
        /// could not resolve (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// An affordance that names an EventType definition with <c>tm:ref</c>,
        /// or that writes explicit <c>uav:eventSelectClauses</c>, states a
        /// selection that is derived by reading the definition it names — a
        /// document, reached through a link. The synchronous conversion never
        /// dereferences one, so it holds no selection to read and would
        /// otherwise materialize an EventType with none of the fields the
        /// definition declares and say nothing about it. It says so here
        /// instead, once per affordance, and names the two ways to convert the
        /// document. Where resolution did run, this reports nothing: a link
        /// that failed to resolve is reported by the resolver, with the reason
        /// it failed, and is not restated here as a missing capability.
        /// </remarks>
        /// <param name="document">The document being converted.</param>
        /// <param name="eventSelectionsResolved">
        /// Whether the caller resolved the document's event selections before
        /// the synthesis.
        /// </param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        private static void ValidateEventSelectionsResolved(
            WotDocument document,
            bool eventSelectionsResolved,
            List<WotDiagnostic> diagnostics)
        {
            if (eventSelectionsResolved)
            {
                return;
            }
            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (!WotEventSelectionResolver.StatesSelection(affordance.Value))
                {
                    continue;
                }
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.EventSelectionUnresolved,
                    $"The event affordance '{affordance.Key}' states its field selection with " +
                    $"'{WotEventSelectClauses.TypeDefinitionReferenceTerm}' or " +
                    $"'{WotEventSelectClauses.Term}', and this conversion holds no resolved " +
                    "selection for it. Convert with " +
                    $"{nameof(ToNodeSetResultAsync)} passing an " +
                    $"{nameof(IWotThingResolver)} - " +
                    $"{nameof(NullWotResolver)}.{nameof(NullWotResolver.Instance)} where every " +
                    "reference is local to this document - or declare the affordance's fields " +
                    "in its own 'data' object and state no selection, which takes the implicit " +
                    "BaseEventType default (WoT Binding Sections 5.1.5 and 6.1).",
                    WotLocation.FromPointer(
                        "/events/" + EscapeJsonPointerToken(affordance.Key))));
            }
        }

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
        /// <param name="eventSelections">
        /// The resolved event field selections of WoT Binding Section 6.1, or
        /// <c>null</c> where no resolver held the documents the affordances
        /// link to.
        /// </param>
        /// <param name="diagnostics">The diagnostics sink.</param>
        private static void ValidateConditions(
            WotDocument document,
            WotEventSelectionCatalog? eventSelections,
            List<WotDiagnostic> diagnostics)
        {
            var conditionEvents = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonElement> affordance in document.Events)
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object ||
                    (!HasNonEmptyString(affordance.Value, ConditionTypeTerm) &&
                        !HasNonEmptyString(affordance.Value, ConditionTypeIdTerm)))
                {
                    continue;
                }

                conditionEvents.Add(affordance.Key);
                string pointer = "/events/" + affordance.Key;

                // Section 13.3: a Condition notification a consumer cannot tie
                // back to an occurrence cannot be acknowledged, confirmed or
                // commented on, so EventId is what makes the event actionable.
                // It is the one hard requirement: every other Condition field
                // is present in 'data' where the affordance selects it and is
                // not otherwise required.
                if (!DeclaresDataField(affordance.Value, EventIdField))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionEventIdMissing,
                        $"An event affordance carrying '{ConditionTypeTerm}' shall declare " +
                        $"'{EventIdField}' in its 'data' object (WoT Binding Section 13.3).",
                        WotLocation.FromPointer(pointer)));
                }
                else if (eventSelections is not null &&
                    !SelectsConditionEventId(
                        affordance.Key, affordance.Value, eventSelections))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionEventIdMissing,
                        $"An event affordance carrying '{ConditionTypeTerm}' shall select " +
                        $"'{EventIdField}' as well as declare it: the resolved selection of " +
                        "WoT Binding Section 6.1 decides what a notification carries, so a " +
                        "selection that omits the field describes a notification that never " +
                        "carries it (WoT Binding Section 13.3).",
                        WotLocation.FromPointer(
                            pointer + "/" + EscapeJsonPointerToken(WotEventSelectClauses.Term))));
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
        /// Gets whether an event affordance's resolved selection materializes
        /// the <c>EventId</c> member (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <remarks>
        /// An affordance that states no selection takes the implicit
        /// <c>BaseEventType</c> default, which always carries <c>EventId</c>.
        /// An affordance that states one is decided by the resolved selection,
        /// because the overlay of Section 6.1 — a linked EventType's baseline
        /// refined by the explicit clauses — is what a MonitoredItem is
        /// actually created with. A resolved catalog that holds no entry for
        /// the affordance means its link did not resolve, which the resolver
        /// reports on its own; it is not double-reported here as a missing
        /// field. Where no catalog was resolved at all the question is not
        /// asked: nothing is known about the selection, so nothing is claimed
        /// about it, and <see cref="ValidateEventSelectionsResolved"/> reports
        /// the unresolved selection itself.
        /// </remarks>
        private static bool SelectsConditionEventId(
            string affordanceName,
            JsonElement affordance,
            WotEventSelectionCatalog eventSelections)
        {
            if (!WotEventSelectionResolver.StatesSelection(affordance))
            {
                return true;
            }
            if (!eventSelections.TryGetSelection(
                affordanceName, out ArrayOf<WotResolvedEventSelectClause> clauses))
            {
                return true;
            }
            for (int ii = 0; ii < clauses.Count; ii++)
            {
                if (string.Equals(clauses[ii].FieldName, EventIdField, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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

        // ------------------------------------------------------------------
        // NodeSet -> WoT
        // ------------------------------------------------------------------

        /// <summary>
        /// What an EventType projects: the ConditionType it derives from, if
        /// any, and the standard EventType whose fields it inherits.
        /// </summary>
        /// <param name="CompactName">
        /// The <c>uav:conditionType</c> compact model name, or <c>null</c> when
        /// the type is not a Condition this Binding recognises.
        /// </param>
        /// <param name="DefinitiveId">
        /// The <c>uav:conditionTypeId</c> pin naming the same type.
        /// </param>
        /// <param name="StandardTypeNodeId">
        /// The most derived standard EventType the supertype chain reaches.
        /// Every EventType derives from <c>BaseEventType</c>, so this is never
        /// empty and the eight mandatory base fields are always known.
        /// </param>
        private readonly record struct WotConditionProjection(
            string? CompactName,
            string? DefinitiveId,
            string StandardTypeNodeId)
        {
            /// <summary>
            /// Gets whether the EventType projects an OPC 10000-9 Condition.
            /// </summary>
            public bool IsCondition => CompactName is not null;
        }

        /// <summary>
        /// Resolves the ConditionType an EventType projects by walking its
        /// supertype chain to the first type this Binding knows.
        /// </summary>
        /// <remarks>
        /// The walk stops at the first standard type it reaches, which is the
        /// most derived one: a type deriving from <c>LimitAlarmType</c>
        /// projects <c>ua:LimitAlarmType</c>, not <c>ua:ConditionType</c>,
        /// even though both are supertypes. A chain that leaves the NodeSet
        /// through an identifier this Binding does not know is not guessed at:
        /// the type keeps the <c>BaseEventType</c> field set every EventType
        /// has and states no ConditionType, because naming one would assert a
        /// derivation the source never stated.
        /// </remarks>
        private static WotConditionProjection ResolveConditionProjection(
            UANode eventType,
            Dictionary<string, UANode> index)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            UANode? current = eventType;
            while (current is not null)
            {
                if (current.NodeId is { Length: > 0 } nodeId && !visited.Add(nodeId))
                {
                    // A NodeSet may state a derivation cycle. Stop rather than
                    // walk it forever; the base field set still holds.
                    break;
                }
                string? superType = SuperTypeOf(current);
                if (superType is null)
                {
                    break;
                }
                if (WotVocabulary.TryGetConditionTypeName(
                    superType, out string conditionTypeName))
                {
                    return new WotConditionProjection(
                        "ua:" + conditionTypeName, superType, superType);
                }
                if (string.Equals(
                    superType, WotVocabulary.BaseEventType, StringComparison.Ordinal))
                {
                    break;
                }
                if (!index.TryGetValue(superType, out UANode? declared))
                {
                    break;
                }
                current = declared;
            }
            return new WotConditionProjection(null, null, WotVocabulary.BaseEventType);
        }

        /// <summary>
        /// Reads the supertype a Node derives from.
        /// </summary>
        private static string? SuperTypeOf(UANode node)
        {
            foreach (Reference reference in node.References ?? [])
            {
                if (!reference.IsForward &&
                    reference.Value is { Length: > 0 } target &&
                    (string.Equals(reference.ReferenceType, "HasSubtype", StringComparison.Ordinal) ||
                        string.Equals(
                            reference.ReferenceType, WotVocabulary.HasSubtype, StringComparison.Ordinal)))
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>
        /// Collects the fields a standard EventType declares together with
        /// everything it inherits, base first.
        /// </summary>
        private static List<WotStandardEventField> CollectStandardEventFields(
            string standardTypeNodeId)
        {
            var chain = new List<WotStandardEventType>();
            string? current = standardTypeNodeId;
            while (!string.IsNullOrEmpty(current))
            {
                bool found = false;
                foreach (WotStandardEventType entry in s_standardEventTypes)
                {
                    if (string.Equals(entry.NodeId, current, StringComparison.Ordinal))
                    {
                        chain.Insert(0, entry);
                        current = entry.SuperTypeNodeId;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    break;
                }
            }
            var fields = new List<WotStandardEventField>();
            foreach (WotStandardEventType entry in chain)
            {
                fields.AddRange(entry.Fields);
            }
            return fields;
        }

        /// <summary>
        /// Gets the BrowseNames of every field the standard EventTypes declare,
        /// used where a Condition's exact type is pinned to an identifier this
        /// Binding cannot resolve.
        /// </summary>
        private static HashSet<string> AllStandardEventFieldNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotStandardEventType entry in s_standardEventTypes)
            {
                foreach (WotStandardEventField field in entry.Fields)
                {
                    names.Add(field.Name);
                }
            }
            return names;
        }

        /// <summary>
        /// Writes the Condition annotations and the <c>data</c> schema of an
        /// event affordance (WoT Binding Sections 13.2 and 13.3).
        /// </summary>
        /// <remarks>
        /// The two are written together because they answer one question. The
        /// ConditionType decides which standard fields the notification
        /// carries, and the EventType's own child Variables add to them. An
        /// event that is not a Condition still gets a <c>data</c> object: the
        /// eight mandatory <c>BaseEventType</c> fields are what Section 6.1
        /// selects when a document states no select clauses, so a schema
        /// without them would describe a notification no consumer receives.
        /// </remarks>
        private static void WriteEventConditionAndData(
            Utf8JsonWriter writer,
            UANode eventType,
            WotConditionProjection projection,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            Dictionary<string, UANode> index,
            string defaultLocale)
        {
            if (projection.IsCondition)
            {
                WriteOptional(writer, ConditionTypeTerm, projection.CompactName);

                // Section 13.2 makes the pin the definitive identity of the
                // same type the compact name reads. It is written alongside
                // rather than instead: a consumer without the OPC UA model
                // loaded resolves the pin, and one with it checks the two
                // agree.
                WriteOptional(
                    writer,
                    ConditionTypeIdTerm,
                    ToPortableNodeId(projection.DefinitiveId, namespaceUris));
            }

            List<WotStandardEventField> standard =
                CollectStandardEventFields(projection.StandardTypeNodeId);
            Dictionary<string, UAVariable> declared = CollectDeclaredEventFields(eventType, index);

            WriteEventDataSchema(
                writer, standard, declared, namespaceUris, nodeSet, defaultLocale);
        }

        /// <summary>
        /// Writes the <c>data</c> DataSchema of an EventType: the fields it
        /// inherits and the fields it declares, in a stated order
        /// (WoT Binding Sections 6.1 and 13.3).
        /// </summary>
        /// <remarks>
        /// The schema is what the fast path of Section 6.1 derives a select
        /// clause per leaf from, so it states <c>uav:fieldOrder</c> for every
        /// object with more than one property: JSON member order is not an
        /// order, and two consumers reading the same document would otherwise
        /// request one EventType's fields in two different orders. The order
        /// written here is the inherited standard fields in the order
        /// OPC 10000-5 and OPC 10000-9 declare them, followed by the fields the
        /// type declares itself in the order its References state them, which
        /// is the NodeSet's own declaration order and therefore reproducible.
        /// </remarks>
        private static void WriteEventDataSchema(
            Utf8JsonWriter writer,
            List<WotStandardEventField> standard,
            Dictionary<string, UAVariable> declared,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            writer.WritePropertyName(DataMember);
            writer.WriteStartObject();
            writer.WriteString("type", "object");

            var required = new List<string>();
            var order = new List<string>();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotStandardEventField field in standard)
            {
                if (written.Add(field.Name))
                {
                    order.Add(field.Name);
                }
            }
            foreach (KeyValuePair<string, UAVariable> field in declared)
            {
                // A Variable whose BrowseName is one of the standard fields
                // re-declares an inherited field rather than adding one, so it
                // has already refined the standard member below. Writing it a
                // second time would state the same field twice under the same
                // name.
                if (written.Add(field.Key))
                {
                    order.Add(field.Key);
                }
            }

            if (order.Count > 1)
            {
                writer.WritePropertyName(WotEventSelectClauses.FieldOrderTerm);
                writer.WriteStartArray();
                foreach (string name in order)
                {
                    writer.WriteStringValue(name);
                }
                writer.WriteEndArray();
            }

            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            written.Clear();
            foreach (WotStandardEventField field in standard)
            {
                if (!written.Add(field.Name))
                {
                    continue;
                }
                declared.TryGetValue(field.Name, out UAVariable? node);
                writer.WritePropertyName(field.Name);
                WriteStandardEventField(writer, field, node, defaultLocale);
                if (IsRequiredEventField(field, node))
                {
                    required.Add(field.Name);
                }
            }
            foreach (KeyValuePair<string, UAVariable> field in declared)
            {
                if (!written.Add(field.Key))
                {
                    continue;
                }
                writer.WritePropertyName(field.Key);
                WriteDeclaredEventField(
                    writer, field.Value, namespaceUris, nodeSet, defaultLocale);
                if (IsMandatory(field.Value))
                {
                    required.Add(field.Key);
                }
            }
            writer.WriteEndObject();

            if (required.Count > 0)
            {
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                foreach (string name in required)
                {
                    writer.WriteStringValue(name);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes the <c>data</c> DataSchema an EventType Node's own Thing
        /// Model carries, which is the EventType definition an event affordance
        /// links to with <c>tm:ref</c> (WoT Binding Section 6.1).
        /// </summary>
        internal static void WriteEventTypeDefinitionData(
            Utf8JsonWriter writer,
            UANode eventType,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            Dictionary<string, UANode> index,
            string defaultLocale)
        {
            WotConditionProjection projection = ResolveConditionProjection(eventType, index);
            WriteEventDataSchema(
                writer,
                CollectStandardEventFields(projection.StandardTypeNodeId),
                CollectDeclaredEventFields(eventType, index),
                namespaceUris,
                nodeSet,
                defaultLocale);
        }

        /// <summary>
        /// Collects the Variables an EventType declares as its own fields, in
        /// the order its References state them.
        /// </summary>
        /// <remarks>
        /// Reference order is the NodeSet's own declaration order and is stable
        /// across reads of the same document, which is what makes the emitted
        /// member order reproducible. Only Variables are fields; a Method an
        /// EventType holds is a Condition Method and is projected as an action,
        /// not as a member of the notification.
        /// </remarks>
        private static Dictionary<string, UAVariable> CollectDeclaredEventFields(
            UANode eventType,
            Dictionary<string, UANode> index)
        {
            var fields = new Dictionary<string, UAVariable>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (Reference reference in eventType.References ?? [])
            {
                if (!reference.IsForward ||
                    reference.Value is null ||
                    !IsComponentReference(reference.ReferenceType) ||
                    !index.TryGetValue(reference.Value, out UANode? target) ||
                    target is not UAVariable variable ||
                    LocalName(variable.BrowseName) is not { Length: > 0 } name ||
                    fields.ContainsKey(name))
                {
                    continue;
                }
                fields[name] = variable;
                order.Add(name);
            }
            var ordered = new Dictionary<string, UAVariable>(StringComparer.Ordinal);
            foreach (string name in order)
            {
                ordered[name] = fields[name];
            }
            return ordered;
        }

        /// <summary>
        /// Gets whether a standard field is required of every notification.
        /// </summary>
        /// <remarks>
        /// A type that re-declares an inherited field states its own
        /// ModellingRule for it, and that statement is what the projected type
        /// says. Where it declares none, Section 13.3 decides.
        /// </remarks>
        private static bool IsRequiredEventField(
            WotStandardEventField field,
            UAVariable? declared)
        {
            if (declared is null)
            {
                return field.Required;
            }
            string? rule = GetBaselineModellingRule(declared);
            return rule is null ? field.Required : IsMandatoryRule(rule);
        }

        private static bool IsMandatory(UANode node)
        {
            return GetBaselineModellingRule(node) is { } rule && IsMandatoryRule(rule);
        }

        private static bool IsMandatoryRule(string rule)
        {
            return string.Equals(rule, "Mandatory", StringComparison.Ordinal) ||
                string.Equals(rule, "MandatoryPlaceholder", StringComparison.Ordinal);
        }

        /// <summary>
        /// Writes one standard event field as a DataSchema.
        /// </summary>
        /// <remarks>
        /// The definitive DataType is deliberately not written. OPC 10000-9
        /// declares these fields, so their types are fixed by that standard
        /// rather than by the document, and the reverse direction reads them
        /// from the same table instead of re-creating the inherited
        /// declarations as Nodes of the derived type. What the document may
        /// still add is the description the projected type gives the field.
        /// </remarks>
        private static void WriteStandardEventField(
            Utf8JsonWriter writer,
            WotStandardEventField field,
            UAVariable? declared,
            string defaultLocale)
        {
            writer.WriteStartObject();
            switch (field.Kind)
            {
                case WotEventFieldKind.ByteString:
                    writer.WriteString("type", "string");
                    writer.WriteString("contentEncoding", WotVocabulary.Base64Encoding);
                    break;
                case WotEventFieldKind.UtcTime:
                    writer.WriteString("type", "string");
                    writer.WriteString("format", "date-time");
                    break;
                case WotEventFieldKind.Severity:
                    writer.WriteString("type", "integer");
                    writer.WriteNumber("minimum", MinimumSeverity);
                    writer.WriteNumber("maximum", MaximumSeverity);
                    break;
                case WotEventFieldKind.Flag:
                    writer.WriteString("type", "boolean");
                    break;
                case WotEventFieldKind.Analog:
                    writer.WriteString("type", "number");
                    break;
                case WotEventFieldKind.TwoState:
                    writer.WriteString("type", "object");
                    // Section 6.1 derives one select clause per leaf of this
                    // schema and walks a multi-property object in the order
                    // uav:fieldOrder states, because JSON member order is not
                    // an order.
                    writer.WritePropertyName(WotEventSelectClauses.FieldOrderTerm);
                    writer.WriteStartArray();
                    writer.WriteStringValue("Id");
                    writer.WriteStringValue(WotEventSelectClauses.StateNameMember);
                    writer.WriteEndArray();
                    writer.WritePropertyName("properties");
                    writer.WriteStartObject();
                    writer.WritePropertyName("Id");
                    writer.WriteStartObject();
                    writer.WriteString("type", "boolean");
                    writer.WriteEndObject();
                    writer.WritePropertyName(WotEventSelectClauses.StateNameMember);
                    writer.WriteStartObject();
                    writer.WriteString("type", "string");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteString("type", "string");
                    break;
            }
            if (declared is not null)
            {
                WriteLocalizedDescription(writer, declared.Description, defaultLocale);
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes a field the EventType declares itself as a DataSchema.
        /// </summary>
        /// <remarks>
        /// Unlike a standard field, nothing outside the document says what this
        /// field is, so everything the reverse direction needs to rebuild the
        /// Variable is written: the definitive DataType of Section 5.4, the
        /// ValueRank that separates a scalar from an array of the same type,
        /// the ArrayDimensions, the BrowseName that may be qualified by a
        /// namespace other than the Thing's, and the ModellingRule.
        /// </remarks>
        private static void WriteDeclaredEventField(
            Utf8JsonWriter writer,
            UAVariable field,
            string[]? namespaceUris,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            writer.WriteStartObject();
            WriteArgumentJsonType(writer, field.DataType);
            WriteLocalizedTitle(writer, field.DisplayName, defaultLocale);
            WriteLocalizedDescription(writer, field.Description, defaultLocale);
            WriteOptional(
                writer,
                "uav:browseName",
                ToPortableQualifiedName(field.BrowseName, namespaceUris));
            WriteOptional(writer, "uav:id", ToPortableNodeId(field.NodeId, namespaceUris));
            WriteOptional(
                writer,
                "uav:mapToType",
                ToPortableDataTypeId(field.DataType, nodeSet));
            writer.WriteNumber("uav:valueRank", field.ValueRank);
            WriteFieldArrayDimensions(writer, field.ArrayDimensions);
            WriteModellingRule(writer, field);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Names the Condition Method a Method Node invokes, where its
        /// BrowseName is one of the closed set of Section 13.2.
        /// </summary>
        /// <remarks>
        /// The BrowseName has to be the base-namespace one. A vendor's own
        /// <c>1:Acknowledge</c> is a different QualifiedName standing for
        /// something OPC 10000-9 says nothing about, and annotating it as the
        /// standard Method would claim a signature and a semantics it never
        /// declared.
        /// </remarks>
        private static string? ConditionActionOf(UAMethod method)
        {
            foreach (string action in s_conditionActions)
            {
                if (IsBaseNamespaceBrowseName(method.BrowseName, action))
                {
                    return action;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets whether a Method Node states that it is the standard Condition
        /// Method of the named action.
        /// </summary>
        /// <remarks>
        /// A <c>MethodDeclarationId</c> naming the OPC 10000-9 declaration is
        /// the NodeSet's own statement that this Method <em>is</em> that
        /// Method, which fixes its signature whether or not the instance
        /// re-declares the argument Properties.
        /// </remarks>
        private static bool DeclaresStandardConditionMethod(UAMethod method, string action)
        {
            foreach (WotConditionMethod candidate in s_conditionMethods)
            {
                if (string.Equals(candidate.Action, action, StringComparison.Ordinal))
                {
                    return string.Equals(
                        method.MethodDeclarationId,
                        candidate.MethodNodeId,
                        StringComparison.Ordinal);
                }
            }
            return false;
        }

        /// <summary>
        /// The signature OPC 10000-9 gives every occurrence-level Condition
        /// Method: a ByteString <c>EventId</c> and a LocalizedText
        /// <c>Comment</c>, in that order.
        /// </summary>
        private static List<WotMethodArgument> StandardConditionMethodArguments()
        {
            return
            [
                new WotMethodArgument(EventIdField, WotVocabulary.ByteString, -1, null, null),
                new WotMethodArgument(CommentField, "i=21", -1, null, null)
            ];
        }

        /// <summary>
        /// Resolves the Condition annotations a Method Node projects, together
        /// with the argument schema the pairing requires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A NodeSet does not reference the EventType from the Method, so the
        /// pairing is recovered from the type that holds the Method. Where the
        /// Method hangs off the Object instead, the document being written
        /// decides: with exactly one Condition event there is one candidate and
        /// the pairing is definite. With several, nothing in the source says
        /// which, so the annotation is left out and reported rather than
        /// assigned to whichever came first - an <c>uav:actsOn</c> naming the
        /// wrong Condition is worse than one that is absent, because a consumer
        /// would acknowledge the wrong alarm.
        /// </para>
        /// <para>
        /// Section 13.4 requires an <c>Acknowledge</c>, <c>Confirm</c> or
        /// <c>AddComment</c> action to declare <c>EventId</c> as an input, so a
        /// pairing without one would produce a document this Binding's own
        /// rules reject. Where the Method holds its argument Property the
        /// schema comes from it; where it only states the standard
        /// <c>MethodDeclarationId</c>, the signature OPC 10000-9 fixes for that
        /// declaration is used. Where it does neither, the pairing is reported
        /// rather than written.
        /// </para>
        /// </remarks>
        private static bool TryResolveConditionAffordance(
            UAMethod method,
            string? owningEventKey,
            List<string> conditionEventKeys,
            ref WotMethodArguments arguments,
            List<WotDiagnostic> diagnostics,
            out string action,
            out string actsOn)
        {
            actsOn = string.Empty;
            action = ConditionActionOf(method) ?? string.Empty;
            if (action.Length == 0)
            {
                return false;
            }

            if (owningEventKey is not null)
            {
                actsOn = owningEventKey;
            }
            else if (conditionEventKeys.Count == 1)
            {
                actsOn = conditionEventKeys[0];
            }
            else
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Warning,
                    WotDiagnosticCode.ConditionActionTargetUnresolved,
                    $"The Method '{method.BrowseName}' names the Condition Method " +
                    $"'{action}', but this document projects {conditionEventKeys.Count} event " +
                    $"affordances carrying '{ConditionTypeTerm}', so '{ActsOnTerm}' cannot name " +
                    "one of them (WoT Binding Section 13.4). The Method is projected as an " +
                    "action without the Condition annotations.",
                    new WotLocation(nodeId: method.NodeId)));
                return false;
            }

            if (Array.IndexOf(s_occurrenceActions, action) < 0 ||
                DeclaresArgument(arguments.Input, EventIdField))
            {
                return true;
            }
            if (DeclaresStandardConditionMethod(method, action))
            {
                arguments = new WotMethodArguments(
                    StandardConditionMethodArguments(), arguments.Output);
                return true;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Warning,
                WotDiagnosticCode.ConditionActionTargetUnresolved,
                $"The Method '{method.BrowseName}' names the Condition Method '{action}', " +
                "which acts on one Event occurrence, but it neither declares an " +
                $"'{EventIdField}' input argument nor states the standard " +
                "MethodDeclarationId OPC 10000-9 gives that Method. A pairing without an " +
                $"'{EventIdField}' input is one WoT Binding Section 13.4 rejects, so the " +
                "Method is projected as an action without the Condition annotations.",
                new WotLocation(nodeId: method.NodeId)));
            return false;
        }

        private static bool DeclaresArgument(List<WotMethodArgument>? arguments, string name)
        {
            foreach (WotMethodArgument argument in arguments ?? [])
            {
                if (string.Equals(argument.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // WoT -> NodeSet
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves the ConditionType an event affordance names, rejecting a
        /// readable hint and a definitive pin that disagree.
        /// </summary>
        /// <remarks>
        /// Section 13.2 makes the pin the definitive identity of <em>the
        /// same</em> type the compact name reads, so a disagreement is not a
        /// precedence question: honouring either one silently discards what the
        /// other says. The document is reported invalid and the pin is used, so
        /// that the rest of the conversion still has one coherent answer to
        /// work from.
        /// </remarks>
        private static string ResolveConditionSupertype(
            WotDocument document,
            JsonElement eventAffordance,
            string key,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            string? hint = GetElementString(eventAffordance, ConditionTypeTerm);
            string hintNodeId = string.Empty;
            bool hintResolved = hint is not null &&
                TryResolveConditionTypeName(document, hint, out hintNodeId);
            if (!hintResolved)
            {
                hintNodeId = string.Empty;
            }

            string? pinned = GetElementString(eventAffordance, ConditionTypeIdTerm);
            if (pinned is not null)
            {
                string pinnedNodeId = ToNodeSetNodeId(pinned, nodeSet, diagnostics);
                if (hintResolved &&
                    !string.Equals(hintNodeId, pinnedNodeId, StringComparison.Ordinal))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionTypeConflict,
                        $"'{ConditionTypeTerm}' names '{hint}' and '{ConditionTypeIdTerm}' " +
                        $"pins '{pinned}', which are different ConditionTypes. The pin is " +
                        "the definitive identity of the type the compact name reads, so the " +
                        "two shall agree (WoT Binding Section 13.2).",
                        WotLocation.FromPointer(
                            "/events/" + EscapeJsonPointerToken(key) + "/" + ConditionTypeIdTerm)));
                }
                return pinnedNodeId;
            }

            if (hint is null)
            {
                return WotVocabulary.BaseEventType;
            }
            if (hintResolved)
            {
                return hintNodeId;
            }

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error,
                WotDiagnosticCode.UnresolvedConditionType,
                $"'{hint}' is not a ConditionType this Binding resolves. Pin it with " +
                $"'{ConditionTypeIdTerm}' (WoT Binding Section 13.2).",
                WotLocation.FromPointer(
                    "/events/" + EscapeJsonPointerToken(key) + "/" + ConditionTypeTerm)));
            return WotVocabulary.BaseEventType;
        }

        /// <summary>
        /// Resolves a <c>uav:conditionType</c> compact model name.
        /// </summary>
        /// <remarks>
        /// Section 13.2 names the ConditionType with a compact model name,
        /// which Section 5.1.2 resolves through the document's own
        /// <c>@context</c> rather than by its literal prefix - an author may
        /// bind a second prefix to the OPC UA namespace. Only that namespace
        /// resolves without a local context; a companion ConditionType has to
        /// be pinned.
        /// </remarks>
        private static bool TryResolveConditionTypeName(
            WotDocument document,
            string compactName,
            out string nodeId)
        {
            if (TrySplitCompactModelName(compactName, out string prefix, out string local) &&
                TryGetContextNamespace(document, prefix, out string namespaceUri) &&
                string.Equals(
                    namespaceUri, WotVocabulary.OpcUaNamespace, StringComparison.Ordinal) &&
                WotVocabulary.TryGetConditionTypeNodeId(local, out string found))
            {
                nodeId = found;
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        /// <summary>
        /// Gets the BrowseNames of the fields an EventType inherits, which are
        /// therefore already declared and must not be created again on the
        /// type being materialized.
        /// </summary>
        /// <remarks>
        /// A derived type that re-declares an inherited field would give a
        /// Server two declarations of one field. Where the supertype is one
        /// this Binding knows, the inherited set is exact. Where it is a
        /// companion ConditionType pinned by <c>uav:conditionTypeId</c>, the
        /// exact set is unknown but every OPC 10000-9 Condition field name is
        /// reserved by that standard, so the whole table is excluded rather
        /// than a Node named <c>ConditionId</c> being invented - that member is
        /// the NodeId Attribute of the Condition and is not a Variable at all.
        /// </remarks>
        private static HashSet<string> InheritedEventFieldNames(
            string superTypeNodeId,
            bool isCondition)
        {
            List<WotStandardEventField> known = CollectStandardEventFields(superTypeNodeId);
            if (known.Count > 0)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (WotStandardEventField field in known)
                {
                    names.Add(field.Name);
                }
                return names;
            }
            return isCondition
                ? AllStandardEventFieldNames()
                : InheritedEventFieldNames(WotVocabulary.BaseEventType, false);
        }

        /// <summary>
        /// Materializes the members of an event affordance's <c>data</c> object
        /// that the projected EventType adds, as fields of that type.
        /// </summary>
        /// <remarks>
        /// Only the members the type adds become Nodes. A member naming a field
        /// the type inherits - every mandatory <c>BaseEventType</c> field, and
        /// every Condition field of Section 13.3 when the event projects a
        /// ConditionType - is already declared by the type it comes from, and
        /// re-declaring it here would leave a Server holding two declarations
        /// of one field. A member that is not a DataSchema at all is reported
        /// and left to preservation rather than dropped.
        /// </remarks>
        private static void SynthesizeEventFields(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement eventAffordance,
            string key,
            string superTypeNodeId,
            string eventNodeId,
            string eventLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> eventReferences,
            List<WotDiagnostic> diagnostics,
            WotEventSelectionCatalog? eventSelections = null)
        {
            System.Text.Json.JsonDocument? linked = null;
            try
            {
                if (!eventAffordance.TryGetProperty(DataMember, out JsonElement data) ||
                    data.ValueKind != JsonValueKind.Object)
                {
                    // Section 6.1: where an affordance declares no data of its
                    // own, the definition it links to is its effective schema,
                    // so the fields materialized here are the ones that
                    // definition declares rather than none at all.
                    if (eventSelections is null ||
                        !eventSelections.TryGetLinkedData(
                            key, out ReadOnlyMemory<byte> linkedData))
                    {
                        return;
                    }
                    linked = System.Text.Json.JsonDocument.Parse(linkedData);
                    data = linked.RootElement;
                }
                if (!data.TryGetProperty("properties", out JsonElement properties) ||
                    properties.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                SynthesizeEventFieldMembers(
                    document, nodeSet, eventAffordance, data, properties, key,
                    superTypeNodeId, eventNodeId, eventLocal, rootLocal, items,
                    eventReferences, diagnostics);
            }
            finally
            {
                linked?.Dispose();
            }
        }

        /// <inheritdoc cref="SynthesizeEventFields"/>
        private static void SynthesizeEventFieldMembers(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement eventAffordance,
            JsonElement data,
            JsonElement properties,
            string key,
            string superTypeNodeId,
            string eventNodeId,
            string eventLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> eventReferences,
            List<WotDiagnostic> diagnostics)
        {
            bool isCondition = HasNonEmptyString(eventAffordance, ConditionTypeTerm) ||
                HasNonEmptyString(eventAffordance, ConditionTypeIdTerm);
            HashSet<string> inherited = InheritedEventFieldNames(superTypeNodeId, isCondition);
            HashSet<string> required = ReadRequiredFields(data);
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonProperty member in properties.EnumerateObject())
            {
                string pointer = "/events/" +
                    EscapeJsonPointerToken(key) +
                    "/" +
                    DataMember +
                    "/properties/" +
                    EscapeJsonPointerToken(member.Name);
                if (member.Value.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.EventFieldInvalid,
                        $"The '{member.Name}' member of an event 'data' object is not a " +
                        "DataSchema, so it does not name an EventType field. It is carried " +
                        "unchanged by preservation rather than dropped.",
                        WotLocation.FromPointer(pointer)));
                    continue;
                }

                string local = LocalName(GetElementString(member.Value, "uav:browseName"))
                    ?? member.Name;
                if (inherited.Contains(local) || inherited.Contains(member.Name))
                {
                    continue;
                }
                if (!declared.Add(local))
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.EventFieldInvalid,
                        "Two members of an event 'data' object name the same EventType " +
                        $"field '{local}'. One field cannot be declared twice, so the " +
                        "second is carried by preservation rather than materialized.",
                        WotLocation.FromPointer(pointer)));
                    continue;
                }

                SynthesizeEventField(
                    document, nodeSet, member.Value, local,
                    required.Contains(member.Name) || required.Contains(local),
                    eventNodeId, eventLocal, rootLocal, items, eventReferences, diagnostics);
            }
        }

        /// <summary>
        /// Materializes one <c>data</c> member as a Property of the EventType.
        /// </summary>
        /// <remarks>
        /// An event field is a Property in OPC 10000-5 terms: it holds a value
        /// the notification carries and owns nothing. The ModellingRule follows
        /// the schema's own <c>required</c> list, which is the only statement a
        /// WoT document makes about whether a member is always present.
        /// </remarks>
        private static void SynthesizeEventField(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement schema,
            string local,
            bool required,
            string eventNodeId,
            string eventLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> eventReferences,
            List<WotDiagnostic> diagnostics)
        {
            string? authoredNodeId = GetElementString(schema, "uav:id");
            string nodeId = authoredNodeId is null
                ? GenerateNestedNodeId(nodeSet, rootLocal, eventLocal, local)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, diagnostics);
            string? authoredBrowseName = GetElementString(schema, "uav:browseName");
            var field = new UAVariable
            {
                NodeId = nodeId,
                BrowseName = authoredBrowseName is null
                    ? "1:" + local
                    : ToNodeSetQualifiedName(document, authoredBrowseName, nodeSet, diagnostics),
                DisplayName = ReadTitle(schema, GetDeclaredLocale(document), local),
                ParentNodeId = eventNodeId,
                DataType = MapJsonSchemaToDataType(document, schema, nodeSet, diagnostics),
                ValueRank = GetElementInt32(schema, "uav:valueRank") ?? -1,
                ArrayDimensions = ReadArrayDimensions(schema, local, diagnostics),
                AccessLevel = AccessLevelCurrentRead,
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
                        Value = required
                            ? WotVocabulary.ModellingRuleMandatory
                            : WotVocabulary.ModellingRuleOptional
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = eventNodeId
                    }
                ]
            };
            string? description = GetElementString(schema, "description");
            if (description is not null)
            {
                field.Description = ReadDescription(schema, GetDeclaredLocale(document));
            }
            items.Add(field);
            eventReferences.Add(new Reference
            {
                ReferenceType = "HasProperty",
                IsForward = true,
                Value = nodeId
            });
        }

        /// <summary>
        /// Gets whether an action's authored BrowseName agrees with the
        /// standard Condition Method it says it invokes.
        /// </summary>
        /// <remarks>
        /// An author who names the Method explicitly is naming a Node, and this
        /// direction honours that name rather than replacing it with the
        /// standard one. Where the two agree - or where the document states no
        /// BrowseName at all - the Node is the standard Method and takes the
        /// base-namespace BrowseName OPC 10000-9 declares it with.
        /// </remarks>
        private static bool IsStandardConditionMethodName(
            JsonElement action,
            string conditionAction)
        {
            string? authored = GetElementString(action, "uav:browseName");
            return authored is null ||
                string.Equals(LocalName(authored), conditionAction, StringComparison.Ordinal);
        }

        /// <summary>
        /// Computes the NodeId an event affordance materializes to, without
        /// materializing it.
        /// </summary>
        /// <remarks>
        /// The actions are synthesized before the events, so an action that
        /// names the event whose Condition it acts on has to reach that event's
        /// identity before the Node exists. The identity is derived exactly as
        /// <see cref="SynthesizeEvent"/> derives it, from the same two inputs,
        /// so the two can never disagree. Diagnostics are dropped here because
        /// anything wrong with the event's own identity belongs to the event
        /// and is reported when it is created.
        /// </remarks>
        private static string EventNodeId(
            JsonElement eventAffordance,
            string key,
            string rootLocal,
            UANodeSet nodeSet)
        {
            string local = LocalName(GetElementString(eventAffordance, "uav:browseName")) ?? key;
            string? authoredNodeId = GetElementString(eventAffordance, "uav:id");
            var ignored = new List<WotDiagnostic>();
            return authoredNodeId is null
                ? GenerateMemberNodeId(nodeSet, rootLocal, local)
                : ToNodeSetNodeId(authoredNodeId, nodeSet, ignored);
        }

        /// <summary>
        /// Resolves the standard Method declaration a Condition action
        /// invokes, rejecting a pairing OPC 10000-9 does not admit.
        /// </summary>
        /// <remarks>
        /// A Method a type does not declare cannot be called on it. An
        /// <c>Acknowledge</c> acting on a plain <c>ua:ConditionType</c> is such
        /// a pairing: OPC 10000-9 declares <c>Acknowledge</c> on
        /// <c>AcknowledgeableConditionType</c>. Reporting it is the point of
        /// resolving the declaration at all - the identifier itself is only
        /// useful once the pairing is known to hold.
        /// </remarks>
        /// <returns>
        /// The declaration NodeId, or <c>null</c> where the projected type is
        /// not one this Binding knows and the pairing therefore cannot be
        /// judged either way.
        /// </returns>
        private static string? ResolveConditionMethodDeclaration(
            WotDocument document,
            JsonElement action,
            string key,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            if (!TryGetNonEmptyString(action, ConditionActionTerm, out string conditionAction) ||
                !TryGetNonEmptyString(action, ActsOnTerm, out string actsOn) ||
                !document.Events.TryGetValue(actsOn, out JsonElement target) ||
                target.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            WotConditionMethod declaration = default;
            bool known = false;
            foreach (WotConditionMethod candidate in s_conditionMethods)
            {
                if (string.Equals(candidate.Action, conditionAction, StringComparison.Ordinal))
                {
                    declaration = candidate;
                    known = true;
                    break;
                }
            }
            if (!known)
            {
                // Outside the closed set of Section 13.2, which
                // ValidateConditions already reports.
                return null;
            }

            // Resolving the target's ConditionType a second time is deliberate:
            // it is resolved without diagnostics here, because any complaint
            // about that event belongs to the event, not to the action that
            // names it.
            var ignored = new List<WotDiagnostic>();
            string conditionType = ResolveConditionSupertype(
                document, target, actsOn, nodeSet, ignored);
            if (!DeclaresConditionMethod(conditionType, declaration.DeclaringTypeNodeId))
            {
                if (WotVocabulary.TryGetConditionTypeName(conditionType, out string typeName) ||
                    string.Equals(
                        conditionType, WotVocabulary.BaseEventType, StringComparison.Ordinal))
                {
                    _ = WotVocabulary.TryGetConditionTypeName(
                        declaration.DeclaringTypeNodeId, out string declaringName);
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.ConditionActionNotDeclared,
                        $"A '{conditionAction}' action acts on an event projecting " +
                        $"'{(typeName.Length > 0 ? typeName : "BaseEventType")}', which does " +
                        "not declare that Method. OPC 10000-9 declares it on " +
                        $"'{declaringName}' (WoT Binding Sections 13.1 and 13.4).",
                        WotLocation.FromPointer(
                            "/actions/" +
                            EscapeJsonPointerToken(key) +
                            "/" +
                            ConditionActionTerm)));
                }
                return null;
            }
            return declaration.MethodNodeId;
        }

        /// <summary>
        /// Gets whether a ConditionType is, or derives from, the type that
        /// declares a Condition Method.
        /// </summary>
        private static bool DeclaresConditionMethod(
            string conditionTypeNodeId,
            string declaringTypeNodeId)
        {
            string? current = conditionTypeNodeId;
            while (!string.IsNullOrEmpty(current))
            {
                if (string.Equals(current, declaringTypeNodeId, StringComparison.Ordinal))
                {
                    return true;
                }
                string? next = null;
                foreach (WotStandardEventType entry in s_standardEventTypes)
                {
                    if (string.Equals(entry.NodeId, current, StringComparison.Ordinal))
                    {
                        next = entry.SuperTypeNodeId;
                        break;
                    }
                }
                current = next;
            }
            return false;
        }
    }
}
