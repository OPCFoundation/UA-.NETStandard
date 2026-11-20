// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Supplemental tests for <see cref="FileWotRegistryStore"/> covering
    /// error paths: corrupt manifest, missing blob, empty snapshot round-trip,
    /// and label persistence.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class FileWotRegistryStoreExtendedTests
    {
        private static string MakeRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "wot-store-ext-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void SafeDelete(string root)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public async Task LoadReturnsEmptyWhenManifestFileDoesNotExist()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                WotRegistrySnapshot snapshot = await store.LoadAsync();

                Assert.That(snapshot.AllResources(), Is.Empty);
                Assert.That(snapshot.Groups, Is.Empty);
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task LoadRejectsInvalidManifestWithoutChangingIt()
        {
            string root = MakeRoot();
            try
            {
                string manifestPath = Path.Combine(root, "manifest.json");
                const string invalidManifest = "{ not valid json }";
                File.WriteAllText(manifestPath, invalidManifest);
                var store = new FileWotRegistryStore(root);

                InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                    async () => await store.LoadAsync());

                Assert.That(error.Message, Does.Contain("corrupt"));
                Assert.That(File.ReadAllText(manifestPath), Is.EqualTo(invalidManifest));
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task CommitAndLoadEmptySnapshotRoundTrips()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                WotRegistrySnapshot initial = await store.LoadAsync();
                await store.CommitAsync(
                    new WotRegistrySnapshot(
                        initial.Generation + 1,
                        initial.Groups,
                        initial.Labels));
                WotRegistrySnapshot loaded = await store.LoadAsync();

                Assert.That(loaded.Groups, Is.Empty);
                Assert.That(loaded.Generation, Is.EqualTo(1));
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task LabelsArePersisted()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                using (var service = new WotRegistryService(store))
                {
                    await service.InitializeAsync();
                    await service.AddRegistryLabelAsync("env", "production", expectedEpoch: 0);
                }

                var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync();

                Assert.That(reloaded.Current.Labels.ContainsKey("env"), Is.True);
                Assert.That(reloaded.Current.Labels["env"], Is.EqualTo("production"));
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task CommitTwiceReplacesManifestAtomically()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                using var service = new WotRegistryService(store);
                await service.InitializeAsync();

                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "td-v1",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:td", "v1"))
                });

                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "td-v1",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:td", "v2"))
                });

                var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync();

                WotResource? td = reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "td-v1");
                Assert.That(td, Is.Not.Null);
                Assert.That(td!.Versions, Has.Length.EqualTo(2));
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task DeletedResourceIsNotReloadedAfterCommit()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                using (var service = new WotRegistryService(store))
                {
                    await service.InitializeAsync();
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "to-delete",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(TestMaterialization.Td("urn:del"))
                    });
                    await service.DeleteResourceAsync(
                        WotRegistryGroups.ThingDescriptions, "to-delete");
                }

                var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync();

                WotResource? td = reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "to-delete");
                Assert.That(td, Is.Null,
                    "A deleted resource must not be present after reload.");
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task ResourceLabelsArePersisted()
        {
            string root = MakeRoot();
            try
            {
                var store = new FileWotRegistryStore(root);
                using (var service = new WotRegistryService(store))
                {
                    await service.InitializeAsync();
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "labeled",
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(TestMaterialization.Td("urn:labeled"))
                    });
                    await service.AddResourceLabelAsync(
                        WotRegistryGroups.ThingDescriptions, "labeled", "color", "red");
                }

                var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync();

                WotResource? resource = reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "labeled");
                Assert.That(resource, Is.Not.Null);
                Assert.That(resource!.Labels.ContainsKey("color"), Is.True);
                Assert.That(resource.Labels["color"], Is.EqualTo("red"));
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task BlobsDirectoryIsCreatedOnFirstCommit()
        {
            string root = MakeRoot();
            string blobsDir = Path.Combine(root, "blobs");
            try
            {
                var store = new FileWotRegistryStore(root);
                using var service = new WotRegistryService(store);
                await service.InitializeAsync();

                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "first",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:first"))
                });

                Assert.That(Directory.Exists(blobsDir), Is.True,
                    "Blobs directory must be created on first commit with content.");
                Assert.That(Directory.GetFiles(blobsDir, "*.bin"), Has.Length.EqualTo(1),
                    "One blob file per unique content digest.");
            }
            finally
            {
                SafeDelete(root);
            }
        }

        [Test]
        public async Task RepeatedSameContentSharesOneBlobFile()
        {
            string root = MakeRoot();
            string blobsDir = Path.Combine(root, "blobs");
            try
            {
                var store = new FileWotRegistryStore(root);
                using var service = new WotRegistryService(store);
                await service.InitializeAsync();

                byte[] content = TestMaterialization.Td("urn:shared");
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "a",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(content)
                });
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "b",
                    Kind = WoTDocumentKindEnum.ThingModel,
                    Content = ByteString.From(content)
                });

                Assert.That(Directory.GetFiles(blobsDir, "*.bin"), Has.Length.EqualTo(1),
                    "Identical content must be deduplicated to a single blob file.");
            }
            finally
            {
                SafeDelete(root);
            }
        }
    }
}
