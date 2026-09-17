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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class WotPreparedMetadataStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotPreparedMetadataStoreTests),
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
        public async Task ProjectionMetadataUpdatesDoNotRereadIndependentBlobContent()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResource independent = await AddAsync(registry, "independent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            content.ClearReads();

            await registry.ApplyProjectionResultsAsync([Projection(selected, 1)]).ConfigureAwait(false);

            Assert.That(content.ReadsFor(independent.DefaultVersion!.DigestHex), Is.Zero,
                "Metadata publication must not reacquire independent bytes through either read API.");
            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(registry.Current.FindResource(
                selected.GroupId, selected.ResourceId)!.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(registry.Current.FindResource(
                independent.GroupId, independent.ResourceId), Is.SameAs(independent));
            Assert.That(registry.Current.FindResource(
                selected.GroupId, selected.ResourceId)!.MetaEpoch, Is.EqualTo(selected.MetaEpoch));
            content.ClearReads();

            await registry.ApplyProjectionResultsAsync([Projection(selected, 2)]).ConfigureAwait(false);

            Assert.That(content.ReadsFor(independent.DefaultVersion.DigestHex), Is.Zero,
                "A new metadata generation must retain validated content evidence rather than rehash the corpus.");
            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 2));
            Assert.That(registry.Current.FindResource(
                selected.GroupId, selected.ResourceId)!.RefreshGeneration, Is.EqualTo(2u));
        }

        [Test]
        public async Task ProjectionMetadataRejectsStaleManifestBeforeReadingBlobContent()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResource independent = await AddAsync(registry, "independent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using var winningStore = new FileWotRegistryStore(m_root, content);
            using var winner = new WotRegistryService(winningStore);
            await winner.InitializeAsync().ConfigureAwait(false);
            await AddAsync(winner, "winner").ConfigureAwait(false);
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await registry.ApplyProjectionResultsAsync([Projection(selected, 1)])
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(independent.DefaultVersion!.DigestHex), Is.Zero,
                "A stale expected manifest must be rejected before any independent content acquisition.");
            Assert.That(content.ReadsFor(selected.DefaultVersion!.DigestHex), Is.Zero);
        }

        [Test]
        public async Task ValidatedGenerationCannotCrossStoreOwners()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            using var foreign = new FileWotRegistryStore(Path.Combine(m_root, "foreign"), content);
            await foreign.LoadAsync().ConfigureAwait(false);
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await foreign.PrepareCommitAsync(
                    NextProjection(registry.Current, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);

            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
            Assert.That(File.Exists(Path.Combine(m_root, "foreign", "manifest.json")), Is.False);
        }

        [Test]
        public async Task DisposedValidatedGenerationCannotPrepare()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            captured.Dispose();
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    NextProjection(before, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
        }

        [Test]
        public async Task ReloadInvalidatesPreviouslyCapturedGeneration()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await store.LoadAsync().ConfigureAwait(false);
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    NextProjection(registry.Current, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
        }

        [Test]
        public async Task PreparedStoreCommitRetainsInputAfterCallerDisposesItsLease()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                NextProjection(before, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                .ConfigureAwait(false);
            captured.Dispose();
            using var observer = new FileWotRegistryStore(m_root, content);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation));
            content.ClearReads();

            await prepared.CommitAsync().ConfigureAwait(false);

            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation + 1));
            await Assert.ThatAsync(
                async () => await prepared.CommitAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedStoreCommitDisposalAbortsWithoutPublication()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                NextProjection(before, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                .ConfigureAwait(false);

            await prepared.DisposeAsync().ConfigureAwait(false);

            using var observer = new FileWotRegistryStore(m_root, content);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation));
            await Assert.ThatAsync(
                async () => await prepared.CommitAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task PreparedMetadataFreezesMutableValidationState()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResourceVersion version = resource.DefaultVersion!;
            var validation = new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success,
                FormatReason = "captured"
            };
            WotResource projected = resource.With(
                versions: resource.Versions.SetItem(
                    resource.Versions.IndexOf(version), version.With(validation: validation)),
                validation: validation,
                refreshGeneration: 1);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await using IWotRegistryPreparedCommit prepared = await store.PrepareCommitAsync(
                ReplaceResource(registry.Current, projected),
                captured,
                WotRegistryCommitScope.ProjectionMetadata).ConfigureAwait(false);

            validation.FormatReason = "changed by caller";

            WotResource frozen = prepared.IntendedSnapshot.FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.That(frozen.Validation!.FormatReason, Is.EqualTo("captured"));
            Assert.That(frozen.DefaultVersion!.Validation!.FormatReason, Is.EqualTo("captured"));
            WoTValidationOutcomeDataType observed = frozen.Validation!;
            observed.FormatReason = "changed through snapshot getter";
            Assert.That(frozen.Validation!.FormatReason, Is.EqualTo("captured"));

            await prepared.CommitAsync().ConfigureAwait(false);

            using var observer = new FileWotRegistryStore(m_root, content);
            WotRegistrySnapshot reloaded = await observer.LoadAsync().ConfigureAwait(false);
            Assert.That(reloaded.FindResource(resource.GroupId, resource.ResourceId)!
                .DefaultVersion!.Validation!.FormatReason, Is.EqualTo("captured"));
        }

        [TestCase("resource-epoch")]
        [TestCase("version-epoch")]
        [TestCase("content")]
        [TestCase("membership")]
        [TestCase("labels")]
        public async Task ProjectionScopeRejectsAuthoritativeMutation(string mutation)
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            WotResourceVersion version = resource.DefaultVersion!;
            WotResource changed = mutation switch
            {
                "resource-epoch" => resource.With(epoch: resource.MetaEpoch + 1),
                "version-epoch" => resource.With(versions: resource.Versions.SetItem(
                    resource.Versions.IndexOf(version), version.With(epoch: version.Epoch + 1))),
                "content" => resource.With(versions: resource.Versions.SetItem(
                    resource.Versions.IndexOf(version), version.With(digest: WotContentDigest.Compute("changed"u8)))),
                _ => resource
            };
            WotRegistrySnapshot intended = mutation switch
            {
                "membership" => before.WithoutGroup(resource.GroupId, before.Generation + 1),
                "labels" => before.WithLabels(before.Labels.Add("illegal", "change"), before.Generation + 1),
                _ => ReplaceResource(before, changed)
            };
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    intended, captured, WotRegistryCommitScope.ProjectionMetadata).ConfigureAwait(false),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(version.DigestHex), Is.Zero);
        }

        [Test]
        public async Task FullMutationStillValidatesNewBlobHash()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            content.CorruptNextWrite = true;

            await Assert.ThatAsync(
                () => AddAsync(registry, "corrupted"),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(registry.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "corrupted"), Is.Null);
        }

        [Test]
        public async Task UnsupportedContentProviderDoesNotAdvertisePreparedCommit()
        {
            using var store = new FileWotRegistryStore(m_root, new InMemoryResourceStore());
            await store.LoadAsync().ConfigureAwait(false);

            Assert.That(store.SupportsPreparedCommits, Is.False);
            await Assert.ThatAsync(
                async () => await store.CaptureValidatedGenerationAsync().ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task StockFileLeaseProtectsContentAndReleasesWithItsOwners()
        {
            using var store = new FileWotRegistryStore(m_root);
            Assert.That(store.SupportsPreparedCommits,
                Is.EqualTo(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)));
            if (!store.SupportsPreparedCommits)
            {
                return;
            }
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            string blob = Path.Combine(m_root, "blobs", resource.DefaultVersion!.DigestHex + ".bin");
            byte[] original = File.ReadAllBytes(blob);

            using (Stream reader = LocalFileSystem.Instance.OpenRead(blob))
            {
                Assert.That(reader.CanRead, Is.True);
                Assert.That(reader.CanWrite, Is.False);
            }
            Assert.That(() => File.WriteAllBytes(blob, new byte[original.Length]), Throws.TypeOf<IOException>());
            Assert.That(() => File.Delete(blob), Throws.TypeOf<IOException>());
            await registry.ApplyProjectionResultsAsync([Projection(resource, 1)]).ConfigureAwait(false);
            Assert.That(File.ReadAllBytes(blob), Is.EqualTo(original));
            captured.Dispose();
            registry.Dispose();

            File.WriteAllBytes(blob, new byte[original.Length]);

            await Assert.ThatAsync(
                async () => await store.LoadAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task MetadataNotCommittedRetainsPreviousGenerationWithoutBlobReads()
        {
            using var content = new RecordingLeasedResourceStore();
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    if (armed)
                    {
                        throw new IOException("Replacement rejected before switching.");
                    }
                    File.Replace(source, destination, backup);
                },
                resourceStore: content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResource independent = await AddAsync(registry, "independent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            armed = true;
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await registry.ApplyProjectionResultsAsync([Projection(selected, 1)])
                    .ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitNotCommittedException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(independent.DefaultVersion!.DigestHex), Is.Zero);
            using var observer = new FileWotRegistryStore(m_root, content);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataDurabilityUncertainPublishesValidatedGeneration(bool afterSync)
        {
            using var content = new RecordingLeasedResourceStore();
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: phase =>
                {
                    if (armed && afterSync && phase == FileWotRegistryStore.DirectorySyncPhase.RootAfterManifest)
                    {
                        throw new IOException("Post-switch directory synchronization failed.");
                    }
                },
                manifestReplace: (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    if (armed && !afterSync)
                    {
                        throw new IOException("Replacement reported failure after switching.");
                    }
                },
                resourceStore: content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResource independent = await AddAsync(registry, "independent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            armed = true;
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await registry.ApplyProjectionResultsAsync([Projection(selected, 1)])
                    .ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitDurabilityUncertainException>()
                    .With.Property(nameof(WotRegistryCommitDurabilityUncertainException.CommittedGeneration))
                    .EqualTo(before.Generation + 1)).ConfigureAwait(false);

            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            Assert.That(registry.Current.FindResource(
                selected.GroupId, selected.ResourceId)!.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(content.ReadsFor(independent.DefaultVersion!.DigestHex), Is.Zero);
            armed = false;
            using var observer = new FileWotRegistryStore(m_root, content);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation + 1));
        }

        [Test]
        public async Task MetadataIndeterminateBlocksMutationAndPreservesRecoveryEvidence()
        {
            using var content = new RecordingLeasedResourceStore();
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    if (armed)
                    {
                        File.Move(destination, backup);
                        throw new IOException("Replacement left no authoritative primary.");
                    }
                    File.Replace(source, destination, backup);
                },
                resourceStore: content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotResource independent = await AddAsync(registry, "independent").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            armed = true;
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await registry.ApplyProjectionResultsAsync([Projection(selected, 1)])
                    .ConfigureAwait(false),
                Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(independent.DefaultVersion!.DigestHex), Is.Zero);
            Assert.That(Directory.GetFiles(m_root, "manifest.json.tmp-*"), Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(m_root, "manifest.json.replace-backup-*"), Has.Length.EqualTo(1));
            await Assert.ThatAsync(
                async () => await registry.ApplyProjectionResultsAsync([Projection(selected, 2)])
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [TestCase(WotRegistryCommitScope.Full)]
        [TestCase(WotRegistryCommitScope.ProjectionMetadata)]
        public async Task CancellationAfterDurableDecisionKeepsCommittedGeneration(WotRegistryCommitScope scope)
        {
            using var cancellation = new CancellationTokenSource();
            bool armed = false;
            using var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                manifestReplace: (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    if (armed)
                    {
                        cancellation.Cancel();
                    }
                });
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource selected = await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            armed = true;
            WotResource? added = null;

            if (scope == WotRegistryCommitScope.Full)
            {
                added = await AddAsync(registry, "second", cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await registry.ApplyProjectionResultsAsync([Projection(selected, 1)], cancellation.Token)
                    .ConfigureAwait(false);
            }

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(registry.Current.Generation, Is.EqualTo(before.Generation + 1));
            using var observer = new FileWotRegistryStore(m_root);
            Assert.That((await observer.LoadAsync().ConfigureAwait(false)).Generation,
                Is.EqualTo(before.Generation + 1));
            if (scope == WotRegistryCommitScope.Full)
            {
                Assert.That(added, Is.Not.Null);
                Assert.That(registry.Current.FindResource(
                    added!.GroupId, added.ResourceId), Is.SameAs(added));
            }
            else
            {
                Assert.That(registry.Current.FindResource(
                    selected.GroupId, selected.ResourceId)!.RefreshGeneration, Is.EqualTo(1u));
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        public async Task PreparedCommitRejectsNonadvancingGeneration(int delta)
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            await AddAsync(registry, "selected").ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            var intended = new WotRegistrySnapshot(before.Generation + delta, before.Groups, before.Labels);
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    intended, captured, WotRegistryCommitScope.ProjectionMetadata).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
        }

        [Test]
        public async Task PreparedCommitRejectsCaptureAfterAnotherCommitOnTheSameOwner()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            await registry.ApplyProjectionResultsAsync([Projection(resource, 1)]).ConfigureAwait(false);
            WotRegistrySnapshot current = registry.Current;
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    NextProjection(current, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(current));
            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
        }

        [Test]
        public async Task DisposedStoreRejectsPreviouslyCapturedInput()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            store.Dispose();
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    NextProjection(registry.Current, resource), captured, WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);

            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
        }

        [TestCase("key")]
        [TestCase("length")]
        [TestCase("digest")]
        public async Task ImmutableLeaseEvidenceMustMatchTheActualManifestContent(string fault)
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            content.NextLeaseFault = fault;

            await Assert.ThatAsync(
                () => AddAsync(registry, "selected"),
                Throws.TypeOf<InvalidDataException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ActiveLeaseCount, Is.Zero);
            Assert.That(File.Exists(Path.Combine(m_root, "manifest.json")), Is.False);
        }

        [Test]
        public async Task PreparedCommitRejectsUnknownScope()
        {
            using var content = new RecordingLeasedResourceStore();
            using var store = new FileWotRegistryStore(m_root, content);
            using var registry = new WotRegistryService(store);
            await registry.InitializeAsync().ConfigureAwait(false);
            WotResource resource = await AddAsync(registry, "selected").ConfigureAwait(false);
            using IWotRegistryValidatedGeneration captured = await store.CaptureValidatedGenerationAsync()
                .ConfigureAwait(false);
            WotRegistrySnapshot before = registry.Current;
            content.ClearReads();

            await Assert.ThatAsync(
                async () => await store.PrepareCommitAsync(
                    NextProjection(before, resource), captured, (WotRegistryCommitScope)123)
                    .ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);

            Assert.That(registry.Current, Is.SameAs(before));
            Assert.That(content.ReadsFor(resource.DefaultVersion!.DigestHex), Is.Zero);
        }

        private static WotRegistrySnapshot NextProjection(WotRegistrySnapshot before, WotResource resource)
        {
            return ReplaceResource(before, resource.With(
                refreshGeneration: 1,
                loadState: WoTLoadStateEnum.Active,
                activeVersionId: resource.DefaultVersionId,
                rootNodeId: new NodeId("selected-root", 2)));
        }

        private static WotRegistrySnapshot ReplaceResource(WotRegistrySnapshot before, WotResource resource)
        {
            WotResourceGroup group = before.FindGroup(resource.GroupId)!;
            return before.WithGroup(
                group.WithResources(group.Resources.SetItem(resource.ResourceId, resource), group.Epoch),
                before.Generation + 1);
        }

        private static async Task<WotResource> AddAsync(
            WotRegistryService registry,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            WotRegistryMutationResult result = await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:" + resourceId))
            }, cancellationToken).ConfigureAwait(false);
            Assert.That(result.Changed, Is.True, result.Message);
            return result.Resource ?? throw new InvalidOperationException("No resource was created.");
        }

        private static WotResourceProjection Projection(WotResource resource, uint generation)
        {
            return new WotResourceProjection(
                resource.GroupId,
                resource.ResourceId,
                WoTLoadStateEnum.Active,
                resource.DefaultVersionId,
                generation,
                1,
                new NodeId("selected-root", 2),
                null,
                [],
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(generation))
            {
                VersionId = resource.DefaultVersionId
            };
        }

        private string m_root = null!;

        internal sealed class RecordingLeasedResourceStore : IWotRegistryContentLeaseProvider, IDisposable
        {
            public bool SupportsImmutableContentLeases => true;
            public bool CorruptNextWrite { get; set; }
            public string? NextLeaseFault { get; set; }
            public Action<string>? ReadObserved { get; set; }

            public int ActiveLeaseCount
            {
                get
                {
                    lock (m_lock)
                    {
                        int count = 0;
                        foreach (int leases in m_leases.Values)
                        {
                            count += leases;
                        }
                        return count;
                    }
                }
            }

            public ValueTask<ByteString> ReadAsync(
                string resourceKey, long offset, int count, CancellationToken ct = default)
            {
                RecordRead(resourceKey);
                return m_inner.ReadAsync(resourceKey, offset, count, ct);
            }

            public async ValueTask WriteAsync(
                string resourceKey, long offset, ByteString data, CancellationToken ct = default)
            {
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (offset == 0 && !CorruptNextWrite &&
                        await m_inner.GetLengthAsync(resourceKey, ct).ConfigureAwait(false) == data.Length)
                    {
                        ByteString current = await ReadAsync(resourceKey, 0, data.Length, ct).ConfigureAwait(false);
                        if (current == data)
                        {
                            return;
                        }
                    }
                    EnsureUnleased(resourceKey);
                    if (CorruptNextWrite)
                    {
                        byte[] corrupted = data.Span.ToArray();
                        corrupted[0] ^= 0xff;
                        data = ByteString.From(corrupted);
                        CorruptNextWrite = false;
                    }
                    await m_inner.WriteAsync(resourceKey, offset, data, ct).ConfigureAwait(false);
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            public async ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
            {
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    EnsureUnleased(resourceKey);
                    return await m_inner.DeleteAsync(resourceKey, ct).ConfigureAwait(false);
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public async ValueTask<IWotRegistryContentLease> AcquireContentLeaseAsync(
                string resourceKey, CancellationToken cancellationToken = default)
            {
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    long length = await m_inner.GetLengthAsync(resourceKey, cancellationToken).ConfigureAwait(false);
                    if (length < 0)
                    {
                        throw new InvalidDataException("The leased content is absent.");
                    }
                    ByteString bytes = await ReadAsync(
                        resourceKey, 0, checked((int)length), cancellationToken).ConfigureAwait(false);
                    lock (m_lock)
                    {
                        m_leases.TryGetValue(resourceKey, out int count);
                        m_leases[resourceKey] = count + 1;
                    }
                    string? fault = NextLeaseFault;
                    NextLeaseFault = null;
                    return new ContentLease(
                        fault == "key" ? resourceKey + "-wrong" : resourceKey,
                        bytes,
                        fault == "length" ? bytes.Length + 1 : bytes.Length,
                        fault == "digest",
                        () => RecordRead(resourceKey),
                        () => Release(resourceKey));
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public int ReadsFor(string key)
            {
                lock (m_lock)
                {
                    return m_reads.TryGetValue(key, out int count) ? count : 0;
                }
            }

            public void ClearReads()
            {
                lock (m_lock)
                {
                    m_reads.Clear();
                }
            }

            public void Dispose()
            {
                m_gate.Dispose();
            }

            private void EnsureUnleased(string key)
            {
                lock (m_lock)
                {
                    if (m_leases.TryGetValue(key, out int count) && count != 0)
                    {
                        throw new IOException("Protected immutable content cannot be changed.");
                    }
                }
            }

            private void RecordRead(string key)
            {
                lock (m_lock)
                {
                    m_reads.TryGetValue(key, out int count);
                    m_reads[key] = count + 1;
                }
                ReadObserved?.Invoke(key);
            }

            private void Release(string key)
            {
                lock (m_lock)
                {
                    m_leases[key]--;
                }
            }

            private readonly InMemoryResourceStore m_inner = new();
            private readonly SemaphoreSlim m_gate = new(1, 1);
            private readonly Lock m_lock = new();
            private readonly Dictionary<string, int> m_reads = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> m_leases = new(StringComparer.Ordinal);

            private sealed class ContentLease(
                string resourceKey,
                ByteString content,
                long length,
                bool corrupt,
                Action recordRead,
                Action release) : IWotRegistryContentLease
            {
                public string ResourceKey { get; } = resourceKey;
                public long ContentLength => length;

                public ValueTask<ByteString> ReadAsync(
                    long offset, int count, CancellationToken cancellationToken = default)
                {
                    if (Volatile.Read(ref m_release) is null)
                    {
                        throw new ObjectDisposedException(nameof(ContentLease));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (offset < 0 || count < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    }
                    recordRead();
                    int take = (int)Math.Min(count, Math.Max(0, content.Length - offset));
                    byte[] bytes = take == 0
                        ? []
                        : content.Span.Slice(checked((int)offset), take).ToArray();
                    if (corrupt && bytes.Length > 0)
                    {
                        bytes[0] ^= 0xff;
                    }
                    return new ValueTask<ByteString>(ByteString.From(bytes));
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref m_release, null)?.Invoke();
                }

                private Action? m_release = release;
            }
        }
    }
}
