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

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the xRegistry label (attribute) service API on the registry,
    /// group and resource entities: add/update/remove, epoch optimistic
    /// concurrency, key validation (reserved names, invalid/control/BIDI/path
    /// characters, length), the per-entity label count bound, deterministic
    /// ordinal ordering, and file-store persistence across a reload.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryLabelsServiceTests
    {
        [Test]
        public async Task AddResourceLabel_AddsThenUpdatesValue()
        {
            using var service = new WotRegistryService();
            await service.TryCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult added = await service.AddResourceLabelAsync(
                "sensors", "a", "site", "seattle");
            Assert.That(added.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotResource? resource = service.Current.FindResource("sensors", "a");
            Assert.That(resource!.Labels["site"], Is.EqualTo("seattle"));

            WotRegistryMutationResult updated = await service.AddResourceLabelAsync(
                "sensors", "a", "site", "portland");
            Assert.That(updated.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            resource = service.Current.FindResource("sensors", "a");
            Assert.That(resource!.Labels.Count, Is.EqualTo(1),
                "Re-adding the same key must update in place, not duplicate.");
            Assert.That(resource.Labels["site"], Is.EqualTo("portland"));
        }

        [Test]
        public async Task RemoveResourceLabel_RemovesKey()
        {
            using var service = new WotRegistryService();
            await service.TryCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);
            await service.AddResourceLabelAsync("sensors", "a", "site", "seattle");

            WotRegistryMutationResult removed = await service.RemoveResourceLabelAsync(
                "sensors", "a", "site");

            Assert.That(removed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            WotResource? resource = service.Current.FindResource("sensors", "a");
            Assert.That(resource!.Labels.ContainsKey("site"), Is.False);
        }

        [Test]
        public async Task RemoveResourceLabel_UnknownKey_Fails()
        {
            using var service = new WotRegistryService();
            await service.TryCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult removed = await service.RemoveResourceLabelAsync(
                "sensors", "a", "missing");

            Assert.That(removed.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task AddResourceLabel_EpochMismatch_Rejected()
        {
            using var service = new WotRegistryService();
            (WotResource resource, _) = await service.GetOrCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult result = await service.AddResourceLabelAsync(
                "sensors", "a", "site", "seattle", expectedEpoch: resource.Epoch + 999);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(
                service.Current.FindResource("sensors", "a")!.Labels.ContainsKey("site"),
                Is.False);
        }

        [Test]
        public async Task AddResourceLabel_CorrectEpoch_Succeeds()
        {
            using var service = new WotRegistryService();
            (WotResource resource, _) = await service.GetOrCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);

            WotRegistryMutationResult result = await service.AddResourceLabelAsync(
                "sensors", "a", "site", "seattle", expectedEpoch: resource.Epoch);

            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [Test]
        public void AddResourceLabel_MissingKey_Throws()
        {
            using var service = new WotRegistryService();
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddResourceLabelAsync("sensors", "a", string.Empty, "x"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [TestCase("Add\u0007Attribute")] // control character
        [TestCase("a/b")]                // path separator
        [TestCase("a\u202Eb")]           // BIDI override
        [TestCase("AddAttribute")]       // reserved container member
        [TestCase("RemoveAttribute")]    // reserved container member
        public void AddResourceLabel_InvalidOrReservedKey_Throws(string key)
        {
            using var service = new WotRegistryService();
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddResourceLabelAsync("sensors", "a", key, "x"));
        }

        [Test]
        public void AddResourceLabel_KeyTooLong_Throws()
        {
            using var service = new WotRegistryService();
            string longKey = new string('k', service.Bounds.MaxLabelKeyLength + 1);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddResourceLabelAsync("sensors", "a", longKey, "x"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddResourceLabel_ValueTooLong_Throws()
        {
            using var service = new WotRegistryService();
            string longValue = new string('v', service.Bounds.MaxLabelValueLength + 1);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddResourceLabelAsync("sensors", "a", "k", longValue));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AddResourceLabel_ExceedsMaxLabelsPerEntity_Throws()
        {
            var bounds = new WotRegistryPersistenceBounds { MaxLabelsPerEntity = 2 };
            using var service = new WotRegistryService(bounds: bounds);
            await service.TryCreateResourceAsync(
                "sensors", "a", WoTDocumentKindEnum.ThingDescription);
            await service.AddResourceLabelAsync("sensors", "a", "k1", "v1");
            await service.AddResourceLabelAsync("sensors", "a", "k2", "v2");

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await service.AddResourceLabelAsync("sensors", "a", "k3", "v3"));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));

            // Updating an existing key must still be allowed at the limit.
            WotRegistryMutationResult update = await service.AddResourceLabelAsync(
                "sensors", "a", "k1", "v1-updated");
            Assert.That(update.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
        }

        [Test]
        public async Task GroupLabels_AddUpdateRemove_EpochAndOrdering()
        {
            using var service = new WotRegistryService();
            WotResourceGroup group = await service.GetOrCreateGroupAsync(
                "sensors", WoTDocumentKindEnum.ThingDescription);

            await service.AddGroupLabelAsync("sensors", "zebra", "1");
            await service.AddGroupLabelAsync("sensors", "alpha", "2");
            WotResourceGroup? updatedGroup = service.Current.FindGroup("sensors");
            Assert.That(updatedGroup!.Labels.Keys, Is.EqualTo(new[] { "alpha", "zebra" }),
                "Labels must enumerate in deterministic ordinal key order.");

            WotRegistryMutationResult mismatched = await service.RemoveGroupLabelAsync(
                "sensors", "alpha", expectedEpoch: group.Epoch + 1);
            Assert.That(mismatched.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));

            WotRegistryMutationResult removed = await service.RemoveGroupLabelAsync(
                "sensors", "alpha");
            Assert.That(removed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.FindGroup("sensors")!.Labels.Keys, Is.EqualTo(new[] { "zebra" }));
        }

        [Test]
        public void AddGroupLabel_UnknownGroup_Fails()
        {
            using var service = new WotRegistryService();
            WotRegistryMutationResult result = service.AddGroupLabelAsync(
                "missing", "k", "v").AsTask().GetAwaiter().GetResult();
            Assert.That(result.Outcome, Is.EqualTo(WoTOutcomeEnum.Failed));
        }

        [Test]
        public async Task RegistryLabels_AddUpdateRemove_UsesSnapshotGenerationAsEpoch()
        {
            using var service = new WotRegistryService();
            long generationBefore = service.Current.Generation;

            WotRegistryMutationResult added = await service.AddRegistryLabelAsync(
                "environment", "production", expectedEpoch: generationBefore);
            Assert.That(added.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.Labels["environment"], Is.EqualTo("production"));

            WotRegistryMutationResult mismatched = await service.AddRegistryLabelAsync(
                "environment", "staging", expectedEpoch: generationBefore);
            Assert.That(mismatched.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected),
                "The registry epoch is the snapshot generation, which already advanced.");

            WotRegistryMutationResult removed = await service.RemoveRegistryLabelAsync("environment");
            Assert.That(removed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(service.Current.Labels.ContainsKey("environment"), Is.False);
        }

        [Test]
        public async Task LabelMutations_AreProjectionOnly_AndDoNotChangeResourceContent()
        {
            using var service = new WotRegistryService();
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "a",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = TestMaterialization.Td("urn:a")
            });
            WotRegistryChangedEventArgs? captured = null;
            service.Changed += (_, e) => captured = e;

            await service.AddResourceLabelAsync(
                WotRegistryGroups.ThingDescriptions, "a", "site", "seattle");

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ProjectionOnly, Is.True,
                "Label-only mutations must not re-trigger materialization.");
        }

        [Test]
        public async Task FileStore_PersistsLabels_AcrossReload()
        {
            string root = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "wot-labels-store-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new FileWotRegistryStore(root);
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
                    await service.AddResourceLabelAsync(
                        WotRegistryGroups.ThingDescriptions, "a", "site", "seattle");
                    await service.AddGroupLabelAsync(
                        WotRegistryGroups.ThingDescriptions, "owner", "team-iot");
                    await service.AddRegistryLabelAsync("environment", "production");
                }

                var reloadStore = new FileWotRegistryStore(root);
                using var reloaded = new WotRegistryService(reloadStore);
                await reloaded.InitializeAsync();

                Assert.That(reloaded.Current.Labels["environment"], Is.EqualTo("production"));
                Assert.That(
                    reloaded.Current.FindGroup(WotRegistryGroups.ThingDescriptions)!
                        .Labels["owner"],
                    Is.EqualTo("team-iot"));
                Assert.That(
                    reloaded.Current.FindResource(WotRegistryGroups.ThingDescriptions, "a")!
                        .Labels["site"],
                    Is.EqualTo("seattle"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
