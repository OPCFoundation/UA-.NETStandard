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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    public sealed class WotDocumentTypeBindingTests
    {
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public async Task DocumentReferencesBindRootsAndVariableAffordances(bool property, int referenceForm)
        {
            const string typeId = "nsu=urn:m0:type-definitions;i=5000";
            string annotation = property ? "uav:variableType" : "uav:objectType";
            using WotDocument type = CreateTypeDocument(annotation);
            string href = referenceForm switch
            {
                1 => "models:type.json",
                2 => "../type.json",
                _ => kTypeDocument
            };
            using WotDocument document = CreateDocumentBinding(property, href);
            Mock<IWotThingResolver> things = CreateThingResolver(type);
            var nodes = new WotDocumentNodeResolver([type]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, nodes).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            UANode instance = property
                ? result.Value!.Items!.OfType<UAVariable>().Single()
                : result.Value!.Items!.OfType<UAObject>().Single();
            Assert.That(instance.References!.Single(item => item.ReferenceType == "HasTypeDefinition").Value,
                Is.EqualTo(WotTestData.LocalNodeId(result.Value!, typeId)));
            Assert.That(result.Value!.Items!.OfType<UAObjectType>(), Is.Empty);
            Assert.That(result.Value!.Items!.OfType<UAVariableType>(), Is.Empty);
            things.Verify(resolver => resolver.ResolveThingAsync(
                kTypeDocument, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task UnresolvedDocumentBindingNeverQueriesTheAddressSpace(bool property, bool hasThingResolver)
        {
            using WotDocument document = CreateDocumentBinding(property, kTypeDocument);
            var nodes = new Mock<IWotNodeResolver>();
            nodes.Setup(resolver => resolver.ResolveByNodeIdAsync(kTypeDocument, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolvedNode?>(new WotResolvedNode(
                    "nsu=urn:m0:unrelated;i=9",
                    property ? WotExpectedNodeClass.VariableType : WotExpectedNodeClass.ObjectType)));
            var things = new Mock<IWotThingResolver>(MockBehavior.Strict);
            things.Setup(resolver => resolver.ResolveThingAsync(
                    kTypeDocument, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(WotResolverResult.NotFound));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, hasThingResolver ? things.Object : null, null, nodes.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.UnresolvedTypeBinding &&
                item.Severity == WotDiagnosticSeverity.Error), Is.True);
            nodes.Verify(resolver => resolver.ResolveByNodeIdAsync(
                kTypeDocument, It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentTypeBindingRejectsTheWrongNodeClass(bool property)
        {
            using WotDocument type = CreateTypeDocument(property ? "uav:objectType" : "uav:variableType");
            using WotDocument document = CreateDocumentBinding(property, kTypeDocument);
            Mock<IWotThingResolver> things = CreateThingResolver(type);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, new WotDocumentNodeResolver([type])).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.InvalidTypeBinding &&
                item.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DocumentBindingUsesTheTargetModelsGeneratedIdentity(bool property)
        {
            using WotDocument explicitType = CreateTypeDocument(property ? "uav:variableType" : "uav:objectType");
            JsonObject authored = JsonNode.Parse(explicitType.Utf8Json.Span)!.AsObject();
            authored.Remove("uav:id");
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(authored.ToJsonString()));
            WotConversionResult<UANodeSet> projectedType = WotNodeSetConverter.ToNodeSetResult(type);
            Assert.That(projectedType.Success, Is.True,
                string.Join("; ", projectedType.Diagnostics.Select(item => item.Message)));
            UANode target = property
                ? projectedType.Value!.Items!.OfType<UAVariableType>().Single()
                : projectedType.Value!.Items!.OfType<UAObjectType>().Single();
            var namespaces = new NamespaceTable();
            foreach (string uri in projectedType.Value!.NamespaceUris!)
            {
                namespaces.GetIndexOrAppend(uri);
            }
            string expected = NodeId.ToExpandedNodeId(NodeId.Parse(target.NodeId!), namespaces).ToString();
            using WotDocument document = CreateDocumentBinding(property, kTypeDocument);
            Mock<IWotThingResolver> things = CreateThingResolver(type);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, new WotDocumentNodeResolver([type])).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            UANode instance = property
                ? result.Value!.Items!.OfType<UAVariable>().Single()
                : result.Value!.Items!.OfType<UAObject>().Single();
            Assert.That(instance.References!.Single(item => item.ReferenceType == "HasTypeDefinition").Value,
                Is.EqualTo(WotTestData.LocalNodeId(result.Value!, expected)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeTypeDocumentWithoutReadableIdBindsItsActualRoot(bool envelope)
        {
            UANodeSet native = WotTestData.CreateReconstructableNodeSet();
            using WotDocument exported = WotNodeSetConverter.FromNodeSet(native,
                options: new WotNodeSetConverterOptions
                {
                    PreservationMode = envelope ? WotNodeSetPreservationMode.Always : WotNodeSetPreservationMode.Never
                });
            JsonObject authored = JsonNode.Parse(exported.Utf8Json.Span)!.AsObject();
            authored.Remove("uav:id");
            authored["id"] = kTypeDocument;
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(authored.ToJsonString()));
            Assert.That(WotNodeSetConverter.TakesRestorePath(type), Is.True);
            WotConversionResult<UANodeSet> restored = WotNodeSetConverter.ToNodeSetResult(type);
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            ExpandedNodeId expected = WotNodeSetConverter.TrySelectProjectionRoot(restored.Value!);
            Assert.That(expected.IsNull, Is.False);
            using WotDocument document = CreateDocumentBinding(false, kTypeDocument);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, CreateThingResolver(type).Object, null, new WotDocumentNodeResolver([type]))
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            UAObject instance = result.Value!.Items!.OfType<UAObject>().Single();
            Assert.That(instance.References!.Single(item => item.ReferenceType == "HasTypeDefinition").Value,
                Is.EqualTo(WotTestData.LocalNodeId(result.Value!, expected.ToString())));
        }

        private static WotDocument CreateDocumentBinding(bool property, string href)
        {
            JsonObject root = JsonNode.Parse("""
                {
                  "@type":["Thing","uav:object"],
                  "id":"urn:m0:type-instance",
                  "title":"Instance",
                  "uav:id":"nsu=urn:m0:type-instances;i=1"
                }
                """)!.AsObject();
            var context = new JsonObject
            {
                ["models"] = "https://models.example/",
                ["@base"] = "https://models.example/instances/"
            };
            JsonObject target = root;
            if (property)
            {
                root["@context"] = new JsonObject
                {
                    ["models"] = "https://wrong.example/",
                    ["@base"] = "https://wrong.example/"
                };
                target = new JsonObject { ["type"] = "number", ["@context"] = context };
                root["properties"] = new JsonObject { ["Value"] = target };
            }
            else
            {
                root["@context"] = context;
            }
            target["links"] = new JsonArray(new JsonObject
            {
                ["rel"] = "ua:HasTypeDefinition",
                ["href"] = href
            });
            return WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
        }

        private static WotDocument CreateTypeDocument(string annotation)
        {
            return WotDocument.Parse(WotTestData.Utf8($$"""
                {
                  "@type":["tm:ThingModel","{{annotation}}"],
                  "id":"https://models.example/type.json",
                  "uav:id":"nsu=urn:m0:type-definitions;i=5000",
                  "uav:browseName":"nsu=urn:m0:type-definitions;CustomType"
                }
                """));
        }

        private static Mock<IWotThingResolver> CreateThingResolver(WotDocument type)
        {
            var things = new Mock<IWotThingResolver>(MockBehavior.Strict);
            things.Setup(resolver => resolver.ResolveThingAsync(
                    kTypeDocument, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(type.Utf8Json.ToArray())));
            return things;
        }

        private const string kTypeDocument = "https://models.example/type.json";
    }
}
