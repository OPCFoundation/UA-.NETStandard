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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Registry
{
    public sealed partial class WotRegistryFollowUpTests
    {
        [Test]
        public async Task FileStoreRetainsLeaseOnlyMetadataThenPersistsEligibleEvictionAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "ua-rl", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var bounds = new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 };
                var store = new FileWotRegistryStore(root);
                using (var service = new WotRegistryService(store, bounds))
                {
                    await service.InitializeAsync().ConfigureAwait(false);
                    await CreateCommittedVersionsAsync(service, "file-lease", "v1", "v2").ConfigureAwait(false);
                    await service.SetDefaultVersionAsync(
                        WotRegistryGroups.ThingDescriptions, "file-lease", "v2").ConfigureAwait(false);
                    await SetActiveVersionAsync(service, "file-lease", "v2").ConfigureAwait(false);
                    WotResource before = service.Current.FindResource(
                        WotRegistryGroups.ThingDescriptions, "file-lease")!;
                    using IWotRegistryVersionLease lease = await service.AcquireVersionLeaseAsync(
                        before.GroupId, before.ResourceId, before.FindVersion("v1")!).ConfigureAwait(false);
                    await Assert.ThatAsync(async () =>
                    {
                        _ = await service.TryCreateVersionAsync(
                            before.GroupId, before.ResourceId, "v3", before.Kind).ConfigureAwait(false);
                    }, Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                    WotRegistrySnapshot durable = await store.LoadAsync().ConfigureAwait(false);
                    WotResource retained = durable.FindResource(before.GroupId, before.ResourceId)!;
                    Assert.That(retained.MetaEpoch, Is.EqualTo(before.MetaEpoch));
                    Assert.That(retained.Versions, Has.Length.EqualTo(2));
                    Assert.That(retained.FindVersion("v1")!.Digest, Is.EqualTo(lease.Version.Digest));
                    Assert.That(await service.ReadContentAsync(lease.Version).ConfigureAwait(false),
                        Is.EqualTo(ByteString.From(TestMaterialization.Td("urn:file-lease", "v1"))));
                    lease.Dispose();
                    WotRegistryMutationResult committed = await service.UpsertResourceAsync(
                        Request("file-lease", TestMaterialization.Td("urn:file-lease", "v3"), "v3", false))
                        .ConfigureAwait(false);
                    Assert.That(committed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                }
                using var restarted = new WotRegistryService(new FileWotRegistryStore(root), bounds);
                await restarted.InitializeAsync().ConfigureAwait(false);
                WotResource after = restarted.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "file-lease")!;
                Assert.That(after.Versions, Has.Length.EqualTo(2));
                Assert.That(after.FindVersion("v1"), Is.Null);
                Assert.That(after.FindVersion("v2"), Is.Not.Null);
                Assert.That(after.FindVersion("v3"), Is.Not.Null);
                Assert.That(after.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.DesiredVersionId, Is.EqualTo("v2"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public async Task LeaseAcquisitionWaitsForAtomicRetentionPublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var store = new RecordingRegistryStore();
            using var service = new WotRegistryService(
                store, new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
            await CreateCommittedVersionsAsync(service, "atomic-lease", "v1", "v2").ConfigureAwait(false);
            await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions, "atomic-lease", "v2").ConfigureAwait(false);
            await SetActiveVersionAsync(service, "atomic-lease", "v2").ConfigureAwait(false);
            WotResourceVersion first = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "atomic-lease")!.FindVersion("v1")!;
            store.BlockNextCommit();
            Task<WotRegistryMutationResult> mutation = service.UpsertResourceAsync(
                Request("atomic-lease", TestMaterialization.Td("urn:atomic-lease", "v3"), "v3", false),
                timeout.Token).AsTask();
            Task<IWotRegistryVersionLease>? acquiring = null;
            try
            {
                await store.WaitForBlockedCommitAsync().ConfigureAwait(false);
                acquiring = service.AcquireVersionLeaseAsync(
                    WotRegistryGroups.ThingDescriptions, "atomic-lease", first, timeout.Token).AsTask();
                Assert.That(acquiring.IsCompleted, Is.False,
                    "The owner has chosen retention victims but has not published its commit.");
                Assert.That(service.Current.FindResource(
                    WotRegistryGroups.ThingDescriptions, "atomic-lease")!.FindVersion("v1"), Is.Not.Null);
            }
            finally
            {
                store.ReleaseBlockedCommit();
            }
            Assert.That((await mutation.ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            await Assert.ThatAsync(async () =>
            {
                using IWotRegistryVersionLease lease = await acquiring!.ConfigureAwait(false);
            }, Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
            Assert.That(service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "atomic-lease")!.FindVersion("v1"), Is.Null);
        }

        [Test]
        public async Task OldIncarnationLeaseCannotProtectOrReleaseItsReplacementAsync()
        {
            using var service = new WotRegistryService(
                bounds: new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
            await CreateCommittedVersionsAsync(service, "lease-incarnation", "v1", "v2").ConfigureAwait(false);
            await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", "v2").ConfigureAwait(false);
            await SetActiveVersionAsync(service, "lease-incarnation", "v2").ConfigureAwait(false);
            WotResourceVersion oldVersion = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation")!.FindVersion("v1")!;
            using IWotRegistryVersionLease oldLease = await service.AcquireVersionLeaseAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", oldVersion).ConfigureAwait(false);
            WotRegistryMutationResult deleted = await service.DeleteVersionAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", "v1", oldVersion.Epoch)
                .ConfigureAwait(false);
            Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", "v1",
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            await service.UpsertResourceAsync(Request(
                "lease-incarnation", TestMaterialization.Td("urn:lease-incarnation", "v1"), "v1", false))
                .ConfigureAwait(false);
            WotResourceVersion replacement = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation")!.FindVersion("v1")!;
            Assert.That(replacement.Digest, Is.EqualTo(oldVersion.Digest));
            await Assert.ThatAsync(async () =>
            {
                using IWotRegistryVersionLease stale = await service.AcquireVersionLeaseAsync(
                    WotRegistryGroups.ThingDescriptions, "lease-incarnation", oldVersion).ConfigureAwait(false);
            }, Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);

            (WotResource Resource, WotResourceVersion Version)? pending = await service.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", "v3",
                WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
            Assert.That(pending, Is.Not.Null, "An old incarnation lease must not protect its replacement.");
            using IWotRegistryVersionLease currentLease = await service.AcquireVersionLeaseAsync(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation", replacement.With(epoch: 99))
                .ConfigureAwait(false);
            Assert.That(currentLease.Version, Is.SameAs(replacement));
            oldLease.Dispose();
            oldLease.Dispose();
            WotRegistrySnapshot before = service.Current;
            WotUpsertResourceRequest third = Request(
                "lease-incarnation", TestMaterialization.Td("urn:lease-incarnation", "v3"), "v3", false, string.Empty);
            Assert.That((await service.UpsertResourceAsync(third).ConfigureAwait(false)).Outcome,
                Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current, Is.SameAs(before));
            currentLease.Dispose();
            Assert.That((await service.UpsertResourceAsync(third).ConfigureAwait(false)).Outcome,
                Is.EqualTo(WoTOutcomeEnum.Success));
            WotResource after = service.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "lease-incarnation")!;
            Assert.That(after.FindVersion("v1"), Is.Null);
            Assert.That(after.FindVersion("v2"), Is.Not.Null);
            Assert.That(after.FindVersion("v3"), Is.Not.Null);
        }
    }
}
