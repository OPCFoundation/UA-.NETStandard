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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotDependencySnapshotTests
    {
        [Test]
        [Platform("Win")]
        public async Task ContentlessDependencyRecordsFailureWithoutAbortingIndependentWork()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            WotRegistryService registry = await runtime.CreateRegistryAsync();
            await registry.GetOrCreateGroupAsync("models", WoTDocumentKindEnum.ThingModel);
            await registry.GetOrCreateVersionAsync("models", "unwritten", "v1", WoTDocumentKindEnum.ThingModel);
            WotResource placeholder = registry.Current.FindResource("models", "unwritten")!;
            WotRegistryMutationResult failed = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "dependent", VersionId = "v1",
                Content = ByteString.From(TestMaterialization.Td("urn:dependent", extendsHrefs: placeholder.Xid))
            });
            WotRegistryMutationResult independent = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "independent", VersionId = "v1",
                Content = ByteString.From(TestMaterialization.Td("urn:independent"))
            });
            using WotMaterializationCoordinator coordinator = Coordinator(registry, runtime);
            WotRefreshResult result = await coordinator.RefreshAsync(new WotRefreshRequest
            {
                Selection = [Selector(failed.Resource!), Selector(independent.Resource!)],
                RequestId = "contentless"
            });
            Assert.That(result.Results.Single(row => row.ResourceId == "dependent").Outcome,
                Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(result.Results.Single(row => row.ResourceId == "independent").LoadState,
                Is.EqualTo(WoTLoadStateEnum.Active));
            WotResourceVersion version = registry.Current.FindResource("things", "dependent")!.DefaultVersion!;
            Assert.That(version.DependencySnapshot, Is.Null);
            Assert.That(version.LastDependencyAttempt, Is.Not.Null);
            Assert.That(version.LastDependencyAttempt!.Targets.Count, Is.Zero);
            Assert.That(version.LastDependencyAttempt.Edges.Count, Is.EqualTo(1));
            Assert.That(version.LastDependencyAttempt.Edges[0].Resolved, Is.False);
            Assert.That(version.LastDependencyAttempt.Edges[0].TargetHref, Is.EqualTo(placeholder.Xid));
        }

        [Test]
        [Platform("Win")]
        public async Task PreDependencyManifestHydrationSupportsPreparedMetadataCommit()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            string root = Path.Combine(Path.GetTempPath(), "wot-b2-upgrade", Guid.NewGuid().ToString("N"));
            try
            {
                WotResource source;
                using (var store = new FileWotRegistryStore(root))
                using (var registry = new WotRegistryService(store))
                {
                    await registry.InitializeAsync();
                    (_, source) = await RegisterPairAsync(registry);
                    WotRegistrySnapshot before = registry.Current;
                    var groups = before.Groups;
                    foreach (WotResourceGroup group in before.Groups.Values)
                    {
                        var resources = group.Resources;
                        foreach (WotResource resource in group.Resources.Values)
                        {
                            var versions = resource.Versions.Select(version => new WotResourceVersion(
                                version.VersionId, version.Digest, version.ContentLength, version.ContentType,
                                version.Format, version.CreatedAt, version.ModifiedAt)
                            {
                                Epoch = version.Epoch,
                                Labels = version.Labels,
                                DocumentId = version.DocumentId,
                                Title = version.Title,
                                BaseUri = version.BaseUri,
                                ModelVersion = version.ModelVersion
                            }).ToImmutableArray();
                            resources = resources.SetItem(resource.ResourceId, resource.With(versions: versions));
                        }
                        groups = groups.SetItem(group.GroupId, group.WithResources(resources, group.Epoch));
                    }
                    await store.CommitAsync(new WotRegistrySnapshot(before.Generation + 1, groups, before.Labels));
                }
                using var reloadedStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadedStore);
                await reloaded.InitializeAsync();
                Assert.That(reloaded.Current.FindResource(source.GroupId, source.ResourceId)!
                    .DefaultVersion!.Dependencies, Is.Not.Null);
                using WotMaterializationCoordinator coordinator = Coordinator(reloaded, runtime);
                WotRefreshResult result = await coordinator.RefreshAsync(Request(source, "upgraded"));
                Assert.That(result.Results.Single(row => row.ResourceId == source.ResourceId).LoadState,
                    Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(Observation(reloaded, source).RequestId, Is.EqualTo("upgraded"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        [Platform("Win")]
        public async Task CommittedSnapshotPinsExactOriginVersionAndRawEdge()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            WotRegistryService registry = await runtime.CreateRegistryAsync();
            (WotResource model, WotResource source) = await RegisterPairAsync(registry);
            using WotMaterializationCoordinator coordinator = Coordinator(registry, runtime);

            await coordinator.RefreshAsync(Request(source, "committed"));
            WotDependencySnapshot snapshot = Observation(registry, source);
            WotDependency edge = snapshot.Edges[0];
            WotDependencyTargetPin target = snapshot.Targets[0];

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.SourceVersionId, Is.EqualTo("v1"));
                Assert.That(snapshot.IsCommitted, Is.True);
                Assert.That(snapshot.Generation, Is.EqualTo(coordinator.Generation));
                Assert.That(snapshot.RequestId, Is.EqualTo("committed"));
                Assert.That(snapshot.EffectiveInputDigest.Length, Is.EqualTo(32));
                Assert.That(edge.SourceXid, Is.EqualTo(source.Xid));
                Assert.That(edge.TargetHref, Is.EqualTo("urn:snapshot:model"));
                Assert.That(edge.TargetXid, Is.EqualTo(model.Xid));
                Assert.That(edge.Resolved, Is.True);
                Assert.That(target.EdgeIndex, Is.Zero);
                Assert.That(target.OriginRegistry!.OriginUri, Is.EqualTo("urn:registry:b2"));
                Assert.That(target.VersionXid, Is.EqualTo(model.Xid + "/versions/v1"));
                Assert.That(target.DocumentUri, Is.EqualTo("urn:snapshot:model"));
                Assert.That(target.ContentDigest, Is.EqualTo(model.DefaultVersion!.Digest));
                Assert.That(target.VersionNodeId.IsNull, Is.True, "This direct provider has no OPC UA endpoint.");
            });
        }

        [Test]
        [Platform("Win")]
        public async Task FailedAttemptDoesNotReplaceCommittedGraph()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            WotRegistryService registry = await runtime.CreateRegistryAsync();
            (_, WotResource source) = await RegisterPairAsync(registry);
            var converter = new FakeWotDocumentConverter();
            using WotMaterializationCoordinator coordinator = Coordinator(registry, runtime, converter);
            await coordinator.RefreshAsync(Request(source, "first"));
            WotDependencySnapshot committed = Observation(registry, source);
            uint generation = coordinator.Generation;
            converter.MarkInvalid(source.ResourceId);
            WotRefreshRequest request = Request(source, "failed");
            request.Options.Force = true;

            WotRefreshResult result = await coordinator.RefreshAsync(request);
            WotResourceVersion version = registry.Current.FindResource(source.GroupId, source.ResourceId)!
                .FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Single(row => row.ResourceId == source.ResourceId).Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(version.DependencySnapshot, Is.SameAs(committed));
                Assert.That(version.LastDependencyAttempt, Is.Not.Null);
                Assert.That(version.LastDependencyAttempt!.IsCommitted, Is.False);
                Assert.That(version.LastDependencyAttempt.RequestId, Is.EqualTo("failed"));
                Assert.That(version.LastDependencyAttempt.Generation, Is.EqualTo(generation));
                Assert.That(version.LastDependencyAttempt.EffectiveInputDigest.Length, Is.Zero);
            });
        }

        [Test]
        [Platform("Win")]
        public async Task DryRunLeavesBothDependencyObservationsUnchanged()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            WotRegistryService registry = await runtime.CreateRegistryAsync();
            (_, WotResource source) = await RegisterPairAsync(registry);
            using WotMaterializationCoordinator coordinator = Coordinator(registry, runtime);
            await coordinator.RefreshAsync(Request(source, "first"));
            WotResourceVersion before = registry.Current.FindResource(source.GroupId, source.ResourceId)!
                .FindVersion("v1")!;
            WotRefreshRequest request = Request(source, "dry");
            request.Options.DryRun = true;
            request.Options.Force = true;
            long generation = registry.Current.Generation;

            await coordinator.RefreshAsync(request);
            WotResourceVersion after = registry.Current.FindResource(source.GroupId, source.ResourceId)!
                .FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(before.DependencySnapshot, Is.Not.Null);
                Assert.That(after.DependencySnapshot, Is.SameAs(before.DependencySnapshot));
                Assert.That(after.LastDependencyAttempt, Is.SameAs(before.LastDependencyAttempt));
                Assert.That(registry.Current.Generation, Is.EqualTo(generation));
            });
        }

        [Test]
        [Platform("Win")]
        public async Task SameInputNoopRecordsAttemptWithoutReplacingCommittedGraph()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            WotRegistryService registry = await runtime.CreateRegistryAsync();
            (_, WotResource source) = await RegisterPairAsync(registry);
            using WotMaterializationCoordinator coordinator = Coordinator(registry, runtime);
            await coordinator.RefreshAsync(Request(source, "first"));
            WotDependencySnapshot committed = Observation(registry, source);

            WotRefreshResult result = await coordinator.RefreshAsync(Request(source, "noop"));
            WotResourceVersion version = registry.Current.FindResource(source.GroupId, source.ResourceId)!
                .FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Results.Single(row => row.ResourceId == source.ResourceId).Outcome,
                    Is.EqualTo(WoTOutcomeEnum.Unchanged));
                Assert.That(version.DependencySnapshot, Is.SameAs(committed));
                Assert.That(version.LastDependencyAttempt, Is.Not.Null);
                Assert.That(version.LastDependencyAttempt!.IsCommitted, Is.False);
                Assert.That(version.LastDependencyAttempt.RequestId, Is.EqualTo("noop"));
                Assert.That(version.LastDependencyAttempt.EffectiveInputDigest,
                    Is.EqualTo(committed.EffectiveInputDigest));
                Assert.That(result.NewGeneration, Is.EqualTo(committed.Generation));
            });
        }

        [Test]
        [Platform("Win")]
        public async Task FileReloadKeepsSnapshotAndReverseDependencyMetadata()
        {
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync();
            string root = Path.Combine(Path.GetTempPath(), "wot-b2-snapshot", Guid.NewGuid().ToString("N"));
            try
            {
                WotResource model;
                WotResource source;
                WotDependencySnapshot committed;
                using (var store = new FileWotRegistryStore(root))
                using (var registry = new WotRegistryService(store))
                using (WotMaterializationCoordinator coordinator = Coordinator(registry, runtime))
                {
                    await registry.InitializeAsync();
                    (model, source) = await RegisterPairAsync(registry);
                    await coordinator.RefreshAsync(Request(source, "persisted"));
                    committed = Observation(registry, source);
                }
                using var reloadedStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadedStore);
                await reloaded.InitializeAsync();

                WotDependencySnapshot restored = Observation(reloaded, source);
                ArrayOf<WotSelectedResource> dependents = WotDependencyGraph.SelectResources(
                    reloaded.Current, [Selector(model)], includeDependents: true);
                string[] expectedResources = ["model", "source"];
                Assert.Multiple(() =>
                {
                    Assert.That(restored.SourceVersionId, Is.EqualTo("v1"));
                    Assert.That(restored.RequestId, Is.EqualTo("persisted"));
                    Assert.That(restored.EffectiveInputDigest, Is.EqualTo(committed.EffectiveInputDigest));
                    Assert.That(restored.Targets[0].OriginRegistry!.OriginUri, Is.EqualTo("urn:registry:b2"));
                    Assert.That(restored.Targets[0].VersionXid, Is.EqualTo(model.Xid + "/versions/v1"));
                    Assert.That(dependents.ToList().Select(selected => selected.Resource.ResourceId),
                        Is.EquivalentTo(expectedResources));
                });
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public async Task CaptureRetainsBytesAndSemanticSccAcrossStoreChanges()
        {
            using var registry = new WotRegistryService();
            ByteString original = ByteString.From(Encoding.UTF8.GetBytes("""
                {"id":"urn:a","title":"A","links":[{"rel":"ua:ConnectedTo","href":"urn:b"}]}
                """));
            WotRegistryMutationResult a = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "a", VersionId = "v1", Content = original
            });
            WotRegistryMutationResult b = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "b", VersionId = "v1",
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {"id":"urn:b","title":"B","links":[{"rel":"ua:ConnectedTo","href":"urn:a"}]}
                    """))
            });
            Assert.That(a.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(b.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            using WotMaterializationSnapshot snapshot = await WotDependencyGraph.CaptureAsync(
                registry, [Selector(a.Resource!)], false, 64);

            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "a", VersionId = "v2",
                Content = ByteString.From(TestMaterialization.Td("urn:a", "changed"))
            });
            await registry.InitializeAsync();

            string[] expectedMembers = ["a", "b"];
            string[] expectedTargets = ["urn:a", "urn:b"];
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.GetContent(a.Resource!, a.Resource!.FindVersion("v1")!), Is.EqualTo(original));
                Assert.That(snapshot.Registry.FindResource("things", "a")!.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(registry.Current.FindResource("things", "a")!.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(snapshot.Closures[0].StronglyConnectedComponents[0].Members.ToList()
                    .Select(member => member.ResourceId), Is.EquivalentTo(expectedMembers));
                Assert.That(snapshot.Closures[0].Dependencies.Select(edge => edge.TargetHref),
                    Is.EquivalentTo(expectedTargets));
            });
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                snapshot.GetContent(a.Resource!, registry.Current.FindResource("things", "a")!.DefaultVersion!));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void MutatingGeneratedObservationDoesNotChangeCapturedGraph()
        {
            ByteString digest = WotContentDigest.Compute("source"u8);
            var snapshot = new WotDependencySnapshot(
                "v1", 1, "immutable", new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), true, digest,
                [new WotDependency("/a", "urn:b", "/b", "tm:extends", true)],
                [new WotDependencyTargetPin(0, new WotRegistryOrigin("urn:registry:b2"),
                    "/b/versions/v1", "urn:b", ExpandedNodeId.Null, digest)]);
            WoTDependencySnapshotDataType mutable = snapshot.ToDataType();
            mutable.Edges[0].TargetUri = "urn:tampered";
            mutable.Targets[0].VersionXid = "/b/versions/v2";
            mutable.Targets[0].OriginRegistry.OriginUri = "urn:other";

            WoTDependencySnapshotDataType reread = snapshot.ToDataType();

            Assert.Multiple(() =>
            {
                Assert.That(reread.Edges[0].TargetUri, Is.EqualTo("urn:b"));
                Assert.That(reread.Targets[0].VersionXid, Is.EqualTo("/b/versions/v1"));
                Assert.That(reread.Targets[0].OriginRegistry.OriginUri, Is.EqualTo("urn:registry:b2"));
            });
        }

        [Test]
        public void ResolvedEdgeWithoutExactTargetPinIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new WotDependencySnapshot(
                "v1", 1, "invalid", DateTime.UtcNow, true, WotContentDigest.Compute("source"u8),
                [new WotDependency("/a", "urn:b", "/b", "tm:extends", true)], []));
        }

        [Test]
        public void SnapshotRuntimeMatchesRequestedGate()
        {
            string? expected = Environment.GetEnvironmentVariable("WOT_B2_EXPECTED_RUNTIME");
            if (!string.IsNullOrEmpty(expected))
            {
                Assert.That(Environment.Version.ToString(), Is.EqualTo(expected));
            }
#if NETFRAMEWORK
            Assert.That(Environment.Version.Major, Is.EqualTo(4));
#elif NET8_0
            Assert.That(Environment.Version.Major, Is.EqualTo(8));
#elif NET9_0
            Assert.That(Environment.Version.Major, Is.EqualTo(9));
#elif NET10_0
            Assert.That(Environment.Version.Major, Is.EqualTo(10));
#endif
        }

        private static WotMaterializationCoordinator Coordinator(
            WotRegistryService registry, PreparedWotTestRuntime runtime, FakeWotDocumentConverter? converter = null)
        {
            return new WotMaterializationCoordinator(
                registry, runtime.Host, documentConverter: converter ?? new FakeWotDocumentConverter())
            {
                RegistryOrigin = new WotRegistryOrigin("urn:registry:b2"),
                ServerNamespaceUris = runtime.Namespaces
            };
        }

        private static async Task<(WotResource Model, WotResource Source)> RegisterPairAsync(
            WotRegistryService registry)
        {
            WotRegistryMutationResult model = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "models", ResourceId = "model", VersionId = "v1", Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(TestMaterialization.Tm("urn:snapshot:model"))
            });
            WotRegistryMutationResult source = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = "things", ResourceId = "source", VersionId = "v1",
                Content = ByteString.From(TestMaterialization.Td(
                    "urn:snapshot:source", extendsHrefs: "urn:snapshot:model"))
            });
            Assert.That(model.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), model.Message);
            Assert.That(source.Outcome, Is.EqualTo(WoTOutcomeEnum.Success), source.Message);
            return (model.Resource!, source.Resource!);
        }

        private static WoTResourceSelectorDataType Selector(WotResource resource)
        {
            return new WoTResourceSelectorDataType
            {
                Kind = resource.Kind, GroupId = resource.GroupId, ResourceId = resource.ResourceId, VersionId = "v1"
            };
        }

        private static WotRefreshRequest Request(WotResource resource, string requestId)
        {
            return new WotRefreshRequest
            {
                RequestId = requestId,
                Selection = [Selector(resource)],
                Options = new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerClosure }
            };
        }

        private static WotDependencySnapshot Observation(WotRegistryService registry, WotResource resource)
        {
            WotDependencySnapshot? snapshot = registry.Current.FindResource(resource.GroupId, resource.ResourceId)!
                .FindVersion("v1")!.DependencySnapshot;
            Assert.That(snapshot, Is.Not.Null, "The committed exact Version must own its actual dependency graph.");
            return snapshot!;
        }
    }
}
