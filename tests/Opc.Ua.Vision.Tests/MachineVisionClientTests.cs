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
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins independent released-only discovery without a provisional Vision namespace.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class MachineVisionClientTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DiscoversEitherReleasedModelWithoutDraftNamespaceAsync(bool assets)
        {
            var harness = new VisionSessionHarness(includeDraftNamespace: false);
            string uri = assets ? MachineVisionModels.AssetNamespaceUri : MachineVisionModels.ControlNamespaceUri;
            ushort ns = harness.NamespaceUris.GetIndexOrAppend(uri);
            harness.MessageContext.NamespaceUris.GetIndexOrAppend(uri);
            var instance = new NodeId("released", ns);
            ExpandedNodeId type = assets
                ? MachineVision.AMCM.ObjectTypeIds.VisionSystemAssetType
                : MachineVision.ObjectTypeIds.VisionSystemType;
            harness.AddBrowse(global::Opc.Ua.ObjectIds.ObjectsFolder,
            [
                new ReferenceDescription
                {
                    NodeId = instance,
                    BrowseName = new QualifiedName("Released", ns),
                    DisplayName = new LocalizedText("Released"),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = type,
                    IsForward = true,
                    ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.Organizes
                }
            ]);
            MachineVisionClient client = harness.Client.MachineVision();
            var found = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in
                assets ? client.EnumerateAssetsAsync() : client.EnumerateSystemsAsync())
            {
                found.Add(entry);
            }
            Assert.That(harness.Client.IsVisionNamespaceAvailable, Is.False);
            Assert.That(client.SupportsAssets, Is.EqualTo(assets));
            Assert.That(client.SupportsControl, Is.EqualTo(!assets));
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].NodeId, Is.EqualTo(instance));
            Assert.That(found[0].TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(type, harness.NamespaceUris)));
        }

        [Test]
        public async Task AbsentReleasedModelsProduceEmptyDiscoveryAsync()
        {
            var harness = new VisionSessionHarness(includeDraftNamespace: false);
            MachineVisionClient client = harness.Client.MachineVision();
            var found = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in client.EnumerateSystemsAsync())
            {
                found.Add(entry);
            }
            await foreach (VisionNodeEntry entry in client.EnumerateAssetsAsync())
            {
                found.Add(entry);
            }
            Assert.That(found, Is.Empty);
            Assert.That(client.SupportsControl, Is.False);
            Assert.That(client.SupportsAssets, Is.False);
        }

        [Test]
        public void NullIdentifiersAndNonPositiveTraversalLimitsAreRejected()
        {
            var harness = new VisionSessionHarness(includeDraftNamespace: false);
            MachineVisionClient client = harness.Client.MachineVision();
            Assert.Throws<ArgumentNullException>(() => client.System(NodeId.Null));
            Assert.Throws<ArgumentNullException>(() => client.Asset(NodeId.Null));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.EnumerateSystemsAsync(maximumNodes: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.EnumerateAssetsAsync(maximumNodes: -1));
        }
    }
}
