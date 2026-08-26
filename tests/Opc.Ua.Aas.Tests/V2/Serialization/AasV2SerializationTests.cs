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
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Tests.V2.Serialization
{
    /// <summary>
    /// Tests AAS V2.0.1 document ingestion.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasV2SerializationTests
    {
        [Test]
        public async Task JsonReaderParsesV2EnvironmentAndEverySubmodelElementType()
        {
            AasDocumentReadResult result = await ReadJsonAsync(FullJson).ConfigureAwait(false);

            AasEnvironment environment = result.Environment!;
            ArrayOf<AasSubmodelElement> elements = environment.Submodels.Value[0].SubmodelElements.Value;
            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(environment.AssetAdministrationShells.Value[0].Asset.IdShort, Is.EqualTo("asset1"));
                Assert.That(environment.AssetAdministrationShells.Value[0].Views.Value[0].Referables.Value[0].Keys[0].Type,
                    Is.EqualTo(AASKeyElementsDataType.Asset));
                Assert.That(environment.Assets.Value[0].Identification.IdType, Is.EqualTo(AASIdentifierTypeDataType.IRDI));
                Assert.That(elements[0], Is.TypeOf<AasProperty>());
                Assert.That(elements[1], Is.TypeOf<AasMultiLanguageProperty>());
                Assert.That(elements[2], Is.TypeOf<AasRange>());
                Assert.That(elements[3], Is.TypeOf<AasBlob>());
                Assert.That(elements[4], Is.TypeOf<AasFile>());
                Assert.That(elements[5], Is.TypeOf<AasReferenceElement>());
                Assert.That(elements[6], Is.TypeOf<AasRelationshipElement>());
                Assert.That(elements[7], Is.TypeOf<AasAnnotatedRelationshipElement>());
                Assert.That(elements[8], Is.TypeOf<AasOrderedSubmodelElementCollection>());
                Assert.That(elements[9], Is.TypeOf<AasEntity>());
                Assert.That(elements[10], Is.TypeOf<AasEvent>());
                Assert.That(elements[11], Is.TypeOf<AasOperation>());
                Assert.That(elements[12], Is.TypeOf<AasCapability>());
                Assert.That(((AasOrderedSubmodelElementCollection)elements[8]).AllowDuplicates.Value, Is.True);
            });
        }

        [Test]
        public async Task XmlReaderParsesV2EnvironmentAndEverySubmodelElementType()
        {
            AasDocumentReadResult result = await ReadXmlAsync(FullXml).ConfigureAwait(false);

            AasEnvironment environment = result.Environment!;
            ArrayOf<AasSubmodelElement> elements = environment.Submodels.Value[0].SubmodelElements.Value;
            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(environment.AssetAdministrationShells.Value[0].SubmodelReferences.Value[0].Keys[0].Value,
                    Is.EqualTo("submodel1"));
                Assert.That(environment.CustomConceptDescriptions.Value.Count, Is.EqualTo(1));
                Assert.That(environment.IrdiConceptDescriptions.Value.Count, Is.EqualTo(1));
                Assert.That(environment.IriConceptDescriptions.Value.Count, Is.EqualTo(1));
                Assert.That(elements[0], Is.TypeOf<AasProperty>());
                Assert.That(elements[1], Is.TypeOf<AasMultiLanguageProperty>());
                Assert.That(elements[2], Is.TypeOf<AasRange>());
                Assert.That(elements[3], Is.TypeOf<AasBlob>());
                Assert.That(elements[4], Is.TypeOf<AasFile>());
                Assert.That(elements[5], Is.TypeOf<AasReferenceElement>());
                Assert.That(elements[6], Is.TypeOf<AasRelationshipElement>());
                Assert.That(elements[7], Is.TypeOf<AasAnnotatedRelationshipElement>());
                Assert.That(elements[8], Is.TypeOf<AasOrderedSubmodelElementCollection>());
                Assert.That(elements[9], Is.TypeOf<AasEntity>());
                Assert.That(elements[10], Is.TypeOf<AasEvent>());
                Assert.That(elements[11], Is.TypeOf<AasOperation>());
                Assert.That(elements[12], Is.TypeOf<AasCapability>());
            });
        }

        [Test]
        public async Task ReadersPreserveAbsentAndPresentEmptyOptionalCollections()
        {
            const string json = "{\"assets\":[],\"assetAdministrationShells\":[],\"submodels\":[{\"idShort\":\"s\"," +
                "\"modelType\":{\"name\":\"Submodel\"},\"identification\":{\"id\":\"s\",\"idType\":\"IRI\"}," +
                "\"submodelElements\":[],\"qualifiers\":[]}],\"conceptDescriptions\":[]}";
            const string xml = "<aasenv xmlns=\"http://www.admin-shell.io/aas/2/0\"><assetAdministrationShells />" +
                "<assets /><submodels><submodel><idShort>s</idShort><identification idType=\"IRI\">s</identification>" +
                "<submodelElements /></submodel></submodels><conceptDescriptions /></aasenv>";

            AasDocumentReadResult jsonResult = await ReadJsonAsync(json).ConfigureAwait(false);
            AasDocumentReadResult xmlResult = await ReadXmlAsync(xml).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(jsonResult.Environment!.AssetAdministrationShells.IsPresent, Is.True);
                Assert.That(jsonResult.Environment.AssetAdministrationShells.Value.Count, Is.Zero);
                Assert.That(jsonResult.Environment.Submodels.Value[0].SubmodelElements.IsPresent, Is.True);
                Assert.That(jsonResult.Environment.Submodels.Value[0].Qualifiers.IsPresent, Is.True);
                Assert.That(jsonResult.Environment.Submodels.Value[0].DataSpecifications.IsPresent, Is.False);
                Assert.That(xmlResult.Environment!.Assets.IsPresent, Is.True);
                Assert.That(xmlResult.Environment.Assets.Value.Count, Is.Zero);
                Assert.That(xmlResult.Environment.Submodels.Value[0].SubmodelElements.IsPresent, Is.True);
            });
        }

        [Test]
        public async Task AasxReaderReadsXmlJsonAndSupplementaryFiles()
        {
            using var jsonPackage = new MemoryStream();
            using var xmlPackage = new MemoryStream();
            await WritePackageAsync(jsonPackage, "/aasx/environment.json", "application/json", FullJson)
                .ConfigureAwait(false);
            await WritePackageAsync(xmlPackage, "/aasx/environment.xml", "application/xml", FullXml)
                .ConfigureAwait(false);
            jsonPackage.Position = 0;
            xmlPackage.Position = 0;

            AasxPackageReadResult jsonResult = await new AasxPackageReader().ReadAsync(jsonPackage)
                .ConfigureAwait(false);
            AasxPackageReadResult xmlResult = await new AasxPackageReader().ReadAsync(xmlPackage)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(jsonResult.Succeeded, Is.True);
                Assert.That(jsonResult.SupplementaryFiles.Count, Is.EqualTo(1));
                Assert.That(Encoding.UTF8.GetString(jsonResult.SupplementaryFiles[0].Content.ToArray()),
                    Is.EqualTo("supplement"));
                Assert.That(xmlResult.Succeeded, Is.True);
                Assert.That(xmlResult.Environment!.Submodels.Value[0].IdShort, Is.EqualTo("submodel1"));
            });
        }

        [Test]
        public async Task NegativeControlsReturnClearDiagnostics()
        {
            AasDocumentReadResult v3Json = await ReadJsonAsync("{\"submodels\":[{\"id\":\"s\"," +
                "\"modelType\":\"Submodel\"}]}").ConfigureAwait(false);
            AasDocumentReadResult malformedJson = await ReadJsonAsync("{").ConfigureAwait(false);
            AasDocumentReadResult malformedXml = await ReadXmlAsync("<aasenv>").ConfigureAwait(false);
            using var noOrigin = new MemoryStream();
            using (Package.Open(noOrigin, FileMode.Create, FileAccess.ReadWrite))
            {
            }

            noOrigin.Position = 0;
            AasxPackageReadResult noOriginResult = await new AasxPackageReader().ReadAsync(noOrigin)
                .ConfigureAwait(false);
            using var missingSpec = new MemoryStream();
            await WritePackageWithMissingSpecAsync(missingSpec).ConfigureAwait(false);
            missingSpec.Position = 0;
            AasxPackageReadResult missingSpecResult = await new AasxPackageReader().ReadAsync(missingSpec)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(v3Json.Succeeded, Is.False);
                Assert.That(v3Json.Error, Does.Contain("AAS V3"));
                Assert.That(malformedJson.Error, Does.Contain("malformed"));
                Assert.That(malformedXml.Error, Does.Contain("malformed"));
                Assert.That(noOriginResult.Error, Does.Contain("aasx-origin"));
                Assert.That(missingSpecResult.Error, Does.Contain("aas-spec"));
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

        private static async Task WritePackageAsync(
            Stream stream,
            string environmentPath,
            string contentType,
            string environment)
        {
            using Package package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
            Uri originUri = PackUriHelper.CreatePartUri(new Uri("/aasx/origin", UriKind.Relative));
            PackagePart originPart = package.CreatePart(
                originUri,
                "application/vnd.admin-shell.aasx-origin",
                CompressionOption.Maximum);
            package.CreateRelationship(originUri, TargetMode.Internal, Opc.Ua.Aas.AasxPackageRelationshipTypes.Origin);

            Uri environmentUri = PackUriHelper.CreatePartUri(new Uri(environmentPath, UriKind.Relative));
            PackagePart environmentPart = package.CreatePart(environmentUri, contentType, CompressionOption.Maximum);
            originPart.CreateRelationship(
                environmentUri,
                TargetMode.Internal,
                Opc.Ua.Aas.AasxPackageRelationshipTypes.Environment);

            Uri supplementUri = PackUriHelper.CreatePartUri(new Uri("/aasx/files/readme.txt", UriKind.Relative));
            PackagePart supplementPart = package.CreatePart(supplementUri, "text/plain", CompressionOption.Maximum);
            originPart.CreateRelationship(
                supplementUri,
                TargetMode.Internal,
                Opc.Ua.Aas.AasxPackageRelationshipTypes.SupplementaryFile);

            using (Stream environmentStream = environmentPart.GetStream(FileMode.Create, FileAccess.Write))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(environment);
#if NETFRAMEWORK
                await environmentStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#else
                await environmentStream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
#endif
            }

            using (Stream supplementStream = supplementPart.GetStream(FileMode.Create, FileAccess.Write))
            {
                byte[] bytes = Encoding.UTF8.GetBytes("supplement");
#if NETFRAMEWORK
                await supplementStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
#else
                await supplementStream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
#endif
            }
        }

        private static async Task WritePackageWithMissingSpecAsync(Stream stream)
        {
            using Package package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
            Uri originUri = PackUriHelper.CreatePartUri(new Uri("/aasx/origin", UriKind.Relative));
            PackagePart originPart = package.CreatePart(
                originUri,
                "application/vnd.admin-shell.aasx-origin",
                CompressionOption.Maximum);
            package.CreateRelationship(originUri, TargetMode.Internal, Opc.Ua.Aas.AasxPackageRelationshipTypes.Origin);
            originPart.CreateRelationship(
                new Uri("/aasx/missing.json", UriKind.Relative),
                TargetMode.Internal,
                Opc.Ua.Aas.AasxPackageRelationshipTypes.Environment);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private const string ReferenceJson = "{\"keys\":[{\"type\":\"Asset\",\"idType\":\"IRI\"," +
            "\"value\":\"https://example.test/asset\",\"local\":true}]}";

        private const string FullJson = "{\"assets\":[{\"idShort\":\"asset1\",\"category\":\"VARIABLE\"," +
            "\"modelType\":{\"name\":\"Asset\"},\"identification\":{\"id\":\"https://example.test/asset\"," +
            "\"idType\":\"IRDI\"},\"administration\":{\"version\":\"1\",\"revision\":\"0\"},\"kind\":\"Instance\"}]," +
            "\"assetAdministrationShells\":[{\"idShort\":\"shell1\",\"category\":\"VARIABLE\"," +
            "\"modelType\":{\"name\":\"AssetAdministrationShell\"},\"identification\":{\"id\":\"shell\"," +
            "\"idType\":\"Custom\"},\"administration\":{\"version\":\"1\",\"revision\":\"0\"},\"asset\":" +
            ReferenceJson + ",\"submodels\":[" + ReferenceJson + "],\"views\":[{\"idShort\":\"view\"," +
            "\"modelType\":{\"name\":\"View\"},\"containedElements\":[" + ReferenceJson + "]}]}]," +
            "\"submodels\":[{\"idShort\":\"submodel1\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Submodel\"}," +
            "\"identification\":{\"id\":\"submodel1\",\"idType\":\"IRI\"},\"kind\":\"Instance\",\"submodelElements\":[" +
            "{\"idShort\":\"p\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Property\"},\"kind\":\"Instance\"," +
            "\"valueType\":\"int\",\"value\":\"42\"}," +
            "{\"idShort\":\"mlp\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"MultiLanguageProperty\"}," +
            "\"kind\":\"Instance\",\"value\":[{\"language\":\"en\",\"text\":\"hello\"}]}," +
            "{\"idShort\":\"range\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Range\"},\"kind\":\"Instance\"," +
            "\"valueType\":\"int\",\"min\":\"1\",\"max\":\"9\"}," +
            "{\"idShort\":\"blob\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Blob\"},\"kind\":\"Instance\"," +
            "\"mimeType\":\"application/octet-stream\",\"value\":\"AQID\"}," +
            "{\"idShort\":\"file\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"File\"},\"kind\":\"Instance\"," +
            "\"mimeType\":\"text/plain\",\"value\":\"/file.txt\"}," +
            "{\"idShort\":\"ref\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"ReferenceElement\"}," +
            "\"kind\":\"Instance\",\"value\":" + ReferenceJson + "}," +
            "{\"idShort\":\"rel\",\"category\":\"RELATIONSHIP\",\"modelType\":{\"name\":\"RelationshipElement\"}," +
            "\"kind\":\"Instance\",\"first\":" + ReferenceJson + ",\"second\":" + ReferenceJson + "}," +
            "{\"idShort\":\"ann\",\"category\":\"RELATIONSHIP\",\"modelType\":{\"name\":\"AnnotatedRelationshipElement\"}," +
            "\"kind\":\"Instance\",\"first\":" + ReferenceJson + ",\"second\":" + ReferenceJson + ",\"annotation\":[]}," +
            "{\"idShort\":\"ordered\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"SubmodelElementCollection\"}," +
            "\"kind\":\"Instance\",\"ordered\":true,\"allowDuplicates\":true,\"value\":[]}," +
            "{\"idShort\":\"entity\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Entity\"},\"kind\":\"Instance\"," +
            "\"entityType\":\"SelfManagedEntity\",\"asset\":" + ReferenceJson + ",\"statements\":[]}," +
            "{\"idShort\":\"event\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"BasicEvent\"},\"kind\":\"Instance\"," +
            "\"observed\":" + ReferenceJson + "}," +
            "{\"idShort\":\"op\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Operation\"},\"kind\":\"Instance\"}," +
            "{\"idShort\":\"cap\",\"category\":\"VARIABLE\",\"modelType\":{\"name\":\"Capability\"},\"kind\":\"Instance\"}]}]," +
            "\"conceptDescriptions\":[{\"idShort\":\"custom\",\"modelType\":{\"name\":\"ConceptDescription\"}," +
            "\"identification\":{\"id\":\"custom\",\"idType\":\"Custom\"}},{\"idShort\":\"irdi\"," +
            "\"modelType\":{\"name\":\"ConceptDescription\"},\"identification\":{\"id\":\"irdi\",\"idType\":\"IRDI\"}}," +
            "{\"idShort\":\"iri\",\"modelType\":{\"name\":\"ConceptDescription\"},\"identification\":{\"id\":\"iri\"," +
            "\"idType\":\"IRI\"}}]}";

        private const string ReferenceXml = "<keys><key type=\"Asset\" idType=\"IRI\" local=\"true\">" +
            "https://example.test/asset</key></keys>";

        private const string FullXml = "<aasenv xmlns=\"http://www.admin-shell.io/aas/2/0\"><assetAdministrationShells>" +
            "<assetAdministrationShell><idShort>shell1</idShort><category>VARIABLE</category>" +
            "<identification idType=\"Custom\">shell</identification><assetRef>" + ReferenceXml + "</assetRef>" +
            "<submodelRefs><submodelRef><keys><key type=\"Submodel\" idType=\"IdShort\" local=\"true\">submodel1</key>" +
            "</keys></submodelRef></submodelRefs></assetAdministrationShell></assetAdministrationShells><assets>" +
            "<asset><idShort>asset1</idShort><category>VARIABLE</category><identification idType=\"IRDI\">" +
            "https://example.test/asset</identification><kind>Instance</kind></asset></assets><submodels><submodel>" +
            "<idShort>submodel1</idShort><category>VARIABLE</category><identification idType=\"IRI\">submodel1" +
            "</identification><kind>Instance</kind><submodelElements>" +
            "<submodelElement><property><idShort>p</idShort><category>VARIABLE</category><kind>Instance</kind>" +
            "<valueType>int</valueType><value>42</value></property></submodelElement>" +
            "<submodelElement><multiLanguageProperty><idShort>mlp</idShort><category>VARIABLE</category>" +
            "<kind>Instance</kind><value><langString lang=\"en\">hello</langString></value></multiLanguageProperty>" +
            "</submodelElement><submodelElement><range><idShort>range</idShort><category>VARIABLE</category>" +
            "<kind>Instance</kind><valueType>int</valueType><min>1</min><max>9</max></range></submodelElement>" +
            "<submodelElement><blob><idShort>blob</idShort><category>VARIABLE</category><kind>Instance</kind>" +
            "<value>AQID</value><mimeType>application/octet-stream</mimeType></blob></submodelElement>" +
            "<submodelElement><file><idShort>file</idShort><category>VARIABLE</category><kind>Instance</kind>" +
            "<mimeType>text/plain</mimeType><value>/file.txt</value></file></submodelElement>" +
            "<submodelElement><referenceElement><idShort>ref</idShort><category>VARIABLE</category><kind>Instance</kind>" +
            "<value>" + ReferenceXml + "</value></referenceElement></submodelElement>" +
            "<submodelElement><relationshipElement><idShort>rel</idShort><category>RELATIONSHIP</category>" +
            "<kind>Instance</kind><first>" + ReferenceXml + "</first><second>" + ReferenceXml + "</second>" +
            "</relationshipElement></submodelElement><submodelElement><annotatedRelationshipElement>" +
            "<idShort>ann</idShort><category>RELATIONSHIP</category><kind>Instance</kind><first>" + ReferenceXml +
            "</first><second>" + ReferenceXml + "</second><annotations /></annotatedRelationshipElement></submodelElement>" +
            "<submodelElement><submodelElementCollection><idShort>ordered</idShort><category>VARIABLE</category>" +
            "<kind>Instance</kind><value /><ordered>true</ordered><allowDuplicates>true</allowDuplicates>" +
            "</submodelElementCollection></submodelElement><submodelElement><entity><idShort>entity</idShort>" +
            "<category>VARIABLE</category><kind>Instance</kind><statements /><entityType>SelfManagedEntity</entityType>" +
            "<assetRef>" + ReferenceXml + "</assetRef></entity></submodelElement><submodelElement><basicEvent>" +
            "<idShort>event</idShort><category>VARIABLE</category><kind>Instance</kind><observed>" + ReferenceXml +
            "</observed></basicEvent></submodelElement><submodelElement><operation><idShort>op</idShort>" +
            "<category>VARIABLE</category><kind>Instance</kind></operation></submodelElement><submodelElement>" +
            "<capability><idShort>cap</idShort><category>VARIABLE</category><kind>Instance</kind></capability>" +
            "</submodelElement></submodelElements></submodel></submodels><conceptDescriptions><conceptDescription>" +
            "<idShort>custom</idShort><identification idType=\"Custom\">custom</identification></conceptDescription>" +
            "<conceptDescription><idShort>irdi</idShort><identification idType=\"IRDI\">irdi</identification>" +
            "</conceptDescription><conceptDescription><idShort>iri</idShort><identification idType=\"IRI\">iri" +
            "</identification></conceptDescription></conceptDescriptions></aasenv>";
    }
}
