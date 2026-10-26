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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotFormExtractor"/>.
    /// </summary>
    [TestFixture]
    public sealed class WotFormExtractorTests
    {
        private static byte[] Utf8(string json)
        {
            return Encoding.UTF8.GetBytes(json);
        }

        [Test]
        public void ExtractReturnsEmptyForNonObject()
        {
            var forms = WotFormExtractor.Extract(Utf8("[]"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractReturnsEmptyForEmptyDocument()
        {
            var forms = WotFormExtractor.Extract(Utf8("{}"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractReturnsEmptyForMalformedJson()
        {
            var forms = WotFormExtractor.Extract(Utf8("{not valid json}"));
            Assert.That(forms.IsEmpty, Is.True);
        }

        [Test]
        public void ExtractPropertyFormWithDefaultOps()
        {
            string td =
                "{\"properties\":{\"temp\":{\"type\":\"number\"," +
                "\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            var form = forms[0];
            Assert.That(form.Kind, Is.EqualTo(WotAffordanceKind.Property));
            Assert.That(form.AffordanceName, Is.EqualTo("temp"));
            Assert.That(form.Href, Is.EqualTo("http://example.com/temp"));
            Assert.That(form.Operations, Does.Contain("readproperty"));
            Assert.That(form.Operations, Does.Contain("writeproperty"));
        }

        [Test]
        public void ExtractReadOnlyPropertyDefaultOpsContainOnlyReadProperty()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"readOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/sensor\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Not.Contain("writeproperty"));
        }

        [Test]
        public void ExtractWriteOnlyPropertyDefaultOpsContainOnlyWriteProperty()
        {
            string td =
                "{\"properties\":{\"actuator\":{\"writeOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/actuator\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Not.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Contain("writeproperty"));
        }

        [Test]
        public void ExtractObservablePropertyDefaultOpsIncludeObserve()
        {
            string td =
                "{\"properties\":{\"power\":{\"observable\":true," +
                "\"forms\":[{\"href\":\"http://example.com/power\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("observeproperty"));
            Assert.That(forms[0].Operations, Does.Contain("unobserveproperty"));
        }

        [Test]
        public void ExtractActionFormWithDefaultOps()
        {
            string td =
                "{\"actions\":{\"reset\":{\"forms\":[{\"href\":\"http://example.com/reset\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Kind, Is.EqualTo(WotAffordanceKind.Action));
            Assert.That(forms[0].Operations, Does.Contain("invokeaction"));
            Assert.That(forms[0].Operations, Has.Length.EqualTo(1));
        }

        [Test]
        public void ExtractEventFormWithDefaultOps()
        {
            string td =
                "{\"events\":{\"alarm\":{\"forms\":[{\"href\":\"http://example.com/alarm\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Kind, Is.EqualTo(WotAffordanceKind.Event));
            Assert.That(forms[0].Operations, Does.Contain("subscribeevent"));
            Assert.That(forms[0].Operations, Does.Contain("unsubscribeevent"));
        }

        [Test]
        public void ExtractFormWithExplicitSingleOpOverride()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"forms\":[{\"href\":\"http://example.com/sensor\"," +
                "\"op\":\"readproperty\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Has.Length.EqualTo(1));
        }

        [Test]
        public void ExtractFormWithExplicitArrayOpOverride()
        {
            string td =
                "{\"properties\":{\"sensor\":{\"forms\":[{\"href\":\"http://example.com/sensor\"," +
                "\"op\":[\"readproperty\",\"observeproperty\"]}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
            Assert.That(forms[0].Operations, Does.Contain("observeproperty"));
        }

        [Test]
        public void ExtractFormLevelSecurityOverridesThingSecurity()
        {
            string td =
                "{\"security\":\"nosec_sc\",\"properties\":{\"sensor\":{\"forms\":[{" +
                "\"href\":\"http://example.com/sensor\",\"security\":\"basic_sc\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("basic_sc"));
            Assert.That(forms[0].SecuritySchemes, Does.Not.Contain("nosec_sc"));
        }

        [Test]
        public void ExtractFormWithoutSecurityFallsBackToThingSecurity()
        {
            string td =
                "{\"security\":\"nosec_sc\",\"properties\":{\"sensor\":{\"forms\":[{" +
                "\"href\":\"http://example.com/sensor\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("nosec_sc"));
        }

        [Test]
        public void ExtractThingLevelSecurityArray()
        {
            string td =
                "{\"security\":[\"auth1_sc\",\"auth2_sc\"],\"properties\":{\"p\":{\"forms\":[{" +
                "\"href\":\"http://example.com/p\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].SecuritySchemes, Does.Contain("auth1_sc"));
            Assert.That(forms[0].SecuritySchemes, Does.Contain("auth2_sc"));
        }

        [Test]
        public void ExtractFormlessAffordanceProducesFormlessDescriptor()
        {
            string td = "{\"properties\":{\"nada\":{\"type\":\"string\"}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(1));
            Assert.That(forms[0].AffordanceName, Is.EqualTo("nada"));
            Assert.That(forms[0].Href, Is.Null);
        }

        [Test]
        public void ExtractMultipleFormsProducesOneEntryPerForm()
        {
            string td =
                "{\"properties\":{\"p\":{\"forms\":[" +
                "{\"href\":\"http://example.com/p\"}," +
                "{\"href\":\"http://backup.com/p\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(2));
        }

        [Test]
        public void ExtractMultipleAffordancesAcrossKinds()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}," +
                "\"actions\":{\"reset\":{\"forms\":[{\"href\":\"http://example.com/reset\"}]}}," +
                "\"events\":{\"alarm\":{\"forms\":[{\"href\":\"http://example.com/alarm\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms, Has.Length.EqualTo(3));
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Property), Is.True);
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Action), Is.True);
            Assert.That(forms.Any(f => f.Kind == WotAffordanceKind.Event), Is.True);
        }

        [Test]
        public void ExtractJsonPointerForFirstForm()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Is.EqualTo("/properties/temp/forms/0"));
        }

        [Test]
        public void ExtractJsonPointerForSecondForm()
        {
            string td =
                "{\"properties\":{\"temp\":{\"forms\":[" +
                "{\"href\":\"http://a.com/t\"},{\"href\":\"http://b.com/t\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Is.EqualTo("/properties/temp/forms/0"));
            Assert.That(forms[1].JsonPointer, Is.EqualTo("/properties/temp/forms/1"));
        }

        [Test]
        public void ExtractEscapesSpecialCharactersInAffordanceName()
        {
            string td =
                "{\"properties\":{\"te~mp/val\":{\"forms\":[{\"href\":\"http://example.com/temp\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].JsonPointer, Does.Contain("te~0mp~1val"));
        }

        [Test]
        public void ExtractFormWithContentType()
        {
            string td =
                "{\"properties\":{\"data\":{\"forms\":[{\"href\":\"http://example.com/data\"," +
                "\"contentType\":\"application/octet-stream\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].ContentType, Is.EqualTo("application/octet-stream"));
        }

        [Test]
        public void ExtractBothReadOnlyAndWriteOnlyYieldsReadProperty()
        {
            string td =
                "{\"properties\":{\"odd\":{\"readOnly\":true,\"writeOnly\":true," +
                "\"forms\":[{\"href\":\"http://example.com/odd\"}]}}}";
            var forms = WotFormExtractor.Extract(Utf8(td));

            Assert.That(forms[0].Operations, Does.Contain("readproperty"));
        }

        /// <summary>
        /// The consumer selects the published 1.0 interpretation explicitly;
        /// the plan request carries that selection whether or not the source
        /// TD includes its supported revision annotation.
        /// </summary>
        [TestCase(null)]
        [TestCase("1.0")]
        public void FromDocumentCarriesExplicitPublishedOneZeroSelection(string? revision)
        {
            var selection = new WotBindingSelectionContext(
                [], [], WotBindingProcessingMode.PublishedOneZero);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "published-device",
                WoTDocumentKindEnum.ThingDescription,
                Utf8(SelectionDocument(revision)),
                selection: selection);
            WotBindingPlanContext context = request.CreateContext(
                WotPayloadCodecRegistry.Default, WotBindingBounds.Default);

            Assert.Multiple(() =>
            {
                Assert.That(request.Selection, Is.SameAs(selection));
                Assert.That(
                    request.Selection.ProcessingMode,
                    Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
                Assert.That(
                    context.ProcessingMode,
                    Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
                Assert.That(request.Forms, Has.Length.EqualTo(1));
                Assert.That(
                    request.Forms[0].Href,
                    Is.EqualTo(
                        "opc.tcp://server.example:4840/UA/Plant/Line" +
                        "?id=nsu=urn%3Atest%3Aidentity;s=Pump"));
            });
        }

        /// <summary>
        /// Verifies that undefined processing modes cannot silently select current behavior.
        /// </summary>
        [TestCase(-1)]
        [TestCase(2)]
        public void UndefinedBindingProcessingModeIsRejected(int value)
        {
            var mode = (WotBindingProcessingMode)value;

            ArgumentOutOfRangeException? selectionError = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WotBindingSelectionContext([], [], mode));
            ArgumentOutOfRangeException? extractionError = Assert.Throws<ArgumentOutOfRangeException>(() =>
                WotFormExtractor.Extract(ByteString.From(Utf8(SelectionDocument(null))), mode));

            Assert.That(selectionError!.ParamName, Is.EqualTo("processingMode"));
            Assert.That(extractionError!.ParamName, Is.EqualTo("processingMode"));
        }

        /// <summary>
        /// The UA binding vocabulary, OPC UA URI scheme and a revision
        /// annotation do not implicitly select a processing mode; an
        /// unconfigured consumer remains on the current rules.
        /// </summary>
        [TestCase(null)]
        [TestCase("1.0")]
        public void FromDocumentDefaultsToCurrentDespiteBindingVocabularyAndRevision(string? revision)
        {
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "default-device",
                WoTDocumentKindEnum.ThingDescription,
                Utf8(SelectionDocument(revision)));
            WotBindingPlanContext context = request.CreateContext(
                WotPayloadCodecRegistry.Default, WotBindingBounds.Default);

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotBindingSelectionContext.Empty.ProcessingMode,
                    Is.EqualTo(WotBindingProcessingMode.Current));
                Assert.That(
                    request.Selection.ProcessingMode,
                    Is.EqualTo(WotBindingProcessingMode.Current));
                Assert.That(
                    context.ProcessingMode,
                    Is.EqualTo(WotBindingProcessingMode.Current));
                Assert.That(request.Forms, Has.Length.EqualTo(1));
                Assert.That(
                    request.Forms[0].Href,
                    Does.StartWith("opc.tcp://server.example:4840/UA/Plant/Line"));
            });
        }

        /// <summary>
        /// Verifies standalone extraction captures numeric browse prefixes only
        /// when the caller explicitly selects the published interpretation.
        /// </summary>
        [Test]
        public void ExtractUsesExplicitPublishedModeForNumericBrowsePrefixes()
        {
            const string document = """
                {
                  "@context": {
                    "1": "urn:path:source",
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/"
                  },
                  "properties": {
                    "value": {
                      "type": "integer",
                      "forms": [{
                        "href": "opc.tcp://localhost:4840/UA/Factory",
                        "uav:browsePath": "/Objects/1:Device/1:Value",
                        "op": "readproperty"
                      }]
                    }
                  }
                }
                """;
            var selection = new WotBindingSelectionContext(
                [], [], WotBindingProcessingMode.PublishedOneZero);
            var request = new WotBindingPlanRequest(
                "/things/published-extractor",
                WoTDocumentKindEnum.ThingDescription,
                WotFormExtractor.Extract(
                    ByteString.From(Utf8(document)),
                    WotBindingProcessingMode.PublishedOneZero),
                selection: selection);
            WotBindingPlan plan = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]).Prepare(request);

            Assert.That(plan.CompiledForms, Has.Length.EqualTo(1), string.Join("; ", plan.Diagnostics));
            Assert.That(
                request.Selection.ProcessingMode,
                Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
        }

        /// <summary>
        /// Standard WoT property operation defaults and access flags remain
        /// effective when the caller explicitly selects published Binding 1.0.
        /// </summary>
        [Test]
        public void PublishedOneZeroSelectionPreservesStandardPropertyDefaultsAndAccessFlags()
        {
            const string document = /*lang=json,strict*/ """
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "properties": {
                    "ordinary": {
                      "type": "integer",
                      "forms": [{ "href": "opc.tcp://server.example:4840" }]
                    },
                    "readOnly": {
                      "type": "integer",
                      "readOnly": true,
                      "forms": [{ "href": "opc.tcp://server.example:4840" }]
                    },
                    "writeOnly": {
                      "type": "integer",
                      "writeOnly": true,
                      "forms": [{ "href": "opc.tcp://server.example:4840" }]
                    },
                    "observable": {
                      "type": "integer",
                      "observable": true,
                      "forms": [{ "href": "opc.tcp://server.example:4840" }]
                    }
                  }
                }
                """;
            var selection = new WotBindingSelectionContext(
                [], [], WotBindingProcessingMode.PublishedOneZero);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                "published-property-defaults",
                WoTDocumentKindEnum.ThingDescription,
                Utf8(document),
                selection: selection);
            WotBindingPlanContext context = request.CreateContext(
                WotPayloadCodecRegistry.Default, WotBindingBounds.Default);
            var forms = request.Forms;

            Assert.Multiple(() =>
            {
                Assert.That(context.ProcessingMode, Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
                Assert.That(forms.Single(form => form.AffordanceName == "ordinary").Operations,
                    Is.EquivalentTo(["readproperty", "writeproperty"]));
                Assert.That(forms.Single(form => form.AffordanceName == "readOnly").Operations,
                    Is.EquivalentTo(["readproperty"]));
                Assert.That(forms.Single(form => form.AffordanceName == "writeOnly").Operations,
                    Is.EquivalentTo(["writeproperty"]));
                Assert.That(forms.Single(form => form.AffordanceName == "observable").Operations,
                    Is.EquivalentTo(["readproperty", "writeproperty", "observeproperty", "unobserveproperty"]));
            });
        }

        /// <summary>
        /// Verifies that a later planning context cannot supply missing original owner namespace authority.
        /// </summary>
        [Test]
        public void ExtractedOwnerCannotAcquireMissingNamespaceAuthorityFromPlanningContext()
        {
            const string document = """
                {
                  "@context": {
                    "uav": "http://opcfoundation.org/UA/WoT-Binding/"
                  },
                  "actions": {
                    "run": {
                      "uav:componentOf": ["ns=2;s=Owner"],
                      "forms": [{
                        "href": "opc.tcp://server.example:4840",
                        "uav:id": "nsu=urn:original:method;s=Run"
                      }]
                    }
                  }
                }
                """;
            var request = new WotBindingPlanRequest(
                "/things/original-owner",
                WoTDocumentKindEnum.ThingDescription,
                WotFormExtractor.Extract(
                    ByteString.From(Utf8(document)),
                    WotBindingProcessingMode.PublishedOneZero),
                selection: new WotBindingSelectionContext([], [], WotBindingProcessingMode.PublishedOneZero),
                namespacePrefixes: ImmutableDictionary<string, string>.Empty.Add("2", "urn:unrelated:owner"));

            WotBindingPlan plan = new WotProtocolBinderRegistry([new OpcUaBindingPlanner()]).Prepare(request);

            Assert.That(plan.FullySupported, Is.False);
            Assert.That(plan.CompiledForms, Is.Empty);
            Assert.That(plan.Diagnostics.Any(diagnostic => diagnostic.IsError &&
                diagnostic.Code == WotBindingDiagnosticCode.InvalidFieldValue &&
                diagnostic.Message.Contains("uav:componentOf", StringComparison.Ordinal)), Is.True);
        }

        private static string SelectionDocument(string? revision)
        {
            string bindingVersion = revision is null
                ? string.Empty
                : ",\"uav:bindingVersion\":\"" + revision + "\"";
            return
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"title\":\"Published consumer selection\"" +
                bindingVersion +
                ",\"properties\":{\"temperature\":{\"type\":\"number\",\"forms\":[{" +
                "\"href\":\"opc.tcp://server.example:4840/UA/Plant/Line" +
                "?id=nsu=urn%3Atest%3Aidentity;s=Pump\"}]}}}";
        }
    }
}
