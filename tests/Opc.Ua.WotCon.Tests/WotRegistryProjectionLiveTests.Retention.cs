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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotRegistryProjectionLiveTests
    {
        [Test]
        public async Task SessionAbandonmentReleasesOnlyItsOwnLeaseAliasesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("pinned-read", "v1", ct)
                .ConfigureAwait(false);
            ByteString original = ByteString.From(TestMaterialization.Td("urn:pinned-read", "session-leased"));
            await first.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("pinned-read", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "session-default")), ct: ct)
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("pinned-read"), ct)
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            _ = await first.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            _ = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            using var peerFixture = new ClientFixture(false, false, m_telemetry);
            await peerFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ISession peer = await peerFixture.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            await using (peer.ConfigureAwait(false))
            {
                try
                {
                    WotRegistryClient peerClient = await WotRegistryClient.ForServerAsync(peer, m_telemetry, ct: ct)
                        .ConfigureAwait(false);
                    (WotRegistryGroupClient peerGroup, _) = await peerClient
                        .GetOrCreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
                    var peerFirst = new ThingDescriptionFileTypeClient(peer, first.ResourceNodeId, m_telemetry);
                    uint retained = await peerFirst.OpenAsync(1, ct).ConfigureAwait(false);
                    await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                    await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                    Assert.That((await client.RefreshAllAsync(requestId: "session-lease-v2", ct: ct)
                        .ConfigureAwait(false)).HasFailures, Is.False);
                    WotResource resource = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                    Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
                    WotRegistrySnapshot before = m_registry.Current;
                    Assert.That(await m_session.CloseAsync(10000, true, ct).ConfigureAwait(false),
                        Is.EqualTo(StatusCodes.Good));
                    await Assert.ThatAsync(async () =>
                    {
                        _ = await peerGroup.CreateResourceAsync("pinned-read", "v3", ct).ConfigureAwait(false);
                    }, Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                    Assert.That(m_registry.Current, Is.SameAs(before));
                    Assert.That(await peerFirst.ReadAsync(retained, int.MaxValue, ct).ConfigureAwait(false),
                        Is.EqualTo(original));
                    await peerFirst.CloseAsync(retained, ct).ConfigureAwait(false);
                    (WotRegistryResourceClient third, _) = await peerGroup.CreateResourceAsync("pinned-read", "v3", ct)
                        .ConfigureAwait(false);
                    await third.Proxy.UploadAsync(
                        ByteString.From(TestMaterialization.Td("urn:pinned-read", "session-released")), ct: ct)
                        .ConfigureAwait(false);
                    WotResource after = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                    Assert.That(after.FindVersion("v1"), Is.Null);
                    Assert.That(after.FindVersion("v2"), Is.Not.Null);
                    Assert.That(after.FindVersion("v3"), Is.Not.Null);
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    Assert.That(await peer.CloseAsync(10000, true, cleanup.Token).ConfigureAwait(false),
                        Is.EqualTo(StatusCodes.Good));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CreationReturnedWriterKeepsItsVersionLeasedUntilCloseAsync(bool typed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("pinned-read", "v1", ct)
                .ConfigureAwait(false);
            ByteString original = ByteString.From(TestMaterialization.Td("urn:pinned-read", "created-writer"));
            await first.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("pinned-read", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "created-default")), ct: ct)
                .ConfigureAwait(false);
            uint handle;
            if (typed)
            {
                (WotRegistryResourceAllocation allocation, bool createdResource, bool createdVersion) =
                    await group.GetOrCreateThingDescriptionResourceAsync("urn:pinned-read", "v1", true, ct)
                        .ConfigureAwait(false);
                Assert.That(createdResource || createdVersion, Is.False);
                Assert.That(allocation.Version.ResourceNodeId, Is.EqualTo(first.ResourceNodeId));
                handle = allocation.FileHandle;
            }
            else
            {
                (NodeId node, string assigned, uint opened, bool created) = await group.Proxy
                    .GetOrCreateResourceAsync(AssignedResourceId("pinned-read"), "v1", true, ct)
                    .ConfigureAwait(false);
                Assert.That(node, Is.EqualTo(first.ResourceNodeId));
                Assert.That(assigned, Is.EqualTo("v1"));
                Assert.That(created, Is.False);
                handle = opened;
            }
            Assert.That(handle, Is.Not.Zero);
            try
            {
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                WotRegistryRefreshResult activated = await client.RefreshAllAsync(
                    requestId: "creation-lease-v2", ct: ct).ConfigureAwait(false);
                Assert.That(activated.HasFailures, Is.False);
                WotResource resource = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
                WotRegistrySnapshot before = m_registry.Current;
                await Assert.ThatAsync(async () =>
                {
                    _ = await group.CreateResourceAsync("pinned-read", "v3", ct).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                Assert.That(await first.Proxy.GetPositionAsync(handle, ct).ConfigureAwait(false), Is.Zero);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await first.Proxy.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }
            WotResource afterClose = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            Assert.That(await m_registry.ReadContentAsync(afterClose.FindVersion("v1")!, ct).ConfigureAwait(false),
                Is.EqualTo(original));
            (WotRegistryResourceClient third, _) = await group.CreateResourceAsync("pinned-read", "v3", ct)
                .ConfigureAwait(false);
            await third.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "creation-released")), ct: ct)
                .ConfigureAwait(false);
            WotResource after = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            Assert.That(after.FindVersion("v1"), Is.Null);
            Assert.That(after.FindVersion("v2"), Is.Not.Null);
            Assert.That(after.FindVersion("v3"), Is.Not.Null);
        }

        [Test]
        public async Task PendingCloseRetainsLeaseOnlyVersionUntilBothAliasesReleaseAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("pinned-read", "v1", ct)
                .ConfigureAwait(false);
            ByteString firstBytes = ByteString.From(TestMaterialization.Td("urn:pinned-read", "pending-leased"));
            await first.Proxy.UploadAsync(firstBytes, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("pinned-read", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "pending-default")), ct: ct)
                .ConfigureAwait(false);
            (WotRegistryResourceClient third, _) = await group.CreateResourceAsync("pinned-read", "v3", ct)
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("pinned-read"), ct)
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            uint exactHandle = await first.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            uint logicalHandle = await logical.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            ByteString thirdBytes = ByteString.From(TestMaterialization.Td("urn:pinned-read", "pending-third"));
            try
            {
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                WotRegistryRefreshResult activated = await client.RefreshAllAsync(
                    requestId: "pending-lease-v2", ct: ct).ConfigureAwait(false);
                Assert.That(activated.HasFailures, Is.False);
                WotResource resource = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
                WotRegistrySnapshot before = m_registry.Current;
                for (int remaining = 2; remaining > 0; remaining--)
                {
                    await Assert.ThatAsync(async () =>
                    {
                        await third.Proxy.UploadAsync(thirdBytes, ct: ct).ConfigureAwait(false);
                    }, Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                    Assert.That(m_registry.Current, Is.SameAs(before));
                    Assert.That(resource.FindVersion("v1"), Is.Not.Null);
                    Assert.That(resource.FindVersion("v3")!.HasContent, Is.False);
                    if (remaining == 2)
                    {
                        Assert.That(await first.Proxy.ReadAsync(exactHandle, int.MaxValue, ct).ConfigureAwait(false),
                            Is.EqualTo(firstBytes));
                        await first.Proxy.CloseAsync(exactHandle, ct).ConfigureAwait(false);
                        exactHandle = 0;
                    }
                }
                Assert.That(await logical.Proxy.ReadAsync(logicalHandle, int.MaxValue, ct).ConfigureAwait(false),
                    Is.EqualTo(firstBytes));
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (exactHandle != 0)
                {
                    await first.Proxy.CloseAsync(exactHandle, cleanup.Token).ConfigureAwait(false);
                }
                await logical.Proxy.CloseAsync(logicalHandle, cleanup.Token).ConfigureAwait(false);
            }
            await third.Proxy.UploadAsync(thirdBytes, ct: ct).ConfigureAwait(false);
            WotResource after = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            Assert.That(after.Versions, Has.Length.EqualTo(2));
            Assert.That(after.FindVersion("v1"), Is.Null);
            Assert.That(after.FindVersion("v2"), Is.Not.Null);
            Assert.That(after.FindVersion("v3")!.HasContent, Is.True);
            Assert.That(await m_registry.ReadContentAsync(after.FindVersion("v3")!, ct).ConfigureAwait(false),
                Is.EqualTo(thirdBytes));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WriterLeasePreventsAllocationUntilThePinnedCommitClosesAsync(bool logicalAlias)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("pinned-read", "v1", ct)
                .ConfigureAwait(false);
            ByteString original = ByteString.From(TestMaterialization.Td("urn:pinned-read", "writer-original"));
            await first.Proxy.UploadAsync(original, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("pinned-read", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "writer-default")), ct: ct)
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("pinned-read"), ct)
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            WotRegistryResourceClient writer = logicalAlias ? logical : first;
            uint handle = await writer.Proxy.OpenAsync(6, ct).ConfigureAwait(false);
            ByteString replacement = ByteString.From(TestMaterialization.Td("urn:pinned-read", "pinned-commit"));
            try
            {
                await writer.Proxy.WriteAsync(handle, replacement, ct).ConfigureAwait(false);
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                WotRegistryRefreshResult activated = await client.RefreshAllAsync(
                    requestId: "writer-lease-v2", ct: ct).ConfigureAwait(false);
                Assert.That(activated.HasFailures, Is.False);
                WotResource resource = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                Assert.Multiple(() =>
                {
                    Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
                });
                WotRegistrySnapshot before = m_registry.Current;
                await Assert.ThatAsync(async () =>
                {
                    _ = await group.CreateResourceAsync("pinned-read", "v3", ct).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                ByteString committed = await m_registry.ReadContentAsync(resource.FindVersion("v1")!, ct)
                    .ConfigureAwait(false);
                Assert.That(committed, Is.EqualTo(original));
                Assert.That(await writer.Proxy.GetPositionAsync(handle, ct).ConfigureAwait(false),
                    Is.EqualTo((ulong)replacement.Length));
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await writer.Proxy.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }
            WotResource afterClose = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            ByteString written = await m_registry.ReadContentAsync(afterClose.FindVersion("v1")!, ct)
                .ConfigureAwait(false);
            Assert.That(written, Is.EqualTo(replacement));
            Assert.That(afterClose.DefaultVersionId, Is.EqualTo("v2"));
            (WotRegistryResourceClient third, _) = await group.CreateResourceAsync("pinned-read", "v3", ct)
                .ConfigureAwait(false);
            await third.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "writer-released")), ct: ct)
                .ConfigureAwait(false);
            WotResource afterEviction = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            Assert.That(afterEviction.FindVersion("v1"), Is.Null);
            Assert.That(afterEviction.FindVersion("v2"), Is.Not.Null);
            Assert.That(afterEviction.FindVersion("v3"), Is.Not.Null);
            Assert.That(afterEviction.Versions, Has.Length.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LeaseOnlyVersionPreventsAllocationUntilItsLastHandleClosesAsync(bool logicalAlias)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            m_registry.Bounds.MaxVersionsPerResource = 2;
            WotRegistryClient client = await OpenClientAsync().ConfigureAwait(false);
            WotRegistryGroupClient group = await client.CreateThingDescriptionGroupAsync(ct).ConfigureAwait(false);
            (WotRegistryResourceClient first, _) = await group.CreateResourceAsync("pinned-read", "v1", ct)
                .ConfigureAwait(false);
            ByteString firstBytes = ByteString.From(TestMaterialization.Td("urn:pinned-read", "leased-first"));
            await first.Proxy.UploadAsync(firstBytes, ct: ct).ConfigureAwait(false);
            (WotRegistryResourceClient second, _) = await group.CreateResourceAsync("pinned-read", "v2", ct)
                .ConfigureAwait(false);
            await second.Proxy.UploadAsync(
                ByteString.From(TestMaterialization.Td("urn:pinned-read", "selected-second")), ct: ct)
                .ConfigureAwait(false);
            WotRegistryResourceClient logical = await group.OpenResourceAsync(AssignedResourceId("pinned-read"), ct)
                .ConfigureAwait(false);
            await WaitForPublishedDefaultAsync(logical, "v1").ConfigureAwait(false);
            WotRegistryResourceClient file = logicalAlias ? logical : first;
            NodeId firstNodeId = first.ResourceNodeId;
            uint handle = await file.Proxy.OpenAsync(1, ct).ConfigureAwait(false);
            try
            {
                await second.SetDefaultVersionAsync("v2", 0, ct).ConfigureAwait(false);
                await second.SetEnabledAsync(true, 0, ct).ConfigureAwait(false);
                WotRegistryRefreshResult activated = await client.RefreshAllAsync(
                    requestId: "lease-only-v2", ct: ct).ConfigureAwait(false);
                Assert.That(activated.HasFailures, Is.False);
                await WaitForPublishedDefaultAsync(logical, "v2").ConfigureAwait(false);
                WotRegistrySnapshot before = m_registry.Current;
                WotResource resource = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
                Assert.Multiple(() =>
                {
                    Assert.That(resource.ActiveVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DefaultVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.DesiredVersionId, Is.EqualTo("v2"));
                    Assert.That(resource.Versions, Has.Length.EqualTo(2));
                    Assert.That(resource.FindVersion("v1"), Is.Not.Null);
                });

                await Assert.ThatAsync(async () =>
                {
                    _ = await group.CreateResourceAsync("pinned-read", "v3", ct).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
                Assert.That(m_registry.Current, Is.SameAs(before));
                (NodeId retainedId, string retainedVersion, uint opened, bool created) =
                    await group.Proxy.GetOrCreateResourceAsync(
                        AssignedResourceId("pinned-read"), "v1", false, ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(retainedId, Is.EqualTo(firstNodeId));
                    Assert.That(retainedVersion, Is.EqualTo("v1"));
                    Assert.That(opened, Is.Zero);
                    Assert.That(created, Is.False);
                });
                ByteString content = await file.Proxy.ReadAsync(handle, int.MaxValue, ct).ConfigureAwait(false);
                Assert.That(content, Is.EqualTo(firstBytes));
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await file.Proxy.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }

            (WotRegistryResourceClient third, string assigned) = await group
                .CreateResourceAsync("pinned-read", "v3", ct).ConfigureAwait(false);
            Assert.That(assigned, Is.EqualTo("v3"));
            ByteString thirdBytes = ByteString.From(TestMaterialization.Td("urn:pinned-read", "eligible-third"));
            await third.Proxy.UploadAsync(thirdBytes, ct: ct).ConfigureAwait(false);
            WotResource after = FindResource(WotRegistryGroups.ThingDescriptions, "pinned-read")!;
            Assert.Multiple(() =>
            {
                Assert.That(after.Versions, Has.Length.EqualTo(2));
                Assert.That(after.FindVersion("v1"), Is.Null);
                Assert.That(after.FindVersion("v2"), Is.Not.Null);
                Assert.That(after.FindVersion("v3"), Is.Not.Null);
                Assert.That(after.ActiveVersionId, Is.EqualTo("v2"));
                Assert.That(after.DefaultVersionId, Is.EqualTo("v2"));
                Assert.That(after.DesiredVersionId, Is.EqualTo("v2"));
            });
            ByteString stored = await m_registry.ReadContentAsync(after.FindVersion("v3")!, ct)
                .ConfigureAwait(false);
            Assert.That(stored, Is.EqualTo(thirdBytes));
        }
    }
}
