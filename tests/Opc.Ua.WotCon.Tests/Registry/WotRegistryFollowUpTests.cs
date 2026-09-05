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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    [TestFixture]
    public sealed class WotRegistryFollowUpTests
    {
        [Test]
        public async Task EmptyCreationReusesPendingAndExplicitConflictIsRejected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
            using var service = new WotRegistryService(bounds: bounds);
            await CreateCommittedVersionsAsync(service, "pending", "v1", "v2");

            var created = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "pending",
                string.Empty,
                WoTDocumentKindEnum.ThingDescription);
            Assert.That(created, Is.Not.Null);
            WotRegistrySnapshot afterFirst = service.Current;
            WotResource resource = afterFirst.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "pending")!;

            var reusedCreate = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "pending",
                string.Empty,
                WoTDocumentKindEnum.ThingDescription);
            (WotResource _, WotResourceVersion reusedGet, bool getCreated) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "pending",
                    string.Empty,
                    WoTDocumentKindEnum.ThingDescription);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.TryCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "pending",
                    "different",
                    WoTDocumentKindEnum.ThingDescription))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(service.Current, Is.SameAs(afterFirst));
                Assert.That(reusedCreate, Is.Not.Null);
                Assert.That(
                    reusedCreate!.Value.Version,
                    Is.SameAs(created!.Value.Version));
                Assert.That(reusedGet, Is.SameAs(created.Value.Version));
                Assert.That(getCreated, Is.False);
                Assert.That(resource.Versions.Count(version => version.HasContent), Is.EqualTo(2));
                Assert.That(resource.Versions.Count(version => !version.HasContent), Is.EqualTo(1));
                Assert.That(resource.FindVersion("v1"), Is.Not.Null);
                Assert.That(resource.FindVersion("v2"), Is.Not.Null);
                Assert.That(resource.FindVersion(created!.Value.Version.VersionId), Is.Not.Null);
            });
        }

        [Test]
        public async Task ClosingPendingVersionTrimsEligibleCommittedVersion()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
            using var service = new WotRegistryService(bounds: bounds);
            await CreateCommittedVersionsAsync(service, "close-pending", "v1", "v2");
            var created = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "close-pending",
                string.Empty,
                WoTDocumentKindEnum.ThingDescription);
            Assert.That(created, Is.Not.Null);
            string pendingVersionId = created!.Value.Version.VersionId;
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "close-pending")!;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                Request(
                    "close-pending",
                    TestMaterialization.Td("urn:close-pending", "third"),
                    pendingVersionId,
                    setAsDefault: false,
                    expectedDigestHex: string.Empty));
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "close-pending")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(before.Versions, Has.Length.EqualTo(3));
                Assert.That(before.FindVersion("v2"), Is.Not.Null);
                Assert.That(after.Versions.Count(version => version.HasContent), Is.EqualTo(2));
                Assert.That(after.Versions.Any(version => !version.HasContent), Is.False);
                Assert.That(after.FindVersion("v1"), Is.Not.Null);
                Assert.That(after.FindVersion("v2"), Is.Null);
                Assert.That(after.FindVersion(pendingVersionId)!.HasContent, Is.True);
                Assert.That(after.MetaEpoch, Is.EqualTo(before.MetaEpoch + 1));
            });
        }

        [Test]
        public async Task ClosingPendingVersionRejectsWhenCommittedVersionsAreProtected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
            var store = new RecordingRegistryStore();
            using var service = new WotRegistryService(store, bounds);
            await CreateCommittedVersionsAsync(service, "protected-close", "v1", "v2");
            var created = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "protected-close",
                string.Empty,
                WoTDocumentKindEnum.ThingDescription);
            Assert.That(created, Is.Not.Null);
            string pendingVersionId = created!.Value.Version.VersionId;
            await SetActiveVersionAsync(service, "protected-close", "v2");
            WotRegistrySnapshot before = service.Current;
            store.BlobStore.ResetWriteCount();

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                Request(
                    "protected-close",
                    TestMaterialization.Td("urn:protected-close", "third"),
                    pendingVersionId,
                    setAsDefault: false,
                    expectedDigestHex: string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(service.Current, Is.SameAs(before));
                Assert.That(store.BlobStore.WriteCount, Is.Zero);
                WotResource resource = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "protected-close")!;
                Assert.That(resource.FindVersion("v1"), Is.Not.Null);
                Assert.That(resource.FindVersion("v2"), Is.Not.Null);
                Assert.That(resource.FindVersion(pendingVersionId)!.HasContent, Is.False);
            });
        }

        [Test]
        public async Task PendingAllocationProtectsDesiredVersion()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 3 };
            var store = new RecordingRegistryStore();
            using (var service = new WotRegistryService(store, bounds))
            {
                await CreateCommittedVersionsAsync(service, "desired", "v1", "v2", "v3");
                await SetActiveVersionAsync(service, "desired", "v1");
                WotResource current = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired")!;
                await service.SetDefaultVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "desired",
                    "v2",
                    current.MetaEpoch);
                WotRegistrySnapshot snapshot = service.Current;
                WotResource resource = snapshot.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired")!;
                store.SetSnapshot(ReplaceResource(snapshot, resource.With(desiredVersionId: "v3")));
            }

            using var reloaded = new WotRegistryService(store, bounds);
            await reloaded.InitializeAsync();
            WotRegistrySnapshot before = reloaded.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await reloaded.TryCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "desired",
                    "v4",
                    WoTDocumentKindEnum.ThingDescription))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(reloaded.Current, Is.SameAs(before));
                WotResource resource = reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired")!;
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DesiredVersionId, Is.EqualTo("v3"));
                Assert.That(resource.Versions, Has.Length.EqualTo(3));
            });
        }

        [Test]
        public async Task ClosingPendingVersionProtectsDesiredVersion()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 3 };
            var store = new RecordingRegistryStore();
            string pendingVersionId;
            using (var service = new WotRegistryService(store, bounds))
            {
                await CreateCommittedVersionsAsync(
                    service,
                    "desired-close",
                    "v1",
                    "v2",
                    "v3");
                await SetActiveVersionAsync(service, "desired-close", "v2");
                var created = await service.TryCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "desired-close",
                    "v4",
                    WoTDocumentKindEnum.ThingDescription);
                Assert.That(created, Is.Not.Null);
                pendingVersionId = created!.Value.Version.VersionId;
                WotRegistrySnapshot snapshot = service.Current;
                WotResource resource = snapshot.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired-close")!;
                store.SetSnapshot(ReplaceResource(snapshot, resource.With(desiredVersionId: "v3")));
            }

            using var reloaded = new WotRegistryService(store, bounds);
            await reloaded.InitializeAsync();
            WotRegistrySnapshot before = reloaded.Current;
            WotRegistryMutationResult result = await reloaded.UpsertResourceAsync(
                Request(
                    "desired-close",
                    TestMaterialization.Td("urn:desired-close", "fourth"),
                    pendingVersionId,
                    setAsDefault: false,
                    expectedDigestHex: string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(reloaded.Current, Is.SameAs(before));
                WotResource resource = reloaded.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired-close")!;
                Assert.That(resource.FindVersion("v1"), Is.Not.Null);
                Assert.That(resource.FindVersion("v2"), Is.Not.Null);
                Assert.That(resource.FindVersion("v3"), Is.Not.Null);
                Assert.That(resource.FindVersion(pendingVersionId)!.HasContent, Is.False);
            });
        }

        [Test]
        public async Task AddingVersionAdvancesMetaEpochAndRejectsStaleMetaMutation()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(
                Request("meta", TestMaterialization.Td("urn:meta", "first"), "v1"));
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "meta")!;
            await Task.Delay(1).ConfigureAwait(false);

            await service.UpsertResourceAsync(
                Request(
                    "meta",
                    TestMaterialization.Td("urn:meta", "second"),
                    "v2",
                    setAsDefault: false));
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "meta")!;
            WotRegistryMutationResult stale = await service.AddResourceLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "meta",
                "owner",
                "stale",
                before.MetaEpoch);

            Assert.Multiple(() =>
            {
                Assert.That(after.MetaEpoch, Is.EqualTo(before.MetaEpoch + 1));
                Assert.That(after.MetaModifiedAt, Is.GreaterThan(before.MetaModifiedAt));
                Assert.That(stale.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(after.MetaLabels.ContainsKey("owner"), Is.False);
            });
        }

        [Test]
        public async Task SameByteMetadataUpdatePreservesMaterializationState()
        {
            var store = new RecordingRegistryStore();
            using var service = new WotRegistryService(store);
            byte[] content = TestMaterialization.Td("urn:metadata-state");
            await service.UpsertResourceAsync(Request("metadata-state", content, "v1"));
            await SetActiveVersionAsync(service, "metadata-state", "v1");
            await service.ValidateResourceAsync(
                WotRegistryGroups.ThingDescriptions,
                "metadata-state");
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "metadata-state")!;
            WoTValidationOutcomeDataType validation = before.Validation!;
            WoTValidationOutcomeDataType versionValidation = before.FindVersion("v1")!.Validation!;
            var changes = new System.Collections.Generic.List<WotRegistryChangedEventArgs>();
            int materializationRequests = 0;
            service.Changed += (_, e) =>
            {
                changes.Add(e);
                if (!e.ProjectionOnly)
                {
                    materializationRequests++;
                }
            };
            store.BlobStore.ResetWriteCount();
            WotUpsertResourceRequest update = Request(
                "metadata-state",
                content,
                "v1",
                expectedDigestHex: before.FindVersion("v1")!.DigestHex);
            update.ContentType = "application/wot+json";
            update.Format = "WoT-TD/1.1+profile";

            WotRegistryMutationResult result = await service.UpsertResourceAsync(update);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "metadata-state")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(changes, Has.Count.EqualTo(1));
                Assert.That(changes[0].ProjectionOnly, Is.True);
                Assert.That(materializationRequests, Is.Zero);
                Assert.That(store.BlobStore.WriteCount, Is.Zero);
                Assert.That(after.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(after.Validation, Is.SameAs(validation));
                Assert.That(after.Diagnostics, Is.EqualTo(before.Diagnostics));
                Assert.That(after.FindVersion("v1")!.Validation, Is.SameAs(versionValidation));
                Assert.That(after.MetaEpoch, Is.EqualTo(before.MetaEpoch));
            });
        }

        [Test]
        public async Task DesiredVersionContentDrivesMaterializationButMetadataDoesNot()
        {
            var store = new RecordingRegistryStore();
            using (var service = new WotRegistryService(store))
            {
                await CreateCommittedVersionsAsync(service, "desired-materialization", "v1", "v2");
                await SetActiveVersionAsync(service, "desired-materialization", "v2");
                await service.ValidateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "desired-materialization",
                    "v1");
                WotRegistrySnapshot snapshot = service.Current;
                WotResource resource = snapshot.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "desired-materialization")!;
                store.SetSnapshot(ReplaceResource(
                    snapshot,
                    resource.With(
                        desiredVersionId: "v2",
                        diagnostics: ImmutableArray.Create("retained"))));
            }

            using var reloaded = new WotRegistryService(store);
            await reloaded.InitializeAsync();
            WotResource before = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "desired-materialization")!;
            WoTValidationOutcomeDataType validation = before.Validation!;
            byte[] selectedContent = TestMaterialization.Td(
                "urn:desired-materialization",
                "v2");
            var changes = new System.Collections.Generic.List<WotRegistryChangedEventArgs>();
            reloaded.Changed += (_, e) => changes.Add(e);
            WotUpsertResourceRequest metadata = Request(
                "desired-materialization",
                selectedContent,
                "v2",
                setAsDefault: false,
                expectedDigestHex: before.FindVersion("v2")!.DigestHex);
            metadata.ContentType = "application/wot+json";

            await reloaded.UpsertResourceAsync(metadata);
            WotResource afterMetadata = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "desired-materialization")!;
            WotUpsertResourceRequest content = Request(
                "desired-materialization",
                TestMaterialization.Td("urn:desired-materialization", "updated"),
                "v2",
                setAsDefault: false,
                expectedDigestHex: afterMetadata.FindVersion("v2")!.DigestHex);
            await reloaded.UpsertResourceAsync(content);
            WotResource afterContent = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "desired-materialization")!;

            Assert.Multiple(() =>
            {
                Assert.That(changes, Has.Count.EqualTo(2));
                Assert.That(changes[0].ProjectionOnly, Is.True);
                Assert.That(afterMetadata.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(afterMetadata.Validation, Is.SameAs(validation));
                Assert.That(afterMetadata.Diagnostics, Is.EqualTo(before.Diagnostics));
                Assert.That(changes[1].ProjectionOnly, Is.False);
                Assert.That(afterContent.LoadState, Is.EqualTo(WoTLoadStateEnum.Unloaded));
                Assert.That(afterContent.Validation, Is.Null);
                Assert.That(afterContent.Diagnostics, Is.Empty);
                Assert.That(afterContent.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(afterContent.DesiredVersionId, Is.EqualTo("v2"));
            });
        }

        [Test]
        public async Task DigestMatchWithoutVersionIdTargetsDefaultInsteadOfDesired()
        {
            byte[] content = TestMaterialization.Td("urn:default-selection");
            ByteString digest = WotContentDigest.Compute(content);
            DateTime now = DateTime.UtcNow;
            var v1 = new WotResourceVersion(
                "v1",
                digest,
                content.Length,
                "default-old",
                "WoT-TD/1.1",
                now,
                now)
            {
                DocumentId = "urn:default-selection",
                Title = "urn:default-selection-1"
            };
            var v2 = new WotResourceVersion(
                "v2",
                digest,
                content.Length,
                "desired-old",
                "WoT-TD/1.1",
                now,
                now)
            {
                DocumentId = "urn:default-selection",
                Title = "urn:default-selection-1"
            };
            var resource = new WotResource(
                WotRegistryGroups.ThingDescriptions,
                "default-selection",
                WoTDocumentKindEnum.ThingDescription,
                [v1, v2],
                defaultVersionId: "v1",
                desiredVersionId: "v2",
                epoch: 2,
                thingId: v1.DocumentId,
                title: v1.Title);
            var group = new WotResourceGroup(
                WotRegistryGroups.ThingDescriptions,
                WoTDocumentKindEnum.ThingDescription,
                ImmutableDictionary<string, WotResource>.Empty.Add(resource.ResourceId, resource));
            var store = new RecordingRegistryStore(
                new WotRegistrySnapshot(
                    2,
                    ImmutableDictionary<string, WotResourceGroup>.Empty.Add(group.GroupId, group)));
            await store.BlobStore.SeedAsync(v1.DigestHex, ByteString.From(content));
            using var service = new WotRegistryService(store);
            await service.InitializeAsync();
            WotUpsertResourceRequest update = Request(
                "default-selection",
                content,
                versionId: null,
                setAsDefault: false);
            update.ContentType = "updated";

            await service.UpsertResourceAsync(update);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "default-selection")!;

            Assert.Multiple(() =>
            {
                Assert.That(after.FindVersion("v1")!.ContentType, Is.EqualTo("updated"));
                Assert.That(after.FindVersion("v1")!.Epoch, Is.EqualTo(2));
                Assert.That(after.FindVersion("v2")!.ContentType, Is.EqualTo("desired-old"));
                Assert.That(after.FindVersion("v2")!.Epoch, Is.EqualTo(1));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(after.DesiredVersionId, Is.EqualTo("v2"));
            });
        }

        [Test]
        public async Task NullContentMetadataNormalizesWithoutPerpetualMutation()
        {
            using var service = new WotRegistryService();
            byte[] content = TestMaterialization.Td("urn:null-metadata");
            WotUpsertResourceRequest request = Request("null-metadata", content, "v1");
            request.ContentType = null!;
            request.Format = null!;
            await service.UpsertResourceAsync(request);
            long generation = service.Current.Generation;

            WotRegistryMutationResult second = await service.UpsertResourceAsync(request);
            WotResourceVersion version = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "null-metadata")!.FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(second.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
                Assert.That(service.Current.Generation, Is.EqualTo(generation));
                Assert.That(version.ContentType, Is.Empty);
                Assert.That(version.Format, Is.Empty);
            });
        }

        [Test]
        public async Task UpsertCaseCollisionThrowsBadNodeIdExists()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(
                Request("collision", TestMaterialization.Td("urn:collision", "first"), "V1"));
            WotRegistrySnapshot before = service.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.UpsertResourceAsync(
                    Request(
                        "collision",
                        TestMaterialization.Td("urn:collision", "second"),
                        "v1")))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
                Assert.That(service.Current, Is.SameAs(before));
            });
        }

        [Test]
        public void MalformedUpsertVersionIdThrowsBadInvalidArgument()
        {
            using var service = new WotRegistryService();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.UpsertResourceAsync(
                    Request(
                        "invalid-upsert",
                        TestMaterialization.Td("urn:invalid-upsert"),
                        "invalid/version")))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void NewOverlongUpsertVersionIdThrowsBadInvalidArgument()
        {
            using var service = new WotRegistryService();

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.UpsertResourceAsync(
                    Request(
                        "overlong-upsert",
                        TestMaterialization.Td("urn:overlong-upsert"),
                        new string('a', 129))))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task ServerAssignedVersionIdRejectsSequenceOverflow()
        {
            using var service = new WotRegistryService();
            string maximum = long.MaxValue.ToString(CultureInfo.InvariantCulture);
            await service.UpsertResourceAsync(
                Request("overflow", TestMaterialization.Td("urn:overflow"), maximum));
            WotRegistrySnapshot before = service.Current;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.TryCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "overflow",
                    string.Empty,
                    WoTDocumentKindEnum.ThingDescription))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(service.Current, Is.SameAs(before));
            });
        }

        [Test]
        public async Task StaleCloseAfterDeletionCannotResurrectVersion()
        {
            using var service = new WotRegistryService();
            (WotResource _, WotResourceVersion baseline, bool _) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "stale-delete",
                    "v1",
                    WoTDocumentKindEnum.ThingDescription);
            await service.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "stale-delete",
                "v1",
                baseline.Epoch);
            WotUpsertResourceRequest stale = Request(
                "stale-delete",
                TestMaterialization.Td("urn:stale-delete"),
                "v1",
                expectedDigestHex: string.Empty);
            stale.ExpectedVersionIncarnation = baseline.IncarnationId;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(stale);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "stale-delete"), Is.Null);
            });
        }

        [Test]
        public async Task StaleCloseAfterDeleteRecreateCannotReplaceNewIncarnation()
        {
            using var service = new WotRegistryService();
            (WotResource _, WotResourceVersion baseline, bool _) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "stale-aba",
                    "v1",
                    WoTDocumentKindEnum.ThingDescription);
            await service.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "stale-aba",
                "v1",
                baseline.Epoch);
            (WotResource _, WotResourceVersion replacement, bool _) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "stale-aba",
                    "v1",
                    WoTDocumentKindEnum.ThingDescription);
            WotRegistrySnapshot before = service.Current;
            WotUpsertResourceRequest stale = Request(
                "stale-aba",
                TestMaterialization.Td("urn:stale-aba"),
                "v1",
                expectedDigestHex: string.Empty);
            stale.ExpectedVersionIncarnation = baseline.IncarnationId;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(stale);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(service.Current, Is.SameAs(before));
                WotResourceVersion current = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "stale-aba")!.FindVersion("v1")!;
                Assert.That(current, Is.SameAs(replacement));
                Assert.That(current.HasContent, Is.False);
            });
        }

        [Test]
        public void VersionedInterfaceExposesAtomicProjectedDelete()
        {
            MethodInfo? method = typeof(IWotVersionedRegistryService).GetMethod(
                "DeleteProjectedEntityAsync",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.Multiple(() =>
            {
                Assert.That(method, Is.Not.Null);
                Assert.That(method!.GetParameters()[3].ParameterType, Is.EqualTo(typeof(bool)));
            });
        }

        [Test]
        public void DeletePolicyIsAnOptionalCapability()
        {
            MethodInfo[] baseDeleteMethods = typeof(IWotRegistryService)
                .GetMethods()
                .Where(method => method.Name == nameof(IWotRegistryService.DeleteResourceAsync))
                .ToArray();
            MethodInfo? policyDelete = typeof(IWotDeletePolicyRegistryService).GetMethod(
                nameof(IWotDeletePolicyRegistryService.DeleteResourceAsync));

            Assert.Multiple(() =>
            {
                Assert.That(baseDeleteMethods, Has.Length.EqualTo(1));
                Assert.That(policyDelete, Is.Not.Null);
                Assert.That(
                    typeof(IWotDeletePolicyRegistryService)
                        .IsAssignableFrom(typeof(WotRegistryService)),
                    Is.True);
            });
        }

        [Test]
        public void ExpectedVersionIncarnationRemainsInternal()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    typeof(WotUpsertResourceRequest).GetProperty(
                        "ExpectedVersionIncarnation",
                        BindingFlags.Public | BindingFlags.Instance),
                    Is.Null);
                Assert.That(
                    typeof(WotUpsertResourceRequest).GetProperty(
                        "ExpectedVersionIncarnation",
                        BindingFlags.NonPublic | BindingFlags.Instance),
                    Is.Not.Null);
            });
        }

        [Test]
        public async Task BenignVersionCopiesPreserveOpenHandleIncarnation()
        {
            using var service = new WotRegistryService();
            byte[] original = TestMaterialization.Td("urn:benign-copy", "first");
            await service.UpsertResourceAsync(Request("benign-copy", original, "v1"));
            WotResourceVersion baseline = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "benign-copy")!.FindVersion("v1")!;
            await service.AddVersionLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "benign-copy",
                "v1",
                "stage",
                "open");
            await SetActiveVersionAsync(service, "benign-copy", "v1");
            WotResourceVersion copied = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "benign-copy")!.FindVersion("v1")!;
            WotUpsertResourceRequest close = Request(
                "benign-copy",
                TestMaterialization.Td("urn:benign-copy", "second"),
                "v1",
                expectedDigestHex: baseline.DigestHex);
            close.ExpectedVersionIncarnation = baseline.IncarnationId;

            WotRegistryMutationResult result = await service.UpsertResourceAsync(close);

            Assert.Multiple(() =>
            {
                Assert.That(copied, Is.Not.SameAs(baseline));
                Assert.That(copied.IncarnationId, Is.EqualTo(baseline.IncarnationId));
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "benign-copy")!.FindVersion("v1")!.IncarnationId,
                    Is.EqualTo(baseline.IncarnationId));
            });
        }

        [Test]
        public async Task ProjectedDeleteStableVersionRoleUsesVersionEpoch()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(service, "role-delete", "v1", "v2");
            WotResource beforeSwitch = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "role-delete")!;
            long v1Epoch = beforeSwitch.FindVersion("v1")!.Epoch;
            await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "role-delete",
                "v2",
                beforeSwitch.MetaEpoch);

            WotRegistryMutationResult result = await service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "role-delete",
                "v1",
                deleteLogicalResource: false,
                expectedEpoch: v1Epoch);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "role-delete")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.FindVersion("v1"), Is.Null);
                Assert.That(after.FindVersion("v2"), Is.Not.Null);
            });
        }

        [Test]
        public async Task ProjectedDeleteRejectsLogicalRoleAfterConcurrentSwitchAway()
        {
            var store = new RecordingRegistryStore();
            using var service = new WotRegistryService(store);
            await CreateCommittedVersionsAsync(service, "concurrent-delete", "v1", "v2");
            WotResource beforeSwitch = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "concurrent-delete")!;
            long v1Epoch = beforeSwitch.FindVersion("v1")!.Epoch;
            store.BlockNextCommit();

            Task<WotRegistryMutationResult> switchTask = service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "concurrent-delete",
                "v2",
                beforeSwitch.MetaEpoch).AsTask();
            await store.WaitForBlockedCommitAsync().ConfigureAwait(false);
            Task<WotRegistryMutationResult> deleteTask = service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "concurrent-delete",
                "v1",
                deleteLogicalResource: true,
                expectedEpoch: v1Epoch).AsTask();
            try
            {
                await Task.Delay(20).ConfigureAwait(false);
                Assert.That(deleteTask.IsCompleted, Is.False);
            }
            finally
            {
                store.ReleaseBlockedCommit();
            }
            await switchTask.ConfigureAwait(false);
            WotRegistryMutationResult deleted = await deleteTask.ConfigureAwait(false);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "concurrent-delete")!;

            Assert.Multiple(() =>
            {
                Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.FindVersion("v1"), Is.Not.Null);
                Assert.That(after.FindVersion("v2"), Is.Not.Null);
            });
        }

        [Test]
        public async Task ProjectedDeleteUsesResourceAndVersionEpochSpaces()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(service, "delete-epochs", "v1", "v2");
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-epochs")!;

            WotRegistryMutationResult defaultWithVersionEpoch =
                await service.DeleteProjectedEntityAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-epochs",
                    "v1",
                    deleteLogicalResource: true,
                    expectedEpoch: before.FindVersion("v1")!.Epoch);
            WotRegistryMutationResult versionWithResourceEpoch =
                await service.DeleteProjectedEntityAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-epochs",
                    "v2",
                    deleteLogicalResource: false,
                    expectedEpoch: before.MetaEpoch);
            WotRegistryMutationResult exactVersion = await service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-epochs",
                "v2",
                deleteLogicalResource: false,
                expectedEpoch: before.FindVersion("v2")!.Epoch);
            WotResource afterVersionDelete = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-epochs")!;
            WotRegistryMutationResult logicalResource = await service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-epochs",
                "v1",
                deleteLogicalResource: true,
                expectedEpoch: afterVersionDelete.MetaEpoch);

            Assert.Multiple(() =>
            {
                Assert.That(defaultWithVersionEpoch.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(versionWithResourceEpoch.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(exactVersion.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(afterVersionDelete.FindVersion("v1"), Is.Not.Null);
                Assert.That(afterVersionDelete.FindVersion("v2"), Is.Null);
                Assert.That(logicalResource.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-epochs"), Is.Null);
            });
        }

        [Test]
        public async Task ProjectedDeleteRejectsVersionRoleAfterSwitchToDefault()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(service, "epoch-collision", "v1");
            await service.AddVersionLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision",
                "v1",
                "first",
                "1");
            await service.AddVersionLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision",
                "v1",
                "second",
                "2");
            await service.UpsertResourceAsync(
                Request(
                    "epoch-collision",
                    TestMaterialization.Td("urn:epoch-collision", "v2"),
                    "v2",
                    setAsDefault: true));
            WotResource beforeSwitch = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision")!;
            long staleVersionEpoch = beforeSwitch.FindVersion("v1")!.Epoch;
            await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision",
                "v1",
                beforeSwitch.MetaEpoch);
            WotResource afterSwitch = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision")!;
            Assert.That(afterSwitch.MetaEpoch, Is.EqualTo(staleVersionEpoch));

            // In the new hierarchy, deleteLogicalResource is authoritative —
            // requesting a version-delete for v1 (the current default) succeeds
            // because the caller's role decision is fixed by structural position.
            WotRegistryMutationResult versionDelete = await service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision",
                "v1",
                deleteLogicalResource: false,
                expectedEpoch: staleVersionEpoch);
            Assert.That(versionDelete.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));

            // The resource still exists with v2 after deleting v1.
            WotResource afterVersionDelete = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision")!;
            Assert.Multiple(() =>
            {
                Assert.That(afterVersionDelete.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(afterVersionDelete.FindVersion("v1"), Is.Null);
                Assert.That(afterVersionDelete.FindVersion("v2"), Is.Not.Null);
            });

            // Logical resource delete removes everything.
            WotRegistryMutationResult logical = await service.DeleteProjectedEntityAsync(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision",
                string.Empty,
                deleteLogicalResource: true,
                expectedEpoch: afterVersionDelete.MetaEpoch);

            Assert.That(logical.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "epoch-collision"), Is.Null);
        }

        [Test]
        public async Task ProjectedDeleteRejectsWrongRoleRegardlessOfEpoch()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(service, "wrong-role", "v1", "v2");
            WotRegistrySnapshot before = service.Current;
            WotResource resource = before.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "wrong-role")!;

            // In the new hierarchy, the role is authoritative from the caller.
            // deleteLogicalResource: false with the default versionId succeeds
            // (deletes that version, leaving the other).
            WotRegistryMutationResult defaultAsVersion =
                await service.DeleteProjectedEntityAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "wrong-role",
                    "v1",
                    deleteLogicalResource: false,
                    expectedEpoch: resource.FindVersion("v1")!.Epoch);
            Assert.That(defaultAsVersion.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));

            // deleteLogicalResource: true with a non-default versionId now
            // deletes the entire resource (versionId is ignored when
            // deleteLogicalResource is true).
            WotResource afterV1Delete = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "wrong-role")!;
            WotRegistryMutationResult versionAsLogical =
                await service.DeleteProjectedEntityAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "wrong-role",
                    "v2",
                    deleteLogicalResource: true,
                    expectedEpoch: afterV1Delete.MetaEpoch);
            Assert.That(versionAsLogical.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "wrong-role"), Is.Null);
        }

        [Test]
        public async Task DeletingLastCommittedVersionWithPendingIsRejected()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(service, "delete-pending", "v1");
            var pending = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-pending",
                "v2",
                WoTDocumentKindEnum.ThingDescription);
            Assert.That(pending, Is.Not.Null);
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-pending")!;

            WotRegistryMutationResult result = await service.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-pending",
                "v1",
                before.FindVersion("v1")!.Epoch);

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                WotResource after = service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-pending")!;
                Assert.That(after.FindVersion("v1"), Is.Not.Null);
                Assert.That(after.FindVersion("v2")!.HasContent, Is.False);
            });
        }

        [Test]
        public async Task DeletingDefaultChoosesNewestCommittedVersionBeforePending()
        {
            using var service = new WotRegistryService();
            await CreateCommittedVersionsAsync(
                service,
                "delete-replacement",
                "v1",
                "v2");
            await service.ValidateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-replacement",
                "v2");
            var pending = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-replacement",
                "v3",
                WoTDocumentKindEnum.ThingDescription);
            Assert.That(pending, Is.Not.Null);
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-replacement")!;
            WoTValidationOutcomeDataType expectedValidation =
                before.FindVersion("v2")!.Validation!;

            WotRegistryMutationResult result = await service.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-replacement",
                "v1",
                before.FindVersion("v1")!.Epoch);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-replacement")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.DesiredVersionId, Is.EqualTo("v2"));
                Assert.That(after.DefaultVersion!.HasContent, Is.True);
                Assert.That(after.LoadState, Is.EqualTo(WoTLoadStateEnum.Unloaded));
                Assert.That(after.Validation, Is.SameAs(expectedValidation));
                Assert.That(after.FindVersion("v2"), Is.Not.Null);
                Assert.That(after.FindVersion("v3")!.HasContent, Is.False);
            });
        }

        [Test]
        public async Task DeletingDefaultPreservesSurvivingCommittedDesiredVersion()
        {
            var store = new RecordingRegistryStore();
            using (var service = new WotRegistryService(store))
            {
                await CreateCommittedVersionsAsync(
                    service,
                    "delete-desired",
                    "v1",
                    "v2",
                    "v3");
                await SetActiveVersionAsync(service, "delete-desired", "v2");
                await service.ValidateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-desired",
                    "v2");
                var pending = await service.TryCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-desired",
                    "v4",
                    WoTDocumentKindEnum.ThingDescription);
                Assert.That(pending, Is.Not.Null);
                WotRegistrySnapshot snapshot = service.Current;
                WotResource resource = snapshot.FindResource(
                    WotRegistryGroups.ThingDescriptions,
                    "delete-desired")!;
                WotResourceVersion desired = resource.FindVersion("v2")!;
                WotResource selected = resource.With(
                        desiredVersionId: "v2",
                        validation: desired.Validation)
                    .WithSelectedVersionMetadata(desired.DocumentId, desired.Title);
                store.SetSnapshot(ReplaceResource(snapshot, selected));
            }

            using var reloaded = new WotRegistryService(store);
            await reloaded.InitializeAsync();
            WotResource before = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-desired")!;
            WotResourceVersion expectedSelected = before.FindVersion("v2")!;

            WotRegistryMutationResult result = await reloaded.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "delete-desired",
                "v1",
                before.FindVersion("v1")!.Epoch);
            WotResource after = reloaded.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "delete-desired")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v3"));
                Assert.That(after.DesiredVersionId, Is.EqualTo("v2"));
                Assert.That(after.DefaultVersion!.VersionId, Is.EqualTo("v2"));
                Assert.That(after.LoadState, Is.EqualTo(WoTLoadStateEnum.Active));
                Assert.That(after.Validation, Is.SameAs(expectedSelected.Validation));
                Assert.That(after.ThingId, Is.EqualTo(expectedSelected.DocumentId));
                Assert.That(after.Title, Is.EqualTo(expectedSelected.Title));
                Assert.That(after.FindVersion("v4")!.HasContent, Is.False);
            });
        }

        private static WotUpsertResourceRequest Request(
            string resourceId,
            byte[] content,
            string? versionId,
            bool setAsDefault = true,
            string? expectedDigestHex = null)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                VersionId = versionId,
                ExpectedVersionDigestHex = expectedDigestHex,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(content),
                SetAsDefault = setAsDefault
            };
        }

        private static async Task CreateCommittedVersionsAsync(
            WotRegistryService service,
            string resourceId,
            params string[] versionIds)
        {
            for (int i = 0; i < versionIds.Length; i++)
            {
                await service.UpsertResourceAsync(
                    Request(
                        resourceId,
                        TestMaterialization.Td($"urn:{resourceId}", versionIds[i]),
                        versionIds[i],
                        setAsDefault: false));
            }
        }

        private static ValueTask SetActiveVersionAsync(
            WotRegistryService service,
            string resourceId,
            string versionId)
        {
            return service.ApplyProjectionResultsAsync(
            [
                new WotResourceProjection(
                    WotRegistryGroups.ThingDescriptions,
                    resourceId,
                    WoTLoadStateEnum.Active,
                    versionId,
                    refreshGeneration: 1,
                    materializedNodeCount: 1,
                    rootNodeId: new NodeId(1u),
                    validation: null,
                    diagnostics: [],
                    lastRefreshTime: DateTime.UtcNow)
                {
                    VersionId = versionId
                }
            ]);
        }

        private static WotRegistrySnapshot ReplaceResource(
            WotRegistrySnapshot snapshot,
            WotResource resource)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            return snapshot.WithGroup(
                group.WithResources(
                    group.Resources.SetItem(resource.ResourceId, resource),
                    group.Epoch),
                snapshot.Generation);
        }

        private sealed class RecordingRegistryStore :
            IWotRegistryStore,
            IWotRegistryResourceStoreProvider
        {
            public RecordingRegistryStore(WotRegistrySnapshot? initial = null)
            {
                m_snapshot = initial ?? WotRegistrySnapshot.Empty;
            }

            public RecordingResourceStore BlobStore { get; } = new();

            IXRegistryResourceStore IWotRegistryResourceStoreProvider.ResourceStore => BlobStore;

            public void BlockNextCommit()
            {
                m_blockNextCommit = true;
            }

            public async Task WaitForBlockedCommitAsync()
            {
                Task completed = await Task.WhenAny(
                        m_commitEntered.Task,
                        Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                if (completed != m_commitEntered.Task)
                {
                    ReleaseBlockedCommit();
                    throw new TimeoutException("Timed out waiting for the blocked registry commit.");
                }
            }

            public void ReleaseBlockedCommit()
            {
                m_releaseCommit.TrySetResult(true);
            }

            public ValueTask<WotRegistrySnapshot> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotRegistrySnapshot>(m_snapshot);
            }

            public async ValueTask CommitAsync(
                WotRegistrySnapshot snapshot,
                CancellationToken cancellationToken = default)
            {
                if (m_blockNextCommit)
                {
                    m_blockNextCommit = false;
                    m_commitEntered.TrySetResult(true);
                    await m_releaseCommit.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                m_snapshot = snapshot;
            }

            public void SetSnapshot(WotRegistrySnapshot snapshot)
            {
                m_snapshot = snapshot;
            }

            private WotRegistrySnapshot m_snapshot;
            private bool m_blockNextCommit;
            private readonly TaskCompletionSource<bool> m_commitEntered = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_releaseCommit = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class RecordingResourceStore : IXRegistryResourceStore
        {
            public int WriteCount { get; private set; }

            public ValueTask<ByteString> ReadAsync(
                string resourceKey,
                long offset,
                int count,
                CancellationToken ct = default)
            {
                return m_inner.ReadAsync(resourceKey, offset, count, ct);
            }

            public ValueTask WriteAsync(
                string resourceKey,
                long offset,
                ByteString data,
                CancellationToken ct = default)
            {
                WriteCount++;
                return m_inner.WriteAsync(resourceKey, offset, data, ct);
            }

            public ValueTask<long> GetLengthAsync(
                string resourceKey,
                CancellationToken ct = default)
            {
                return m_inner.GetLengthAsync(resourceKey, ct);
            }

            public ValueTask<bool> DeleteAsync(
                string resourceKey,
                CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(resourceKey, ct);
            }

            public ValueTask SeedAsync(string resourceKey, ByteString content)
            {
                return m_inner.WriteAsync(resourceKey, 0, content);
            }

            public void ResetWriteCount()
            {
                WriteCount = 0;
            }

            private readonly InMemoryResourceStore m_inner = new();
        }
    }
}
