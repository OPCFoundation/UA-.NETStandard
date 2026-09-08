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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises the Alarms and Conditions mapping of WoT Binding Section 13:
    /// the ConditionType an event projects and the conformance rules that keep
    /// a Condition actionable.
    /// </summary>
    [TestFixture]
    public sealed class WotConditionMappingTests
    {
        private const string LimitAlarmType = "i=2955";
        private const string BaseEventType = "i=2041";

        /// <summary>
        /// The worked shape of Section 13.5 is the reference document, so it
        /// must convert without a single complaint.
        /// </summary>
        [Test]
        public void TheWorkedShapeOfTheSpecificationConvertsCleanly()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                ConditionAction("Acknowledge", "highTemperature", withEventId: true));

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
        }

        /// <summary>
        /// A Condition event derives from the ConditionType it names, not from
        /// BaseEventType. Otherwise the whole Condition state model is lost.
        /// </summary>
        [Test]
        public void AConditionEventDerivesFromTheNamedConditionType()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true));

            Assert.That(SupertypeOfEvent(result.Value), Is.EqualTo(LimitAlarmType));
        }

        /// <summary>
        /// An event naming no ConditionType is an ordinary event and keeps the
        /// BaseEventType default.
        /// </summary>
        [Test]
        public void AnOrdinaryEventStillDerivesFromBaseEventType()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"tick\":{\"@type\":\"uav:eventType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{}}}}");

            Assert.That(SupertypeOfEvent(result.Value), Is.EqualTo(BaseEventType));
        }

        /// <summary>
        /// The definitive pin of Section 13.2 wins over the readable hint, and
        /// reaches a ConditionType outside the four Section 13.1 scopes.
        /// </summary>
        [Test]
        public void ADefinitiveConditionTypeIdWinsOverTheHint()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"events\":{\"highTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:conditionType\":\"vendor:CustomAlarmType\"," +
                "\"uav:conditionTypeId\":\"nsu=urn:test:pump;i=7001\"," +
                "\"data\":{\"type\":\"object\",\"properties\":" +
                "{\"EventId\":{\"type\":\"string\"}}}}}");

            Assert.That(
                result.Diagnostics.Where(d => d.Severity == WotDiagnosticSeverity.Error),
                Is.Empty);
            Assert.That(SupertypeOfEvent(result.Value), Is.Not.EqualTo(BaseEventType));
        }

        /// <summary>
        /// A ConditionType this Binding cannot resolve and that carries no pin
        /// is reported rather than guessed.
        /// </summary>
        [Test]
        public void AnUnresolvableConditionTypeIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("vendor:CustomAlarmType", withEventId: true));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.UnresolvedConditionType),
                Is.True);
        }

        /// <summary>
        /// Section 13.3: without EventId a consumer can receive the
        /// notification but can never name the occurrence to act on.
        /// </summary>
        [Test]
        public void AConditionEventWithoutEventIdIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: false));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionEventIdMissing),
                Is.True);
        }

        /// <summary>
        /// Section 13.2 closes the set of Condition Methods, so anything else
        /// is a defect rather than an extension point.
        /// </summary>
        [Test]
        public void AConditionActionOutsideTheClosedSetIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                ConditionAction("Shelve", "highTemperature", withEventId: true));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidConditionAction),
                Is.True);
        }

        /// <summary>
        /// Section 13.4: an action with nothing to act on cannot be invoked.
        /// </summary>
        [Test]
        public void AConditionActionWithoutActsOnIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                "\"actions\":{\"ack\":{\"uav:conditionAction\":\"Acknowledge\"," +
                "\"input\":{\"type\":\"object\",\"properties\":" +
                "{\"EventId\":{\"type\":\"string\"}}}}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidConditionTarget),
                Is.True);
        }

        /// <summary>
        /// Section 13.4 requires the target to be an event affordance in the
        /// same document that actually carries a ConditionType.
        /// </summary>
        [Test]
        public void AConditionActionNamingANonConditionEventIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                ConditionAction("Acknowledge", "somethingElse", withEventId: true));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidConditionTarget),
                Is.True);
        }

        /// <summary>
        /// Section 13.4 requires the target event itself to carry the
        /// Condition metadata, so an ordinary event cannot receive Methods.
        /// </summary>
        [Test]
        public void AConditionActionNamingAnOrdinaryEventIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                OrdinaryEvent("tick") + "," +
                ConditionAction("Acknowledge", "tick", withEventId: true));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidConditionTarget),
                Is.True);
        }

        /// <summary>
        /// Section 13.4: the three occurrence-level Methods bind to one Event
        /// through EventId, so the input is what makes them invocable.
        /// </summary>
        [TestCase("Acknowledge")]
        [TestCase("Confirm")]
        [TestCase("AddComment")]
        public void AnOccurrenceActionWithoutAnEventIdInputIsReported(string action)
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                ConditionAction(action, "highTemperature", withEventId: false));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionInputMissing),
                Is.True);
        }

        /// <summary>
        /// Enable and Disable act on the Condition instance rather than one
        /// occurrence, so requiring EventId of them would reject a valid
        /// document.
        /// </summary>
        [TestCase("Enable")]
        [TestCase("Disable")]
        public void AnInstanceActionNeedsNoEventIdInput(string action)
        {
            WotConversionResult<UANodeSet> result = Convert(
                ConditionEvent("ua:LimitAlarmType", withEventId: true) + "," +
                ConditionAction(action, "highTemperature", withEventId: false));

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ConditionActionInputMissing),
                Is.False);
        }

        private static string ConditionEvent(string conditionType, bool withEventId)
        {
            string properties = withEventId
                ? "\"EventId\":{\"type\":\"string\",\"contentEncoding\":\"base64\"}," +
                  "\"Severity\":{\"type\":\"integer\"}"
                : "\"Severity\":{\"type\":\"integer\"}";
            return "\"events\":{\"highTemperature\":{\"@type\":\"uav:eventType\"," +
                "\"uav:conditionType\":\"" + conditionType + "\"," +
                "\"data\":{\"type\":\"object\",\"properties\":{" + properties + "}}}}";
        }

        private static string ConditionAction(
            string action, string actsOn, bool withEventId)
        {
            string properties = withEventId
                ? "\"EventId\":{\"type\":\"string\",\"contentEncoding\":\"base64\"}," +
                  "\"Comment\":{\"type\":\"string\"}"
                : "\"Comment\":{\"type\":\"string\"}";
            return "\"actions\":{\"acknowledgeHighTemperature\":{\"@type\":\"uav:method\"," +
                "\"uav:conditionAction\":\"" + action + "\"," +
                "\"uav:actsOn\":\"" + actsOn + "\"," +
                "\"input\":{\"type\":\"object\",\"properties\":{" + properties + "}}}}";
        }

        private static string OrdinaryEvent(string name)
        {
            return "\"events\":{\"" + name + "\":{\"@type\":\"uav:eventType\"," +
                "\"data\":{\"type\":\"object\",\"properties\":" +
                "{\"EventId\":{\"type\":\"string\"}}}}}";
        }

        private static string SupertypeOfEvent(UANodeSet nodeSet)
        {
            UANode eventType = nodeSet.Items.First(i => i is UAObjectType);
            return eventType.References.First(r =>
                string.Equals(r.ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                !r.IsForward).Value;
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
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                members + "}");

            using WotDocument document = WotDocument.Parse(json);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
