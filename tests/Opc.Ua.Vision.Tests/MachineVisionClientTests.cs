// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            harness.AddBrowse(ObjectIds.ObjectsFolder,
            [
                new ReferenceDescription
                {
                    NodeId = instance,
                    BrowseName = new QualifiedName("Released", ns),
                    DisplayName = new LocalizedText("Released"),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = type,
                    IsForward = true,
                    ReferenceTypeId = ReferenceTypeIds.Organizes
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
