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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class WotCanonicalGraphStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotCanonicalGraphStoreTests),
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        [Test]
        [Platform("Win")]
        public async Task PreparedGraphCarrierPersistsWithAllAffectedAncestorMetadata()
        {
            using var store = new FileWotRegistryStore(m_root);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource child = await AddAsync(registry, "child").ConfigureAwait(false);
            WotResource parent = await AddAsync(registry, "parent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            WotRegistrySnapshot intended = GraphSnapshot(before, child, parent);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                intended, captured, WotRegistryCommitScope.ProjectionMetadata).ConfigureAwait(false);

            await prepared.CommitAsync().ConfigureAwait(false);

            using var observer = new FileWotRegistryStore(m_root);
            WotRegistrySnapshot reloaded = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(reloaded.CanonicalViewGraphState, Is.EqualTo(s_graph));
            Assert.That(reloaded.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(reloaded.Generation, Is.EqualTo(before.Generation + 1));
            AssertAffected(reloaded, child, "child-view");
            AssertAffected(reloaded, parent, "parent-view");
        }

        [Test]
        [Platform("Win")]
        public async Task PreparedGraphNoncommitRetainsTheWholePriorImage()
        {
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    if (armed)
                    {
                        throw new IOException("Graph decision rejected before replacement.");
                    }
                    File.Replace(source, destination, backup);
                });
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource child = await AddAsync(registry, "child").ConfigureAwait(false);
            WotResource parent = await AddAsync(registry, "parent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                GraphSnapshot(before, child, parent), captured, WotRegistryCommitScope.ProjectionMetadata)
                .ConfigureAwait(false);
            armed = true;

            await Assert.ThatAsync(
                async () => await prepared.CommitAsync().ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            using var observer = new FileWotRegistryStore(m_root);
            WotRegistrySnapshot reloaded = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(reloaded.Generation, Is.EqualTo(before.Generation));
            Assert.That(reloaded.RefreshGeneration, Is.Zero);
            Assert.That(reloaded.CanonicalViewGraphState.IsNull, Is.True);
            Assert.That(reloaded.FindResource(child.GroupId, child.ResourceId)!.RootNodeId.IsNull, Is.True);
            Assert.That(reloaded.FindResource(parent.GroupId, parent.ResourceId)!.RootNodeId.IsNull, Is.True);
        }

        [Test]
        [Platform("Win")]
        public async Task GraphDurabilityWarningCarriesTheWholeCommittedImage()
        {
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    if (armed)
                    {
                        throw new IOException("Graph replacement reported a post-switch failure.");
                    }
                });
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource child = await AddAsync(registry, "child").ConfigureAwait(false);
            WotResource parent = await AddAsync(registry, "parent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                GraphSnapshot(before, child, parent), captured, WotRegistryCommitScope.ProjectionMetadata)
                .ConfigureAwait(false);
            armed = true;
            WotRegistryCommitDurabilityUncertainException? outcome = null;

            try
            {
                await prepared.CommitAsync().ConfigureAwait(false);
                Assert.Fail("The injected post-switch failure must remain observable.");
            }
            catch (WotRegistryCommitDurabilityUncertainException failure)
            {
                outcome = failure;
            }

            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.CommittedSnapshot.CanonicalViewGraphState, Is.EqualTo(s_graph));
            Assert.That(outcome.CommittedSnapshot.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(outcome.CommittedGeneration, Is.EqualTo(before.Generation + 1));
            AssertAffected(outcome.CommittedSnapshot, child, "child-view");
            AssertAffected(outcome.CommittedSnapshot, parent, "parent-view");
        }

        [Test]
        public void GraphCarrierOwnsInputAndSurvivesUnrelatedMetadataUpdates()
        {
            byte[] input = [0x43, 0x32, 0x01];
            var snapshot = new WotRegistrySnapshot(
                7, ImmutableDictionary<string, WotResourceGroup>.Empty, null, ByteString.From(input), 3);
            input[0] = 0;

            WotRegistrySnapshot relabeled = snapshot.WithLabels(snapshot.Labels.Add("label", "value"), 8);
            WotRegistrySnapshot retained = relabeled.WithPublicationState(9, 4);
            WotRegistrySnapshot cleared = retained.WithPublicationState(10, 5, ByteString.Empty);

            Assert.That(snapshot.CanonicalViewGraphState, Is.EqualTo(ByteString.From(new byte[] { 0x43, 0x32, 0x01 })));
            Assert.That(relabeled.CanonicalViewGraphState, Is.EqualTo(snapshot.CanonicalViewGraphState));
            Assert.That(relabeled.RefreshGeneration, Is.EqualTo(3u));
            Assert.That(retained.CanonicalViewGraphState, Is.EqualTo(snapshot.CanonicalViewGraphState));
            Assert.That(retained.RefreshGeneration, Is.EqualTo(4u));
            Assert.That(cleared.CanonicalViewGraphState.IsNull, Is.False);
            Assert.That(cleared.CanonicalViewGraphState.Length, Is.Zero);
            Assert.That(cleared.RefreshGeneration, Is.EqualTo(5u));
            Assert.That(snapshot.Generation, Is.EqualTo(7));
        }

        [Test]
        public void CommittedPublicationDerivesGenerationFromItsRegistryOwnerImage()
        {
            WotRegistrySnapshot snapshot = WotRegistrySnapshot.Empty.WithPublicationState(8, 3, s_graph);
            var view = new WotViewProjectionHandle("/groups/views/resources/child", new NodeId("view", 2), 1);
            var publication = new WotCommittedPublicationState(snapshot, [view]);

            Assert.That(publication.RegistrySnapshot, Is.SameAs(snapshot));
            Assert.That(publication.RefreshGeneration, Is.EqualTo(3u));
            Assert.That(publication.Views.Count, Is.EqualTo(1));
            Assert.That(publication.Views[0], Is.SameAs(view));
            Assert.That(publication.ActiveBindingPlans.Count, Is.Zero);
        }

        [Test]
        public void PreparedGraphRequiresCompleteBytesAndUniqueAffectedResources()
        {
            Assert.That(
                () => new WotPreparedViewGraphState(default, [], []),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new WotPreparedViewGraphState(s_graph, [], ["resource", "resource"]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new WotPreparedViewGraphState(s_graph, [], [string.Empty]),
                Throws.TypeOf<ArgumentException>());

            var clear = new WotPreparedViewGraphState(ByteString.Empty, [], ["removed-resource"]);
            Assert.That(clear.CanonicalViewGraphState.IsNull, Is.False);
            Assert.That(clear.CanonicalViewGraphState.Length, Is.Zero);
            Assert.That(clear.Views.Count, Is.Zero);
            Assert.That(clear.AffectedResourceXids[0], Is.EqualTo("removed-resource"));
        }

        [Test]
        [Platform("Win")]
        public async Task EmptyGraphImageRoundTripsWithoutBecomingAbsent()
        {
            using var store = new FileWotRegistryStore(m_root);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            await AddAsync(registry, "child").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            Assert.That(before.CanonicalViewGraphState.IsNull, Is.True);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            WotRegistrySnapshot empty = before.WithPublicationState(before.Generation + 1, 1, ByteString.Empty);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                empty, captured, WotRegistryCommitScope.ProjectionMetadata).ConfigureAwait(false);

            await prepared.CommitAsync().ConfigureAwait(false);

            using var observerStore = new FileWotRegistryStore(m_root);
            using var observer = new WotRegistryService(observerStore);
            await observer.InitializeAsync().ConfigureAwait(false);
            Assert.That(observer.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(observer.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(observer.Current.CanonicalViewGraphState.IsNull, Is.False,
                "An explicit empty graph is not an absent legacy graph.");
            Assert.That(observer.Current.CanonicalViewGraphState.Length, Is.Zero);
            await observer.InitializeAsync().ConfigureAwait(false);
            Assert.That(observer.Current.CanonicalViewGraphState.IsNull, Is.False,
                "Restoring Version incarnations must preserve the root graph state.");
        }

        [Test]
        public void CommittedPublicationOwnsBindingCapabilitySnapshots()
        {
            const string uri = "urn:binding:committed-image";
            var capability = new WoTBindingCapabilityDataType
            {
                BindingUri = uri
            };
            var plan = new WotBindingPlan("resource", [capability], [], [], [])
                .WithDeclarationContext(true);
            var publication = new WotCommittedPublicationState(
                WotRegistrySnapshot.Empty.WithPublicationState(8, 3, s_graph), activeBindingPlans: [plan]);
            capability.BindingUri = "urn:changed-input";

            WotBindingPlan exposed = publication.ActiveBindingPlans[0];
            Assert.That(exposed.Capabilities[0].BindingUri, Is.EqualTo(uri));
            Assert.That(exposed.ResourceXid, Is.EqualTo(plan.ResourceXid));
            Assert.That(exposed.IsDeclarationContext, Is.True);
            exposed.Capabilities[0].BindingUri = "urn:changed-output";

            Assert.That(publication.ActiveBindingPlans[0].Capabilities[0].BindingUri, Is.EqualTo(uri));
            Assert.That(publication.RefreshGeneration, Is.EqualTo(3u));
        }

        private static async Task<WotResource> AddAsync(WotRegistryService registry, string id)
        {
            WotRegistryMutationResult created = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = id,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:" + id))
            }).ConfigureAwait(false);
            Assert.That(created.Changed, Is.True, created.Message);
            return created.Resource ?? throw new InvalidOperationException("The test resource was not created.");
        }

        private static WotRegistrySnapshot GraphSnapshot(
            WotRegistrySnapshot before, WotResource child, WotResource parent)
        {
            WotRegistrySnapshot next = WithRoot(before, child, "child-view");
            next = WithRoot(next, parent, "parent-view");
            return next.WithPublicationState(before.Generation + 1, 1, s_graph);
        }

        private static WotRegistrySnapshot WithRoot(WotRegistrySnapshot snapshot, WotResource resource, string id)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            WotResource projected = resource.With(
                activeVersionId: resource.DefaultVersionId,
                loadState: WoTLoadStateEnum.Active,
                rootNodeId: new NodeId(id, 2),
                materializedNodeCount: 1,
                refreshGeneration: 1);
            return snapshot.WithGroup(
                group.WithResources(group.Resources.SetItem(resource.ResourceId, projected), group.Epoch),
                snapshot.Generation);
        }

        private static void AssertAffected(WotRegistrySnapshot snapshot, WotResource original, string root)
        {
            WotResource resource = snapshot.FindResource(original.GroupId, original.ResourceId)!;
            Assert.That(resource.RootNodeId, Is.EqualTo(new NodeId(root, 2)));
            Assert.That(resource.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(resource.MetaEpoch, Is.EqualTo(original.MetaEpoch));
            Assert.That(resource.DefaultVersion!.Epoch, Is.EqualTo(original.DefaultVersion!.Epoch));
            Assert.That(resource.DefaultVersion.Digest, Is.EqualTo(original.DefaultVersion.Digest));
        }

        private static readonly ByteString s_graph = ByteString.From("canonical-graph-v1"u8.ToArray());
        private string m_root = null!;
    }
}
