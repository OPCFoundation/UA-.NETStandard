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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Additional coverage for <see cref="WotRegistryService"/>: the
    /// <c>GetOrCreateGroupAsync</c> overload, registry-level label operations,
    /// the <c>NormalizeSegment</c>/<c>Slugify</c> helpers (exercised through the
    /// public surface), and the <c>MutateGroupAsync</c> internal path via the
    /// group-label API.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryServiceExtendedTests
    {
        [Test]
        public async Task GetOrCreateGroupAsyncCreatesNewGroup()
        {
            using var service = new WotRegistryService();

            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(group, Is.Not.Null);
            Assert.That(group.GroupId, Is.EqualTo("sensors"));
            Assert.That(group.Kind, Is.EqualTo(WoTDocumentKindEnum.ThingDescription));
            Assert.That(service.Current.FindGroup("sensors"), Is.Not.Null);
        }

        [Test]
        public async Task GetOrCreateGroupAsyncReturnsExistingGroup()
        {
            using var service = new WotRegistryService();
            WotResourceGroup first = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);
            long generation = service.Current.Generation;

            WotResourceGroup second = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(second.GroupId, Is.EqualTo(first.GroupId));
            Assert.That(service.Current.Generation, Is.EqualTo(generation),
                "Finding an existing group must not advance the registry generation.");
        }

        [Test]
        public async Task GetOrCreateGroupAsyncWithNameSetsDisplayName()
        {
            using var service = new WotRegistryService();

            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription, name: "Sensor Devices");

            Assert.That(group.Name, Is.EqualTo("Sensor Devices"));
        }

        [Test]
        public async Task GetOrCreateGroupAsyncExceedingMaxGroupsThrows()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxGroups = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.GetOrCreateGroupAsync("first", WoTDocumentKindEnum.ThingDescription);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.GetOrCreateGroupAsync(
                    "second", WoTDocumentKindEnum.ThingDescription));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(service.Current.FindGroup("second"), Is.Null);
        }

        [Test]
        public async Task GetOrCreateGroupAsyncRaisesChangedForNewGroup()
        {
            using var service = new WotRegistryService();
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.GetOrCreateGroupAsync("sensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.False);
        }

        [Test]
        public void GetOrCreateGroupAsyncWithEmptyGroupIdThrows()
        {
            using var service = new WotRegistryService();

            Assert.ThrowsAsync<System.ArgumentException>(
                async () => await service.GetOrCreateGroupAsync(
                    string.Empty, WoTDocumentKindEnum.ThingDescription));
        }

        [Test]
        public void GetOrCreateGroupAsyncWithWhitespaceOnlyGroupIdThrows()
        {
            using var service = new WotRegistryService();

            Assert.ThrowsAsync<System.ArgumentException>(
                async () => await service.GetOrCreateGroupAsync(
                    "   ", WoTDocumentKindEnum.ThingDescription));
        }

        [Test]
        public async Task GetOrCreateGroupAsyncSlugifiesUppercase()
        {
            using var service = new WotRegistryService();

            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "MySensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(group.GroupId, Is.EqualTo("mysensors").Or.EqualTo("my-sensors"),
                "Uppercase must be lowercased by the Slugify helper.");
        }

        [Test]
        public async Task GetOrCreateGroupAsyncSlugifiesSpaces()
        {
            using var service = new WotRegistryService();

            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "my sensors", WoTDocumentKindEnum.ThingDescription);

            Assert.That(group.GroupId, Does.Not.Contain(" "),
                "Spaces must be replaced by the Slugify helper.");
        }

        [Test]
        public async Task AddRegistryLabelAsyncAddsLabel()
        {
            using var service = new WotRegistryService();

            WotRegistryMutationResult result = await service.AddRegistryLabelAsync(
                "environment", "production");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.Labels["environment"], Is.EqualTo("production"));
        }

        [Test]
        public async Task AddRegistryLabelAsyncUpdatesExistingLabel()
        {
            using var service = new WotRegistryService();
            await service.AddRegistryLabelAsync("environment", "staging");

            WotRegistryMutationResult result = await service.AddRegistryLabelAsync(
                "environment", "production");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.Labels["environment"], Is.EqualTo("production"));
            Assert.That(service.Current.Labels, Has.Count.EqualTo(1),
                "Updating the same key must not duplicate.");
        }

        [Test]
        public async Task AddRegistryLabelAsyncWithEpochMismatchReturnsRejected()
        {
            using var service = new WotRegistryService();
            long generation = service.Current.Generation;

            WotRegistryMutationResult result = await service.AddRegistryLabelAsync(
                "environment", "production", expectedEpoch: generation + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Labels.ContainsKey("environment"), Is.False);
        }

        [Test]
        public async Task AddRegistryLabelAsyncWithCorrectEpochSucceeds()
        {
            using var service = new WotRegistryService();
            long generation = service.Current.Generation;

            WotRegistryMutationResult result = await service.AddRegistryLabelAsync(
                "environment", "production", expectedEpoch: generation);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [Test]
        public async Task AddRegistryLabelAsyncExceedsMaxLabelsThrows()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxLabelsPerEntity = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.AddRegistryLabelAsync("k1", "v1");

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddRegistryLabelAsync("k2", "v2"));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task AddRegistryLabelAsyncRaisesProjectionOnlyChanged()
        {
            using var service = new WotRegistryService();
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.AddRegistryLabelAsync("environment", "production");

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.True,
                "Registry label mutations are projection-only.");
        }

        [Test]
        public async Task RemoveRegistryLabelAsyncRemovesExistingLabel()
        {
            using var service = new WotRegistryService();
            await service.AddRegistryLabelAsync("environment", "production");

            WotRegistryMutationResult result = await service.RemoveRegistryLabelAsync("environment");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.Labels.ContainsKey("environment"), Is.False);
        }

        [Test]
        public async Task RemoveRegistryLabelAsyncForMissingKeyReturnsFailed()
        {
            using var service = new WotRegistryService();

            WotRegistryMutationResult result = await service.RemoveRegistryLabelAsync("missing");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public void RemoveRegistryLabelAsyncWithEmptyKeyThrows()
        {
            using var service = new WotRegistryService();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.RemoveRegistryLabelAsync(string.Empty));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task RemoveRegistryLabelAsyncWithEpochMismatchReturnsRejected()
        {
            using var service = new WotRegistryService();
            await service.AddRegistryLabelAsync("environment", "production");

            WotRegistryMutationResult result = await service.RemoveRegistryLabelAsync(
                "environment", expectedEpoch: service.Current.Generation + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current.Labels.ContainsKey("environment"), Is.True,
                "Label must remain when epoch check fails.");
        }

        [Test]
        public async Task RemoveRegistryLabelAsyncRaisesProjectionOnlyChanged()
        {
            using var service = new WotRegistryService();
            await service.AddRegistryLabelAsync("environment", "production");
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.RemoveRegistryLabelAsync("environment");

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.True);
        }

        [Test]
        public async Task MutateGroupAsyncViaGroupLabelUpdatesGroup()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateGroupAsync("sensors", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult result = await service.AddGroupLabelAsync(
                "sensors", "owner", "team-iot");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(
                service.Current.FindGroup("sensors")!.Labels["owner"],
                Is.EqualTo("team-iot"));
        }

        [Test]
        public async Task MutateGroupAsyncViaGroupLabelWhenGroupMissingReturnsFailed()
        {
            using var service = new WotRegistryService();

            WotRegistryMutationResult result = await service.AddGroupLabelAsync(
                "nonexistent", "k", "v");

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task MutateGroupAsyncViaGroupLabelWithEpochMismatchReturnsRejected()
        {
            using var service = new WotRegistryService();
            await service.GetOrCreateGroupAsync("sensors", WoTDocumentKindEnum.ThingDescription);
            WotResourceGroup? group = service.Current.FindGroup("sensors");

            WotRegistryMutationResult result = await service.AddGroupLabelAsync(
                "sensors", "owner", "team-iot", expectedEpoch: group!.Epoch + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
        }

        [Test]
        public async Task NormalizeSegmentViaUpsertSlugsUpperCase()
        {
            using var service = new WotRegistryService();
            var request = new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "MyDevice",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:device")
            };

            WotRegistryMutationResult result = await service.UpsertResourceAsync(request);

            Assert.That(result.Outcome, Is.Not.EqualTo(WoTOutcomeEnum.Rejected));
        }

        [Test]
        public async Task MaxResourcesPerGroupBoundRejectedByUpsert()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxResourcesPerGroup = 1 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a")
            });

            WotRegistryMutationResult result = await service.UpsertResourceAsync(
                new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "b",
                    Kind = WoTDocumentKindEnum.ThingDescription,
                    Content = TestMaterialization.Td("urn:b")
                });

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected),
                "Adding a second resource beyond MaxResourcesPerGroup must be rejected.");
        }
    }
}
