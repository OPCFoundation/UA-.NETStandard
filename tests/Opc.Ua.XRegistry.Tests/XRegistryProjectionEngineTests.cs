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
    /// Exercises the shared immutable-snapshot projection engine directly.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryProjectionEngineTests
    {
        [Test]
        public async Task ReconcileCreatesStableGroupAndResourceNodeIdsAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create();
            harness.Strategy.Snapshot = new TestSnapshot(
                [
                    new TestGroup("schemas", [
                        new TestResource("schemas", "pump")
                    ])
                ]);

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            var group = (GroupState?)harness.Added.Single(n => n is GroupState);
            var resource = (ResourceState?)harness.Added.Single(n => n is ResourceState);
            Assert.Multiple(() =>
            {
                Assert.That(group, Is.Not.Null);
                Assert.That(group!.NodeId, Is.EqualTo(new NodeId("TestRegistry/groups/schemas", 1)));
                Assert.That(resource, Is.Not.Null);
                Assert.That(resource!.NodeId,
                    Is.EqualTo(new NodeId("TestRegistry/groups/schemas/resources/pump", 1)));
                Assert.That(resource.VersionId!.Value, Is.EqualTo("v1"));
                Assert.That(harness.Engine.EventSourceFor("/groups/schemas/resources/pump"),
                    Is.SameAs(resource));
            });
        }

        [Test]
        public async Task ReconcileRemovesNodesThatLeftTheSnapshotAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create();
            harness.Strategy.Snapshot = new TestSnapshot(
                [
                    new TestGroup("schemas", [
                        new TestResource("schemas", "pump")
                    ])
                ]);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            harness.Strategy.Snapshot = new TestSnapshot([]);

            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Deleted, Does.Contain(new NodeId("TestRegistry/groups/schemas/resources/pump", 1)));
            Assert.That(harness.Deleted, Does.Contain(new NodeId("TestRegistry/groups/schemas", 1)));
        }

        [Test]
        public async Task EventReconcileIsSilentInitiallyAndSuppressesDuplicateSnapshotsAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([]);
            harness.Strategy.EventSnapshot = EmptyEventSnapshot(1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(harness.Events, Is.Empty);

            harness.Strategy.Snapshot = new TestSnapshot(
            [
                new TestGroup("schemas", [new TestResource("schemas", "pump")])
            ]);
            harness.Strategy.EventSnapshot = SnapshotWithResource(epoch: 2, versionEpoch: 1);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(RegistryUpdatedEventState),
                typeof(GroupCreatedEventState),
                typeof(ResourceCreatedEventState),
                typeof(VersionCreatedEventState)
            }));
            int count = harness.Events.Count;

            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Events, Has.Count.EqualTo(count));
        }

        [Test]
        public async Task EventReconcileDetectsOutOfBandVersionUpdateOnceAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot(
            [
                new TestGroup("schemas", [new TestResource("schemas", "pump")])
            ]);
            harness.Strategy.EventSnapshot = SnapshotWithResource(epoch: 2, versionEpoch: 1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.EventSnapshot = SnapshotWithResource(epoch: 3, versionEpoch: 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(ResourceUpdatedEventState),
                typeof(VersionUpdatedEventState)
            }));
        }

        private static XRegistryProjectionEventSnapshot EmptyEventSnapshot(uint epoch)
        {
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                []);
        }

        private static XRegistryProjectionEventSnapshot SnapshotWithResource(
            uint epoch,
            uint versionEpoch)
        {
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                [
                    new XRegistryProjectionEventGroup(
                        "schemas",
                        "/groups/schemas",
                        2,
                        ImmutableSortedDictionary<string, string>.Empty,
                        false,
                        [
                            new XRegistryProjectionEventResource(
                                "schemas",
                                "pump",
                                "/groups/schemas/resources/pump",
                                versionEpoch,
                                epoch,
                                ImmutableSortedDictionary<string, string>.Empty,
                                false,
                                "v1",
                                [
                                    new XRegistryProjectionEventVersion(
                                        "v1",
                                        "/groups/schemas/resources/pump/versions/v1",
                                        versionEpoch,
                                        ImmutableSortedDictionary<string, string>.Empty
                                            .Add(
                                                "resource",
                                                versionEpoch.ToString(
                                                    System.Globalization.CultureInfo.InvariantCulture)))
                                ])
                        ])
                ]);
        }

        private sealed class ProjectionHarness
        {
            private ProjectionHarness(
                XRegistryProjectionEngine engine,
                RegistryState registry,
                TestStrategy strategy,
                List<NodeState> added,
                List<NodeId> deleted,
                List<BaseEventState> events)
            {
                Engine = engine;
                Registry = registry;
                Strategy = strategy;
                Added = added;
                Deleted = deleted;
                Events = events;
            }

            public XRegistryProjectionEngine Engine { get; }
            public RegistryState Registry { get; }
            public TestStrategy Strategy { get; }
            public List<NodeState> Added { get; }
            public List<NodeId> Deleted { get; }
            public List<BaseEventState> Events { get; }

            public static ProjectionHarness Create(bool eventsEnabled = false)
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
                var added = new List<NodeState>();
                var deleted = new List<NodeId>();
                var events = new List<BaseEventState>();
                registry.OnReportEvent = (_, _, target) =>
                {
                    if (target is BaseEventState evt)
                    {
                        events.Add(evt);
                    }
                };
                var strategy = new TestStrategy();
                var projectionContext = new XRegistryProjectionContext(
                    context,
                    context.NamespaceUris,
                    1,
                    (node, ct) =>
                    {
                        added.Add(node);
                        return default;
                    },
                    (nodeId, ct) =>
                    {
                        deleted.Add(nodeId);
                        return default;
                    },
                    (ctx, operation) => ServiceResult.Good,
                    eventsEnabled
                        ? new XRegistryServerOptions
                        {
                            EventsEnabled = true,
                            EventSourceUrl = "https://registry.example.test"
                        }
                        : null);
                return new ProjectionHarness(
                    new XRegistryProjectionEngine(projectionContext, strategy, "TestRegistry"),
                    registry,
                    strategy,
                    added,
                    deleted,
                    events);
            }
        }

        private sealed class TestStrategy :
            IXRegistryProjectionStrategy,
            IXRegistryProjectionEventMetadataProvider
        {
            public IXRegistryProjectionSnapshot Snapshot { get; set; } = new TestSnapshot([]);
            public XRegistryProjectionEventSnapshot EventSnapshot { get; set; } =
                EmptyEventSnapshot(0);

            public IXRegistryProjectionSnapshot Current => Snapshot;

            public XRegistryProjectionEventSnapshot CaptureEventSnapshot() => EventSnapshot;

            public GroupState CreateGroupNode(BaseObjectState registryNode, IXRegistryProjectionGroup group)
            {
                return new GroupState(registryNode)
                {
                    TypeDefinitionId = new NodeId(ObjectTypes.GroupType, 1)
                };
            }

            public ResourceState CreateResourceNode(GroupState groupNode, IXRegistryProjectionResource resource)
            {
                return new ResourceState(groupNode)
                {
                    TypeDefinitionId = new NodeId(ObjectTypes.ResourceType, 1)
                };
            }

            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
            }

            public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
            {
            }

            public IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                return null;
            }

            public ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionGroup?>(new TestGroup(groupId, []));
            }

            public ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                return new ValueTask<(IXRegistryProjectionGroup, bool)>((new TestGroup(groupId, []), true));
            }

            public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionResource?>(new TestResource(groupId, resourceId));
            }

            public ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                return new ValueTask<(IXRegistryProjectionResource, bool)>(
                    (new TestResource(groupId, resourceId), true));
            }

            public ValueTask<ServiceResult> DeleteGroupAsync(
                string groupId,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> AddRegistryLabelAsync(
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> RemoveRegistryLabelAsync(
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> AddGroupLabelAsync(
                string groupId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> RemoveGroupLabelAsync(
                string groupId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> AddResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> RemoveResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private sealed class TestSnapshot : IXRegistryProjectionSnapshot
        {
            public TestSnapshot(IEnumerable<IXRegistryProjectionGroup> groups)
            {
                Groups = groups;
            }

            public ImmutableSortedDictionary<string, string> Labels { get; }
                = ImmutableSortedDictionary<string, string>.Empty;

            public IEnumerable<IXRegistryProjectionGroup> Groups { get; }
        }

        private sealed class TestGroup : IXRegistryProjectionGroup
        {
            public TestGroup(string groupId, IEnumerable<IXRegistryProjectionResource> resources)
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

        private sealed class TestResource : IXRegistryProjectionResource
        {
            public TestResource(string groupId, string resourceId)
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
    }
}
