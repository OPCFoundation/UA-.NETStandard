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

using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Validates the model- and platform-vocabulary terms of WoT Binding
    /// Section 6 (composition, containment, naming, units and scaling,
    /// semantics, inheritance) and the anchored browse-path term of
    /// Section 5.1.4, together with their round-trip preservation.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotModelVocabularyTests
    {
        [Test]
        public void ValidCompositionTermsProduceNoModelVocabularyDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:isComposite\":true," +
                "\"uav:includeInherited\":true," +
                "\"uav:additionalProperties\":false");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(HasModelVocabularyError(result), Is.False);
        }

        [Test]
        public void NonBooleanIsCompositeReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:isComposite\":\"yes\"");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True,
                "A non-boolean uav:isComposite should be reported.");
        }

        [Test]
        public void NonBooleanIncludeInheritedReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:includeInherited\":1");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True);
        }

        [Test]
        public void NonBooleanAdditionalPropertiesReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:additionalProperties\":\"open\"");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True);
        }

        [Test]
        public void ValidNameNamespaceAndSemanticIdProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:nameNamespace\":\"http://example.com/demo/pump\"," +
                "\"uav:semanticId\":\"http://example.com/ontology/Pump\"");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonAbsoluteIri),
                Is.False);
        }

        [Test]
        public void UrnSemanticIdProducesNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:semanticId\":\"urn:example:pump:model\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonAbsoluteIri),
                Is.False);
        }

        [Test]
        public void RelativeSemanticIdReportsNonAbsoluteIri()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:semanticId\":\"/ontology/Pump\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonAbsoluteIri),
                Is.True);
        }

        [Test]
        public void NonStringSemanticIdReportsNonAbsoluteIri()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:semanticId\":42");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonAbsoluteIri),
                Is.True);
        }

        [Test]
        public void ValidContainsMatchingRefNameProducesNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:isComposite\":true,\"uav:contains\":[\"Impeller\"]," +
                "\"links\":[{\"rel\":\"ua:HasComponent\",\"href\":\"urn:x\"," +
                "\"uav:refName\":\"Impeller\",\"uav:refId\":\"i=47\"}]");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.False);
        }

        [Test]
        public void ContainsEntryWithoutMatchingRefNameReportsInvalidContainment()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:isComposite\":true,\"uav:contains\":[\"Missing\"]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.True,
                "A uav:contains entry with no matching link uav:refName should be reported.");
        }

        [Test]
        public void ContainsNonArrayReportsInvalidContainment()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:contains\":\"Impeller\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.True);
        }

        [Test]
        public void ValidContainedInProducesNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:containedIn\":\"AssetType\"");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.False);
        }

        [Test]
        public void ContainedInNamingSelfReportsInvalidContainment()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:containedIn\":\"PumpType\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.True,
                "A uav:containedIn naming the type itself is a cycle and should be reported.");
        }

        [Test]
        public void NonStringContainedInReportsInvalidContainment()
        {
            WotConversionResult<UANodeSet> result = Convert("\"uav:containedIn\":[]");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidContainment),
                Is.True);
        }

        [Test]
        public void ValidScalingTermsProduceNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"uav:unitProperty\":\"/properties/flowUnit\"," +
                "\"uav:scaleFactor\":0.1,\"uav:decimalPlaces\":2}," +
                "\"flowUnit\":{\"type\":\"string\"}}");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(HasModelVocabularyError(result), Is.False);
        }

        [Test]
        public void NonNumberScaleFactorReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:scaleFactor\":\"fast\"}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True);
        }

        [Test]
        public void ZeroScaleFactorReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:scaleFactor\":0}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True,
                "A zero uav:scaleFactor is not invertible and should be reported.");
        }

        [Test]
        public void FractionalDecimalPlacesReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:decimalPlaces\":2.5}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True);
        }

        [Test]
        public void IntegerValuedFloatDecimalPlacesReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:decimalPlaces\":2.0}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True,
                "A uav:decimalPlaces expressed as 2.0 is not an integer literal.");
        }

        [Test]
        public void NegativeDecimalPlacesReportsInvalidModelVocabularyValue()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:decimalPlaces\":-1}}");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.InvalidModelVocabularyValue),
                Is.True);
        }

        [Test]
        public void UnitPropertyNotAPointerReportsInvalidUnitPointer()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:unitProperty\":\"unit\"}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True,
                "A uav:unitProperty that is not a JSON Pointer should be reported.");
        }

        [Test]
        public void UnitPropertyResolvingToNonStringReportsInvalidUnitPointer()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:unitProperty\":\"/properties/flow\"}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True,
                "A uav:unitProperty naming its own affordance should be reported.");
        }

        [Test]
        public void UnitPropertyPointingIntoTheAffordanceReportsInvalidUnitPointer()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"uav:unitProperty\":\"/properties/flow/unit\"}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True,
                "The unit is a Property Node of its own, so a pointer at a member " +
                "inside the annotated affordance names nothing that exists.");
        }

        [Test]
        public void UnitPropertyNamingANonStringSiblingReportsInvalidUnitPointer()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:unitProperty\":\"/properties/flowUnit\"}," +
                "\"flowUnit\":{\"type\":\"number\"}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True,
                "The affordance a unit pointer names shall be string-valued.");
        }

        [Test]
        public void UnitPropertyThatDoesNotResolveReportsInvalidUnitPointer()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:unitProperty\":\"/properties/missing\"}}");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidUnitPointer),
                Is.True);
        }

        [Test]
        public void OpaqueMetadataAndConfigurationAreNeverRejected()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:metadata\":{\"revision\":3,\"maintainer\":\"Modeling WG\"}," +
                "\"properties\":{\"flow\":{\"type\":\"number\"," +
                "\"uav:propertyConfiguration\":{\"scan\":100}}}," +
                "\"actions\":{\"start\":{\"uav:actionConfiguration\":[1,2,3]}}," +
                "\"events\":{\"trip\":{\"uav:eventConfiguration\":\"opaque\"}}");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(HasModelVocabularyError(result), Is.False,
                "Opaque Section 6.7 terms shall never cause a document to be rejected.");
        }

        [Test]
        public void PortableBrowsePathAnchorProducesNoDiagnostic()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:browsePathAnchor\":\"nsu=urn:test:pump;s=Pump\"");

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code is WotDiagnosticCode.NonPortableIdentity or
                    WotDiagnosticCode.ValidationError),
                Is.False);
        }

        [Test]
        public void SessionLocalBrowsePathAnchorReportsNonPortableIdentity()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:browsePathAnchor\":\"ns=1;s=Pump\"");

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NonPortableIdentity),
                Is.True,
                "A session-local ns=<index> uav:browsePathAnchor should be reported.");
        }

        [Test]
        public void NonNodeIdBrowsePathAnchorReportsValidationError()
        {
            WotConversionResult<UANodeSet> result = Convert(
                "\"uav:browsePathAnchor\":\"not-a-node-id\"");

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ValidationError),
                Is.True,
                "A uav:browsePathAnchor that is not an ExpandedNodeId should be reported.");
        }

        [Test]
        public void RootModelVocabularyTermsSurviveWotToNodeSetToWot()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:isComposite\":true," +
                "\"uav:includeInherited\":true," +
                "\"uav:additionalProperties\":false," +
                "\"uav:nameNamespace\":\"http://example.com/demo/pump\"," +
                "\"uav:semanticId\":\"http://example.com/ontology/Pump\"," +
                "\"uav:containedIn\":\"AssetType\"," +
                "\"uav:metadata\":{\"revision\":3}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement root = restored.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("uav:isComposite").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("uav:includeInherited").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("uav:additionalProperties").GetBoolean(), Is.False);
                Assert.That(
                    root.GetProperty("uav:nameNamespace").GetString(),
                    Is.EqualTo("http://example.com/demo/pump"));
                Assert.That(
                    root.GetProperty("uav:semanticId").GetString(),
                    Is.EqualTo("http://example.com/ontology/Pump"));
                Assert.That(
                    root.GetProperty("uav:containedIn").GetString(),
                    Is.EqualTo("AssetType"));
                Assert.That(
                    root.GetProperty("uav:metadata").GetProperty("revision").GetInt32(),
                    Is.EqualTo(3));
            });
        }

        [Test]
        public void ContainsArraySurvivesWotToNodeSetToWot()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:isComposite\":true,\"uav:contains\":[\"Impeller\"]," +
                "\"links\":[{\"rel\":\"ua:HasComponent\",\"href\":\"urn:x\"," +
                "\"uav:refName\":\"Impeller\",\"uav:refId\":\"i=47\"}]");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            JsonElement contains = restored.RootElement.GetProperty("uav:contains");
            Assert.That(contains.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(contains[0].GetString(), Is.EqualTo("Impeller"));
        }

        [Test]
        public void AffordanceScalingTermsSurviveWotToNodeSetToWot()
        {
            using WotDocument original = ParseThingModel(
                "\"properties\":{\"flow\":{\"type\":\"number\",\"unit\":\"rpm\"," +
                "\"uav:unitProperty\":\"/properties/flowUnit\"," +
                "\"uav:scaleFactor\":0.1,\"uav:decimalPlaces\":2," +
                "\"uav:semanticId\":\"http://example.com/ontology/Speed\"}," +
                "\"flowUnit\":{\"type\":\"string\"}}");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(restored.Properties.ContainsKey("flow"), Is.True);
            JsonElement flow = restored.Properties["flow"];
            Assert.Multiple(() =>
            {
                Assert.That(flow.GetProperty("uav:scaleFactor").GetDouble(), Is.EqualTo(0.1));
                Assert.That(flow.GetProperty("uav:decimalPlaces").GetInt32(), Is.EqualTo(2));
                Assert.That(
                    flow.GetProperty("uav:unitProperty").GetString(),
                    Is.EqualTo("/properties/flowUnit"));
                Assert.That(flow.GetProperty("unit").GetString(), Is.EqualTo("rpm"));
                Assert.That(
                    flow.GetProperty("uav:semanticId").GetString(),
                    Is.EqualTo("http://example.com/ontology/Speed"));
            });
        }

        [Test]
        public void BrowsePathAnchorSurvivesWotToNodeSetToWot()
        {
            using WotDocument original = ParseThingModel(
                "\"uav:browsePathAnchor\":\"nsu=urn:test:pump;s=Pump\"");

            UANodeSet nodeSet = WotNodeSetConverter.ToNodeSet(original);
            using WotDocument restored = WotNodeSetConverter.FromNodeSet(nodeSet);

            Assert.That(
                restored.RootElement.GetProperty("uav:browsePathAnchor").GetString(),
                Is.EqualTo("nsu=urn:test:pump;s=Pump"));
        }

        private static WotConversionResult<UANodeSet> Convert(string members)
        {
            using WotDocument document = ParseThingModel(members);
            return WotNodeSetConverter.ToNodeSetResult(document);
        }

        private static WotDocument ParseThingModel(string members)
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"urn:test:pump\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"Pump\",\"uav:browseName\":\"pump:PumpType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=1001\"," +
                members +
                "}");
            return WotDocument.Parse(json);
        }

        private static bool HasModelVocabularyError(WotConversionResult<UANodeSet> result)
        {
            return result.Diagnostics.Any(d =>
                d.Code is WotDiagnosticCode.InvalidModelVocabularyValue
                    or WotDiagnosticCode.NonAbsoluteIri
                    or WotDiagnosticCode.InvalidUnitPointer
                    or WotDiagnosticCode.InvalidContainment);
        }
    }
}
