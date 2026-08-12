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

using Opc.Ua.Aas.V3;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Serialization
{
    /// <summary>
    /// Tests the AAS JSON and XML document serializers.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasSerializationTests
    {
        [Test]
        public async Task JsonReaderAndWriterPreserveAbsentAndEmptyMembers()
        {
            const string json = "{\"assetAdministrationShells\":[],\"submodels\":[{\"id\":\"s\",\"modelType\":\"Submodel\"," +
                "\"displayName\":[],\"qualifiers\":[],\"submodelElements\":[]}]}";

            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.AssetAdministrationShells.IsPresent, Is.True);
                Assert.That(result.Environment.Submodels.Value[0].DisplayName.IsPresent, Is.True);
                Assert.That(result.Environment.Submodels.Value[0].Qualifiers.IsPresent, Is.True);
                Assert.That(result.Environment.Submodels.Value[0].SubmodelElements.Value.Count, Is.Zero);
                Assert.That(roundTrip, Does.Contain("\"assetAdministrationShells\": []"));
                Assert.That(roundTrip, Does.Contain("\"displayName\": []"));
                Assert.That(roundTrip, Does.Contain("\"qualifiers\": []"));
                Assert.That(roundTrip, Does.Contain("\"submodelElements\": []"));
            });
        }

        [Test]
        public async Task JsonReaderAndWriterPreserveAbsentMembersAsAbsent()
        {
            const string json = "{\"submodels\":[{\"id\":\"s\",\"modelType\":\"Submodel\"}]}";

            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].SubmodelElements.IsPresent, Is.False);
                Assert.That(result.Environment.Submodels.Value[0].Qualifiers.IsPresent, Is.False);
                Assert.That(roundTrip, Does.Not.Contain("submodelElements"));
                Assert.That(roundTrip, Does.Not.Contain("qualifiers"));
            });
        }

        [Test]
        public async Task JsonReaderReportsMalformedInput()
        {
            AasDocumentReadResult result = await ReadJsonAsync("{").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Error, Does.Contain("malformed"));
            });
        }

        [Test]
        public async Task JsonPropertyValueLexicalFormSurvivesRoundTrip()
        {
            const string json = "{\"submodels\":[{\"id\":\"s\",\"modelType\":\"Submodel\",\"submodelElements\":[" +
                "{\"idShort\":\"p\",\"modelType\":\"Property\",\"valueType\":\"xs:decimal\",\"value\":\"1.500000\"}]}]}";

            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(roundTrip, Does.Contain("\"value\": \"1.500000\""));
            });
        }

        [Test]
        public async Task XmlReaderAndWriterPreservePresentEmptyCollections()
        {
            const string xml = "<environment><submodels><submodel><id>s</id><modelType>Submodel</modelType>" +
                "<submodelElements /></submodel></submodels></environment>";

            AasDocumentReadResult result = await ReadXmlAsync(xml).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasXmlWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].SubmodelElements.IsPresent, Is.True);
                Assert.That(result.Environment.Submodels.Value[0].SubmodelElements.Value.Count, Is.Zero);
                Assert.That(roundTrip, Does.Contain("submodelElements"));
            });
        }

        [Test]
        public async Task XmlReaderReportsMalformedInput()
        {
            AasDocumentReadResult result = await ReadXmlAsync("<environment>").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Error, Does.Contain("malformed"));
            });
        }

        [Test]
        public async Task XmlDocumentRoundTripSerializesFromModel()
        {
            const string xml = "<environment><submodels><submodel><id>s</id><modelType>Submodel</modelType>" +
                "<submodelElements><submodelElement><idShort>p</idShort><modelType>Property</modelType>" +
                "<valueType>xs:decimal</valueType><value>1.500000</value></submodelElement></submodelElements>" +
                "</submodel></submodels></environment>";

            AasDocumentReadResult result = await ReadXmlAsync(xml).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasXmlWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(roundTrip, Does.Contain("1.500000"));
                Assert.That(roundTrip, Does.Contain("submodelElement"));
            });
        }

        [Test]
        public async Task JsonOrderedArraysPreserveOrder()
        {
            const string json = "{\"submodels\":[{\"id\":\"s\",\"modelType\":\"Submodel\",\"submodelElements\":[" +
                "{\"idShort\":\"ref\",\"modelType\":\"ReferenceElement\",\"value\":{\"type\":\"ExternalReference\"," +
                "\"keys\":[{\"type\":\"GlobalReference\",\"value\":\"a\"},{\"type\":\"Property\",\"value\":\"b\"}]} }," +
                "{\"idShort\":\"mlp\",\"modelType\":\"MultiLanguageProperty\",\"value\":[" +
                "{\"language\":\"en\",\"text\":\"one\"},{\"language\":\"de\",\"text\":\"zwei\"}]}," +
                "{\"idShort\":\"list\",\"modelType\":\"SubmodelElementList\",\"orderRelevant\":true," +
                "\"typeValueListElement\":\"Property\",\"value\":[{\"modelType\":\"Property\",\"valueType\":\"xs:string\"," +
                "\"value\":\"first\"},{\"modelType\":\"Property\",\"valueType\":\"xs:string\",\"value\":\"second\"}]}," +
                "{\"idShort\":\"op\",\"modelType\":\"Operation\",\"inputVariables\":[{\"value\":{\"modelType\":\"Property\"," +
                "\"valueType\":\"xs:string\",\"value\":\"in\"}}],\"outputVariables\":[{\"value\":{\"modelType\":\"Property\"," +
                "\"valueType\":\"xs:string\",\"value\":\"out\"}}],\"inoutputVariables\":[{\"value\":{\"modelType\":\"Property\"," +
                "\"valueType\":\"xs:string\",\"value\":\"io\"}}]}]}]}";

            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);
            string roundTrip = Encoding.UTF8.GetString(output.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(roundTrip.IndexOf("\"a\"", System.StringComparison.Ordinal),
                    Is.LessThan(roundTrip.IndexOf("\"b\"", System.StringComparison.Ordinal)));
                Assert.That(roundTrip.IndexOf("\"one\"", System.StringComparison.Ordinal),
                    Is.LessThan(roundTrip.IndexOf("\"zwei\"", System.StringComparison.Ordinal)));
                Assert.That(roundTrip.IndexOf("\"first\"", System.StringComparison.Ordinal),
                    Is.LessThan(roundTrip.IndexOf("\"second\"", System.StringComparison.Ordinal)));
                Assert.That(roundTrip, Does.Contain("inputVariables"));
                Assert.That(roundTrip, Does.Contain("outputVariables"));
                Assert.That(roundTrip, Does.Contain("inoutputVariables"));
            });
        }

        [Test]
        public async Task JsonRoundTripPreservesEverySubmodelElementType()
        {
            var environment = new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new[]
                {
                    new AasSubmodel
                    {
                        Id = "s",
                        SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(new AasSubmodelElement[]
                        {
                            new AasProperty { ValueType = AASDataTypeDefXsdDataType.String },
                            new AasMultiLanguageProperty(),
                            new AasRange { ValueType = AASDataTypeDefXsdDataType.Int },
                            new AasBlob { ContentType = "application/octet-stream" },
                            new AasFile { ContentType = "text/plain" },
                            new AasReferenceElement(),
                            new AasRelationshipElement { First = Reference(), Second = Reference() },
                            new AasAnnotatedRelationshipElement { First = Reference(), Second = Reference() },
                            new AasSubmodelElementCollection(),
                            new AasSubmodelElementList
                            {
                                TypeValueListElement = AASSubmodelElementsDataType.Property
                            },
                            new AasEntity { EntityType = AASEntityTypeDataType.SelfManagedEntity },
                            new AasBasicEventElement
                            {
                                Observed = Reference(),
                                Direction = AASDirectionDataType.Input,
                                State = AASStateOfEventDataType.On
                            },
                            new AasOperation(),
                            new AasCapability()
                        })
                    }
                })
            };

            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, environment).ConfigureAwait(false);
            string json = Encoding.UTF8.GetString(output.ToArray());
            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].SubmodelElements.Value.Count, Is.EqualTo(14));
                Assert.That(json, Does.Contain("Property"));
                Assert.That(json, Does.Contain("MultiLanguageProperty"));
                Assert.That(json, Does.Contain("Range"));
                Assert.That(json, Does.Contain("Blob"));
                Assert.That(json, Does.Contain("File"));
                Assert.That(json, Does.Contain("ReferenceElement"));
                Assert.That(json, Does.Contain("RelationshipElement"));
                Assert.That(json, Does.Contain("AnnotatedRelationshipElement"));
                Assert.That(json, Does.Contain("SubmodelElementCollection"));
                Assert.That(json, Does.Contain("SubmodelElementList"));
                Assert.That(json, Does.Contain("Entity"));
                Assert.That(json, Does.Contain("BasicEventElement"));
                Assert.That(json, Does.Contain("Operation"));
                Assert.That(json, Does.Contain("Capability"));
            });
        }

        [Test]
        public async Task JsonLargeVendoredTemplateCanBeRoundTripped()
        {
            const string path = "C:\\Users\\mschier\\.copilot\\session-state\\b0b93e43-93ad-40d8-854d-6bb0e5f5b89d" +
                "\\files\\opcua-drafts\\companion-specs\\AAS\\tools\\jsonld\\vendor\\templates\\digital-nameplate.json";
            if (!File.Exists(path))
            {
                Assert.Ignore("The vendored AAS template is not available in this environment.");
            }

            string json = File.ReadAllText(path);
            AasDocumentReadResult result = await ReadJsonAsync(json).ConfigureAwait(false);
            using var output = new MemoryStream();
            await new AasJsonWriter().WriteAsync(output, result.Environment!).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(output.Length, Is.GreaterThan(0));
            });
        }

        private static async Task<AasDocumentReadResult> ReadJsonAsync(string json)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return await new AasJsonReader().ReadAsync(input).ConfigureAwait(false);
        }

        private static async Task<AasDocumentReadResult> ReadXmlAsync(string xml)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return await new AasXmlReader().ReadAsync(input).ConfigureAwait(false);
        }

        private static AASReferenceDataType Reference()
        {
            var reference = Generated<AASReferenceDataType>();
            reference.Type = AASReferenceTypesDataType.ExternalReference;
            reference.Keys = new ArrayOf<AASKeyDataType>(new[]
            {
                new AASKeyDataType { Type = AASKeyTypesDataType.GlobalReference, Value = "x" }
            });
            return reference;
        }

        private static T Generated<T>()
            where T : class, new()
        {
            return new T();
        }
    }
}
