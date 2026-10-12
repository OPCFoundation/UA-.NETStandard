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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Client;
using Opc.Ua.XRegistry.Server;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;
using UaDataTypeIds = Opc.Ua.DataTypeIds;
using UaReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using UaVariableTypeIds = Opc.Ua.VariableTypeIds;
using XRegistryMethodIds = Opc.Ua.XRegistry.MethodIds;
using XRegistryObjectTypeIds = Opc.Ua.XRegistry.ObjectTypeIds;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Exercises logical Resource and exact Version roles through an encrypted native session.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [Category("RuntimeNodeSet")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryRegistrarRolesIntegrationTests
    {
        [TestCase("v7")]
        [TestCase("pump")]
        public async Task NativeCreateGetOrCreateAndDeletePreservesLogicalAndExactRolesAsync(string firstVersionId)
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                await AssertWireContractsAsync(native, ct).ConfigureAwait(false);

                RegistryTypeClient registry = native.Client.GetRegistry(native.Client.RegistryNodeId);
                NodeId groupId = await registry.CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                (NodeId existingGroupId, bool groupCreated) = await registry
                    .GetOrCreateGroupAsync("schemas", ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(groupId.IsNull, Is.False);
                    Assert.That(existingGroupId, Is.EqualTo(groupId));
                    Assert.That(groupCreated, Is.False);
                });

                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId firstId, string firstAssignedId, uint firstHandle) = await group
                    .CreateResourceAsync("pump", firstVersionId, false, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstId.IsNull, Is.False);
                    Assert.That(firstAssignedId, Is.EqualTo(firstVersionId));
                    Assert.That(firstHandle, Is.Zero);
                });

                ResourceRoles originalRoles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                AssertVersionSet(native, originalRoles, [(firstVersionId, firstId)]);
                ResourceSnapshot firstBefore = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot initialLogical = await ReadResourceSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot earlierMeta = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                AssertResourceIdentity(firstBefore, firstVersionId, kLogicalXid + "/versions/" + firstVersionId);
                AssertResourceIdentity(initialLogical, firstVersionId, kLogicalXid);
                AssertDefaultProjection(initialLogical, firstBefore);
                Assert.Multiple(() =>
                {
                    Assert.That(earlierMeta.Epoch, Is.GreaterThan(0u));
                    Assert.That(earlierMeta.LabelsId, Is.Not.EqualTo(firstBefore.LabelsId));
                    Assert.That(earlierMeta.LabelsId, Is.Not.EqualTo(initialLogical.LabelsId));
                });

                (NodeId secondId, string secondAssignedId, uint secondHandle) = await group
                    .CreateResourceAsync("pump", "v8", false, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(secondId.IsNull, Is.False);
                    Assert.That(secondId, Is.Not.EqualTo(firstId));
                    Assert.That(secondAssignedId, Is.EqualTo("v8"));
                    Assert.That(secondHandle, Is.Zero);
                });

                ResourceSnapshot secondBefore = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfterSelection = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(metaAfterSelection.Epoch, Is.GreaterThan(earlierMeta.Epoch));
                    Assert.That(metaAfterSelection.CreatedAt, Is.EqualTo(earlierMeta.CreatedAt));
                    Assert.That(metaAfterSelection.ModifiedAt, Is.GreaterThanOrEqualTo(earlierMeta.ModifiedAt));
                    Assert.That(metaAfterSelection.LabelsId, Is.EqualTo(earlierMeta.LabelsId));
                });

                ByteString payload = ByteString.From(9, 8, 7, 6, 5);
                await CommitDocumentAsync(native.Client.GetResource(secondId), payload, ct).ConfigureAwait(false);
                ArrayOf<(string VersionId, NodeId NodeId)> bothVersions = [(firstVersionId, firstId), ("v8", secondId)];
                await AssertStableRolesAsync(native, groupId, originalRoles, bothVersions, ct).ConfigureAwait(false);
                ResourceSnapshot firstAfter = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot secondAfter = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfter = await ReadResourceSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfterCommit = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                AssertResourceIdentity(firstAfter, firstVersionId, kLogicalXid + "/versions/" + firstVersionId);
                AssertResourceIdentity(secondAfter, "v8", kLogicalXid + "/versions/v8");
                AssertResourceIdentity(logicalAfter, "v8", kLogicalXid);
                AssertDefaultProjection(logicalAfter, secondAfter);
                Assert.Multiple(() =>
                {
                    Assert.That(firstAfter, Is.EqualTo(firstBefore));
                    Assert.That(secondAfter.Epoch, Is.GreaterThan(secondBefore.Epoch));
                    Assert.That(secondAfter.CreatedAt, Is.EqualTo(secondBefore.CreatedAt));
                    Assert.That(secondAfter.ModifiedAt, Is.GreaterThanOrEqualTo(secondBefore.ModifiedAt));
                    Assert.That(secondAfter.Size, Is.EqualTo((ulong)payload.Length));
                    Assert.That(secondAfter.LabelsId, Is.EqualTo(secondBefore.LabelsId));
                    Assert.That(secondAfter.LabelsId, Is.Not.EqualTo(firstAfter.LabelsId));
                    Assert.That(metaAfterCommit, Is.EqualTo(metaAfterSelection));
                    Assert.That(metaAfterCommit.LabelsId, Is.Not.EqualTo(secondAfter.LabelsId));
                    Assert.That(metaAfterCommit.LabelsId, Is.Not.EqualTo(logicalAfter.LabelsId));
                });
                await AssertNoVersionsComponentAsync(native, firstId, ct).ConfigureAwait(false);
                await AssertNoVersionsComponentAsync(native, secondId, ct).ConfigureAwait(false);
                await AssertEmptyLabelsAsync(native, firstAfter.LabelsId, ct).ConfigureAwait(false);
                await AssertEmptyLabelsAsync(native, secondAfter.LabelsId, ct).ConfigureAwait(false);
                await AssertEmptyLabelsAsync(native, logicalAfter.LabelsId, ct).ConfigureAwait(false);
                await AssertEmptyLabelsAsync(native, metaAfterCommit.LabelsId, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, originalRoles.LogicalId, payload, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, secondId, payload, ct).ConfigureAwait(false);

                (NodeId existingId, string existingVersionId, uint existingHandle, bool resourceCreated) = await group
                    .GetOrCreateResourceAsync("pump", firstVersionId, false, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(existingId, Is.EqualTo(firstId));
                    Assert.That(existingVersionId, Is.EqualTo(firstVersionId));
                    Assert.That(existingHandle, Is.Zero);
                    Assert.That(resourceCreated, Is.False);
                });
                await AssertStableRolesAsync(native, groupId, originalRoles, bothVersions, ct).ConfigureAwait(false);
                ResourceSnapshot firstAfterGet = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot secondAfterGet = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfterGet = await ReadResourceSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfterGet = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstAfterGet, Is.EqualTo(firstAfter));
                    Assert.That(secondAfterGet, Is.EqualTo(secondAfter));
                    Assert.That(logicalAfterGet, Is.EqualTo(logicalAfter));
                    Assert.That(logicalAfterGet.VersionId, Is.EqualTo("v8"));
                    Assert.That(metaAfterGet, Is.EqualTo(metaAfterCommit));
                    Assert.That(firstAfterGet.Epoch, Is.GreaterThan(0u));
                    Assert.That(firstAfterGet.Epoch, Is.Not.EqualTo(metaAfterGet.Epoch));
                });
                await AssertDocumentAsync(native, originalRoles.LogicalId, payload, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, secondId, payload, ct).ConfigureAwait(false);

                await native.Client.GetResource(firstId).DeleteAsync(firstAfterGet.Epoch, ct).ConfigureAwait(false);
                await AssertAbsentAsync(native, [firstId], ct).ConfigureAwait(false);
                ArrayOf<(string VersionId, NodeId NodeId)> remainingVersions = [("v8", secondId)];
                await AssertStableRolesAsync(native, groupId, originalRoles, remainingVersions, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfterDelete = await ReadResourceSnapshotAsync(
                    native,
                    originalRoles.LogicalId,
                    ct).ConfigureAwait(false);
                ResourceSnapshot remainingAfterDelete = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfterDelete = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                AssertResourceIdentity(logicalAfterDelete, "v8", kLogicalXid);
                AssertDefaultProjection(logicalAfterDelete, remainingAfterDelete);
                Assert.Multiple(() =>
                {
                    Assert.That(remainingAfterDelete, Is.EqualTo(secondAfterGet));
                    Assert.That(metaAfterDelete.Epoch, Is.GreaterThan(metaAfterGet.Epoch));
                    Assert.That(metaAfterDelete.Epoch, Is.Not.EqualTo(earlierMeta.Epoch));
                    Assert.That(metaAfterDelete.CreatedAt, Is.EqualTo(earlierMeta.CreatedAt));
                    Assert.That(metaAfterDelete.LabelsId, Is.EqualTo(earlierMeta.LabelsId));
                    Assert.That(earlierMeta.Epoch, Is.GreaterThan(0u));
                });
                await AssertDocumentAsync(native, originalRoles.LogicalId, payload, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, secondId, payload, ct).ConfigureAwait(false);

                await Assert.ThatAsync(
                    async () => await native.Client.GetResource(originalRoles.LogicalId)
                        .DeleteAsync(earlierMeta.Epoch, ct)
                        .ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadInvalidState))
                    .ConfigureAwait(false);
                await AssertStableRolesAsync(native, groupId, originalRoles, remainingVersions, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfterStale = await ReadResourceSnapshotAsync(
                    native,
                    originalRoles.LogicalId,
                    ct).ConfigureAwait(false);
                ResourceSnapshot remainingAfterStale = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot currentMeta = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(logicalAfterStale, Is.EqualTo(logicalAfterDelete));
                    Assert.That(logicalAfterStale.VersionId, Is.EqualTo("v8"));
                    Assert.That(remainingAfterStale, Is.EqualTo(remainingAfterDelete));
                    Assert.That(currentMeta, Is.EqualTo(metaAfterDelete));
                    Assert.That(currentMeta.Epoch, Is.GreaterThan(0u));
                    Assert.That(remainingAfterStale.Epoch, Is.GreaterThan(0u));
                    Assert.That(currentMeta.Epoch, Is.Not.EqualTo(remainingAfterStale.Epoch));
                });
                await AssertDocumentAsync(native, originalRoles.LogicalId, payload, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, secondId, payload, ct).ConfigureAwait(false);

                await native.Client.GetResource(originalRoles.LogicalId)
                    .DeleteAsync(currentMeta.Epoch, ct)
                    .ConfigureAwait(false);
                ArrayOf<ReferenceDescription> groupChildren = await native
                    .BrowseAsync(groupId, UaReferenceTypeIds.Organizes, NodeClass.Object, ct)
                    .ConfigureAwait(false);
                Assert.That(groupChildren, Is.Empty);
                await AssertAbsentAsync(native, [originalRoles.LogicalId, originalRoles.VersionsId, secondId], ct)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeAssignedVersionMatchesBrowseAndDuplicateCreateFailsAsync()
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);

                RegistryTypeClient registry = native.Client.GetRegistry(native.Client.RegistryNodeId);
                (NodeId groupId, bool groupCreated) = await registry
                    .GetOrCreateGroupAsync("schemas", ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(groupId.IsNull, Is.False);
                    Assert.That(groupCreated, Is.True);
                });
                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId exactId, string assignedId, uint handle, bool created) = await group
                    .GetOrCreateResourceAsync("pump", string.Empty, false, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(exactId.IsNull, Is.False);
                    Assert.That(assignedId, Is.Not.Null.And.Not.Empty);
                    Assert.That(handle, Is.Zero);
                    Assert.That(created, Is.True);
                });

                ResourceRoles originalRoles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ArrayOf<(string VersionId, NodeId NodeId)> expectedVersions = [(assignedId, exactId)];
                AssertVersionSet(native, originalRoles, expectedVersions);
                ResourceSnapshot originalExact = await ReadResourceSnapshotAsync(native, exactId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot originalLogical = await ReadResourceSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot originalMeta = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                AssertResourceIdentity(originalExact, assignedId, kLogicalXid + "/versions/" + assignedId);
                AssertResourceIdentity(originalLogical, assignedId, kLogicalXid);
                AssertDefaultProjection(originalLogical, originalExact);
                await AssertNoVersionsComponentAsync(native, exactId, ct).ConfigureAwait(false);

                (NodeId repeatedId, string repeatedVersion, uint repeatedHandle, bool repeatedCreated) = await group
                    .GetOrCreateResourceAsync("pump", string.Empty, false, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(repeatedId, Is.EqualTo(exactId));
                    Assert.That(repeatedVersion, Is.EqualTo(assignedId));
                    Assert.That(repeatedHandle, Is.Zero);
                    Assert.That(repeatedCreated, Is.False);
                });
                await AssertStableRolesAsync(native, groupId, originalRoles, expectedVersions, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot repeatedExact = await ReadResourceSnapshotAsync(native, exactId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot repeatedLogical = await ReadResourceSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot repeatedMeta = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(repeatedExact, Is.EqualTo(originalExact));
                    Assert.That(repeatedLogical, Is.EqualTo(originalLogical));
                    Assert.That(repeatedLogical.VersionId, Is.EqualTo(assignedId));
                    Assert.That(repeatedMeta, Is.EqualTo(originalMeta));
                });

                await Assert.ThatAsync(
                    async () =>
                    {
                        await group.CreateResourceAsync("pump", assignedId, false, ct).ConfigureAwait(false);
                    },
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadNodeIdExists))
                    .ConfigureAwait(false);
                await AssertStableRolesAsync(native, groupId, originalRoles, expectedVersions, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot afterDuplicateExact = await ReadResourceSnapshotAsync(native, exactId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot afterDuplicateLogical = await ReadResourceSnapshotAsync(
                    native,
                    originalRoles.LogicalId,
                    ct).ConfigureAwait(false);
                MetaSnapshot afterDuplicateMeta = await ReadMetaSnapshotAsync(native, originalRoles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(afterDuplicateExact, Is.EqualTo(originalExact));
                    Assert.That(afterDuplicateLogical, Is.EqualTo(originalLogical));
                    Assert.That(afterDuplicateLogical.VersionId, Is.EqualTo(assignedId));
                    Assert.That(afterDuplicateMeta, Is.EqualTo(originalMeta));
                });
            }
        }

        [Test]
        public async Task NativePinnedUnreadLogicalHandlePreservesVersionCursorAndSessionOwnershipAsync()
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId firstId, _, _) = await group.CreateResourceAsync("pump", "v1", false, ct)
                    .ConfigureAwait(false);
                await CommitDocumentAsync(native.Client.GetResource(firstId), ByteString.From(1, 2, 3, 4, 5), ct)
                    .ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ResourceTypeClient logical = native.Client.GetResource(roles.LogicalId);
                uint oldHandle = await logical.OpenAsync(1, ct).ConfigureAwait(false);
                Assert.That(await logical.GetPositionAsync(oldHandle, ct).ConfigureAwait(false), Is.Zero);

                (NodeId secondId, _, _) = await group.CreateResourceAsync("pump", "v2", false, ct)
                    .ConfigureAwait(false);
                await CommitDocumentAsync(native.Client.GetResource(secondId), ByteString.From(9, 8, 7), ct)
                    .ConfigureAwait(false);
                ResourceSnapshot current = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.That(current.VersionId, Is.EqualTo("v2"));
                Assert.That(current.Size, Is.EqualTo(3uL));

                ISession peer = await native.CreateSessionAsync(ct).ConfigureAwait(false);
                await using (peer.ConfigureAwait(false))
                {
                    try
                    {
                        var peerClient = new GenericXRegistryClient(peer, native.Telemetry);
                        ResourceTypeClient peerLogical = peerClient.GetResource(roles.LogicalId);
                        await AssertServiceFailureAsync(async () =>
                        {
                            _ = await peerLogical.ReadAsync(oldHandle, 1, ct).ConfigureAwait(false);
                        }, StatusCodes.BadInvalidState).ConfigureAwait(false);
                        Assert.That(await logical.GetPositionAsync(oldHandle, ct).ConfigureAwait(false), Is.Zero);
                        await AssertServiceFailureAsync(async () =>
                        {
                            await peerLogical.CloseAsync(oldHandle, ct).ConfigureAwait(false);
                        }, StatusCodes.BadInvalidState).ConfigureAwait(false);
                        Assert.That(await logical.GetPositionAsync(oldHandle, ct).ConfigureAwait(false), Is.Zero);

                        uint newHandle = await peerLogical.OpenAsync(1, ct).ConfigureAwait(false);
                        Assert.That(await peerLogical.GetPositionAsync(newHandle, ct).ConfigureAwait(false), Is.Zero);
                        await AssertReadAsync(logical, oldHandle, 2, ByteString.From(1, 2), 2, ct)
                            .ConfigureAwait(false);
                        await logical.SetPositionAsync(oldHandle, 1, ct).ConfigureAwait(false);
                        Assert.That(await logical.GetPositionAsync(oldHandle, ct).ConfigureAwait(false),
                            Is.EqualTo(1uL));
                        await AssertReadAsync(logical, oldHandle, 2, ByteString.From(2, 3), 3, ct)
                            .ConfigureAwait(false);
                        await AssertReadAsync(logical, oldHandle, 10, ByteString.From(4, 5), 5, ct)
                            .ConfigureAwait(false);
                        await AssertReadAsync(logical, oldHandle, 1, ByteString.Empty, 5, ct).ConfigureAwait(false);
                        await logical.SetPositionAsync(oldHandle, 99, ct).ConfigureAwait(false);
                        Assert.That(await logical.GetPositionAsync(oldHandle, ct).ConfigureAwait(false),
                            Is.EqualTo(5uL));

                        await AssertReadAsync(peerLogical, newHandle, 1, ByteString.From(9), 1, ct)
                            .ConfigureAwait(false);
                        await peerLogical.SetPositionAsync(newHandle, 0, ct).ConfigureAwait(false);
                        Assert.That(await peerLogical.GetPositionAsync(newHandle, ct).ConfigureAwait(false), Is.Zero);
                        await AssertReadAsync(peerLogical, newHandle, 2, ByteString.From(9, 8), 2, ct)
                            .ConfigureAwait(false);
                        await AssertReadAsync(peerLogical, newHandle, 10, ByteString.From(7), 3, ct)
                            .ConfigureAwait(false);
                        await AssertReadAsync(peerLogical, newHandle, 1, ByteString.Empty, 3, ct).ConfigureAwait(false);
                        await logical.CloseAsync(oldHandle, ct).ConfigureAwait(false);
                        await peerLogical.CloseAsync(newHandle, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        await CloseSessionAsync(peer).ConfigureAwait(false);
                    }
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task NativeLogicalAndExactAliasesShareVersionReservationsAsync(bool logicalReader)
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId firstId, _, _) = await group.CreateResourceAsync("pump", "v1", false, ct)
                    .ConfigureAwait(false);
                ByteString firstBytes = ByteString.From(1, 2, 3, 4, 5);
                await CommitDocumentAsync(native.Client.GetResource(firstId), firstBytes, ct).ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ResourceTypeClient reader = native.Client.GetResource(logicalReader ? roles.LogicalId : firstId);
                ResourceTypeClient opposite = native.Client.GetResource(logicalReader ? firstId : roles.LogicalId);
                uint handle = await reader.OpenAsync(1, ct).ConfigureAwait(false);
                await AssertServiceFailureAsync(async () =>
                {
                    _ = await opposite.OpenAsync(2, ct).ConfigureAwait(false);
                }, StatusCodes.BadNotWritable).ConfigureAwait(false);

                (NodeId secondId, _, _) = await group.CreateResourceAsync("pump", "v2", false, ct)
                    .ConfigureAwait(false);
                ByteString secondBytes = ByteString.From(9, 8, 7);
                await CommitDocumentAsync(native.Client.GetResource(secondId), secondBytes, ct).ConfigureAwait(false);
                ResourceSnapshot current = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.That(current.VersionId, Is.EqualTo("v2"));
                await AssertDocumentAsync(native, roles.LogicalId, secondBytes, ct).ConfigureAwait(false);
                await AssertReadAsync(reader, handle, 10, firstBytes, 5, ct).ConfigureAwait(false);
                ResourceTypeClient exactFirst = native.Client.GetResource(firstId);
                await AssertServiceFailureAsync(async () =>
                {
                    _ = await exactFirst.OpenAsync(2, ct).ConfigureAwait(false);
                }, StatusCodes.BadNotWritable).ConfigureAwait(false);

                await reader.CloseAsync(handle, ct).ConfigureAwait(false);
                uint recovered = await exactFirst.OpenAsync(2, ct).ConfigureAwait(false);
                await exactFirst.CloseAsync(recovered, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, firstId, firstBytes, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, roles.LogicalId, secondBytes, ct).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeSessionCloseDiscardsLogicalWriterAndReleasesReservationAsync()
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                (NodeId exactId, _, _) = await native.Client.GetGroup(groupId)
                    .CreateResourceAsync("pump", "v1", false, ct).ConfigureAwait(false);
                ByteString originalBytes = ByteString.From(1, 2, 3, 4, 5);
                await CommitDocumentAsync(native.Client.GetResource(exactId), originalBytes, ct)
                    .ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ResourceSnapshot exactBefore = await ReadResourceSnapshotAsync(native, exactId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalBefore = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaBefore = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);

                ISession owner = await native.CreateSessionAsync(ct).ConfigureAwait(false);
                await using (owner.ConfigureAwait(false))
                {
                    try
                    {
                        var ownerClient = new GenericXRegistryClient(owner, native.Telemetry);
                        ResourceTypeClient ownerFile = ownerClient.GetResource(roles.LogicalId);
                        uint abandoned = await ownerFile.OpenAsync(2, ct).ConfigureAwait(false);
                        await ownerFile.WriteAsync(abandoned, ByteString.From(9, 8, 7), ct).ConfigureAwait(false);
                        await AssertServiceFailureAsync(async () =>
                        {
                            _ = await native.Client.GetResource(exactId).OpenAsync(2, ct).ConfigureAwait(false);
                        }, StatusCodes.BadNotWritable).ConfigureAwait(false);
                    }
                    finally
                    {
                        await CloseSessionAsync(owner).ConfigureAwait(false);
                    }
                }

                ResourceSnapshot exactAfter = await ReadResourceSnapshotAsync(native, exactId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfter = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfter = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(exactAfter, Is.EqualTo(exactBefore));
                    Assert.That(logicalAfter, Is.EqualTo(logicalBefore));
                    Assert.That(metaAfter, Is.EqualTo(metaBefore));
                });
                await AssertDocumentAsync(native, exactId, originalBytes, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, roles.LogicalId, originalBytes, ct).ConfigureAwait(false);
                ResourceTypeClient file = native.Client.GetResource(exactId);
                uint recovered = await file.OpenAsync(2, ct).ConfigureAwait(false);
                await file.CloseAsync(recovered, ct).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeLogicalWriterCommitsItsPinnedVersionAfterDefaultChangesAsync()
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId firstId, _, _) = await group.CreateResourceAsync("pump", "v1", false, ct)
                    .ConfigureAwait(false);
                await CommitDocumentAsync(native.Client.GetResource(firstId), ByteString.From(1, 2, 3, 4), ct)
                    .ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ResourceTypeClient logical = native.Client.GetResource(roles.LogicalId);
                ResourceSnapshot firstBefore = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                uint pinned = await logical.OpenAsync(6, ct).ConfigureAwait(false);
                (NodeId secondId, string assigned, uint secondHandle) = await group
                    .CreateResourceAsync("pump", "v2", true, ct).ConfigureAwait(false);
                Assert.That(assigned, Is.EqualTo("v2"));
                Assert.That(secondHandle, Is.Not.Zero);
                ByteString secondBytes = ByteString.From(7, 8, 9);
                ResourceTypeClient second = native.Client.GetResource(secondId);
                await second.WriteAsync(secondHandle, secondBytes, ct).ConfigureAwait(false);
                await second.CloseAsync(secondHandle, ct).ConfigureAwait(false);
                ResourceSnapshot logicalBefore = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot secondBefore = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaBefore = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);

                ByteString firstBytes = ByteString.From(5, 4, 3, 2, 1);
                await logical.WriteAsync(pinned, firstBytes, ct).ConfigureAwait(false);
                await logical.CloseAsync(pinned, ct).ConfigureAwait(false);
                ResourceSnapshot firstAfter = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot secondAfter = await ReadResourceSnapshotAsync(native, secondId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot logicalAfter = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaAfter = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(firstAfter.Epoch, Is.EqualTo(firstBefore.Epoch + 1));
                    Assert.That(firstAfter.Size, Is.EqualTo((ulong)firstBytes.Length));
                    Assert.That(firstAfter.Xid, Is.EqualTo(firstBefore.Xid));
                    Assert.That(secondAfter, Is.EqualTo(secondBefore));
                    Assert.That(logicalAfter, Is.EqualTo(logicalBefore));
                    Assert.That(logicalAfter.VersionId, Is.EqualTo("v2"));
                    Assert.That(metaAfter, Is.EqualTo(metaBefore));
                });
                await AssertDocumentAsync(native, firstId, firstBytes, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, secondId, secondBytes, ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, roles.LogicalId, secondBytes, ct).ConfigureAwait(false);

                await second.DeleteAsync(secondAfter.Epoch, ct).ConfigureAwait(false);
                await AssertStableRolesAsync(native, groupId, roles, [("v1", firstId)], ct).ConfigureAwait(false);
                ResourceSnapshot restored = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                ResourceSnapshot surviving = await ReadResourceSnapshotAsync(native, firstId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot afterDeletion = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(surviving, Is.EqualTo(firstAfter));
                    Assert.That(restored.VersionId, Is.EqualTo("v1"));
                    Assert.That(restored.Xid, Is.EqualTo(kLogicalXid));
                    Assert.That(afterDeletion.Epoch, Is.EqualTo(metaBefore.Epoch + 1));
                    Assert.That(afterDeletion.CreatedAt, Is.EqualTo(metaBefore.CreatedAt));
                });
                AssertDefaultProjection(restored, surviving);
                await AssertAbsentAsync(native, [secondId], ct).ConfigureAwait(false);
                await AssertDocumentAsync(native, roles.LogicalId, firstBytes, ct).ConfigureAwait(false);
            }
        }

        [TestCase("Labels")]
        [TestCase("MetaLabels")]
        public async Task NativeUpdatesCorrelateCoalesceAndPreserveNoOpOwnedStateAsync(string labelsRole)
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                (NodeId exactId, _, _) = await native.Client.GetGroup(groupId)
                    .CreateResourceAsync("pump", "v1", false, ct).ConfigureAwait(false);
                await CommitDocumentAsync(native.Client.GetResource(exactId), ByteString.From(1, 2, 3, 4), ct)
                    .ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                ResourceTypeClient logical = native.Client.GetResource(roles.LogicalId);
                MetaSnapshot initialMeta = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                var updates = new NativeUpdates(native);
                await using (updates.ConfigureAwait(false))
                {
                    await updates.StartAsync(ct).ConfigureAwait(false);
                    await CommitChunksAsync(logical, ByteString.From(5, 6, 7, 8), ct).ConfigureAwait(false);
                    ResourceSnapshot first = await ReadResourceSnapshotAsync(native, exactId, ct)
                        .ConfigureAwait(false);
                    await updates.WaitForPairAsync(first.Epoch, ct).ConfigureAwait(false);

                    ByteString committed = ByteString.From(9, 10, 11, 12);
                    await CommitChunksAsync(logical, committed, ct).ConfigureAwait(false);
                    ResourceSnapshot second = await ReadResourceSnapshotAsync(native, exactId, ct)
                        .ConfigureAwait(false);
                    await updates.WaitForPairAsync(second.Epoch, ct).ConfigureAwait(false);
                    Assert.That(second.Epoch, Is.EqualTo(first.Epoch + 1));
                    ResourceUpdatedEventTypeRecord resourceEvent = updates.ResourceUpdates.ToList()
                        .Single(evt => evt.Epoch == first.Epoch);
                    VersionUpdatedEventTypeRecord versionEvent = updates.VersionUpdates.ToList()
                        .Single(evt => evt.Epoch == first.Epoch);
                    Assert.Multiple(() =>
                    {
                        Assert.That(resourceEvent.EventType,
                            Is.EqualTo(native.LocalNodeId(XRegistryObjectTypeIds.ResourceUpdatedEventType)));
                        Assert.That(versionEvent.EventType,
                            Is.EqualTo(native.LocalNodeId(XRegistryObjectTypeIds.VersionUpdatedEventType)));
                        Assert.That(resourceEvent.EventType, Is.EqualTo(new NodeId(63018u, native.NamespaceIndex)));
                        Assert.That(versionEvent.EventType, Is.EqualTo(new NodeId(63023u, native.NamespaceIndex)));
                        Assert.That(resourceEvent.SourceNode, Is.EqualTo(roles.LogicalId));
                        Assert.That(versionEvent.SourceNode, Is.EqualTo(exactId));
                        Assert.That(resourceEvent.Subject, Is.EqualTo(kLogicalXid));
                        Assert.That(versionEvent.Subject, Is.EqualTo(kLogicalXid + "/versions/v1"));
                        Assert.That(resourceEvent.SourceUrl, Is.EqualTo(kEventSourceUrl));
                        Assert.That(versionEvent.SourceUrl, Is.EqualTo(kEventSourceUrl));
                        Assert.That(resourceEvent.Epoch, Is.EqualTo(first.Epoch));
                        Assert.That(versionEvent.Epoch, Is.EqualTo(first.Epoch));
                        Assert.That(resourceEvent.MetaEpoch, Is.EqualTo(initialMeta.Epoch));
                        Assert.That(resourceEvent.Time, Is.EqualTo(versionEvent.Time));
                        Assert.That(resourceEvent.Time, Is.GreaterThan(DateTime.MinValue));
                        Assert.That(resourceEvent.Changed, Is.EqualTo(s_documentChanged));
                        Assert.That(versionEvent.Changed, Is.EqualTo(s_documentChanged));
                        Assert.That(updates.ResourceUpdates.ToList().Select(evt => evt.Epoch),
                            Is.EqualTo(new[] { first.Epoch, second.Epoch }));
                        Assert.That(updates.VersionUpdates.ToList().Select(evt => evt.Epoch),
                            Is.EqualTo(new[] { first.Epoch, second.Epoch }));
                    });

                    ResourceSnapshot logicalBefore = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaBefore = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    Assert.That(metaBefore, Is.EqualTo(initialMeta));
                    await CommitChunksAsync(logical, committed, ct).ConfigureAwait(false);
                    ResourceSnapshot exactNoOp = await ReadResourceSnapshotAsync(native, exactId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalNoOp = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaNoOp = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(exactNoOp, Is.EqualTo(second));
                        Assert.That(logicalNoOp, Is.EqualTo(logicalBefore));
                        Assert.That(metaNoOp, Is.EqualTo(metaBefore));
                    });
                    await AssertDocumentAsync(native, roles.LogicalId, committed, ct).ConfigureAwait(false);

                    bool metaLabels = labelsRole == "MetaLabels";
                    NodeId labelsId = await ReadLabelsIdAsync(
                        native, metaLabels ? roles.LogicalId : exactId, labelsRole, ct).ConfigureAwait(false);
                    var labels = new AttributesTypeClient(native.Session, labelsId, native.Telemetry);
                    uint epoch = metaLabels ? metaBefore.Epoch : second.Epoch;
                    await labels.AddAttributeAsync("owner", "plant-1", epoch, ct).ConfigureAwait(false);
                    ResourceSnapshot exactLabel = await ReadResourceSnapshotAsync(native, exactId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalLabel = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaLabel = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    if (metaLabels)
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(exactLabel, Is.EqualTo(exactNoOp));
                            Assert.That(logicalLabel, Is.EqualTo(logicalNoOp));
                            Assert.That(metaLabel.Epoch, Is.EqualTo(metaBefore.Epoch + 1));
                        });
                    }
                    else
                    {
                        Assert.That(exactLabel.Epoch, Is.EqualTo(second.Epoch + 1));
                        Assert.That(metaLabel, Is.EqualTo(metaBefore));
                        AssertDefaultProjection(logicalLabel, exactLabel);
                    }
                    ArrayOf<DataValue> labelValue = await native.ReadPropertiesAsync(
                        labelsId, [native.Name("owner")], ct).ConfigureAwait(false);
                    Assert.That(DecodeString(labelValue[0]), Is.EqualTo("plant-1"));
                    uint currentEpoch = metaLabels ? metaLabel.Epoch : exactLabel.Epoch;
                    await labels.AddAttributeAsync("owner", "plant-1", currentEpoch, ct).ConfigureAwait(false);
                    await AssertServiceFailureAsync(async () =>
                    {
                        await labels.RemoveAttributeAsync("absent", currentEpoch, ct).ConfigureAwait(false);
                    }, StatusCodes.BadNotFound).ConfigureAwait(false);
                    await AssertServiceFailureAsync(async () =>
                    {
                        await labels.AddAttributeAsync("owner", "stale", currentEpoch + 1, ct).ConfigureAwait(false);
                    }, StatusCodes.BadInvalidState).ConfigureAwait(false);

                    ResourceSnapshot exactAfter = await ReadResourceSnapshotAsync(native, exactId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalAfter = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaAfter = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    labelValue = await native.ReadPropertiesAsync(labelsId, [native.Name("owner")], ct)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(exactAfter, Is.EqualTo(exactLabel));
                        Assert.That(logicalAfter, Is.EqualTo(logicalLabel));
                        Assert.That(metaAfter, Is.EqualTo(metaLabel));
                        Assert.That(DecodeString(labelValue[0]), Is.EqualTo("plant-1"));
                    });
                    await AssertDocumentAsync(native, exactId, committed, ct).ConfigureAwait(false);
                    await AssertDocumentAsync(native, roles.LogicalId, committed, ct).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task NativeDefaultSwitchEventsDescribeDelegatedVersionAttributesAsync()
        {
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                GroupTypeClient group = native.Client.GetGroup(groupId);
                (NodeId firstId, _, _) = await group.CreateResourceAsync("pump", "v1", false, ct)
                    .ConfigureAwait(false);
                ByteString document = ByteString.From(1, 2, 3, 4, 5);
                await CommitDocumentAsync(native.Client.GetResource(firstId), document, ct).ConfigureAwait(false);
                ResourceRoles roles = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
                NodeId labelsId = await ReadLabelsIdAsync(native, firstId, "Labels", ct).ConfigureAwait(false);
                var labels = new AttributesTypeClient(native.Session, labelsId, native.Telemetry);
                await labels.AddAttributeAsync("owner", "old-default", 0, ct).ConfigureAwait(false);
                ResourceSnapshot first = await ReadResourceSnapshotAsync(native, firstId, ct).ConfigureAwait(false);
                ResourceSnapshot logicalBefore = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                MetaSnapshot metaBefore = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                    .ConfigureAwait(false);
                ArrayOf<DataValue> originalFormat = await native.ReadPropertiesAsync(
                    roles.LogicalId, [native.Name("Format"), native.Name("ContentType")], ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(first.Epoch, Is.EqualTo(3u));
                    Assert.That(first.Size, Is.EqualTo(5ul));
                    Assert.That(logicalBefore.VersionId, Is.EqualTo("v1"));
                    Assert.That(metaBefore.Epoch, Is.EqualTo(1u));
                    Assert.That(DecodeString(originalFormat[0]), Is.EqualTo("avro"));
                    Assert.That(DecodeString(originalFormat[1]), Is.Null);
                });
                foreach (NodeId id in new[] { first.LabelsId, logicalBefore.LabelsId })
                {
                    ArrayOf<DataValue> values = await native.ReadPropertiesAsync(id, [native.Name("owner")], ct)
                        .ConfigureAwait(false);
                    Assert.That(DecodeString(values[0]), Is.EqualTo("old-default"));
                }

                var updates = new NativeUpdates(native);
                await using (updates.ConfigureAwait(false))
                {
                    await updates.StartAsync(ct).ConfigureAwait(false);
                    (NodeId secondId, _, _) = await group.CreateResourceAsync("pump", "v2", false, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot second = await ReadResourceSnapshotAsync(native, secondId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalSelected = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaSelected = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    AssertDefaultProjection(logicalSelected, second);
                    Assert.Multiple(() =>
                    {
                        Assert.That(second.VersionId, Is.EqualTo("v2"));
                        Assert.That(second.Epoch, Is.EqualTo(1u));
                        Assert.That(second.Size, Is.Zero);
                        Assert.That(logicalSelected.LabelsId, Is.EqualTo(logicalBefore.LabelsId));
                        Assert.That(metaSelected.Epoch, Is.EqualTo(metaBefore.Epoch + 1));
                        Assert.That(metaSelected.CreatedAt, Is.EqualTo(metaBefore.CreatedAt));
                        Assert.That(metaSelected.LabelsId, Is.EqualTo(metaBefore.LabelsId));
                    });
                    await AssertEmptyLabelsAsync(native, logicalSelected.LabelsId, ct).ConfigureAwait(false);
                    ArrayOf<DataValue> selectedFormat = await native.ReadPropertiesAsync(
                        roles.LogicalId, [native.Name("Format"), native.Name("ContentType")], ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(DecodeString(selectedFormat[0]), Is.Null);
                        Assert.That(DecodeString(selectedFormat[1]), Is.Null);
                    });
                    ResourceUpdatedEventTypeRecord creation = await updates.WaitForMetaAsync(metaSelected.Epoch, ct)
                        .ConfigureAwait(false);
                    (NodeId selectedId, string assignedId, uint handle, bool created) = await group
                        .GetOrCreateResourceAsync("pump", string.Empty, false, ct).ConfigureAwait(false);
                    MetaSnapshot metaAfterLookup = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(selectedId, Is.EqualTo(secondId));
                        Assert.That(assignedId, Is.EqualTo("v2"));
                        Assert.That(handle, Is.Zero);
                        Assert.That(created, Is.False);
                        Assert.That(metaAfterLookup, Is.EqualTo(metaSelected));
                    });

                    await native.Client.GetResource(secondId).DeleteAsync(second.Epoch, ct).ConfigureAwait(false);
                    ResourceUpdatedEventTypeRecord deletion = await updates.WaitForMetaAsync(
                        metaBefore.Epoch + 2, ct).ConfigureAwait(false);
                    ResourceSnapshot survivor = await ReadResourceSnapshotAsync(native, firstId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalRestored = await ReadResourceSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaRestored = await ReadMetaSnapshotAsync(native, roles.LogicalId, ct)
                        .ConfigureAwait(false);
                    AssertDefaultProjection(logicalRestored, first);
                    ArrayOf<DataValue> restoredLabel = await native.ReadPropertiesAsync(
                        logicalRestored.LabelsId, [native.Name("owner")], ct).ConfigureAwait(false);
                    ArrayOf<DataValue> restoredFormat = await native.ReadPropertiesAsync(
                        roles.LogicalId, [native.Name("Format")], ct).ConfigureAwait(false);
                    await AssertDocumentAsync(native, roles.LogicalId, document, ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(survivor, Is.EqualTo(first));
                        Assert.That(DecodeString(restoredLabel[0]), Is.EqualTo("old-default"));
                        Assert.That(DecodeString(restoredFormat[0]), Is.EqualTo("avro"));
                        Assert.That(metaRestored.Epoch, Is.EqualTo(metaBefore.Epoch + 2));
                        Assert.That(metaRestored.CreatedAt, Is.EqualTo(metaBefore.CreatedAt));
                        Assert.That(metaRestored.LabelsId, Is.EqualTo(metaBefore.LabelsId));
                        Assert.That(updates.ResourceUpdates.Count, Is.EqualTo(2),
                            "Default deletion is an ordered barrier after the no-op lookup.");
                        Assert.That(updates.VersionUpdates, Is.Empty);
                        Assert.That(updates.ResourceUpdates.ToList().Select(evt => evt.MetaEpoch),
                            Is.EqualTo(new[] { metaBefore.Epoch + 1, metaBefore.Epoch + 2 }));
                        Assert.That(creation.Epoch, Is.EqualTo(1u));
                        Assert.That(deletion.Epoch, Is.EqualTo(3u));
                        Assert.That(deletion.Time, Is.GreaterThanOrEqualTo(creation.Time!.Value));
                        foreach (ResourceUpdatedEventTypeRecord evt in new[] { creation, deletion })
                        {
                            Assert.That(evt.SourceNode, Is.EqualTo(roles.LogicalId));
                            Assert.That(evt.SourceUrl, Is.EqualTo(kEventSourceUrl));
                            Assert.That(evt.Subject, Is.EqualTo(kLogicalXid));
                            Assert.That(evt.EventType, Is.EqualTo(new NodeId(63018u, native.NamespaceIndex)));
                            Assert.That(evt.Time, Is.GreaterThan(DateTime.MinValue));
                            Assert.That(evt.Changed, Is.EqualTo(s_defaultSwitchChanged));
                        }
                    });
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeInitialVersionTimestampsUseOneInstantAsync(bool idempotentFirst)
        {
            using var output = new StringWriter(m_testOutput, CultureInfo.CurrentCulture);
            var native = new NativeFixture();
            await using (native.ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                CancellationToken ct = timeout.Token;
                await native.StartAsync(ct).ConfigureAwait(false);
                output.WriteLine(
                    $"Runtime: {Environment.Version}; ServerGC: {System.Runtime.GCSettings.IsServerGC}");
                NodeId groupId = await native.Client.GetRegistry(native.Client.RegistryNodeId)
                    .CreateGroupAsync("schemas", ct).ConfigureAwait(false);
                GroupTypeClient group = native.Client.GetGroup(groupId);
                var initialVersions = new List<ResourceSnapshot>();
                for (int index = 0; index < 3; index++)
                {
                    string resourceId = $"clock{index}";
                    NodeId firstId;
                    if (idempotentFirst)
                    {
                        (NodeId exactId, string assignedId, uint handle, bool created) = await group
                            .GetOrCreateResourceAsync(resourceId, string.Empty, false, ct).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(exactId.IsNull, Is.False);
                            Assert.That(assignedId, Is.Not.Empty);
                            Assert.That(handle, Is.Zero);
                            Assert.That(created, Is.True);
                        });
                        firstId = exactId;
                    }
                    else
                    {
                        (NodeId exactId, string assignedId, uint handle) = await group
                            .CreateResourceAsync(resourceId, "v1", false, ct).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(exactId.IsNull, Is.False);
                            Assert.That(assignedId, Is.EqualTo("v1"));
                            Assert.That(handle, Is.Zero);
                        });
                        firstId = exactId;
                    }

                    ArrayOf<ReferenceDescription> resources = await native.BrowseAsync(
                        groupId, UaReferenceTypeIds.Organizes, NodeClass.Object, ct).ConfigureAwait(false);
                    NodeId logicalId = native.LocalNodeId(FindReference(resources, native.Name(resourceId)).NodeId);
                    ResourceSnapshot first = await ReadResourceSnapshotAsync(native, firstId, ct)
                        .ConfigureAwait(false);
                    initialVersions.Add(first);
                    ResourceSnapshot logicalFirst = await ReadResourceSnapshotAsync(native, logicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaFirst = await ReadMetaSnapshotAsync(native, logicalId, ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(logicalId, Is.Not.EqualTo(firstId));
                        Assert.That(logicalFirst.CreatedAt, Is.EqualTo(first.CreatedAt));
                        Assert.That(logicalFirst.ModifiedAt, Is.EqualTo(first.ModifiedAt));
                        Assert.That(metaFirst.Epoch, Is.EqualTo(1u));
                        Assert.That(metaFirst.ModifiedAt, Is.EqualTo(metaFirst.CreatedAt));
                    });

                    (NodeId secondId, string secondAssignedId, uint secondHandle) = await group
                        .CreateResourceAsync(resourceId, "v2", false, ct).ConfigureAwait(false);
                    ResourceSnapshot second = await ReadResourceSnapshotAsync(native, secondId, ct)
                        .ConfigureAwait(false);
                    initialVersions.Add(second);
                    ResourceSnapshot firstAfter = await ReadResourceSnapshotAsync(native, firstId, ct)
                        .ConfigureAwait(false);
                    ResourceSnapshot logicalSecond = await ReadResourceSnapshotAsync(native, logicalId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaSecond = await ReadMetaSnapshotAsync(native, logicalId, ct)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(secondId, Is.Not.EqualTo(firstId));
                        Assert.That(secondAssignedId, Is.EqualTo("v2"));
                        Assert.That(secondHandle, Is.Zero);
                        Assert.That(firstAfter, Is.EqualTo(first));
                        Assert.That(logicalSecond.CreatedAt, Is.EqualTo(second.CreatedAt));
                        Assert.That(logicalSecond.ModifiedAt, Is.EqualTo(second.ModifiedAt));
                        Assert.That(metaSecond.CreatedAt, Is.EqualTo(metaFirst.CreatedAt));
                        Assert.That(metaSecond.Epoch, Is.EqualTo(metaFirst.Epoch + 1));
                    });

                    (NodeId selectedId, string selectedVersion, uint selectedHandle, bool wasCreated) = await group
                        .GetOrCreateResourceAsync(resourceId, string.Empty, false, ct).ConfigureAwait(false);
                    ResourceSnapshot secondAfter = await ReadResourceSnapshotAsync(native, secondId, ct)
                        .ConfigureAwait(false);
                    MetaSnapshot metaAfterLookup = await ReadMetaSnapshotAsync(native, logicalId, ct)
                        .ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(selectedId, Is.EqualTo(secondId));
                        Assert.That(selectedVersion, Is.EqualTo("v2"));
                        Assert.That(selectedHandle, Is.Zero);
                        Assert.That(wasCreated, Is.False);
                        Assert.That(secondAfter, Is.EqualTo(second));
                        Assert.That(metaAfterLookup, Is.EqualTo(metaSecond));
                    });
                }

                foreach (ResourceSnapshot version in initialVersions)
                {
                    output.WriteLine(
                        $"{version.ResourceId}/{version.VersionId}: " +
                        $"CreatedAt={version.CreatedAt.Value}; ModifiedAt={version.ModifiedAt.Value}");
                }
                Assert.Multiple(() =>
                {
                    foreach (ResourceSnapshot version in initialVersions)
                    {
                        Assert.That(version.Epoch, Is.EqualTo(1u));
                        Assert.That(version.ModifiedAt, Is.EqualTo(version.CreatedAt),
                            $"{version.ResourceId}/{version.VersionId} must start at one exact commit instant.");
                    }
                });
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (m_testOutput.Length != 0 &&
                TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Passed)
            {
                TestContext.Out.Write(m_testOutput.ToString());
            }
            m_testOutput.Clear();
        }

        private static async Task CommitChunksAsync(
            ResourceTypeClient resource,
            ByteString document,
            CancellationToken ct)
        {
            uint handle = await resource.OpenAsync(6, ct).ConfigureAwait(false);
            await resource.WriteAsync(handle, ByteString.From(document.Span[..2]), ct).ConfigureAwait(false);
            await resource.WriteAsync(handle, ByteString.From(document.Span[2..]), ct).ConfigureAwait(false);
            await resource.CloseAsync(handle, ct).ConfigureAwait(false);
        }

        private static async Task AssertServiceFailureAsync(Func<Task> action, StatusCode expected)
        {
            await Assert.ThatAsync(
                action,
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(expected))
                .ConfigureAwait(false);
        }

        private static async Task AssertReadAsync(
            ResourceTypeClient resource,
            uint handle,
            int length,
            ByteString expected,
            ulong position,
            CancellationToken ct)
        {
            ByteString bytes = await resource.ReadAsync(handle, length, ct).ConfigureAwait(false);
            ulong actualPosition = await resource.GetPositionAsync(handle, ct).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(bytes, Is.EqualTo(expected));
                Assert.That(actualPosition, Is.EqualTo(position));
            });
        }

        private static async Task CloseSessionAsync(ISession session)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(kOperationTimeout));
            StatusCode status = await session.CloseAsync(kOperationTimeout, true, cleanup.Token).ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(StatusCodes.Good));
        }

        private static async Task AssertWireContractsAsync(NativeFixture native, CancellationToken ct)
        {
            ArrayOf<WireArgument> resourceInputs =
            [
                new("ResourceId", UaDataTypeIds.String),
                new("VersionId", UaDataTypeIds.String),
                new("RequestFileOpen", UaDataTypeIds.Boolean)
            ];
            ArrayOf<WireArgument> resourceOutputs =
            [
                new("ResourceNodeId", UaDataTypeIds.NodeId),
                new("AssignedVersionId", UaDataTypeIds.String),
                new("FileHandle", UaDataTypeIds.UInt32)
            ];
            ArrayOf<MethodContract> contracts =
            [
                new(
                    XRegistryMethodIds.RegistryType_CreateGroup,
                    63521u,
                    [new("GroupId", UaDataTypeIds.String)],
                    [new("GroupNodeId", UaDataTypeIds.NodeId)]),
                new(
                    XRegistryMethodIds.RegistryType_GetOrCreateGroup,
                    63524u,
                    [new("GroupId", UaDataTypeIds.String)],
                    [new("GroupNodeId", UaDataTypeIds.NodeId), new("Created", UaDataTypeIds.Boolean)]),
                new(XRegistryMethodIds.GroupType_CreateResource, 63537u, resourceInputs, resourceOutputs),
                new(
                    XRegistryMethodIds.GroupType_GetOrCreateResource,
                    63540u,
                    resourceInputs,
                    [.. resourceOutputs, new("Created", UaDataTypeIds.Boolean)]),
                new(
                    XRegistryMethodIds.ResourceType_Delete,
                    63559u,
                    [new("ExpectedEpoch", UaDataTypeIds.UInt32)],
                    []),
                new(
                    XRegistryMethodIds.AttributesType_AddAttribute,
                    63501u,
                    [
                        new("Key", UaDataTypeIds.String),
                        new("Value", UaDataTypeIds.String),
                        new("ExpectedEpoch", UaDataTypeIds.UInt32)
                    ],
                    []),
                new(
                    XRegistryMethodIds.AttributesType_RemoveAttribute,
                    63503u,
                    [new("Key", UaDataTypeIds.String), new("ExpectedEpoch", UaDataTypeIds.UInt32)],
                    [])
            ];

            foreach (MethodContract contract in contracts.ToList())
            {
                NodeId methodId = native.LocalNodeId(contract.MethodId);
                Assert.That(methodId, Is.EqualTo(new NodeId(contract.NumericId, native.NamespaceIndex)));
                ArrayOf<ReferenceDescription> properties = await native
                    .BrowseAsync(methodId, UaReferenceTypeIds.HasProperty, NodeClass.Variable, ct)
                    .ConfigureAwait(false);
                await AssertArgumentsAsync(native, properties, "InputArguments", contract.Inputs, ct)
                    .ConfigureAwait(false);
                await AssertArgumentsAsync(native, properties, "OutputArguments", contract.Outputs, ct)
                    .ConfigureAwait(false);
            }
        }

        private static async Task AssertArgumentsAsync(
            NativeFixture native,
            ArrayOf<ReferenceDescription> properties,
            string propertyName,
            ArrayOf<WireArgument> expected,
            CancellationToken ct)
        {
            List<ReferenceDescription> matches = properties.ToList()
                .Where(reference => reference.BrowseName.Name == propertyName)
                .ToList();
            if (expected.Count == 0)
            {
                Assert.That(matches, Is.Empty, "This type method declares no OutputArguments property.");
                return;
            }

            Assert.That(matches, Has.Count.EqualTo(1), propertyName);
            ReferenceDescription property = matches[0];
            Assert.Multiple(() =>
            {
                Assert.That(property.BrowseName, Is.EqualTo(new QualifiedName(propertyName)));
                Assert.That(property.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(native.LocalNodeId(property.TypeDefinition), Is.EqualTo(UaVariableTypeIds.PropertyType));
            });
            ArrayOf<DataValue> values = await native.ReadValuesAsync(
                [new ReadValueId { NodeId = native.LocalNodeId(property.NodeId), AttributeId = Attributes.Value }],
                ct).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(values[0].WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
                Assert.That(values[0].WrappedValue.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            });
            Assert.That(
                values[0].WrappedValue.TryGetValue(out ArrayOf<Argument> arguments, native.Session.MessageContext),
                Is.True,
                propertyName + " must decode as Argument[].");
            Assert.That(arguments.Count, Is.EqualTo(expected.Count), propertyName);
            for (int index = 0; index < expected.Count; index++)
            {
                Argument argument = arguments[index];
                WireArgument expectedArgument = expected[index];
                Assert.Multiple(() =>
                {
                    Assert.That(argument.Name, Is.EqualTo(expectedArgument.Name));
                    Assert.That(argument.DataType, Is.EqualTo(expectedArgument.DataType));
                    Assert.That(argument.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                    Assert.That(argument.ArrayDimensions, Is.Empty);
                });
            }
        }

        private static async Task<ResourceRoles> BrowseRolesAsync(
            NativeFixture native,
            NodeId groupId,
            CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> resources = await native
                .BrowseAsync(groupId, UaReferenceTypeIds.Organizes, NodeClass.Object, ct)
                .ConfigureAwait(false);
            Assert.That(
                resources,
                Has.Count.EqualTo(1),
                "The group must organize one logical Resource, not its Versions.");
            ReferenceDescription logical = FindReference(resources, native.Name("pump"));
            AssertObjectReference(native, logical, XRegistryObjectTypeIds.ResourceType, 63002u);
            NodeId logicalId = native.LocalNodeId(logical.NodeId);
            ArrayOf<ReferenceDescription> components = await native
                .BrowseAsync(logicalId, UaReferenceTypeIds.HasComponent, NodeClass.Object, ct)
                .ConfigureAwait(false);
            ReferenceDescription versions = FindReference(components, native.Name("Versions"));
            AssertObjectReference(native, versions, XRegistryObjectTypeIds.ResourceVersionsType, 63025u);
            NodeId versionsId = native.LocalNodeId(versions.NodeId);
            ArrayOf<ReferenceDescription> exactVersions = await native
                .BrowseAsync(versionsId, UaReferenceTypeIds.Organizes, NodeClass.Object, ct)
                .ConfigureAwait(false);
            Assert.That(logicalId, Is.Not.EqualTo(versionsId));
            return new ResourceRoles(logicalId, versionsId, exactVersions);
        }

        private static async Task AssertStableRolesAsync(
            NativeFixture native,
            NodeId groupId,
            ResourceRoles original,
            ArrayOf<(string VersionId, NodeId NodeId)> expectedVersions,
            CancellationToken ct)
        {
            ResourceRoles current = await BrowseRolesAsync(native, groupId, ct).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(current.LogicalId, Is.EqualTo(original.LogicalId));
                Assert.That(current.VersionsId, Is.EqualTo(original.VersionsId));
            });
            AssertVersionSet(native, current, expectedVersions);
        }

        private static void AssertVersionSet(
            NativeFixture native,
            ResourceRoles roles,
            ArrayOf<(string VersionId, NodeId NodeId)> expected)
        {
            Assert.That(roles.Versions.Count, Is.EqualTo(expected.Count));
            Assert.That(
                roles.Versions.ToList().Select(reference => reference.BrowseName.Name),
                Is.EquivalentTo(expected.ToList().Select(version => version.VersionId)));
            var identities = new List<NodeId> { roles.LogicalId, roles.VersionsId };
            foreach ((string versionId, NodeId exactId) in expected)
            {
                ReferenceDescription version = FindReference(roles.Versions, native.Name(versionId));
                AssertObjectReference(native, version, XRegistryObjectTypeIds.ResourceType, 63002u);
                NodeId browsedId = native.LocalNodeId(version.NodeId);
                Assert.That(browsedId, Is.EqualTo(exactId));
                identities.Add(browsedId);
            }
            Assert.That(
                identities,
                Is.Unique,
                "Logical Resource, Versions container and exact Versions are separate nodes.");
        }

        private static void AssertObjectReference(
            NativeFixture native,
            ReferenceDescription reference,
            ExpandedNodeId generatedTypeId,
            uint numericTypeId)
        {
            NodeId typeId = native.LocalNodeId(generatedTypeId);
            Assert.Multiple(() =>
            {
                Assert.That(reference.IsForward, Is.True);
                Assert.That(reference.NodeClass, Is.EqualTo(NodeClass.Object));
                Assert.That(reference.BrowseName.NamespaceIndex, Is.EqualTo(native.NamespaceIndex));
                Assert.That(typeId, Is.EqualTo(new NodeId(numericTypeId, native.NamespaceIndex)));
                Assert.That(native.LocalNodeId(reference.TypeDefinition), Is.EqualTo(typeId));
            });
        }

        private static ReferenceDescription FindReference(
            ArrayOf<ReferenceDescription> references,
            QualifiedName browseName)
        {
            List<ReferenceDescription> matches = references.ToList()
                .Where(reference => reference.BrowseName.Name == browseName.Name)
                .ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"Expected one native child named {browseName}.");
            Assert.That(matches[0].BrowseName, Is.EqualTo(browseName));
            return matches[0];
        }

        private static async Task<ResourceSnapshot> ReadResourceSnapshotAsync(
            NativeFixture native,
            NodeId resourceId,
            CancellationToken ct)
        {
            ArrayOf<DataValue> values = await native.ReadPropertiesAsync(
                resourceId,
                [
                    native.Name("ResourceId"),
                    native.Name("VersionId"),
                    native.Name("Xid"),
                    native.Name("Epoch"),
                    native.Name("CreatedAt"),
                    native.Name("ModifiedAt"),
                    new QualifiedName("Size")
                ],
                ct).ConfigureAwait(false);
            NodeId labelsId = await ReadLabelsIdAsync(native, resourceId, "Labels", ct).ConfigureAwait(false);
            var snapshot = new ResourceSnapshot(
                DecodeString(values[0]),
                DecodeString(values[1]),
                DecodeString(values[2]),
                DecodeUInt32(values[3]),
                DecodeDateTime(values[4]),
                DecodeDateTime(values[5]),
                DecodeUInt64(values[6]),
                labelsId);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Epoch, Is.GreaterThan(0u));
                Assert.That(snapshot.CreatedAt, Is.GreaterThan(DateTimeUtc.MinValue));
                Assert.That(snapshot.ModifiedAt, Is.GreaterThanOrEqualTo(snapshot.CreatedAt));
            });
            return snapshot;
        }

        private static async Task<MetaSnapshot> ReadMetaSnapshotAsync(
            NativeFixture native,
            NodeId logicalId,
            CancellationToken ct)
        {
            ArrayOf<DataValue> values = await native.ReadPropertiesAsync(
                logicalId,
                [native.Name("MetaEpoch"), native.Name("MetaCreatedAt"), native.Name("MetaModifiedAt")],
                ct).ConfigureAwait(false);
            NodeId labelsId = await ReadLabelsIdAsync(native, logicalId, "MetaLabels", ct).ConfigureAwait(false);
            var snapshot = new MetaSnapshot(
                DecodeUInt32(values[0]),
                DecodeDateTime(values[1]),
                DecodeDateTime(values[2]),
                labelsId);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Epoch, Is.GreaterThan(0u));
                Assert.That(snapshot.CreatedAt, Is.GreaterThan(DateTimeUtc.MinValue));
                Assert.That(snapshot.ModifiedAt, Is.GreaterThanOrEqualTo(snapshot.CreatedAt));
            });
            return snapshot;
        }

        private static async Task<NodeId> ReadLabelsIdAsync(
            NativeFixture native,
            NodeId resourceId,
            string labelsName,
            CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> components = await native
                .BrowseAsync(resourceId, UaReferenceTypeIds.HasComponent, NodeClass.Object, ct)
                .ConfigureAwait(false);
            ReferenceDescription labels = FindReference(components, native.Name(labelsName));
            AssertObjectReference(native, labels, XRegistryObjectTypeIds.AttributesType, 63003u);
            return native.LocalNodeId(labels.NodeId);
        }

        private static async Task AssertEmptyLabelsAsync(NativeFixture native, NodeId labelsId, CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> labels = await native
                .BrowseAsync(labelsId, UaReferenceTypeIds.HasProperty, NodeClass.Variable, ct)
                .ConfigureAwait(false);
            Assert.That(labels, Is.Empty, "Creating or committing a Version must not invent labels.");
        }

        private static async Task AssertNoVersionsComponentAsync(
            NativeFixture native,
            NodeId exactId,
            CancellationToken ct)
        {
            ArrayOf<ReferenceDescription> components = await native
                .BrowseAsync(exactId, UaReferenceTypeIds.HasComponent, NodeClass.Object, ct)
                .ConfigureAwait(false);
            Assert.That(
                components.ToList().Select(reference => reference.BrowseName.Name),
                Does.Not.Contain("Versions"),
                "Only a logical Resource owns a Versions container.");
        }

        private static void AssertResourceIdentity(ResourceSnapshot snapshot, string versionId, string xid)
        {
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ResourceId, Is.EqualTo("pump"));
                Assert.That(snapshot.VersionId, Is.EqualTo(versionId));
                Assert.That(snapshot.Xid, Is.EqualTo(xid));
            });
        }

        private static void AssertDefaultProjection(ResourceSnapshot logical, ResourceSnapshot exact)
        {
            Assert.Multiple(() =>
            {
                Assert.That(logical.VersionId, Is.EqualTo(exact.VersionId));
                Assert.That(logical.Epoch, Is.EqualTo(exact.Epoch));
                Assert.That(logical.Size, Is.EqualTo(exact.Size));
            });
        }

        private static async Task CommitDocumentAsync(
            ResourceTypeClient resource,
            ByteString document,
            CancellationToken ct)
        {
            uint handle = await resource.OpenAsync(2, ct).ConfigureAwait(false);
            await resource.WriteDocumentAsync(handle, document, 2, ct).ConfigureAwait(false);
        }

        private static async Task AssertDocumentAsync(
            NativeFixture native,
            NodeId resourceId,
            ByteString expected,
            CancellationToken ct)
        {
            ByteString actual = await native.Client.GetResource(resourceId)
                .ReadDocumentAsync(2, ct)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(actual.Length, Is.EqualTo(expected.Length));
                Assert.That(actual, Is.EqualTo(expected));
            });
        }

        private static async Task AssertAbsentAsync(
            NativeFixture native,
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct)
        {
            ReadResponse response = await native.Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                [
                    .. nodeIds.ToList().Select(nodeId =>
                        new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass })
                ],
                ct).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results.Count, Is.EqualTo(nodeIds.Count));
            for (int index = 0; index < nodeIds.Count; index++)
            {
                DataValue value = response.Results[index];
                Assert.Multiple(() =>
                {
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown), $"{nodeIds[index]}");
                    Assert.That(value.WrappedValue.IsNull, Is.True);
                });
            }
        }

        private static string DecodeString(in DataValue value)
        {
            AssertScalar(value.WrappedValue, BuiltInType.String);
            Assert.That(value.WrappedValue.TryGetValue(out string result), Is.True);
            return result;
        }

        private static uint DecodeUInt32(in DataValue value)
        {
            AssertScalar(value.WrappedValue, BuiltInType.UInt32);
            Assert.That(value.WrappedValue.TryGetValue(out uint result), Is.True);
            return result;
        }

        private static ulong DecodeUInt64(in DataValue value)
        {
            AssertScalar(value.WrappedValue, BuiltInType.UInt64);
            Assert.That(value.WrappedValue.TryGetValue(out ulong result), Is.True);
            return result;
        }

        private static DateTimeUtc DecodeDateTime(in DataValue value)
        {
            AssertScalar(value.WrappedValue, BuiltInType.DateTime);
            Assert.That(value.WrappedValue.TryGetValue(out DateTimeUtc result), Is.True);
            return result;
        }

        private static void AssertScalar(Variant value, BuiltInType builtInType)
        {
            Assert.Multiple(() =>
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
                Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            });
        }

        private sealed record ResourceRoles(
            NodeId LogicalId,
            NodeId VersionsId,
            ArrayOf<ReferenceDescription> Versions);

        private sealed record ResourceSnapshot(
            string ResourceId,
            string VersionId,
            string Xid,
            uint Epoch,
            DateTimeUtc CreatedAt,
            DateTimeUtc ModifiedAt,
            ulong Size,
            NodeId LabelsId);

        private sealed record MetaSnapshot(
            uint Epoch,
            DateTimeUtc CreatedAt,
            DateTimeUtc ModifiedAt,
            NodeId LabelsId);

        private readonly record struct WireArgument(string Name, NodeId DataType);

        private sealed record MethodContract(
            ExpandedNodeId MethodId,
            uint NumericId,
            ArrayOf<WireArgument> Inputs,
            ArrayOf<WireArgument> Outputs);

        private sealed class NativeUpdates(NativeFixture native) : IAsyncDisposable
        {
            public ArrayOf<ResourceUpdatedEventTypeRecord> ResourceUpdates => [.. m_resourceUpdates];

            public ArrayOf<VersionUpdatedEventTypeRecord> VersionUpdates => [.. m_versionUpdates];

            public async Task StartAsync(CancellationToken ct)
            {
                CreateSubscriptionResponse subscription = await native.Session.CreateSubscriptionAsync(
                    null, 100, 1200, 20, 0, true, 0, ct).ConfigureAwait(false);
                Assert.That(subscription.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                m_subscriptionId = subscription.SubscriptionId;
                var decoders = new EventRecordDecoderRegistry()
                    .RegisterxRegistryDecoders(native.Session.NamespaceUris);
                EventFilter resourceFilter = ResourceUpdatedEventTypeRecord.EventFilters.Build(
                    native.Session.NamespaceUris, decoders);
                EventFilter versionFilter = VersionUpdatedEventTypeRecord.EventFilters.Build(
                    native.Session.NamespaceUris, decoders);
                CreateMonitoredItemsResponse created = await native.Session.CreateMonitoredItemsAsync(
                    null,
                    m_subscriptionId,
                    TimestampsToReturn.Neither,
                    [
                        MonitoredItem(native.Client.RegistryNodeId, 1, resourceFilter),
                        MonitoredItem(Ua.ObjectIds.Server, 2, versionFilter)
                    ],
                    ct).ConfigureAwait(false);
                Assert.That(created.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(created.Results.Count, Is.EqualTo(2));
                foreach (MonitoredItemCreateResult item in created.Results)
                {
                    Assert.That(item.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
            }

            public async Task WaitForPairAsync(uint epoch, CancellationToken ct)
            {
                while (!m_resourceUpdates.Any(evt => evt.Epoch == epoch) ||
                    !m_versionUpdates.Any(evt => evt.Epoch == epoch))
                {
                    await PublishAsync(ct).ConfigureAwait(false);
                }
            }

            public async Task<ResourceUpdatedEventTypeRecord> WaitForMetaAsync(uint metaEpoch, CancellationToken ct)
            {
                while (!m_resourceUpdates.Any(evt => evt.MetaEpoch == metaEpoch))
                {
                    await PublishAsync(ct).ConfigureAwait(false);
                }
                return m_resourceUpdates.Single(evt => evt.MetaEpoch == metaEpoch);
            }

            public async ValueTask DisposeAsync()
            {
                if (m_subscriptionId != 0)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(kOperationTimeout));
                    DeleteSubscriptionsResponse deleted = await native.Session.DeleteSubscriptionsAsync(
                        null, [m_subscriptionId], cleanup.Token).ConfigureAwait(false);
                    Assert.That(deleted.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(deleted.Results.Count, Is.EqualTo(1));
                    Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
                }
            }

            private async Task PublishAsync(CancellationToken ct)
            {
                PublishResponse response = await native.Session.PublishAsync(null, m_acknowledgements, ct)
                    .ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.SubscriptionId, Is.EqualTo(m_subscriptionId));
                foreach (StatusCode result in response.Results)
                {
                    Assert.That(result, Is.EqualTo(StatusCodes.Good));
                }
                m_acknowledgements = response.AvailableSequenceNumbers.ConvertAll(sequence =>
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = m_subscriptionId,
                        SequenceNumber = sequence
                    });
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    Assert.That(notification.TryGetValue(out EventNotificationList? events), Is.True);
                    foreach (EventFieldList evt in events!.Events)
                    {
                        if (evt.ClientHandle == 1)
                        {
                            ResourceUpdatedEventTypeRecord? decoded =
                                ResourceUpdatedEventTypeRecord.Decoder.Decode(evt.EventFields.Span.ToArray());
                            Assert.That(decoded, Is.Not.Null);
                            m_resourceUpdates.Add(decoded!);
                        }
                        else
                        {
                            Assert.That(evt.ClientHandle, Is.EqualTo(2u));
                            VersionUpdatedEventTypeRecord? decoded =
                                VersionUpdatedEventTypeRecord.Decoder.Decode(evt.EventFields.Span.ToArray());
                            Assert.That(decoded, Is.Not.Null);
                            m_versionUpdates.Add(decoded!);
                        }
                    }
                }
            }

            private static MonitoredItemCreateRequest MonitoredItem(NodeId source, uint handle, EventFilter filter)
            {
                return new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = source, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = handle,
                        SamplingInterval = 0,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter)
                    }
                };
            }

            private readonly List<ResourceUpdatedEventTypeRecord> m_resourceUpdates = [];
            private readonly List<VersionUpdatedEventTypeRecord> m_versionUpdates = [];
            private ArrayOf<SubscriptionAcknowledgement> m_acknowledgements;
            private uint m_subscriptionId;
        }

        private sealed class NativeFixture : IAsyncDisposable
        {
            public NativeFixture()
            {
                Telemetry = NUnitTelemetryContext.Create(true);
                m_services = new ServiceCollection()
                    .AddXRegistryContentIdProvider<XRegistryServerTestHarness.FakeContentIdProvider>()
                    .AddXRegistryServer(options =>
                    {
                        options.EventsEnabled = true;
                        options.EventSourceUrl = kEventSourceUrl;
                    })
                    .BuildServiceProvider();
                m_options = m_services.GetRequiredService<XRegistryServerOptions>();
                m_factory = new NativeNodeManagerFactory(m_options);
                m_server = new ServerFixture<NativeServer>(telemetry => new NativeServer(telemetry, m_factory))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    SecurityNone = false,
                    AutoAccept = true
                };
                m_clientApplication = new ApplicationInstance(Telemetry)
                {
                    ApplicationName = "RegistryRolesClient",
                    ApplicationType = ApplicationType.Client
                };
                m_pkiRoot = Path.Combine(Path.GetTempPath(), "ua-rg", Path.GetRandomFileName());
            }

            public ITelemetryContext Telemetry { get; }

            public ISession Session =>
                m_session ?? throw new InvalidOperationException("The native session is not open.");

            public GenericXRegistryClient Client =>
                m_client ?? throw new InvalidOperationException("The native client is not initialized.");

            public ushort NamespaceIndex => Client.NamespaceIndex;

            public async Task StartAsync(CancellationToken ct)
            {
                IXRegistryResourceStore store = m_services.GetRequiredService<IXRegistryResourceStore>();
                IResourceContentIdProvider contentProvider = m_services
                    .GetRequiredService<IResourceContentIdProvider>();
                Assert.Multiple(() =>
                {
                    Assert.That(m_services.GetRequiredService<XRegistryServerOptions>(), Is.SameAs(m_options));
                    Assert.That(m_services.GetRequiredService<IXRegistryResourceStore>(), Is.SameAs(store));
                    Assert.That(
                        m_services.GetRequiredService<IResourceContentIdProvider>(),
                        Is.SameAs(contentProvider));
                    Assert.That(m_options.ResourceStore, Is.SameAs(store));
                    Assert.That(m_options.ContentIdProvider, Is.SameAs(contentProvider));
                    Assert.That(contentProvider, Is.TypeOf<XRegistryServerTestHarness.FakeContentIdProvider>());
                    Assert.That(m_options.EventsEnabled, Is.True);
                    Assert.That(m_options.EventSourceUrl, Is.EqualTo(kEventSourceUrl));
                });

                await m_server.StartAsync(Path.Combine(m_pkiRoot, "s")).ConfigureAwait(false);
                Assert.That(m_factory.UsedOptions, Is.SameAs(m_options));
                string clientPkiRoot = Path.Combine(m_pkiRoot, "c");
                ArrayOf<CertificateIdentifier> certificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        "CN=RegistryRolesClient, O=OPC Foundation, DC=localhost",
                        CertificateStoreType.Directory,
                        clientPkiRoot);
                m_clientConfiguration = await m_clientApplication
                    .Build("urn:localhost:ua:RegistryRolesClient", "urn:ua:RegistryRolesClient")
                    .SetMaxByteStringLength(4 * 1024 * 1024)
                    .SetMaxArrayLength(1024 * 1024)
                    .AsClient()
                    .AddSecurityConfiguration(certificates, clientPkiRoot)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .SetRejectSHA1SignedCertificates(true)
                    .CreateAsync(ct)
                    .ConfigureAwait(false);
                bool hasCertificate = await m_clientApplication
                    .CheckApplicationInstanceCertificatesAsync(true, ct: ct)
                    .ConfigureAwait(false);
                Assert.That(
                    hasCertificate,
                    Is.True,
                    "The isolated client certificate store must contain a certificate.");

                ServerConfiguration serverConfiguration = m_server.Config?.ServerConfiguration
                    ?? throw new InvalidOperationException("The server configuration is not initialized.");
                var discoveryUrl = new Uri(serverConfiguration.BaseAddresses[0]);
                Assert.Multiple(() =>
                {
                    Assert.That(discoveryUrl.Scheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
                    Assert.That(discoveryUrl.IsLoopback, Is.True);
                    Assert.That(discoveryUrl.Port, Is.EqualTo(m_server.Port));
                    Assert.That(discoveryUrl.Port, Is.GreaterThan(0));
                });
                var endpointConfiguration = EndpointConfiguration.Create(m_clientConfiguration);
                endpointConfiguration.OperationTimeout = kOperationTimeout;
                using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                    m_clientConfiguration,
                    discoveryUrl,
                    endpointConfiguration,
                    ct: ct).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints;
                try
                {
                    endpoints = await discovery.GetEndpointsAsync(default, ct).ConfigureAwait(false);
                }
                finally
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(kOperationTimeout));
                    await discovery.CloseAsync(cleanup.Token).ConfigureAwait(false);
                }

                EndpointDescription selected = endpoints.ToList().Single(endpoint =>
                    endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
                m_endpoint = new ConfiguredEndpoint(null, selected, endpointConfiguration);
                m_session = await CreateSessionAsync(ct).ConfigureAwait(false);
                m_client = new GenericXRegistryClient(m_session, Telemetry);
                var connectedUrl = new Uri(m_session.ConfiguredEndpoint.Description.EndpointUrl
                    ?? throw new InvalidOperationException("The connected endpoint has no URL."));
                Assert.Multiple(() =>
                {
                    Assert.That(m_session.Connected, Is.True);
                    Assert.That(m_session.SessionId.IsNull, Is.False);
                    Assert.That(m_session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(m_session.ConfiguredEndpoint.Description.SecurityPolicyUri,
                        Is.EqualTo(SecurityPolicies.Basic256Sha256));
                    Assert.That(connectedUrl.Scheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
                    Assert.That(connectedUrl.IsLoopback, Is.True);
                    Assert.That(connectedUrl.Port, Is.EqualTo(m_server.Port));
                    Assert.That(m_session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
                    Assert.That(m_session.NamespaceUris.GetIndex(XRegistryWellKnown.XRegistryNamespaceUri),
                        Is.EqualTo((int)NamespaceIndex));
                    Assert.That(Client.RegistryNodeId, Is.EqualTo(new NodeId(65000u, NamespaceIndex)));
                });
            }

            /// <summary>
            /// Creates an additional caller-owned session using the same isolated client and identity.
            /// </summary>
            public async Task<ISession> CreateSessionAsync(CancellationToken ct)
            {
                ApplicationConfiguration configuration = m_clientConfiguration
                    ?? throw new InvalidOperationException("The client configuration is not initialized.");
                ConfiguredEndpoint endpoint = m_endpoint
                    ?? throw new InvalidOperationException("The encrypted endpoint has not been discovered.");
                var endpointUrl = new Uri(endpoint.Description.EndpointUrl
                    ?? throw new InvalidOperationException("The discovered endpoint has no URL."));
                Assert.Multiple(() =>
                {
                    Assert.That(endpointUrl.Scheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
                    Assert.That(endpointUrl.IsLoopback, Is.True);
                    Assert.That(endpointUrl.Port, Is.EqualTo(m_server.Port));
                    Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(endpoint.Description.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                });
                ISession session = await new DefaultSessionFactory(Telemetry).CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    true,
                    "RegistryRoles",
                    60000u,
                    m_identity,
                    default,
                    ct).ConfigureAwait(false);
                session.DeleteSubscriptionsOnClose = true;
                return session;
            }

            public QualifiedName Name(string name)
            {
                return new QualifiedName(name, NamespaceIndex);
            }

            public NodeId LocalNodeId(ExpandedNodeId expandedId)
            {
                Assert.That(expandedId.ServerIndex, Is.Zero, "Role references must remain on the local native server.");
                NodeId nodeId = ExpandedNodeId.ToNodeId(expandedId, Session.NamespaceUris);
                Assert.That(nodeId.IsNull, Is.False, $"The native namespace table must resolve {expandedId}.");
                return nodeId;
            }

            public async Task<ArrayOf<ReferenceDescription>> BrowseAsync(
                NodeId parentId,
                NodeId referenceTypeId,
                NodeClass nodeClass,
                CancellationToken ct)
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null,
                    new ViewDescription(),
                    2u,
                    [
                        new BrowseDescription
                        {
                            NodeId = parentId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = referenceTypeId,
                            IncludeSubtypes = false,
                            NodeClassMask = (uint)nodeClass,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    ct).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results, Has.Count.EqualTo(1));
                BrowseResult page = response.Results[0];
                ByteString continuationPoint = page.ContinuationPoint;
                var references = new List<ReferenceDescription>();
                try
                {
                    while (true)
                    {
                        Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good), $"Browse {parentId}.");
                        foreach (ReferenceDescription reference in page.References)
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(reference.IsForward, Is.True);
                                Assert.That(reference.ReferenceTypeId, Is.EqualTo(referenceTypeId));
                                Assert.That(reference.NodeClass, Is.EqualTo(nodeClass));
                            });
                            references.Add(reference);
                        }
                        if (continuationPoint.IsEmpty)
                        {
                            break;
                        }

                        BrowseNextResponse next = await Session
                            .BrowseNextAsync(null, false, [continuationPoint], ct)
                            .ConfigureAwait(false);
                        Assert.That(next.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                        Assert.That(next.Results, Has.Count.EqualTo(1));
                        page = next.Results[0];
                        continuationPoint = page.ContinuationPoint;
                    }
                }
                finally
                {
                    if (!continuationPoint.IsEmpty)
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(kOperationTimeout));
                        BrowseNextResponse released = await Session
                            .BrowseNextAsync(null, true, [continuationPoint], cleanup.Token)
                            .ConfigureAwait(false);
                        Assert.That(released.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                        foreach (BrowseResult result in released.Results)
                        {
                            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                        }
                    }
                }
                return [.. references];
            }

            public async Task<ArrayOf<DataValue>> ReadPropertiesAsync(
                NodeId parentId,
                ArrayOf<QualifiedName> names,
                CancellationToken ct)
            {
                ArrayOf<ReferenceDescription> properties = await BrowseAsync(
                    parentId,
                    UaReferenceTypeIds.HasProperty,
                    NodeClass.Variable,
                    ct).ConfigureAwait(false);
                ArrayOf<ReadValueId> requests =
                [
                    .. names.ToList().Select(name => new ReadValueId
                    {
                        NodeId = LocalNodeId(FindReference(properties, name).NodeId),
                        AttributeId = Attributes.Value
                    })
                ];
                return await ReadValuesAsync(requests, ct).ConfigureAwait(false);
            }

            public async Task<ArrayOf<DataValue>> ReadValuesAsync(
                ArrayOf<ReadValueId> requests,
                CancellationToken ct)
            {
                ReadResponse response = await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    requests,
                    ct).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results.Count, Is.EqualTo(requests.Count));
                for (int index = 0; index < requests.Count; index++)
                {
                    Assert.That(
                        response.Results[index].StatusCode,
                        Is.EqualTo(StatusCodes.Good),
                        $"Read {requests[index].NodeId}, attribute {requests[index].AttributeId}.");
                }
                return response.Results;
            }

            public async ValueTask DisposeAsync()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                try
                {
                    if (m_session != null)
                    {
                        try
                        {
                            if (m_session.Connected)
                            {
                                using var cleanup = new CancellationTokenSource(
                                    TimeSpan.FromMilliseconds(kOperationTimeout));
                                StatusCode status = await m_session
                                    .CloseAsync(kOperationTimeout, true, cleanup.Token)
                                    .ConfigureAwait(false);
                                Assert.That(status, Is.EqualTo(StatusCodes.Good));
                            }
                        }
                        finally
                        {
                            await m_session.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (m_server.Application != null)
                        {
                            await m_server.StopAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            await m_clientApplication.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            try
                            {
                                var certificateManager = m_clientConfiguration?.CertificateManager;
                                if (certificateManager is IAsyncDisposable asyncCertificateManager)
                                {
                                    await asyncCertificateManager.DisposeAsync().ConfigureAwait(false);
                                }
                                else if (certificateManager is IDisposable disposableCertificateManager)
                                {
                                    disposableCertificateManager.Dispose();
                                }
                            }
                            finally
                            {
                                try
                                {
                                    await m_services.DisposeAsync().ConfigureAwait(false);
                                }
                                finally
                                {
                                    if (Directory.Exists(m_pkiRoot))
                                    {
                                        Directory.Delete(m_pkiRoot, recursive: true);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private readonly ServiceProvider m_services;
            private readonly XRegistryServerOptions m_options;
            private readonly NativeNodeManagerFactory m_factory;
            private readonly ServerFixture<NativeServer> m_server;
            private readonly ApplicationInstance m_clientApplication;
            private readonly IUserIdentity m_identity = new UserIdentity(new AnonymousIdentityToken());
            private readonly string m_pkiRoot;
            private ApplicationConfiguration? m_clientConfiguration;
            private ConfiguredEndpoint? m_endpoint;
            private ISession? m_session;
            private GenericXRegistryClient? m_client;
            private bool m_disposed;
        }

        private sealed class NativeServer : ReferenceServer
        {
            public NativeServer(ITelemetryContext telemetry, IAsyncNodeManagerFactory factory)
                : base(telemetry)
            {
                AddNodeManager(factory);
            }
        }

        private sealed class NativeNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public NativeNodeManagerFactory(XRegistryServerOptions options)
            {
                Options = options ?? throw new ArgumentNullException(nameof(options));
            }

            public XRegistryServerOptions Options { get; }

            public XRegistryServerOptions? UsedOptions { get; private set; }

            public ArrayOf<string> NamespacesUris => [Options.RegistryNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UsedOptions = Options;
                return new ValueTask<IAsyncNodeManager>(
                    new XRegistryRegistrationNodeManager(server, configuration, Options));
            }
        }

        private readonly StringBuilder m_testOutput = new();
        private const string kLogicalXid = "/groups/schemas/resources/pump";
        private const string kEventSourceUrl = "https://registry.example.test";
        private const int kOperationTimeout = 10000;
        private static readonly string[] s_documentChanged = ["epoch", "modifiedat", "resource"];
        private static readonly string[] s_defaultSwitchChanged =
        [
            "createdat", "epoch", "format", "labels", "meta.defaultversionid", "meta.epoch",
            "meta.modifiedat", "modifiedat", "resource", "versionid", "versions", "versionscount"
        ];
    }
}
