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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Exercises collection-qualified, metadata-only projection of committed generations through
    /// the shared <see cref="XRegistryProjectionEngine"/>.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryCollectionProjectionTests
    {
        [Test]
        public async Task EqualGroupIdsInDistinctCollectionsProjectDistinctGroupsAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                1,
                Collection("endpoints", Group("endpoints", "orders", 2)),
                Collection("messagegroups", Group("messagegroups", "orders", 5)));

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            GroupCollectionState endpoints = harness.View("endpoints");
            GroupCollectionState messageGroups = harness.View("messagegroups");
            GroupState endpoint = harness.GroupIn(endpoints, "orders");
            GroupState messageGroup = harness.GroupIn(messageGroups, "orders");
            Assert.Multiple(() =>
            {
                Assert.That(endpoints, Is.Not.SameAs(messageGroups));
                Assert.That(endpoints.TypeDefinitionId,
                    Is.EqualTo(new NodeId(ObjectTypes.GroupCollectionType, 1)));
                Assert.That(endpoints.NodeId, Is.EqualTo(new NodeId("TestRegistry/endpoints", 1)));
                Assert.That(endpoints.BrowseName, Is.EqualTo(new QualifiedName("endpoints", 1)));
                Assert.That(endpoints.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(endpoints.Parent, Is.SameAs(harness.Registry));
                Assert.That(endpoints.CollectionName!.Value, Is.EqualTo("endpoints"));
                Assert.That(messageGroups.CollectionName!.Value, Is.EqualTo("messagegroups"));

                Assert.That(endpoint, Is.Not.SameAs(messageGroup));
                Assert.That(endpoint.BrowseName, Is.EqualTo(messageGroup.BrowseName));
                Assert.That(endpoint.GroupId!.Value, Is.EqualTo("orders"));
                Assert.That(messageGroup.GroupId!.Value, Is.EqualTo("orders"));
                Assert.That(endpoint.Xid!.Value, Is.EqualTo("/endpoints/orders"));
                Assert.That(messageGroup.Xid!.Value, Is.EqualTo("/messagegroups/orders"));
                Assert.That(endpoint.CollectionName!.Value, Is.EqualTo("endpoints"));
                Assert.That(messageGroup.CollectionName!.Value, Is.EqualTo("messagegroups"));
                Assert.That(endpoint.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/endpoints/orders", 1)));
                Assert.That(messageGroup.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/messagegroups/orders", 1)));
                Assert.That(endpoint.Parent, Is.SameAs(endpoints));
                Assert.That(messageGroup.Parent, Is.SameAs(messageGroups));
                Assert.That(endpoint.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(endpoint.TypeDefinitionId, Is.EqualTo(new NodeId(ObjectTypes.GroupType, 1)));
                Assert.That(endpoint.Epoch!.Value, Is.EqualTo(2u));
                Assert.That(messageGroup.Epoch!.Value, Is.EqualTo(5u));

                Assert.That(harness.Engine.EventSourceFor("/endpoints/orders"), Is.SameAs(endpoint));
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders"),
                    Is.SameAs(messageGroup));
                Assert.That(harness.Engine.EventSourceFor("/groups/orders"), Is.SameAs(harness.Registry));
                Assert.That(harness.Added.IndexOf(endpoints), Is.LessThan(harness.Added.IndexOf(endpoint)));
                Assert.That(harness.Registry.CreateGroup!.OnCallMethod2Async, Is.Null);
                Assert.That(harness.Duplicates, Is.Empty);
            });
        }

        [Test]
        public async Task MetadataResourceIsPublishedWithoutFileBehaviorAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                1,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 7) with
                    {
                        Name = "Order created",
                        VersionId = "1",
                        Labels = ImmutableSortedDictionary<string, string>.Empty.Add("env", "prod")
                    })));

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            FolderState messages = (FolderState)group.FindChild(harness.Context, s_messagesFolder)!;
            var message = (MetadataResourceState)messages.FindChild(
                harness.Context,
                new QualifiedName("created", 1))!;
            var children = new List<BaseInstanceState>();
            message.GetChildren(harness.Context, children);
            PropertyState<string>? label = message.Labels!.FindChild(
                harness.Context,
                new QualifiedName("env", 1)) as PropertyState<string>;
            Assert.Multiple(() =>
            {
                Assert.That(message, Is.Not.InstanceOf<FileState>());
                Assert.That(message.TypeDefinitionId,
                    Is.EqualTo(new NodeId(ObjectTypes.MetadataResourceType, 1)));
                Assert.That(message.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/messagegroups/orders/messages/created", 1)));
                Assert.That(message.BrowseName, Is.EqualTo(new QualifiedName("created", 1)));
                Assert.That(message.Parent, Is.SameAs(messages));
                Assert.That(message.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
                Assert.That(message.ResourceId!.Value, Is.EqualTo("created"));
                Assert.That(message.HasDocument!.Value, Is.False);
                Assert.That(message.MaxVersions!.Value, Is.EqualTo(1u));
                Assert.That(message.Xid!.Value, Is.EqualTo("/messagegroups/orders/messages/created"));
                Assert.That(message.Epoch!.Value, Is.EqualTo(7u));
                Assert.That(message.VersionId!.Value, Is.EqualTo("1"));
                Assert.That(message.Name!.Value, Is.EqualTo("Order created"));
                Assert.That(message.Description, Is.Null);
                Assert.That(label!.Value, Is.EqualTo("prod"));
                Assert.That(label.NodeId, Is.EqualTo(new NodeId(
                    "TestRegistry/messagegroups/orders/messages/created/labels/env",
                    1)));
                Assert.That(message.FindChild(harness.Context, new QualifiedName(Ua.BrowseNames.Open, 0)),
                    Is.Null);
                Assert.That(message.FindChild(harness.Context, new QualifiedName(Ua.BrowseNames.Write, 0)),
                    Is.Null);
                Assert.That(children.OfType<MethodState>().Select(method => method.BrowseName.Name),
                    Is.EqualTo(s_deleteMethodOnly));
                Assert.That(message.Metadata!.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.FileType));
                Assert.That(message.Metadata.Writable!.Value, Is.False);
                Assert.That(message.Metadata.UserWritable!.Value, Is.False);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(message));
                Assert.That(harness.Strategy.Configured
                    .Where(call => ReferenceEquals(call.Node, message))
                    .Select(call => call.Created), Is.EqualTo(s_createdOnce));
            });
        }

        [Test]
        public async Task CommittedEpochIsPublishedVerbatimAndUnaffectedSiblingIsUntouchedAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                10,
                Collection(
                    "messagegroups",
                    Group(
                        "messagegroups",
                        "orders",
                        5,
                        Message("orders", "created", 3) with { Name = "a" },
                        Message("orders", "shipped", 42))));
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            MetadataResourceState created = harness.MessageIn(group, "created");
            MetadataResourceState shipped = harness.MessageIn(group, "shipped");
            int addedBefore = harness.Added.Count;

            harness.Strategy.Committed = Generation(
                11,
                Collection(
                    "messagegroups",
                    Group(
                        "messagegroups",
                        "orders",
                        5,
                        Message("orders", "created", 9) with { Name = "b" },
                        Message("orders", "shipped", 42))));
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.MessageIn(group, "created"), Is.SameAs(created));
                Assert.That(harness.MessageIn(group, "shipped"), Is.SameAs(shipped));
                Assert.That(created.Epoch!.Value, Is.EqualTo(9u));
                Assert.That(created.Name!.Value, Is.EqualTo("b"));
                Assert.That(created.DisplayName.Text, Is.EqualTo("b"));
                Assert.That(shipped.Epoch!.Value, Is.EqualTo(42u));
                Assert.That(group.Epoch!.Value, Is.EqualTo(5u));
                Assert.That(harness.Added, Has.Count.EqualTo(addedBefore));
                Assert.That(harness.Deleted, Is.Empty);
                Assert.That(harness.Strategy.Configured
                    .Where(call => ReferenceEquals(call.Node, created))
                    .Select(call => (call.Created, call.Entity.Epoch)),
                    Is.EqualTo(new[] { (true, 3u), (false, 9u) }));
            });
        }

        [Test]
        public async Task RemovingCollectionDrainsSubtreeLookupAndDeleteRoutingAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                1,
                Collection("endpoints", Group("endpoints", "orders", 2)),
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 7))));
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupCollectionState view = harness.View("messagegroups");
            GroupState group = harness.GroupIn(view, "orders");
            MetadataResourceState message = harness.MessageIn(group, "created");
            GroupState endpoint = harness.GroupIn(harness.View("endpoints"), "orders");

            harness.Strategy.Committed = Generation(
                2,
                Collection("endpoints", Group("endpoints", "orders", 2)));
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            ServiceResult staleDelete = await InvokeDeleteAsync(harness, message.Delete!, message, 7)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Deleted, Is.EqualTo(new[] { message.NodeId, group.NodeId, view.NodeId }));
                Assert.That(harness.Live.ContainsKey(message.NodeId), Is.False);
                Assert.That(harness.Live.ContainsKey(group.NodeId), Is.False);
                Assert.That(harness.Live.ContainsKey(view.NodeId), Is.False);
                Assert.That(
                    harness.Registry.FindChild(harness.Context, new QualifiedName("messagegroups", 1)),
                    Is.Null);
                Assert.That(view.FindChild(harness.Context, new QualifiedName("orders", 1)), Is.Null);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(harness.Registry));
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders"),
                    Is.SameAs(harness.Registry));
                Assert.That(staleDelete.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(harness.Strategy.Deletes, Is.Empty);
                Assert.That(harness.GroupIn(harness.View("endpoints"), "orders"), Is.SameAs(endpoint));
                Assert.That(harness.Engine.EventSourceFor("/endpoints/orders"), Is.SameAs(endpoint));
            });
        }

        [Test]
        public async Task StaleAndFailedGenerationsDoNotReplaceActiveProjectionAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                5,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 3))));
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            MetadataResourceState created = harness.MessageIn(group, "created");
            int addedBefore = harness.Added.Count;

            await harness.Engine.ReconcileAsync(
                    Generation(4, Collection("messagegroups", Group("messagegroups", "orders", 5))),
                    null,
                    CancellationToken.None)
                .ConfigureAwait(false);
            XRegistryProjectionGeneration failing = Generation(
                7,
                Collection(
                    "messagegroups",
                    Group(
                        "messagegroups",
                        "orders",
                        5,
                        Message("orders", "created", 4),
                        Message("orders", "mistyped", 1) with
                        {
                            TypeDefinitionId = ObjectTypeIds.GroupType
                        })));
            InvalidOperationException? failure = Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Engine.ReconcileAsync(failing, null, CancellationToken.None).AsTask());
            uint epochAfterFailure = created.Epoch!.Value;
            await harness.Engine.ReconcileAsync(
                    Generation(
                        6,
                        Collection(
                            "messagegroups",
                            Group("messagegroups", "orders", 5, Message("orders", "created", 8)))),
                    null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain("/messagegroups/orders/messages/mistyped"));
                Assert.That(epochAfterFailure, Is.EqualTo(3u));
                Assert.That(harness.Added, Has.Count.EqualTo(addedBefore));
                Assert.That(harness.Deleted, Is.Empty);
                Assert.That(harness.MessageIn(group, "created"), Is.SameAs(created));
                Assert.That(created.Epoch.Value, Is.EqualTo(8u));
                Assert.That(
                    harness.Strategy.Configured.Count(call =>
                        string.Equals(call.Entity.Xid, "/messagegroups/orders/messages/mistyped",
                            StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ChangedMemberPresenceReplacesNodeWithoutDuplicateNodeIdAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                1,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 3))));
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            MetadataResourceState before = harness.MessageIn(group, "created");
            string nodeId = before.NodeId.ToString();

            harness.Strategy.Committed = Generation(
                2,
                Collection(
                    "messagegroups",
                    Group(
                        "messagegroups",
                        "orders",
                        5,
                        Message("orders", "created", 4) with { Description = "now present" })));
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            MetadataResourceState after = harness.MessageIn(group, "created");
            ServiceResult oldDelete = await InvokeDeleteAsync(harness, before.Delete!, before, 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(after, Is.Not.SameAs(before));
                Assert.That(after.NodeId, Is.EqualTo(before.NodeId));
                Assert.That(after.Description!.Value, Is.EqualTo("now present"));
                Assert.That(before.Description, Is.Null);
                Assert.That(after.Epoch!.Value, Is.EqualTo(4u));
                Assert.That(
                    harness.Operations.Where(operation => operation.EndsWith(nodeId, StringComparison.Ordinal)),
                    Is.EqualTo(new[] { "+" + nodeId, "-" + nodeId, "+" + nodeId }));
                Assert.That(harness.Duplicates, Is.Empty);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(after));
                Assert.That(oldDelete.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(harness.Strategy.Deletes, Is.Empty);
            });
        }

        [Test]
        public async Task DeleteRoutesObservedEntityThroughCommitSeamAsync()
        {
            Harness harness = Harness.Create();
            TestResource committedMessage = Message("orders", "created", 3);
            harness.Strategy.Committed = Generation(
                2,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, committedMessage)));
            harness.Strategy.OnDelete = (entity, expectedEpoch) =>
            {
                if (expectedEpoch != 0 && expectedEpoch != entity.Epoch)
                {
                    return StatusCodes.BadInvalidState;
                }
                harness.Strategy.Committed = Generation(
                    3,
                    Collection("messagegroups", Group("messagegroups", "orders", 5)));
                return ServiceResult.Good;
            };
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            MetadataResourceState message = harness.MessageIn(group, "created");

            ServiceResult stale = await InvokeDeleteAsync(harness, message.Delete!, message, 9)
                .ConfigureAwait(false);
            bool presentAfterStale = harness.Live.ContainsKey(message.NodeId);
            ServiceResult deleted = await InvokeDeleteAsync(harness, message.Delete!, message, 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(presentAfterStale, Is.True);
                Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(harness.Strategy.Deletes.Select(call => call.ExpectedEpoch),
                    Is.EqualTo(s_staleThenCurrentEpoch));
                Assert.That(harness.Strategy.Deletes.Select(call => call.Entity),
                    Is.All.SameAs(committedMessage));
                Assert.That(harness.Deleted, Is.EqualTo(new[] { message.NodeId }));
                Assert.That(harness.MessageInOrNull(group, "created"), Is.Null);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(harness.Registry));
            });
        }

        [Test]
        public async Task FailedActivationAfterCommitIsUncertainAndNextActivationConvergesAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                2,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 3))));
            harness.Strategy.OnDelete = (entity, expectedEpoch) =>
            {
                harness.Strategy.Committed = Generation(
                    3,
                    Collection("messagegroups", Group("messagegroups", "orders", 5)));
                return ServiceResult.Good;
            };
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState group = harness.GroupIn(harness.View("messagegroups"), "orders");
            MetadataResourceState message = harness.MessageIn(group, "created");
            harness.FailDelete = nodeId => nodeId == message.NodeId;

            ServiceResult result = await InvokeDeleteAsync(harness, message.Delete!, message, 0)
                .ConfigureAwait(false);
            bool liveAfterFailure = harness.Live.ContainsKey(message.NodeId);
            harness.FailDelete = null;
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
                Assert.That(result.LocalizedText.Text, Does.Contain("was committed"));
                Assert.That(liveAfterFailure, Is.True);
                Assert.That(harness.Live.ContainsKey(message.NodeId), Is.False);
                Assert.That(harness.Deleted, Is.EqualTo(new[] { message.NodeId }));
                Assert.That(harness.MessageInOrNull(group, "created"), Is.Null);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(harness.Registry));
            });
        }

        [Test]
        public async Task HostOwnedCollectionViewIsReusedAndNeverDeletedAsync()
        {
            Harness harness = Harness.Create();
            var hostView = new GroupCollectionState(harness.Registry)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            hostView.Create(
                harness.Context,
                new NodeId("TestRegistry/Endpoints", 1),
                new QualifiedName("Endpoints", 1),
                new LocalizedText("Endpoints"),
                assignNodeIds: false);
            harness.Registry.AddChild(hostView);
            harness.Strategy.Committed = Generation(
                1,
                new TestCollection("endpoints", [Group("endpoints", "orders", 2)])
                {
                    BrowseName = new QualifiedName("Endpoints", 1)
                });

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupState endpoint = harness.GroupIn(hostView, "orders");
            NodeState? publishedParent = endpoint.Parent;
            string collectionName = hostView.CollectionName!.Value;
            harness.Strategy.Committed = Generation(2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(collectionName, Is.EqualTo("endpoints"));
                Assert.That(publishedParent, Is.SameAs(hostView));
                Assert.That(endpoint.Xid!.Value, Is.EqualTo("/endpoints/orders"));
                Assert.That(harness.Added.OfType<GroupCollectionState>(), Is.Empty);
                Assert.That(harness.Deleted, Is.EqualTo(new[] { endpoint.NodeId }));
                Assert.That(harness.Registry.FindChild(harness.Context, new QualifiedName("Endpoints", 1)),
                    Is.SameAs(hostView));
                Assert.That(hostView.FindChild(harness.Context, new QualifiedName("orders", 1)), Is.Null);
            });
        }

        [Test]
        public async Task DetachRemovesPublishedNodesAndUnbindsDeleteAsync()
        {
            Harness harness = Harness.Create();
            harness.Strategy.Committed = Generation(
                1,
                Collection(
                    "messagegroups",
                    Group("messagegroups", "orders", 5, Message("orders", "created", 3))));
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            GroupCollectionState view = harness.View("messagegroups");
            GroupState group = harness.GroupIn(view, "orders");
            MetadataResourceState message = harness.MessageIn(group, "created");

            await harness.Engine.DetachAsync(CancellationToken.None).ConfigureAwait(false);
            ServiceResult delete = await InvokeDeleteAsync(harness, message.Delete!, message, 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Deleted, Is.EqualTo(new[] { message.NodeId, group.NodeId, view.NodeId }));
                Assert.That(harness.Live.Keys.Where(nodeId => nodeId != harness.Registry.NodeId), Is.Empty);
                Assert.That(harness.Registry.FindChild(harness.Context, view.BrowseName), Is.Null);
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.Not.SameAs(message));
                Assert.That(delete.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(harness.Strategy.Deletes, Is.Empty);
            });
        }

        [Test]
        public async Task DocumentGroupsAndCollectionsShareOneRegistryWithoutAliasingAsync()
        {
            var strategy = new Mock<IXRegistryProjectionStrategy>();
            Mock<IXRegistryCollectionProjectionStrategy> collections =
                strategy.As<IXRegistryCollectionProjectionStrategy>();
            strategy
                .Setup(s => s.CreateGroupNode(
                    It.IsAny<BaseObjectState>(),
                    It.IsAny<IXRegistryProjectionGroup>()))
                .Returns<BaseObjectState, IXRegistryProjectionGroup>((parent, _) => new GroupState(parent));
            strategy
                .Setup(s => s.CreateResourceNode(
                    It.IsAny<GroupState>(),
                    It.IsAny<IXRegistryProjectionResource>()))
                .Returns<GroupState, IXRegistryProjectionResource>((parent, _) => new ResourceState(parent));
            collections
                .Setup(s => s.CreateCollectionNode(
                    It.IsAny<BaseObjectState>(),
                    It.IsAny<IXRegistryProjectionCollection>()))
                .Returns<BaseObjectState, IXRegistryProjectionCollection>(
                    (parent, _) => new GroupCollectionState(parent));
            collections
                .Setup(s => s.CreateEntityNode(It.IsAny<NodeState>(), It.IsAny<IXRegistryProjectionEntity>()))
                .Returns<NodeState, IXRegistryProjectionEntity>((parent, entity) =>
                    entity.Role == XRegistryProjectionEntityRole.Group
                        ? new GroupState(parent)
                        : new MetadataResourceState(parent));
            XRegistryProjectionGeneration committed = new(
                new TestSnapshot(
                    Collection(
                        "messagegroups",
                        Group("messagegroups", "orders", 5, Message("orders", "created", 3) with
                        {
                            ContainerBrowseName = QualifiedName.Null
                        })))
                {
                    Groups = [new DocumentGroup("orders", [new DocumentResource("orders", "spec")])]
                },
                EventSnapshot(1));
            collections.Setup(s => s.CaptureProjectionGeneration()).Returns(() => committed);
            Harness harness = Harness.Create(
                context => new XRegistryProjectionEngine(context, strategy.Object, "TestRegistry"));

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            var documentGroup = (GroupState)harness.Registry.FindChild(
                harness.Context,
                new QualifiedName("orders", 1))!;
            var document = (ResourceState)documentGroup.FindChild(
                harness.Context,
                new QualifiedName("spec", 1))!;
            GroupState metadataGroup = harness.GroupIn(harness.View("messagegroups"), "orders");
            var metadata = (MetadataResourceState)metadataGroup.FindChild(
                harness.Context,
                new QualifiedName("created", 1))!;
            int added = harness.Added.Count;
            committed = new(
                new TestSnapshot(
                    Collection("groups", Group("groups", "orders", 1)),
                    Collection("messagegroups", Group("messagegroups", "orders", 5)))
                {
                    Groups = [new DocumentGroup("orders", [new DocumentResource("orders", "spec")])]
                },
                EventSnapshot(2));
            InvalidOperationException? aliasing = Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Engine.ReconcileAsync(CancellationToken.None).AsTask());

            Assert.Multiple(() =>
            {
                Assert.That(documentGroup.NodeId, Is.EqualTo(new NodeId("TestRegistry/groups/orders", 1)));
                Assert.That(document, Is.InstanceOf<FileState>());
                Assert.That(document.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/groups/orders/resources/spec", 1)));
                Assert.That(metadataGroup.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/messagegroups/orders", 1)));
                Assert.That(metadata, Is.Not.InstanceOf<FileState>());
                Assert.That(metadata.Parent, Is.SameAs(metadataGroup));
                Assert.That(harness.Engine.EventSourceFor("/groups/orders/resources/spec"),
                    Is.SameAs(document));
                Assert.That(harness.Engine.EventSourceFor("/messagegroups/orders/messages/created"),
                    Is.SameAs(metadata));
                Assert.That(harness.Registry.CreateGroup!.OnCallMethod2Async, Is.Not.Null);
                Assert.That(aliasing!.Message, Does.Contain("'groups' collection"));
                Assert.That(harness.Added, Has.Count.EqualTo(added));
                Assert.That(harness.Deleted, Is.Empty);
                Assert.That(harness.Duplicates, Is.Empty);
            });
        }

        private static ValueTask<ServiceResult> InvokeDeleteAsync(
            Harness harness,
            MethodState method,
            NodeState target,
            uint expectedEpoch)
        {
            return method.OnCallMethod2Async!(
                harness.Context,
                method,
                target.NodeId,
                [new Variant(expectedEpoch)],
                [],
                CancellationToken.None);
        }

        private static XRegistryProjectionGeneration Generation(
            uint epoch,
            params IXRegistryProjectionCollection[] collections)
        {
            return new XRegistryProjectionGeneration(new TestSnapshot(collections), EventSnapshot(epoch));
        }

        private static XRegistryProjectionEventSnapshot EventSnapshot(uint epoch)
        {
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                []);
        }

        private static TestCollection Collection(
            string name,
            params IXRegistryProjectionCollectionGroup[] groups)
        {
            return new TestCollection(name, groups);
        }

        private static TestGroup Group(
            string collectionName,
            string groupId,
            uint epoch,
            params IXRegistryProjectionMetadataResource[] resources)
        {
            return new TestGroup(collectionName, groupId, epoch, resources);
        }

        private static TestResource Message(string groupId, string resourceId, uint epoch)
        {
            return new TestResource("messagegroups", groupId, "messages", resourceId, epoch)
            {
                ContainerBrowseName = s_messagesFolder
            };
        }

        private sealed class Harness
        {
            private Harness(ServerSystemContext context, RegistryState registry)
            {
                Context = context;
                Registry = registry;
            }

            public XRegistryProjectionEngine Engine { get; private set; } = null!;
            public ServerSystemContext Context { get; }
            public RegistryState Registry { get; }
            public CollectionStrategy Strategy { get; } = new();
            public List<NodeState> Added { get; } = [];
            public List<NodeId> Deleted { get; } = [];
            public List<string> Operations { get; } = [];
            public Dictionary<NodeId, NodeState> Live { get; } = [];
            public List<NodeId> Duplicates { get; } = [];
            public Func<NodeId, bool>? FailDelete { get; set; }

            public static Harness Create(
                Func<XRegistryProjectionContext, XRegistryProjectionEngine>? engineFactory = null)
            {
                Mock<IServerInternal> server =
                    XRegistryServerTestHarness.CreateServer(XRegistryWellKnown.XRegistryNamespaceUri);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy();
                context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
                var registry = new RegistryState(null)
                {
                    NodeId = new NodeId("TestRegistry", 1),
                    BrowseName = new QualifiedName("TestRegistry", 1),
                    DisplayName = new LocalizedText("TestRegistry")
                };
                registry.AddCreateGroup(context)
                    .AddGetOrCreateGroup(context)
                    .AddLabels(context);
                var harness = new Harness(context, registry);
                harness.Live[registry.NodeId] = registry;
                var projectionContext = new XRegistryProjectionContext(
                    context,
                    context.NamespaceUris,
                    1,
                    harness.OnAddAsync,
                    harness.OnDeleteAsync,
                    (ctx, operation) => ServiceResult.Good);
                harness.Engine = engineFactory is null
                    ? new XRegistryProjectionEngine(projectionContext, harness.Strategy, "TestRegistry")
                    : engineFactory(projectionContext);
                return harness;
            }

            public GroupCollectionState View(string name)
            {
                return Registry.FindChild(Context, new QualifiedName(name, 1)) as GroupCollectionState ??
                    throw new AssertionException($"The collection view '{name}' is not published.");
            }

            public GroupState GroupIn(GroupCollectionState view, string groupId)
            {
                return view.FindChild(Context, new QualifiedName(groupId, 1)) as GroupState ??
                    throw new AssertionException($"The Group '{groupId}' is not published.");
            }

            public MetadataResourceState MessageIn(GroupState group, string resourceId)
            {
                return MessageInOrNull(group, resourceId) ??
                    throw new AssertionException($"The Message '{resourceId}' is not published.");
            }

            public MetadataResourceState? MessageInOrNull(GroupState group, string resourceId)
            {
                return group.FindChild(Context, s_messagesFolder)?.FindChild(
                    Context,
                    new QualifiedName(resourceId, 1)) as MetadataResourceState;
            }

            private ValueTask OnAddAsync(NodeState node, CancellationToken ct)
            {
                if (Live.ContainsKey(node.NodeId))
                {
                    Duplicates.Add(node.NodeId);
                }
                Live[node.NodeId] = node;
                Added.Add(node);
                Operations.Add("+" + node.NodeId);
                return default;
            }

            private ValueTask OnDeleteAsync(NodeId nodeId, CancellationToken ct)
            {
                if (FailDelete?.Invoke(nodeId) == true)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                if (Live.TryGetValue(nodeId, out NodeState? node))
                {
                    foreach (NodeId descendant in Live
                        .Where(pair => IsDescendant(pair.Value, node))
                        .Select(pair => pair.Key)
                        .ToList())
                    {
                        Live.Remove(descendant);
                    }
                    Live.Remove(nodeId);
                }
                Deleted.Add(nodeId);
                Operations.Add("-" + nodeId);
                return default;
            }

            private static bool IsDescendant(NodeState candidate, NodeState ancestor)
            {
                for (NodeState? parent = (candidate as BaseInstanceState)?.Parent;
                    parent is not null;
                    parent = (parent as BaseInstanceState)?.Parent)
                {
                    if (ReferenceEquals(parent, ancestor))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class CollectionStrategy : IXRegistryCollectionProjectionStrategy
        {
            public XRegistryProjectionGeneration Committed { get; set; } = Generation(0);

            public Func<IXRegistryProjectionEntity, uint, ServiceResult>? OnDelete { get; set; }

            public List<(IXRegistryProjectionEntity Entity, uint ExpectedEpoch)> Deletes { get; } = [];

            public List<(BaseObjectState Node, IXRegistryProjectionEntity Entity, bool Created)> Configured
            { get; } = [];

            public XRegistryProjectionGeneration CaptureProjectionGeneration()
            {
                return Committed;
            }

            public GroupCollectionState CreateCollectionNode(
                BaseObjectState registryNode,
                IXRegistryProjectionCollection collection)
            {
                return new GroupCollectionState(registryNode);
            }

            public BaseObjectState CreateEntityNode(NodeState parent, IXRegistryProjectionEntity entity)
            {
                return entity.Role == XRegistryProjectionEntityRole.Group
                    ? new GroupState(parent)
                    : new MetadataResourceState(parent);
            }

            public void ConfigureEntityNode(
                BaseObjectState node,
                IXRegistryProjectionEntity entity,
                bool created)
            {
                Configured.Add((node, entity, created));
                if (created && node is GroupState group && group.NodeId.TryGetValue(out string path))
                {
                    // A domain Group type declares the Folder that organizes its Messages.
                    group.AddChild(new FolderState(group)
                    {
                        NodeId = new NodeId(path + "/Messages", 1),
                        BrowseName = s_messagesFolder,
                        DisplayName = new LocalizedText("Messages"),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        TypeDefinitionId = Ua.ObjectTypeIds.FolderType
                    });
                }
            }

            public ValueTask<ServiceResult> DeleteEntityAsync(
                ISystemContext context,
                IXRegistryProjectionEntity entity,
                uint expectedEpoch,
                CancellationToken ct)
            {
                Deletes.Add((entity, expectedEpoch));
                return new ValueTask<ServiceResult>(OnDelete?.Invoke(entity, expectedEpoch) ?? ServiceResult.Good);
            }
        }

        private sealed class TestSnapshot : IXRegistryCollectionProjectionSnapshot
        {
            public TestSnapshot(params IXRegistryProjectionCollection[] collections)
            {
                Collections = collections;
            }

            public ImmutableSortedDictionary<string, string> Labels { get; }
                = ImmutableSortedDictionary<string, string>.Empty;

            public IEnumerable<IXRegistryProjectionGroup> Groups { get; init; } = [];

            public ArrayOf<IXRegistryProjectionCollection> Collections { get; }
        }

        private sealed record TestCollection(
            string Name,
            ArrayOf<IXRegistryProjectionCollectionGroup> Groups) : IXRegistryProjectionCollection
        {
            public QualifiedName BrowseName { get; init; }

            public NodeId NodeId { get; init; }
        }

        private sealed record TestGroup(
            string CollectionName,
            string GroupId,
            uint Epoch,
            ArrayOf<IXRegistryProjectionMetadataResource> Resources) : IXRegistryProjectionCollectionGroup
        {
            public XRegistryProjectionEntityRole Role => XRegistryProjectionEntityRole.Group;
            public string Xid { get; init; } = $"/{CollectionName}/{GroupId}";
            public NodeId NodeId { get; init; }
            public ExpandedNodeId TypeDefinitionId { get; init; } = ObjectTypeIds.GroupType;
            public string? Name { get; init; } = GroupId;
            public string? Description { get; init; }
            public string? Documentation { get; init; }
            public DateTimeUtc CreatedAt { get; init; } = s_createdAt;
            public DateTimeUtc ModifiedAt { get; init; } = s_createdAt;
            public ImmutableSortedDictionary<string, string>? Labels { get; init; }
        }

        private sealed record TestResource(
            string CollectionName,
            string GroupId,
            string ResourceCollectionName,
            string ResourceId,
            uint Epoch) : IXRegistryProjectionMetadataResource
        {
            public XRegistryProjectionEntityRole Role => XRegistryProjectionEntityRole.Resource;
            public string Xid { get; init; } =
                $"/{CollectionName}/{GroupId}/{ResourceCollectionName}/{ResourceId}";
            public NodeId NodeId { get; init; }
            public ExpandedNodeId TypeDefinitionId { get; init; } = ObjectTypeIds.MetadataResourceType;
            public string? Name { get; init; }
            public string? Description { get; init; }
            public string? Documentation { get; init; }
            public DateTimeUtc CreatedAt { get; init; } = s_createdAt;
            public DateTimeUtc ModifiedAt { get; init; } = s_createdAt;
            public ImmutableSortedDictionary<string, string>? Labels { get; init; }
            public string? VersionId { get; init; }
            public QualifiedName ContainerBrowseName { get; init; }
        }

        private sealed class DocumentGroup : IXRegistryProjectionGroup
        {
            public DocumentGroup(string groupId, IEnumerable<IXRegistryProjectionResource> resources)
            {
                GroupId = groupId;
                Resources = resources;
            }

            public string GroupId { get; }
            public string Xid => "/groups/" + GroupId;
            public string Name => GroupId;
            public string Description => string.Empty;
            public long Epoch => 1;
            public ImmutableSortedDictionary<string, string> Labels { get; }
                = ImmutableSortedDictionary<string, string>.Empty;
            public IEnumerable<IXRegistryProjectionResource> Resources { get; }
        }

        private sealed class DocumentResource : IXRegistryProjectionResource
        {
            public DocumentResource(string groupId, string resourceId)
            {
                GroupId = groupId;
                ResourceId = resourceId;
            }

            public string GroupId { get; }
            public string ResourceId { get; }
            public string Xid => $"/groups/{GroupId}/resources/{ResourceId}";
            public string Name => ResourceId;
            public string Description => string.Empty;
            public string VersionId => "v1";
            public string Format => "test";
            public string ContentType => "application/test";
            public long Epoch => 1;
            public DateTime CreatedAt => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public DateTime ModifiedAt => new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            public ImmutableSortedDictionary<string, string> Labels { get; }
                = ImmutableSortedDictionary<string, string>.Empty;
        }

        private static readonly QualifiedName s_messagesFolder = new("Messages", 1);

        private static readonly string[] s_deleteMethodOnly = ["Delete"];

        private static readonly bool[] s_createdOnce = [true];

        private static readonly uint[] s_staleThenCurrentEpoch = [9u, 3u];

        private static readonly DateTimeUtc s_createdAt =
            (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
