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

using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [Test]
        public async Task NativeInverseNonPlacementReferenceIsPreserved()
        {
            XDocument model = XDocument.Parse(s_model);
            model.Root!.Add(new XElement(s_nodes + "UAReferenceType",
                new XAttribute("NodeId", "ns=1;i=4020"), new XAttribute("BrowseName", "1:ConnectedTo"),
                new XElement(s_nodes + "DisplayName", "ConnectedTo"),
                new XElement(s_nodes + "InverseName", "ConnectedFrom"),
                new XElement(s_nodes + "References", Reference("i=45", "i=32", inverse: true))));
            model.Root.Add(new XElement(s_nodes + "UAObject",
                new XAttribute("NodeId", "ns=1;i=4030"), new XAttribute("BrowseName", "1:Peer"),
                new XElement(s_nodes + "DisplayName", "Peer"),
                new XElement(s_nodes + "References", Reference("i=40", "i=58"),
                    Reference("i=35", "i=85", inverse: true))));
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting));
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: nodes =>
                nodes.Root!.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference("ns=2;i=4020", "ns=2;i=4030", inverse: true)));
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            ArrayOf<ReferenceDescription> references = await ReferencesAsync(
                fixture, asset.AssetId, new NodeId(4020u, fixture.ModelNamespaceIndex), BrowseDirection.Inverse);
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, fixture.Session.NamespaceUris),
                Is.EqualTo(new NodeId(4030u, fixture.ModelNamespaceIndex)));
        }

        [TestCase(WotNodeSetPreservationMode.Always)]
        [TestCase(WotNodeSetPreservationMode.Never)]
        public async Task OwnedInverseReferencesRetireWithTheirNativeGraph(WotNodeSetPreservationMode mode)
        {
            XDocument model = CreateInverseReferenceModel();
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting));
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = NativeDocument(mode, "mapped", modify: nodes =>
                nodes.Root!.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference("ns=2;i=4020", "ns=2;i=4030", inverse: true)));
            var referenceType = new NodeId(4020u, fixture.ModelNamespaceIndex);
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            Assert.That((await ReferencesAsync(fixture, asset.AssetId, referenceType,
                BrowseDirection.Inverse)).Count, Is.EqualTo(1));

            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());

            Assert.That((await ReferencesAsync(fixture, asset.AssetId, referenceType,
                BrowseDirection.Inverse)).Count, Is.Zero);
            Assert.That((await ReferencesAsync(fixture, asset.AssetId, Ua.ReferenceTypeIds.Organizes,
                BrowseDirection.Inverse)).Count, Is.EqualTo(1));
            Assert.That((await ReferencesAsync(fixture, asset.AssetId, Ua.ReferenceTypeIds.HasInterface,
                BrowseDirection.Forward)).Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ExistingInverseReferenceIsNotOwnedByTheReplacementGraph()
        {
            XDocument model = CreateInverseReferenceModel();
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting));
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            var referenceType = new NodeId(4020u, fixture.ModelNamespaceIndex);
            var peer = new NodeId(4030u, fixture.ModelNamespaceIndex);
            await fixture.Server.NodeManager.AddReferencesAsync(asset.AssetId,
                [new NodeStateReference(referenceType, true, peer)]);
            Assert.That((await ReferencesAsync(fixture, asset.AssetId, referenceType,
                BrowseDirection.Inverse)).Count, Is.EqualTo(1));
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: nodes =>
                nodes.Root!.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference("ns=2;i=4020", "ns=2;i=4030", inverse: true)));
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());

            ArrayOf<ReferenceDescription> references = await ReferencesAsync(
                fixture, asset.AssetId, referenceType, BrowseDirection.Inverse);
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, fixture.Session.NamespaceUris), Is.EqualTo(peer));
        }

        [TestCase(false, "upload")]
        [TestCase(true, "upload")]
        [TestCase(false, "discovery")]
        [TestCase(true, "discovery")]
        [TestCase(false, "restore")]
        [TestCase(true, "restore")]
        public async Task ConflictingNativePlacementFailsBeforeProviderEffects(bool component, string path)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: nodes =>
                nodes.Root!.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference(component ? "i=47" : "i=35", "i=85", inverse: true)));
            if (path == "upload")
            {
                WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
                ByteString original = Document();
                await asset.UploadThingDescriptionAsync(original.Span.ToArray());
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await asset.UploadThingDescriptionAsync(content.Span.ToArray()))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
                Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(original.Span.ToArray()));
                Assert.That((await PropertiesAsync(asset)).Count, Is.EqualTo(1));
            }
            else
            {
                if (path == "discovery")
                {
                    fixture.Options.Discovery = new Discovery(content);
                    ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await fixture.Client.CreateAssetForEndpointAsync(
                            "mapped", "sim://opcua.test/wot/mapped"))!;
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
                }
                else
                {
                    await fixture.RestartManagerAsync(content);
                    Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(content));
                }
                Assert.That(fixture.Provider.Connects, Is.Zero);
                await AssertNoPublishedAssetsAsync(fixture);
            }
        }

        [Test]
        public async Task NativeCanonicalPlacementIsIdempotentAndSurvivesRetirement()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ArrayOf<ReferenceDescription> parents = await ReferencesAsync(
                fixture, asset.AssetId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Inverse);
            Assert.That(parents.Count, Is.EqualTo(1));
            NodeId parent = ExpandedNodeId.ToNodeId(parents[0].NodeId, fixture.Session.NamespaceUris);
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: nodes =>
            {
                nodes.Root!.Element(s_nodes + "NamespaceUris")!.Add(
                    new XElement(s_nodes + "Uri", fixture.Session.NamespaceUris.GetString(parent.NamespaceIndex)));
                nodes.Root.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference("i=35", parent.WithNamespaceIndex(3).ToString(), inverse: true));
            });
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());

            parents = await ReferencesAsync(
                fixture, asset.AssetId, Ua.ReferenceTypeIds.Organizes, BrowseDirection.Inverse);
            Assert.That(parents.Count, Is.EqualTo(1));
            Assert.That(ExpandedNodeId.ToNodeId(parents[0].NodeId, fixture.Session.NamespaceUris), Is.EqualTo(parent));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeInlineInverseReferenceUsesItsPreparedHierarchy(bool hierarchical)
        {
            XDocument model = CreateInverseReferenceModel();
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting));
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = InlineInverseReferenceDocument(hierarchical);
            if (hierarchical)
            {
                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await asset.UploadThingDescriptionAsync(content.Span.ToArray()))!;
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
                Assert.That(fixture.Provider.Connects, Is.Zero);
            }
            else
            {
                await asset.UploadThingDescriptionAsync(content.Span.ToArray());
                var referenceType = new NodeId("Assets/mapped/support/LocalRelation", asset.AssetId.NamespaceIndex);
                ArrayOf<ReferenceDescription> references = await ReferencesAsync(
                    fixture, asset.AssetId, referenceType, BrowseDirection.Inverse);
                Assert.That(references.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, fixture.Session.NamespaceUris),
                    Is.EqualTo(new NodeId(4030u, fixture.ModelNamespaceIndex)));
            }
        }

        [Test]
        public async Task ReplacementInverseReferenceUsesCandidateHierarchy()
        {
            XDocument model = CreateInverseReferenceModel();
            await using Fixture fixture = await Fixture.CreateAsync(
                modelXml: model.ToString(SaveOptions.DisableFormatting));
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString original = InlineInverseReferenceDocument(false);
            await asset.UploadThingDescriptionAsync(original.Span.ToArray());

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(InlineInverseReferenceDocument(true).Span.ToArray()))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(original.Span.ToArray()));
            var referenceType = new NodeId("Assets/mapped/support/LocalRelation", asset.AssetId.NamespaceIndex);
            Assert.That((await ReferencesAsync(fixture, asset.AssetId, referenceType,
                BrowseDirection.Inverse)).Count, Is.EqualTo(1));
        }

        private static ByteString InlineInverseReferenceDocument(bool hierarchical)
        {
            return NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: nodes =>
            {
                nodes.Root!.Add(new XElement(s_nodes + "UAReferenceType",
                    new XAttribute("NodeId", "ns=1;s=Assets/mapped/support/LocalRelation"),
                    new XAttribute("BrowseName", "1:LocalRelation"),
                    new XElement(s_nodes + "DisplayName", "LocalRelation"),
                    new XElement(s_nodes + "InverseName", "LocalInverseRelation"),
                    new XElement(s_nodes + "References",
                        Reference("i=45", hierarchical ? "i=33" : "i=32", inverse: true))));
                nodes.Root.Elements(s_nodes + "UAObject").Single().Element(s_nodes + "References")!
                    .Add(Reference("ns=1;s=Assets/mapped/support/LocalRelation", "ns=2;i=4030", inverse: true));
            });
        }

        private static XDocument CreateInverseReferenceModel()
        {
            XDocument model = XDocument.Parse(s_model);
            model.Root!.Add(new XElement(s_nodes + "UAReferenceType",
                new XAttribute("NodeId", "ns=1;i=4020"), new XAttribute("BrowseName", "1:ConnectedTo"),
                new XElement(s_nodes + "DisplayName", "ConnectedTo"),
                new XElement(s_nodes + "InverseName", "ConnectedFrom"),
                new XElement(s_nodes + "References", Reference("i=45", "i=32", inverse: true))));
            model.Root.Add(new XElement(s_nodes + "UAObject",
                new XAttribute("NodeId", "ns=1;i=4030"), new XAttribute("BrowseName", "1:Peer"),
                new XElement(s_nodes + "DisplayName", "Peer"),
                new XElement(s_nodes + "References", Reference("i=40", "i=58"),
                    Reference("i=35", "i=85", inverse: true))));
            return model;
        }
    }
}
