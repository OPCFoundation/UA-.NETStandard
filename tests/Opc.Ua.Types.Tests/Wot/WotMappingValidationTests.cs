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
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotMappingValidationTests
    {
        [Test]
        public void FromNodeSetEmitsEventTypeAnnotationForEventTypeRoot()
        {
            UANodeSet nodeSet = CreateEventTypeNodeSet("OverTempType", "ns=1;i=1001");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(
                document.TypeTokens.Contains("uav:eventType"),
                Is.True,
                "Event type root should carry the uav:eventType annotation.");
            Assert.That(
                document.RootElement.TryGetProperty("uav:isEvent", out _),
                Is.False,
                "WoT Binding 1.1 defines no uav:isEvent term.");
        }

        [Test]
        public void FromNodeSetEmitsEventTypeAnnotationRatherThanObjectType()
        {
            UANodeSet nodeSet = CreateEventTypeNodeSet("MyEventType", "ns=1;i=1002");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(document.TypeTokens.Contains("uav:objectType"), Is.False);
            Assert.That(document.TypeTokens.Contains("uav:eventType"), Is.True);
        }

        [Test]
        public void ToNodeSetSynthesizesFromTitleWithSpecialCharacters()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"My Device (v2) - Test!\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            UAObjectType root = result.Value?.Items?.OfType<UAObjectType>().FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            string browseName = root!.BrowseName;
            Assert.That(browseName, Is.Not.Null);
            Assert.That(
                browseName!.IndexOf(' ', StringComparison.Ordinal) < 0,
                Is.True,
                "BrowseName derived from SanitizeName should have no spaces.");
            Assert.That(
                browseName.IndexOf('(', StringComparison.Ordinal) < 0,
                Is.True,
                "BrowseName derived from SanitizeName should have no parentheses.");
        }

        [Test]
        public void ToNodeSetSynthesizesFromTitleWithAllSpecialChars()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"!@#$%^&*()=+[]{}|;':,./<>?\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            UAObjectType root = result.Value?.Items?.OfType<UAObjectType>().FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            Assert.That(root!.BrowseName, Is.Not.Null);
        }

        [Test]
        public void ToNodeSetSynthesizesHasComponentReferencesFromUavHasComponent()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"MyType\",\"uav:browseName\":\"1:MyType\"," +
                "\"uav:hasComponent\":[\"nsu=urn:test;i=99\",\"nsu=urn:test;i=100\"]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items?.OfType<UAObjectType>().FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            var forwardRefs = root!.References?
                .Where(r => r.IsForward && r.ReferenceType == "HasComponent")
                .ToList();
            Assert.That(forwardRefs, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToNodeSetSynthesizesComponentOfReferences()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"MyType\",\"uav:browseName\":\"1:MyType\"," +
                "\"uav:componentOf\":[\"nsu=urn:test;i=50\"]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items?.OfType<UAObjectType>().FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            var backwardRefs = root!.References?
                .Where(r => !r.IsForward && r.ReferenceType == "HasComponent")
                .ToList();
            Assert.That(backwardRefs, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToNodeSetReportsUndefinedUavRelationInLink()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[{\"rel\":\"uav:unknownBindingRel\",\"href\":\"urn:x\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("uav:unknownBindingRel", StringComparison.Ordinal)),
                Is.True,
                "An undefined uav:-prefixed relation should produce a diagnostic.");
        }

        [Test]
        public void ToNodeSetReportsUnboundPrefixInLinkRelation()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"links\":[{\"rel\":\"unbound:SomeRel\",\"href\":\"urn:x\",\"uav:refId\":\"i=47\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved),
                Is.True,
                "A non-HTTP/URN relation with an unbound prefix should produce a diagnostic.");
        }

        [Test]
        public void ToNodeSetReportsMapToTypeNameNotCompactModelName()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"uav:mapToTypeName\":\"not-a-compact-name\"," +
                "\"uav:mapToType\":\"i=58\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved),
                Is.True,
                "A uav:mapToTypeName that is not a compact model name should produce a diagnostic.");
        }

        [Test]
        public void ToNodeSetReportsMapToTypeNameMissingDefinitiveMember()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                "\"title\":\"T\",\"uav:browseName\":\"1:T\"," +
                "\"uav:mapToTypeName\":\"ua:BaseObjectType\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                    d.Message.Contains("uav:mapToType", StringComparison.Ordinal)),
                Is.True,
                "uav:mapToTypeName without the required uav:mapToType should produce a diagnostic.");
        }

        [Test]
        public void FromNodeSetRoundTripPreservesEventTypeIsEvent()
        {
            UANodeSet source = CreateEventTypeNodeSet("AlertType", "ns=1;i=2001");

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);
            UAObjectType root = result.Value!.Items?.OfType<UAObjectType>().FirstOrDefault();
            Assert.That(root, Is.Not.Null);
            Assert.That(root!.BrowseName, Is.EqualTo("1:AlertType"));
        }

        [Test]
        public void ToNodeSetSynthesizesVariableTypeRootAsVariableType()
        {
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"uav:variableType\"]," +
                "\"title\":\"SpeedType\",\"uav:browseName\":\"1:SpeedType\"," +
                "\"uav:id\":\"nsu=urn:test;i=3001\"}");

            using WotDocument document = WotDocument.Parse(json);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Not.Null);

            // §9.1 maps a VariableType to a Thing Model too. Reading only
            // "Thing Model" turned every one into an ObjectType, losing the
            // type and inventing a different one in its place.
            Assert.That(
                result.Value!.Items?.OfType<UAVariableType>().Any(),
                Is.True,
                "A uav:variableType Thing Model synthesizes as a UAVariableType.");
            Assert.That(
                result.Value!.Items?.OfType<UAObjectType>().Any(),
                Is.False);
        }

        [Test]
        public void FromNodeSetVariableTypeEmitsVariableTypeAnnotation()
        {
            var nodeSet = new UANodeSet
            {
                NamespaceUris = ["urn:test"],
                Models = [new ModelTableEntry { ModelUri = "urn:test" }],
                Items =
                [
                    new UAVariableType
                    {
                        NodeId = "ns=1;i=3001",
                        BrowseName = "1:SpeedType",
                        DisplayName = [new Export.LocalizedText { Value = "SpeedType" }],
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

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                nodeSet,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                });

            Assert.That(document.TypeTokens.Contains("uav:variableType"), Is.True);
        }

        private static UANodeSet CreateEventTypeNodeSet(string browseName, string nodeId)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model",
                        Version = "1.0.0"
                    }
                ],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = nodeId,
                        BrowseName = "1:" + browseName,
                        DisplayName =
                        [
                            new Export.LocalizedText { Value = browseName }
                        ],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=2041"
                            }
                        ]
                    }
                ]
            };
        }
    }
}
