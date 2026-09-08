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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises the event data schema and Condition emission of WoT Binding
    /// Section 13: the fields a notification carries, the ConditionType it
    /// projects, and the Condition Methods that act on it.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventDataTests
    {
        private const string BaseEventType = "i=2041";
        private const string ConditionType = "i=2782";
        private const string AcknowledgeableConditionType = "i=2881";
        private const string AlarmConditionType = "i=2915";
        private const string LimitAlarmType = "i=2955";
        private const string EventKey = "OverTemperatureEventType";

        private static readonly string[] s_baseEventFields =
        [
            "EventId", "EventType", "SourceNode", "SourceName",
            "Time", "ReceiveTime", "Message", "Severity"
        ];

        private static readonly string[] s_conditionFields =
        [
            "ConditionId", "ConditionName", "BranchId", "Retain",
            "ConditionClassId", "ConditionClassName", "Quality", "LastSeverity",
            "Comment", "ClientUserId", "EnabledState"
        ];

        private static readonly string[] s_conditionMethodArguments = ["EventId", "Comment"];

        private static readonly string[] s_pairedConditionActions = ["Acknowledge", "Confirm"];

        /// <summary>
        /// The complete field list a <c>LimitAlarmType</c> notification
        /// carries, base first and then each subtype's own state, which is the
        /// inheritance order of WoT Binding Section 13.3.
        /// </summary>
        private static readonly string[] s_limitAlarmFields =
        [
            "EventId", "EventType", "SourceNode", "SourceName",
            "Time", "ReceiveTime", "Message", "Severity",
            "ConditionId", "ConditionName", "BranchId", "Retain",
            "ConditionClassId", "ConditionClassName", "Quality", "LastSeverity",
            "Comment", "ClientUserId", "EnabledState",
            "AckedState", "ConfirmedState",
            "ActiveState", "InputNode", "SuppressedOrShelved",
            "HighHighLimit", "HighLimit", "LowLimit", "LowLowLimit"
        ];

        private static readonly int[] s_traceArrayDimensions = [3, 4];

        // ------------------------------------------------------------------
        // NodeSet -> WoT
        // ------------------------------------------------------------------

        /// <summary>
        /// Section 6.1 makes the eight mandatory <c>BaseEventType</c> fields
        /// the list a consumer selects when a document says nothing else, so an
        /// event affordance that does not state them describes a notification
        /// nobody receives.
        /// </summary>
        [Test]
        public void AnOrdinaryEventCarriesTheMandatoryBaseFieldsInDeclarationOrder()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(BaseEventType));

            JsonElement data = document.Events[EventKey].GetProperty("data");

            Assert.That(data.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(MemberNames(data), Is.EqualTo(s_baseEventFields).AsCollection);
            Assert.That(RequiredNames(data), Is.EqualTo(s_baseEventFields).AsCollection);
        }

        /// <summary>
        /// The readable refinements are what separate an EventId from a
        /// SourceName and a Time from either: without them every field of the
        /// notification reads back as a bare string.
        /// </summary>
        [Test]
        public void TheStandardFieldsCarryTheRefinementsThatDistinguishThem()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(BaseEventType));

            JsonElement properties = document.Events[EventKey]
                .GetProperty("data").GetProperty("properties");

            JsonElement eventId = properties.GetProperty("EventId");
            Assert.That(eventId.GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(
                eventId.GetProperty("contentEncoding").GetString(), Is.EqualTo("base64"));

            JsonElement time = properties.GetProperty("Time");
            Assert.That(time.GetProperty("format").GetString(), Is.EqualTo("date-time"));

            JsonElement severity = properties.GetProperty("Severity");
            Assert.That(severity.GetProperty("type").GetString(), Is.EqualTo("integer"));
            Assert.That(severity.GetProperty("minimum").GetInt32(), Is.EqualTo(1));
            Assert.That(severity.GetProperty("maximum").GetInt32(), Is.EqualTo(1000));
        }

        /// <summary>
        /// Section 13.2 names the ConditionType with the compact model name and
        /// pins it definitively, and Section 13.3 fixes the fields that follow
        /// from it.
        /// </summary>
        [TestCase(ConditionType, "ua:ConditionType")]
        [TestCase(AcknowledgeableConditionType, "ua:AcknowledgeableConditionType")]
        [TestCase(AlarmConditionType, "ua:AlarmConditionType")]
        [TestCase(LimitAlarmType, "ua:LimitAlarmType")]
        public void AConditionEventNamesItsConditionTypeAndPinsIt(
            string superType, string compactName)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(superType));

            JsonElement affordance = document.Events[EventKey];

            Assert.That(
                affordance.GetProperty("uav:conditionType").GetString(),
                Is.EqualTo(compactName));
            Assert.That(
                affordance.GetProperty("uav:conditionTypeId").GetString(),
                Is.EqualTo(superType));
        }

        /// <summary>
        /// Section 13.3 makes the mandatory base fields and the Condition
        /// identity and state fields required of every Condition notification,
        /// and the state a subtype adds present but not required.
        /// </summary>
        [Test]
        public void ALimitAlarmCarriesEveryInheritedConditionFieldInInheritanceOrder()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(LimitAlarmType));

            JsonElement data = document.Events[EventKey].GetProperty("data");

            Assert.That(
                MemberNames(data),
                Is.EqualTo(s_limitAlarmFields).AsCollection);
            Assert.That(
                RequiredNames(data),
                Is.EqualTo([.. s_baseEventFields, .. s_conditionFields]).AsCollection);
        }

        /// <summary>
        /// A two-state Variable is one field with a localized name and a
        /// Boolean, so it is one object member with both, not two members or a
        /// bare string.
        /// </summary>
        [Test]
        public void ATwoStateConditionFieldIsAnObjectWithIdAndName()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(AlarmConditionType));

            JsonElement state = document.Events[EventKey]
                .GetProperty("data").GetProperty("properties").GetProperty("ActiveState");

            Assert.That(state.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(
                state.GetProperty("properties").GetProperty("Id")
                    .GetProperty("type").GetString(),
                Is.EqualTo("boolean"));
            Assert.That(
                state.GetProperty("properties").GetProperty("Name")
                    .GetProperty("type").GetString(),
                Is.EqualTo("string"));
        }

        /// <summary>
        /// The ConditionType a type projects is the most derived one its
        /// supertype chain reaches, whether it reaches it directly or through
        /// intermediate types the same NodeSet declares.
        /// </summary>
        [Test]
        public void AConditionTypeReachedThroughALocalTypeIsStillNamed()
        {
            UANodeSet nodeSet = CreateEventNodeSet("ns=1;i=1003");
            var items = new List<UANode>(nodeSet.Items)
            {
                new UAObjectType
                {
                    NodeId = "ns=1;i=1003",
                    BrowseName = "1:VendorAlarmType",
                    DisplayName = [new Export.LocalizedText { Value = "VendorAlarmType" }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype",
                            IsForward = false,
                            Value = AlarmConditionType
                        }
                    ]
                }
            };
            nodeSet.Items = [.. items];

            using WotDocument document = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(
                document.Events[EventKey].GetProperty("uav:conditionType").GetString(),
                Is.EqualTo("ua:AlarmConditionType"));
        }

        /// <summary>
        /// A chain that leaves the NodeSet through an identifier this Binding
        /// does not know is not guessed at. The type keeps the base field set
        /// every EventType has and claims no Condition.
        /// </summary>
        [Test]
        public void AnUnknownSupertypeYieldsNoConditionClaim()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet("ns=1;i=9999"));

            JsonElement affordance = document.Events[EventKey];

            Assert.That(affordance.TryGetProperty("uav:conditionType", out _), Is.False);
            Assert.That(affordance.TryGetProperty("uav:conditionTypeId", out _), Is.False);
            Assert.That(
                MemberNames(affordance.GetProperty("data")),
                Is.EqualTo(s_baseEventFields).AsCollection);
        }

        /// <summary>
        /// A field the type adds carries everything the reverse direction needs
        /// to rebuild the Variable: nothing outside the document says what it
        /// is.
        /// </summary>
        [Test]
        public void ADeclaredFieldCarriesItsDataTypeRankAndDimensions()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(BaseEventType, DeclaredField("Optional")));

            JsonElement data = document.Events[EventKey].GetProperty("data");
            JsonElement field = data.GetProperty("properties").GetProperty("Trace");

            Assert.That(field.GetProperty("uav:mapToType").GetString(), Is.EqualTo("i=6"));
            Assert.That(field.GetProperty("uav:valueRank").GetInt32(), Is.EqualTo(2));
            Assert.That(
                field.GetProperty("uav:arrayDimensions").EnumerateArray()
                    .Select(e => e.GetInt32()),
                Is.EqualTo(s_traceArrayDimensions).AsCollection);
            Assert.That(
                field.GetProperty("uav:browseName").GetString(),
                Is.EqualTo("ns1:Trace"));
            Assert.That(field.GetProperty("description").GetString(), Is.EqualTo("A trace."));
            Assert.That(
                field.GetProperty("uav:modellingRule").GetString(), Is.EqualTo("Optional"));

            // Declared fields follow the inherited ones, and an optional field
            // is not required of a notification.
            IReadOnlyList<string> members = MemberNames(data);
            Assert.That(members[members.Count - 1], Is.EqualTo("Trace"));
            Assert.That(RequiredNames(data), Does.Not.Contain("Trace"));
        }

        /// <summary>
        /// The ModellingRule is the source's own statement about whether the
        /// field is always there, so it is what the schema's required list
        /// says.
        /// </summary>
        [Test]
        public void AMandatoryDeclaredFieldIsRequired()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateEventNodeSet(BaseEventType, DeclaredField("Mandatory")));

            Assert.That(
                RequiredNames(document.Events[EventKey].GetProperty("data")),
                Does.Contain("Trace"));
        }

        /// <summary>
        /// A type that re-declares an inherited field refines that field; it
        /// does not add a second one with the same name.
        /// </summary>
        [Test]
        public void ARedeclaredStandardFieldIsWrittenOnce()
        {
            using WotDocument document =
                WotNodeSetConverter.FromNodeSet(CreateSeverityEventNodeSet("700"));

            JsonElement affordance = document.Events[EventKey];
            IReadOnlyList<string> members = MemberNames(affordance.GetProperty("data"));

            Assert.That(members.Count(n => n == "Severity"), Is.EqualTo(1));

            // Severity is a field of an occurrence and nothing else: the
            // retired affordance-level term states no second fact.
            Assert.That(affordance.TryGetProperty("uav:severity", out _), Is.False);
            Assert.That(
                affordance.GetProperty("data").GetProperty("properties")
                    .GetProperty("Severity").GetProperty("type").GetString(),
                Is.EqualTo("integer"));
        }

        /// <summary>
        /// Section 13.4: a Condition Method is a component of the type that
        /// declares the Condition, so the type that owns it names the event the
        /// action acts on - definitely, and for any number of Condition events.
        /// </summary>
        [Test]
        public void AConditionMethodOwnedByTheEventTypeIsPairedWithIt()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateConditionMethodNodeSet(ownedByEventType: true));

            JsonElement acknowledge = document.Actions["Acknowledge"];

            Assert.That(
                acknowledge.GetProperty("uav:conditionAction").GetString(),
                Is.EqualTo("Acknowledge"));
            Assert.That(
                acknowledge.GetProperty("uav:actsOn").GetString(), Is.EqualTo(EventKey));
        }

        /// <summary>
        /// A hand-written NodeSet that hangs the Method off the Object rather
        /// than off the type still pairs, because exactly one Condition event
        /// leaves exactly one candidate.
        /// </summary>
        [Test]
        public void AConditionMethodOnTheObjectPairsWithTheOnlyConditionEvent()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateConditionMethodNodeSet(ownedByEventType: false));

            JsonElement acknowledge = document.Actions["Acknowledge"];

            Assert.That(
                acknowledge.GetProperty("uav:conditionAction").GetString(),
                Is.EqualTo("Acknowledge"));
            Assert.That(
                acknowledge.GetProperty("uav:actsOn").GetString(), Is.EqualTo(EventKey));
        }

        /// <summary>
        /// With more than one candidate nothing in the source says which
        /// Condition the Method acts on, so the pairing is reported rather than
        /// assigned to whichever came first: acknowledging the wrong alarm is
        /// worse than not being told which one to acknowledge.
        /// </summary>
        [Test]
        public void AnAmbiguousConditionMethodPairingIsReportedNotGuessed()
        {
            UANodeSet nodeSet = CreateConditionMethodNodeSet(ownedByEventType: false);
            AddSecondConditionEvent(nodeSet);

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(nodeSet);

            using WotDocument document = result.Value!;
            Assert.That(
                document.Actions["Acknowledge"]
                    .TryGetProperty("uav:conditionAction", out _),
                Is.False);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionTargetUnresolved),
                Is.True);
        }

        // ------------------------------------------------------------------
        // WoT -> NodeSet
        // ------------------------------------------------------------------

        /// <summary>
        /// A field the projected type inherits is already declared by the type
        /// it comes from. Creating it again would leave a Server holding two
        /// declarations of one field.
        /// </summary>
        [Test]
        public void InheritedConditionFieldsAreNotMaterializedAgain()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", ConditionDataMembers()));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(
                result.Value!.Items.OfType<UAVariable>().Select(v => v.BrowseName),
                Is.Empty);
        }

        /// <summary>
        /// A member the type adds is a field of that type, with the DataType,
        /// rank, dimensions and ModellingRule the schema states.
        /// </summary>
        [TestCase(true, "i=78")]
        [TestCase(false, "i=80")]
        public void AFieldTheTypeAddsIsMaterialized(bool required, string modellingRule)
        {
            string members = "\"EventId\":{\"type\":\"string\"}," +
                "\"Trace\":{\"type\":\"integer\",\"uav:mapToType\":\"i=6\"," +
                "\"uav:valueRank\":2,\"uav:arrayDimensions\":[3,4]," +
                "\"description\":\"A trace.\"}";
            string requiredList = required ? ",\"required\":[\"Trace\"]" : string.Empty;

            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", members, requiredList));

            UAVariable field = result.Value!.Items.OfType<UAVariable>().Single();
            Assert.That(field.BrowseName, Is.EqualTo("1:Trace"));
            Assert.That(field.DataType, Is.EqualTo("i=6"));
            Assert.That(field.ValueRank, Is.EqualTo(2));
            Assert.That(field.ArrayDimensions, Is.EqualTo("3,4"));
            Assert.That(field.Description[0].Value, Is.EqualTo("A trace."));
            Assert.That(
                field.References.Single(r =>
                    string.Equals(r.ReferenceType, "HasModellingRule", StringComparison.Ordinal))
                    .Value,
                Is.EqualTo(modellingRule));

            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>()
                .Single(t => t.BrowseName!.EndsWith("AlarmType", StringComparison.Ordinal));
            Assert.That(
                eventType.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "HasProperty", StringComparison.Ordinal) &&
                    string.Equals(r.Value, field.NodeId, StringComparison.Ordinal)),
                Is.True);
            Assert.That(field.ParentNodeId, Is.EqualTo(eventType.NodeId));
        }

        /// <summary>
        /// A member that is not a DataSchema names no field. It is reported and
        /// left to preservation rather than dropped.
        /// </summary>
        [Test]
        public void ADataMemberThatIsNotASchemaIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "ua:LimitAlarmType",
                    "\"EventId\":{\"type\":\"string\"},\"Trace\":\"not-a-schema\""));

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventFieldInvalid),
                Is.True);
        }

        /// <summary>
        /// One field cannot be declared twice, so two members reaching the same
        /// BrowseName are reported rather than silently collapsed.
        /// </summary>
        [Test]
        public void TwoDataMembersNamingOneFieldAreReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "ua:LimitAlarmType",
                    "\"EventId\":{\"type\":\"string\"}," +
                    "\"Trace\":{\"type\":\"integer\",\"uav:mapToType\":\"i=6\"}," +
                    "\"trace\":{\"type\":\"integer\",\"uav:mapToType\":\"i=6\"," +
                    "\"uav:browseName\":\"Trace\"}"));

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventFieldInvalid),
                Is.True);
            Assert.That(result.Value!.Items.OfType<UAVariable>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// Section 13.2 makes the pin the definitive identity of the same type
        /// the compact name reads, so the two agreeing is the ordinary case.
        /// </summary>
        [Test]
        public void AnAgreeingHintAndPinAreAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "ua:LimitAlarmType",
                    "\"EventId\":{\"type\":\"string\"}",
                    string.Empty,
                    "\"uav:conditionTypeId\":\"i=2955\","));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(SupertypeOfEvent(result.Value!), Is.EqualTo(LimitAlarmType));
        }

        /// <summary>
        /// A disagreement is a contradiction rather than a precedence question:
        /// honouring either one silently discards what the other says.
        /// </summary>
        [Test]
        public void AHintAndPinThatDisagreeAreRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "ua:LimitAlarmType",
                    "\"EventId\":{\"type\":\"string\"}",
                    string.Empty,
                    "\"uav:conditionTypeId\":\"i=2782\","));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionTypeConflict),
                Is.True);
        }

        /// <summary>
        /// A pin reaching a companion ConditionType this Binding cannot name is
        /// still definitive, so a hint it cannot resolve is not an error on its
        /// own.
        /// </summary>
        [Test]
        public void AnUnresolvableHintWithAPinIsAccepted()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "vendor:CustomAlarmType",
                    "\"EventId\":{\"type\":\"string\"}",
                    string.Empty,
                    "\"uav:conditionTypeId\":\"nsu=urn:test:pump;i=7001\","));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(SupertypeOfEvent(result.Value!), Is.Not.EqualTo(BaseEventType));
        }

        /// <summary>
        /// Section 13.4: a Condition Method is the standard Method OPC 10000-9
        /// declares, and the instance says so through its MethodDeclarationId.
        /// </summary>
        [TestCase("Acknowledge", "i=9111")]
        [TestCase("Confirm", "i=9113")]
        [TestCase("AddComment", "i=9029")]
        [TestCase("Enable", "i=9027")]
        [TestCase("Disable", "i=9028")]
        public void AConditionActionCarriesTheStandardMethodDeclaration(
            string action, string declaration)
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", "\"EventId\":{\"type\":\"string\"}") +
                "," + ConditionAction(action));

            UAMethod method = result.Value!.Items.OfType<UAMethod>().Single();
            Assert.That(method.MethodDeclarationId, Is.EqualTo(declaration));
            Assert.That(method.BrowseName, Is.EqualTo(action));
        }

        /// <summary>
        /// The pairing is recorded structurally: the Method is a component of
        /// the type that declares the Condition, which is what the forward
        /// direction reads back instead of guessing at.
        /// </summary>
        [Test]
        public void AConditionActionBecomesAComponentOfTheEventType()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", "\"EventId\":{\"type\":\"string\"}") +
                "," + ConditionAction("Acknowledge"));

            UAMethod method = result.Value!.Items.OfType<UAMethod>().Single();
            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>()
                .Single(t => t.BrowseName!.EndsWith("AlarmType", StringComparison.Ordinal));

            Assert.That(method.ParentNodeId, Is.EqualTo(eventType.NodeId));
            Assert.That(
                eventType.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal) &&
                    string.Equals(r.Value, method.NodeId, StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                result.Value.Items.OfType<UAObject>().Single().References.Any(r =>
                    string.Equals(r.Value, method.NodeId, StringComparison.Ordinal)),
                Is.False,
                "The Object does not also own the Method.");
        }

        /// <summary>
        /// A Method a type does not declare cannot be called on it. OPC 10000-9
        /// declares Acknowledge on AcknowledgeableConditionType, so pairing it
        /// with a plain ConditionType is a contradiction.
        /// </summary>
        [TestCase("Acknowledge")]
        [TestCase("Confirm")]
        public void AConditionActionTheProjectedTypeDoesNotDeclareIsRejected(string action)
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:ConditionType", "\"EventId\":{\"type\":\"string\"}") +
                "," + ConditionAction(action));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionNotDeclared),
                Is.True);
            Assert.That(
                result.Value!.Items.OfType<UAMethod>().Single().MethodDeclarationId,
                Is.Null);
        }

        /// <summary>
        /// Enable, Disable and AddComment are declared by ConditionType itself,
        /// so they pair with any Condition.
        /// </summary>
        [TestCase("AddComment")]
        [TestCase("Enable")]
        [TestCase("Disable")]
        public void AConditionTypeMethodPairsWithAPlainCondition(string action)
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:ConditionType", "\"EventId\":{\"type\":\"string\"}") +
                "," + ConditionAction(action));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionNotDeclared),
                Is.False);
            Assert.That(
                result.Value!.Items.OfType<UAMethod>().Single().MethodDeclarationId,
                Is.Not.Null);
        }

        /// <summary>
        /// A companion ConditionType this Binding cannot name declares fields
        /// and Methods it knows nothing about, so the pairing can be judged
        /// neither way. It is still recorded - the Method belongs to the type
        /// the pairing names - but no declaration OPC 10000-9 has not been
        /// shown to give it is asserted.
        /// </summary>
        [Test]
        public void APairingWithACompanionConditionTypeIsRecordedWithoutADeclaration()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent(
                    "vendor:CustomAlarmType",
                    "\"EventId\":{\"type\":\"string\"}",
                    string.Empty,
                    "\"uav:conditionTypeId\":\"nsu=urn:test:pump;i=7001\",") +
                "," + ConditionAction("Acknowledge"));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);

            UAMethod method = result.Value!.Items.OfType<UAMethod>().Single();
            UAObjectType eventType = result.Value.Items.OfType<UAObjectType>()
                .Single(t => t.BrowseName!.EndsWith("AlarmType", StringComparison.Ordinal));

            Assert.That(method.MethodDeclarationId, Is.Null);
            Assert.That(method.BrowseName, Is.EqualTo("Acknowledge"));
            Assert.That(method.ParentNodeId, Is.EqualTo(eventType.NodeId));
        }

        /// <summary>
        /// Section 13.4's own shape - a mandatory EventId and an optional
        /// Comment - materializes as the Method's InputArguments in the order
        /// OPC 10000-9 fixes, which is what T7's argument mapping supplies.
        /// </summary>
        [Test]
        public void TheConditionMethodArgumentsMaterializeInStandardOrder()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", "\"EventId\":{\"type\":\"string\"}") +
                "," + ConditionAction("Acknowledge"));

            UAVariable arguments = result.Value!.Items.OfType<UAVariable>()
                .Single(v => string.Equals(
                    v.BrowseName, "InputArguments", StringComparison.Ordinal));

            Assert.That(arguments.DataType, Is.EqualTo("i=296"));
            Assert.That(arguments.ValueRank, Is.EqualTo(1));
            Assert.That(
                ArgumentNames(arguments),
                Is.EqualTo(s_conditionMethodArguments).AsCollection);
        }

        // ------------------------------------------------------------------
        // Round trip
        // ------------------------------------------------------------------

        /// <summary>
        /// The specification's own Condition example is the reference document,
        /// so it must convert, import and come back naming the same Condition
        /// and the same pairing.
        /// </summary>
        /// <remarks>
        /// The example's event affordance links to the EventType definitions of
        /// example 27, so it converts through the asynchronous path that
        /// resolves the link rather than the synchronous one, which reports it.
        /// </remarks>
        [Test]
        public async Task TheConditionExampleRoundTripsAndImportsAsync()
        {
            using WotDocument authored = WotDocument.Parse(
                ReadExample("21-condition-limit-alarm.jsonld"));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(authored, null, new ExampleResolver())
                .ConfigureAwait(false);
            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);

            WotNodeSetImportTests.AssertImportable(result.Value!, "example 21");

            using WotDocument restored = WotNodeSetConverter.FromNodeSet(result.Value!);
            JsonElement projected = restored.Events["HighTemperatureAlarmType"];

            Assert.That(
                projected.GetProperty("uav:conditionType").GetString(),
                Is.EqualTo("ua:LimitAlarmType"));
            Assert.That(
                projected.GetProperty("uav:conditionTypeId").GetString(),
                Is.EqualTo(LimitAlarmType));
            Assert.That(
                MemberNames(projected.GetProperty("data")),
                Is.SupersetOf(s_conditionFields));

            foreach (string action in s_pairedConditionActions)
            {
                Assert.That(
                    restored.Actions[action].GetProperty("uav:conditionAction").GetString(),
                    Is.EqualTo(action));
                Assert.That(
                    restored.Actions[action].GetProperty("uav:actsOn").GetString(),
                    Is.EqualTo("HighTemperatureAlarmType"));
            }
        }

        /// <summary>
        /// A NodeSet carrying a Condition and its Methods survives the readable
        /// projection: what comes back projects and materializes to the same
        /// NodeSet again, so nothing is added and nothing is lost.
        /// </summary>
        [Test]
        public void AConditionNodeSetRoundTripsThroughTheReadableProjection()
        {
            UANodeSet source = CreateConditionMethodNodeSet(ownedByEventType: true);

            UANodeSet once;
            using (WotDocument document = WotNodeSetConverter.FromNodeSet(source))
            {
                once = WotNodeSetConverter.ToNodeSet(document);
            }

            UANodeSet twice;
            using (WotDocument document = WotNodeSetConverter.FromNodeSet(once))
            {
                twice = WotNodeSetConverter.ToNodeSet(document);
            }

            NodeSetComparisonResult comparison = NodeSetComparer.Compare(once, twice);
            Assert.That(
                comparison.AreEquivalent,
                Is.True,
                string.Join("; ", comparison.Differences));

            // The Condition survives as a Condition, with its Method still held
            // by the type that declares it.
            UAObjectType eventType = twice.Items.OfType<UAObjectType>()
                .Single(t => t.BrowseName.EndsWith(EventKey, StringComparison.Ordinal));
            UAMethod method = twice.Items.OfType<UAMethod>().Single();
            Assert.That(SupertypeOfEvent(twice), Is.EqualTo(LimitAlarmType));
            Assert.That(method.BrowseName, Is.EqualTo("Acknowledge"));
            Assert.That(method.MethodDeclarationId, Is.EqualTo("i=9111"));
            Assert.That(method.ParentNodeId, Is.EqualTo(eventType.NodeId));

            WotNodeSetImportTests.AssertImportable(twice, "condition round trip");
        }

        /// <summary>
        /// The generated document has to be one the Binding's own Section 13
        /// rules accept, or the converter's output could not be fed back to it.
        /// </summary>
        /// <remarks>
        /// The <c>uav:nodes</c> projection is stripped first: with it present
        /// the way back restores from it and never reaches the readable
        /// synthesis, so the readable document's own Section 13 conformance
        /// would go untested.
        /// </remarks>
        [Test]
        public void TheProjectedConditionDocumentSatisfiesSection13()
        {
            using WotDocument projected = WotNodeSetConverter.FromNodeSet(
                CreateConditionMethodNodeSet(ownedByEventType: true));
            using WotDocument document = WithoutNativeProjection(projected);

            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error)
                    .Select(d => d.Message),
                Is.Empty);

            // Section 13.4's own shape survived: the Method is the standard
            // Acknowledge, it holds the arguments it takes, and it belongs to
            // the type that declares the Condition.
            UAMethod method = result.Value.Items.OfType<UAMethod>().Single();
            Assert.That(method.MethodDeclarationId, Is.EqualTo("i=9111"));
            Assert.That(
                ArgumentNames(result.Value.Items.OfType<UAVariable>().Single(v =>
                    string.Equals(
                        v.BrowseName, "InputArguments", StringComparison.Ordinal))),
                Is.EqualTo(s_conditionMethodArguments).AsCollection);
        }

        /// <summary>
        /// A Condition Method that neither holds its arguments nor states the
        /// standard declaration cannot be paired: Section 13.4 requires the
        /// EventId input, and claiming a pairing without one would emit a
        /// document this Binding rejects.
        /// </summary>
        [Test]
        public void AConditionMethodWithoutAnEventIdInputIsNotPaired()
        {
            UANodeSet source = CreateConditionMethodNodeSet(ownedByEventType: true);
            source.Items.OfType<UAMethod>().Single().MethodDeclarationId = null;

            WotConversionResult<WotDocument> result =
                WotNodeSetConverter.FromNodeSetResult(source);

            using WotDocument document = result.Value;
            Assert.That(
                document.Actions["Acknowledge"]
                    .TryGetProperty("uav:conditionAction", out _),
                Is.False);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionTargetUnresolved),
                Is.True);
        }

        // ------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------

        private static IReadOnlyList<string> MemberNames(JsonElement data)
        {
            return [.. data.GetProperty("properties").EnumerateObject().Select(p => p.Name)];
        }

        private static IReadOnlyList<string> RequiredNames(JsonElement data)
        {
            return data.TryGetProperty("required", out JsonElement required)
                ? [.. required.EnumerateArray().Select(e => e.GetString()!)]
                : [];
        }

        private static List<string> ArgumentNames(UAVariable arguments)
        {
            var names = new List<string>();
            foreach (System.Xml.XmlNode item in arguments.Value.ChildNodes)
            {
                System.Xml.XmlNode name = item.SelectSingleNode(".//*[local-name()='Name']");
                if (name != null)
                {
                    names.Add(name.InnerText);
                }
            }
            return names;
        }

        private static string SupertypeOfEvent(UANodeSet nodeSet)
        {
            var generated = new HashSet<string>(StringComparer.Ordinal);
            foreach (UANode node in nodeSet.Items)
            {
                foreach (Reference reference in node.References ?? [])
                {
                    if (reference.IsForward &&
                        string.Equals(
                            reference.ReferenceType, "GeneratesEvent", StringComparison.Ordinal))
                    {
                        generated.Add(reference.Value);
                    }
                }
            }
            UANode eventType = nodeSet.Items.Single(n => generated.Contains(n.NodeId));
            return eventType.References.First(r =>
                string.Equals(r.ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                !r.IsForward).Value!;
        }

        private static UAVariable DeclaredField(string modellingRule)
        {
            return new UAVariable
            {
                NodeId = "ns=1;i=6001",
                BrowseName = "1:Trace",
                DisplayName = [new Export.LocalizedText { Value = "Trace" }],
                Description = [new Export.LocalizedText { Value = "A trace." }],
                ParentNodeId = "ns=1;i=1002",
                DataType = "i=6",
                ValueRank = 2,
                ArrayDimensions = "3,4",
                AccessLevel = 1,
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "HasModellingRule",
                        IsForward = true,
                        Value = string.Equals(
                            modellingRule, "Mandatory", StringComparison.Ordinal)
                            ? "i=78"
                            : "i=80"
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty", IsForward = false, Value = "ns=1;i=1002"
                    }
                ]
            };
        }

        /// <summary>
        /// An Object generating one EventType derived from the given supertype,
        /// optionally declaring one field of its own.
        /// </summary>
        private static UANodeSet CreateEventNodeSet(
            string superType,
            UAVariable declaredField = null)
        {
            var eventReferences = new List<Reference>
            {
                new Reference
                {
                    ReferenceType = "HasSubtype", IsForward = false, Value = superType
                }
            };
            var items = new List<UANode>
            {
                new UAObjectType
                {
                    NodeId = "ns=1;i=1001",
                    BrowseName = "1:PumpType",
                    DisplayName = [new Export.LocalizedText { Value = "PumpType" }],
                    References =
                    [
                        new Reference
                        {
                            ReferenceType = "HasSubtype",
                            IsForward = false,
                            Value = "i=58"
                        },
                        new Reference
                        {
                            ReferenceType = "GeneratesEvent",
                            IsForward = true,
                            Value = "ns=1;i=1002"
                        }
                    ]
                }
            };
            if (declaredField is not null)
            {
                eventReferences.Add(new Reference
                {
                    ReferenceType = "HasProperty",
                    IsForward = true,
                    Value = declaredField.NodeId
                });
            }
            items.Add(new UAObjectType
            {
                NodeId = "ns=1;i=1002",
                BrowseName = "1:" + EventKey,
                DisplayName = [new Export.LocalizedText { Value = EventKey }],
                References = [.. eventReferences]
            });
            if (declaredField is not null)
            {
                items.Add(declaredField);
            }

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:model" }],
                Items = [.. items]
            };
        }

        /// <summary>
        /// An EventType re-declaring the inherited Severity Property with an
        /// authored default.
        /// </summary>
        private static UANodeSet CreateSeverityEventNodeSet(string severity)
        {
            UANodeSet nodeSet = CreateEventNodeSet(BaseEventType);
            var items = new List<UANode>(nodeSet.Items);
            var eventType = (UAObjectType)items[1];
            eventType.References =
            [
                .. eventType.References,
                new Reference
                {
                    ReferenceType = "HasProperty", IsForward = true, Value = "ns=1;i=6002"
                }
            ];
            items.Add(new UAVariable
            {
                NodeId = "ns=1;i=6002",
                BrowseName = "Severity",
                DisplayName = [new Export.LocalizedText { Value = "Severity" }],
                ParentNodeId = "ns=1;i=1002",
                DataType = "i=5",
                AccessLevel = 1,
                Value = WotTestData.ParseValue(
                    "<uax:UInt16 xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">" +
                    severity + "</uax:UInt16>"),
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition", IsForward = true, Value = "i=68"
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty", IsForward = false, Value = "ns=1;i=1002"
                    }
                ]
            });
            nodeSet.Items = [.. items];
            return nodeSet;
        }

        /// <summary>
        /// A LimitAlarmType-derived EventType with an Acknowledge Method held
        /// either by the type that declares the Condition or by the Object.
        /// </summary>
        private static UANodeSet CreateConditionMethodNodeSet(bool ownedByEventType)
        {
            UANodeSet nodeSet = CreateEventNodeSet(LimitAlarmType);
            var items = new List<UANode>(nodeSet.Items);
            var owner = (UANode)(ownedByEventType ? items[1] : items[0]);
            owner.References =
            [
                .. owner.References,
                new Reference
                {
                    ReferenceType = "HasComponent", IsForward = true, Value = "ns=1;i=7001"
                }
            ];
            items.Add(new UAMethod
            {
                NodeId = "ns=1;i=7001",
                BrowseName = "Acknowledge",
                DisplayName = [new Export.LocalizedText { Value = "Acknowledge" }],
                ParentNodeId = owner.NodeId,
                MethodDeclarationId = "i=9111",
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasComponent",
                        IsForward = false,
                        Value = owner.NodeId
                    }
                ]
            });
            nodeSet.Items = [.. items];
            return nodeSet;
        }

        private static void AddSecondConditionEvent(UANodeSet nodeSet)
        {
            var items = new List<UANode>(nodeSet.Items);
            items[0].References =
            [
                .. items[0].References,
                new Reference
                {
                    ReferenceType = "GeneratesEvent", IsForward = true, Value = "ns=1;i=1004"
                }
            ];
            items.Add(new UAObjectType
            {
                NodeId = "ns=1;i=1004",
                BrowseName = "1:UnderTemperatureEventType",
                DisplayName = [new Export.LocalizedText { Value = "UnderTemperatureEventType" }],
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasSubtype",
                        IsForward = false,
                        Value = LimitAlarmType
                    }
                ]
            });
            nodeSet.Items = [.. items];
        }

        private static string ConditionDataMembers()
        {
            var members = new List<string>();
            foreach (string name in s_baseEventFields.Concat(s_conditionFields))
            {
                members.Add("\"" + name + "\":{\"type\":\"string\"}");
            }
            return string.Join(",", members);
        }

        private static string ConditionEvent(
            string conditionType,
            string members,
            string required = "",
            string pin = "")
        {
            return "\"events\":{\"highTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:browseName\":\"pump:HighTemperatureAlarmType\"," +
                "\"uav:conditionType\":\"" + conditionType + "\"," + pin +
                "\"data\":{\"type\":\"object\",\"properties\":{" + members + "}" +
                required + "}}}";
        }

        private static string ConditionAction(string action)
        {
            return "\"actions\":{\"act\":{\"@type\":\"uav:method\"," +
                "\"uav:conditionAction\":\"" + action + "\"," +
                "\"uav:actsOn\":\"highTemperature\"," +
                "\"input\":{\"type\":\"object\",\"required\":[\"EventId\"]," +
                "\"properties\":{" +
                "\"EventId\":{\"type\":\"string\",\"contentEncoding\":\"base64\"}," +
                "\"Comment\":{\"type\":\"string\"}}}}}";
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"vendor\":\"urn:test:vendor\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:Pump\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                members + "}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static byte[] ReadExample(string name)
        {
            string resource = typeof(WotEventDataTests).Assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith("Wot.Assets." + name, StringComparison.Ordinal));
            using Stream stream = typeof(WotEventDataTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>
        /// Serves the embedded specification examples by their relative
        /// reference, so an example that links to a sibling example resolves
        /// without any I/O (WoT Binding Section 5.1.5).
        /// </summary>
        private sealed class ExampleResolver : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = reference;
                int slash = name.LastIndexOf('/');
                if (slash >= 0)
                {
                    name = name.Substring(slash + 1);
                }
                if (!name.EndsWith(".jsonld", StringComparison.Ordinal))
                {
                    return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
                }
                return new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(ReadExample(name), "application/td+json"));
            }
        }

        /// <summary>
        /// Re-parses a generated document without its exact projections, so the
        /// way back takes the readable synthesis path rather than restoring
        /// from <c>uav:nodes</c>.
        /// </summary>
        private static WotDocument WithoutNativeProjection(WotDocument document)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Name is "uav:nodes" or "uav:nodeSet")
                    {
                        continue;
                    }
                    writer.WritePropertyName(member.Name);
                    member.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return WotDocument.Parse(buffer.ToArray());
        }
    }
}
