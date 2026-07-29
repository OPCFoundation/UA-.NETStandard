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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises strict fail-closed loading, serialized durable commits, stale
    /// generation rejection, and round-trip persistence.
    /// </summary>
    [TestFixture]
    public sealed class FileWotRegistryStoreTests
    {
        private string m_root = null!;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "wot-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public async Task PersistAndReloadRoundTripsResource()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "a",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.Td("urn:a")
                });
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "m",
                    Kind = WoTDocumentKindEnum.ThingModel,
                    Content = TestMaterialization.Tm("urn:m")
                });
            }

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            WotResource? td = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a");
            Assert.That(td, Is.Not.Null);
            Assert.That(td!.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(td.Versions, Has.Length.EqualTo(1));
            Assert.That(
                Encoding.UTF8.GetString(td.Versions[0].Content.ToArray()),
                Does.Contain("urn:a"));
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingModels, "m"), Is.Not.Null);
        }

        [Test]
        public async Task AnInjectedResourceStoreHoldsTheDocumentBytesInsteadOfTheRegistryFolder()
        {
            string storeRoot = Path.Combine(m_root, "external");
            using var resourceStore = new WotBlobResourceStore(storeRoot);

            var store = new FileWotRegistryStore(m_root, resourceStore);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "a",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.Td("urn:a")
                });
            }

            string registryBlobs = Path.Combine(m_root, "blobs");
            string[] inRegistry = Directory.Exists(registryBlobs)
                ? Directory.GetFiles(registryBlobs, "*.bin")
                : [];
            string[] inStore = Directory.GetFiles(storeRoot, "*.bin");

            var reloadStore = new FileWotRegistryStore(m_root, resourceStore);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();
            WotResource? td = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a");

            Assert.Multiple(() =>
            {
                Assert.That(inStore, Is.Not.Empty,
                    "The injected store must hold the document bytes, which is what lets a " +
                    "distributed deployment put them somewhere every node can reach.");
                Assert.That(inRegistry, Is.Empty,
                    "The registry folder must no longer hold blobs once a store is injected.");
                Assert.That(td, Is.Not.Null);
                Assert.That(
                    Encoding.UTF8.GetString(td!.Versions[0].Content.ToArray()),
                    Does.Contain("urn:a"),
                    "A reload must read the document back out of the injected store.");
            });
        }

        [Test]
        public void AnInjectedResourceStoreIsRequiredWhenTheOverloadIsUsed()
        {
            Assert.That(
                () => new FileWotRegistryStore(m_root, resourceStore: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task InvalidDocumentSurvivesReloadWithFailureState()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "bad",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.InvalidJson()
                });
            }

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            WotResource bad = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "bad")!;
            Assert.That(bad.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            Assert.That(bad.Validation, Is.Not.Null);
            Assert.That(bad.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task UpsertOverwritesResourceAtomically()
        {
            var store = new FileWotRegistryStore(m_root);
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a", "v1")
            });
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a", "v2")
            });

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!.Versions,
                Has.Length.EqualTo(2));
        }

        [Test]
        public async Task PristineAbsentPrimaryManifestLoadsEmptyRegistry()
        {
            WotRegistrySnapshot loaded =
                await new FileWotRegistryStore(m_root).LoadAsync();

            Assert.That(loaded, Is.SameAs(WotRegistrySnapshot.Empty));
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(Directory.Exists(BlobsPath), Is.False);
        }

        [Test]
        public async Task AbsentPrimaryWithPriorStateFailsClosedWithoutDataLoss()
        {
            WotRegistryMutationResult persisted =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] backup = File.ReadAllBytes(BackupPath);
            byte[] blob = File.ReadAllBytes(BlobPath(persisted));
            File.Delete(ManifestPath);
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync());
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));

            Assert.That(error.Message, Does.Contain("recovery artifacts"));
            Assert.That(error.Message, Does.Contain("cannot be treated as empty"));
            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(backup));
            Assert.That(File.ReadAllBytes(BlobPath(persisted)), Is.EqualTo(blob));
            Assert.That(File.Exists(ManifestPath), Is.False);
        }

        [TestCase("manifest.json.tmp-test", false)]
        [TestCase("manifest.json.replace-backup-test", false)]
        [TestCase("manifest.json.rollback-test", false)]
        [TestCase("blobs", true)]
        public void AbsentPrimaryWithPendingArtifactFailsClosed(
            string artifactName,
            bool directory)
        {
            string artifactPath = Path.Combine(m_root, artifactName);
            if (directory)
            {
                Directory.CreateDirectory(artifactPath);
            }
            else
            {
                File.WriteAllText(artifactPath, "operator recovery evidence");
            }

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await new FileWotRegistryStore(m_root).LoadAsync());

            Assert.That(error.Message, Does.Contain(artifactName));
            Assert.That(
                directory ? Directory.Exists(artifactPath) : File.Exists(artifactPath),
                Is.True);
        }

        [Test]
        public async Task CorruptPrimaryFailsClosedAndCannotCommitEmptyView()
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            File.WriteAllText(ManifestPath, "{ corrupt");
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException loadError = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync());
            InvalidOperationException commitError =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await store.CommitAsync(WotRegistrySnapshot.Empty));

            Assert.That(loadError.Message, Does.Contain("primary manifest"));
            Assert.That(loadError.Message, Does.Contain("corrupt"));
            Assert.That(commitError.Message, Does.Contain("LoadAsync"));
            AssertDataFilesEqual(expected);
        }

        [Test]
        public async Task UnsupportedSchemaFailsClosedWithoutUsingValidBackup()
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            File.WriteAllBytes(
                ManifestPath,
                WithSchemaVersion(File.ReadAllBytes(ManifestPath), schemaVersion: 1));
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            NotSupportedException error = Assert.ThrowsAsync<NotSupportedException>(
                async () => await store.LoadAsync());
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));

            Assert.That(error.Message, Does.Contain("schema 1"));
            AssertDataFilesEqual(expected);
        }

        [Test]
        public async Task SuccessfulCommitLeavesOperatorBackupUntouched()
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] backup = File.ReadAllBytes(BackupPath);

            await PersistResourceAsync("b", "urn:b");

            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(backup));
            Assert.That(ReplaceBackupPaths, Is.Empty);
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Not.Null);
        }

        [Test]
        public async Task DeleteRetainsBlobReferencedByOperatorBackup()
        {
            WotRegistryMutationResult persisted =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] backup = File.ReadAllBytes(BackupPath);
            byte[] blob = File.ReadAllBytes(BlobPath(persisted));
            using var service = new WotRegistryService(
                new FileWotRegistryStore(m_root));
            await service.InitializeAsync();

            await service.DeleteResourceAsync(
                WotRegistryGroups.ThingDescriptions, "a");

            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(backup));
            Assert.That(File.ReadAllBytes(BlobPath(persisted)), Is.EqualTo(blob));
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Null);
        }

        [Test]
        public async Task TransientPrimaryReadFailureDoesNotUseOrOverwriteBackup()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("FileShare.None is enforced by Windows for this test.");
            }

            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            using (new FileStream(
                ManifestPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                IOException error = Assert.ThrowsAsync<IOException>(
                    async () => await store.LoadAsync());
                Assert.That(error.Message, Does.Contain("primary manifest"));
                Assert.That(error.Message, Does.Contain("left unchanged"));
            }

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));
            AssertDataFilesEqual(expected);
        }

        [Test]
        public async Task MissingReferencedBlobFailsClosedWithoutChangingOtherBlobs()
        {
            WotRegistryMutationResult first =
                await PersistResourceAsync("a", "urn:a");
            WotRegistryMutationResult second =
                await PersistResourceAsync("b", "urn:b");
            CreateBackup();
            File.Delete(BlobPath(second));
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync());
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));

            Assert.That(error.Message, Does.Contain("missing"));
            Assert.That(File.Exists(BlobPath(first)), Is.True);
            AssertDataFilesEqual(expected);
        }

        [Test]
        public async Task LockedReferencedBlobFailsClosedWithoutChangingFiles()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("FileShare.None is enforced by Windows for this test.");
            }

            WotRegistryMutationResult persisted =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            using (new FileStream(
                BlobPath(persisted),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                IOException error = Assert.ThrowsAsync<IOException>(
                    async () => await store.LoadAsync());
                Assert.That(error.Message, Does.Contain("blob"));
                Assert.That(error.Message, Does.Contain("left unchanged"));
            }

            AssertDataFilesEqual(expected);
        }

        [Test]
        public async Task TamperedReferencedBlobFailsClosedWithoutChangingOtherBlobs()
        {
            WotRegistryMutationResult first =
                await PersistResourceAsync("a", "urn:a");
            WotRegistryMutationResult second =
                await PersistResourceAsync("b", "urn:b");
            CreateBackup();
            File.WriteAllText(BlobPath(second), "tampered");
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync());
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));

            Assert.That(error.Message, Does.Contain("SHA-256"));
            Assert.That(File.Exists(BlobPath(first)), Is.True);
            AssertDataFilesEqual(expected);
        }

        [Test]
        public Task DuplicateGroupIdsFailClosedWithoutChangingFiles()
        {
            return AssertDuplicateManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.GroupDto group = manifest.Groups![0];
                    manifest.Groups = [group, group];
                },
                "group id");
        }

        [Test]
        public Task DuplicateResourceIdsFailClosedWithoutChangingFiles()
        {
            return AssertDuplicateManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.GroupDto group = manifest.Groups![0];
                    FileWotRegistryStore.ResourceDto resource = group.Resources![0];
                    group.Resources = [resource, resource];
                },
                "resource id");
        }

        [Test]
        public Task DuplicateVersionIdsFailClosedWithoutChangingFiles()
        {
            return AssertDuplicateManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.ResourceDto resource =
                        manifest.Groups![0].Resources![0];
                    FileWotRegistryStore.VersionDto version = resource.Versions![0];
                    resource.Versions = [version, version];
                },
                "version id");
        }

        [Test]
        public Task CrossGroupResourceSpoofThatDuplicatesNodeIdFailsClosed()
        {
            return AssertInvalidManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.GroupDto first = manifest.Groups![0];
                    FileWotRegistryStore.GroupDto spoofed = CloneGroup(first);
                    spoofed.GroupId = "spoofed-container";
                    manifest.Groups = [first, spoofed];
                },
                "does not belong",
                "spoofed-container");
        }

        [Test]
        public Task GroupAndResourcePathIdentityCollisionFailsSegmentSafety()
        {
            return AssertInvalidManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.GroupDto first = manifest.Groups![0];
                    FileWotRegistryStore.GroupDto second = CloneGroup(first);
                    first.GroupId = "a";
                    first.Resources![0].GroupId = "a";
                    first.Resources[0].ResourceId = "b";
                    second.GroupId = "a/resources/b";
                    second.Resources = null;
                    manifest.Groups = [first, second];
                },
                "not segment-safe");
        }

        [Test]
        public Task CrossGroupResourcePathIdentityCollisionFailsSegmentSafety()
        {
            return AssertInvalidManifestRejected(
                manifest =>
                {
                    FileWotRegistryStore.GroupDto first = manifest.Groups![0];
                    FileWotRegistryStore.GroupDto second = CloneGroup(first);
                    first.GroupId = "a";
                    first.Resources![0].GroupId = "a";
                    first.Resources[0].ResourceId = "b/resources/c";
                    second.GroupId = "a/resources/b";
                    second.Resources![0].GroupId = "a/resources/b";
                    second.Resources[0].ResourceId = "c";
                    manifest.Groups = [first, second];
                },
                "not segment-safe");
        }

        [Test]
        public Task PublicSnapshotGroupOwnershipMismatchFailsBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    WotResource spoofed = CloneResource(
                        resource,
                        groupId: "spoofed-container");
                    WotResourceGroup malformed = group.WithResources(
                        group.Resources.SetItem(resource.ResourceId, spoofed),
                        group.Epoch);
                    return snapshot.WithGroup(malformed, snapshot.Generation);
                },
                "does not belong",
                WotRegistryGroups.ThingDescriptions);
        }

        [Test]
        public Task PublicSnapshotDuplicateVersionIdsFailBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    WotResourceVersion version = resource.Versions[0];
                    WotResource malformed = CloneResource(
                        resource,
                        versions: ImmutableArray.Create(version, version));
                    return snapshot.WithGroup(
                        group.WithResources(
                            group.Resources.SetItem(resource.ResourceId, malformed),
                            group.Epoch),
                        snapshot.Generation);
                },
                "duplicate",
                "version id");
        }

        [Test]
        public Task PublicSnapshotDigestMismatchFailsBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    WotResourceVersion version = resource.Versions[0];
                    var tampered = new WotResourceVersion(
                        version.VersionId,
                        version.Content,
                        version.ContentType,
                        version.Format,
                        version.CreatedAt,
                        version.ModifiedAt,
                        new byte[32]);
                    WotResource malformed = CloneResource(
                        resource,
                        versions: ImmutableArray.Create(tampered));
                    return snapshot.WithGroup(
                        group.WithResources(
                            group.Resources.SetItem(resource.ResourceId, malformed),
                            group.Epoch),
                        snapshot.Generation);
                },
                "content hashes");
        }

        [Test]
        public Task PublicSnapshotUnsafeGroupIdentityFailsBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    const string collidingGroupId = "a/resources/b";
                    WotResource moved = CloneResource(
                        resource,
                        groupId: collidingGroupId);
                    var malformedGroup = new WotResourceGroup(
                        collidingGroupId,
                        group.Kind,
                        ImmutableDictionary<string, WotResource>.Empty.Add(
                            moved.ResourceId,
                            moved),
                        epoch: group.Epoch);
                    return new WotRegistrySnapshot(
                        snapshot.Generation,
                        ImmutableDictionary<string, WotResourceGroup>.Empty.Add(
                            collidingGroupId,
                            malformedGroup),
                        snapshot.Labels);
                },
                "group id",
                "not segment-safe");
        }

        [Test]
        public Task PublicSnapshotUnsafeResourceIdFailsBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    WotResource malformed = CloneResource(
                        resource,
                        resourceId: "b/resources/c");
                    return snapshot.WithGroup(
                        group.WithResources(
                            ImmutableDictionary<string, WotResource>.Empty.Add(
                                malformed.ResourceId,
                                malformed),
                            group.Epoch),
                        snapshot.Generation);
                },
                "resource id",
                "not segment-safe");
        }

        [Test]
        public Task PublicSnapshotUnsafeVersionIdFailsBeforeDiskMutation()
        {
            return AssertMalformedSnapshotCommitRejected(
                snapshot =>
                {
                    WotResourceGroup group =
                        snapshot.FindGroup(WotRegistryGroups.ThingDescriptions)!;
                    WotResource resource = group.Resources.Values.Single();
                    WotResourceVersion version = resource.Versions[0];
                    var unsafeVersion = new WotResourceVersion(
                        "version/path",
                        version.Content,
                        version.ContentType,
                        version.Format,
                        version.CreatedAt,
                        version.ModifiedAt,
                        version.Digest);
                    WotResource malformed = CloneResource(
                        resource,
                        versions: ImmutableArray.Create(unsafeVersion));
                    return snapshot.WithGroup(
                        group.WithResources(
                            group.Resources.SetItem(resource.ResourceId, malformed),
                            group.Epoch),
                        snapshot.Generation);
                },
                "version id",
                "not segment-safe");
        }

        [Test]
        public async Task ConcurrentStoresSerializeAndRejectStaleCommitWithoutBlobDeletion()
        {
            WotRegistryMutationResult baseline =
                await PersistResourceAsync("base", "urn:base");
            using var firstEnteredSync = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            var firstStore = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (phase == FileWotRegistryStore.DirectorySyncPhase.BlobsBeforeManifest)
                    {
                        firstEnteredSync.Set();
                        if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException("Timed out waiting to release commit.");
                        }
                    }
                });
            var secondStore = new FileWotRegistryStore(m_root);
            using var firstService = new WotRegistryService(firstStore);
            using var secondService = new WotRegistryService(secondStore);
            await firstService.InitializeAsync();
            await secondService.InitializeAsync();

            Task<WotRegistryMutationResult> firstCommit = Task.Run(
                async () => await firstService.UpsertResourceAsync(
                    TdRequest("a", "urn:a")));
            Assert.That(firstEnteredSync.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Task<WotRegistryMutationResult> staleCommit = Task.Run(
                async () => await secondService.UpsertResourceAsync(
                    TdRequest("b", "urn:b")));
            await Task.Delay(150);
            Assert.That(staleCommit.IsCompleted, Is.False);

            releaseFirst.Set();
            WotRegistryMutationResult committed = await firstCommit;
            InvalidOperationException staleError =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await staleCommit);

            Assert.That(staleError.Message, Does.Contain("changed after this store loaded"));
            Assert.That(File.Exists(BlobPath(baseline)), Is.True);
            Assert.That(File.Exists(BlobPath(committed)), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(2));
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
        }

        [Test]
        public async Task StaleAbsentStoreRejectsRecoveryArtifactAbaWithoutMutation()
        {
            var staleStore = new FileWotRegistryStore(m_root);
            using var staleService = new WotRegistryService(staleStore);
            await staleService.InitializeAsync();

            string? replaceBackupPath = null;
            var writerStore = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                (source, destination, backup) =>
                {
                    replaceBackupPath = backup;
                    File.Move(destination, backup);
                    throw new IOException(
                        "Injected replacement failure after moving destination to backup.");
                });
            using var writerService = new WotRegistryService(writerStore);
            await writerService.InitializeAsync();
            await writerService.UpsertResourceAsync(TdRequest("a", "urn:a"));
            byte[] committedPrimary = File.ReadAllBytes(ManifestPath);

            Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                async () => await writerService.UpsertResourceAsync(
                    TdRequest("b", "urn:b")));
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(replaceBackupPath, Is.Not.Null);
            Assert.That(
                File.ReadAllBytes(replaceBackupPath!),
                Is.EqualTo(committedPrimary));
            Assert.That(ManifestContainsResource(replaceBackupPath!, "a"), Is.True);
            Assert.That(TemporaryManifestPaths, Has.Length.EqualTo(1));
            Dictionary<string, byte[]> indeterminateFiles = CaptureDataFiles();

            WotRegistryCommitIndeterminateException staleError =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await staleService.UpsertResourceAsync(
                        TdRequest("c", "urn:c")));

            Assert.That(
                staleError.PersistenceFailure.Message,
                Does.Contain("commit was not attempted"));
            Assert.That(
                staleError.ValidationFailure.Message,
                Does.Contain("recovery artifacts"));
            Assert.That(staleService.Current, Is.SameAs(WotRegistrySnapshot.Empty));
            AssertDataFilesEqual(indeterminateFiles);

            InvalidOperationException blocked =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await staleService.UpsertResourceAsync(
                        TdRequest("d", "urn:d")));
            Assert.That(blocked.Message, Does.Contain("InitializeAsync"));
            Assert.ThrowsAsync<InvalidDataException>(
                async () => await staleStore.LoadAsync());
            AssertDataFilesEqual(indeterminateFiles);
        }

        [Test]
        public async Task SameStoreRejectsSecondSnapshotWithAlreadyCommittedGeneration()
        {
            await PersistResourceAsync("a", "urn:a");
            var store = new FileWotRegistryStore(m_root);
            WotRegistrySnapshot loaded = await store.LoadAsync();
            long nextGeneration = loaded.Generation + 1;
            var first = new WotRegistrySnapshot(
                nextGeneration, loaded.Groups, loaded.Labels);
            var stale = new WotRegistrySnapshot(
                nextGeneration, WotRegistrySnapshot.Empty.Groups, loaded.Labels);

            await store.CommitAsync(first);
            byte[] committedManifest = File.ReadAllBytes(ManifestPath);
            InvalidOperationException error =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await store.CommitAsync(stale));
            Assert.That(error.Message, Does.Contain("strictly greater"));
            Assert.That(File.ReadAllBytes(ManifestPath), Is.EqualTo(committedManifest));
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
        }

        [Test]
        public async Task SameGenerationManifestRewriteRejectsStaleCommit()
        {
            WotRegistryMutationResult baseline =
                await PersistResourceAsync("a", "urn:a");
            var store = new FileWotRegistryStore(m_root);
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            File.AppendAllText(ManifestPath, Environment.NewLine);
            byte[] rewrittenManifest = File.ReadAllBytes(ManifestPath);

            InvalidOperationException error =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.Message, Does.Contain("manifest SHA-256"));
            Assert.That(File.ReadAllBytes(ManifestPath), Is.EqualTo(rewrittenManifest));
            Assert.That(File.Exists(BlobPath(baseline)), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(1));
        }

        [Test]
        public async Task ExclusiveOpenStorageLockRetriesContentionUntilCancellation()
        {
            string lockPath = Path.Combine(m_root, ".wot-registry.lock");
            using var externalLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(250));

            Assert.CatchAsync<OperationCanceledException>(
                async () => await new FileWotRegistryStore(m_root)
                    .LoadAsync(cancellation.Token));
        }

        [Test]
        public async Task MultiLevelDirectoryCreationSyncsEveryModifiedParentInOrder()
        {
            string nestedRoot = Path.Combine(m_root, "one", "two", "registry");
            var phases = new List<FileWotRegistryStore.DirectorySyncPhase>();
            var store = new FileWotRegistryStore(nestedRoot, phases.Add);
            using var service = new WotRegistryService(store);

            await service.InitializeAsync();
            await service.UpsertResourceAsync(TdRequest("a", "urn:a"));

            Assert.That(
                phases,
                Is.EqualTo(new[]
                {
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.RootComponentParent,
                    FileWotRegistryStore.DirectorySyncPhase.BlobsBeforeManifest,
                    FileWotRegistryStore.DirectorySyncPhase.RootBeforeManifest,
                    FileWotRegistryStore.DirectorySyncPhase.RootAfterManifestStaging,
                    FileWotRegistryStore.DirectorySyncPhase.RootAfterManifest
                }));
        }

        [Test]
        public async Task MultiLevelPartialRootSyncFailureResyncsEveryComponentOnRetry()
        {
            string first = Path.Combine(m_root, "one");
            string second = Path.Combine(first, "two");
            string nestedRoot = Path.Combine(second, "registry");
            bool injectFailure = true;
            int syncAttempts = 0;
            var store = new FileWotRegistryStore(
                nestedRoot,
                phase =>
                {
                    if (phase ==
                        FileWotRegistryStore.DirectorySyncPhase.RootComponentParent)
                    {
                        syncAttempts++;
                        if (injectFailure && syncAttempts == 3)
                        {
                            throw new IOException(
                                "Injected root parent directory sync failure.");
                        }
                    }
                });

            Assert.ThrowsAsync<IOException>(
                async () => await store.LoadAsync());
            Assert.That(Directory.Exists(first), Is.True);
            Assert.That(Directory.Exists(second), Is.True);
            Assert.That(Directory.Exists(nestedRoot), Is.False);
            Assert.That(
                File.Exists(Path.Combine(nestedRoot, ".wot-registry.lock")),
                Is.False);

            injectFailure = false;
            WotRegistrySnapshot loaded = await store.LoadAsync();

            Assert.That(loaded, Is.SameAs(WotRegistrySnapshot.Empty));
            Assert.That(syncAttempts, Is.EqualTo(7));
            Assert.That(
                File.Exists(Path.Combine(nestedRoot, ".wot-registry.lock")),
                Is.True);
        }

        [Test]
        public async Task PristineCancellationBeforeManifestRollsBackAndRetrySucceeds()
        {
            bool injectFailure = true;
            using var cancellation = new CancellationTokenSource();
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectFailure &&
                        phase == FileWotRegistryStore.DirectorySyncPhase.BlobsBeforeManifest)
                    {
                        injectFailure = false;
                        cancellation.Cancel();
                        cancellation.Token.ThrowIfCancellationRequested();
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            OperationCanceledException error =
                Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a"),
                        cancellation.Token));

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(Directory.Exists(BlobsPath), Is.False);
            Assert.That(TemporaryManifestPaths, Is.Empty);
            Assert.That(RollbackMarkerPaths, Is.Empty);

            WotRegistryMutationResult retry = await service.UpsertResourceAsync(
                TdRequest("a", "urn:a"));

            Assert.That(retry.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(File.Exists(ManifestPath), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(1));
        }

        [Test]
        public async Task PristineManifestStagingFailureRollsBackAndRetrySucceeds()
        {
            bool injectFailure = true;
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectFailure &&
                        phase ==
                        FileWotRegistryStore.DirectorySyncPhase.RootAfterManifestStaging)
                    {
                        injectFailure = false;
                        throw new IOException(
                            "Injected manifest staging directory sync failure.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            IOException error = Assert.ThrowsAsync<IOException>(
                async () => await service.UpsertResourceAsync(
                    TdRequest("a", "urn:a")));

            Assert.That(error.Message, Does.Contain("manifest staging"));
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(Directory.Exists(BlobsPath), Is.False);
            Assert.That(TemporaryManifestPaths, Is.Empty);
            Assert.That(RollbackMarkerPaths, Is.Empty);

            WotRegistryMutationResult retry = await service.UpsertResourceAsync(
                TdRequest("a", "urn:a"));

            Assert.That(retry.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(File.Exists(ManifestPath), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(1));
        }

        [Test]
        public async Task AmbiguousPristineArtifactsRemainFailClosed()
        {
            string unknownArtifact = Path.Combine(BlobsPath, "unknown.bin");
            bool injectFailure = true;
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectFailure &&
                        phase == FileWotRegistryStore.DirectorySyncPhase.RootBeforeManifest)
                    {
                        injectFailure = false;
                        File.WriteAllText(unknownArtifact, "ambiguous external artifact");
                        throw new IOException(
                            "Injected pre-switch failure with an ambiguous artifact.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            WotRegistryCommitIndeterminateException error =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a")));
            Dictionary<string, byte[]> retained = CaptureDataFiles();

            Assert.That(error.PersistenceFailure.Message, Does.Contain("ambiguous artifact"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("rollback"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("Unknown"));
            Assert.That(File.Exists(unknownArtifact), Is.True);
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(Directory.GetFiles(BlobsPath), Has.Length.EqualTo(2));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.UpsertResourceAsync(
                    TdRequest("b", "urn:b")));
            Assert.ThrowsAsync<InvalidDataException>(
                async () => await new FileWotRegistryStore(m_root).LoadAsync());
            AssertDataFilesEqual(retained);
        }

        [Test]
        public async Task FailedPristineRollbackRetainsMarkerAndFailsClosed()
        {
            bool injectCommitFailure = true;
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectCommitFailure &&
                        phase == FileWotRegistryStore.DirectorySyncPhase.RootBeforeManifest)
                    {
                        injectCommitFailure = false;
                        throw new IOException("Injected pre-switch failure.");
                    }
                    if (phase ==
                        FileWotRegistryStore.DirectorySyncPhase.BlobsAfterPristineRollback)
                    {
                        throw new IOException("Injected rollback directory sync failure.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            WotRegistryCommitIndeterminateException error =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("a", "urn:a")));
            Dictionary<string, byte[]> retained = CaptureDataFiles();

            Assert.That(error.PersistenceFailure.Message, Does.Contain("pre-switch"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("rollback"));
            Assert.That(RollbackMarkerPaths, Has.Length.EqualTo(1));
            Assert.That(Directory.Exists(BlobsPath), Is.True);
            Assert.That(Directory.GetFileSystemEntries(BlobsPath), Is.Empty);
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.ThrowsAsync<InvalidDataException>(
                async () => await new FileWotRegistryStore(m_root).LoadAsync());
            AssertDataFilesEqual(retained);
        }

        [Test]
        public Task BlobDirectorySyncFailureLeavesPrimaryGenerationReadable()
        {
            return AssertPreManifestDirectorySyncFailure(
                FileWotRegistryStore.DirectorySyncPhase.BlobsBeforeManifest);
        }

        [Test]
        public Task RootBeforeManifestSyncFailureLeavesPrimaryGenerationReadable()
        {
            return AssertPreManifestDirectorySyncFailure(
                FileWotRegistryStore.DirectorySyncPhase.RootBeforeManifest);
        }

        private async Task AssertPreManifestDirectorySyncFailure(
            FileWotRegistryStore.DirectorySyncPhase phaseToFail)
        {
            WotRegistryMutationResult baseline =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] primary = File.ReadAllBytes(ManifestPath);
            byte[] backup = File.ReadAllBytes(BackupPath);
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (phase == phaseToFail)
                    {
                        throw new IOException(
                            $"Injected {phaseToFail} directory sync failure.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            long previousGeneration = service.Current.Generation;

            IOException error = Assert.ThrowsAsync<IOException>(
                async () => await service.UpsertResourceAsync(
                    TdRequest("b", "urn:b")));

            Assert.That(error.Message, Does.Contain("Injected"));
            Assert.That(
                error,
                Is.Not.InstanceOf<WotRegistryCommitDurabilityUncertainException>());
            Assert.That(service.Current.Generation, Is.EqualTo(previousGeneration));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
            Assert.That(File.ReadAllBytes(ManifestPath), Is.EqualTo(primary));
            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(backup));
            Assert.That(File.Exists(BlobPath(baseline)), Is.True);
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
        }

        [Test]
        public async Task ReplaceFailureBeforeMovesIsNotCommittedAndPreservesStagedManifest()
        {
            WotRegistryMutationResult baseline =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] primary = File.ReadAllBytes(ManifestPath);
            byte[] operatorBackup = File.ReadAllBytes(BackupPath);
            bool injectFailure = true;
            string? replaceBackupPath = null;
            var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                (source, destination, backup) =>
                {
                    replaceBackupPath = backup;
                    if (injectFailure)
                    {
                        injectFailure = false;
                        throw new IOException("Injected replacement failure before any move.");
                    }
                    File.Replace(source, destination, backup);
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            WotRegistrySnapshot before = service.Current;

            WotRegistryCommitNotCommittedException error =
                Assert.ThrowsAsync<WotRegistryCommitNotCommittedException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.PersistenceFailure.Message, Does.Contain("before any move"));
            Assert.That(service.Current, Is.SameAs(before));
            Assert.That(File.ReadAllBytes(ManifestPath), Is.EqualTo(primary));
            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(operatorBackup));
            Assert.That(error.RecoveryArtifactPath, Is.Not.Null);
            Assert.That(File.Exists(error.RecoveryArtifactPath), Is.True);
            Assert.That(ManifestContainsResource(error.RecoveryArtifactPath!, "b"), Is.True);
            Assert.That(replaceBackupPath, Is.Not.Null);
            Assert.That(File.Exists(replaceBackupPath), Is.False);
            Assert.That(ReplaceBackupPaths, Is.Empty);
            Assert.That(File.Exists(BlobPath(baseline)), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(2));

            WotRegistryMutationResult retry = await service.UpsertResourceAsync(
                TdRequest("b", "urn:b"));

            Assert.That(retry.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(File.Exists(error.RecoveryArtifactPath), Is.True);
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Not.Null);
        }

        [Test]
        public async Task ReplaceReportsFailureAfterSuccessfulMoveAsCommittedUncertain()
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] primary = File.ReadAllBytes(ManifestPath);
            byte[] operatorBackup = File.ReadAllBytes(BackupPath);
            bool injectFailure = true;
            string? replaceBackupPath = null;
            var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                (source, destination, backup) =>
                {
                    replaceBackupPath = backup;
                    File.Replace(source, destination, backup);
                    if (injectFailure)
                    {
                        injectFailure = false;
                        throw new IOException(
                            "Injected replacement failure after destination move.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            WotRegistryCommitDurabilityUncertainException error =
                Assert.ThrowsAsync<WotRegistryCommitDurabilityUncertainException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.PersistenceFailure.Message, Does.Contain("destination move"));
            Assert.That(service.Current, Is.SameAs(error.CommittedSnapshot));
            Assert.That(ManifestContainsResource(ManifestPath, "b"), Is.True);
            Assert.That(replaceBackupPath, Is.Not.Null);
            Assert.That(File.ReadAllBytes(replaceBackupPath!), Is.EqualTo(primary));
            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(operatorBackup));
            Assert.That(TemporaryManifestPaths, Is.Empty);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(2));

            WotRegistryMutationResult next = await service.UpsertResourceAsync(
                TdRequest("c", "urn:c"));
            Assert.That(next.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [Test]
        public async Task DestinationMovedToBackupFailureIsIndeterminateWithoutDataLoss()
        {
            WotRegistryMutationResult baseline =
                await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] primary = File.ReadAllBytes(ManifestPath);
            byte[] operatorBackup = File.ReadAllBytes(BackupPath);
            bool injectFailure = true;
            string? replaceBackupPath = null;
            var store = new FileWotRegistryStore(
                m_root,
                directorySyncFailureInjector: null,
                (source, destination, backup) =>
                {
                    replaceBackupPath = backup;
                    if (injectFailure)
                    {
                        injectFailure = false;
                        File.Move(destination, backup);
                        throw new IOException(
                            "Injected replacement failure after moving destination to backup.");
                    }
                    File.Replace(source, destination, backup);
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            WotRegistrySnapshot before = service.Current;

            WotRegistryCommitIndeterminateException error =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.PersistenceFailure.Message, Does.Contain("moving destination"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("primary manifest"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("staged manifest"));
            Assert.That(error.ValidationFailure.Message, Does.Contain("replace backup manifest"));
            Assert.That(service.Current, Is.SameAs(before));
            Assert.That(File.Exists(ManifestPath), Is.False);
            Assert.That(replaceBackupPath, Is.Not.Null);
            Assert.That(File.ReadAllBytes(replaceBackupPath!), Is.EqualTo(primary));
            Assert.That(File.ReadAllBytes(BackupPath), Is.EqualTo(operatorBackup));
            Assert.That(TemporaryManifestPaths, Has.Length.EqualTo(1));
            Assert.That(ManifestContainsResource(TemporaryManifestPaths[0], "b"), Is.True);
            Assert.That(File.Exists(BlobPath(baseline)), Is.True);
            Assert.That(Directory.GetFiles(BlobsPath, "*.bin"), Has.Length.EqualTo(2));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.UpsertResourceAsync(
                    TdRequest("c", "urn:c")));

            using (var restarted = new WotRegistryService(
                new FileWotRegistryStore(m_root)))
            {
                InvalidDataException restartError =
                    Assert.ThrowsAsync<InvalidDataException>(
                        async () => await restarted.InitializeAsync());
                Assert.That(restartError.Message, Does.Contain("recovery artifacts"));
                InvalidOperationException restartBlocked =
                    Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await restarted.UpsertResourceAsync(
                            TdRequest("c", "urn:c")));
                Assert.That(restartBlocked.Message, Does.Contain("InitializeAsync"));
            }

            File.Move(replaceBackupPath!, ManifestPath);
            await service.InitializeAsync();
            WotRegistryMutationResult recovered = await service.UpsertResourceAsync(
                TdRequest("c", "urn:c"));
            Assert.That(recovered.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
        }

        [Test]
        public async Task FinalRootSyncFailurePublishesValidatedCommittedSnapshot()
        {
            await PersistResourceAsync("a", "urn:a");
            bool injectFailure = true;
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectFailure &&
                        phase == FileWotRegistryStore.DirectorySyncPhase.RootAfterManifest)
                    {
                        injectFailure = false;
                        throw new IOException("Injected root directory sync failure.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();

            WotRegistryCommitDurabilityUncertainException error =
                Assert.ThrowsAsync<WotRegistryCommitDurabilityUncertainException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.PersistenceFailure.Message, Does.Contain("Injected"));
            Assert.That(service.Current, Is.SameAs(error.CommittedSnapshot));
            Assert.That(service.Current.Generation, Is.EqualTo(error.CommittedGeneration));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Not.Null);

            WotRegistryMutationResult next = await service.UpsertResourceAsync(
                TdRequest("c", "urn:c"));
            Assert.That(next.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotRegistrySnapshot reloaded =
                await new FileWotRegistryStore(m_root).LoadAsync();
            Assert.That(reloaded.Generation, Is.EqualTo(service.Current.Generation));
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Not.Null);
            Assert.That(
                reloaded.FindResource(WotRegistryGroups.ThingDescriptions, "c"),
                Is.Not.Null);
        }

        [Test]
        public async Task UnvalidatedPostSwitchFailureBlocksMutationUntilReload()
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            byte[] recoveryManifest = File.ReadAllBytes(BackupPath);
            bool injectFailure = true;
            var store = new FileWotRegistryStore(
                m_root,
                phase =>
                {
                    if (injectFailure &&
                        phase == FileWotRegistryStore.DirectorySyncPhase.RootAfterManifest)
                    {
                        File.WriteAllText(ManifestPath, "{ corrupt");
                        throw new IOException("Injected root directory sync failure.");
                    }
                });
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            WotRegistrySnapshot before = service.Current;

            WotRegistryCommitIndeterminateException error =
                Assert.ThrowsAsync<WotRegistryCommitIndeterminateException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("b", "urn:b")));

            Assert.That(error.IntendedGeneration, Is.EqualTo(before.Generation + 1));
            Assert.That(service.Current, Is.SameAs(before));
            InvalidOperationException blocked =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await service.UpsertResourceAsync(
                        TdRequest("c", "urn:c")));
            Assert.That(blocked.Message, Does.Contain("InitializeAsync"));

            injectFailure = false;
            File.WriteAllBytes(ManifestPath, recoveryManifest);
            await service.InitializeAsync();
            WotRegistryMutationResult recovered = await service.UpsertResourceAsync(
                TdRequest("c", "urn:c"));

            Assert.That(recovered.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "b"),
                Is.Null);
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "c"),
                Is.Not.Null);
        }

        private async Task<WotRegistryMutationResult> PersistResourceAsync(
            string resourceId,
            string thingId)
        {
            using var service = new WotRegistryService(
                new FileWotRegistryStore(m_root));
            await service.InitializeAsync();
            return await service.UpsertResourceAsync(
                TdRequest(resourceId, thingId));
        }

        private async Task AssertDuplicateManifestRejected(
            Action<FileWotRegistryStore.ManifestDto> duplicate,
            string logicalId)
        {
            await AssertInvalidManifestRejected(
                duplicate,
                "duplicate",
                logicalId);
        }

        private async Task AssertInvalidManifestRejected(
            Action<FileWotRegistryStore.ManifestDto> invalidate,
            params string[] diagnostics)
        {
            await PersistResourceAsync("a", "urn:a");
            CreateBackup();
            FileWotRegistryStore.ManifestDto manifest = JsonSerializer.Deserialize(
                    File.ReadAllBytes(ManifestPath),
                    WotRegistryStoreJson.Default.ManifestDto) ??
                throw new InvalidDataException("The test manifest was null.");
            invalidate(manifest);
            File.WriteAllBytes(
                ManifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    manifest, WotRegistryStoreJson.Default.ManifestDto));
            Dictionary<string, byte[]> expected = CaptureDataFiles();
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.LoadAsync());
            foreach (string diagnostic in diagnostics)
            {
                Assert.That(error.Message, Does.Contain(diagnostic));
            }
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.CommitAsync(WotRegistrySnapshot.Empty));
            AssertDataFilesEqual(expected);
        }

        private async Task AssertMalformedSnapshotCommitRejected(
            Func<WotRegistrySnapshot, WotRegistrySnapshot> invalidate,
            params string[] diagnostics)
        {
            WotRegistrySnapshot valid = await CreateInMemorySnapshotAsync();
            WotRegistrySnapshot malformed = invalidate(valid);
            Assert.That(Directory.GetFileSystemEntries(m_root), Is.Empty);
            var store = new FileWotRegistryStore(m_root);

            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await store.CommitAsync(malformed));

            foreach (string diagnostic in diagnostics)
            {
                Assert.That(error.Message, Does.Contain(diagnostic));
            }
            Assert.That(
                Directory.GetFileSystemEntries(m_root),
                Is.Empty,
                "Malformed snapshots must fail before creating lock, blobs, or manifests.");
        }

        private static async Task<WotRegistrySnapshot> CreateInMemorySnapshotAsync()
        {
            using var service = new WotRegistryService(new InMemoryWotRegistryStore());
            await service.InitializeAsync();
            await service.UpsertResourceAsync(TdRequest("a", "urn:a"));
            return service.Current;
        }

        private static WotResource CloneResource(
            WotResource resource,
            string? groupId = null,
            string? resourceId = null,
            ImmutableArray<WotResourceVersion>? versions = null)
        {
            return new WotResource(
                groupId ?? resource.GroupId,
                resourceId ?? resource.ResourceId,
                resource.Kind,
                versions ?? resource.Versions,
                resource.DefaultVersionId,
                resource.DesiredVersionId,
                resource.ActiveVersionId,
                resource.Enabled,
                resource.LoadState,
                resource.Validation,
                resource.Diagnostics,
                resource.Epoch,
                resource.RefreshGeneration,
                resource.LastRefreshTime,
                resource.MaterializedNodeCount,
                resource.RootNodeId,
                resource.Name,
                resource.Description,
                resource.ThingId,
                resource.Title,
                resource.Labels);
        }

        private static FileWotRegistryStore.GroupDto CloneGroup(
            FileWotRegistryStore.GroupDto group)
        {
            return JsonSerializer.Deserialize(
                    JsonSerializer.SerializeToUtf8Bytes(
                        group,
                        WotRegistryStoreJson.Default.GroupDto),
                    WotRegistryStoreJson.Default.GroupDto) ??
                throw new InvalidDataException("The cloned test group was null.");
        }

        private void CreateBackup()
        {
            File.Copy(ManifestPath, BackupPath, overwrite: true);
        }

        private Dictionary<string, byte[]> CaptureDataFiles()
        {
            return Directory.GetFiles(m_root, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    ".wot-registry.lock",
                    StringComparison.Ordinal))
                .ToDictionary(
                    RelativePath,
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }

        private void AssertDataFilesEqual(Dictionary<string, byte[]> expected)
        {
            Dictionary<string, byte[]> actual = CaptureDataFiles();
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (KeyValuePair<string, byte[]> item in expected)
            {
                Assert.That(
                    actual[item.Key],
                    Is.EqualTo(item.Value),
                    $"File changed: {item.Key}");
            }
        }

        private string RelativePath(string path)
        {
            return path.Substring(m_root.Length).TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private string BlobPath(WotRegistryMutationResult result)
        {
            return Path.Combine(
                BlobsPath,
                result.Resource!.DefaultVersion!.DigestHex + ".bin");
        }

        private static bool ManifestContainsResource(string path, string resourceId)
        {
            FileWotRegistryStore.ManifestDto manifest = JsonSerializer.Deserialize(
                    File.ReadAllBytes(path),
                    WotRegistryStoreJson.Default.ManifestDto) ??
                throw new InvalidDataException("The test manifest was null.");
            return manifest.Groups?
                .SelectMany(group => group.Resources ?? [])
                .Any(resource => string.Equals(
                    resource.ResourceId,
                    resourceId,
                    StringComparison.Ordinal)) == true;
        }

        private string ManifestPath => Path.Combine(m_root, "manifest.json");

        private string BackupPath => Path.Combine(m_root, "manifest.json.bak");

        private string[] ReplaceBackupPaths =>
            Directory.GetFiles(m_root, "manifest.json.replace-backup-*");

        private string[] TemporaryManifestPaths =>
            Directory.GetFiles(m_root, "manifest.json.tmp-*");

        private string[] RollbackMarkerPaths =>
            Directory.GetFiles(m_root, "manifest.json.rollback-*");

        private string BlobsPath => Path.Combine(m_root, "blobs");

        private static byte[] WithSchemaVersion(byte[] manifest, int schemaVersion)
        {
            string json = Encoding.UTF8.GetString(manifest);
            const string current = "\"SchemaVersion\": 2";
            Assert.That(json, Does.Contain(current));
            int index = json.IndexOf(current, StringComparison.Ordinal);
            return Encoding.UTF8.GetBytes(
                json.Remove(index, current.Length)
                    .Insert(index, $"\"SchemaVersion\": {schemaVersion}"));
        }

        private static WotUpsertResourceRequest TdRequest(
            string resourceId,
            string thingId)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td(thingId)
            };
        }
    }
}
