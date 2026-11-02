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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Guards the live materialization path: what the registry converter hands
    /// back has to survive being written, read and imported.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotMaterializationImportTests
    {
        [Test]
        public async Task ConvertedRegistryDocumentSurvivesSerializeReadImportAsync()
        {
            // IWotDocumentConverter produces a UANodeSet, the coordinator
            // writes it to XML, and the runtime NodeSet node manager reads and
            // imports that XML. A reference written as a bare name the document
            // never declares fails the last step with BadNodeIdInvalid, and
            // nothing before it notices.
            var converter = new WotNodeSetDocumentConverter();
            byte[] content = TestMaterialization.Tm("urn:test:materialization");

            var version = new WotResourceVersion(
                versionId: "v1",
                digest: WotContentDigest.Compute(content),
                contentLength: content.Length,
                contentType: "application/tm+json",
                format: "WoT-TM/1.0",
                createdAt: default,
                modifiedAt: default);
            var resource = new WotResource(
                groupId: WotRegistryGroups.ThingModels,
                resourceId: "materialization-tm",
                kind: WoTDocumentKindEnum.ThingModel,
                versions: ImmutableArray.Create(version),
                defaultVersionId: "v1");

            using var service = new WotRegistryService();

            WotConversionOutput output = await converter
                .ConvertAsync(
                    resource,
                    ByteString.From(content),
                    service.Current,
                    new Dictionary<string, ByteString>
                    {
                        [version.DigestHex] = ByteString.From(content)
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(
                output.Succeeded,
                Is.True,
                string.Join("; ", output.Errors));

            byte[] xml;
            using (var buffer = new MemoryStream())
            {
                output.NodeSet!.Write(buffer);
                xml = buffer.ToArray();
            }

            UANodeSet reread;
            using (var buffer = new MemoryStream(xml, writable: false))
            {
                reread = UANodeSet.Read(buffer)!;
            }
            Assert.That(reread, Is.Not.Null);

            var namespaces = new NamespaceTable();
            foreach (string namespaceUri in reread.NamespaceUris ?? [])
            {
                namespaces.GetIndexOrAppend(namespaceUri);
            }
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();

            Assert.DoesNotThrow(
                () => reread.Import(context, nodes),
                "A converted registry document has to be importable:" +
                Environment.NewLine + Encoding.UTF8.GetString(xml));
            Assert.That(nodes, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies that a serialized/imported action projection retains its
        /// original published owner candidates, not the generated local root
        /// or affordance identifiers.
        /// </summary>
        [Test]
        public async Task ImportedPublishedActionKeepsSourceComponentOfAuthority()
        {
            byte[] content = Encoding.UTF8.GetBytes(
                /*lang=json,strict*/ """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    { "uav": "http://opcfoundation.org/UA-WoT-Binding/" }
                  ],
                  "@type": "Thing",
                  "title": "Original published action",
                  "uav:id": "nsu=urn:source;s=Owner",
                  "actions": {
                    "run": {
                      "uav:componentOf": ["nsu=urn:source;s=Owner"],
                      "forms": [{
                        "href": "opc.tcp://source.example:4840",
                        "op": "invokeaction",
                        "uav:id": "nsu=urn:source;s=Run"
                      }]
                    }
                  }
                }
                """);

            using WotDocument projection = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"uav":"http://opcfoundation.org/UA-WoT-Binding/"}],
                  "@type":["Thing","uav:projection"],
                  "title":"Imported action projection",
                  "uav:id":"nsu=urn:local;s=ProjectionRoot",
                  "uav:scenario":"urn:scenario:published-import",
                  "uav:projects":[{
                    "uav:sourceName":"source",
                    "href":"urn:source:td",
                    "type":"application/td+json",
                    "uav:routing":"source",
                    "uav:selectAll":true
                  }]
                }
                """));
            var resolver = new WotProjectionResolver(new PublishedSourceResolver(content));
            WotConversionResult<WotDocument> resolved = await resolver.ResolveAsync(projection).ConfigureAwait(false);
            Assert.That(resolved.Success, Is.True, string.Join("; ", resolved.Diagnostics));
            using WotDocument document = resolved.Value!;
            WotConversionResult<UANodeSet> converted = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(converted.HasErrors, Is.False, string.Join("; ", converted.Diagnostics));
            UANodeSet nodeSet = converted.Value!;
            Assert.That(nodeSet.NamespaceUris, Is.Not.Null.And.Not.Empty);

            byte[] xml;
            using (var buffer = new MemoryStream())
            {
                nodeSet.Write(buffer);
                xml = buffer.ToArray();
            }

            UANodeSet imported;
            using (var buffer = new MemoryStream(xml, writable: false))
            {
                imported = UANodeSet.Read(buffer)!;
            }
            var namespaces = new NamespaceTable();
            foreach (string namespaceUri in imported.NamespaceUris ?? [])
            {
                namespaces.GetIndexOrAppend(namespaceUri);
            }
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var importedNodes = new NodeStateCollection();
            imported.Import(context, importedNodes);
            Assert.That(importedNodes, Is.Not.Empty);

            ExpandedNodeId projectionRoot = WotNodeSetConverter.TrySelectProjectionRoot(imported);
            ArrayOf<WotProjectedAffordance> localAffordances =
                WotNodeSetConverter.ResolveAffordanceNodes(document, imported, projectionRoot)
                    .ConvertAll(WotProjectedAffordance.FromConverted);
            Assert.That(localAffordances.Count, Is.EqualTo(1));
            WotProjectedAffordance localAction = localAffordances[0];
            Assert.That(localAction.NodeId, Is.Not.EqualTo("nsu=urn:source;s=Run"));
            Assert.That(localAction.OwnerNodeId, Is.Not.EqualTo("nsu=urn:source;s=Owner"));

            var selection = new WotBindingSelectionContext(
                ["opc.opcua"], [OpcUaBindingPlanner.BindingUri], WotBindingProcessingMode.PublishedOneZero);
            WotBindingPlanRequest request = WotBindingPlanRequest.FromDocument(
                    "imported-action", WoTDocumentKindEnum.ThingDescription, document, selection: selection)
                .WithProjectedAffordances(localAffordances)
                .WithProjectionRoot(projectionRoot);
            WotBindingCompilation compilation = new OpcUaBindingPlanner().Compile(
                request.Forms.Single(),
                request.CreateContext(WotPayloadCodecRegistry.Default, WotBindingBounds.Default));

            Assert.That(request.Selection.ProcessingMode, Is.EqualTo(WotBindingProcessingMode.PublishedOneZero));
            Assert.That(compilation.HasErrors, Is.False, string.Join("; ", compilation.Diagnostics));
            WotCompiledForm sourceAction = compilation.Entries.Single();
            Assert.That(sourceAction.Addressing.Metadata["bindingProcessingMode"], Is.EqualTo("published-one-zero"));
            Assert.That(sourceAction.Addressing.Metadata["sourceCallReceiver"], Is.EqualTo("componentOf"));
            Assert.That(sourceAction.Addressing.Metadata["componentOfCandidates"],
                Is.EqualTo("[\"nsu=urn:source;s=Owner\"]"));
            Assert.That(sourceAction.Addressing.Metadata.ContainsKey("callObjectId"), Is.False);
            Assert.That(sourceAction.Addressing.Metadata["componentOfCandidates"].Contains(
                localAction.NodeId, StringComparison.Ordinal),
                Is.False);
            Assert.That(sourceAction.Addressing.Metadata["componentOfCandidates"].Contains(
                localAction.OwnerNodeId, StringComparison.Ordinal),
                Is.False);
        }

        /// <summary>
        /// Supplies the original published TD to the real projection resolver.
        /// </summary>
        private sealed class PublishedSourceResolver(byte[] content) : IWotThingResolver
        {
            /// <inheritdoc/>
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string href, WotResolutionContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(href, Is.EqualTo("urn:source:td"));
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(content));
            }
        }
    }
}
