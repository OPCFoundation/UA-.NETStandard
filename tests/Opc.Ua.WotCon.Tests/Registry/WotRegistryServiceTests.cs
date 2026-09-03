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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the stable registry service: CRUD, versioning, default and
    /// enabled state, invalid-document retention, unchanged idempotency, epoch
    /// concurrency and persistence bounds.
    /// </summary>
    [TestFixture]
    public sealed class WotRegistryServiceTests
    {
        private static WotUpsertResourceRequest TdRequest(
            string resourceId, byte[] content, bool setDefault = true)
        {
            return new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(content),
                SetAsDefault = setDefault
            };
        }

        [Test]
        public async Task UpsertCreatesResourceAndBumpsGeneration()
        {
            using var service = new WotRegistryService();
            byte[] doc = TestMaterialization.Td("urn:a");

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                TdRequest("a", doc));

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(result.Generation, Is.GreaterThan(0));
            WotResource? resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a");
            Assert.That(resource, Is.Not.Null);
            Assert.That(resource!.Versions, Has.Length.EqualTo(1));
            Assert.That(resource.DefaultVersionId, Is.EqualTo(resource.Versions[0].VersionId));
            Assert.That(resource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
        }

        [Test]
        public async Task UpsertSameContentReturnsUnchanged()
        {
            using var service = new WotRegistryService();
            byte[] doc = TestMaterialization.Td("urn:a");
            await service.UpsertResourceAsync(TdRequest("a", doc));
            long generation = service.Current.Generation;

            WotRegistryMutationResult second = await service.UpsertResourceAsync(
                TdRequest("a", doc));

            Assert.That(second.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            Assert.That(service.Current.Generation, Is.EqualTo(generation),
                "An unchanged upload must not advance the registry generation.");
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!.Versions,
                Has.Length.EqualTo(1));
        }

        [Test]
        public async Task UpsertNewContentAddsVersion()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v1")));
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v2")));

            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;
            Assert.That(resource.Versions, Has.Length.EqualTo(2));
            Assert.That(resource.DefaultVersionId, Is.EqualTo(resource.Versions[1].VersionId));
        }

        [TestCase("v7", "v7")]
        [TestCase("", "0000000000000000001")]
        public async Task VersionAwareCreateCommitsIdentityBeforeContent(
            string requestedVersionId,
            string expectedVersionId)
        {
            using var service = new WotRegistryService();

            (WotResource resource, WotResourceVersion version, bool created) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "a",
                    requestedVersionId,
                    WoTDocumentKindEnum.ThingDescription);

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(version.VersionId, Is.EqualTo(expectedVersionId));
                Assert.That(version.HasContent, Is.False);
                Assert.That(resource.DefaultVersionId, Is.EqualTo(expectedVersionId));
                Assert.That(resource.MetaEpoch, Is.EqualTo(1));
                Assert.That(resource.MetaCreatedAt, Is.EqualTo(resource.MetaModifiedAt));
            });

            DateTime createdAt = version.CreatedAt;
            WotRegistryMutationResult close = await service.UpsertResourceAsync(
                new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "a",
                    VersionId = expectedVersionId,
                    ExpectedVersionDigestHex = string.Empty,
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:a"))
                });
            WotResource committed = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            WotResourceVersion committedVersion = committed.FindVersion(expectedVersionId)!;

            Assert.Multiple(() =>
            {
                Assert.That(close.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(committed.Versions, Has.Length.EqualTo(1));
                Assert.That(committedVersion.HasContent, Is.True);
                Assert.That(committedVersion.Epoch, Is.EqualTo(2));
                Assert.That(committedVersion.CreatedAt, Is.EqualTo(createdAt));
                Assert.That(committedVersion.ModifiedAt, Is.GreaterThanOrEqualTo(createdAt));
                Assert.That(committed.MetaEpoch, Is.EqualTo(1),
                    "Updating Version content must not mutate Resource Meta.");
            });
        }

        [Test]
        public async Task GetOrCreateVersionRetryFillsTheSameContentlessVersion()
        {
            using var service = new WotRegistryService();
            (WotResource _, WotResourceVersion first, bool firstCreated) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "retry",
                    string.Empty,
                    WoTDocumentKindEnum.ThingDescription);
            (WotResource _, WotResourceVersion second, bool secondCreated) =
                await service.GetOrCreateVersionAsync(
                    WotRegistryGroups.ThingDescriptions,
                    "retry",
                    string.Empty,
                    WoTDocumentKindEnum.ThingDescription);

            WotUpsertResourceRequest upload = TdRequest(
                "retry",
                TestMaterialization.Td("urn:retry"));
            upload.VersionId = second.VersionId;
            upload.ExpectedVersionDigestHex = string.Empty;
            WotRegistryMutationResult result = await service.UpsertResourceAsync(upload);
            WotResource stored = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "retry")!;

            Assert.Multiple(() =>
            {
                Assert.That(firstCreated, Is.True);
                Assert.That(secondCreated, Is.False);
                Assert.That(second.VersionId, Is.EqualTo(first.VersionId));
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(stored.Versions, Has.Length.EqualTo(1));
                Assert.That(stored.DefaultVersionId, Is.EqualTo(first.VersionId));
                Assert.That(stored.DefaultVersion!.HasContent, Is.True);
            });
        }

        [Test]
        public async Task AdditionalVersionCreationAndReplacementPreserveDefaultVersion()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v1",
                WoTDocumentKindEnum.ThingDescription);
            WotUpsertResourceRequest first = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v1"),
                setDefault: false);
            first.VersionId = "v1";
            first.ExpectedVersionDigestHex = string.Empty;
            await service.UpsertResourceAsync(first);

            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                WoTDocumentKindEnum.ThingDescription);

            WotResource afterCreate = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            WotUpsertResourceRequest second = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v2"),
                setDefault: false);
            second.VersionId = "v2";
            second.ExpectedVersionDigestHex = string.Empty;
            await service.UpsertResourceAsync(second);
            WotResource afterReplace = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(afterCreate.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(afterCreate.DesiredVersionId, Is.EqualTo("v1"));
                Assert.That(afterReplace.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(afterReplace.DesiredVersionId, Is.EqualTo("v1"));
                Assert.That(afterReplace.Versions, Has.Length.EqualTo(2));
                Assert.That(afterReplace.FindVersion("v2")!.HasContent, Is.True);
            });
        }

        [Test]
        public async Task StructuralVersionCreationDoesNotRequestMaterialization()
        {
            using var service = new WotRegistryService();
            WotRegistryChangedEventArgs? changed = null;
            service.Changed += (_, e) => changed = e;

            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "placeholder",
                "v1",
                WoTDocumentKindEnum.ThingDescription);

            Assert.That(changed, Is.Not.Null);
            Assert.That(changed!.ProjectionOnly, Is.True);
            Assert.That(changed.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "placeholder")!.DefaultVersion!.HasContent, Is.False);
        }

        [Test]
        public void VersionCapabilityInterfaceIsPublicAndOptional()
        {
            Assert.That(typeof(IWotVersionedRegistryService).IsPublic, Is.True);
            Assert.That(
                typeof(IWotVersionedRegistryService).IsAssignableFrom(typeof(IWotRegistryService)),
                Is.False);
        }

        [Test]
        public async Task ValidationOutcomeIsStoredOnTheExactVersion()
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest v1 = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v1"),
                setDefault: false);
            v1.VersionId = "v1";
            await service.UpsertResourceAsync(v1);
            WotUpsertResourceRequest v2 = TdRequest(
                "a",
                TestMaterialization.InvalidJson(),
                setDefault: false);
            v2.VersionId = "v2";
            await service.UpsertResourceAsync(v2);

            WoTValidationOutcomeDataType outcome = await service.ValidateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2");
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(outcome.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(resource.FindVersion("v1")!.Validation, Is.Null);
                Assert.That(
                    resource.FindVersion("v2")!.Validation!.FormatOutcome,
                    Is.EqualTo(WoTOutcomeEnum.Failed));
                Assert.That(resource.Validation, Is.Null);
            });
        }

        [Test]
        public async Task VersionLabelsAndResourceMetaHaveIndependentEpochs()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v1",
                WoTDocumentKindEnum.ThingDescription);
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                WoTDocumentKindEnum.ThingDescription);
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            DateTime metaCreatedAt = before.MetaCreatedAt;
            long metaEpoch = before.MetaEpoch;

            WotRegistryMutationResult versionResult = await service.AddVersionLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v1",
                "version",
                "one",
                expectedEpoch: 1);
            WotResource afterVersion = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            WotRegistryMutationResult metaResult = await service.AddResourceLabelAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "owner",
                "plant-1",
                expectedEpoch: metaEpoch);
            WotResource afterMeta = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(versionResult.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(afterVersion.FindVersion("v1")!.Epoch, Is.EqualTo(2));
                Assert.That(afterVersion.FindVersion("v2")!.Epoch, Is.EqualTo(1));
                Assert.That(afterVersion.MetaEpoch, Is.EqualTo(metaEpoch));
                Assert.That(metaResult.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(afterMeta.MetaEpoch, Is.EqualTo(metaEpoch + 1));
                Assert.That(afterMeta.MetaCreatedAt, Is.EqualTo(metaCreatedAt));
                Assert.That(afterMeta.MetaLabels["owner"], Is.EqualTo("plant-1"));
                Assert.That(afterMeta.FindVersion("v1")!.Labels["version"], Is.EqualTo("one"));
                Assert.That(afterMeta.FindVersion("v1")!.Epoch, Is.EqualTo(2));
                Assert.That(afterMeta.FindVersion("v2")!.Epoch, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DefaultSwitchChangesOnlyResourceMeta()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v1",
                WoTDocumentKindEnum.ThingDescription);
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                WoTDocumentKindEnum.ThingDescription);
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            long v1Epoch = before.FindVersion("v1")!.Epoch;
            long v2Epoch = before.FindVersion("v2")!.Epoch;

            WotRegistryMutationResult result = await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                before.MetaEpoch);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.MetaEpoch, Is.EqualTo(before.MetaEpoch + 1));
                Assert.That(after.FindVersion("v1")!.Epoch, Is.EqualTo(v1Epoch));
                Assert.That(after.FindVersion("v2")!.Epoch, Is.EqualTo(v2Epoch));
            });
        }

        [Test]
        public async Task StaleVersionWriterCannotReplaceNewerCommittedContent()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v1",
                WoTDocumentKindEnum.ThingDescription);
            WotUpsertResourceRequest first = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v1"));
            first.VersionId = "v1";
            first.ExpectedVersionDigestHex = string.Empty;
            await service.UpsertResourceAsync(first);
            WotResourceVersion baseline = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!.FindVersion("v1")!;

            WotUpsertResourceRequest newer = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v2"));
            newer.VersionId = "v1";
            newer.ExpectedVersionDigestHex = baseline.DigestHex;
            await service.UpsertResourceAsync(newer);
            WotResourceVersion committed = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!.FindVersion("v1")!;

            WotUpsertResourceRequest stale = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "stale"));
            stale.VersionId = "v1";
            stale.ExpectedVersionDigestHex = baseline.DigestHex;
            WotRegistryMutationResult result = await service.UpsertResourceAsync(stale);
            WotResourceVersion after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!.FindVersion("v1")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(after.DigestHex, Is.EqualTo(committed.DigestHex));
                Assert.That(after.Epoch, Is.EqualTo(committed.Epoch));
            });
        }

        [Test]
        public async Task InvalidDocumentIsStoredWithFailureState()
        {
            using var service = new WotRegistryService();

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                TdRequest("bad", TestMaterialization.InvalidJson()));

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "bad")!;
            Assert.That(resource.LoadState, Is.EqualTo(WoTLoadStateEnum.Failed));
            Assert.That(resource.Validation, Is.Not.Null);
            Assert.That(resource.Validation!.FormatOutcome, Is.EqualTo(WoTOutcomeEnum.Failed));
            Assert.That(resource.Versions, Has.Length.EqualTo(1),
                "The invalid document must still be stored.");
        }

        [Test]
        public async Task UpsertTooLargeIsRejectedAndNotStored()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxDocumentBytes = 32 };
            using var service = new WotRegistryService(bounds: bounds);
            byte[] big = new byte[64];

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                TdRequest("big", big));

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "big"), Is.Null);
        }

        [Test]
        public async Task MaxGroupsImplicitCreateViaGetOrCreateResourceIsRejected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            // Fill the single group slot via the well-known Thing Description group.
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));
            Assert.That(service.Current.Groups, Has.Count.EqualTo(1));

            // Implicitly creating a placeholder in a new group would exceed
            // MaxGroups and must be rejected identically to the explicit
            // group-create APIs (BadTooManyOperations).
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.GetOrCreateResourceAsync("sensors", "r", WoTDocumentKindEnum.ThingDescription));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(service.Current.FindGroup("sensors"), Is.Null,
                "The over-limit implicit group must not be created.");
        }

        [Test]
        public async Task MaxGroupsImplicitCreateViaTryCreateResourceIsRejected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await service.TryCreateResourceAsync("sensors", "r", WoTDocumentKindEnum.ThingDescription));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(service.Current.FindGroup("sensors"), Is.Null);
        }

        [Test]
        public async Task MaxGroupsImplicitCreateViaUpsertIsRejected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            // An upsert whose target group does not yet exist would implicitly
            // create a second group; the bound must reject it.
            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                new WotUpsertResourceRequest
                {
                    GroupId = "sensors",
                    ResourceId = "r",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = ByteString.From(TestMaterialization.Td("urn:r"))
                });

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.FindGroup("sensors"), Is.Null);
        }

        [Test]
        public async Task MaxGroupsAllowsAnotherResourceInExistingGroup()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            // Creating another resource in the SAME existing group creates no new
            // group, so it must not be blocked by MaxGroups.
            (WotResource _, bool created) = await service.GetOrCreateResourceAsync(
                WotRegistryGroups.ThingDescriptions, "b", WoTDocumentKindEnum.ThingDescription);

            Assert.That(created, Is.True);
            Assert.That(service.Current.Groups, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task VersionRetentionTrimsOldestBeyondBound()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 3 };
            using var service = new WotRegistryService(bounds: bounds);
            for (int i = 0; i < 5; i++)
            {
                await service.UpsertResourceAsync(
                    TdRequest("a", TestMaterialization.Td("urn:a", "v" + i)));
            }

            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;
            Assert.That(resource.Versions, Has.Length.EqualTo(3),
                "Version retention must trim the oldest versions.");
        }

        [Test]
        public async Task VersionRetentionNeverTrimsStickyDefault()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
            using var service = new WotRegistryService(bounds: bounds);
            foreach (string versionId in s_retentionInputVersionIds)
            {
                WotUpsertResourceRequest request = TdRequest(
                    "a",
                    TestMaterialization.Td("urn:a", versionId),
                    setDefault: false);
                request.VersionId = versionId;
                await service.UpsertResourceAsync(request);
            }

            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            Assert.Multiple(() =>
            {
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(
                    resource.Versions.Select(version => version.VersionId),
                    Is.EqualTo(s_expectedRetainedVersionIds));
            });
        }

        [Test]
        public async Task VersionRetentionProtectsActiveDefaultAndIncomingVersions()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 3 };
            using var service = new WotRegistryService(bounds: bounds);
            foreach (string versionId in new[] { "v1", "v2", "v3" })
            {
                WotUpsertResourceRequest request = TdRequest(
                    "a",
                    TestMaterialization.Td("urn:a", versionId),
                    setDefault: false);
                request.VersionId = versionId;
                await service.UpsertResourceAsync(request);
            }
            await SetActiveVersionAsync(service, "a", "v1");
            WotResource beforeSwitch = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;
            await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                beforeSwitch.MetaEpoch);

            WotUpsertResourceRequest incoming = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v4"),
                setDefault: false);
            incoming.VersionId = "v4";
            WotRegistryMutationResult result = await service.UpsertResourceAsync(incoming);
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(
                    resource.Versions.Select(version => version.VersionId),
                    Is.EqualTo(new[] { "v1", "v2", "v4" }));
            });
        }

        [Test]
        public async Task VersionRetentionProtectsIncomingNonDefaultVersionAtLimit()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
            using var service = new WotRegistryService(bounds: bounds);
            foreach (string versionId in new[] { "v1", "v2" })
            {
                WotUpsertResourceRequest request = TdRequest(
                    "a",
                    TestMaterialization.Td("urn:a", versionId),
                    setDefault: false);
                request.VersionId = versionId;
                await service.UpsertResourceAsync(request);
            }

            WotUpsertResourceRequest incoming = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v3"),
                setDefault: false);
            incoming.VersionId = "v3";
            WotRegistryMutationResult result = await service.UpsertResourceAsync(incoming);
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(
                    resource.Versions.Select(version => version.VersionId),
                    Is.EqualTo(new[] { "v1", "v3" }));
            });
        }

        [Test]
        public async Task VersionRetentionRejectsWhenLimitCannotKeepProtectedVersions()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            WotUpsertResourceRequest first = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v1"),
                setDefault: false);
            first.VersionId = "v1";
            await service.UpsertResourceAsync(first);
            await SetActiveVersionAsync(service, "a", "v1");
            long generation = service.Current.Generation;

            WotUpsertResourceRequest incoming = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "v2"),
                setDefault: false);
            incoming.VersionId = "v2";
            WotRegistryMutationResult result = await service.UpsertResourceAsync(incoming);
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.Message, Does.Contain("retention").IgnoreCase);
                Assert.That(service.Current.Generation, Is.EqualTo(generation));
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v1"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v1"));
                Assert.That(
                    resource.Versions.Select(version => version.VersionId),
                    Is.EqualTo(new[] { "v1" }));
            });
        }

        [Test]
        public async Task DefaultSwitchUsesSelectedVersionDocumentMetadata()
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest v1 = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "first"),
                setDefault: false);
            v1.VersionId = "v1";
            await service.UpsertResourceAsync(v1);
            WotUpsertResourceRequest v2 = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "second"),
                setDefault: false);
            v2.VersionId = "v2";
            await service.UpsertResourceAsync(v2);
            WotResource before = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            WotRegistryMutationResult result = await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions,
                "a",
                "v2",
                before.MetaEpoch);
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.ThingId, Is.EqualTo("urn:a"));
                Assert.That(after.Title, Is.EqualTo("urn:a-second"));
                Assert.That(after.FindVersion("v1")!.DocumentId, Is.EqualTo("urn:a"));
                Assert.That(after.FindVersion("v1")!.Title, Is.EqualTo("urn:a-first"));
                Assert.That(after.FindVersion("v2")!.DocumentId, Is.EqualTo("urn:a"));
                Assert.That(after.FindVersion("v2")!.Title, Is.EqualTo("urn:a-second"));
            });
        }

        [Test]
        public async Task NewVersionWithIncompatibleDocumentIdentityIsRejected()
        {
            using var service = new WotRegistryService();
            WotUpsertResourceRequest v1 = TdRequest(
                "a",
                TestMaterialization.Td("urn:a", "first"),
                setDefault: false);
            v1.VersionId = "v1";
            await service.UpsertResourceAsync(v1);
            long generation = service.Current.Generation;

            WotUpsertResourceRequest v2 = TdRequest(
                "a",
                TestMaterialization.Td("urn:other", "second"),
                setDefault: false);
            v2.VersionId = "v2";
            WotRegistryMutationResult result = await service.UpsertResourceAsync(v2);
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions,
                "a")!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(result.Message, Does.Contain("identity").IgnoreCase);
                Assert.That(service.Current.Generation, Is.EqualTo(generation));
                Assert.That(resource.Versions, Has.Length.EqualTo(1));
                Assert.That(resource.ThingId, Is.EqualTo("urn:a"));
                Assert.That(resource.Title, Is.EqualTo("urn:a-first"));
            });
        }

        [Test]
        public async Task SetDefaultVersionSwitchesActiveDefault()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v1")));
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v2")));
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;
            string firstVersion = resource.Versions[0].VersionId;

            WotRegistryMutationResult result = await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions, "a", firstVersion, resource.Epoch);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!
                    .DefaultVersionId,
                Is.EqualTo(firstVersion));
        }

        [Test]
        public async Task SetDefaultVersionWrongEpochIsRejected()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));
            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;

            WotRegistryMutationResult result = await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions, "a",
                resource.Versions[0].VersionId, expectedEpoch: resource.Epoch + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
        }

        [Test]
        public async Task SetEnabledTogglesEnabledState()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            await service.SetEnabledAsync(WotRegistryGroups.ThingDescriptions, "a", enabled: false);

            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!.Enabled,
                Is.False);
        }

        [Test]
        public async Task DeleteRemovesResource()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            WotRegistryMutationResult result = await service.DeleteResourceAsync(
                WotRegistryGroups.ThingDescriptions, "a");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a"), Is.Null);
        }

        [Test]
        public async Task ChangedRaisedForContentMutation()
        {
            using var service = new WotRegistryService();
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.False);
            Assert.That(captured.ChangedResourceXids, Has.Count.EqualTo(1));
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

        private static readonly string[] s_retentionInputVersionIds = ["v1", "v2", "v3"];
        private static readonly string[] s_expectedRetainedVersionIds = ["v1", "v3"];
    }
}
