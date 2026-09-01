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

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for MQTT, CoAP, BACnet, LoRaWAN and OPC UA binding planners.
    /// </summary>
    [TestFixture]
    public sealed class BindingPlannerTests
    {
        /// <summary>
        /// The implicit BaseEventType default clauses followed by the two clauses that the
        /// superseded <c>uav:eventFields</c> spelling adds to them.
        /// </summary>
        private static readonly string[] s_legacyEventFieldsOnDefault =
        [
            "EventId", "EventType", "SourceNode", "SourceName",
            "Time", "ReceiveTime", "Message", "Severity", "LocalTime"
        ];

        private static WotBindingPlanContext DefaultContext()
        {
            return new WotBindingPlanContext();
        }

        private static WotAffordanceForm MakePropertyForm(
            string formJson,
            string affordanceName = "p",
            string affordanceJson = "{}",
            ImmutableArray<string> ops = default)
        {
            using var formDoc = JsonDocument.Parse(formJson);
            using var affordanceDoc = JsonDocument.Parse(affordanceJson);
            JsonElement formElement = formDoc.RootElement.Clone();
            JsonElement affordanceElement = affordanceDoc.RootElement.Clone();

            string? href = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("href", out JsonElement hrefEl) &&
                hrefEl.ValueKind == JsonValueKind.String)
            {
                href = hrefEl.GetString();
            }

            string? contentType = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("contentType", out JsonElement ctEl) &&
                ctEl.ValueKind == JsonValueKind.String)
            {
                contentType = ctEl.GetString();
            }

            ImmutableArray<string> operations = ops.IsDefault
                ? (ImmutableArray<string>)["readproperty", "writeproperty"]
                : ops;

            return new WotAffordanceForm(
                WotAffordanceKind.Property,
                affordanceName,
                operations,
                href,
                contentType,
                null,
                [],
                "/properties/" + affordanceName + "/forms/0",
                formElement,
                affordanceElement);
        }

        private static WotAffordanceForm MakeActionForm(string formJson, string affordanceName = "act")
        {
            using var formDoc = JsonDocument.Parse(formJson);
            JsonElement formElement = formDoc.RootElement.Clone();

            string? href = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("href", out JsonElement hrefEl) &&
                hrefEl.ValueKind == JsonValueKind.String)
            {
                href = hrefEl.GetString();
            }

            return new WotAffordanceForm(
                WotAffordanceKind.Action,
                affordanceName,
                ["invokeaction"],
                href,
                null,
                null,
                [],
                "/actions/" + affordanceName + "/forms/0",
                formElement,
                default);
        }

        private static WotAffordanceForm MakeEventForm(string formJson, string affordanceName = "ev")
        {
            using var formDoc = JsonDocument.Parse(formJson);
            JsonElement formElement = formDoc.RootElement.Clone();

            string? href = null;
            if (formElement.ValueKind == JsonValueKind.Object &&
                formElement.TryGetProperty("href", out JsonElement hrefEl) &&
                hrefEl.ValueKind == JsonValueKind.String)
            {
                href = hrefEl.GetString();
            }

            return new WotAffordanceForm(
                WotAffordanceKind.Event,
                affordanceName,
                ["subscribeevent", "unsubscribeevent"],
                href,
                null,
                null,
                [],
                "/events/" + affordanceName + "/forms/0",
                formElement,
                default);
        }

        [Test]
        public void MqttPlannerCompilesValidPropertyForm()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883/sensors/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Is.Not.Empty);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        [Test]
        public void MqttPlannerExtractsTopicFromHref()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883/my/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("my/topic"));
        }

        [Test]
        public void MqttPlannerUsesExplicitTopic()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883","mqv:topic":"custom/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("custom/topic"));
        }

        [Test]
        public void MqttPlannerRejectsFormWithNoHref()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }

        [Test]
        public void MqttPlannerRejectsNonMqttScheme()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://broker.example.com/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void MqttPlannerRejectsEmptyTopic()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void MqttPlannerRejectsInvalidQos()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883/topic","mqv:qos":3,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void MqttPlannerRejectsTopicExceedingMaxLength()
        {
            var planner = new MqttBindingPlanner();
            string longTopic = new string('x', 65536);
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"mqtt://broker.example.com:1883/" + longTopic + "\",\"op\":\"readproperty\"}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        [TestCase("#")]
        [TestCase("tenant/+/temperature")]
        public void MqttPlannerRejectsWildcardTopicByDefault(string topic)
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"mqtt://broker.example.com:1883\",\"mqv:topic\":\"" + topic +
                "\",\"op\":\"observeproperty\"}",
                ops: ["observeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void MqttPlannerAllowsWildcardTopicWhenPolicyOptsIn()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883","mqv:topic":"tenant/+","op":"observeproperty"}""",
                ops: ["observeproperty"]);
            var context = new WotBindingPlanContext(bounds: new WotBindingBounds { AllowMqttWildcardTopics = true });

            WotBindingCompilation result = planner.Compile(form, context);

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("tenant/+"));
        }

        [Test]
        public void MqttPlannerWarnsOnUnknownControlPacket()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883/topic","mqv:controlPacket":"unknown","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Severity == WotDiagnosticSeverity.Warning &&
                d.Code == WotBindingDiagnosticCode.UnknownVocabularyTerm), Is.True);
        }

        [Test]
        public void MqttPlannerAcceptsMqttsScheme()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtts://broker.example.com:8883/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void MqttPlannerSetsRetainFlagWhenTrue()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"mqtt://broker.example.com:1883/topic","mqv:retain":true,"op":"writeproperty"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(
                result.Entries[0].Addressing.Metadata.TryGetValue("retain", out string? retain) &&
                retain == "true",
                Is.True);
        }

        [Test]
        public void MqttPlannerIdentity()
        {
            var planner = new MqttBindingPlanner();
            Assert.That(planner.Identity.Id, Is.EqualTo("w3c.mqtt"));
        }

        [Test]
        public void HttpPlannerRejectsUnsafeContentType()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/p","contentType":"application/json\r\nX-Injected: pwned"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void HttpPlannerAcceptsSafeContentType()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/p","contentType":"application/json; charset=utf-8"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Payload.ContentType, Is.EqualTo("application/json; charset=utf-8"));
        }

        [Test]
        public void CoapPlannerCompilesValidPropertyForm()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"coap://sensor.example.com:5683/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        [Test]
        public void CoapPlannerAcceptsCoapsScheme()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"coaps://sensor.example.com:5684/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void CoapPlannerRejectsInvalidScheme()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://example.com/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void CoapPlannerRejectsInvalidMethod()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"coap://sensor.example.com:5683/temp","cov:method":"INVALID","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void CoapPlannerAcceptsValidMethod()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"coap://sensor.example.com:5683/temp","cov:method":"PUT","op":"writeproperty"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].OperationInfo.Method, Is.EqualTo("PUT"));
        }

        [Test]
        public void CoapPlannerRejectsFormWithNoHref()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void CoapPlannerIsNotExecutable()
        {
            var planner = new CoapBindingPlanner();
            Assert.That(planner.Capability.IsExecutable, Is.False);
        }

        [Test]
        public void BacnetPlannerCompilesValidForm()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"bacv:objectType":"analog-input","bacv:instanceNumber":1,"bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void BacnetPlannerRejectsMissingObjectType()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"bacv:instanceNumber":1,"bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void BacnetPlannerRejectsMissingInstanceNumber()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"bacv:objectType":"analog-input","bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void BacnetPlannerRejectsMissingPropertyIdentifier()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"bacv:objectType":"analog-input","bacv:instanceNumber":0}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void BacnetPlannerRejectsUsePriorityOutOfRange()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                "{\"bacv:objectType\":\"analog-output\",\"bacv:instanceNumber\":1," +
                "\"bacv:propertyIdentifier\":\"present-value\",\"bacv:usePriority\":17}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void BacnetPlannerAcceptsUsePriorityAtBoundary()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                "{\"bacv:objectType\":\"analog-output\",\"bacv:instanceNumber\":1," +
                "\"bacv:propertyIdentifier\":\"present-value\",\"bacv:usePriority\":1}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void BacnetPlannerIsNotExecutable()
        {
            var planner = new BacnetBindingPlanner();
            Assert.That(planner.Capability.IsExecutable, Is.False);
        }

        [Test]
        public void LoRaWanPlannerCompilesValidForm()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"0102030405060708"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata.ContainsKey("devEui"), Is.True);
        }

        [Test]
        public void LoRaWanPlannerRejectsMissingDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:fPort":1}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        [Test]
        public void LoRaWanPlannerRejectsInvalidDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"ZZZZZZZZZZZZZZZZ"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void LoRaWanPlannerRejectsTooShortDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"01020304"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void LoRaWanPlannerRejectsInvalidFPort()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"0102030405060708","lorawan:fPort":0}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        [Test]
        public void LoRaWanPlannerRejectsFPortAboveMax()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"0102030405060708","lorawan:fPort":224}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void LoRaWanPlannerAcceptsFPortAtBoundary()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"lorawan:DevEUI":"0102030405060708","lorawan:fPort":223}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void LoRaWanPlannerIsNotExecutable()
        {
            var planner = new LoRaWanBindingPlanner();
            Assert.That(planner.Capability.IsExecutable, Is.False);
        }

        [Test]
        public void OpcUaPlannerCompilesValidFormWithHref()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.tcp://server.example.com:4840","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Is.EqualTo("ns=2;i=1001"));
        }

        [Test]
        public void OpcUaPlannerExtractsNodeIdFromHrefPath()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.tcp://server.example.com:4840/ns=2;i=1002","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Does.Contain("ns=2"));
        }

        [Test]
        public void OpcUaPlannerRejectsNonOpcScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"http://server.example.com/p","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnsupportedScheme), Is.True);
        }

        [Test]
        public void OpcUaPlannerRejectsFormWithNoNodeId()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.tcp://server.example.com:4840/somenode","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            // "somenode" doesn't look like a NodeId (no '='), so it should be rejected.
            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void OpcUaPlannerUsesContextBaseUriWhenNoHref()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"uav:id":"ns=2;i=2000","op":"readproperty"}""",
                ops: ["readproperty"]);
            var context = new WotBindingPlanContext(
                baseUri: "opc.tcp://server.example.com:4840");

            WotBindingCompilation result = planner.Compile(form, context);

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Is.EqualTo("ns=2;i=2000"));
        }

        [Test]
        public void OpcUaPlannerRejectsFormWithNoHrefAndNoBaseUri()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"uav:id":"ns=2;i=2000","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        [Test]
        public void OpcUaPlannerAcceptsOpcHttpsScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.https://server.example.com:443","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void OpcUaPlannerAcceptsOpcWssScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.wss://server.example.com:443","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        [Test]
        public void OpcUaPlannerAddsComponentOfToMetadata()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\"," +
                "\"uav:id\":\"ns=2;i=1001\",\"uav:componentOf\":\"ns=1;i=500\"," +
                "\"op\":\"readproperty\"}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata.ContainsKey("componentOf"), Is.True);
        }

        [Test]
        public void OpcUaPlannerCompilesLegacyEventFieldsOntoTheDocumentedDefault()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakeEventForm(
                "{\"href\":\"opc.tcp://server.example.com:4840\"," +
                "\"uav:id\":\"ns=2;i=3001\"," +
                "\"uav:eventFields\":[\"Message\",\"LocalTime\"]}");

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            WotEventSelection? selection = result.Entries[0].EventSelection;
            Assert.That(selection, Is.Not.Null);
            Assert.That(selection!.Origin, Is.EqualTo(WotEventSelectionOrigin.Legacy));
            Assert.That(
                selection.Clauses.ToList().Select(c => c.BrowsePath),
                Is.EqualTo(s_legacyEventFieldsOnDefault),
                "The superseded spelling adds to the implicit BaseEventType default and never repeats it.");
        }

        [Test]
        public void OpcUaPlannerExtractsNodeIdFromQueryString()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                """{"href":"opc.tcp://server.example.com:4840?id=ns%3D2%3Bi%3D1003","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Does.Contain("ns=2"));
        }

        [Test]
        public void OpcUaPlannerIdentityId()
        {
            var planner = new OpcUaBindingPlanner();
            Assert.That(planner.Identity.Id, Is.EqualTo("opc.opcua"));
        }
    }
}
