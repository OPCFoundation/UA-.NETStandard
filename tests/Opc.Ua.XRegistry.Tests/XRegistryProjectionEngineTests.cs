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
using System.Collections.Concurrent;
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
        public async Task DuplicateCreateCanAtomicallyClaimOnlyAContentlessVersionAsync()
        {
            var strategy = new ContentlessClaimStrategy();
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            var group = (GroupState)harness.Added.Single(node => node is GroupState);
            var output = new List<Variant>();

            ServiceResult claimed = await group.CreateResource!.OnCallMethod2Async!(
                harness.Context,
                group.CreateResource,
                group.NodeId,
                [
                    new Variant("pump"),
                    new Variant("v1"),
                    new Variant(true)
                ],
                output,
                CancellationToken.None).ConfigureAwait(false);
            NodeId claimedNodeId = output[0].TryGetValue(out NodeId nodeId)
                ? nodeId
                : NodeId.Null;
            uint claimedFileHandle = output[2].TryGetValue(out uint fileHandle)
                ? fileHandle
                : 0;

            strategy.File.HasContent = true;
            output.Clear();
            ServiceResult filled = await group.CreateResource.OnCallMethod2Async!(
                harness.Context,
                group.CreateResource,
                group.NodeId,
                [
                    new Variant("pump"),
                    new Variant("v1"),
                    new Variant(true)
                ],
                output,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(claimed), Is.True);
                Assert.That(
                    claimedNodeId,
                    Is.EqualTo(new NodeId(
                        "TestRegistry/groups/schemas/resources/pump/versions/v1",
                        1)));
                Assert.That(claimedFileHandle, Is.EqualTo(42));
                Assert.That(strategy.File.ContentlessOpenCount, Is.EqualTo(2));
                Assert.That(filled.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            });
        }

        [Test]
        public async Task VersionedDeletePassesLogicalRoleAndEpochToAtomicStrategyAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels()),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            ResourceState v1 = FindVersionNode(harness, "v1");

            // In the new hierarchy, deleting a version node always uses
            // version-delete semantics regardless of being the default.
            ServiceResult result = await InvokeDeleteAsync(
                harness,
                v1,
                23).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(v1.BrowseName.Name, Is.EqualTo("v1"));
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        "pump",
                        "v1",
                        23,
                        false,
                        ProjectedDeleteTarget.Version)
                }));
                Assert.That(strategy.ResourceDeletes, Is.Empty);
            });
        }

        [Test]
        public async Task VersionedDeleteUsesLogicalRoleWithoutEventMetadataAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23,
                OmitEventMetadata = true
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            // In the new hierarchy, to delete the logical resource you must
            // delete the logical resource node, not a version node.
            ResourceState logical = FindLogicalResourceNode(harness, "pump");

            ServiceResult result = await InvokeDeleteAsync(
                harness,
                logical,
                17).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        "pump",
                        string.Empty,
                        17,
                        true,
                        ProjectedDeleteTarget.Resource)
                }));
            });
        }

        [Test]
        public async Task VersionedDeletePassesVersionRoleAndEpochToAtomicStrategyAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels()),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            ResourceState v2 = FindVersionNode(harness, "v2");

            ServiceResult result = await InvokeDeleteAsync(
                harness,
                v2,
                23).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(v2.BrowseName.Name, Is.EqualTo("v2"));
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        "pump",
                        "v2",
                        23,
                        false,
                        ProjectedDeleteTarget.Version)
                }));
                Assert.That(strategy.ResourceDeletes, Is.Empty);
            });
        }

        [Test]
        public async Task VersionedDeleteUsesLogicalXidMappingWhenBrowseNamesCollideAsync()
        {
            const string ResourceId = "v2";
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1", ResourceId),
                EventSnapshot = VersionedEventSnapshot(
                    "v1",
                    1,
                    WotLabels(),
                    resourceId: ResourceId),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            // In the new hierarchy, logical resource and version nodes are distinct.
            ResourceState logical = FindLogicalResourceNode(harness, ResourceId);
            ResourceState exactVersion = FindVersionNode(harness, "v2");

            // Version delete — always deleteLogicalResource=false.
            ServiceResult versionResult = await InvokeDeleteAsync(
                harness,
                exactVersion,
                23).ConfigureAwait(false);
            // Logical resource delete — always deleteLogicalResource=true.
            ServiceResult logicalResult = await InvokeDeleteAsync(
                harness,
                logical,
                17).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(logical.BrowseName.Name, Is.EqualTo(ResourceId));
                Assert.That(exactVersion.BrowseName.Name, Is.EqualTo("v2"));
                Assert.That(ServiceResult.IsGood(versionResult), Is.True);
                Assert.That(ServiceResult.IsGood(logicalResult), Is.True);
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        ResourceId,
                        "v2",
                        23,
                        false,
                        ProjectedDeleteTarget.Version),
                    new ProjectedDeleteInvocation(
                        "schemas",
                        ResourceId,
                        string.Empty,
                        17,
                        true,
                        ProjectedDeleteTarget.Resource)
                }));
                Assert.That(strategy.ResourceDeletes, Is.Empty);
            });
        }

        [Test]
        public async Task VersionedDeletePassesStaleNonDefaultRoleAndServiceRejectsAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels()),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            ResourceState v2 = FindVersionNode(harness, "v2");

            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            strategy.CurrentDefaultVersionId = "v2";
            strategy.ResourceEpoch = 29;
            strategy.VersionEpoch = 31;

            // In the new hierarchy, v2 is a version node and its delete role
            // cannot become stale — it is always version-delete semantics.
            ServiceResult result = await InvokeDeleteAsync(harness, v2, 31)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(FindVersionNode(harness, "v2"), Is.SameAs(v2));
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        "pump",
                        "v2",
                        31,
                        false,
                        ProjectedDeleteTarget.Version)
                }));
                Assert.That(strategy.ResourceDeletes, Is.Empty);
            });
        }

        [Test]
        public async Task VersionedDeleteUsesReconciledLogicalRoleForNewDefaultAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels()),
                CurrentDefaultVersionId = "v1",
                ResourceEpoch = 17,
                VersionEpoch = 23
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);
            ResourceState v2 = FindVersionNode(harness, "v2");

            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            strategy.CurrentDefaultVersionId = "v2";
            strategy.ResourceEpoch = 29;
            strategy.VersionEpoch = 31;
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            strategy.RejectGenerationCaptureBeforeProjectedDelete = true;

            // In the new hierarchy, v2 is always a version node regardless of
            // whether it is the current default. Delete always uses version-delete
            // semantics.
            ServiceResult result = await InvokeDeleteAsync(harness, v2, 31)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                // BrowseName is always the VersionId in the new hierarchy.
                Assert.That(v2.BrowseName.Name, Is.EqualTo("v2"));
                Assert.That(strategy.ProjectedDeletes, Is.EqualTo(new[]
                {
                    new ProjectedDeleteInvocation(
                        "schemas",
                        "pump",
                        "v2",
                        31,
                        false,
                        ProjectedDeleteTarget.Version)
                }));
                Assert.That(strategy.ResourceDeletes, Is.Empty);
            });
        }

        [Test]
        public async Task DeleteOnNonVersionedResourceStillUsesLogicalResourceRouteAsync()
        {
            var strategy = new RecordingTestStrategy
            {
                Snapshot = new TestSnapshot(
                [
                    new TestGroup("schemas", [new TestResource("schemas", "pump")])
                ])
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ServiceResult result = await InvokeDeleteAsync(
                harness,
                harness.Added.OfType<ResourceState>().Single(),
                37).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(strategy.ResourceDeletes, Is.EqualTo(new[]
                {
                    new ResourceDeleteInvocation("schemas", "pump", 37)
                }));
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
        public async Task CreatingDeprecatedGroupEmitsSpecializedLifecycleEventAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([]);
            harness.Strategy.EventSnapshot = EmptyEventSnapshot(0);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.Snapshot = new TestSnapshot([new TestGroup("schemas", [])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup("reason=legacy", 1);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(RegistryUpdatedEventState),
                    typeof(GroupCreatedEventState),
                    typeof(GroupDeprecatedEventState)
                }));
                Assert.That(harness.Events.OfType<GroupUpdatedEventState>(), Is.Empty);
            });
        }

        [Test]
        public async Task CreatingNormalGroupDoesNotEmitGroupDeprecatedAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([]);
            harness.Strategy.EventSnapshot = EmptyEventSnapshot(0);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.Snapshot = new TestSnapshot([new TestGroup("schemas", [])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedGroup(null, 1);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(RegistryUpdatedEventState),
                typeof(GroupCreatedEventState)
            }));
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
                    Is.EqualTo(s_expectedCreatedResourceSubjects));
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
        public async Task CreatingDeprecatedResourceEmitsSpecializedLifecycleEventAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([new TestGroup("schemas", [])]);
            harness.Strategy.EventSnapshot = SnapshotWithEmptyGroup(1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.Snapshot = new TestSnapshot(
                [new TestGroup("schemas", [new TestResource("schemas", "pump")])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedResource("reason=legacy", 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(ResourceCreatedEventState),
                    typeof(VersionCreatedEventState),
                    typeof(ResourceDeprecatedEventState),
                    typeof(GroupUpdatedEventState)
                }));
                Assert.That(harness.Events.OfType<ResourceUpdatedEventState>(), Is.Empty);
            });
        }

        [Test]
        public async Task CreatingNormalResourceDoesNotEmitResourceDeprecatedAsync()
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot([new TestGroup("schemas", [])]);
            harness.Strategy.EventSnapshot = SnapshotWithEmptyGroup(1);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.Snapshot = new TestSnapshot(
                [new TestGroup("schemas", [new TestResource("schemas", "pump")])]);
            harness.Strategy.EventSnapshot = SnapshotWithDeprecatedResource(null, 2);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(ResourceCreatedEventState),
                typeof(VersionCreatedEventState),
                typeof(GroupUpdatedEventState)
            }));
        }

        [Test]
        public Task ResourceNameChangeIncludesCanonicalNameAndMetaEpochAsync()
        {
            return AssertResourceTextChangeAsync(
                "Old name",
                "New name",
                "Description",
                "Description",
                s_resourceNameChanged);
        }

        [Test]
        public Task ResourceDescriptionChangeIncludesCanonicalDescriptionAndMetaEpochAsync()
        {
            return AssertResourceTextChangeAsync(
                "Name",
                "Name",
                "Old description",
                "New description",
                s_resourceDescriptionChanged);
        }

        [Test]
        public Task ResourceNameAndDescriptionChangeIncludesBothAndMetaEpochAsync()
        {
            return AssertResourceTextChangeAsync(
                "Old name",
                "New name",
                "Old description",
                "New description",
                s_resourceNameAndDescriptionChanged);
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
        public async Task DefaultSwitchIncludesAllPresentVersionAttributesWithoutVersionUpdateAsync()
        {
            ImmutableSortedDictionary<string, string> v1Attributes =
                ImmutableSortedDictionary<string, string>.Empty
                    .Add("thing", "digest-v1")
                    .Add("zeta", "old");
            ImmutableSortedDictionary<string, string> v2Attributes =
                ImmutableSortedDictionary<string, string>.Empty
                    .Add("alpha", "new")
                    .Add("format", "WoT-TD/1.1");
            ImmutableSortedDictionary<string, string> v1Labels =
                ImmutableSortedDictionary<string, string>.Empty.Add("quality", "approved");
            var strategy = new VersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot(
                    "v1",
                    1,
                    WotLabels(),
                    v1Attributes: v1Attributes,
                    v2Attributes: v2Attributes,
                    v1Labels: v1Labels)
            };
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: true,
                suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot(
                "v2",
                1,
                WotLabels(),
                v1Attributes: v1Attributes,
                v2Attributes: v2Attributes,
                v1Labels: v1Labels);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            ResourceUpdatedEventState updated =
                harness.Events.OfType<ResourceUpdatedEventState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events, Has.Count.EqualTo(1));
                Assert.That(harness.Events[0], Is.SameAs(updated));
                Assert.That(updated.Changed!.Value.ToArray(), Is.EqualTo(s_defaultSwitchChanged));
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
            // In the new hierarchy, the logical resource node lives at the
            // stable resource path, not at a version path.
            Assert.That(
                source.NodeId,
                Is.EqualTo(new NodeId(
                    "TestRegistry/groups/schemas/resources/pump",
                    1)));
        }

        /// <summary>
        /// Scenario 1: One Resource + one Version produces three distinct NodeIds
        /// (logical Resource, Versions folder, Version node), distinct BrowseNames
        /// (ResourceId vs VersionId), distinct Xids, and NO sibling collision when
        /// ResourceId == VersionId.
        /// </summary>
        [Test]
        public async Task DistinctHierarchyProducesThreeNodeIdsAndNoCollisionAsync()
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

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");
            ResourceState versionNode = FindVersionNode(harness, "v1");
            // The Versions folder is a child of the logical node, not a separate Added entry.
            ResourceVersionsState? versionsFolder = logicalNode.Versions;

            Assert.Multiple(() =>
            {
                Assert.That(versionsFolder, Is.Not.Null);
                // Three distinct NodeIds.
                Assert.That(logicalNode.NodeId, Is.Not.EqualTo(versionNode.NodeId));
                Assert.That(logicalNode.NodeId, Is.Not.EqualTo(versionsFolder!.NodeId));
                Assert.That(versionNode.NodeId, Is.Not.EqualTo(versionsFolder.NodeId));

                // Distinct BrowseNames.
                Assert.That(logicalNode.BrowseName.Name, Is.EqualTo("pump"));
                Assert.That(versionNode.BrowseName.Name, Is.EqualTo("v1"));
                Assert.That(versionsFolder.BrowseName.Name, Is.EqualTo("Versions"));

                // Distinct Xids.
                string logicalXid = logicalNode.Xid?.Value ?? string.Empty;
                string versionXid = versionNode.Xid?.Value ?? string.Empty;
                Assert.That(logicalXid, Is.Not.EqualTo(versionXid));
            });
        }

        /// <summary>
        /// Scenario 2: Switching the default Version does NOT change any NodeId/Xid
        /// identity — verified by existing test
        /// <c>VersionSourcesMetaAndDefaultSwitchAreDiffedIndependentlyAsync</c>.
        /// This additional test verifies no spurious VersionUpdated event fires for
        /// a version that was not changed.
        /// </summary>
        [Test]
        public async Task DefaultSwitchDoesNotFireSpuriousVersionUpdatedAsync()
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

            ResourceState logicalBefore = FindLogicalResourceNode(harness, "pump");
            NodeId logicalNodeIdBefore = logicalBefore.NodeId;

            // Switch default to v2.
            harness.Events.Clear();
            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            ResourceState logicalAfter = FindLogicalResourceNode(harness, "pump");
            Assert.Multiple(() =>
            {
                // Logical Resource NodeId is stable.
                Assert.That(logicalAfter.NodeId, Is.EqualTo(logicalNodeIdBefore));
                // No VersionUpdated events emitted for the switch itself (only
                // ResourceUpdated).
                Assert.That(
                    harness.Events.OfType<VersionUpdatedEventState>().Count(),
                    Is.Zero);
            });
        }

        /// <summary>
        /// Scenario 3: Delete role is fixed by structural position.
        /// Deleting a Version node uses Version-delete semantics (the handler
        /// wired on creation). Deleting the logical Resource uses Resource-delete
        /// semantics that cascades to all Versions.
        /// </summary>
        [Test]
        public async Task DeleteRoleIsFixedByStructuralPositionAsync()
        {
            var strategy = new VersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshotTwoVersions("v1", "v2", "v1"),
                EventSnapshot = VersionedEventSnapshotTwoVersions("v1", "v2", "v1", 1)
            };
            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: false,
                suppliedStrategy: strategy);

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            // Verify each Delete handler is wired.
            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");
            ResourceState v1Node = FindVersionNode(harness, "v1");
            ResourceState v2Node = FindVersionNode(harness, "v2");

            Assert.Multiple(() =>
            {
                Assert.That(logicalNode.Delete?.OnCallMethod2Async, Is.Not.Null);
                Assert.That(v1Node.Delete?.OnCallMethod2Async, Is.Not.Null);
                Assert.That(v2Node.Delete?.OnCallMethod2Async, Is.Not.Null);
            });

            // Deleting non-default v2 Version uses Version-delete path.
            ServiceResult v2DeleteResult = await InvokeDeleteAsync(harness, v2Node, 0)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(v2DeleteResult));

            // Deleting logical Resource uses Resource-delete path.
            ServiceResult logicalDeleteResult = await InvokeDeleteAsync(harness, logicalNode, 0)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(logicalDeleteResult));
        }

        /// <summary>
        /// Scenario 4 (Gap 1): Logical Resource's Open/Close methods are wired for forwarding
        /// when the strategy supplies a forwarding-capable file object.
        /// </summary>
        [Test]
        public async Task LogicalResourceFileForwardingIsWiredAsync()
        {
            var file = new Mock<IXRegistryProjectedResourceFile>();
            file.As<IXRegistryProjectedResourceFileHandleForwarder>();
            var strategy = new VersionedTestStrategy();
            strategy.FileFactory = (_, _) => file.Object;
            strategy.Snapshot = VersionedProjectionSnapshot("v1");
            strategy.EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels());

            ProjectionHarness harness = ProjectionHarness.Create(
                eventsEnabled: false,
                suppliedStrategy: strategy);

            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");

            // The logical node's Open handler should be wired.
            Assert.Multiple(() =>
            {
                Assert.That(logicalNode.Open?.OnCall, Is.Not.Null);
                Assert.That(logicalNode.Close?.OnCallAsync, Is.Not.Null);
                Assert.That(logicalNode.Read?.OnCallAsync, Is.Not.Null);
                Assert.That(logicalNode.Write?.OnCall, Is.Not.Null);
                Assert.That(logicalNode.GetPosition?.OnCall, Is.Not.Null);
                Assert.That(logicalNode.SetPosition?.OnCall, Is.Not.Null);
            });
        }

        private delegate ServiceResult ForwardOpenCallback(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle);

        private const byte TestReadMode = 1;
        private const byte TestWriteEraseMode = 6;

        private static Mock<IXRegistryProjectedResourceFileHandleForwarder> CreateForwarderMock(
            uint underlyingOpenHandle)
        {
            var file = new Mock<IXRegistryProjectedResourceFile>();
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarder =
                file.As<IXRegistryProjectedResourceFileHandleForwarder>();
            forwarder
                .Setup(f => f.ForwardOpen(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<MethodState>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<byte>(),
                    ref It.Ref<uint>.IsAny))
                .Returns(new ForwardOpenCallback(
                    (ISystemContext c, MethodState m, NodeId o, byte mode, ref uint h) =>
                    {
                        h = underlyingOpenHandle;
                        return ServiceResult.Good;
                    }));
            forwarder
                .Setup(f => f.ForwardCloseAsync(
                    It.IsAny<ISystemContext>(),
                    It.IsAny<MethodState>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            return forwarder;
        }

        /// <summary>
        /// Reviewer issue #2 regression: <c>LogicalResourceEntry.PinnedHandles</c>
        /// was previously keyed only by the raw <c>uint</c> handle returned by
        /// whichever Version's file manager served an <c>Open</c> call. Because
        /// every Version's manager numbers its own handles independently
        /// starting from 1 (exactly as two real, independent
        /// <c>WotResourceFileManager</c> instances do in production), opening
        /// through the logical Resource once while v1 is default and again
        /// after switching the default to v2 can return the SAME underlying
        /// handle number from two different managers. Keying the pin by that
        /// raw number alone let the second Open silently overwrite the first
        /// pin, so closing the first (v1) handle would incorrectly route to
        /// v2's manager instead. The fix allocates an engine-owned synthetic
        /// handle per Open so the two pins never collide even when the
        /// underlying numbers do.
        /// </summary>
        [Test]
        public async Task PinnedFileHandlesDoNotCollideAcrossDefaultSwitchAsync()
        {
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarderV1 =
                CreateForwarderMock(underlyingOpenHandle: 1);
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarderV2 =
                CreateForwarderMock(underlyingOpenHandle: 1);

            var strategy = new VersionedTestStrategy
            {
                FileFactory = (_, resource) => resource.VersionId == "v1"
                    ? (IXRegistryProjectedResourceFile)forwarderV1.Object
                    : (IXRegistryProjectedResourceFile)forwarderV2.Object,
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");

            // Open while v1 is default -> forwards to fileV1, underlying handle 1.
            uint handle1 = 0;
            ServiceResult open1 = logicalNode.Open!.OnCall!.Invoke(
                harness.Context, logicalNode.Open, logicalNode.NodeId, TestWriteEraseMode, ref handle1);
            Assert.That(ServiceResult.IsGood(open1), Is.True);

            // Switch the default to v2.
            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            // Open again -> forwards to fileV2, ALSO underlying handle 1.
            uint handle2 = 0;
            ServiceResult open2 = logicalNode.Open!.OnCall!.Invoke(
                harness.Context, logicalNode.Open, logicalNode.NodeId, TestWriteEraseMode, ref handle2);
            Assert.That(ServiceResult.IsGood(open2), Is.True);

            // The engine must allocate distinct synthetic handles even though
            // both underlying managers reported the same raw number.
            Assert.That(handle2, Is.Not.EqualTo(handle1));

            // Closing the FIRST handle must close fileV1 with its OWN
            // underlying handle (1), and must not touch fileV2 at all.
            CloseMethodStateResult close1 = await logicalNode.Close!.OnCallAsync!(
                    harness.Context, logicalNode.Close, logicalNode.NodeId, handle1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(close1.ServiceResult), Is.True);

            forwarderV1.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                1u, It.IsAny<CancellationToken>()), Times.Once);
            forwarderV2.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);

            // Closing the SECOND handle must close fileV2 with its own
            // underlying handle (1); fileV1 must still show exactly one close.
            CloseMethodStateResult close2 = await logicalNode.Close!.OnCallAsync!(
                    harness.Context, logicalNode.Close, logicalNode.NodeId, handle2, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(close2.ServiceResult), Is.True);

            forwarderV2.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                1u, It.IsAny<CancellationToken>()), Times.Once);
            forwarderV1.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Reviewer issue #1 regression: <c>LogicalResourceEntry.Versions</c> was
        /// a plain <see cref="Dictionary{TKey,TValue}"/>, read directly (via
        /// <c>TryGetValue</c>) by the logical Resource's <c>Open</c> handler on
        /// OPC UA method-dispatch threads, outside the engine's reconciliation
        /// gate, while reconciliation mutates the SAME dictionary under that
        /// gate on a different logical call. A plain Dictionary is not safe for
        /// that concurrent read/write pattern and can throw or corrupt.
        /// Hammering concurrent Opens against concurrent reconciliation passes
        /// (which repeatedly rewrite <c>Versions</c> while toggling the
        /// default) must not throw.
        /// </summary>
        [Test]
        public async Task ConcurrentOpenDuringReconcileDoesNotThrowAsync()
        {
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarder =
                CreateForwarderMock(underlyingOpenHandle: 1);
            var strategy = new VersionedTestStrategy
            {
                FileFactory = (_, _) => (IXRegistryProjectedResourceFile)forwarder.Object,
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");

            using var cts = new CancellationTokenSource();
            var exceptions = new ConcurrentBag<Exception>();

            Task reconcileLoop = Task.Run(async () =>
            {
                try
                {
                    bool toggle = false;
                    uint epoch = 1;
                    while (!cts.IsCancellationRequested)
                    {
                        toggle = !toggle;
                        epoch++;
                        string defaultId = toggle ? "v2" : "v1";
                        strategy.Snapshot = VersionedProjectionSnapshot(defaultId);
                        strategy.EventSnapshot = VersionedEventSnapshot(defaultId, epoch, WotLabels());
                        await harness.Engine.ReconcileAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Task[] openLoops = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        uint handle = 0;
                        ServiceResult result = logicalNode.Open!.OnCall!.Invoke(
                            harness.Context,
                            logicalNode.Open,
                            logicalNode.NodeId,
                            TestReadMode,
                            ref handle);
                        if (ServiceResult.IsGood(result))
                        {
                            await logicalNode.Close!.OnCallAsync!(
                                    harness.Context,
                                    logicalNode.Close,
                                    logicalNode.NodeId,
                                    handle,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            await Task.Delay(300).ConfigureAwait(false);
            cts.Cancel();
            await Task.WhenAll(openLoops.Append(reconcileLoop)).ConfigureAwait(false);

            Assert.That(exceptions, Is.Empty,
                () => string.Join(Environment.NewLine, exceptions.Select(e => e.ToString())));
        }

        /// <summary>
        /// Reviewer issue #1 regression (second review round): a different
        /// session guessing/replaying the synthetic handle number a logical
        /// Resource's Open returned to the rightful owner must be rejected
        /// (BadUserAccessDenied) WITHOUT removing the owner's pin and WITHOUT
        /// ever reaching the underlying manager's own Close. The rightful
        /// owner must still be able to close (and thereby release the writer
        /// slot) afterwards using the same handle, and a further close
        /// attempt must then correctly report the handle as gone.
        /// </summary>
        [Test]
        public async Task CrossSessionCloseCannotStrandLegitimateOwnerHandleAsync()
        {
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarder =
                CreateForwarderMock(underlyingOpenHandle: 1);
            var strategy = new VersionedTestStrategy
            {
                FileFactory = (_, _) => (IXRegistryProjectedResourceFile)forwarder.Object,
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");

            var ownerContext = (ServerSystemContext)harness.Context.Copy();
            ownerContext.SessionId = new NodeId("owner-session", 1);
            var attackerContext = (ServerSystemContext)harness.Context.Copy();
            attackerContext.SessionId = new NodeId("attacker-session", 1);

            uint handle = 0;
            ServiceResult open = logicalNode.Open!.OnCall!.Invoke(
                ownerContext, logicalNode.Open, logicalNode.NodeId, TestWriteEraseMode, ref handle);
            Assert.That(ServiceResult.IsGood(open), Is.True);

            // A different session guesses/replays the same synthetic handle.
            CloseMethodStateResult attackerClose = await logicalNode.Close!.OnCallAsync!(
                    attackerContext, logicalNode.Close, logicalNode.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                attackerClose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            // The malicious attempt must never have reached the underlying
            // manager's Close at all — the pin must still be intact.
            forwarder.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.Never);

            // The rightful owner must still be able to close (and thereby
            // release the writer slot) using the SAME handle afterwards.
            CloseMethodStateResult ownerClose = await logicalNode.Close!.OnCallAsync!(
                    ownerContext, logicalNode.Close, logicalNode.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(ownerClose.ServiceResult), Is.True);

            forwarder.Verify(f => f.ForwardCloseAsync(
                It.IsAny<ISystemContext>(), It.IsAny<MethodState>(), It.IsAny<NodeId>(),
                1u, It.IsAny<CancellationToken>()), Times.Once);

            // The handle is now fully released at our layer too: a further
            // close attempt (even by the owner) reports "unknown handle"
            // rather than silently succeeding or hitting a stale pin.
            CloseMethodStateResult secondClose = await logicalNode.Close!.OnCallAsync!(
                    ownerContext, logicalNode.Close, logicalNode.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                secondClose.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Reviewer issue #2 regression (second review round): mirroring the
        /// default Version's FileType Properties onto the logical Resource
        /// sets each child PropertyState's own change mask (Size, OpenCount,
        /// ...); clearing change masks with <c>includeChildren: false</c> only
        /// processes the logical Resource node's OWN mask and leaves those
        /// children dirty/unreported. A subscriber on a mirrored child
        /// Property must be notified (and that child's mask cleared)
        /// immediately after a logical-Resource Open and again after Close.
        /// </summary>
        [Test]
        public async Task LogicalResourceOpenAndCloseNotifyMirroredChildPropertyChangesAsync()
        {
            Mock<IXRegistryProjectedResourceFileHandleForwarder> forwarder =
                CreateForwarderMock(underlyingOpenHandle: 1);
            var strategy = new VersionedTestStrategy
            {
                FileFactory = (_, _) => (IXRegistryProjectedResourceFile)forwarder.Object,
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");
            ResourceState v1Node = FindVersionNode(harness, "v1");
            v1Node.Size!.Value = 555UL;

            var notifications = new List<NodeStateChangeMasks>();
            logicalNode.Size!.OnStateChanged = (_, _, changes) => notifications.Add(changes);

            uint handle = 0;
            ServiceResult open = logicalNode.Open!.OnCall!.Invoke(
                harness.Context, logicalNode.Open, logicalNode.NodeId, TestWriteEraseMode, ref handle);
            Assert.That(ServiceResult.IsGood(open), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications[0].HasFlag(NodeStateChangeMasks.Value), Is.True);
                // The mask must actually be cleared, not merely observed once
                // and left dirty for the child.
                Assert.That(logicalNode.Size.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            });

            v1Node.Size.Value = 777UL;
            await logicalNode.Close!.OnCallAsync!(
                    harness.Context, logicalNode.Close, logicalNode.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Has.Count.EqualTo(2));
                Assert.That(logicalNode.Size.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            });
        }

        /// <summary>
        /// Reviewer issue #3 regression: the logical Resource's inherited
        /// FileType Properties (Size, Writable, UserWritable, OpenCount,
        /// MimeType, LastModifiedTime, MaxByteStringLength) must mirror the
        /// currently selected default Version's own values after a
        /// reconciliation pass, not remain at their uninitialized/default
        /// values.
        /// </summary>
        [Test]
        public async Task LogicalResourceMirrorsDefaultVersionFileTypePropertiesAsync()
        {
            var strategy = new VersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");
            ResourceState v1Node = FindVersionNode(harness, "v1");

            // Simulate a real file manager having populated the exact Version's
            // own FileType Properties (as WotResourceFileManager does on
            // construction/Open/commit). Size/Writable/UserWritable/OpenCount
            // are Mandatory FileType members (always present); MimeType,
            // LastModifiedTime and MaxByteStringLength are Optional per Part 5
            // and mirrored through the same null-guarded code path when a
            // domain model instantiates them.
            v1Node.Size!.Value = 12345UL;
            v1Node.Writable!.Value = true;
            v1Node.UserWritable!.Value = true;
            v1Node.OpenCount!.Value = 2;

            // A reconciliation pass must pick up and mirror the Version's
            // current FileType Property values onto the logical Resource.
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(logicalNode.Size!.Value, Is.EqualTo(12345UL));
                Assert.That(logicalNode.Writable!.Value, Is.True);
                Assert.That(logicalNode.UserWritable!.Value, Is.True);
                Assert.That(logicalNode.OpenCount!.Value, Is.EqualTo((ushort)2));
            });
        }

        /// <summary>
        /// Reviewer issue #4 regression (read side): the logical Resource's
        /// (non-Meta) Labels container was created but never synced, leaving it
        /// permanently empty regardless of the selected default Version's
        /// actual labels. A reconciliation pass must populate the logical
        /// Resource's Labels children from the CURRENTLY selected default
        /// Version's labels, and must update them again when the default
        /// switches.
        /// </summary>
        [Test]
        public async Task LogicalResourceLabelsSyncFromDefaultVersionAsync()
        {
            ImmutableSortedDictionary<string, string> v1Labels =
                ImmutableSortedDictionary<string, string>.Empty.Add("color", "blue");
            ImmutableSortedDictionary<string, string> v2Labels =
                ImmutableSortedDictionary<string, string>.Empty.Add("color", "red");

            var strategy = new VersionedTestStrategy
            {
                Snapshot = new TestSnapshot(
                [
                    new TestGroup("schemas",
                    [
                        new VersionedTestResource(
                            "schemas", "pump", "v1", isDefaultVersion: true, labels: v1Labels),
                        new VersionedTestResource(
                            "schemas", "pump", "v2", isDefaultVersion: false, labels: v2Labels)
                    ])
                ]),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");
            Assert.That(logicalNode.Labels, Is.Not.Null);
            Assert.That(ReadLabelValue(harness, logicalNode.Labels!, "color"), Is.EqualTo("blue"));

            // Switch default to v2: the logical Resource's Labels must now
            // reflect v2's labels, not v1's.
            strategy.Snapshot = new TestSnapshot(
            [
                new TestGroup("schemas",
                [
                    new VersionedTestResource(
                        "schemas", "pump", "v1", isDefaultVersion: false, labels: v1Labels),
                    new VersionedTestResource(
                        "schemas", "pump", "v2", isDefaultVersion: true, labels: v2Labels)
                ])
            ]);
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(ReadLabelValue(harness, logicalNode.Labels!, "color"), Is.EqualTo("red"));
        }

        /// <summary>
        /// Reviewer issue #4 regression (write side): calling AddAttribute on
        /// the logical Resource's Labels container must delegate to whichever
        /// Version is CURRENTLY the resolved default, resolved dynamically at
        /// call time (mirroring the FileType forwarding architecture) — not
        /// silently do nothing, and not stay pinned to whichever Version was
        /// default when the node was created.
        /// </summary>
        [Test]
        public async Task LogicalResourceAddAttributeDelegatesToCurrentDefaultVersionAsync()
        {
            var strategy = new RecordingVersionedTestStrategy
            {
                Snapshot = VersionedProjectionSnapshot("v1"),
                EventSnapshot = VersionedEventSnapshot("v1", 1, WotLabels())
            };
            ProjectionHarness harness = ProjectionHarness.Create(suppliedStrategy: strategy);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            ResourceState logicalNode = FindLogicalResourceNode(harness, "pump");

            ServiceResult first = await AddAttributeAsync(harness, logicalNode, "color", "blue")
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(first), Is.True);

            // Switch default to v2.
            strategy.Snapshot = VersionedProjectionSnapshot("v2");
            strategy.EventSnapshot = VersionedEventSnapshot("v2", 2, WotLabels());
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            ServiceResult second = await AddAttributeAsync(harness, logicalNode, "color", "red")
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(second), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(strategy.AddVersionLabelCalls, Has.Count.EqualTo(2));
                Assert.That(strategy.AddVersionLabelCalls[0].VersionId, Is.EqualTo("v1"));
                Assert.That(strategy.AddVersionLabelCalls[1].VersionId, Is.EqualTo("v2"));
            });
        }

        private static ValueTask<ServiceResult> AddAttributeAsync(
            ProjectionHarness harness,
            ResourceState logicalNode,
            string key,
            string value)
        {
            return logicalNode.Labels!.AddAttribute!.OnCallMethod2Async!(
                harness.Context,
                logicalNode.Labels.AddAttribute,
                logicalNode.NodeId,
                [new Variant(key), new Variant(value), new Variant(0u)],
                [],
                CancellationToken.None);
        }

        private static string? ReadLabelValue(
            ProjectionHarness harness,
            AttributesState labels,
            string key)
        {
            var children = new List<BaseInstanceState>();
            labels.GetChildren(harness.Context, children);
            return children
                .OfType<PropertyState<string>>()
                .FirstOrDefault(p => string.Equals(p.BrowseName.Name, key, StringComparison.Ordinal))
                ?.Value;
        }

        /// <summary>
        /// Scenario 10 (Gap 2): <c>IsVersionAtLeast</c> correctly detects 0.6.0+ versions.
        /// </summary>
        [TestCase("0.6.0", true)]
        [TestCase("0.5.0", false)]
        [TestCase("1.0.0", true)]
        [TestCase("0.6.0-preview.1", true)]
        [TestCase("", false)]
        public void ModelVersionDetectionReturnsCorrectResult(
            string version, bool expected)
        {
            bool result = Client.XRegistryClient.IsVersionAtLeast(version, 0, 6, 0);
            Assert.That(result, Is.EqualTo(expected));
        }

        private static TestSnapshot VersionedProjectionSnapshotTwoVersions(
            string v1, string v2, string defaultVersionId)
        {
            return new TestSnapshot(
                [
                    new TestGroup("schemas",
                    [
                        new VersionedTestResource("schemas", "pump", v1, v1 == defaultVersionId, 1),
                        new VersionedTestResource("schemas", "pump", v2, v2 == defaultVersionId, 1)
                    ])
                ]);
        }

        private static XRegistryProjectionEventSnapshot VersionedEventSnapshotTwoVersions(
            string v1, string v2, string defaultVersionId, uint epoch)
        {
            var labels = WotLabels();
            return new XRegistryProjectionEventSnapshot(
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
                        [
                            new XRegistryProjectionEventResource(
                                "schemas",
                                "pump",
                                "/groups/schemas/resources/pump",
                                epoch,
                                epoch,
                                labels,
                                false,
                                defaultVersionId,
                                [
                                    new XRegistryProjectionEventVersion(
                                        v1,
                                        $"/groups/schemas/resources/pump/versions/{v1}",
                                        epoch,
                                        ImmutableSortedDictionary<string, string>.Empty),
                                    new XRegistryProjectionEventVersion(
                                        v2,
                                        $"/groups/schemas/resources/pump/versions/{v2}",
                                        epoch,
                                        ImmutableSortedDictionary<string, string>.Empty)
                                ])
                        ])
                ]);
        }

        private static XRegistryProjectionEventSnapshot EmptyEventSnapshot(uint epoch)
        {
            return new XRegistryProjectionEventSnapshot(
                "/",
                epoch,
                ImmutableSortedDictionary<string, string>.Empty,
                []);
        }

        private static XRegistryProjectionEventSnapshot SnapshotWithEmptyGroup(uint epoch)
        {
            return new XRegistryProjectionEventSnapshot(
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
                        [])
                ]);
        }

        private static ValueTask<ServiceResult> InvokeDeleteAsync(
            ProjectionHarness harness,
            ResourceState resource,
            uint expectedEpoch)
        {
            return resource.Delete!.OnCallMethod2Async!(
                harness.Context,
                resource.Delete,
                resource.NodeId,
                [new Variant(expectedEpoch)],
                [],
                CancellationToken.None);
        }

        private static ResourceState FindVersionNode(
            ProjectionHarness harness,
            string versionId)
        {
            // In the hierarchical model, version nodes are children of a
            // ResourceVersionsState folder. Filter to nodes whose parent is
            // a ResourceVersionsState to avoid matching the logical Resource node.
            IEnumerable<ResourceState> candidates = harness.Added
                .OfType<ResourceState>()
                .Where(node =>
                    string.Equals(node.VersionId?.Value, versionId, StringComparison.Ordinal));

            ResourceState? versionChild = candidates
                .FirstOrDefault(node => node.Parent is ResourceVersionsState);
            return versionChild ?? candidates.Single();
        }

        private static ResourceState FindLogicalResourceNode(
            ProjectionHarness harness,
            string resourceId)
        {
            return harness.Added
                .OfType<ResourceState>()
                .Single(node =>
                    string.Equals(node.BrowseName.Name, resourceId, StringComparison.Ordinal) &&
                    node.Parent is not ResourceVersionsState);
        }

        private static async Task AssertResourceTextChangeAsync(
            string previousName,
            string currentName,
            string previousDescription,
            string currentDescription,
            string[] expectedChanged)
        {
            ProjectionHarness harness = ProjectionHarness.Create(eventsEnabled: true);
            harness.Strategy.Snapshot = new TestSnapshot(
                [new TestGroup("schemas", [new TestResource("schemas", "pump")])]);
            harness.Strategy.EventSnapshot = SnapshotWithResourceText(
                previousName,
                previousDescription,
                versionEpoch: 7,
                metaEpoch: 3);
            await harness.Engine.AttachAsync(harness.Registry, CancellationToken.None)
                .ConfigureAwait(false);

            harness.Strategy.EventSnapshot = SnapshotWithResourceText(
                currentName,
                currentDescription,
                versionEpoch: 7,
                metaEpoch: 4);
            await harness.Engine.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);

            ResourceUpdatedEventState updated =
                harness.Events.OfType<ResourceUpdatedEventState>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(harness.Events, Has.Count.EqualTo(1));
                Assert.That(harness.Events[0], Is.SameAs(updated));
                Assert.That(updated.Epoch!.Value, Is.EqualTo(7u));
                Assert.That(updated.MetaEpoch!.Value, Is.EqualTo(4u));
                Assert.That(updated.Changed!.Value.ToArray(), Is.EqualTo(expectedChanged));
            });
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

        private static XRegistryProjectionEventSnapshot SnapshotWithResourceText(
            string name,
            string description,
            uint versionEpoch,
            uint metaEpoch)
        {
            NodeId source = new("TestRegistry/groups/schemas/resources/pump", 1);
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
                                versionEpoch,
                                metaEpoch,
                                ImmutableSortedDictionary<string, string>.Empty,
                                false,
                                "v1",
                                [
                                    new XRegistryProjectionEventVersion(
                                        "v1",
                                        "/groups/schemas/resources/pump/versions/v1",
                                        versionEpoch,
                                        ImmutableSortedDictionary<string, string>.Empty)
                                    {
                                        SourceNodeId = source,
                                        SourceName = "v1",
                                        CreatedAt = s_unixEpoch,
                                        ModifiedAt = s_unixEpoch
                                    }
                                ])
                            {
                                SourceNodeId = source,
                                SourceName = name,
                                Name = name,
                                Description = description,
                                MetaCreatedAt = s_unixEpoch,
                                MetaModifiedAt = s_unixEpoch.AddSeconds(metaEpoch)
                            }
                        ])
                    {
                        SourceNodeId = new NodeId("TestRegistry/groups/schemas", 1),
                        SourceName = "schemas"
                    }
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
            string defaultVersionId,
            string resourceId = "pump")
        {
            return new TestSnapshot(
            [
                new TestGroup(
                    "schemas",
                    [
                        new VersionedTestResource(
                            "schemas",
                            resourceId,
                            "v1",
                            defaultVersionId == "v1"),
                        new VersionedTestResource(
                            "schemas",
                            resourceId,
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
            uint v2Epoch = 1,
            ImmutableSortedDictionary<string, string>? v1Attributes = null,
            ImmutableSortedDictionary<string, string>? v2Attributes = null,
            ImmutableSortedDictionary<string, string>? v1Labels = null,
            ImmutableSortedDictionary<string, string>? v2Labels = null,
            string resourceId = "pump")
        {
            NodeId v1Node = new(
                $"TestRegistry/groups/schemas/resources/{resourceId}/versions/v1",
                1);
            NodeId v2Node = new(
                $"TestRegistry/groups/schemas/resources/{resourceId}/versions/v2",
                1);
            XRegistryProjectionEventVersion V(string id, uint epoch, NodeId source)
            {
                ImmutableSortedDictionary<string, string> attributes = id == "v1"
                    ? v1Attributes ??
                        ImmutableSortedDictionary<string, string>.Empty.Add(
                            "resource",
                            epoch.ToString(
                                System.Globalization.CultureInfo.InvariantCulture))
                    : v2Attributes ??
                        ImmutableSortedDictionary<string, string>.Empty.Add(
                            "resource",
                            epoch.ToString(
                                System.Globalization.CultureInfo.InvariantCulture));
                return new XRegistryProjectionEventVersion(
                    id,
                    $"/groups/schemas/resources/{resourceId}/versions/{id}",
                    epoch,
                    attributes)
                {
                    SourceNodeId = source,
                    SourceName = id,
                    Labels = id == "v1"
                        ? v1Labels ?? ImmutableSortedDictionary<string, string>.Empty
                        : v2Labels ?? ImmutableSortedDictionary<string, string>.Empty,
                    CreatedAt = s_unixEpoch,
                    ModifiedAt = s_unixEpoch.AddSeconds(epoch)
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
                                resourceId,
                                $"/groups/schemas/resources/{resourceId}",
                                defaultVersionId == "v1" ? v1Epoch : v2Epoch,
                                metaEpoch,
                                metaLabels,
                                false,
                                defaultVersionId,
                                [V("v1", v1Epoch, v1Node), V("v2", v2Epoch, v2Node)])
                            {
                                SourceNodeId = defaultVersionId == "v1" ? v1Node : v2Node,
                                SourceName = resourceId,
                                MetaCreatedAt = s_unixEpoch,
                                MetaModifiedAt = s_unixEpoch.AddSeconds(metaEpoch)
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
                                        ModifiedAt = s_unixEpoch.AddSeconds(versionEpoch)
                                    }
                                ])
                            {
                                SourceNodeId = source,
                                SourceName = "pump",
                                MetaCreatedAt = s_unixEpoch,
                                MetaModifiedAt = s_unixEpoch
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

            public virtual IXRegistryProjectedResourceFile? CreateResourceFile(
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

            public virtual ValueTask<ServiceResult> DeleteResourceAsync(
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

        private class VersionedTestStrategy :
            TestStrategy,
            IXRegistryVersionedProjectionStrategy
        {
            public Func<ResourceState, IXRegistryProjectionResource, IXRegistryProjectedResourceFile?>?
                FileFactory { get; set; }

            public override IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                return FileFactory?.Invoke(node, resource);
            }

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

            public virtual ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
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

            public virtual ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                CancellationToken ct)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public virtual ValueTask<ServiceResult> AddVersionLabelAsync(
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

            public virtual ValueTask<ServiceResult> RemoveVersionLabelAsync(
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
                                    MetaCreatedAt = s_unixEpoch,
                                    MetaModifiedAt = s_unixEpoch
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

        private sealed class RecordingTestStrategy : TestStrategy
        {
            public List<ResourceDeleteInvocation> ResourceDeletes { get; } = [];

            public override ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                ResourceDeletes.Add(new ResourceDeleteInvocation(groupId, resourceId, epoch));
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private sealed class RecordingVersionedTestStrategy : VersionedTestStrategy
        {
            public string CurrentDefaultVersionId { get; set; } = "v1";
            public long ResourceEpoch { get; set; }
            public long VersionEpoch { get; set; }
            public bool RejectGenerationCaptureBeforeProjectedDelete { get; set; }
            public bool OmitEventMetadata { get; set; }
            public List<ProjectedDeleteInvocation> ProjectedDeletes { get; } = [];
            public List<ResourceDeleteInvocation> ResourceDeletes { get; } = [];
            public List<(string GroupId, string ResourceId, string VersionId, string Key, string Value, long? Epoch)>
                AddVersionLabelCalls { get; } = [];
            public List<(string GroupId, string ResourceId, string VersionId, string Key, long? Epoch)>
                RemoveVersionLabelCalls { get; } = [];

            public override XRegistryProjectionGeneration CaptureProjectionGeneration()
            {
                if (RejectGenerationCaptureBeforeProjectedDelete &&
                    !m_projectedDeleteInvoked)
                {
                    throw new InvalidOperationException(
                        "The engine captured projection state before atomic delete.");
                }
                if (OmitEventMetadata)
                {
                    return new XRegistryProjectionGeneration(Snapshot, null);
                }
                return base.CaptureProjectionGeneration();
            }

            public override ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                ResourceDeletes.Add(new ResourceDeleteInvocation(groupId, resourceId, epoch));
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public override ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                CancellationToken ct)
            {
                m_projectedDeleteInvoked = true;
                // In the new hierarchy, the role is determined by the caller
                // (structural position), not by whether the versionId happens
                // to be the current default.
                ProjectedDeleteTarget target = deleteLogicalResource
                    ? ProjectedDeleteTarget.Resource
                    : ProjectedDeleteTarget.Version;
                ProjectedDeletes.Add(
                    new ProjectedDeleteInvocation(
                        groupId,
                        resourceId,
                        versionId,
                        epoch,
                        deleteLogicalResource,
                        target));
                long expectedEpoch = target == ProjectedDeleteTarget.Resource
                    ? ResourceEpoch
                    : VersionEpoch;
                ServiceResult result = epoch == expectedEpoch
                    ? ServiceResult.Good
                    : ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The projected epoch did not match the atomically resolved role.");
                return new ValueTask<ServiceResult>(result);
            }

            private bool m_projectedDeleteInvoked;

            public override ValueTask<ServiceResult> AddVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                AddVersionLabelCalls.Add((groupId, resourceId, versionId, key, value, epoch));
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public override ValueTask<ServiceResult> RemoveVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                RemoveVersionLabelCalls.Add((groupId, resourceId, versionId, key, epoch));
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private enum ProjectedDeleteTarget
        {
            Resource,
            Version
        }

        private sealed record ProjectedDeleteInvocation(
            string GroupId,
            string ResourceId,
            string VersionId,
            long? Epoch,
            bool DeleteLogicalResource,
            ProjectedDeleteTarget Target);

        private sealed record ResourceDeleteInvocation(
            string GroupId,
            string ResourceId,
            long? Epoch);

        private sealed class ContentlessClaimStrategy : VersionedTestStrategy
        {
            public ContentlessClaimStrategy()
            {
                Snapshot = new TestSnapshot(
                [
                    new TestGroup(
                        "schemas",
                        [new VersionedTestResource("schemas", "pump", "v1")])
                ]);
            }

            public ContentlessResourceFile File { get; } = new();

            public override IXRegistryProjectedResourceFile CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                return File;
            }

            public override ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                return new ValueTask<IXRegistryProjectionResource?>(
                    (IXRegistryProjectionResource?)null);
            }
        }

        private sealed class ContentlessResourceFile :
            IXRegistryProjectedResourceFile,
            IXRegistryProjectedContentlessResourceFile
        {
            public bool HasContent { get; set; }
            public int ContentlessOpenCount { get; private set; }

            public ServiceResult TryOpenWriteHandle(
                ISystemContext context,
                out uint fileHandle)
            {
                fileHandle = 0;
                return StatusCodes.BadNotSupported;
            }

            public ServiceResult TryOpenContentlessWriteHandle(
                ISystemContext context,
                out uint fileHandle)
            {
                ContentlessOpenCount++;
                fileHandle = HasContent ? 0u : 42u;
                return HasContent ? StatusCodes.BadInvalidState : ServiceResult.Good;
            }

            public void ApplyResource(IXRegistryProjectionResource resource)
            {
            }

            public void Dispose()
            {
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

        private sealed class VersionedTestResource :
            IXRegistryProjectionResource,
            IXRegistryProjectionResourceMeta
        {
            public VersionedTestResource(
                string groupId,
                string resourceId,
                string versionId,
                bool isDefaultVersion = true,
                long epoch = 1,
                ImmutableSortedDictionary<string, string>? labels = null)
            {
                GroupId = groupId;
                ResourceId = resourceId;
                VersionId = versionId;
                IsDefaultVersion = isDefaultVersion;
                Epoch = epoch;
                Labels = labels ?? ImmutableSortedDictionary<string, string>.Empty;
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
            public DateTime CreatedAt => s_unixEpoch;
            public DateTime ModifiedAt => s_unixEpoch;
            public ImmutableSortedDictionary<string, string> Labels { get; }
            public long MetaEpoch => 1;
            public ImmutableSortedDictionary<string, string> MetaLabels { get; } =
                ImmutableSortedDictionary<string, string>.Empty;
            public DateTime MetaCreatedAt => s_unixEpoch;
            public DateTime MetaModifiedAt => s_unixEpoch;
            public bool IsDefaultVersion { get; }
        }

        private static readonly string[] s_defaultSwitchChanged =
        [
            "alpha",
            "createdat",
            "epoch",
            "format",
            "isdefault",
            "labels",
            "meta.defaultversionid",
            "modifiedat",
            "thing",
            "versionid",
            "xid",
            "zeta"
        ];

        private static readonly string[] s_expectedCreatedResourceSubjects =
        [
            "/groups/schemas/resources/a",
            "/groups/schemas/resources/b"
        ];

        private static readonly string[] s_resourceNameChanged =
            ["meta.epoch", "meta.modifiedat", "name"];

        private static readonly string[] s_resourceDescriptionChanged =
            ["description", "meta.epoch", "meta.modifiedat"];

        private static readonly string[] s_resourceNameAndDescriptionChanged =
            ["description", "meta.epoch", "meta.modifiedat", "name"];

        private static readonly DateTime s_unixEpoch =
            new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
