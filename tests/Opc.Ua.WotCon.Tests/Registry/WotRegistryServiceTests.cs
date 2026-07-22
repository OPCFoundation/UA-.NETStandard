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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
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
            => new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = resourceId,
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = content,
                SetAsDefault = setDefault
            };

        [Test]
        public async Task Upsert_CreatesResourceAndBumpsGeneration()
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
            Assert.That(resource!.Versions.Length, Is.EqualTo(1));
            Assert.That(resource.DefaultVersionId, Is.EqualTo(resource.Versions[0].VersionId));
            Assert.That(resource.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
        }

        [Test]
        public async Task Upsert_SameContent_ReturnsUnchanged()
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
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!
                    .Versions.Length,
                Is.EqualTo(1));
        }

        [Test]
        public async Task Upsert_NewContent_AddsVersion()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v1")));
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a", "v2")));

            WotResource resource = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "a")!;
            Assert.That(resource.Versions.Length, Is.EqualTo(2));
            Assert.That(resource.DefaultVersionId, Is.EqualTo(resource.Versions[1].VersionId));
        }

        [Test]
        public async Task InvalidDocument_IsStoredWithFailureState()
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
            Assert.That(resource.Versions.Length, Is.EqualTo(1),
                "The invalid document must still be stored.");
        }

        [Test]
        public async Task Upsert_TooLarge_IsRejectedAndNotStored()
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
        public async Task MaxGroups_ImplicitCreateViaGetOrCreateResource_IsRejected()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            // Fill the single group slot via the well-known Thing Description group.
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));
            Assert.That(service.Current.Groups.Count, Is.EqualTo(1));

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
        public async Task MaxGroups_ImplicitCreateViaTryCreateResource_IsRejected()
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
        public async Task MaxGroups_ImplicitCreateViaUpsert_IsRejected()
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
                    Content = TestMaterialization.Td("urn:r")
                });

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.FindGroup("sensors"), Is.Null);
        }

        [Test]
        public async Task MaxGroups_AllowsAnotherResourceInExistingGroup()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            // Creating another resource in the SAME existing group creates no new
            // group, so it must not be blocked by MaxGroups.
            (WotResource _, bool created) = await service.GetOrCreateResourceAsync(
                WotRegistryGroups.ThingDescriptions, "b", WoTDocumentKindEnum.ThingDescription);

            Assert.That(created, Is.True);
            Assert.That(service.Current.Groups.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task VersionRetention_TrimsOldestBeyondBound()
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
            Assert.That(resource.Versions.Length, Is.EqualTo(3),
                "Version retention must trim the oldest versions.");
        }

        [Test]
        public async Task SetDefaultVersion_SwitchesActiveDefault()
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
        public async Task SetDefaultVersion_WrongEpoch_IsRejected()
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
        public async Task SetEnabled_TogglesEnabledState()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            await service.SetEnabledAsync(WotRegistryGroups.ThingDescriptions, "a", enabled: false);

            Assert.That(
                service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!.Enabled,
                Is.False);
        }

        [Test]
        public async Task Delete_RemovesResource()
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
        public async Task Changed_RaisedForContentMutation()
        {
            using var service = new WotRegistryService();
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.UpsertResourceAsync(TdRequest("a", TestMaterialization.Td("urn:a")));

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.False);
            Assert.That(captured.ChangedResourceXids, Has.Count.EqualTo(1));
        }
    }
}
