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
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.OfType<GroupCreatedEventState>()
                    .Single().SourceNode!.Value,
                    Is.EqualTo(new NodeId("TestRegistry/groups/schemas", 1)));
                Assert.That(harness.Events.OfType<ResourceCreatedEventState>()
                    .Single().SourceNode!.Value,
                    Is.EqualTo(new NodeId(
                        "TestRegistry/groups/schemas/resources/pump",
                        1)));
                Assert.That(harness.Events.OfType<VersionCreatedEventState>()
                    .Single().SourceNode!.Value,
                    Is.EqualTo(new NodeId(
                        "TestRegistry/groups/schemas/resources/pump",
                        1)));
            });
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
            Assert.That(harness.Events.Select(evt => evt.SourceNode!.Value).Distinct().Single(),
                Is.EqualTo(new NodeId("TestRegistry/groups/schemas/resources/pump", 1)));
        }

        [Test]
        public void SixParameterProjectionContextConstructorRemainsAvailable()
        {
            Type contextType = typeof(XRegistryProjectionContext);
            Assert.That(
                contextType.GetConstructor(
                [
                    typeof(ISystemContext),
                    typeof(NamespaceTable),
                    typeof(ushort),
                    typeof(Func<NodeState, CancellationToken, ValueTask>),
                    typeof(Func<NodeId, CancellationToken, ValueTask>),
                    typeof(Func<ISystemContext, string, ServiceResult>)
                ]),
                Is.Not.Null);
        }

        [TestCase("v7", "v7")]
        [TestCase("", "generated-1")]
        public async Task VersionAwareCreateHonorsExplicitAndAssignedVersionIdsAsync(
            string requestedVersionId,
            string expectedVersionId)
        {
            var strategy = new VersionedTestStrategy();
            strategy.SetInitialGroup("schemas");
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: true,
                suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            XRegistryProjectionEventSnapshot previousEvents = strategy.EventSnapshot;
            var group = (GroupState)harness.Added.Single(node => node is GroupState);
            var output = new List<Variant>();

            ServiceResult result = await group.CreateResource!.OnCallMethod2Async!(
                harness.Context,
                group.CreateResource,
                group.NodeId,
                [
                    new Variant("pump"),
                    new Variant(requestedVersionId),
                    new Variant(false)
                ],
                output,
                CancellationToken.None).ConfigureAwait(false);
            await harness.Engine.ReconcileAsync(
                    new XRegistryProjectionGeneration(
                        strategy.Snapshot,
                        strategy.EventSnapshot),
                    previousEvents,
                    CancellationToken.None)
                .ConfigureAwait(false);

            NodeId expectedNodeId = new(
                $"TestRegistry/groups/schemas/resources/pump/versions/{expectedVersionId}",
                1);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(output[0].TryGetValue(out NodeId nodeId) ? nodeId : NodeId.Null,
                    Is.EqualTo(expectedNodeId));
                Assert.That(output[1].TryGetValue(out string versionId) ? versionId : string.Empty,
                    Is.EqualTo(expectedVersionId));
                Assert.That(harness.Added.Any(node => node.NodeId == expectedNodeId), Is.True);
                Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(ResourceCreatedEventState),
                    typeof(VersionCreatedEventState),
                    typeof(GroupUpdatedEventState)
                }));
            });

            harness.Events.Clear();
            var updated = new VersionedTestResource(
                "schemas",
                "pump",
                expectedVersionId,
                isDefaultVersion: true,
                epoch: 2);
            strategy.Snapshot = new TestSnapshot(
                [new TestGroup("schemas", [updated])]);
            strategy.EventSnapshot = SingleVersionEventSnapshot(
                expectedVersionId,
                versionEpoch: 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(VersionUpdatedEventState),
                    typeof(ResourceUpdatedEventState)
                }));
                Assert.That(harness.Events.Any(evt =>
                    evt is ResourceCreatedEventState or VersionCreatedEventState), Is.False);
            });
        }

        [Test]
        public async Task GenerationCaptureCannotMixProjectionAndEventSnapshotsAsync()
        {
            var strategy = new AdvancingGenerationStrategy(
                (new TestSnapshot([]), EmptyEventSnapshot(0)),
                GenerationWithResource("a", 1),
                GenerationWithResource("b", 2));
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: true,
                suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Added.Any(node =>
                    node.NodeId == new NodeId("TestRegistry/groups/schemas/resources/a", 1)),
                    Is.True);
                Assert.That(harness.Events.OfType<ResourceCreatedEventState>()
                    .Single().Subject!.Value,
                    Is.EqualTo("/groups/schemas/resources/a"));
                Assert.That(harness.Events.Any(evt =>
                    evt is XRegistryEventState xregistry &&
                    xregistry.Subject!.Value.EndsWith("/b", StringComparison.Ordinal)),
                    Is.False);
            });

            harness.Events.Clear();
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Added.Any(node =>
                    node.NodeId == new NodeId("TestRegistry/groups/schemas/resources/b", 1)),
                    Is.True);
                Assert.That(harness.Events.OfType<ResourceCreatedEventState>()
                    .Single().Subject!.Value,
                    Is.EqualTo("/groups/schemas/resources/b"));
            });
        }

        [Test]
        public async Task SuppliedTransitionsEmitIntermediateCreateAndDeleteAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([]);
            harness.Strategy.EventSnapshot = EmptyEventSnapshot(0);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            (IXRegistryProjectionSnapshot createdProjection,
                XRegistryProjectionEventSnapshot createdEvents) =
                GenerationWithResource("queued", 1);
            await harness.Engine.ReconcileAsync(
                    new XRegistryProjectionGeneration(createdProjection, createdEvents),
                    EmptyEventSnapshot(0),
                    CancellationToken.None)
                .ConfigureAwait(false);
            XRegistryProjectionEventSnapshot deletedEvents = EmptyEventSnapshot(2);
            await harness.Engine.ReconcileAsync(
                    new XRegistryProjectionGeneration(new TestSnapshot([]), deletedEvents),
                    createdEvents,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    harness.Events
                        .Where(evt => evt is ResourceCreatedEventState or ResourceDeletedEventState)
                        .Select(evt => evt.GetType()),
                    Is.EqualTo(new[]
                    {
                        typeof(ResourceCreatedEventState),
                        typeof(ResourceDeletedEventState)
                    }));
                Assert.That(
                    harness.Deleted,
                    Does.Contain(new NodeId(
                        "TestRegistry/groups/schemas/resources/queued",
                        1)));
            });
        }

        [Test]
        public async Task ProjectionOnlyReconcileLeavesFifoTransitionsAsEventAuthorityAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([]);
            harness.Strategy.EventSnapshot = EmptyEventSnapshot(0);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            (IXRegistryProjectionSnapshot firstProjection,
                XRegistryProjectionEventSnapshot firstEvents) =
                GenerationWithResources(1, "a");
            (IXRegistryProjectionSnapshot secondProjection,
                XRegistryProjectionEventSnapshot secondEvents) =
                GenerationWithResources(2, "a", "b");
            harness.Strategy.Snapshot = secondProjection;
            harness.Strategy.EventSnapshot = secondEvents;

            await harness.Engine.ReconcileProjectionAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await harness.Engine.ReconcileAsync(
                    new XRegistryProjectionGeneration(firstProjection, firstEvents),
                    EmptyEventSnapshot(0),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await harness.Engine.ReconcileAsync(
                    new XRegistryProjectionGeneration(secondProjection, secondEvents),
                    firstEvents,
                    CancellationToken.None)
                .ConfigureAwait(false);

            string[] created = harness.Events
                .OfType<ResourceCreatedEventState>()
                .Select(evt => evt.Subject!.Value)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(
                    created,
                    Is.EqualTo(new[]
                    {
                        "/groups/schemas/resources/a",
                        "/groups/schemas/resources/b"
                    }));
                Assert.That(
                    created.Count(subject =>
                        string.Equals(
                            subject,
                            "/groups/schemas/resources/b",
                            StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    harness.Added.Any(node =>
                        node.NodeId == new NodeId(
                            "TestRegistry/groups/schemas/resources/b",
                            1)),
                    Is.True);
            });
        }

        [Test]
        public async Task DeprecatedObjectValueChangesEmitSpecializedAndUpdatedEventsAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([new TestGroup("schemas", [])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup(null, 1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup("reason=a", 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(GroupDeprecatedEventState),
                typeof(GroupUpdatedEventState)
            }));

            harness.Events.Clear();
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup("reason=b", 3);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(GroupDeprecatedEventState),
                typeof(GroupUpdatedEventState)
            }));

            harness.Events.Clear();
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup(null, 4);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(GroupUndeprecatedEventState),
                typeof(GroupUpdatedEventState)
            }));
        }

        [Test]
        public async Task ResourceDeprecatedObjectUsesCreateChangeAndRemovalErrataAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot(
                [new TestGroup("schemas", [new TestResource("schemas", "pump")])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedResource(null, 1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            foreach ((string? value, Type specialized) in new[]
            {
                ("reason=a", typeof(ResourceDeprecatedEventState)),
                ("reason=b", typeof(ResourceDeprecatedEventState)),
                (null, typeof(ResourceUndeprecatedEventState))
            })
            {
                harness.Events.Clear();
                harness.Strategy.EventSnapshot = SnapshotWithDeprecatedResource(
                    value,
                    (uint)(2 + (value == "reason=b" ? 1 : value is null ? 2 : 0)));
                await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(harness.Events.Any(evt => evt.GetType() == specialized), Is.True);
                    ResourceUpdatedEventState updated =
                        harness.Events.OfType<ResourceUpdatedEventState>().Single();
                    Assert.That(updated.Changed!.Value.ToArray(), Does.Contain("meta.deprecated"));
                });
            }
        }

        [Test]
        public async Task VersionSourcesMetaAndDefaultSwitchAreDiffedIndependentlyAsync()
        {
            var strategy = new VersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: true,
                suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()),
                    Is.EquivalentTo(new[] { typeof(ResourceUpdatedEventState) }));
                Assert.That(
                    harness.Events.OfType<ResourceUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(new NodeId(
                        "TestRegistry/groups/schemas/resources/pump/versions/v2",
                        1)));
            });

            harness.Events.Clear();
            strategy.EventSnapshot = VersionedEventSnapshot(
                "v2",
                3,
                WotLabels().Add("owner", "plant-1"));
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()),
                    Is.EquivalentTo(new[] { typeof(ResourceUpdatedEventState) }));
                Assert.That(
                    harness.Events.OfType<ResourceUpdatedEventState>()
                        .Single().Changed!.Value.ToArray(),
                    Does.Contain("meta.labels"));
            });

            harness.Events.Clear();
            strategy.EventSnapshot = VersionedEventSnapshot(
                "v2",
                3,
                WotLabels().Add("owner", "plant-1"),
                v1Epoch: 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()),
                    Is.EquivalentTo(new[] { typeof(VersionUpdatedEventState) }));
                Assert.That(
                    harness.Events.OfType<VersionUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(new NodeId(
                        "TestRegistry/groups/schemas/resources/pump/versions/v1",
                        1)));
            });
        }

        [Test]
        public async Task LogicalResourceEventSourceIsMappedWhenGenericEventsAreDisabled()
        {
            var strategy = new VersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: false,
                suppliedStrategy: strategy);

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            NodeState source = harness.Engine.EventSourceFor(
                "/groups/schemas/resources/pump");
            Assert.That(
                source.NodeId,
                Is.EqualTo(new NodeId(
                    "TestRegistry/groups/schemas/resources/pump/versions/v1",
                    1)));
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

        private static (
            IXRegistryProjectionSnapshot Projection,
            XRegistryProjectionEventSnapshot Events) GenerationWithResource(
                string resourceId,
                uint epoch)
        {
            return GenerationWithResources(epoch, resourceId);
        }

        private static (
            IXRegistryProjectionSnapshot Projection,
            XRegistryProjectionEventSnapshot Events) GenerationWithResources(
                uint epoch,
                params string[] resourceIds)
        {
            TestResource[] resources = resourceIds
                .Select(resourceId => new TestResource("schemas", resourceId))
                .ToArray();
            IXRegistryProjectionSnapshot projection = new TestSnapshot(
                [new TestGroup("schemas", resources)]);
            var events = new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                [
                    new XRegistryProjectionEventGroup(
                        "schemas",
                        "/groups/schemas",
                        epoch,
                        ImmutableSortedDictionary<string, string>.Empty,
                        false,
                        resourceIds.Select(resourceId =>
                            new XRegistryProjectionEventResource(
                                "schemas",
                                resourceId,
                                $"/groups/schemas/resources/{resourceId}",
                                1,
                                1,
                                ImmutableSortedDictionary<string, string>.Empty,
                                false,
                                "v1",
                                [
                                    new XRegistryProjectionEventVersion(
                                        "v1",
                                        $"/groups/schemas/resources/{resourceId}/versions/v1",
                                        1,
                                        ImmutableSortedDictionary<string, string>.Empty)
                                ])
                        ).ToImmutableArray())
                ]);
            return (projection, events);
        }

        private static XRegistryProjectionEventSnapshot SnapshotWithDeprecatedGroup(
            string? canonicalDeprecated,
            uint epoch)
        {
            var group = new XRegistryProjectionEventGroup(
                "schemas",
                "/groups/schemas",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                false,
                [])
            {
                Deprecation = canonicalDeprecated is null
                    ? null
                    : new XRegistryProjectionDeprecation(
                        canonicalDeprecated,
                        ImmutableSortedDictionary<string, string>.Empty)
            };
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                [group]);
        }

        private static XRegistryProjectionEventSnapshot SnapshotWithDeprecatedResource(
            string? canonicalDeprecated,
            uint epoch)
        {
            var resource = new XRegistryProjectionEventResource(
                "schemas",
                "pump",
                "/groups/schemas/resources/pump",
                1,
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                false,
                "v1",
                [
                    new XRegistryProjectionEventVersion(
                        "v1",
                        "/groups/schemas/resources/pump/versions/v1",
                        1,
                        ImmutableSortedDictionary<string, string>.Empty)
                ])
            {
                Deprecation = canonicalDeprecated is null
                    ? null
                    : new XRegistryProjectionDeprecation(
                        canonicalDeprecated,
                        ImmutableSortedDictionary<string, string>.Empty)
            };
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                [
                    new XRegistryProjectionEventGroup(
                        "schemas",
                        "/groups/schemas",
                        1,
                        ImmutableSortedDictionary<string, string>.Empty,
                        false,
                        [resource])
                ]);
        }

        private static ImmutableSortedDictionary<string, string> WotLabels()
        {
            return ImmutableSortedDictionary<string, string>.Empty;
        }

        private static TestSnapshot VersionedProjectionSnapshot(
            string defaultVersionId)
        {
            return new TestSnapshot(
            [
                new TestGroup(
                    "schemas",
                    [
                        new VersionedTestResource(
                            "schemas",
                            "pump",
                            "v1",
                            defaultVersionId == "v1"),
                        new VersionedTestResource(
                            "schemas",
                            "pump",
                            "v2",
                            defaultVersionId == "v2")
                    ])
            ]);
        }

        private static XRegistryProjectionEventSnapshot VersionedEventSnapshot(
            string defaultVersionId,
            uint metaEpoch,
            ImmutableSortedDictionary<string, string> metaLabels,
            uint v1Epoch = 1,
            uint v2Epoch = 1)
        {
            NodeId v1Node = new(
                "TestRegistry/groups/schemas/resources/pump/versions/v1",
                1);
            NodeId v2Node = new(
                "TestRegistry/groups/schemas/resources/pump/versions/v2",
                1);
            XRegistryProjectionEventVersion V(string id, uint epoch, NodeId source)
            {
                return new XRegistryProjectionEventVersion(
                    id,
                    $"/groups/schemas/resources/pump/versions/{id}",
                    epoch,
                    ImmutableSortedDictionary<string, string>.Empty.Add(
                        "resource",
                        epoch.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)))
                {
                    SourceNodeId = source,
                    SourceName = id,
                    CreatedAt = DateTime.UnixEpoch,
                    ModifiedAt = DateTime.UnixEpoch.AddSeconds(epoch)
                };
            }

            return new XRegistryProjectionEventSnapshot(
                "/",
                metaEpoch,
                ImmutableSortedDictionary<string, string>.Empty,
                [
                    new XRegistryProjectionEventGroup(
                        "schemas",
                        "/groups/schemas",
                        1,
                        ImmutableSortedDictionary<string, string>.Empty,
                        false,
                        [
                            new XRegistryProjectionEventResource(
                                "schemas",
                                "pump",
                                "/groups/schemas/resources/pump",
                                defaultVersionId == "v1" ? v1Epoch : v2Epoch,
                                metaEpoch,
                                metaLabels,
                                false,
                                defaultVersionId,
                                [V("v1", v1Epoch, v1Node), V("v2", v2Epoch, v2Node)])
                            {
                                SourceNodeId = defaultVersionId == "v1" ? v1Node : v2Node,
                                SourceName = "pump",
                                MetaCreatedAt = DateTime.UnixEpoch,
                                MetaModifiedAt = DateTime.UnixEpoch.AddSeconds(metaEpoch)
                            }
                        ])
                    {
                        SourceNodeId = new NodeId("TestRegistry/groups/schemas", 1),
                        SourceName = "schemas"
                    }
                ]);
        }

        private static XRegistryProjectionEventSnapshot SingleVersionEventSnapshot(
            string versionId,
            uint versionEpoch)
        {
            NodeId source = new(
                $"TestRegistry/groups/schemas/resources/pump/versions/{versionId}",
                1);
            return new XRegistryProjectionEventSnapshot(
                "/",
                2,
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
                                1,
                                ImmutableSortedDictionary<string, string>.Empty,
                                false,
                                versionId,
                                [
                                    new XRegistryProjectionEventVersion(
                                        versionId,
                                        $"/groups/schemas/resources/pump/versions/{versionId}",
                                        versionEpoch,
                                        ImmutableSortedDictionary<string, string>.Empty.Add(
                                            "resource",
                                            versionEpoch.ToString(
                                                System.Globalization.CultureInfo.InvariantCulture)))
                                    {
                                        SourceNodeId = source,
                                        SourceName = versionId,
                                        ModifiedAt = DateTime.UnixEpoch.AddSeconds(versionEpoch)
                                    }
                                ])
                            {
                                SourceNodeId = source,
                                SourceName = "pump",
                                MetaCreatedAt = DateTime.UnixEpoch,
                                MetaModifiedAt = DateTime.UnixEpoch
                            }
                        ])
                    {
                        SourceNodeId = new NodeId("TestRegistry/groups/schemas", 1),
                        SourceName = "schemas"
                    }
                ]);
        }

        private sealed class ProjectionHarness
        {
            private ProjectionHarness(
                XRegistryProjectionEngine engine,
                ServerSystemContext context,
                RegistryState registry,
                TestStrategy strategy,
                List<NodeState> added,
                List<NodeId> deleted,
                List<BaseEventState> events)
            {
                Engine = engine;
                Context = context;
                Registry = registry;
                Strategy = strategy;
                Added = added;
                Deleted = deleted;
                Events = events;
            }

            public XRegistryProjectionEngine Engine { get; }
            public ServerSystemContext Context { get; }
            public RegistryState Registry { get; }
            public TestStrategy Strategy { get; }
            public List<NodeState> Added { get; }
            public List<NodeId> Deleted { get; }
            public List<BaseEventState> Events { get; }

            public static ProjectionHarness Create(
                bool eventsEnabled = false,
                TestStrategy? suppliedStrategy = null)
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
                TestStrategy strategy = suppliedStrategy ?? new TestStrategy();
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
                    context,
                    registry,
                    strategy,
                    added,
                    deleted,
                    events);
            }
        }

        private class TestStrategy :
            IXRegistryProjectionStrategy,
            IXRegistryProjectionEventMetadataProvider,
            IXRegistryProjectionGenerationProvider
        {
            public IXRegistryProjectionSnapshot Snapshot { get; set; } = new TestSnapshot([]);
            public XRegistryProjectionEventSnapshot EventSnapshot { get; set; } =
                EmptyEventSnapshot(0);

            public virtual IXRegistryProjectionSnapshot Current => Snapshot;

            public XRegistryProjectionEventSnapshot CaptureEventSnapshot() => EventSnapshot;

            public virtual XRegistryProjectionGeneration CaptureProjectionGeneration()
            {
                return new XRegistryProjectionGeneration(Snapshot, EventSnapshot);
            }

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

        private sealed class AdvancingGenerationStrategy : TestStrategy
        {
            public AdvancingGenerationStrategy(
                params (IXRegistryProjectionSnapshot Projection,
                    XRegistryProjectionEventSnapshot Events)[] generations)
            {
                m_generations = new Queue<XRegistryProjectionGeneration>(
                    generations.Select(generation => new XRegistryProjectionGeneration(
                        generation.Projection,
                        generation.Events)));
            }

            public override XRegistryProjectionGeneration CaptureProjectionGeneration()
            {
                return m_generations.Dequeue();
            }

            private readonly Queue<XRegistryProjectionGeneration> m_generations;
        }

        private sealed class VersionedTestStrategy :
            TestStrategy,
            IXRegistryVersionedProjectionStrategy
        {
            public void SetInitialGroup(string groupId)
            {
                Snapshot = new TestSnapshot([new TestGroup(groupId, [])]);
                EventSnapshot = new XRegistryProjectionEventSnapshot(
                    "/",
                    1,
                    ImmutableSortedDictionary<string, string>.Empty,
                    [
                        new XRegistryProjectionEventGroup(
                            groupId,
                            $"/groups/{groupId}",
                            1,
                            ImmutableSortedDictionary<string, string>.Empty,
                            false,
                            [])
                    ]);
            }

            public ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                IXRegistryProjectionResource resource = Create(
                    groupId,
                    resourceId,
                    versionId);
                return new ValueTask<IXRegistryProjectionResource?>(resource);
            }

            public ValueTask<(IXRegistryProjectionResource Resource, bool Created)>
                GetOrCreateResourceAsync(
                    string groupId,
                    string resourceId,
                    string versionId,
                    CancellationToken ct)
            {
                IXRegistryProjectionResource resource = Create(
                    groupId,
                    resourceId,
                    versionId);
                return new ValueTask<(IXRegistryProjectionResource, bool)>((resource, true));
            }

            public ValueTask<ServiceResult> DeleteVersionAsync(
                string groupId,
                string resourceId,
                string versionId,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> AddVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> RemoveVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> AddResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> RemoveResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            private VersionedTestResource Create(
                string groupId,
                string resourceId,
                string requestedVersionId)
            {
                string versionId = string.IsNullOrEmpty(requestedVersionId)
                    ? $"generated-{++m_nextVersion}"
                    : requestedVersionId;
                var resource = new VersionedTestResource(
                    groupId,
                    resourceId,
                    versionId);
                Snapshot = new TestSnapshot([new TestGroup(groupId, [resource])]);
                NodeId versionNodeId = new(
                    $"TestRegistry/groups/{groupId}/resources/{resourceId}/versions/{versionId}",
                    1);
                EventSnapshot = new XRegistryProjectionEventSnapshot(
                    "/",
                    2,
                    ImmutableSortedDictionary<string, string>.Empty,
                    [
                        new XRegistryProjectionEventGroup(
                            groupId,
                            $"/groups/{groupId}",
                            2,
                            ImmutableSortedDictionary<string, string>.Empty,
                            false,
                            [
                                new XRegistryProjectionEventResource(
                                    groupId,
                                    resourceId,
                                    $"/groups/{groupId}/resources/{resourceId}",
                                    1,
                                    1,
                                    ImmutableSortedDictionary<string, string>.Empty,
                                    false,
                                    versionId,
                                    [
                                        new XRegistryProjectionEventVersion(
                                            versionId,
                                            resource.Xid,
                                            1,
                                            ImmutableSortedDictionary<string, string>.Empty)
                                        {
                                            SourceNodeId = versionNodeId,
                                            SourceName = versionId
                                        }
                                    ])
                                {
                                    SourceNodeId = versionNodeId,
                                    SourceName = resourceId,
                                    MetaCreatedAt = DateTime.UnixEpoch,
                                    MetaModifiedAt = DateTime.UnixEpoch
                                }
                            ])
                        {
                            SourceNodeId = new NodeId(
                                $"TestRegistry/groups/{groupId}",
                                1),
                            SourceName = groupId
                        }
                    ]);
                return resource;
            }

            private int m_nextVersion;
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

        private sealed class VersionedTestResource :
            IXRegistryProjectionResource,
            IXRegistryProjectionResourceMeta
        {
            public VersionedTestResource(
                string groupId,
                string resourceId,
                string versionId,
                bool isDefaultVersion = true,
                long epoch = 1)
            {
                GroupId = groupId;
                ResourceId = resourceId;
                VersionId = versionId;
                IsDefaultVersion = isDefaultVersion;
                Epoch = epoch;
            }

            public string GroupId { get; }
            public string ResourceId { get; }
            public string VersionId { get; }
            public string Xid =>
                $"/groups/{GroupId}/resources/{ResourceId}/versions/{VersionId}";
            public string Name => ResourceId;
            public string Description => string.Empty;
            public string Format => "json";
            public string ContentType => "application/json";
            public long Epoch { get; }
            public DateTime CreatedAt => DateTime.UnixEpoch;
            public DateTime ModifiedAt => DateTime.UnixEpoch;
            public ImmutableSortedDictionary<string, string> Labels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;
            public long MetaEpoch => 1;
            public ImmutableSortedDictionary<string, string> MetaLabels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;
            public DateTime MetaCreatedAt => DateTime.UnixEpoch;
            public DateTime MetaModifiedAt => DateTime.UnixEpoch;
            public bool IsDefaultVersion { get; }
        }
    }
}
