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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Client;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.AMB.Tests
{
    public sealed partial class AmbBoundedClientTests
    {
        [Test]
        public async Task NestedCrossServerSubAssetsAreEnumeratedAsync()
        {
            const string serverA = "urn:amb:server:a";
            const string serverB = "urn:amb:server:b";
            const string serverC = "urn:amb:server:c";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);

            var b1 = new NodeId("B-asset", 4);
            var c1 = new NodeId("C-asset", 4);
            Func<NodeId, IEnumerable<ReferenceDescription>> graphB = _ => [];
            await using RemoteManagedSession remoteB = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA, serverC],
                [b1],
                node => graphB(node)).ConfigureAwait(false);
            await using RemoteManagedSession remoteC = await CreateRemoteManagedSessionAsync(
                serverC,
                [InstanceNamespace],
                [serverC, serverA, serverB],
                [c1],
                _ => []).ConfigureAwait(false);

            graphB = node => node == b1 ? [Reference(RemoteNode(remoteB, serverC, c1))] : [];

            var localBrowsed = new List<NodeId>();
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset ? [Reference(RemoteNode(local, serverB, b1))] : [],
                localBrowsed);
            var resolutions = new List<string>();
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) =>
                {
                    resolutions.Add(serverUri);
                    return Task.FromResult(serverUri == serverB ? remoteB.Session : remoteC.Session);
                });

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(b1, remoteB.Session.NamespaceUris)),
                new AssetSubAssetReference(serverC, NodeId.ToExpandedNodeId(c1, remoteC.Session.NamespaceUris))
            }));
            Assert.That(resolutions, Is.EqualTo(new[] { serverB, serverC }));
            Assert.That(remoteB.BrowseNodes, Does.Contain(b1));
            Assert.That(remoteC.BrowseNodes, Does.Contain(c1));
            Assert.That(localBrowsed, Does.Not.Contain(b1));
            Assert.That(remoteB.BrowseNodes, Does.Not.Contain(c1));
        }

        [Test]
        public async Task RemoteNamespaceUriMapsToRemoteSessionIndexAsync()
        {
            const string serverA = "urn:amb:namespace-source";
            const string serverB = "urn:amb:namespace-target";
            const string modelNamespace = "urn:amb:model:remote";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            ushort sourceIndex = checked((ushort)local.Object.NamespaceUris.GetIndexOrAppend("urn:amb:model:source"));
            var remoteNode = new NodeId("translated", 0);
            var discoveredAssets = new List<NodeId>();
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                ["urn:amb:remote:padding:1", "urn:amb:remote:padding:2", "urn:amb:remote:padding:3",
                    "urn:amb:remote:padding:4", "urn:amb:remote:padding:5", modelNamespace, InstanceNamespace],
                [serverB, serverA],
                discoveredAssets,
                _ => []).ConfigureAwait(false);
            ushort targetIndex = checked((ushort)remote.Session.NamespaceUris.GetIndex(modelNamespace));
            remoteNode = new NodeId("translated", targetIndex);
            discoveredAssets.Add(remoteNode);
            Assert.That(sourceIndex, Is.Not.EqualTo(targetIndex),
                "The source's numeric namespace index must not be reused on the resolved session.");

            var localBrowsed = new List<NodeId>();
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset
                    ? [Reference(new ExpandedNodeId(
                        new NodeId("translated", sourceIndex),
                        modelNamespace,
                        ServerIndex(local.Object, serverB)))]
                    : [],
                localBrowsed);
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) => Task.FromResult(serverUri == serverB ? remote.Session : null!));

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(remoteNode, remote.Session.NamespaceUris))
            }));
            Assert.That(remote.BrowseNodes, Does.Contain(remoteNode));
            Assert.That(remote.BrowseNodes, Does.Not.Contain(new NodeId("translated", sourceIndex)));
        }

        [Test]
        public async Task UnknownRemoteNamespaceUriFailsMappingAsync()
        {
            const string serverA = "urn:amb:unknown-namespace-source";
            const string serverB = "urn:amb:unknown-namespace-target";
            const string unknownNamespace = "urn:amb:missing:remote";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA],
                [],
                _ => []).ConfigureAwait(false);
            var unknownNode = new NodeId("unmapped", 4);
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset
                    ? [Reference(new ExpandedNodeId(
                        unknownNode,
                        unknownNamespace,
                        ServerIndex(local.Object, serverB)))]
                    : [],
                new List<NodeId>());
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) => Task.FromResult(serverUri == serverB ? remote.Session : null!));

            ServiceResultException failure = await FailureAsync<ServiceResultException>(
                () => CollectSubAssetsAcrossServersAsync(client, s_asset)).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            Assert.That(remote.BrowseNodes, Does.Not.Contain(unknownNode),
                "An unmapped reference must fail before it is browsed using a coincidental numeric index.");
        }

        [Test]
        public async Task RepeatedRemoteLinkIsResolvedAndYieldedOnlyOnceAsync()
        {
            const string serverA = "urn:amb:repeat-source";
            const string serverB = "urn:amb:repeat-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            var target = new NodeId("repeated", 4);
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA],
                [target],
                _ => []).ConfigureAwait(false);
            var remoteReference = RemoteNode(local, serverB, target);
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset
                    ? [Reference(remoteReference), Reference(remoteReference)]
                    : [],
                new List<NodeId>());
            int resolutions = 0;
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (_, _) =>
                {
                    resolutions++;
                    return Task.FromResult(remote.Session);
                });

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(target, remote.Session.NamespaceUris))
            }));
            Assert.That(resolutions, Is.EqualTo(1));
            Assert.That(remote.BrowseNodes.Count(node => node == target), Is.EqualTo(1));
        }

        [Test]
        public async Task RemoteCycleBackToEarlierServerNodeTerminatesAsync()
        {
            const string serverA = "urn:amb:cycle-source";
            const string serverB = "urn:amb:cycle-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            var b1 = new NodeId("cycle-B", 4);
            Func<NodeId, IEnumerable<ReferenceDescription>> remoteGraph = _ => [];
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA],
                [b1],
                node => remoteGraph(node)).ConfigureAwait(false);
            remoteGraph = node => node == b1 ? [Reference(RemoteNode(remote, serverA, s_asset))] : [];
            var localBrowsed = new List<NodeId>();
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset ? [Reference(RemoteNode(local, serverB, b1))] : [],
                localBrowsed);
            int resolutions = 0;
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (_, _) =>
                {
                    resolutions++;
                    return Task.FromResult(remote.Session);
                });

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(b1, remote.Session.NamespaceUris))
            }));
            Assert.That(resolutions, Is.EqualTo(1), "The cycle to server A uses the already-owned root session.");
            Assert.That(remote.BrowseNodes.Count(node => node == b1), Is.EqualTo(1));
            Assert.That(localBrowsed.Count(node => node == s_asset), Is.EqualTo(1),
                "The seen server/node pair prevents the root from being queued again.");
        }

        [Test]
        public async Task SameNodeIdentifierOnDifferentServersIsNotCollapsedAsync()
        {
            const string serverA = "urn:amb:identity-source";
            const string serverB = "urn:amb:identity-b";
            const string serverC = "urn:amb:identity-c";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB, serverC);
            var sameNode = new NodeId("same-identifier", 4);
            await using RemoteManagedSession remoteB = await CreateRemoteManagedSessionAsync(
                serverB, [InstanceNamespace], [serverB, serverA, serverC], [sameNode], _ => [])
                .ConfigureAwait(false);
            await using RemoteManagedSession remoteC = await CreateRemoteManagedSessionAsync(
                serverC, [InstanceNamespace], [serverC, serverA, serverB], [sameNode], _ => [])
                .ConfigureAwait(false);
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset
                    ? [Reference(RemoteNode(local, serverB, sameNode)), Reference(RemoteNode(local, serverC, sameNode))]
                    : [],
                new List<NodeId>());
            var resolutions = new List<string>();
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) =>
                {
                    resolutions.Add(serverUri);
                    return Task.FromResult(serverUri == serverB ? remoteB.Session : remoteC.Session);
                });

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(sameNode, remoteB.Session.NamespaceUris)),
                new AssetSubAssetReference(serverC, NodeId.ToExpandedNodeId(sameNode, remoteC.Session.NamespaceUris))
            }));
            Assert.That(resolutions, Is.EqualTo(new[] { serverB, serverC }));
            Assert.That(remoteB.BrowseNodes.Count(node => node == sameNode), Is.EqualTo(1));
            Assert.That(remoteC.BrowseNodes.Count(node => node == sameNode), Is.EqualTo(1));
        }

        [Test]
        public async Task SameNamespaceIndexDifferentUrisRemainDistinctAsync()
        {
            const string serverA = "urn:amb:namespace-identity-source";
            const string serverB = "urn:amb:namespace-identity-target";
            const string firstNamespace = "urn:amb:model:first";
            const string secondNamespace = "urn:amb:model:second";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            ushort sourceIndex = checked((ushort)local.Object.NamespaceUris
                .GetIndexOrAppend("urn:amb:local:namespace"));
            var discoveredAssets = new List<NodeId>();
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                ["urn:amb:remote:padding", firstNamespace, secondNamespace, InstanceNamespace],
                [serverB, serverA],
                discoveredAssets,
                _ => []).ConfigureAwait(false);
            ushort firstIndex = checked((ushort)remote.Session.NamespaceUris.GetIndex(firstNamespace));
            ushort secondIndex = checked((ushort)remote.Session.NamespaceUris.GetIndex(secondNamespace));
            var firstTarget = new NodeId("same-name", firstIndex);
            var secondTarget = new NodeId("same-name", secondIndex);
            discoveredAssets.Add(firstTarget);
            discoveredAssets.Add(secondTarget);
            var resolutions = new List<string>();
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset
                    ? [
                        Reference(new ExpandedNodeId(
                            new NodeId("same-name", sourceIndex),
                            firstNamespace,
                            ServerIndex(local.Object, serverB))),
                        Reference(new ExpandedNodeId(
                            new NodeId("same-name", sourceIndex),
                            secondNamespace,
                            ServerIndex(local.Object, serverB)))
                    ]
                    : [],
                new List<NodeId>());
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) =>
                {
                    resolutions.Add(serverUri);
                    return Task.FromResult(serverUri == serverB ? remote.Session : null!);
                });

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                .ConfigureAwait(false);

            Assert.That(firstIndex, Is.Not.EqualTo(secondIndex));
            Assert.That(found, Is.EqualTo(new[]
            {
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(firstTarget, remote.Session.NamespaceUris)),
                new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(secondTarget, remote.Session.NamespaceUris))
            }));
            Assert.That(resolutions, Is.EqualTo(new[] { serverB }));
            Assert.That(remote.BrowseNodes, Does.Contain(firstTarget));
            Assert.That(remote.BrowseNodes, Does.Contain(secondTarget));
        }

        [Test]
        public async Task RemoteTraversalHonorsExactMaximumDepthAsync()
        {
            await using DepthTraversal traversal = await CreateDepthTraversalAsync(AmbClient.MaxDepth)
                .ConfigureAwait(false);

            List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(
                traversal.Client, s_asset).ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(AmbClient.MaxDepth));
            Assert.That(found[^1].NodeId, Is.EqualTo(
                NodeId.ToExpandedNodeId(
                    traversal.NodeAt(AmbClient.MaxDepth), traversal.RemoteC.Session.NamespaceUris)));
            Assert.That(traversal.RemoteB.BrowseNodes, Does.Contain(traversal.BNode));
            Assert.That(traversal.RemoteC.BrowseNodes, Does.Contain(traversal.NodeAt(AmbClient.MaxDepth)));
        }

        [Test]
        public async Task RemoteTraversalRejectsOneBeyondMaximumDepthAsync()
        {
            await using DepthTraversal traversal = await CreateDepthTraversalAsync(AmbClient.MaxDepth + 1)
                .ConfigureAwait(false);
            var yielded = new List<AssetSubAssetReference>();

            ServiceResultException failure = await FailureAsync<ServiceResultException>(async () =>
            {
                await foreach (AssetSubAssetReference reference in traversal.Client
                    .EnumerateSubAssetsAcrossServersAsync(s_asset).ConfigureAwait(false))
                {
                    yielded.Add(reference);
                }
            }).ConfigureAwait(false);

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(yielded, Has.Count.EqualTo(AmbClient.MaxDepth));
            Assert.That(yielded[^1].NodeId, Is.EqualTo(
                NodeId.ToExpandedNodeId(
                    traversal.NodeAt(AmbClient.MaxDepth), traversal.RemoteC.Session.NamespaceUris)));
            Assert.That(traversal.RemoteC.BrowseNodes, Does.Contain(traversal.NodeAt(AmbClient.MaxDepth)));
            Assert.That(traversal.RemoteC.BrowseNodes, Does.Not.Contain(traversal.NodeAt(AmbClient.MaxDepth + 1)),
                "The first over-depth child is rejected before it is queued or browsed.");
        }

        [Test]
        public async Task ResolvedManagedSessionRemainsBorrowedAfterTraversalAsync()
        {
            const string serverA = "urn:amb:borrowed-source";
            const string serverB = "urn:amb:borrowed-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            var target = new NodeId("borrowed-asset", 4);
            RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA],
                [target],
                _ => []).ConfigureAwait(false);
            try
            {
                SetupLocalRemoteTraversalBrowse(
                    local,
                    [],
                    node => node == s_asset ? [Reference(RemoteNode(local, serverB, target))] : [],
                    new List<NodeId>());
                AmbClient client = CreateRemoteTraversalClient(
                    local,
                    (serverUri, _) => Task.FromResult(serverUri == serverB ? remote.Session : null!));

                List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                    .ConfigureAwait(false);

                Assert.That(found, Is.EqualTo(new[]
                {
                    new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(target, remote.Session.NamespaceUris))
                }));
                Assert.That(remote.Session.Connected, Is.True);
                Assert.That(remote.Session.Disposed, Is.False);
                Assert.That(remote.CloseRequests, Is.Empty,
                    "Traversal borrows the resolver's session and must not send CloseSession.");

                BrowseResponse stillUsable = await remote.Session.BrowseAsync(
                    null,
                    null,
                    1,
                    [new BrowseDescription
                    {
                        NodeId = target,
                        ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                        BrowseDirection = BrowseDirection.Forward,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }],
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(stillUsable.ResponseHeader.ServiceResult), Is.True);
                Assert.That(remote.Session.Connected, Is.True);
                Assert.That(remote.Session.Disposed, Is.False);
                Assert.That(remote.CloseRequests, Is.Empty);
                Assert.That(remote.BrowseNodes.Count(node => node == target), Is.EqualTo(2),
                    "The post-traversal browse is dispatched through the still-open borrowed session.");
            }
            finally
            {
                await remote.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(remote.CloseRequests, Has.Count.EqualTo(1),
                "The owning test closes the real managed session using the scripted offline response.");
            Assert.That(remote.Session.Disposed, Is.True);
        }

        [TestCase(4, true)]
        [TestCase(3, false)]
        public async Task CrossServerDiscoveryAndTraversalShareReferenceBudgetAsync(int maximum, bool succeeds)
        {
            const string serverA = "urn:amb:budget-source";
            const string serverB = "urn:amb:budget-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            var target = new NodeId("budget-asset", 4);
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB, [InstanceNamespace], [serverB, serverA], [target], _ => []).ConfigureAwait(false);
            SetupLocalRemoteTraversalBrowse(
                local, [], node => node == s_asset ? [Reference(RemoteNode(local, serverB, target))] : [], []);
            var client = new AmbClient(local.Object, NUnitTelemetryContext.Create(), new AmbClientOptions
            {
                MaximumBrowseReferences = maximum,
                RemoteSessionResolver = (_, _) => Task.FromResult(remote.Session)
            });

            if (succeeds)
            {
                List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                    .ConfigureAwait(false);
                Assert.That(found, Is.EqualTo(new[]
                {
                    new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(target, remote.Session.NamespaceUris))
                }));
            }
            else
            {
                await AssertStatusAsync(
                    () => CollectSubAssetsAcrossServersAsync(client, s_asset),
                    StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
                Assert.That(remote.BrowseNodes, Does.Not.Contain(target));
            }
        }

        [Test]
        public async Task ResolvedSessionMustBelongToRequestedServerAsync()
        {
            const string serverA = "urn:amb:identity-check-source";
            const string serverB = "urn:amb:identity-check-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                "urn:amb:wrong-server", [InstanceNamespace], ["urn:amb:wrong-server"], [], _ => [])
                .ConfigureAwait(false);
            SetupRemoteTraversalBrowse(local, s_asset, RemoteNode(local, serverB, s_asset), []);
            AmbClient client = CreateRemoteTraversalClient(local, (_, _) => Task.FromResult(remote.Session));

            await AssertStatusAsync(
                () => CollectSubAssetsAcrossServersAsync(client, s_asset),
                StatusCodes.BadServerUriInvalid).ConfigureAwait(false);

            Assert.That(remote.BrowseRequests, Is.Empty);
            Assert.That(remote.Session.Disposed, Is.False);
        }

        [Test]
        public async Task CancellationWhileResolvingRemoteSessionPropagatesAsync()
        {
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, "urn:amb:cancel-source", "urn:amb:cancel-target");
            SetupRemoteTraversalBrowse(local, s_asset, RemoteNode(local, "urn:amb:cancel-target", s_asset), []);
            using var cancellation = new CancellationTokenSource();
            bool resolved = false;
            AmbClient client = CreateRemoteTraversalClient(local, (_, token) =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                resolved = true;
                cancellation.Cancel();
                return Task.FromCanceled<ManagedSession>(token);
            });

            await FailureAsync<OperationCanceledException>(
                () => CollectSubAssetsAcrossServersAsync(client, s_asset, cancellation.Token)).ConfigureAwait(false);

            Assert.That(resolved, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task RemoteIndexMustExistInResolvedNamespaceTableAsync(bool validIndex)
        {
            const string serverB = "urn:amb:namespace-index-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, "urn:amb:namespace-index-source", serverB);
            var target = new NodeId("remote-index", validIndex ? (ushort)4 : ushort.MaxValue);
            await using RemoteManagedSession remote = await CreateRemoteManagedSessionAsync(
                serverB, [InstanceNamespace], [serverB], [target], _ => []).ConfigureAwait(false);
            SetupRemoteTraversalBrowse(local, s_asset, RemoteNode(local, serverB, target), []);
            AmbClient client = CreateRemoteTraversalClient(local, (_, _) => Task.FromResult(remote.Session));

            if (validIndex)
            {
                List<AssetSubAssetReference> found = await CollectSubAssetsAcrossServersAsync(client, s_asset)
                    .ConfigureAwait(false);
                Assert.That(found, Is.EqualTo(new[]
                {
                    new AssetSubAssetReference(serverB, NodeId.ToExpandedNodeId(target, remote.Session.NamespaceUris))
                }));
            }
            else
            {
                await AssertStatusAsync(
                    () => CollectSubAssetsAcrossServersAsync(client, s_asset),
                    StatusCodes.BadNodeIdInvalid).ConfigureAwait(false);
                Assert.That(remote.BrowseNodes, Does.Not.Contain(target));
            }
        }

        private static void SetupLocalRemoteTraversalBrowse(
            Mock<ISession> session,
            IReadOnlyList<NodeId> discoveredAssets,
            Func<NodeId, IEnumerable<ReferenceDescription>> graph,
            List<NodeId> browsed)
        {
            var discoveryClient = CreateRemoteTraversalClient(session, null);
            NodeId byProductInstance = discoveryClient.GetCategoryId(AssetAliasCategory.ByProductInstanceUri);
            NodeId byAssetId = discoveryClient.GetCategoryId(AssetAliasCategory.ByAssetId);
            var alias = new NodeId("local-discovery-alias", checked((ushort)session.Object.NamespaceUris
                .GetIndexOrAppend(InstanceNamespace)));
            SetupBrowse(session, request =>
            {
                browsed.Add(request.NodeId);
                if (request.NodeId == byProductInstance)
                {
                    return new BrowseResult { References = [Reference(alias)] };
                }
                if (request.NodeId == byAssetId)
                {
                    return new BrowseResult();
                }
                if (request.NodeId == alias && request.ReferenceTypeId == Ua.ReferenceTypeIds.AliasFor)
                {
                    return new BrowseResult
                    {
                        References = discoveredAssets
                            .Select(node => Reference(new ExpandedNodeId(node, null, 0)))
                            .ToArrayOf()
                    };
                }
                return new BrowseResult { References = graph(request.NodeId).ToArrayOf() };
            });
        }

        private static async Task<RemoteManagedSession> CreateRemoteManagedSessionAsync(
            string serverUri,
            string[] namespaceUris,
            string[] serverUris,
            IReadOnlyList<NodeId> discoveredAssets,
            Func<NodeId, IEnumerable<ReferenceDescription>> graph)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            foreach (string namespaceUri in namespaceUris)
            {
                if (!string.Equals(namespaceUri, InstanceNamespace, StringComparison.Ordinal))
                {
                    messageContext.NamespaceUris.GetIndexOrAppend(namespaceUri);
                }
            }
            while (messageContext.NamespaceUris.Count < 4)
            {
                messageContext.NamespaceUris.GetIndexOrAppend(
                    $"urn:amb:remote:namespace-padding:{messageContext.NamespaceUris.Count}");
            }
            messageContext.NamespaceUris.GetIndexOrAppend(InstanceNamespace);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.AMB);
            messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            foreach (string uri in serverUris)
            {
                messageContext.ServerUris.GetIndexOrAppend(uri);
            }

            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://offline.invalid:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription { ApplicationUri = serverUri },
                UserIdentityTokens = [new UserTokenPolicy()]
            };
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            var endpoint = new ConfiguredEndpoint(
                null,
                endpointDescription,
                endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
            var channel = new Mock<ITransportChannel>(MockBehavior.Strict);
            channel.SetupGet(value => value.MessageContext).Returns(messageContext);
            channel.SetupGet(value => value.EndpointDescription).Returns(endpointDescription);
            channel.SetupGet(value => value.EndpointConfiguration)
                .Returns(endpointConfiguration);
            channel.SetupGet(value => value.ChannelThumbprint).Returns([]);
            channel.SetupGet(value => value.ClientChannelCertificate).Returns([]);
            channel.SetupGet(value => value.ServerChannelCertificate).Returns([]);
            channel.SetupGet(value => value.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
            channel.SetupProperty(value => value.OperationTimeout, 60_000);
            channel.Setup(value => value.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channel.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channel.Setup(value => value.Dispose());
            var browseRequests = new List<BrowseDescription>();
            var closeRequests = new List<CloseSessionRequest>();
            var inner = new SessionMock(channel, configuration, endpoint);
            inner.SetConnected();

            var discoveryClient = new AmbClient(inner, telemetry, new AmbClientOptions());
            NodeId byProductInstance = discoveryClient.GetCategoryId(AssetAliasCategory.ByProductInstanceUri);
            NodeId byAssetId = discoveryClient.GetCategoryId(AssetAliasCategory.ByAssetId);
            ushort aliasNamespace = checked((ushort)messageContext.NamespaceUris.GetIndex(InstanceNamespace));
            var alias = new NodeId("remote-discovery-alias", aliasNamespace);

            channel.Setup(value => value.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((request, cancellationToken) =>
                    new ValueTask<IServiceResponse>(DispatchRemoteRequest(
                        request,
                        cancellationToken,
                        browseRequests,
                        closeRequests,
                        byProductInstance,
                        byAssetId,
                        alias,
                        discoveredAssets,
                        graph)));

            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(ClassicSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<uint>(),
                    It.IsAny<IUserIdentity?>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(inner);

            ManagedSession managed = await ManagedSession.CreateAsync(
                configuration,
                endpoint,
                factory.Object,
                telemetry: telemetry,
                updateBeforeConnect: false).ConfigureAwait(false);
            return new RemoteManagedSession(managed, browseRequests, closeRequests);
        }

        private static IServiceResponse DispatchRemoteRequest(
            IServiceRequest request,
            CancellationToken cancellationToken,
            List<BrowseDescription> browseRequests,
            List<CloseSessionRequest> closeRequests,
            NodeId byProductInstance,
            NodeId byAssetId,
            NodeId alias,
            IReadOnlyList<NodeId> discoveredAssets,
            Func<NodeId, IEnumerable<ReferenceDescription>> graph)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is BrowseRequest browse)
            {
                BrowseResult[] results = new BrowseResult[browse.NodesToBrowse.Count];
                for (int ii = 0; ii < results.Length; ii++)
                {
                    BrowseDescription description = browse.NodesToBrowse[ii];
                    browseRequests.Add(description);
                    if (description.NodeId == byProductInstance)
                    {
                        results[ii] = new BrowseResult { References = [Reference(alias)] };
                    }
                    else if (description.NodeId == byAssetId)
                    {
                        results[ii] = new BrowseResult();
                    }
                    else if (description.NodeId == alias &&
                        description.ReferenceTypeId == Ua.ReferenceTypeIds.AliasFor)
                    {
                        results[ii] = new BrowseResult
                        {
                            References = discoveredAssets
                                .Select(node => Reference(new ExpandedNodeId(node, null, 0)))
                                .ToArrayOf()
                        };
                    }
                    else
                    {
                        results[ii] = new BrowseResult { References = graph(description.NodeId).ToArrayOf() };
                    }
                }
                return new BrowseResponse
                {
                    ResponseHeader = GoodResponseHeader(request),
                    Results = results.ToArrayOf(),
                    DiagnosticInfos = []
                };
            }
            if (request is CloseSessionRequest close)
            {
                closeRequests.Add(close);
                return new CloseSessionResponse
                {
                    ResponseHeader = GoodResponseHeader(request)
                };
            }
            throw new InvalidOperationException(
                $"Unexpected service request in the offline remote-session fixture: {request.GetType().Name}.");
        }

        private static ResponseHeader GoodResponseHeader(IServiceRequest request)
        {
            return new ResponseHeader
            {
                RequestHandle = request.RequestHeader.RequestHandle,
                ServiceResult = StatusCodes.Good
            };
        }

        private static ExpandedNodeId RemoteNode(Mock<ISession> source, string serverUri, NodeId node)
        {
            return new ExpandedNodeId(node, null, ServerIndex(source.Object, serverUri));
        }

        private static ExpandedNodeId RemoteNode(RemoteManagedSession source, string serverUri, NodeId node)
        {
            return new ExpandedNodeId(node, null, ServerIndex(source.Session, serverUri));
        }

        private static uint ServerIndex(ISession session, string serverUri)
        {
            int index = session.ServerUris.GetIndex(serverUri);
            if (index <= 0)
            {
                throw new InvalidOperationException($"Server URI '{serverUri}' is not in the session's table.");
            }
            return checked((uint)index);
        }

        private static async Task<DepthTraversal> CreateDepthTraversalAsync(int lastLevel)
        {
            const string serverA = "urn:amb:depth-source";
            const string serverB = "urn:amb:depth-intermediate";
            const string serverC = "urn:amb:depth-target";
            Mock<ISession> local = CreateSession();
            ConfigureRemoteTraversalSession(local, serverA, serverB);
            var bNode = new NodeId("depth-B", 4);
            ushort namespaceIndex = 4;
            var cNodes = Enumerable.Range(2, lastLevel - 1)
                .Select(level => new NodeId($"depth-{level}", namespaceIndex))
                .ToArray();
            RemoteManagedSession remoteB = null!;
            remoteB = await CreateRemoteManagedSessionAsync(
                serverB,
                [InstanceNamespace],
                [serverB, serverA, serverC],
                [bNode],
                node => node == bNode
                    ? [Reference(RemoteNode(remoteB, serverC, cNodes[0]))]
                    : []).ConfigureAwait(false);
            var remoteC = await CreateRemoteManagedSessionAsync(
                serverC,
                [InstanceNamespace],
                [serverC, serverA, serverB],
                cNodes,
                node =>
                {
                    int level = node.TryGetValue(out string? identifier) &&
                        identifier is not null &&
                        identifier.StartsWith("depth-", StringComparison.Ordinal) &&
                        int.TryParse(
                            identifier["depth-".Length..],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int parsed)
                            ? parsed
                            : lastLevel;
                    return level < lastLevel
                        ? [Reference(new ExpandedNodeId(
                            new NodeId($"depth-{level + 1}", namespaceIndex),
                            null,
                            0))]
                        : [];
                }).ConfigureAwait(false);
            var localBrowsed = new List<NodeId>();
            SetupLocalRemoteTraversalBrowse(
                local,
                [],
                node => node == s_asset ? [Reference(RemoteNode(local, serverB, bNode))] : [],
                localBrowsed);
            AmbClient client = CreateRemoteTraversalClient(
                local,
                (serverUri, _) => Task.FromResult(serverUri == serverB
                    ? remoteB.Session
                    : serverUri == serverC ? remoteC.Session : null!));
            return new DepthTraversal(client, remoteB, remoteC, bNode, namespaceIndex);
        }

        private sealed class RemoteManagedSession : IAsyncDisposable
        {
            public RemoteManagedSession(
                ManagedSession session,
                List<BrowseDescription> browseRequests,
                List<CloseSessionRequest> closeRequests)
            {
                Session = session;
                BrowseRequests = browseRequests;
                CloseRequests = closeRequests;
            }

            public ManagedSession Session { get; }

            public List<BrowseDescription> BrowseRequests { get; }

            public List<CloseSessionRequest> CloseRequests { get; }

            public IEnumerable<NodeId> BrowseNodes => BrowseRequests.Select(request => request.NodeId);

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    return Session.DisposeAsync();
                }
                return ValueTask.CompletedTask;
            }

            private int m_disposed;
        }

        private sealed class DepthTraversal : IAsyncDisposable
        {
            public DepthTraversal(
                AmbClient client,
                RemoteManagedSession remoteB,
                RemoteManagedSession remoteC,
                NodeId bNode,
                ushort namespaceIndex)
            {
                Client = client;
                RemoteB = remoteB;
                RemoteC = remoteC;
                BNode = bNode;
                NamespaceIndex = namespaceIndex;
            }

            public AmbClient Client { get; }

            public RemoteManagedSession RemoteB { get; }

            public RemoteManagedSession RemoteC { get; }

            public NodeId BNode { get; }

            public ushort NamespaceIndex { get; }

            public NodeId NodeAt(int level)
            {
                return new NodeId($"depth-{level}", NamespaceIndex);
            }

            public async ValueTask DisposeAsync()
            {
                await RemoteC.DisposeAsync().ConfigureAwait(false);
                await RemoteB.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
