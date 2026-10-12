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

using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;
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

        /// <summary>
        /// Verifies that published URI target vectors match OPC UA planning.
        /// </summary>
        [Test]
        public void PublishedUriTargetVectorsMatchOpcUaPlanning()
        {
            foreach (JsonElement test in WotSpecVectors.Cases("uriTargets"))
            {
                var form = new System.Text.Json.Nodes.JsonObject
                {
                    ["href"] = test.GetProperty("href").GetString(),
                    ["op"] = "readproperty"
                };
                var affordance = new System.Text.Json.Nodes.JsonObject();
                if (test.TryGetProperty("browsePath", out JsonElement path))
                {
                    affordance["uav:browsePath"] = path.GetString();
                    affordance["uav:browsePathAnchor"] = test.GetProperty("anchor").GetString();
                }
                WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                    MakePropertyForm(form.ToJsonString(), affordanceJson: affordance.ToJsonString(),
                        ops: ["readproperty"]),
                    new WotBindingPlanContext(baseUri: test.GetProperty("base").GetString()));

                Assert.That(result.IsSupported, Is.True,
                    test.GetProperty("id").GetString() + ": " + string.Join("; ", result.Diagnostics));
                Assert.That(result.Entries, Has.Length.EqualTo(1));
                WotCompiledForm compiled = result.Entries[0];
                Assert.That(compiled.Endpoint.BaseUri, Is.EqualTo(test.GetProperty("endpoint").GetString()),
                    test.GetProperty("id").GetString());
                string? expected = test.GetProperty("nodeId").GetString();
                Assert.That(compiled.Addressing.Metadata.TryGetValue("nodeId", out string? actual),
                    Is.EqualTo(expected is not null));
                Assert.That(actual, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Verifies that duplicate node ID queries return diagnostics for href and base URI.
        /// </summary>
        [Test]
        public void DuplicateNodeIdQueriesReturnDiagnosticsForHrefAndBaseUri()
        {
            foreach (string uri in new[]
            {
                "opc.tcp://host:4840/UA?id=i%3D1&id=i%3D2",
                "opc.tcp://host:4840/UA?%69d=i%3D1&id=i%3D2"
            })
            {
                foreach (bool inBase in new[] { false, true })
                {
                    WotAffordanceForm form = MakePropertyForm(
                        inBase ? """{"uav:id":"i=2258","op":"readproperty"}""" :
                            "{\"href\":" + JsonSerializer.Serialize(uri) + ",\"op\":\"readproperty\"}",
                        ops: ["readproperty"]);
                    WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                        form, new WotBindingPlanContext(baseUri: inBase ? uri : null));

                    Assert.That(result.IsSupported, Is.False);
                    Assert.That(result.Entries, Is.Empty);
                    Assert.That(result.Diagnostics.Any(diagnostic =>
                        diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
                }
            }
        }

        /// <summary>
        /// Verifies that encoded node ID query names resolve from absolute and relative hrefs.
        /// </summary>
        [Test]
        public void EncodedNodeIdQueryNamesResolveFromAbsoluteAndRelativeHrefs()
        {
            foreach (bool relative in new[] { false, true })
            {
                string href = relative ? "?%69d=i%3D2258" : "opc.tcp://host:4840/UA?%69d=i%3D2258";
                WotBindingCompilation result = new OpcUaBindingPlanner().Compile(
                    MakePropertyForm("{\"href\":" + JsonSerializer.Serialize(href) + ",\"op\":\"readproperty\"}",
                        ops: ["readproperty"]),
                    new WotBindingPlanContext(baseUri: relative ? "opc.tcp://host:4840/UA" : null));

                Assert.That(result.IsSupported, Is.True, string.Join("; ", result.Diagnostics));
                Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Is.EqualTo("i=2258"));
                Assert.That(result.Entries[0].Endpoint.BaseUri, Is.EqualTo("opc.tcp://host:4840/UA"));
            }
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
                ? ["readproperty", "writeproperty"]
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

        /// <summary>
        /// Verifies that MQTT planner compiles valid property form.
        /// </summary>
        [Test]
        public void MqttPlannerCompilesValidPropertyForm()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883/sensors/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries, Is.Not.Empty);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        /// <summary>
        /// Verifies that MQTT planner extracts topic from href.
        /// </summary>
        [Test]
        public void MqttPlannerExtractsTopicFromHref()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883/my/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("my/topic"));
        }

        /// <summary>
        /// Verifies that MQTT planner uses explicit topic.
        /// </summary>
        [Test]
        public void MqttPlannerUsesExplicitTopic()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883","mqv:topic":"custom/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("custom/topic"));
        }

        /// <summary>
        /// Verifies that MQTT planner rejects form with no href.
        /// </summary>
        [Test]
        public void MqttPlannerRejectsFormWithNoHref()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.HasErrors, Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner rejects non MQTT scheme.
        /// </summary>
        [Test]
        public void MqttPlannerRejectsNonMqttScheme()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"http://broker.example.com/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that MQTT planner rejects empty topic.
        /// </summary>
        [Test]
        public void MqttPlannerRejectsEmptyTopic()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner rejects invalid qos.
        /// </summary>
        [Test]
        public void MqttPlannerRejectsInvalidQos()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883/topic","mqv:qos":3,"op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner rejects topic exceeding max length.
        /// </summary>
        [Test]
        public void MqttPlannerRejectsTopicExceedingMaxLength()
        {
            var planner = new MqttBindingPlanner();
            string longTopic = new('x', 65536);
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"mqtt://broker.example.com:1883/" + longTopic + "\",\"op\":\"readproperty\"}",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.BoundsExceeded), Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner rejects wildcard topic by default.
        /// </summary>
        [TestCase("#")]
        [TestCase("tenant/+/temperature")]
        public void MqttPlannerRejectsWildcardTopicByDefault(string topic)
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                "{\"href\":\"mqtt://broker.example.com:1883\",\"mqv:topic\":\"" +
                topic +
                "\",\"op\":\"observeproperty\"}",
                ops: ["observeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner allows wildcard topic when policy opts in.
        /// </summary>
        [Test]
        public void MqttPlannerAllowsWildcardTopicWhenPolicyOptsIn()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883","mqv:topic":"tenant/+","op":"observeproperty"}""",
                ops: ["observeproperty"]);
            var context = new WotBindingPlanContext(bounds: new WotBindingBounds { AllowMqttWildcardTopics = true });

            WotBindingCompilation result = planner.Compile(form, context);

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Target, Is.EqualTo("tenant/+"));
        }

        /// <summary>
        /// Verifies that MQTT planner warns on unknown control packet.
        /// </summary>
        [Test]
        public void MqttPlannerWarnsOnUnknownControlPacket()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtt://broker.example.com:1883/topic","mqv:controlPacket":"unknown","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Diagnostics.Any(d =>
                d.Severity == WotDiagnosticSeverity.Warning &&
                d.Code == WotBindingDiagnosticCode.UnknownVocabularyTerm), Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner accepts mqtts scheme.
        /// </summary>
        [Test]
        public void MqttPlannerAcceptsMqttsScheme()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"mqtts://broker.example.com:8883/topic","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        /// <summary>
        /// Verifies that MQTT planner sets retain flag when true.
        /// </summary>
        [Test]
        public void MqttPlannerSetsRetainFlagWhenTrue()
        {
            var planner = new MqttBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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

        /// <summary>
        /// Verifies that HTTP planner rejects unsafe content type.
        /// </summary>
        [Test]
        public void HttpPlannerRejectsUnsafeContentType()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"http://example.com/p","contentType":"application/json\r\nX-Injected: pwned"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that HTTP planner accepts safe content type.
        /// </summary>
        [Test]
        public void HttpPlannerAcceptsSafeContentType()
        {
            var planner = new HttpBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"http://example.com/p","contentType":"application/json; charset=utf-8"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Payload.ContentType, Is.EqualTo("application/json; charset=utf-8"));
        }

        /// <summary>
        /// Verifies that coap planner compiles valid property form.
        /// </summary>
        [Test]
        public void CoapPlannerCompilesValidPropertyForm()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"coap://sensor.example.com:5683/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Operation, Is.EqualTo(WoTBindingCapabilityEnum.ReadProperty));
        }

        /// <summary>
        /// Verifies that coap planner accepts coaps scheme.
        /// </summary>
        [Test]
        public void CoapPlannerAcceptsCoapsScheme()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"coaps://sensor.example.com:5684/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        /// <summary>
        /// Verifies that coap planner rejects invalid scheme.
        /// </summary>
        [Test]
        public void CoapPlannerRejectsInvalidScheme()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"http://example.com/temp","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that coap planner rejects invalid method.
        /// </summary>
        [Test]
        public void CoapPlannerRejectsInvalidMethod()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"coap://sensor.example.com:5683/temp","cov:method":"INVALID","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that coap planner accepts valid method.
        /// </summary>
        [Test]
        public void CoapPlannerAcceptsValidMethod()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"coap://sensor.example.com:5683/temp","cov:method":"PUT","op":"writeproperty"}""",
                ops: ["writeproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].OperationInfo.Method, Is.EqualTo("PUT"));
        }

        /// <summary>
        /// Verifies that coap planner rejects form with no href.
        /// </summary>
        [Test]
        public void CoapPlannerRejectsFormWithNoHref()
        {
            var planner = new CoapBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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

        /// <summary>
        /// Verifies that bacnet planner compiles valid form.
        /// </summary>
        [Test]
        public void BacnetPlannerCompilesValidForm()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"bacv:objectType":"analog-input","bacv:instanceNumber":1,"bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        /// <summary>
        /// Verifies that bacnet planner rejects missing object type.
        /// </summary>
        [Test]
        public void BacnetPlannerRejectsMissingObjectType()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"bacv:instanceNumber":1,"bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        /// <summary>
        /// Verifies that bacnet planner rejects missing instance number.
        /// </summary>
        [Test]
        public void BacnetPlannerRejectsMissingInstanceNumber()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"bacv:objectType":"analog-input","bacv:propertyIdentifier":"present-value"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that bacnet planner rejects missing property identifier.
        /// </summary>
        [Test]
        public void BacnetPlannerRejectsMissingPropertyIdentifier()
        {
            var planner = new BacnetBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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

        /// <summary>
        /// Verifies that lo ra wan planner compiles valid form.
        /// </summary>
        [Test]
        public void LoRaWanPlannerCompilesValidForm()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:DevEUI":"0102030405060708"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata.ContainsKey("devEui"), Is.True);
        }

        /// <summary>
        /// Verifies that lo ra wan planner rejects missing dev eui.
        /// </summary>
        [Test]
        public void LoRaWanPlannerRejectsMissingDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:fPort":1}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
        }

        /// <summary>
        /// Verifies that lo ra wan planner rejects invalid dev eui.
        /// </summary>
        [Test]
        public void LoRaWanPlannerRejectsInvalidDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:DevEUI":"ZZZZZZZZZZZZZZZZ"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that lo ra wan planner rejects too short dev eui.
        /// </summary>
        [Test]
        public void LoRaWanPlannerRejectsTooShortDevEUI()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:DevEUI":"01020304"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that lo ra wan planner rejects invalid f port.
        /// </summary>
        [Test]
        public void LoRaWanPlannerRejectsInvalidFPort()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:DevEUI":"0102030405060708","lorawan:fPort":0}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that lo ra wan planner rejects f port above max.
        /// </summary>
        [Test]
        public void LoRaWanPlannerRejectsFPortAboveMax()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"lorawan:DevEUI":"0102030405060708","lorawan:fPort":224}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that lo ra wan planner accepts f port at boundary.
        /// </summary>
        [Test]
        public void LoRaWanPlannerAcceptsFPortAtBoundary()
        {
            var planner = new LoRaWanBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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

        /// <summary>
        /// Verifies that OPC UA planner compiles valid form with href.
        /// </summary>
        [Test]
        public void OpcUaPlannerCompilesValidFormWithHref()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"opc.tcp://server.example.com:4840","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Is.EqualTo("ns=2;i=1001"));
        }

        /// <summary>
        /// Verifies that OPC UA planner extracts node ID from href path.
        /// </summary>
        [Test]
        public void OpcUaPlannerExtractsNodeIdFromHrefPath()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"opc.tcp://server.example.com:4840/ns=2;i=1002","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Does.Contain("ns=2"));
        }

        /// <summary>
        /// Verifies that OPC UA planner rejects non OPC scheme.
        /// </summary>
        [Test]
        public void OpcUaPlannerRejectsNonOpcScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"http://server.example.com/p","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.UnsupportedScheme), Is.True);
        }

        /// <summary>
        /// Verifies that OPC UA planner rejects form with no node ID.
        /// </summary>
        [Test]
        public void OpcUaPlannerRejectsFormWithNoNodeId()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"opc.tcp://server.example.com:4840/somenode","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            // "somenode" doesn't look like a NodeId (no '='), so it should be rejected.
            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that OPC UA planner uses context base URI when no href.
        /// </summary>
        [Test]
        public void OpcUaPlannerUsesContextBaseUriWhenNoHref()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"uav:id":"ns=2;i=2000","op":"readproperty"}""",
                ops: ["readproperty"]);
            var context = new WotBindingPlanContext(
                baseUri: "opc.tcp://server.example.com:4840");

            WotBindingCompilation result = planner.Compile(form, context);

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries[0].Addressing.Metadata["nodeId"], Is.EqualTo("ns=2;i=2000"));
        }

        /// <summary>
        /// Verifies that OPC UA planner rejects form with no href and no base URI.
        /// </summary>
        [Test]
        public void OpcUaPlannerRejectsFormWithNoHrefAndNoBaseUri()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"uav:id":"ns=2;i=2000","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
        }

        /// <summary>
        /// Verifies that OPC UA planner accepts OPC HTTPS scheme.
        /// </summary>
        [Test]
        public void OpcUaPlannerAcceptsOpcHttpsScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
                                     """{"href":"opc.https://server.example.com:443","uav:id":"ns=2;i=1001","op":"readproperty"}""",
                ops: ["readproperty"]);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
        }

        /// <summary>
        /// Verifies that OPC UA planner accepts OPC wss scheme.
        /// </summary>
        [Test]
        public void OpcUaPlannerAcceptsOpcWssScheme()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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

        /// <summary>
        /// Verifies that OPC UA planner carries the explicit call receiver separately from local placement.
        /// </summary>
        [Test]
        public void OpcUaPlannerCarriesTheExplicitCallReceiverSeparatelyFromLocalPlacement()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakeActionForm(
                /*lang=json,strict*/
                                     """
                {
                  "href": "opc.tcp://source:4840",
                  "uav:id": "nsu=urn:source;s=Run",
                  "uav:callObjectId": "nsu=urn:source;s=Device",
                  "uav:componentOf": "nsu=urn:local;s=Projection",
                  "op": "invokeaction"
                }
                """);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.True);
            Assert.That(result.Entries.Single().Addressing.Metadata.TryGetValue("callObjectId", out string? receiver),
                Is.True);
            Assert.That(receiver, Is.EqualTo("nsu=urn:source;s=Device"));
            Assert.That(result.Entries.Single().Addressing.Target, Is.EqualTo("nsu=urn:source;s=Run"));
        }

        /// <summary>
        /// Verifies that OPC UA planner rejects an invalid explicit receiver instead of using a legacy fallback.
        /// </summary>
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("\"\"")]
        [TestCase("\"not-a-node-id\"")]
        [TestCase("\"ns=1;s=Device\"")]
        [TestCase("\"svr=1;i=1\"")]
        [TestCase("\"i=0\"")]
        public void OpcUaPlannerRejectsAnInvalidExplicitReceiverInsteadOfUsingALegacyFallback(string receiver)
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakeActionForm(
                "{\"href\":\"opc.tcp://source:4840\",\"uav:id\":\"nsu=urn:source;s=Run\"," +
                "\"uav:callObjectId\":" +
                receiver +
                "," +
                "\"uav:componentOf\":\"nsu=urn:source;s=Device\",\"op\":\"invokeaction\"}");

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.InvalidFieldValue), Is.True);
        }

        /// <summary>
        /// Verifies that OPC UA planner rejects local placement arrays as call receiver authority.
        /// </summary>
        [Test]
        public void OpcUaPlannerRejectsLocalPlacementArraysAsCallReceiverAuthority()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakeActionForm(
                /*lang=json,strict*/
                                     """
                {
                  "href": "opc.tcp://source:4840",
                  "uav:id": "nsu=urn:source;s=Run",
                  "uav:componentOf": ["nsu=urn:local;s=Projection"],
                  "op": "invokeaction"
                }
                """);

            WotBindingCompilation result = planner.Compile(form, DefaultContext());

            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.MissingRequiredField), Is.True);
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

        /// <summary>
        /// Verifies that OPC UA planner extracts node ID from query string.
        /// </summary>
        [Test]
        public void OpcUaPlannerExtractsNodeIdFromQueryString()
        {
            var planner = new OpcUaBindingPlanner();
            WotAffordanceForm form = MakePropertyForm(
                /*lang=json,strict*/
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
