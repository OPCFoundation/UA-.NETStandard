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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

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

        /// <summary>
        /// Mutating one resource must not rewrite the stored bytes of an
        /// unrelated one.
        /// </summary>
        /// <remarks>
        /// This is the property the content-addressed byte store exists to
        /// provide. While document bytes travelled inside the snapshot, every
        /// commit rewrote every referenced blob, so editing one small document
        /// rewrote the whole corpus. Now the snapshot carries only metadata and
        /// a blob is written once per digest, so an untouched document's file is
        /// never reopened. Both the content and the last-write time are checked:
        /// content alone would pass even if the file were rewritten with
        /// identical bytes, which is exactly the work this removes.
        /// </remarks>
        [Test]
        public async Task MutatingOneResourceDoesNotRewriteAnotherResourceBytes()
        {
            using var service = new WotRegistryService(new FileWotRegistryStore(m_root));
            await service.InitializeAsync();
            WotRegistryMutationResult untouched = await service
                .UpsertResourceAsync(TdRequest("stable", "urn:stable"));
            await service.UpsertResourceAsync(TdRequest("edited", "urn:edited:v1"));

            string untouchedBlob = BlobPath(untouched);
            Assert.That(File.Exists(untouchedBlob), Is.True,
                "The unrelated resource must have been persisted to its own blob.");
            byte[] before = File.ReadAllBytes(untouchedBlob);
            DateTime writtenAt = File.GetLastWriteTimeUtc(untouchedBlob);

            // Move the clock past the file system's timestamp resolution so a
            // rewrite would be visible rather than indistinguishable.
            await Task.Delay(50).ConfigureAwait(false);
            await service.UpsertResourceAsync(TdRequest("edited", "urn:edited:v2"));

            Assert.That(File.ReadAllBytes(untouchedBlob), Is.EqualTo(before),
                "An unrelated document's bytes must not change.");
            Assert.That(File.GetLastWriteTimeUtc(untouchedBlob), Is.EqualTo(writtenAt),
                "An unrelated document's blob must not be rewritten at all.");
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
                    Content = ByteString.From(TestMaterialization.Td("urn:a"))
                });
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "m",
                    Kind = WoTDocumentKindEnum.ThingModel,
                    Content = ByteString.From(TestMaterialization.Tm("urn:m"))
                });
                WotResource createdTd = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "a")!;
                await service.AddVersionLabelAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "a",
                    createdTd.DefaultVersionId!,
                    "version",
                    "one",
                    createdTd.DefaultVersion!.Epoch);
                await service.AddResourceLabelAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "a",
                    "owner",
                    "plant-1",
                    createdTd.MetaEpoch);
                await service.ValidateResourceAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "a");
            }

            var reloadStore = new FileWotRegistryStore(m_root);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            WotResource? td = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a");
            Assert.That(td, Is.Not.Null);
            Assert.That(td!.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(td.Versions, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(td.DefaultVersion!.Epoch, Is.EqualTo(2));
                Assert.That(td.DefaultVersion.Labels["version"], Is.EqualTo("one"));
                Assert.That(
                    td.DefaultVersion.Validation!.FormatOutcome,
                    Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(td.MetaLabels["owner"], Is.EqualTo("plant-1"));
                Assert.That(td.MetaCreatedAt, Is.Not.Default);
                Assert.That(td.MetaModifiedAt, Is.GreaterThanOrEqualTo(td.MetaCreatedAt));
            });
            ByteString tdContent = await reloaded.ReadContentAsync(td.Versions[0]);
            Assert.That(
                Encoding.UTF8.GetString(tdContent.Span.ToArray()),
                Does.Contain("urn:a"));
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingModels, "m"), Is.Not.Null);
        }

        [Test]
        public async Task PerVersionDocumentMetadataRoundTripsAcrossDefaultSwitch()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                foreach ((string VersionId, string Title, string BaseUri) version in new[]
                {
                    ("v1", "first", "https://example.test/first/"),
                    ("v2", "second", "https://example.test/second/")
                })
                {
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "metadata",
                        VersionId = version.VersionId,
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(ThingDescriptionWithMetadata(
                            "urn:metadata",
                            "urn:metadata-" + version.Title,
                            version.BaseUri)),
                        SetAsDefault = false
                    });
                }
                WotResource before = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "metadata")!;
                await service.SetDefaultVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "metadata",
                    "v2",
                    before.MetaEpoch);

                foreach ((string VersionId, string Title, string ModelVersion) version in new[]
                {
                    ("v1", "first", "1.0.0"),
                    ("v2", "second", "2.0.0")
                })
                {
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingModels,
                        ResourceId = "model-metadata",
                        VersionId = version.VersionId,
                        Kind = WoTDocumentKindEnum.ThingModel,
                        Content = ByteString.From(ThingModelWithMetadata(
                            "urn:model-metadata",
                            "urn:model-metadata-" + version.Title,
                            version.ModelVersion)),
                        SetAsDefault = false
                    });
                }
                WotResource modelBefore = service.Current.FindResource(
                    WotRegistryGroups.ThingModels,
                    "model-metadata")!;
                await service.SetDefaultVersionAsync(
                    WotRegistryGroups.ThingModels,
                    "model-metadata",
                    "v2",
                    modelBefore.MetaEpoch);
            }

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();
            WotResource restored = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "metadata")!;

            Assert.Multiple(() =>
            {
                Assert.That(restored.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(restored.ThingId, Is.EqualTo("urn:metadata"));
                Assert.That(restored.Title, Is.EqualTo("urn:metadata-second"));
                Assert.That(
                    restored.FindVersion("v1")!.DocumentId,
                    Is.EqualTo("urn:metadata"));
                Assert.That(
                    restored.FindVersion("v1")!.Title,
                    Is.EqualTo("urn:metadata-first"));
                Assert.That(
                    restored.FindVersion("v1")!.BaseUri,
                    Is.EqualTo("https://example.test/first/"));
                Assert.That(
                    restored.FindVersion("v2")!.DocumentId,
                    Is.EqualTo("urn:metadata"));
                Assert.That(
                    restored.FindVersion("v2")!.Title,
                    Is.EqualTo("urn:metadata-second"));
                Assert.That(
                    restored.FindVersion("v2")!.BaseUri,
                    Is.EqualTo("https://example.test/second/"));
            });

            WotResource restoredModel = reloaded.Current.FindResource(
                WotRegistryGroups.ThingModels,
                "model-metadata")!;
            Assert.Multiple(() =>
            {
                Assert.That(restoredModel.Title, Is.EqualTo("urn:model-metadata-second"));
                Assert.That(
                    restoredModel.FindVersion("v1")!.ModelVersion,
                    Is.EqualTo("1.0.0"));
                Assert.That(
                    restoredModel.FindVersion("v2")!.ModelVersion,
                    Is.EqualTo("2.0.0"));
            });

            await reloaded.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "metadata",
                "v1",
                restored.MetaEpoch);
            Assert.That(
                reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "metadata")!.Title,
                Is.EqualTo("urn:metadata-first"));
        }

        [Test]
        public async Task ConflictingPerVersionDocumentIdentityFailsClosed()
        {
            using (var service = new WotRegistryService(new FileWotRegistryStore(m_root)))
            {
                await service.InitializeAsync();
                foreach (string versionId in new[] { "v1", "v2" })
                {
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "identity",
                        VersionId = versionId,
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(
                            TestMaterialization.Td("urn:identity", versionId)),
                        SetAsDefault = false
                    });
                }
            }

            JsonObject manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
            JsonObject secondVersion =
                manifest["Groups"]![0]!["Resources"]![0]!["Versions"]![1]!.AsObject();
            secondVersion["DocumentId"] = "urn:other";
            File.WriteAllText(
                ManifestPath,
                manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            InvalidDataException error = Assert.ThrowsAsync<InvalidDataException>(
                async () => await reloaded.InitializeAsync())!;

            Assert.That(error.Message, Does.Contain("incompatible document identities"));
        }

        [Test]
        public async Task RetentionRoundTripKeepsActiveAndDefaultVersionReferences()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 3 };
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store, bounds))
            {
                await service.InitializeAsync();
                foreach (string versionId in new[] { "v1", "v2", "v3" })
                {
                    await service.UpsertResourceAsync(new WotUpsertResourceRequest
                    {
                        GroupId = WotRegistryGroups.ThingDescriptions,
                        ResourceId = "retained",
                        VersionId = versionId,
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(
                            TestMaterialization.Td("urn:retained", versionId)),
                        SetAsDefault = false
                    });
                }
                await service.ApplyProjectionResultsAsync(
                [
                    new WotResourceProjection(
                        WotRegistryGroups.ThingDescriptions,
                        "retained",
                        WoTLoadStateEnum.Active,
                        "v1",
                        refreshGeneration: 1,
                        materializedNodeCount: 1,
                        rootNodeId: new NodeId(1u),
                        validation: null,
                        diagnostics: [],
                        lastRefreshTime: DateTime.UtcNow)
                    {
                        VersionId = "v1"
                    }
                ]);
                WotResource beforeSwitch = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "retained")!;
                await service.SetDefaultVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "retained",
                    "v2",
                    beforeSwitch.MetaEpoch);
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "retained",
                    VersionId = "v4",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:retained", "v4")),
                    SetAsDefault = false
                });
            }

            using var reloaded = new WotRegistryService(
                new FileWotRegistryStore(m_root),
                bounds);
            await reloaded.InitializeAsync();
            WotResource restored = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "retained")!;

            Assert.Multiple(() =>
            {
                Assert.That(restored.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(restored.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(restored.FindVersion(restored.ActiveVersionId), Is.Not.Null);
                Assert.That(restored.FindVersion(restored.DefaultVersionId), Is.Not.Null);
                Assert.That(
                    restored.Versions.Select(version => version.VersionId),
                    Is.EqualTo(new[] { "v1", "v2", "v4" }));
            });
        }

        [Test]
        public async Task StructuralVersionWithoutContentRoundTrips()
        {
            var store = new FileWotRegistryStore(m_root);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "placeholder",
                    "v1",
                    WoTDocumentKindEnum.ThingDescription);
            }

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();
            WotResource resource = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "placeholder")!;

            Assert.Multiple(() =>
            {
                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllBytes(ManifestPath));
                Assert.That(
                    manifest.RootElement.GetProperty("SchemaVersion").GetInt32(),
                    Is.EqualTo(4));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(resource.Versions, Has.Length.EqualTo(1));
                Assert.That(resource.Versions[0].HasContent, Is.False);
                Assert.That(resource.Versions[0].Epoch, Is.EqualTo(1));
                Assert.That(resource.MetaEpoch, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task Schema3ManifestMigratesVersionAndResourceMetaDefaults()
        {
            using (var service = new WotRegistryService(new FileWotRegistryStore(m_root)))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(TdRequest("legacy", "urn:legacy"));
                await service.ValidateResourceAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "legacy");
            }

            JsonObject manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
            manifest["SchemaVersion"] = 3;
            JsonObject resource = manifest["Groups"]![0]!["Resources"]![0]!.AsObject();
            resource.Remove("MetaCreatedAt");
            resource.Remove("MetaModifiedAt");
            JsonObject version = resource["Versions"]![0]!.AsObject();
            resource["Validation"] = version["Validation"]!.DeepClone();
            version.Remove("Epoch");
            version.Remove("Labels");
            version.Remove("HasContent");
            version.Remove("Validation");
            version.Remove("DocumentId");
            version.Remove("Title");
            File.WriteAllText(ManifestPath, manifest.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));

            using var reloaded = new WotRegistryService(new FileWotRegistryStore(m_root));
            await reloaded.InitializeAsync();
            WotResource migrated = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "legacy")!;

            Assert.Multiple(() =>
            {
                Assert.That(migrated.DefaultVersion!.Epoch, Is.EqualTo(1));
                Assert.That(migrated.DefaultVersion.HasContent, Is.True);
                Assert.That(migrated.DefaultVersion.Labels, Is.Empty);
                Assert.That(migrated.DefaultVersion.Validation, Is.Not.Null);
                Assert.That(migrated.DefaultVersion.DocumentId, Is.EqualTo("urn:legacy"));
                Assert.That(migrated.DefaultVersion.Title, Is.EqualTo("urn:legacy-1"));
                Assert.That(
                    migrated.DefaultVersion.Validation!.FormatOutcome,
                    Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(migrated.MetaCreatedAt, Is.Not.Default);
                Assert.That(
                    migrated.MetaModifiedAt,
                    Is.GreaterThanOrEqualTo(migrated.MetaCreatedAt));
            });
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
                    Content = ByteString.From(TestMaterialization.Td("urn:a"))
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
            ByteString tdContent = await reloaded.ReadContentAsync(td!.Versions[0]);

            Assert.Multiple(() =>
            {
                Assert.That(inStore, Is.Not.Empty,
                    "The injected store must hold the document bytes, which is what lets a " +
                    "distributed deployment put them somewhere every node can reach.");
                Assert.That(inRegistry, Is.Empty,
                    "The registry folder must no longer hold blobs once a store is injected.");
                Assert.That(td, Is.Not.Null);
                Assert.That(
                    Encoding.UTF8.GetString(tdContent.Span.ToArray()),
                    Does.Contain("urn:a"),
                    "A reload must read the document back out of the injected store.");
            });
        }

        [Test]
        public async Task InjectedStoreDoesNotRewriteExistingMatchingBlobDuringMetadataCommit()
        {
            var resourceStore = new CorruptingResourceStore();
            var store = new FileWotRegistryStore(m_root, resourceStore);
            using (var service = new WotRegistryService(store))
            {
                await service.InitializeAsync();
                await service.UpsertResourceAsync(TdRequest("a", "urn:a"));
                Assert.That(resourceStore.WriteCount, Is.EqualTo(1));

                resourceStore.FailOnWrite = true;
                await service.AddRegistryLabelAsync("environment", "test");
            }

            var reloadStore = new FileWotRegistryStore(m_root, resourceStore);
            using var reloaded = new WotRegistryService(reloadStore);
            await reloaded.InitializeAsync();

            Assert.That(resourceStore.WriteCount, Is.EqualTo(1));
            Assert.That(
                reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a"),
                Is.Not.Null);
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
                    Content = ByteString.From(TestMaterialization.InvalidJson())
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
                Content = ByteString.From(TestMaterialization.Td("urn:a", "v1"))
            });
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(TestMaterialization.Td("urn:a", "v2"))
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
        public async Task FutureSchemaFailsClosed()
        {
            await PersistResourceAsync("a", "urn:a");
            File.WriteAllBytes(
                ManifestPath,
                WithSchemaVersion(File.ReadAllBytes(ManifestPath), schemaVersion: 5));
            var store = new FileWotRegistryStore(m_root);

            NotSupportedException error = Assert.ThrowsAsync<NotSupportedException>(
                async () => await store.LoadAsync());

            Assert.That(error.Message, Does.Contain("schema 5"));
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

        /// <summary>
        /// A version whose recorded digest does not describe its content must be
        /// rejected before anything is written.
        /// </summary>
        /// <remarks>
        /// The digest is the store key, so a version claiming a digest that does
        /// not name its content is a version pointing at a document that was
        /// never stored, and that is how the store reports it. The property
        /// under test - rejected, and rejected before any file is created - is
        /// unchanged; only the diagnostic tracks content addressing.
        /// </remarks>
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
                        ByteString.From(new byte[32]),
                        version.ContentLength,
                        version.ContentType,
                        version.Format,
                        version.CreatedAt,
                        version.ModifiedAt);
                    WotResource malformed = CloneResource(
                        resource,
                        versions: ImmutableArray.Create(tampered));
                    return snapshot.WithGroup(
                        group.WithResources(
                            group.Resources.SetItem(resource.ResourceId, malformed),
                            group.Epoch),
                        snapshot.Generation);
                },
                "is missing");
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
                        version.Digest,
                        version.ContentLength,
                        version.ContentType,
                        version.Format,
                        version.CreatedAt,
                        version.ModifiedAt);
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

        /// <summary>
        /// Captures the durable data files of the registry root: the manifests
        /// and the committed blobs.
        /// </summary>
        /// <remarks>
        /// The lock file and the staging directory are excluded because neither
        /// is durable state. Staging holds bytes a writer made durable before
        /// asking for a commit, so a commit that fails closed necessarily leaves
        /// an entry there - that is the cost of writing content before the
        /// manifest that names it. Such an entry is inert: nothing can reference
        /// a document until a manifest names it, and only promoted entries are
        /// ever named. What must not change when a commit is refused is the
        /// committed data, which is what this captures.
        /// </remarks>
        private Dictionary<string, byte[]> CaptureDataFiles()
        {
            return Directory.GetFiles(m_root, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                        Path.GetFileName(path),
                        ".wot-registry.lock",
                        StringComparison.Ordinal) &&
                    !IsUnderStaging(path))
                .ToDictionary(
                    RelativePath,
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }

        private bool IsUnderStaging(string path)
        {
            return RelativePath(path).StartsWith(
                "staging" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
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
            const string current = "\"SchemaVersion\": 4";
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
                Content = ByteString.From(TestMaterialization.Td(thingId))
            };
        }

        private static byte[] ThingDescriptionWithMetadata(
            string id,
            string title,
            string baseUri)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"uav:object\",\"id\":\"" + id + "\"," +
                "\"title\":\"" + title + "\",\"base\":\"" + baseUri + "\"}");
        }

        private static byte[] ThingModelWithMetadata(
            string id,
            string title,
            string modelVersion)
        {
            return Encoding.UTF8.GetBytes(
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"@type\":\"tm:ThingModel\",\"id\":\"" + id + "\"," +
                "\"title\":\"" + title + "\",\"version\":{\"model\":\"" +
                modelVersion + "\"}}");
        }

        private sealed class CorruptingResourceStore : IXRegistryResourceStore
        {
            public int WriteCount { get; private set; }

            public bool FailOnWrite { get; set; }

            public ValueTask<ByteString> ReadAsync(
                string resourceKey,
                long offset,
                int count,
                CancellationToken ct = default)
            {
                if (!m_blobs.TryGetValue(resourceKey, out byte[]? content))
                {
                    return new ValueTask<ByteString>(default(ByteString));
                }
                if (offset >= content.Length)
                {
                    return new ValueTask<ByteString>(ByteString.From([]));
                }
                int take = (int)Math.Min(count, content.Length - offset);
                return new ValueTask<ByteString>(
                    ByteString.From(content.AsSpan((int)offset, take).ToArray()));
            }

            public ValueTask WriteAsync(
                string resourceKey,
                long offset,
                ByteString data,
                CancellationToken ct = default)
            {
                WriteCount++;
                if (FailOnWrite)
                {
                    m_blobs[resourceKey] = [0];
                    throw new IOException("Injected partial write.");
                }
                m_blobs[resourceKey] = data.Span.ToArray();
                return default;
            }

            public ValueTask<long> GetLengthAsync(
                string resourceKey,
                CancellationToken ct = default)
            {
                return new ValueTask<long>(
                    m_blobs.TryGetValue(resourceKey, out byte[]? content) ? content.Length : -1);
            }

            public ValueTask<bool> DeleteAsync(
                string resourceKey,
                CancellationToken ct = default)
            {
                return new ValueTask<bool>(m_blobs.Remove(resourceKey));
            }

            private readonly Dictionary<string, byte[]> m_blobs = new(StringComparer.Ordinal);
        }
    }
}
