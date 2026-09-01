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
using System.Text;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Severity of a <see cref="WotDiagnostic"/>.
    /// </summary>
    public enum WotDiagnosticSeverity
    {
        /// <summary>An informational note; conversion succeeded.</summary>
        Info,

        /// <summary>A recoverable concern; conversion succeeded with caveats.</summary>
        Warning,

        /// <summary>A fatal problem; the associated conversion did not succeed.</summary>
        Error
    }

    /// <summary>
    /// Stable diagnostic codes emitted by the WoT/NodeSet conversion.
    /// </summary>
    public enum WotDiagnosticCode
    {
        /// <summary>No specific code.</summary>
        None = 0,

        /// <summary>The JSON document exceeded the configured byte limit.</summary>
        JsonDocumentTooLarge = 1000,

        /// <summary>The NodeSet2 payload exceeded the configured byte limit.</summary>
        NodeSetTooLarge = 1001,

        /// <summary>A nesting depth limit was exceeded.</summary>
        DepthExceeded = 1002,

        /// <summary>The node count limit was exceeded.</summary>
        NodeCountExceeded = 1003,

        /// <summary>The affordance count limit was exceeded.</summary>
        AffordanceCountExceeded = 1004,

        /// <summary>The JSON document was malformed.</summary>
        MalformedJson = 1005,

        /// <summary>The NodeSet2 XML was malformed.</summary>
        MalformedNodeSet = 1006,

        /// <summary>The preservation envelope is missing.</summary>
        EnvelopeMissing = 2000,

        /// <summary>The preservation envelope is structurally invalid.</summary>
        EnvelopeInvalid = 2001,

        /// <summary>The envelope content type is not supported.</summary>
        UnsupportedContentType = 2002,

        /// <summary>The envelope encoding is not supported.</summary>
        UnsupportedEncoding = 2003,

        /// <summary>The envelope data was not valid base64.</summary>
        InvalidBase64 = 2004,

        /// <summary>The envelope digest was not a valid SHA-256 value.</summary>
        InvalidDigest = 2005,

        /// <summary>The envelope digest did not match the decoded payload.</summary>
        DigestMismatch = 2006,

        /// <summary>A native member restated a baseline fact inconsistently.</summary>
        NativeProjectionConflict = 3000,

        /// <summary>Neither an envelope nor a native projection was present.</summary>
        NoConvertibleContent = 3001,

        /// <summary>A native projection record was structurally invalid.</summary>
        NativeProjectionInvalid = 3002,

        /// <summary>
        /// The structured native projection could not reproduce the source
        /// NodeSet and required an explicit preservation-envelope fallback.
        /// </summary>
        NativeProjectionIncomplete = 3003,

        /// <summary>
        /// Pointer-addressed WoT JSON residue in a NodeSet Extension was invalid.
        /// </summary>
        ResidueInvalid = 3004,

        /// <summary>
        /// Preserved WoT JSON residue conflicted with a value reconstructed from
        /// OPC UA model facts.
        /// </summary>
        ResidueConflict = 3005,

        /// <summary>
        /// A readable affordance is not represented by the authoritative
        /// native projection carried in <c>uav:nodes</c>.
        /// </summary>
        NativeProjectionUncoveredAffordance = 3006,

        /// <summary>
        /// A readable DataType definition does not satisfy WoT Binding §6.11.
        /// </summary>
        DataTypeDefinitionInvalid = 3007,

        /// <summary>A referenced target could not be resolved to a NodeId.</summary>
        UnresolvedReference = 4000,

        /// <summary>A NodeId was generated deterministically because none was supplied.</summary>
        GeneratedNodeId = 4001,

        /// <summary>A WoT construct had no faithful NodeSet2 representation.</summary>
        LossySynthesis = 4002,

        /// <summary>A required BrowseName or title was missing.</summary>
        MissingBrowseName = 4003,

        /// <summary>A DataSchema could not be mapped to an OPC UA DataType.</summary>
        UnsupportedSchema = 4004,

        /// <summary>External resolution detected a cycle.</summary>
        ResolverCycle = 5000,

        /// <summary>External resolution exceeded the configured depth.</summary>
        ResolverDepthExceeded = 5001,

        /// <summary>External resolution exceeded a configured resource limit.</summary>
        ResolverLimitExceeded = 5002,

        /// <summary>An external document could not be resolved.</summary>
        ResolverNotFound = 5003,

        /// <summary>A document validation rule was violated.</summary>
        ValidationError = 6000,

        /// <summary>
        /// A portable identity term used the session-local <c>ns=&lt;index&gt;</c>
        /// form instead of an OPC 10000-6 ExpandedNodeId (WoT Binding Section 5.1.1).
        /// </summary>
        NonPortableIdentity = 6001,

        /// <summary>
        /// An event affordance annotated <c>@type: uav:eventType</c> also set
        /// <c>uav:isEvent: false</c>, contradicting the event mapping
        /// (WoT Binding Section 5.2).
        /// </summary>
        EventAnnotationConflict = 6002,

        /// <summary>
        /// A NamespaceUri-qualified model-name hint could not be resolved and
        /// no definitive ExpandedNodeId fallback was available.
        /// </summary>
        ModelConceptUnresolved = 6003,

        /// <summary>
        /// A model-name hint and its definitive ExpandedNodeId resolved to
        /// different OPC UA model Nodes.
        /// </summary>
        ModelConceptConflict = 6004,

        /// <summary>
        /// A readable QualifiedName or BrowsePath persisted a numeric namespace
        /// index instead of a NamespaceUri-qualified form.
        /// </summary>
        NonPortableQualifiedName = 6005,

        /// <summary>
        /// A model or platform vocabulary term (WoT Binding Section 6) carried a
        /// value outside its allowed range, for example a non-boolean
        /// <c>uav:isComposite</c>, a <c>uav:scaleFactor</c> that is not a
        /// non-zero number, or a <c>uav:decimalPlaces</c> that is not an
        /// integer greater than or equal to zero.
        /// </summary>
        InvalidModelVocabularyValue = 6006,

        /// <summary>
        /// An absolute-IRI term (WoT Binding Section 6), such as
        /// <c>uav:semanticId</c>, did not carry an
        /// absolute IRI with a scheme.
        /// </summary>
        NonAbsoluteIri = 6007,

        /// <summary>
        /// A <c>uav:unitProperty</c> value was not a canonical RFC 6901 JSON
        /// Pointer of the form <c>/properties/&lt;name&gt;</c> naming a sibling
        /// string-valued property affordance of the same document (WoT Binding
        /// Sections 6.4 and 7). The OPC UA fact it records is an
        /// <c>EngineeringUnits</c> Property Node of its own, so a pointer into
        /// the annotated affordance names nothing that exists.
        /// </summary>
        InvalidUnitPointer = 6008,

        /// <summary>
        /// A containment term (WoT Binding Section 6.3) was inconsistent: a
        /// <c>uav:contains</c> entry did not name a declared link, or a
        /// <c>uav:containedIn</c> value was malformed or named the type itself.
        /// </summary>
        InvalidContainment = 6009,

        /// <summary>
        /// A projection document does not declare <c>uav:scenario</c>, or
        /// declares one that is not an absolute IRI.
        /// </summary>
        ProjectionScenarioMissing = 6010,

        /// <summary>
        /// A projection document's <c>uav:projects</c> manifest is absent,
        /// empty or structurally invalid.
        /// </summary>
        ProjectionManifestInvalid = 6011,

        /// <summary>
        /// A <c>uav:select</c> filter is malformed or carries a key outside the
        /// closed predicate set.
        /// </summary>
        ProjectionSelectorInvalid = 6012,

        /// <summary>
        /// A projection document defines an affordance instead of declaring one
        /// through <c>tm:ref</c>.
        /// </summary>
        ProjectionDefinesAffordance = 6013,

        /// <summary>
        /// A projection source could not be resolved.
        /// </summary>
        ProjectionSourceUnresolved = 6014,

        /// <summary>
        /// The projection source graph contains a cycle.
        /// </summary>
        ProjectionCycle = 6015,

        /// <summary>
        /// A projection source's <c>uav:sourceDigest</c> does not match the
        /// retrieved bytes.
        /// </summary>
        ProjectionDigestMismatch = 6016,

        /// <summary>
        /// A context prefix is bound to two different URIs across the sources
        /// of a projection.
        /// </summary>
        ProjectionContextConflict = 6017,

        /// <summary>
        /// A selection names an affordance that was already selected, so the
        /// later selection is dropped.
        /// </summary>
        ProjectionSelectionDropped = 6018,

        /// <summary>
        /// A document declares more than one type binding (WoT Binding
        /// Section 5.2.1): either more than one member of a single
        /// <c>@type</c> resolves as a type binding, or the document carries
        /// more than one <c>ua:HasTypeDefinition</c> link. A Node has exactly
        /// one <c>HasTypeDefinition</c>, so the document is invalid.
        /// </summary>
        AmbiguousTypeBinding = 6019,

        /// <summary>
        /// A <c>ua:HasTypeDefinition</c> link (WoT Binding Section 5.2.1) did
        /// not carry a usable definitive ExpandedNodeId in its <c>href</c>.
        /// </summary>
        InvalidTypeBinding = 6020,

        /// <summary>
        /// A type binding (WoT Binding Section 5.2.1) names a type the local
        /// context of Section 5.1.5 does not hold. The projection fails rather
        /// than falling back to <c>BaseObjectType</c>, because a silently
        /// mistyped node is worse than a reported failure: a Client browsing
        /// for the companion type would not find it and nothing would say why.
        /// </summary>
        UnresolvedTypeBinding = 6021,

        /// <summary>
        /// An event affordance declares <c>uav:conditionType</c> but its
        /// <c>data</c> object does not declare <c>EventId</c> (WoT Binding
        /// Section 13.3). <c>EventId</c> names the Event occurrence, so without
        /// it a consumer can receive the notification but can never identify
        /// the occurrence to acknowledge, confirm or comment on.
        /// </summary>
        ConditionEventIdMissing = 6022,

        /// <summary>
        /// A <c>uav:conditionAction</c> names something outside the closed set
        /// of Condition Methods this Binding maps (WoT Binding Section 13.2):
        /// <c>Acknowledge</c>, <c>Confirm</c>, <c>AddComment</c>,
        /// <c>Enable</c>, <c>Disable</c>.
        /// </summary>
        InvalidConditionAction = 6023,

        /// <summary>
        /// An action affordance declares <c>uav:conditionAction</c> without a
        /// <c>uav:actsOn</c> naming an event affordance in the same document
        /// that carries <c>uav:conditionType</c> (WoT Binding Section 13.4). A
        /// Condition Method acts on a Condition, so an action that does not say
        /// which one cannot be invoked.
        /// </summary>
        InvalidConditionTarget = 6024,

        /// <summary>
        /// An <c>Acknowledge</c>, <c>Confirm</c> or <c>AddComment</c> action
        /// does not declare <c>EventId</c> as an input (WoT Binding Section
        /// 13.4). These Methods act on one Event occurrence, so the input is
        /// what binds the invocation to the notification the consumer received.
        /// <c>Enable</c> and <c>Disable</c> act on the Condition instance and
        /// are not subject to this rule.
        /// </summary>
        ConditionActionInputMissing = 6025,

        /// <summary>
        /// A <c>uav:conditionType</c> names a ConditionType this Binding cannot
        /// resolve (WoT Binding Section 13.2). Only the four ConditionTypes
        /// Section 13.1 scopes resolve by name; anything else is pinned with
        /// <c>uav:conditionTypeId</c>. This is distinct from
        /// <see cref="UnresolvedTypeBinding"/>, which is about the Section
        /// 5.2.1 type of the projected node rather than an event's Condition.
        /// </summary>
        UnresolvedConditionType = 6026,

        /// <summary>
        /// A <c>uav:componentOf</c> link names the parent of the projected
        /// Object, but the target did not resolve to another registry
        /// document, an existing AddressSpace Node, or the Thing Model
        /// projection root (WoT Connectivity Section 7.3).
        /// </summary>
        UnresolvedParentPlacement = 6027,

        /// <summary>
        /// A projection document annotates a declared affordance with a member
        /// outside the closed set WoT Binding Section 12.5 admits. A projection
        /// <em>declares</em> affordances and does not define them, so an
        /// annotation may restate presentation and semantics - <c>title</c>,
        /// <c>titles</c>, <c>description</c>, <c>descriptions</c>, additional
        /// <c>@type</c> values, <c>uav:semanticId</c> and <c>uav:metadata</c>,
        /// plus <c>forms</c> and <c>security</c> where the source is routed
        /// through the projection - but never the source's schema. Merging a
        /// restated <c>type</c>, <c>unit</c>, <c>minimum</c>, <c>maximum</c> or
        /// <c>enum</c> would silently override what the source says about the
        /// Node, which is the one thing the clause forbids.
        /// </summary>
        ProjectionAnnotationNotPermitted = 6028,

        /// <summary>
        /// An affordance carries <c>uav:severity</c> outside the range OPC
        /// 10000-5 defines for <c>BaseEventType.Severity</c>, or on an
        /// affordance that is not an event (WoT Binding Sections 6.6 and 7).
        /// The value is rejected rather than clamped: a clamped severity is a
        /// number the author never wrote, and no later reader could tell it
        /// from an authored one.
        /// </summary>
        InvalidEventSeverity = 6029,

        /// <summary>
        /// An action's <c>input</c> or <c>output</c> DataSchema declares more
        /// than one member but states no order for them. OPC UA Method
        /// arguments are positional, and RFC 8259 gives JSON object members no
        /// order, so enumeration order would make the argument list depend on
        /// how the document happens to be serialized. The schema states
        /// <c>uav:fieldOrder</c> (WoT Binding Section 6.11.4) or the order has
        /// to follow from the Condition Method the action invokes; otherwise
        /// the arguments are left to preservation and reported here.
        /// </summary>
        MethodArgumentOrderAmbiguous = 6030,

        /// <summary>
        /// An action's <c>input</c> or <c>output</c> member is not a DataSchema
        /// this Binding can map to an <c>Argument</c> list - it is not a JSON
        /// object, or its <c>uav:fieldOrder</c> and <c>properties</c> disagree.
        /// The member is carried unchanged by preservation rather than dropped.
        /// </summary>
        MethodArgumentSchemaInvalid = 6031,

        /// <summary>
        /// An event affordance states <c>uav:conditionType</c> and
        /// <c>uav:conditionTypeId</c> that name different ConditionTypes (WoT
        /// Binding Section 13.2). The pin is the definitive identity of
        /// <em>the same</em> type the compact name reads, so a disagreement is
        /// a contradiction rather than a precedence question: honouring either
        /// one would silently discard what the other says.
        /// </summary>
        ConditionTypeConflict = 6032,

        /// <summary>
        /// A <c>uav:conditionAction</c> names a Condition Method the
        /// ConditionType of the event it acts on does not declare - an
        /// <c>Acknowledge</c> or <c>Confirm</c> against a plain
        /// <c>ua:ConditionType</c>, which OPC 10000-9 declares on
        /// <c>AcknowledgeableConditionType</c> instead (WoT Binding Sections
        /// 13.1 and 13.4). The pairing is rejected rather than materialized
        /// against a Method the projected type does not have.
        /// </summary>
        ConditionActionNotDeclared = 6033,

        /// <summary>
        /// A member of an event affordance's <c>data</c> object is not a
        /// DataSchema this Binding can materialize as an EventType field - it
        /// is not a JSON object, or it collides with the BrowseName of a field
        /// already declared. The member is carried unchanged by preservation
        /// rather than dropped.
        /// </summary>
        EventFieldInvalid = 6034,

        /// <summary>
        /// A NodeSet declares a Condition Method - <c>Acknowledge</c>,
        /// <c>Confirm</c>, <c>AddComment</c>, <c>Enable</c> or
        /// <c>Disable</c> - but the document projects no single event
        /// affordance carrying <c>uav:conditionType</c> for it to act on, so
        /// <c>uav:actsOn</c> cannot be resolved (WoT Binding Section 13.4). The
        /// Method is still projected as an action; only the Condition pairing
        /// is left unstated, because naming an arbitrary one of several
        /// candidate events would invent a relation the source does not have.
        /// </summary>
        ConditionActionTargetUnresolved = 6035,

        /// <summary>
        /// A <c>uav:valueRank</c> is not an integer, or is below <c>-3</c>,
        /// or a <c>uav:arrayDimensions</c> list contradicts it (WoT Binding
        /// Section 7). OPC 10000-3 gives ArrayDimensions one bound per
        /// dimension, so its length is the rank by construction and only a
        /// fixed rank of at least one admits it at all. The value is rejected
        /// rather than collapsed to a scalar, because a rank of <c>-2</c>,
        /// <c>-3</c> or <c>0</c> says something a scalar does not.
        /// </summary>
        InvalidValueRank = 6036,

        /// <summary>
        /// A range is not a range: <c>minimum</c> exceeds <c>maximum</c>, a
        /// <c>uav:instrumentRange</c> is not an object carrying two numbers,
        /// or the engineering range the DataSchema states is not contained in
        /// the instrument range (WoT Binding Sections 6.4.1 and 7). An
        /// engineering range outside what the instrument can measure is not a
        /// fact about any instrument, so it is reported rather than narrowed.
        /// </summary>
        InvalidRangeValue = 6037,

        /// <summary>
        /// A <c>uav:engineeringUnits</c> object does not carry the
        /// <c>namespaceUri</c>, integer <c>unitId</c> and <c>displayName</c>
        /// that WoT Binding Section 6.4.1 requires of the readable
        /// <c>EUInformation</c> preservation. A display string alone is lossy,
        /// because the authority's machine-readable UnitId cannot be recovered
        /// from it, so an incomplete object is rejected rather than materialized
        /// as a unit identity it never stated.
        /// </summary>
        InvalidEngineeringUnits = 6038,

        /// <summary>
        /// A document carries <c>titles</c> without <c>title</c>, or
        /// <c>descriptions</c> without <c>description</c>, or the singular
        /// member disagrees with the plural member's entry for the document's
        /// default locale, or that entry is missing (WoT Binding Section
        /// 9.1.1). Restating one value in two places is only safe while the two
        /// agree.
        /// </summary>
        InvalidLocalizedText = 6039,

        /// <summary>
        /// A link <c>rel</c> names a ReferenceType the WoT Binding Section
        /// 5.1.5 local context holds more than once — the same spelling is one
        /// ReferenceType's BrowseName and another's InverseName, say — and the
        /// link carries no <c>uav:refId</c> to settle it. Section 6.2 makes
        /// <c>uav:refId</c> required exactly when lookup is ambiguous, so
        /// picking one of the candidates would assert a relation the document
        /// never chose.
        /// </summary>
        ReferenceTypeAmbiguous = 6040,

        /// <summary>
        /// A link <c>rel</c> or <c>uav:refId</c> names a Node the local
        /// context holds, but that Node is not a ReferenceType (WoT Binding
        /// Sections 5.1.2 and 6.2). A relation may only be typed by a
        /// ReferenceType, so the reference is reported rather than created
        /// with some other type in its place.
        /// </summary>
        ReferenceTypeNodeClassInvalid = 6041
    }

    /// <summary>
    /// Locates a diagnostic within a WoT document and/or a NodeSet2 document.
    /// </summary>
    public sealed class WotLocation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotLocation"/> class.
        /// </summary>
        /// <param name="jsonPointer">An RFC 6901 JSON Pointer into the WoT document.</param>
        /// <param name="nodeId">An OPC UA NodeId string.</param>
        /// <param name="attribute">An OPC UA attribute name.</param>
        /// <param name="reference">A reference descriptor (type and target).</param>
        public WotLocation(
            string? jsonPointer = null,
            string? nodeId = null,
            string? attribute = null,
            string? reference = null)
        {
            JsonPointer = jsonPointer;
            NodeId = nodeId;
            Attribute = attribute;
            Reference = reference;
        }

        /// <summary>Gets the RFC 6901 JSON Pointer of the location, if any.</summary>
        public string? JsonPointer { get; }

        /// <summary>Gets the OPC UA NodeId of the location, if any.</summary>
        public string? NodeId { get; }

        /// <summary>Gets the OPC UA attribute name of the location, if any.</summary>
        public string? Attribute { get; }

        /// <summary>Gets the reference descriptor of the location, if any.</summary>
        public string? Reference { get; }

        /// <summary>Creates a location from a JSON Pointer.</summary>
        public static WotLocation FromPointer(string jsonPointer)
        {
            return new WotLocation(jsonPointer: jsonPointer);
        }

        /// <summary>Creates a location from a NodeId and optional attribute.</summary>
        public static WotLocation FromNode(string nodeId, string? attribute = null)
        {
            return new WotLocation(nodeId: nodeId, attribute: attribute);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder();
            Append(builder, nameof(JsonPointer), JsonPointer);
            Append(builder, nameof(NodeId), NodeId);
            Append(builder, nameof(Attribute), Attribute);
            Append(builder, nameof(Reference), Reference);
            return builder.Length == 0 ? "(document)" : builder.ToString();

            static void Append(StringBuilder builder, string name, string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(name).Append('=').Append(value);
            }
        }
    }

    /// <summary>
    /// A single structured conversion diagnostic.
    /// </summary>
    public sealed class WotDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotDiagnostic"/> class.
        /// </summary>
        /// <param name="severity">The severity of the diagnostic.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="message">A human-readable message.</param>
        /// <param name="location">The optional location of the diagnostic.</param>
        public WotDiagnostic(
            WotDiagnosticSeverity severity,
            WotDiagnosticCode code,
            string message,
            WotLocation? location = null)
        {
            Severity = severity;
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Location = location;
        }

        /// <summary>Gets the severity of the diagnostic.</summary>
        public WotDiagnosticSeverity Severity { get; }

        /// <summary>Gets the stable diagnostic code.</summary>
        public WotDiagnosticCode Code { get; }

        /// <summary>Gets the human-readable message.</summary>
        public string Message { get; }

        /// <summary>Gets the optional location of the diagnostic.</summary>
        public WotLocation? Location { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} WOT{1:D4}: {2}{3}",
                Severity,
                (int)Code,
                Message,
                Location is null ? string.Empty : " [" + Location + "]");
        }
    }

    /// <summary>
    /// The outcome of a WoT/NodeSet conversion: an optional value together
    /// with the structured diagnostics that describe how it was produced.
    /// </summary>
    /// <typeparam name="T">The type of the produced value.</typeparam>
    public sealed class WotConversionResult<T>
        where T : class
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotConversionResult{T}"/> class.
        /// </summary>
        /// <param name="value">The produced value, or <c>null</c> on failure.</param>
        /// <param name="diagnostics">The diagnostics produced.</param>
        public WotConversionResult(T? value, IReadOnlyList<WotDiagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>Gets the produced value, or <c>null</c> when conversion failed.</summary>
        public T? Value { get; }

        /// <summary>Gets the diagnostics produced during conversion.</summary>
        public IReadOnlyList<WotDiagnostic> Diagnostics { get; }

        /// <summary>Gets a value indicating whether any error diagnostic was produced.</summary>
        public bool HasErrors
        {
            get
            {
                for (int ii = 0; ii < Diagnostics.Count; ii++)
                {
                    if (Diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether conversion produced a usable value
        /// without any error diagnostic.
        /// </summary>
        public bool Success => Value is not null && !HasErrors;
    }
}
