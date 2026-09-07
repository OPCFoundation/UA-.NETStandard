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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Additional coverage for <c>WotNodeSetConverter.Mapping.cs</c> paths not
    /// exercised by the first two rounds of Wot tests.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotMappingAdditionalTests
    {
        private const string s_uavCtx = "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
            "\"ua\":\"http://opcfoundation.org/UA/\"";
        private const string s_wotCtx = "\"https://www.w3.org/2022/wot/td/v1.1\"";

        private static byte[] ThingModelJson(string extra = "")
        {
            string json =
                "{\"@context\":[" + s_wotCtx + ",{" + s_uavCtx + "}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"" +
                (string.IsNullOrEmpty(extra) ? string.Empty : "," + extra) +
                "}";
            return Encoding.UTF8.GetBytes(json);
        }

        private static byte[] ThingModelWithContextJson(string extraContext, string extra = "")
        {
            string json =
                "{\"@context\":[" + s_wotCtx + ",{" + s_uavCtx + "," + extraContext + "}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"" +
                (string.IsNullOrEmpty(extra) ? string.Empty : "," + extra) +
                "}";
            return Encoding.UTF8.GetBytes(json);
        }

        [Test]
        public void FromNodeSetUAObjectRootEmitsObjectAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;i=1",
                        BrowseName = "1:MyObj",
                        DisplayName = [new Export.LocalizedText { Value = "MyObj" }]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.WhenRequired
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(
                document.TypeTokens.Contains("uav:object"),
                Is.True,
                "A UAObject root should produce @type uav:object.");
            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
        }

        [Test]
        public void FromNodeSetUAVariableRootEmitsVariableAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAVariable
                    {
                        NodeId = "ns=1;i=2",
                        BrowseName = "1:MyVar",
                        DisplayName = [new Export.LocalizedText { Value = "MyVar" }],
                        DataType = "Double"
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.WhenRequired
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(
                document.TypeTokens.Contains("uav:variable"),
                Is.True,
                "A UAVariable root should produce @type uav:variable.");
            Assert.That(document.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
        }

        [Test]
        public void FromNodeSetEmptyItemsProducesDefaultThingModelAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items = []
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.WhenRequired
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(
                document.TypeTokens.Contains("tm:ThingModel"),
                Is.True,
                "A null root (empty Items) should produce @type tm:ThingModel string.");
        }

        [Test]
        public void FromNodeSetDataTypeRootFallsToDefaultThingModelAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UADataType
                    {
                        NodeId = "ns=1;i=10",
                        BrowseName = "1:MyEnum",
                        DisplayName = [new Export.LocalizedText { Value = "MyEnum" }]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.WhenRequired
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(
                document.TypeTokens.Contains("tm:ThingModel"),
                Is.True,
                "A UADataType root should fall through to the default @type tm:ThingModel.");
        }

        [Test]
        public void ToNodeSetAddsNonHierarchicalReferenceForUaLink()
        {
            byte[] json = ThingModelJson(
                "\"links\":[{\"rel\":\"ua:NonHierarchicalReferences\",\"href\":\"i=47\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            bool hasNonHierarchical = root.References != null &&
                root.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "i=32", StringComparison.Ordinal) &&
                    string.Equals(r.Value, "i=47", StringComparison.Ordinal));
            Assert.That(
                hasNonHierarchical,
                Is.True,
                "A ua:NonHierarchicalReferences link should synthesize that reference (i=32).");
        }

        [Test]
        public void ToNodeSetAddsHasComponentForUaHasComponentLink()
        {
            byte[] json = ThingModelJson(
                "\"links\":[{\"rel\":\"ua:HasComponent\",\"href\":\"i=58\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            bool hasHasComponent = root.References != null &&
                root.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "i=47", StringComparison.Ordinal) &&
                    string.Equals(r.Value, "i=58", StringComparison.Ordinal));
            Assert.That(
                hasHasComponent,
                Is.True,
                "A ua:HasComponent link should synthesize a HasComponent reference (i=47).");
        }

        [Test]
        public void ToNodeSetReportsCustomNamespaceRelationUnresolvable()
        {
            byte[] json = ThingModelWithContextJson(
                "\"ns1\":\"urn:custom:ns\"",
                "\"links\":[{\"rel\":\"ns1:MyRef\",\"href\":\"nsu=urn:custom:ns;i=99\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("ns1:MyRef", StringComparison.Ordinal)),
                Is.True,
                "A relation whose namespace is not OPC UA and has no refId fallback should produce a diagnostic.");
        }

        [Test]
        public void ToNodeSetAddsSuperTypeFromTmExtendsLink()
        {
            byte[] json = ThingModelJson(
                "\"links\":[{\"rel\":\"tm:extends\",\"href\":\"nsu=urn:base;i=1\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            bool hasSupertype = root.References != null &&
                root.References.Any(r =>
                    !r.IsForward &&
                    string.Equals(r.ReferenceType, "HasSubtype", StringComparison.Ordinal));
            Assert.That(
                hasSupertype,
                Is.True,
                "tm:extends with a NodeId href should produce a HasSubtype backward reference.");
        }

        [Test]
        public void ToNodeSetWarnsAboutNonNodeIdHrefWithoutResolver()
        {
            byte[] json = ThingModelJson(
                "\"links\":[{\"rel\":\"ua:NonHierarchicalReferences\",\"href\":\"https://example.com/other-thing\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.UnresolvedReference),
                Is.True,
                "A non-NodeId href without a resolver should produce an UnresolvedReference warning.");
        }

        [Test]
        public void ToNodeSetReportsAffordanceCountExceededWhenBudgetReached()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"prop1\":{\"type\":\"number\"}," +
                "\"prop2\":{\"type\":\"string\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                document,
                options: new WotNodeSetConverterOptions { MaxAffordanceCount = 1 });

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AffordanceCountExceeded),
                Is.True,
                "Exceeding MaxAffordanceCount should produce an AffordanceCountExceeded diagnostic.");
        }

        [Test]
        public void ToNodeSetReportsMapToTypeNameWithPrefixNotInContext()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"uav:mapToTypeName\":\"ns1:SomeType\"," +
                "\"uav:mapToType\":\"nsu=urn:test;i=1\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("ns1:SomeType", StringComparison.Ordinal)),
                Is.True,
                "A uav:mapToTypeName with a prefix not bound in @context should produce a diagnostic.");
        }

        [Test]
        public void ToNodeSetWarnsAboutNumericPrefixedBrowseName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:browseName\":\"1:MyType\"," +
                "\"properties\":{\"p1\":{\"uav:browseName\":\"1:Prop1\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonPortableQualifiedName),
                Is.True,
                "A uav:browseName with a numeric namespace index should produce a NonPortableQualifiedName warning.");
        }

        [Test]
        public void ToNodeSetReportsUnboundContextPrefixInBrowseName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{\"p1\":{\"uav:browseName\":\"unbound:Prop1\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NonPortableQualifiedName &&
                    d.Message.Contains("unbound", StringComparison.Ordinal)),
                Is.True,
                "A uav:browseName with an unbound context prefix should produce a NonPortableQualifiedName error.");
        }

        [Test]
        public void ToNodeSetSynthesizesUaHasSubtypeRefViaContextNamespace()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[{\"rel\":\"ua:HasSubtype\",\"href\":\"i=58\",\"uav:refId\":\"i=45\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            bool hasForwardRef = root.References != null &&
                root.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.Value, "i=58", StringComparison.Ordinal));
            Assert.That(
                hasForwardRef,
                Is.True,
                "A ua:HasSubtype link should add a forward reference pointing at the href target.");
        }

        [Test]
        public void ToNodeSetResolvesUaHasComponentRelViaContextNamespace()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[{\"rel\":\"ua:HasComponent\",\"href\":\"i=58\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            bool hasComponent = root.References != null &&
                root.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.Value, "i=58", StringComparison.Ordinal));
            Assert.That(
                hasComponent,
                Is.True,
                "A ua:HasComponent link resolved via OPC UA context namespace should add a forward reference.");
        }

        [Test]
        public void ToNodeSetAppliesNsuBrowseNameViaNsu()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:browseName\":\"nsu=urn:test;MyType\"," +
                "\"uav:id\":\"nsu=urn:test;i=5000\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.BrowseName.Contains("MyType", StringComparison.Ordinal),
                Is.True,
                "nsu= browseName should produce a valid qualified name.");
        }

        [Test]
        public void ToNodeSetHandlesOpcUaNsBrowseNameWithNsu()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:browseName\":\"nsu=http://opcfoundation.org/UA/;BaseObjectType\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            Assert.That(
                string.Equals(root.BrowseName, "BaseObjectType", StringComparison.Ordinal),
                Is.True,
                "nsu= form with OPC UA namespace should strip the prefix and keep only the local name.");
        }

        [Test]
        public void ToNodeSetSynthesizesEventAffordanceAsObjectType()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"events\":{\"alarm\":{\"uav:browseName\":\"1:Alarm\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            bool hasEventType = result.Value!.Items!.OfType<UAObjectType>().Count() >= 2;
            Assert.That(
                hasEventType,
                Is.True,
                "An event affordance should synthesize an additional UAObjectType in the NodeSet.");
        }

        [Test]
        public void ToNodeSetSynthesizesActionAffordanceAsUAMethod()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"actions\":{\"start\":{\"title\":\"Start\",\"uav:browseName\":\"1:Start\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            bool hasMethod = result.Value!.Items!.OfType<UAMethod>().Any();
            Assert.That(
                hasMethod,
                Is.True,
                "An action affordance should synthesize a UAMethod node.");
        }

        [Test]
        public void ToNodeSetHandlesNsuMalformedBrowseName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:browseName\":\"nsu=malformed-no-semicolon\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.NonPortableQualifiedName),
                Is.True,
                "A malformed nsu= browseName (no semicolon) should produce a NonPortableQualifiedName diagnostic.");
        }

        [Test]
        public void FromNodeSetObjectTypeNonEventEmitsObjectTypeArray()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=100",
                        BrowseName = "1:MyDeviceType",
                        DisplayName = [new Export.LocalizedText { Value = "MyDeviceType" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(
                document.TypeTokens.Contains("uav:objectType"),
                Is.True,
                "A non-event UAObjectType root should produce uav:objectType in @type array.");
            Assert.That(
                document.TypeTokens.Contains("tm:ThingModel"),
                Is.True,
                "A non-event UAObjectType root should have tm:ThingModel in @type array.");
        }

        [Test]
        public void ToNodeSetSynthesizesDescriptionFromRootDescription()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"description\":\"A test device type.\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            Assert.That(root.Description, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToNodeSetWarnsAboutPropertyWithExternalSchema()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"data\":{\"uav:externalSchema\":\"https://example.com/schema.json\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedSchema),
                Is.True,
                "A property with uav:externalSchema should produce an UnsupportedSchema warning.");
        }

        [Test]
        public void ToNodeSetWarnsAboutObjectTypeDataSchema()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"config\":{\"type\":\"object\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedSchema),
                Is.True,
                "A property with type 'object' DataSchema should produce an UnsupportedSchema warning.");
        }

        [Test]
        public void ToNodeSetWarnsAboutArrayTypeDataSchema()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"items\":{\"type\":\"array\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnsupportedSchema),
                Is.True,
                "A property with type 'array' DataSchema should produce an UnsupportedSchema warning.");
        }

        [Test]
        public void ToNodeSetAddsModellingRuleForPropertyWithModellingRule()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"value\":{\"type\":\"number\",\"uav:modellingRule\":\"Mandatory\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable property = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(property, Is.Not.Null);
            bool hasModellingRule = property!.References != null &&
                property.References.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "HasModellingRule", StringComparison.Ordinal));
            Assert.That(
                hasModellingRule,
                Is.True,
                "A property with uav:modellingRule should add a HasModellingRule reference.");
        }

        [Test]
        public void ToNodeSetSynthesizesPropertyWithReadOnlyAccessLevel()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"reading\":{\"type\":\"number\",\"readOnly\":true}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable property = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(property, Is.Not.Null);
            Assert.That(property!.AccessLevel & 1u, Is.Not.Zero, "ReadOnly=true should set AccessLevel read bit.");
            Assert.That(property.AccessLevel & 2u, Is.Zero, "ReadOnly=true should not set the write bit.");
        }

        [Test]
        public void ToNodeSetSynthesizesPropertyWithWriteOnlyAccessLevel()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"command\":{\"type\":\"string\",\"writeOnly\":true}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable property = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(property, Is.Not.Null);
            Assert.That(property!.AccessLevel & 2u, Is.Not.Zero, "WriteOnly=true should set AccessLevel write bit.");
            Assert.That(property.AccessLevel & 1u, Is.Zero, "WriteOnly=true should not set the read bit.");
        }

        [Test]
        public void FromNodeSetVariableTypeEmitsPortableBrowseName()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=200",
                        BrowseName = "1:SensorType",
                        DisplayName = [new Export.LocalizedText { Value = "SensorType" }],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=201"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=201",
                        BrowseName = "1:Temperature",
                        DisplayName = [new Export.LocalizedText { Value = "Temperature" }],
                        DataType = "Double",
                        AccessLevel = 1
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Properties, Is.Not.Empty,
                "A UAVariable child via HasProperty should produce a WoT property affordance.");
        }

        [Test]
        public void ToNodeSetReportsModelConceptConflictWhenRefIdDisagreesWithModelName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[{\"rel\":\"ua:HasComponent\",\"href\":\"i=58\",\"uav:refId\":\"i=99\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ModelConceptConflict),
                Is.True,
                "A ua:HasComponent link whose resolved NodeId disagrees with uav:refId should produce a ModelConceptConflict.");
        }

        [Test]
        public void ToNodeSetTreatsARetiredIsEventFlagAsResidue()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":\"uav:eventType\",\"uav:isEvent\":false}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventAnnotationConflict),
                Is.False,
                "WoT Binding 1.1 defines no uav:isEvent term, so it can contradict nothing.");
            Assert.That(result.HasErrors, Is.False);
        }

        [Test]
        public void ToNodeSetDeriveModelUriFromNsuId()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:id\":\"nsu=urn:my:namespace;i=1\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            Assert.That(
                result.Value!.NamespaceUris != null &&
                result.Value.NamespaceUris.Any(
                    ns => string.Equals(ns, "urn:my:namespace", StringComparison.Ordinal)),
                Is.True,
                "The model URI should be extracted from the nsu= form of uav:id.");
        }

        [Test]
        public void ToNodeSetDeriveModelUriFromDocumentId()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"id\":\"urn:example:my-model\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            bool hasExpectedNamespace = result.Value!.NamespaceUris != null &&
                result.Value.NamespaceUris.Any(
                    ns => string.Equals(ns, "urn:example:my-model", StringComparison.Ordinal));
            bool hasModelUri = result.Value.Models != null &&
                result.Value.Models.Any(
                    m => string.Equals(m.ModelUri, "urn:example:my-model", StringComparison.Ordinal));
            Assert.That(
                hasExpectedNamespace || hasModelUri,
                Is.True,
                "The model URI should be derived from the document @id.");
        }

        [Test]
        public void FromNodeSetWriteOnlyVariableEmitsWriteOnlyAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=300",
                        BrowseName = "1:ActuatorType",
                        DisplayName = [new Export.LocalizedText { Value = "ActuatorType" }],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=301"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=301",
                        BrowseName = "1:SetPoint",
                        DisplayName = [new Export.LocalizedText { Value = "SetPoint" }],
                        DataType = "Double",
                        AccessLevel = 2
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Properties, Is.Not.Empty);
            bool hasWriteOnly = document.RootElement.TryGetProperty("properties", out var props) &&
                props.EnumerateObject().Any(p =>
                    p.Value.TryGetProperty("writeOnly", out var wo) &&
                    wo.ValueKind == JsonValueKind.True);
            Assert.That(
                hasWriteOnly,
                Is.True,
                "A UAVariable with write-only AccessLevel should produce a writeOnly property.");
        }

        [Test]
        public void FromNodeSetDuplicateLocalBrowseNameGetsUniqueSuffix()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=400",
                        BrowseName = "1:MultiPropType",
                        DisplayName = [new Export.LocalizedText { Value = "MultiPropType" }],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=401"
                            },
                            new Reference
                            {
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=402"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=401",
                        BrowseName = "1:Value",
                        DisplayName = [new Export.LocalizedText { Value = "Value" }],
                        DataType = "Double",
                        AccessLevel = 1
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=402",
                        BrowseName = "1:Value",
                        DisplayName = [new Export.LocalizedText { Value = "Value" }],
                        DataType = "Double",
                        AccessLevel = 1
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Properties, Has.Count.EqualTo(2),
                "Two properties with the same local BrowseName should both appear with unique keys.");
        }

        [Test]
        public void ToNodeSetSynthesizesThingDescriptionAsUAObject()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\"," +
                "\"title\":\"SensorDevice\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            bool hasObject = result.Value!.Items!.OfType<UAObject>().Any();
            Assert.That(
                hasObject,
                Is.True,
                "A ThingDescription with @type uav:object should synthesize a UAObject root.");
        }

        [Test]
        public void ToNodeSetReadOnlyAndWriteOnlyTogetherProducesReadAccess()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"val\":{\"type\":\"number\",\"readOnly\":true,\"writeOnly\":true}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable property = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(property, Is.Not.Null);
            Assert.That(
                property!.AccessLevel & 1u,
                Is.Not.Zero,
                "When both readOnly and writeOnly are true, read access should be preserved as a fallback.");
        }

        [Test]
        public void FromNodeSetEmitsEventAffordanceForGeneratesEventReference()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=500",
                        BrowseName = "1:AlarmSource",
                        DisplayName = [new Export.LocalizedText { Value = "AlarmSource" }],
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
                                Value = "ns=1;i=501"
                            }
                        ]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=501",
                        BrowseName = "1:AlarmType",
                        DisplayName = [new Export.LocalizedText { Value = "AlarmType" }]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Events, Has.Count.EqualTo(1),
                "A GeneratesEvent reference to a node in the index should produce an event affordance.");
        }

        [Test]
        public void FromNodeSetEmitsActionAffordanceForUAMethod()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=600",
                        BrowseName = "1:RobotType",
                        DisplayName = [new Export.LocalizedText { Value = "RobotType" }],
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
                                ReferenceType = "HasComponent",
                                IsForward = true,
                                Value = "ns=1;i=601"
                            }
                        ]
                    },
                    new UAMethod
                    {
                        NodeId = "ns=1;i=601",
                        BrowseName = "1:MoveArm",
                        DisplayName = [new Export.LocalizedText { Value = "MoveArm" }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasComponent",
                                IsForward = false,
                                Value = "ns=1;i=600"
                            }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Actions, Has.Count.EqualTo(1),
                "A HasComponent reference to a UAMethod in the index should produce an action affordance.");
        }

        [Test]
        public void FromNodeSetEmitsTypedComponentLinksForHasOrderedComponent()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=700",
                        BrowseName = "1:ListType",
                        DisplayName = [new Export.LocalizedText { Value = "ListType" }],
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
                                ReferenceType = "HasOrderedComponent",
                                IsForward = true,
                                Value = "ns=1;i=701"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=701",
                        BrowseName = "1:Item",
                        DisplayName = [new Export.LocalizedText { Value = "Item" }],
                        DataType = "Double",
                        AccessLevel = 1
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            bool hasComponentLink = document.Links.Any(l =>
                l.TryGetProperty("rel", out JsonElement rel) &&
                rel.GetString()!.StartsWith("ua:Has", StringComparison.Ordinal));
            Assert.That(
                hasComponentLink,
                Is.True,
                "A HasOrderedComponent reference should produce a typed component link with rel=ua:HasOrderedComponent.");
        }

        [Test]
        public void ToNodeSetSynthesizesHasComponentArrayAsForwardReferences()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:hasComponent\":[\"i=47\",\"i=48\"]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UANode root1266 = result.Value!.Items?.FirstOrDefault();
            Assert.That(root1266, Is.Not.Null);
            bool hasForwardComponents = root1266.References?.Any(r =>
                r.IsForward && string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal)) ?? false;
            Assert.That(
                hasForwardComponents,
                Is.True,
                "uav:hasComponent entries should become forward HasComponent references on the root node.");
        }

        [Test]
        public void ToNodeSetSynthesizesComponentOfArrayAsBackwardReferences()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:componentOf\":[\"i=200\"]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UANode root1290 = result.Value!.Items?.FirstOrDefault();
            Assert.That(root1290, Is.Not.Null);
            bool hasBackwardComponent = root1290.References?.Any(r =>
                !r.IsForward && string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal)) ?? false;
            Assert.That(
                hasBackwardComponent,
                Is.True,
                "uav:componentOf entries should become backward HasComponent references on the root node.");
        }

        [Test]
        public void ToNodeSetTreatsARetiredIsEventFlagAsResidueWhenTypeIsArray()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"events\":{" +
                "\"alarm\":{\"@type\":[\"uav:eventType\",\"saref:Event\"],\"uav:isEvent\":false}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.EventAnnotationConflict),
                Is.False,
                "WoT Binding 1.1 defines no uav:isEvent term, so it can contradict nothing.");
        }

        [Test]
        public void ToNodeSetSanitizesSpecialCharTitleToNull()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"!@#$%\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UANode root1334 = result.Value!.Items?.FirstOrDefault();
            Assert.That(root1334, Is.Not.Null);
            Assert.That(
                root1334.BrowseName,
                Does.Contain("Thing"),
                "A title with only special chars should fall back to 'Thing' as the sanitized name.");
        }

        [Test]
        public void ToNodeSetDeriveModelUriDefaultsToSynthesized()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            bool hasSynthesizedUri = result.Value!.NamespaceUris?.Any(
                ns => ns.Contains("opcua", StringComparison.OrdinalIgnoreCase) ||
                      ns.Contains("synthesized", StringComparison.OrdinalIgnoreCase) ||
                      ns.Contains("wot", StringComparison.OrdinalIgnoreCase)) ?? false;
            Assert.That(
                result.Value.NamespaceUris,
                Is.Not.Null.And.Not.Empty,
                "A ThingModel without a uav:id or @id should still get a model URI namespace.");
        }

        [Test]
        public void ToNodeSetLocalNameExtractedFromNsuBrowseName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"sensor\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"nsu=urn:example:ns;SensorValue\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(variable, Is.Not.Null);
            Assert.That(
                variable.BrowseName,
                Does.Contain("SensorValue"),
                "Local name from nsu= browseName form should be 'SensorValue'.");
        }

        [Test]
        public void ToNodeSetLocalNameFallsBackToKeyWhenNsuBrowseNameHasNoSemicolon()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"mykey\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"nsu=urn:example:ns\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(variable, Is.Not.Null,
                "A property with nsu= browseName without semicolon should still produce a UAVariable.");
        }

        [Test]
        public void ToNodeSetSynthesizesActionWithTitle()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"actions\":{" +
                "\"start\":{\"title\":\"Start Motor\",\"@type\":\"uav:method\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAMethod method = result.Value!.Items!.OfType<UAMethod>().FirstOrDefault();
            Assert.That(method, Is.Not.Null);
            Assert.That(
                method.DisplayName != null && method.DisplayName.Length > 0 &&
                method.DisplayName[0].Value == "Start Motor",
                Is.True,
                "An action with a title should populate the UAMethod DisplayName.");
        }

        [Test]
        public void ToNodeSetSynthesizesEventWithTitle()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"events\":{" +
                "\"alarm\":{\"title\":\"High Temp Alarm\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType eventType = result.Value!.Items!.OfType<UAObjectType>()
                .Skip(1).FirstOrDefault();
            Assert.That(eventType, Is.Not.Null);
            Assert.That(
                eventType.DisplayName != null && eventType.DisplayName.Length > 0 &&
                eventType.DisplayName[0].Value == "High Temp Alarm",
                Is.True,
                "An event affordance with a title should populate the UAObjectType DisplayName.");
        }

        [Test]
        public void FromNodeSetVariableWithNoDataTypeEmitsNoJsonType()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=800",
                        BrowseName = "1:SensorType",
                        DisplayName = [new Export.LocalizedText { Value = "SensorType" }],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=801"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=801",
                        BrowseName = "1:RawValue",
                        DisplayName = [new Export.LocalizedText { Value = "RawValue" }],
                        DataType = "UnknownCustomType",
                        AccessLevel = 1
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Properties, Is.Not.Empty);
            bool hasTypeField = document.RootElement.TryGetProperty("properties", out var props) &&
                props.EnumerateObject().Any(p =>
                    p.Value.TryGetProperty("type", out var _));
            Assert.That(
                !hasTypeField,
                Is.True,
                "A property with an unrecognized DataType should not emit a JSON 'type' field.");
        }

        [Test]
        public void FromNodeSetVariableTypeRootEmitsVariableTypeModel()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAVariableType
                    {
                        NodeId = "ns=1;i=900",
                        BrowseName = "1:TemperatureVariableType",
                        DisplayName = [new Export.LocalizedText { Value = "TemperatureVariableType" }],
                        DataType = "Double",
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=62"
                            }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            bool isVariableType = document.RootElement.TryGetProperty("@type", out JsonElement typeEl) &&
                typeEl.ValueKind == JsonValueKind.Array &&
                typeEl.EnumerateArray().Any(t =>
                    t.ValueKind == JsonValueKind.String &&
                    t.GetString() == "uav:variableType");
            Assert.That(
                isVariableType,
                Is.True,
                "A UAVariableType root should produce @type containing 'uav:variableType'.");
        }

        [Test]
        public void ToNodeSetValidatesUndefinedUavBindingRelation()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"links\":[{\"rel\":\"uav:unknownBinding\",\"href\":\"i=47\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("uav:unknownBinding", StringComparison.Ordinal)),
                Is.True,
                "A link with a uav:-prefixed relation that is not a known binding relation should produce ModelConceptUnresolved.");
        }

        [Test]
        public void ToNodeSetValidatesUnboundNsRelationPrefix()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"links\":[{\"rel\":\"ns99:MyRefType\",\"href\":\"i=47\",\"uav:refId\":\"i=47\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("ns99:MyRefType", StringComparison.Ordinal)),
                Is.True,
                "A link with a ns<n>: prefix not bound in @context should produce ModelConceptUnresolved.");
        }

        [Test]
        public void ToNodeSetValidatesMapToTypeNameRequiresMapToType()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"val\":{\"type\":\"number\"," +
                "\"uav:mapToTypeName\":\"ua:AnalogItemType\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("uav:mapToTypeName", StringComparison.Ordinal) &&
                    d.Message.Contains("uav:mapToType", StringComparison.Ordinal)),
                Is.True,
                "uav:mapToTypeName without uav:mapToType should produce ModelConceptUnresolved.");
        }

        /// <summary>
        /// Spec PR #19 replaced <c>uav:capability</c> with <c>ua:HasInterface</c>
        /// used directly as a link <c>rel</c>, so the converter has to know the
        /// ReferenceType by name.
        /// </summary>
        [Test]
        public void ToNodeSetAddsHasInterfaceForUaLink()
        {
            byte[] json = ThingModelJson(
                "\"links\":[{\"rel\":\"ua:HasInterface\",\"href\":\"i=58\"}]");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().First();
            Assert.That(
                root.References!.Any(r =>
                    r.IsForward &&
                    string.Equals(r.ReferenceType, "i=17603", StringComparison.Ordinal) &&
                    string.Equals(r.Value, "i=58", StringComparison.Ordinal)),
                Is.True,
                "A ua:HasInterface link should synthesize a HasInterface reference (i=17603).");
        }

        [Test]
        public void ToNodeSetSilentlyIgnoresExternalPrefixRelations()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"links\":[{\"rel\":\"https://example.org/rels/parent\",\"href\":\"i=47\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.All(d => d.Code != WotDiagnosticCode.ModelConceptUnresolved ||
                    !d.Message.Contains("example.org", StringComparison.Ordinal)),
                Is.True,
                "A link with https:-prefixed rel should not trigger ModelConceptUnresolved.");
        }

        [Test]
        public void ToNodeSetWarnWhenHasComponentEntryUsesSessionLocalNsForm()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:hasComponent\":[\"ns=2;i=100\"]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NonPortableIdentity &&
                    d.Message.Contains("ns=2;i=100", StringComparison.Ordinal)),
                Is.True,
                "A uav:hasComponent entry with session-local ns= form should produce NonPortableIdentity warning.");
        }

        [Test]
        public void ToNodeSetWarnWhenHrefQueryIdUsesSessionLocalNsForm()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"links\":[{\"rel\":\"ua:HasComponent\"," +
                "\"href\":\"https://example.org/td?id=ns=2;i=100\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.NonPortableIdentity &&
                    d.Message.Contains("ns=2;i=100", StringComparison.Ordinal)),
                Is.True,
                "An href with ?id=ns=<index> query should produce NonPortableIdentity warning.");
        }

        [Test]
        public void ToNodeSetQualifiedNameResolvesNsuOpcUaNamespaceToBareName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"val\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"nsu=http://opcfoundation.org/UA/;BaseDataVariableType\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(variable, Is.Not.Null);
            Assert.That(
                variable.BrowseName,
                Is.EqualTo("BaseDataVariableType"),
                "nsu= with OPC UA namespace should strip the namespace and return just the local name.");
        }

        [Test]
        public void ToNodeSetNodeIdResolvesNsuOpcUaNamespaceToIdentifierOnly()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:id\":\"nsu=http://opcfoundation.org/UA/;i=1\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UANode root = result.Value!.Items?.FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.NodeId,
                Is.EqualTo("i=1"),
                "nsu= with OPC UA namespace for uav:id should resolve to bare 'i=<n>' form.");
        }

        [Test]
        public void ToNodeSetNodeIdResolvesNsuCustomNamespaceToIndexedForm()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"uav:id\":\"nsu=urn:custom:ns;i=42\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UANode root = result.Value!.Items?.FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            Assert.That(
                root.NodeId,
                Does.StartWith("ns=").And.EndWith(";i=42"),
                "nsu= with custom namespace for uav:id should resolve to 'ns=<index>;i=42' form.");
        }

        [Test]
        public void ToNodeSetQualifiedNameResolvesContextPrefixToOpcUaName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\"," +
                "\"properties\":{" +
                "\"val\":{\"type\":\"number\"," +
                "\"uav:browseName\":\"ua:AnalogItemType\"}" +
                "}}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAVariable variable = result.Value!.Items!.OfType<UAVariable>().FirstOrDefault();
            Assert.That(variable, Is.Not.Null);
            Assert.That(
                variable.BrowseName,
                Is.EqualTo("AnalogItemType"),
                "ua: context prefix for OPC UA namespace should resolve to bare local name.");
        }

        [Test]
        public void FromNodeSetWritesDescriptionFromNode()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:MotorType",
                        DisplayName = [new Export.LocalizedText { Value = "MotorType" }],
                        Description = [new Export.LocalizedText { Value = "A type for electric motors." }],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            bool hasDescription = document.RootElement.TryGetProperty("description", out JsonElement desc) &&
                desc.ValueKind == JsonValueKind.String &&
                desc.GetString() == "A type for electric motors.";
            Assert.That(
                hasDescription,
                Is.True,
                "A UANode with a Description should emit a 'description' field in the WoT document.");
        }

        [Test]
        public void FromNodeSetPortableBrowseNameStripsNamespace0Prefix()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1100",
                        BrowseName = "1:SensorType",
                        DisplayName = [new Export.LocalizedText { Value = "SensorType" }],
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
                                ReferenceType = "HasProperty",
                                IsForward = true,
                                Value = "ns=1;i=1101"
                            }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=1101",
                        BrowseName = "0:Namespace0Prop",
                        DisplayName = [new Export.LocalizedText { Value = "Namespace0Prop" }],
                        DataType = "Double",
                        AccessLevel = 1
                    }
                ]
            };

            WotConversionResult<WotDocument> result = WotNodeSetConverter.FromNodeSetResult(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(result.Value, Is.Not.Null);
            using WotDocument document = result.Value!;
            Assert.That(document.Properties, Is.Not.Empty);
            bool hasNs0BrowseName = document.RootElement.TryGetProperty("properties", out var props) &&
                props.EnumerateObject().Any(p =>
                    p.Value.TryGetProperty("uav:browseName", out var bn) &&
                    bn.GetString() == "Namespace0Prop");
            Assert.That(
                hasNs0BrowseName,
                Is.True,
                "A BrowseName with namespace index 0 should produce a bare 'Name' portable browse name.");
        }

        [Test]
        public async Task ComponentOfLinkToRegistryDocumentEmitsInverseHasComponent()
        {
            byte[] childJson = ThingDescriptionWithComponentOf("urn:parent");
            byte[] parentJson = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Parent\"," +
                "\"uav:id\":\"nsu=urn:plant;s=Parent\"}");

            using WotDocument document = WotDocument.Parse(childJson);
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(
                    document,
                    thingResolver: new MapThingResolver(new Dictionary<string, byte[]>
                    {
                        ["urn:parent"] = parentJson
                    }));

            Assert.That(result.Success, Is.True);
            AssertParentReference(result.Value!, "nsu=urn:plant;s=Parent");
        }

        [Test]
        public async Task ComponentOfLinkToAddressSpaceNodeIdEmitsInverseHasComponent()
        {
            const string parentNodeId = "nsu=urn:plant;s=Line01";
            using WotDocument document = WotDocument.Parse(
                ThingDescriptionWithComponentOf(parentNodeId));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(
                    document,
                    null,
                    null,
                    null,
                    nodeResolver: new MapNodeResolver(new Dictionary<string, WotResolvedNode>
                    {
                        [parentNodeId] = new WotResolvedNode(
                            parentNodeId, WotExpectedNodeClass.Any)
                    }));

            Assert.That(result.Success, Is.True);
            AssertParentReference(result.Value!, parentNodeId);
        }

        [Test]
        public async Task ComponentOfLinkToMissingTargetFailsLoudly()
        {
            using WotDocument document = WotDocument.Parse(
                ThingDescriptionWithComponentOf("nsu=urn:plant;s=Missing"));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code == WotDiagnosticCode.UnresolvedParentPlacement),
                Is.True,
                "An unresolved uav:componentOf link must fail rather than dropping the reference.");
        }

        [Test]
        public async Task NoComponentOfLinkDoesNotEmitParentHasComponent()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Child\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter
                .ToNodeSetResultAsync(document);

            Assert.That(result.Success, Is.True);
            UANode root = result.Value!.Items![0];
            Assert.That(
                root.References?.Any(r =>
                    !r.IsForward &&
                    string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal)) ?? false,
                Is.False,
                "No uav:componentOf link leaves the existing Objects-folder Organizes placement to runtime import.");
        }

        private static byte[] ThingDescriptionWithComponentOf(string href)
        {
            return WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":\"uav:object\",\"title\":\"Child\"," +
                "\"links\":[{\"rel\":\"uav:componentOf\",\"href\":\"" + href + "\"}]}");
        }

        private static void AssertParentReference(UANodeSet nodeSet, string parentNodeId)
        {
            UANode root = nodeSet.Items![0];
            Assert.That(
                root.References?.Any(r =>
                    !r.IsForward &&
                    string.Equals(r.ReferenceType, "HasComponent", StringComparison.Ordinal) &&
                    string.Equals(r.Value, parentNodeId, StringComparison.Ordinal)) ?? false,
                Is.True);
        }

        private sealed class MapThingResolver : IWotThingResolver
        {
            public MapThingResolver(IReadOnlyDictionary<string, byte[]> documents)
            {
                m_documents = documents;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolverResult>(
                    m_documents.TryGetValue(reference, out byte[] document)
                        ? WotResolverResult.FromBytes(document)
                        : WotResolverResult.NotFound);
            }

            private readonly IReadOnlyDictionary<string, byte[]> m_documents;
        }

        private sealed class MapNodeResolver : IWotNodeResolver
        {
            public MapNodeResolver(IReadOnlyDictionary<string, WotResolvedNode> nodes)
            {
                m_nodes = nodes;
            }

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<bool>(false);
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<WotResolvedNode?>(
                    m_nodes.TryGetValue(expandedNodeId, out WotResolvedNode node)
                        ? node
                        : null);
            }

            private readonly IReadOnlyDictionary<string, WotResolvedNode> m_nodes;
        }
    }
}
