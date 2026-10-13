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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Proves that a native NodeId Organizes link between two registry resources
    /// is retained by the public document converter and still names the target
    /// the other resource defines, even though it is not a document dependency.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotNativeOrganizesConversionTests
    {
        private const string DiNamespace = "http://opcfoundation.org/UA/DI/";

        /// <summary>
        /// A DeviceSet that Organizes a native DeviceFeatures node, with both
        /// documents in the registry, converts to a NodeSet whose forward
        /// Organizes reference names exactly the DI i=15034 target.
        /// </summary>
        [Test]
        public async Task NativeOrganizesTargetDefinedBySiblingResourceIsRetainedAsync()
        {
            byte[] features = Document("DeviceFeatures", "i=15034", null);
            byte[] deviceSet = Document("DeviceSet", "i=5001", "nsu=" + DiNamespace + ";i=15034");
            using var registry = new WotRegistryService();
            WotResource featuresResource = await UpsertAsync(registry, "opc-ua-di-devicefeatures", features)
                .ConfigureAwait(false);
            WotResource setResource = await UpsertAsync(registry, "opc-ua-di-deviceset", deviceSet)
                .ConfigureAwait(false);
            var contents = new Dictionary<string, ByteString>(System.StringComparer.Ordinal);
            foreach (WotResource resource in new[] { featuresResource, setResource })
            {
                WotResourceVersion version = resource.DefaultVersion!;
                contents[version.DigestHex] = ByteString.From(
                    resource == featuresResource ? features : deviceSet);
            }
            var converter = new WotNodeSetDocumentConverter(addressSpace: CreateCoreAddressSpace());

            WotConversionOutput output = await converter.ConvertAsync(
                setResource, ByteString.From(deviceSet), registry.Current, contents, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(output.Succeeded, Is.True, string.Join("; ", output.Errors));
            var namespaces = new NamespaceTable();
            foreach (string uri in output.NodeSet!.NamespaceUris!)
            {
                namespaces.Append(uri);
            }
            UANode root = output.NodeSet.Items!.Single(
                item => NodeId.Parse(item.NodeId!) == ExpandedNodeId.ToNodeId(output.RootNodeId, namespaces));
            Reference organizes = root.References!.Single(
                reference => reference.IsForward && reference.ReferenceType is "Organizes" or "i=35");
            Assert.That(organizes.Value, Is.Not.Null.And.Not.Empty);
            NodeId target = NodeId.Parse(organizes.Value!);

            Assert.That(namespaces.GetString(target.NamespaceIndex), Is.EqualTo(DiNamespace));
            Assert.That(target.TryGetValue(out uint identifier), Is.True);
            Assert.That(identifier, Is.EqualTo(15034u));
        }

        /// <summary>
        /// Builds the loaded-AddressSpace half of the WoT Binding Section 5.1.5
        /// local context for a Server holding the standard core model, so the
        /// documents' <c>ua:HasTypeDefinition</c> link to BaseObjectType (i=58)
        /// resolves instead of being reported as missing.
        /// </summary>
        private static AddressSpaceWotNodeResolver CreateCoreAddressSpace()
        {
            var namespaceUris = new NamespaceTable();
            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);

            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .Setup(m => m.GetNodeMetadataAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<object>(),
                    It.IsAny<BrowseResultMask>(),
                    It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, object handle, BrowseResultMask _, CancellationToken _) =>
                    new ValueTask<NodeMetadata>(new NodeMetadata(handle, (NodeId)handle)
                    {
                        NodeClass = NodeClass.ObjectType,
                        BrowseName = new QualifiedName("BaseObjectType")
                    }));

            var master = new Mock<IMasterNodeManager>();
            master
                .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId nodeId, CancellationToken _) =>
                    new ValueTask<(object?, IAsyncNodeManager?)>(
                        nodeId == Opc.Ua.ObjectTypeIds.BaseObjectType
                            ? (nodeId, nodeManager.Object)
                            : ((object?)null, (IAsyncNodeManager?)null)));
            master
                .Setup(m => m.BrowseAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<(ArrayOf<BrowseResult>, ArrayOf<DiagnosticInfo>)>((
                    new ArrayOf<BrowseResult>(new[] { new BrowseResult { StatusCode = StatusCodes.Good } }),
                    ArrayOf<DiagnosticInfo>.Empty)));

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.TypeTree).Returns(typeTable);
            server.Setup(s => s.NodeManager).Returns(master.Object);
            return new AddressSpaceWotNodeResolver(server.Object);
        }

        private static async Task<WotResource> UpsertAsync(
            WotRegistryService registry,
            string resourceId,
            byte[] content)
        {
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(content)
            }).ConfigureAwait(false);
            return registry.Current.AllResources().Single(resource => resource.ResourceId == resourceId);
        }

        private static byte[] Document(string title, string identifier, string? organizesHref)
        {
            string organizes = organizesHref is null
                ? string.Empty
                : $$""",{"href":"{{organizesHref}}","rel":"ua:Organizes","uav:refId":"i=35"}""";
            return Encoding.UTF8.GetBytes($$"""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1",
                    {"ns1":"{{DiNamespace}}","ua":"http://opcfoundation.org/UA/",
                     "uav":"http://opcfoundation.org/UA/WoT-Binding/"},
                    "http://opcfoundation.org/UA/WoT-Binding/v1.1/opc-ua-wot-binding.context.jsonld"],
                  "@type": "uav:object",
                  "links": [{"href":"i=58","rel":"ua:HasTypeDefinition"}{{organizes}}],
                  "title": "{{title}}",
                  "uav:bindingVersion": "1.1",
                  "uav:browseName": "ns1:{{title}}",
                  "uav:id": "nsu={{DiNamespace}};{{identifier}}"
                }
                """);
        }
    }
}
