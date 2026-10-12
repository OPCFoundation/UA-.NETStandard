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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    public sealed class WotCapturedDataTypeDefinitionTests
    {
        [Test]
        public async Task CapturedDefinitionsRespectTheDocumentBudgetWithoutAResolver()
        {
            using WotDocument owner = DefinitionDocument();
            using WotDocument consumer = ConsumerDocument();
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.As<IWotCapturedDataTypeDefinitions>().SetupGet(value => value.DataTypeDefinitions)
                .Returns(WotNodeSetConverter.ReadDataTypeDefinitions(owner));
            var options = new WotNodeSetConverterOptions { MaxResolverDocuments = 1 };

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, options, resolver.Object, null, null).ConfigureAwait(false);

            Assert.That(result.Success, Is.False,
                "Supplying captured definitions must not bypass the configured conversion document budget.");
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CapturedDefinitionEmissionPreservesItsAuthoritativeNamespace(
            bool separatelyProjected, bool contextualDefinition)
        {
            using WotDocument owner = DefinitionDocument(contextualDefinition);
            using WotDocument consumer = ConsumerDocument();
            byte[] original = owner.Utf8Json.ToArray();
            ArrayOf<WotDataTypeDefinitionSource> read = WotNodeSetConverter.ReadDataTypeDefinitions(owner);
            Assert.That(read.Count, Is.EqualTo(1));
            Assert.That(read[0].Document, Is.SameAs(owner));
            Assert.That(WotNodeSetConverter.ReadDataTypeDefinitions(consumer).Count, Is.Zero);
            var source = new WotDataTypeDefinitionSource(owner, read[0].Definition)
            {
                ProjectedSeparately = separatelyProjected
            };
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.As<IWotCapturedDataTypeDefinitions>().SetupGet(value => value.DataTypeDefinitions)
                .Returns(new ArrayOf<WotDataTypeDefinitionSource>([source]));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, resolver.Object, null, null).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(value => value.Message)));
            UANodeSet nodes = result.Value!;
            Assert.That(nodes.Items!.OfType<UADataType>().Count(), Is.EqualTo(separatelyProjected ? 0 : 1));
            Assert.That(nodes.Models!.Any(model => model.ModelUri == "urn:test:captured-types"),
                Is.EqualTo(!separatelyProjected));
            UAVariable property = nodes.Items!.OfType<UAVariable>()
                .Single(node => node.BrowseName!.EndsWith(":Reading", System.StringComparison.Ordinal));
            Assert.That(NodeId.TryParse(property.DataType!, out NodeId dataType), Is.True);
            Assert.That(nodes.NamespaceUris![dataType.NamespaceIndex - 1], Is.EqualTo("urn:test:captured-types"));
            Assert.That(dataType.TryGetValue(out uint identifier), Is.True);
            Assert.That(identifier, Is.EqualTo(3000u));
            Assert.That(owner.Utf8Json.ToArray(), Is.EqualTo(original));
            resolver.Verify(value => value.ResolveThingAsync(
                It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void QualifiedNameParsingUsesTheCarryingContext()
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes("""
                {
                  "@context": {"t":"urn:outer"},
                  "properties": {"Reading":{"@context":{"t":"urn:inner"},"uav:dataTypeName":"t:Reading"}}
                }
                """));
            var owner = document.RootElement.GetProperty("properties").GetProperty("Reading");

            bool parsed = WotNodeSetConverter.TrySplitCompactName(
                document, "t:Reading", out string namespaceUri, out string name, owner);

            Assert.That(parsed, Is.True);
            Assert.That(namespaceUri, Is.EqualTo("urn:inner"));
            Assert.That(name, Is.EqualTo("Reading"));
            Assert.That(WotNodeSetConverter.TrySplitCompactName(
                document, "missing:Reading", out _, out _, owner), Is.False);
            Assert.That(WotNodeSetConverter.TryDeriveDataTypeNodeId(
                document, "t:Reading", out ExpandedNodeId identity, owner), Is.True);
            Assert.That(identity.ToString(), Is.EqualTo("nsu=urn:inner;s=DataTypes/Reading"));
            Assert.That(WotNodeSetConverter.TryDeriveDataTypeNodeId(
                document, "missing:Reading", out ExpandedNodeId missing, owner), Is.False);
            Assert.That(missing.IsNull, Is.True);
        }

        [Test]
        public async Task ScopedGraphReferenceDoesNotBecomeASecondCompleteDefinition()
        {
            using WotDocument owner = DefinitionDocument(contextualIdentity: true);
            using WotDocument consumer = ConsumerDocument(scopedReference: true);
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.As<IWotCapturedDataTypeDefinitions>().SetupGet(value => value.DataTypeDefinitions)
                .Returns(WotNodeSetConverter.ReadDataTypeDefinitions(owner));

            Assert.That(WotNodeSetConverter.ReadDataTypeDefinitions(consumer).Count, Is.Zero);
            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                consumer, null, resolver.Object, null, null).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(value => value.Message)));
            Assert.That(result.Value!.Items!.OfType<UADataType>().Count(), Is.EqualTo(1));
        }

        [TestCase("1.0", true)]
        [TestCase("99.0", false)]
        public void NativePathClassificationDoesNotReadRecordsOfAnUnsupportedProfile(string profile, bool restores)
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes($$$"""
                {
                  "title":"Readable",
                  "uav:nodes":{"@type":"uav:NodeModel","profileVersion":"{{{profile}}}","nodes":{}}
                }
                """));

            Assert.That(WotNodeSetConverter.TakesRestorePath(document), Is.EqualTo(restores));
        }

        private static WotDocument DefinitionDocument(bool contextualIdentity = false)
        {
            string graphId = contextualIdentity ? "d:captured-reading" : "urn:test:captured-reading";
            return WotDocument.Parse(Encoding.UTF8.GetBytes($$"""
                {
                  "@context":{"uav":"http://opcfoundation.org/UA/WoT-Binding/","t":"urn:test:captured-types"},
                  "id":"urn:test:definition-document",
                  "uav:dataTypeDefinitions":[{
                    "@context":{"d":"urn:test:"},
                    "@id":"{{graphId}}","@type":"uav:SimpleDataType",
                    "uav:dataTypeName":"t:Reading","uav:dataTypeId":"nsu=urn:test:captured-types;i=3000",
                    "uav:dataTypeSubtypeOf":{"uav:dataTypeId":"i=6"}
                  }]
                }
                """));
        }

        private static WotDocument ConsumerDocument(bool scopedReference = false)
        {
            string reference = scopedReference
                ? """{"@context":{"d":"urn:test:"},"@id":"d:captured-reading"}"""
                : """{"@id":"urn:test:captured-reading"}""";
            return WotDocument.Parse(Encoding.UTF8.GetBytes($$$"""
                {
                  "@context":{
                    "uav":"http://opcfoundation.org/UA/WoT-Binding/",
                    "c":"urn:test:captured-consumer","d":"urn:wrong:consumer"
                  },
                  "@type":"uav:object","id":"urn:test:consumer","title":"Consumer",
                  "uav:id":"nsu=urn:test:captured-consumer;i=1","uav:browseName":"c:Consumer",
                  "properties":{"Reading":{"uav:dataTypeDefinition":{{{reference}}}}}
                }
                """));
        }
    }
}
