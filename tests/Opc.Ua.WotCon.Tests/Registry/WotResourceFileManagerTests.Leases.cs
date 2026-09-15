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
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Tests.Materialization;

namespace Opc.Ua.WotCon.Tests.Registry
{
    public sealed partial class WotResourceFileManagerTests
    {
        [Test]
        public async Task ClosedHandleKeepsItsLeaseUntilTheActiveReadDrainsAsync()
        {
            using var owner = new WotRegistryService(
                bounds: new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
            foreach (string versionId in new[] { "v1", "v2" })
            {
                await owner.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "draining-lease",
                    VersionId = versionId,
                    Content = ByteString.From(TestMaterialization.Td("urn:draining-lease", versionId)),
                    SetAsDefault = false
                }).ConfigureAwait(false);
            }
            await owner.SetDefaultVersionAsync(WotRegistryGroups.ThingDescriptions, "draining-lease", "v2")
                .ConfigureAwait(false);
            WotResourceVersion first = owner.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "draining-lease")!.FindVersion("v1")!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new Harness(
                readContent: async (key, offset, count, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    return await owner.ReadContentChunkAsync(key, offset, count, ct).ConfigureAwait(false);
                },
                sessionId: new NodeId(94u),
                acquireVersionLease: (version, ct) => owner.AcquireVersionLeaseAsync(
                    WotRegistryGroups.ThingDescriptions, "draining-lease", version, ct));
            harness.Manager.UpdatePersistedContent(first, first.ContentType);
            OpenMethodStateResult opened = await harness.File.Open!.OnCallAsync!(
                harness.Context, harness.File.Open, harness.File.NodeId, ModeRead, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
            Task<(ServiceResult Status, ByteString Data)> reading = harness.ReadAsync(opened.FileHandle, 8).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(await harness.CloseAsync(opened.FileHandle).ConfigureAwait(false)),
                    Is.True);
                Assert.That(harness.File.OpenCount!.Value, Is.Zero);
                await Assert.ThatAsync(async () =>
                {
                    _ = await owner.TryCreateVersionAsync(
                        WotRegistryGroups.ThingDescriptions, "draining-lease", "v3",
                        WoTDocumentKindEnum.ThingDescription).ConfigureAwait(false);
                }, Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
            }
            (ServiceResult status, _) = await reading.ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(await owner.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions, "draining-lease", "v3", WoTDocumentKindEnum.ThingDescription)
                .ConfigureAwait(false), Is.Not.Null);
        }

        [TestCase((byte)1)]
        [TestCase((byte)6)]
        public async Task CanceledLeaseOpenReleasesOwnerPinAndWriterReservationAsync(byte mode)
        {
            using var owner = new WotRegistryService(
                bounds: new WotRegistryPersistenceBounds { MaxVersionsPerResource = 2 });
            foreach (string versionId in new[] { "v1", "v2" })
            {
                await owner.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingDescriptions,
                    ResourceId = "cancel-lease",
                    VersionId = versionId,
                    Content = ByteString.From(TestMaterialization.Td("urn:cancel-lease", versionId)),
                    SetAsDefault = false
                }).ConfigureAwait(false);
            }
            await owner.SetDefaultVersionAsync(WotRegistryGroups.ThingDescriptions, "cancel-lease", "v2")
                .ConfigureAwait(false);
            WotResourceVersion first = owner.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "cancel-lease")!.FindVersion("v1")!;
            var acquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = new Harness(
                maxOpenHandles: 1,
                readContent: owner.ReadContentChunkAsync,
                sessionId: new NodeId(93u),
                acquireVersionLease: async (version, ct) =>
                {
                    IWotRegistryVersionLease lease = await owner.AcquireVersionLeaseAsync(
                        WotRegistryGroups.ThingDescriptions, "cancel-lease", version, ct).ConfigureAwait(false);
                    acquired.TrySetResult(true);
                    await complete.Task.ConfigureAwait(false);
                    return lease;
                });
            harness.Manager.UpdatePersistedContent(first, first.ContentType);
            using var canceled = new CancellationTokenSource();
            Task<OpenMethodStateResult> opening = harness.File.Open!.OnCallAsync!(
                harness.Context, harness.File.Open, harness.File.NodeId, mode, canceled.Token).AsTask();
            try
            {
                await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                canceled.Cancel();
            }
            finally
            {
                complete.TrySetResult(true);
            }
            await Assert.ThatAsync(() => opening,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(harness.File.OpenCount!.Value, Is.Zero);
            (WotResource Resource, WotResourceVersion Version)? pending = await owner.TryCreateVersionAsync(
                WotRegistryGroups.ThingDescriptions, "cancel-lease", "v3", WoTDocumentKindEnum.ThingDescription)
                .ConfigureAwait(false);
            Assert.That(pending, Is.Not.Null, "Canceled Open must not leave a retention lease.");
            OpenMethodStateResult recovered = await harness.File.Open.OnCallAsync!(
                harness.Context, harness.File.Open, harness.File.NodeId, ModeWriteErase, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(recovered.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(await harness.CloseAsync(recovered.FileHandle).ConfigureAwait(false)),
                Is.True);
            Assert.That(harness.File.OpenCount.Value, Is.Zero);
            WotRegistryMutationResult committed = await owner.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "cancel-lease",
                VersionId = "v3",
                Content = ByteString.From(TestMaterialization.Td("urn:cancel-lease", "v3")),
                SetAsDefault = false,
                ExpectedVersionDigestHex = string.Empty
            }).ConfigureAwait(false);
            Assert.That(committed.Outcome, Is.EqualTo(WoTOutcomeEnum.Success));
            Assert.That(owner.Current.FindResource(
                WotRegistryGroups.ThingDescriptions, "cancel-lease")!.FindVersion("v1"), Is.Null);
        }
    }
}
