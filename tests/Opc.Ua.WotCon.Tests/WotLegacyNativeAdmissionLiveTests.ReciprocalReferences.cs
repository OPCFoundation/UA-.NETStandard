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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase(WotNodeSetPreservationMode.Always, false, false)]
        [TestCase(WotNodeSetPreservationMode.Always, false, true)]
        [TestCase(WotNodeSetPreservationMode.Always, true, false)]
        [TestCase(WotNodeSetPreservationMode.Always, true, true)]
        [TestCase(WotNodeSetPreservationMode.Never, false, false)]
        [TestCase(WotNodeSetPreservationMode.Never, false, true)]
        [TestCase(WotNodeSetPreservationMode.Never, true, false)]
        [TestCase(WotNodeSetPreservationMode.Never, true, true)]
        public async Task NativeRootReferencePublishesPeerCounterpart(
            WotNodeSetPreservationMode mode, bool inverse, bool sameManager)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager);
            ByteString content = PeerReferenceDocument(mode, inverse, sameManager);

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task NativeReciprocalEdgesRetireIndependently(
            bool sameManager, bool existingOwner, bool existingPeer)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager);
            var referenceType = new NodeId(4020u, fixture.ModelNamespaceIndex);
            if (existingOwner)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(asset.AssetId,
                    [new NodeStateReference(referenceType, true, peer)]);
            }
            if (existingPeer)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(peer,
                    [new NodeStateReference(referenceType, false, asset.AssetId)]);
            }
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingPeer);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingOwner);

            await asset.UploadThingDescriptionAsync(
                PeerReferenceDocument(WotNodeSetPreservationMode.Always, true, sameManager).Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);

            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingPeer);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingOwner);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeReciprocalOwnershipSurvivesSameEdgeReplacement(bool inverse, bool sameManager)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager);
            ByteString original = PeerReferenceDocument(WotNodeSetPreservationMode.Always, inverse, sameManager);
            ByteString replacement = PeerReferenceDocument(
                WotNodeSetPreservationMode.Always, inverse, sameManager, replacement: true);
            Assert.That(replacement, Is.Not.EqualTo(original));
            await asset.UploadThingDescriptionAsync(original.Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);

            await asset.UploadThingDescriptionAsync(replacement.Span.ToArray());

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(replacement.Span.ToArray()));
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: false);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: false);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task NativeReciprocalRetiresWhenAssetIsDeleted(
            bool inverse, bool sameManager, bool existingPeer)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager);
            if (existingPeer)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(peer,
                    [new NodeStateReference(new NodeId(4020u, fixture.ModelNamespaceIndex), !inverse, asset.AssetId)]);
            }
            await asset.UploadThingDescriptionAsync(
                PeerReferenceDocument(WotNodeSetPreservationMode.Always, inverse, sameManager).Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse, present: true);

            await fixture.Client.DeleteAssetAsync(asset.AssetId);
            WotAssetClient recreated = await fixture.Client.CreateAssetAsync("mapped");

            // Reusing the target identity makes a retained dangling edge observable through Browse.
            Assert.That(recreated.AssetId, Is.EqualTo(asset.AssetId));
            await AssertPeerReferenceAsync(fixture, recreated.AssetId, peer, inverse, existingPeer);
            await AssertOwnerReferenceAsync(fixture, recreated.AssetId, peer, inverse, present: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeDeletionPreservesPreexistingPeerAndOwnerRelation(bool sameManager)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager);
            var referenceType = new NodeId(4020u, fixture.ModelNamespaceIndex);
            await fixture.Server.NodeManager.AddReferencesAsync(peer,
                [new NodeStateReference(referenceType, false, asset.AssetId)]);
            await fixture.Server.NodeManager.AddReferencesAsync(asset.AssetId,
                [new NodeStateReference(referenceType, true, peer)]);
            await asset.UploadThingDescriptionAsync(
                PeerReferenceDocument(WotNodeSetPreservationMode.Always, true, sameManager).Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);

            await fixture.Client.DeleteAssetAsync(asset.AssetId);
            WotAssetClient recreated = await fixture.Client.CreateAssetAsync("mapped");

            Assert.That(recreated.AssetId, Is.EqualTo(asset.AssetId));
            await AssertPeerReferenceAsync(fixture, recreated.AssetId, peer, inverse: true, present: true);
            await AssertOwnerReferenceAsync(fixture, recreated.AssetId, peer, inverse: true, present: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativePeerReferenceSurvivesTargetManagerReload(bool existingPeer)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager: false);
            if (existingPeer)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(peer,
                    [new NodeStateReference(new NodeId(4020u, fixture.ModelNamespaceIndex), false, asset.AssetId)]);
            }
            await asset.UploadThingDescriptionAsync(
                PeerReferenceDocument(WotNodeSetPreservationMode.Always, true, false).Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);

            await fixture.ReloadPeerModelAsync();

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingPeer);
            await fixture.ReloadPeerModelAsync();
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingPeer);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativePeerReferenceSurvivesAssetManagerRestart(bool existingPeer)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await CreatePeerAsync(fixture, sameManager: false);
            if (existingPeer)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(peer,
                    [new NodeStateReference(new NodeId(4020u, fixture.ModelNamespaceIndex), false, asset.AssetId)]);
            }
            ByteString content = PeerReferenceDocument(WotNodeSetPreservationMode.Always, true, false);
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);

            await fixture.RestartManagerAsync();

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            WotAssetClient restored = await fixture.Client.OpenAssetAsync(asset.AssetId);
            Assert.That(await restored.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            await restored.UploadThingDescriptionAsync(Document().Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, existingPeer);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task NativePeerOwnershipSurvivesRepeatedAssetManagerRetirement(
            bool inverse, bool existingOwner, bool existingPeer)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            var factory = new DirectAssetManagerFactory(fixture.Options);
            await fixture.RestartManagerAsync(factory: factory);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId assetId = asset.AssetId;
            NodeId unboundType = await fixture.TypeAsync(assetId);
            NodeId peer = await CreatePeerAsync(fixture, sameManager: false);
            var referenceType = new NodeId(4020u, fixture.ModelNamespaceIndex);
            if (existingOwner)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(assetId,
                    [new NodeStateReference(referenceType, inverse, peer)]);
            }
            if (existingPeer)
            {
                await fixture.Server.NodeManager.AddReferencesAsync(peer,
                    [new NodeStateReference(referenceType, !inverse, assetId)]);
            }
            await AssertPeerReferenceAsync(fixture, assetId, peer, inverse, existingPeer);
            await AssertOwnerReferenceAsync(fixture, assetId, peer, inverse, existingOwner);
            ByteString content = PeerReferenceDocument(WotNodeSetPreservationMode.Always, inverse, false);
            ByteString replacement = Document(binding: "\"@type\":\"Thing\",");

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            for (int restart = 0; restart <= 2; restart++)
            {
                if (restart > 0)
                {
                    await fixture.RestartManagerAsync(factory: factory);
                    asset = await fixture.Client.OpenAssetAsync(assetId);
                }
                Assert.That(asset.AssetId, Is.EqualTo(assetId));
                Assert.That(fixture.Provider.Connects, Is.EqualTo(restart + 1));
                Assert.That(await fixture.TypeAsync(assetId),
                    Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
                Assert.That((await PropertiesAsync(asset)).Count, Is.EqualTo(1));
                Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
                Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(content));
                await AssertPeerReferenceAsync(fixture, assetId, peer, inverse, present: true);
                await AssertOwnerReferenceAsync(fixture, assetId, peer, inverse, present: true);
            }

            await asset.UploadThingDescriptionAsync(replacement.Span.ToArray());

            Assert.That(fixture.Provider.Connects, Is.EqualTo(4));
            Assert.That(await fixture.TypeAsync(assetId), Is.EqualTo(unboundType));
            Assert.That((await PropertiesAsync(asset)).Count, Is.EqualTo(1));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(replacement.Span.ToArray()));
            Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(replacement));
            await AssertPeerReferenceAsync(fixture, assetId, peer, inverse, existingPeer);
            await AssertOwnerReferenceAsync(fixture, assetId, peer, inverse, present: false);

            await fixture.RestartManagerAsync(factory: factory);

            WotAssetClient restored = await fixture.Client.OpenAssetAsync(assetId);
            Assert.That(restored.AssetId, Is.EqualTo(assetId));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(5));
            Assert.That(await fixture.TypeAsync(assetId), Is.EqualTo(unboundType));
            Assert.That((await PropertiesAsync(restored)).Count, Is.EqualTo(1));
            Assert.That(await restored.DownloadThingDescriptionAsync(), Is.EqualTo(replacement.Span.ToArray()));
            Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(replacement));
            await AssertPeerReferenceAsync(fixture, assetId, peer, inverse, existingPeer);
            await AssertOwnerReferenceAsync(fixture, assetId, peer, inverse, present: false);
        }

        [TestCase(true, false, TestName = "NativePeerPublicationUnsupportedIsReported")]
        [TestCase(false, false, TestName = "NativePeerPublicationRejectedIsReported")]
        [TestCase(false, true, TestName = "NativePeerPublicationExceptionIsReported")]
        public async Task NativePeerPublicationFailureIsNotReportedAsSuccess(bool unsupported, bool throws)
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            StatusCode status = unsupported ? StatusCodes.BadNotSupported : StatusCodes.BadResourceUnavailable;
            NodeId peer = await fixture.AddRejectingPeerAsync(status, throws);
            ByteString content = PeerReferenceDocument(
                WotNodeSetPreservationMode.Always, true, false, rejectingPeer: true);

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(content.Span.ToArray()))!;

            Assert.That(failure.StatusCode, Is.EqualTo(status));
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: false);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: false);
        }

        [Test]
        public async Task NativePeerPublicationSupportsLifecycleAdapter()
        {
            await using Fixture fixture = await CreatePeerFixtureAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId peer = await fixture.AddRejectingPeerAsync(StatusCodes.Good, throws: false, adapted: true);
            await asset.UploadThingDescriptionAsync(PeerReferenceDocument(
                WotNodeSetPreservationMode.Always, true, false, rejectingPeer: true).Span.ToArray());

            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            await AssertOwnerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: true);
            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());
            await AssertPeerReferenceAsync(fixture, asset.AssetId, peer, inverse: true, present: false);
        }

        private static Task<Fixture> CreatePeerFixtureAsync()
        {
            return Fixture.CreateAsync(modelXml: CreateInverseReferenceModel().ToString(SaveOptions.DisableFormatting));
        }

        private static async Task<NodeId> CreatePeerAsync(Fixture fixture, bool sameManager)
        {
            return sameManager
                ? (await fixture.Client.CreateAssetAsync("peer")).AssetId
                : new NodeId(4030u, fixture.ModelNamespaceIndex);
        }

        private static ByteString PeerReferenceDocument(
            WotNodeSetPreservationMode mode, bool inverse, bool sameManager,
            bool replacement = false, bool rejectingPeer = false)
        {
            return NativeDocument(mode, "mapped", modify: nodes =>
            {
                string peer = sameManager ? "ns=1;s=Assets/peer" : "ns=2;i=4030";
                if (rejectingPeer)
                {
                    nodes.Root!.Element(s_nodes + "NamespaceUris")!
                        .Add(new XElement(s_nodes + "Uri", RejectingPeerNamespaceUri));
                    peer = "ns=3;i=4040";
                }
                XElement root = nodes.Root!.Elements(s_nodes + "UAObject").Single();
                root.Element(s_nodes + "References")!.Add(Reference("ns=2;i=4020", peer, inverse));
                if (replacement)
                {
                    root.Element(s_nodes + "DisplayName")!.Value = "Replacement mapped asset";
                }
            });
        }

        private static async Task AssertPeerReferenceAsync(
            Fixture fixture, NodeId asset, NodeId peer, bool inverse, bool present)
        {
            ArrayOf<ReferenceDescription> references = await ReferencesAsync(
                fixture, peer, new NodeId(4020u, fixture.ModelNamespaceIndex),
                inverse ? BrowseDirection.Forward : BrowseDirection.Inverse);
            Assert.That(references.Count, Is.EqualTo(present ? 1 : 0), "Peer-first native Browse");
            if (present)
            {
                Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, fixture.Session.NamespaceUris),
                    Is.EqualTo(asset));
            }
        }

        private static async Task AssertOwnerReferenceAsync(
            Fixture fixture, NodeId asset, NodeId peer, bool inverse, bool present)
        {
            ArrayOf<ReferenceDescription> references = await ReferencesAsync(
                fixture, asset, new NodeId(4020u, fixture.ModelNamespaceIndex),
                inverse ? BrowseDirection.Inverse : BrowseDirection.Forward);
            Assert.That(references.Count, Is.EqualTo(present ? 1 : 0), "Owner-side native Browse");
            if (present)
            {
                Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, fixture.Session.NamespaceUris),
                    Is.EqualTo(peer));
            }
        }

        private sealed partial class Fixture
        {
            public async Task ReloadPeerModelAsync()
            {
                NodeManagerRegistration registration = m_server.NodeManagerLifecycle.Registrations.ToList()
                    .Single(candidate => candidate.NamespaceUris.Contains(ModelUri));
                await m_server.NodeManagerLifecycle.ReloadRuntimeNodeSetAsync(registration,
                    new RuntimeNodeSetOptions
                    {
                        Sources = [RuntimeNodeSetSource.FromStream("R42PeerReload",
                            _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(
                                CreateInverseReferenceModel().ToString(SaveOptions.DisableFormatting)), false)),
                            [ModelUri])]
                    }, null);
            }

            public async Task<NodeId> AddRejectingPeerAsync(StatusCode status, bool throws, bool adapted = false)
            {
                await m_server.NodeManagerLifecycle.AddAsync(new RejectingPeerFactory(status, throws, adapted), null);
                await Session.FetchNamespaceTablesAsync();
                return new NodeId(4040u, Session.NamespaceUris.GetIndexOrAppend(RejectingPeerNamespaceUri));
            }
        }

        private sealed class RejectingPeerFactory(StatusCode status, bool throws, bool adapted)
            : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [RejectingPeerNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var manager = new RejectingPeerNodeManager(server, configuration, status, throws);
                return new ValueTask<IAsyncNodeManager>(
                    adapted ? manager.SyncNodeManager.ToAsyncNodeManager() : manager);
            }
        }

        private sealed class DirectAssetManagerFactory(WotConnectivityServerOptions options) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [options.AssetNamespaceUri, Namespaces.WotCon];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(new WotConnectivityNodeManager(server, configuration, options));
            }
        }

        private sealed class RejectingPeerNodeManager : AsyncCustomNodeManager
        {
            public RejectingPeerNodeManager(
                IServerInternal server, ApplicationConfiguration configuration, StatusCode status, bool throws)
                : base(server, configuration, server.Telemetry.CreateLogger<RejectingPeerNodeManager>(),
                    RejectingPeerNamespaceUri)
            {
                m_status = status;
                m_throws = throws;
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken);
                await AddPredefinedNodeAsync(SystemContext, new BaseObjectState(null)
                {
                    NodeId = new NodeId(4040u, NamespaceIndexes[0]),
                    BrowseName = new QualifiedName("RejectingPeer", NamespaceIndexes[0]),
                    DisplayName = new LocalizedText("RejectingPeer"),
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
                }, cancellationToken);
            }

            public override ValueTask<ServiceResult> AddReferenceAsync(
                OperationContext context, AddReferencesItem item, CancellationToken cancellationToken = default)
            {
                if (m_status == StatusCodes.Good)
                {
                    return base.AddReferenceAsync(context, item, cancellationToken);
                }
                if (m_throws)
                {
                    throw new ServiceResultException(m_status, "Injected reciprocal publication failure.");
                }
                return new ValueTask<ServiceResult>(new ServiceResult(m_status));
            }

            private readonly StatusCode m_status;
            private readonly bool m_throws;
        }

        private const string RejectingPeerNamespaceUri = "urn:test:r42-rejecting-peer";
    }
}
