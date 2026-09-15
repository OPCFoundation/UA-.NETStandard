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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Registry
{
    public sealed partial class WotRegistryFollowUpTests
    {
        [TestCase("reload")]
        [TestCase("uncommitted-recovery")]
        [TestCase("committed-recovery")]
        [TestCase("committed-publication")]
        public async Task RehydrationRetainsEveryLiveLeaseUntilLastReleaseAsync(string publication)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            string root = Path.Combine(Path.GetTempPath(), "ua-rh", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var durable = new FileWotRegistryStore(root);
                var boundary = new RehydratingLeaseStore(durable);
                using var service = new WotRegistryService(
                    publication == "reload" ? durable : boundary,
                    new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
                boundary.SetContentReader(service.ReadContentAsync);
                await service.InitializeAsync(ct).ConfigureAwait(false);
                await CreateLeaseRecoveryPairAsync(service).ConfigureAwait(false);
                WotResource resource = FindLeaseRecoveryResource(service);
                WotResourceVersion first = resource.FindVersion("v1")!;
                using IWotRegistryVersionLease initial = await service.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, first, ct).ConfigureAwait(false);
                using IWotRegistryVersionLease peer = await service.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, first, ct).ConfigureAwait(false);
                await AssertRehydratedLeaseRetentionAsync(service, durable, ct).ConfigureAwait(false);

                if (publication == "reload")
                {
                    await service.InitializeAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    boundary.FailNextCommit(
                        commitBeforeFailure: publication != "uncommitted-recovery",
                        indeterminate: publication != "committed-publication");
                    WotUpsertResourceRequest update = Request(
                        resource.ResourceId,
                        TestMaterialization.Td("urn:rehydrated-lease", "updated-v2"),
                        "v2",
                        false);
                    if (publication == "committed-publication")
                    {
                        try
                        {
                            await service.UpsertResourceAsync(update, ct).ConfigureAwait(false);
                            Assert.Fail("The committed outcome must still be surfaced.");
                        }
                        catch (WotRegistryCommitDurabilityUncertainException exception)
                        {
                            Assert.That(service.Current, Is.SameAs(exception.CommittedSnapshot));
                        }
                    }
                    else
                    {
                        await Assert.ThatAsync(
                            async () => await service.UpsertResourceAsync(update, ct).ConfigureAwait(false),
                            Throws.TypeOf<WotRegistryCommitIndeterminateException>()).ConfigureAwait(false);
                        await Assert.ThatAsync(async () =>
                        {
                            _ = await service.TryCreateVersionAsync(
                                resource.GroupId, resource.ResourceId, "v3", resource.Kind, ct).ConfigureAwait(false);
                        }, Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                        await service.InitializeAsync(ct).ConfigureAwait(false);
                    }
                }

                WotResourceVersion rehydrated = FindLeaseRecoveryResource(service).FindVersion("v1")!;
                Assert.Multiple(() =>
                {
                    Assert.That(rehydrated.Digest, Is.EqualTo(first.Digest));
                    Assert.That(rehydrated.Epoch, Is.EqualTo(first.Epoch));
                    Assert.That(rehydrated.CreatedAt, Is.EqualTo(first.CreatedAt));
                    Assert.That(rehydrated.ModifiedAt, Is.EqualTo(first.ModifiedAt));
                });
                int changes = 0;
                service.Changed += (_, _) => changes++;
                await AssertRehydratedLeaseRetentionAsync(service, durable, ct).ConfigureAwait(false);
                using IWotRegistryVersionLease renewed = await service.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, first, ct).ConfigureAwait(false);
                Assert.That(renewed.Version, Is.SameAs(rehydrated));
                initial.Dispose();
                initial.Dispose();
                await service.InitializeAsync(ct).ConfigureAwait(false);
                await AssertRehydratedLeaseRetentionAsync(service, durable, ct).ConfigureAwait(false);
                peer.Dispose();
                await AssertRehydratedLeaseRetentionAsync(service, durable, ct).ConfigureAwait(false);
                Assert.That(await service.ReadContentAsync(initial.Version, ct).ConfigureAwait(false),
                    Is.EqualTo(ByteString.From(TestMaterialization.Td("urn:rehydrated-lease", "v1"))));
                Assert.That(changes, Is.Zero, "Reload, rejection and lease accounting must not emit mutations.");
                WotRegistrySnapshot beforeRelease = service.Current;
                renewed.Dispose();
                Assert.That(service.Current, Is.SameAs(beforeRelease));

                await AssertLeaseRecoveryEvictionAsync(service, durable, ct).ConfigureAwait(false);
                Assert.That(changes, Is.EqualTo(2), "Only allocation and content commit publish mutations.");
                using var restarted = new WotRegistryService(durable, service.Bounds);
                await restarted.InitializeAsync(ct).ConfigureAwait(false);
                AssertLeaseRecoveryVersions(FindLeaseRecoveryResource(restarted));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RehydrationDoesNotReconnectDeletedVersionOrResourceLeasesAsync(bool deleteResource)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            string root = Path.Combine(Path.GetTempPath(), "ua-ri", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var durable = new FileWotRegistryStore(root);
                using var service = new WotRegistryService(
                    durable, new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
                await service.InitializeAsync(ct).ConfigureAwait(false);
                await CreateLeaseRecoveryPairAsync(service).ConfigureAwait(false);
                WotResource resource = FindLeaseRecoveryResource(service);
                WotResourceVersion original = resource.FindVersion("v1")!;
                using IWotRegistryVersionLease stale = await service.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, original, ct).ConfigureAwait(false);
                await service.InitializeAsync(ct).ConfigureAwait(false);
                WotRegistryMutationResult deleted = deleteResource
                    ? await service.DeleteResourceAsync(
                        resource.GroupId, resource.ResourceId, cancellationToken: ct).ConfigureAwait(false)
                    : await service.DeleteVersionAsync(
                        resource.GroupId, resource.ResourceId, "v1", cancellationToken: ct).ConfigureAwait(false);
                Assert.That(deleted.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                await service.InitializeAsync(ct).ConfigureAwait(false);
                Assert.That(service.Current.FindResource(resource.GroupId, resource.ResourceId)?.FindVersion("v1"),
                    Is.Null);
                await CreateLeaseRecoveryPairAsync(service).ConfigureAwait(false);
                WotResourceVersion replacement = FindLeaseRecoveryResource(service).FindVersion("v1")!;
                Assert.That(replacement.Digest, Is.EqualTo(original.Digest));
                await service.InitializeAsync(ct).ConfigureAwait(false);
                await Assert.ThatAsync(async () =>
                {
                    using IWotRegistryVersionLease unexpected = await service.AcquireVersionLeaseAsync(
                        resource.GroupId, resource.ResourceId, original, ct).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                Assert.That(await service.TryCreateVersionAsync(
                    resource.GroupId, resource.ResourceId, "v3", resource.Kind, ct).ConfigureAwait(false),
                    Is.Not.Null, "A stale lease cannot protect a same-id, same-byte replacement.");
                using IWotRegistryVersionLease current = await service.AcquireVersionLeaseAsync(
                    resource.GroupId, resource.ResourceId, replacement, ct).ConfigureAwait(false);
                stale.Dispose();
                stale.Dispose();
                WotRegistrySnapshot beforeCommit = service.Current;
                Assert.That((await service.UpsertResourceAsync(Request(
                    resource.ResourceId, TestMaterialization.Td("urn:rehydrated-lease", "v3"), "v3", false), ct)
                    .ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
                Assert.That(service.Current, Is.SameAs(beforeCommit),
                    "Releasing an old lease cannot release the replacement's independent lease.");
                current.Dispose();
                Assert.That((await service.UpsertResourceAsync(Request(
                    resource.ResourceId, TestMaterialization.Td("urn:rehydrated-lease", "v3"), "v3", false), ct)
                    .ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
                AssertLeaseRecoveryVersions(FindLeaseRecoveryResource(service));
                AssertLeaseRecoveryVersions((await durable.LoadAsync(ct).ConfigureAwait(false))
                    .FindResource(resource.GroupId, resource.ResourceId)!);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task CreateLeaseRecoveryPairAsync(WotRegistryService service)
        {
            await CreateCommittedVersionsAsync(service, "rehydrated-lease", "v1", "v2").ConfigureAwait(false);
            Assert.That((await service.SetDefaultVersionAsync(
                WotRegistryGroups.ThingDescriptions, "rehydrated-lease", "v2").ConfigureAwait(false)).Outcome,
                Is.AnyOf(WoTOutcomeEnum.Success, WoTOutcomeEnum.Unchanged));
            await SetActiveVersionAsync(service, "rehydrated-lease", "v2").ConfigureAwait(false);
            WotResource resource = FindLeaseRecoveryResource(service);
            Assert.That(resource.FindVersion("v1")!.HasContent, Is.True);
            Assert.That(resource.FindVersion("v2")!.HasContent, Is.True);
            AssertLeaseRecoverySelectors(resource);
        }

        private static async Task AssertRehydratedLeaseRetentionAsync(
            WotRegistryService service,
            FileWotRegistryStore durable,
            CancellationToken ct)
        {
            WotRegistrySnapshot before = service.Current;
            WotResource resource = FindLeaseRecoveryResource(service);
            AssertLeaseRecoverySelectors(resource);
            await Assert.ThatAsync(async () =>
            {
                _ = await service.TryCreateVersionAsync(
                    resource.GroupId, resource.ResourceId, "v3", resource.Kind, ct).ConfigureAwait(false);
            }, Throws.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
            Assert.That((await service.UpsertResourceAsync(Request(
                resource.ResourceId, TestMaterialization.Td("urn:rehydrated-lease", "v3"), "v3", false), ct)
                .ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            Assert.That(service.Current, Is.SameAs(before));
            WotRegistrySnapshot stored = await durable.LoadAsync(ct).ConfigureAwait(false);
            WotResource retained = stored.FindResource(resource.GroupId, resource.ResourceId)!;
            Assert.Multiple(() =>
            {
                Assert.That(stored.Generation, Is.EqualTo(before.Generation));
                Assert.That(retained.MetaEpoch, Is.EqualTo(resource.MetaEpoch));
                Assert.That(retained.MetaCreatedAt, Is.EqualTo(resource.MetaCreatedAt));
                Assert.That(retained.MetaModifiedAt, Is.EqualTo(resource.MetaModifiedAt));
                Assert.That(retained.Versions.Select(version => version.VersionId),
                    Is.EquivalentTo(s_retainedRecoveryVersionIds));
                Assert.That(retained.FindVersion("v1")!.Digest, Is.EqualTo(resource.FindVersion("v1")!.Digest));
            });
            AssertLeaseRecoverySelectors(retained);
        }

        private static async Task AssertLeaseRecoveryEvictionAsync(
            WotRegistryService service,
            FileWotRegistryStore durable,
            CancellationToken ct)
        {
            WotResource resource = FindLeaseRecoveryResource(service);
            Assert.That(await service.TryCreateVersionAsync(
                resource.GroupId, resource.ResourceId, "v3", resource.Kind, ct).ConfigureAwait(false), Is.Not.Null);
            Assert.That((await service.UpsertResourceAsync(Request(
                resource.ResourceId, TestMaterialization.Td("urn:rehydrated-lease", "v3"), "v3", false), ct)
                .ConfigureAwait(false)).Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            AssertLeaseRecoveryVersions(FindLeaseRecoveryResource(service));
            AssertLeaseRecoveryVersions((await durable.LoadAsync(ct).ConfigureAwait(false))
                .FindResource(resource.GroupId, resource.ResourceId)!);
            Assert.That(await service.ReadContentAsync(
                FindLeaseRecoveryResource(service).FindVersion("v3")!, ct).ConfigureAwait(false),
                Is.EqualTo(ByteString.From(TestMaterialization.Td("urn:rehydrated-lease", "v3"))));
        }

        private static WotResource FindLeaseRecoveryResource(WotRegistryService service)
        {
            return service.Current.FindResource(WotRegistryGroups.ThingDescriptions, "rehydrated-lease")!;
        }

        private static void AssertLeaseRecoverySelectors(WotResource resource)
        {
            Assert.Multiple(() =>
            {
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
            });
        }

        private static void AssertLeaseRecoveryVersions(WotResource resource)
        {
            Assert.That(resource.Versions.Select(version => version.VersionId),
                Is.EquivalentTo(s_survivingRecoveryVersionIds));
            AssertLeaseRecoverySelectors(resource);
        }

        private static readonly string[] s_retainedRecoveryVersionIds = ["v1", "v2"];
        private static readonly string[] s_survivingRecoveryVersionIds = ["v2", "v3"];

        private sealed class RehydratingLeaseStore(FileWotRegistryStore inner) : IWotRegistryStore
        {
            public void SetContentReader(
                Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> readContent)
            {
                m_readContent = readContent ?? throw new ArgumentNullException(nameof(readContent));
            }

            public void FailNextCommit(bool commitBeforeFailure, bool indeterminate)
            {
                m_failNext = true;
                m_commitBeforeFailure = commitBeforeFailure;
                m_indeterminate = indeterminate;
            }

            public ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
            {
                return inner.LoadAsync(cancellationToken);
            }

            public async ValueTask CommitAsync(
                WotRegistrySnapshot snapshot,
                CancellationToken cancellationToken = default)
            {
                bool fail = m_failNext;
                m_failNext = false;
                if (!fail || m_commitBeforeFailure)
                {
                    Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> read =
                        m_readContent ?? throw new InvalidOperationException("The public content reader is required.");
                    foreach (WotResourceGroup group in snapshot.Groups.Values)
                    {
                        foreach (WotResource resource in group.Resources.Values)
                        {
                            foreach (WotResourceVersion version in resource.Versions
                                .Where(version => version.HasContent))
                            {
                                ByteString bytes = await read(version, cancellationToken).ConfigureAwait(false);
                                await inner.ResourceStore.WriteAsync(version.DigestHex, 0, bytes, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                    await inner.CommitAsync(snapshot, cancellationToken).ConfigureAwait(false);
                }
                if (fail)
                {
                    if (m_indeterminate)
                    {
                        throw new WotRegistryCommitIndeterminateException(
                            snapshot,
                            new IOException("Controlled store commit outcome."),
                            new IOException("Controlled generation validation failure."));
                    }
                    throw new WotRegistryCommitDurabilityUncertainException(
                        await inner.LoadAsync(CancellationToken.None).ConfigureAwait(false),
                        new IOException("Controlled durability-uncertain committed generation."));
                }
            }

            private Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>>? m_readContent;
            private bool m_failNext;
            private bool m_commitBeforeFailure;
            private bool m_indeterminate;
        }
    }
}
