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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    public sealed partial class XRegistryRegistrationEventTests
    {
        [TestCase("v7")]
        [TestCase("pump")]
        public async Task PublicCreationSeparatesLogicalResourceAndExactVersionRolesAsync(string firstVersion)
        {
            (XRegistryRegistrationNodeManager nodeManager, RegistryState registry, _) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager manager = nodeManager;
            CreateGroupMethodStateResult groupResult = await registry.CreateGroup!.OnCallAsync!(
                manager.SystemContext, registry.CreateGroup, registry.NodeId,
                "schemas", CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(groupResult.ServiceResult), Is.True);
            var group = (GroupState)manager.Find(groupResult.GroupNodeId)!;
            CreateResourceMethodStateResult first = await group.CreateResource!.OnCallAsync!(
                manager.SystemContext, group.CreateResource, group.NodeId,
                "pump", firstVersion, false, CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult second = await group.CreateResource.OnCallAsync!(
                manager.SystemContext, group.CreateResource, group.NodeId,
                "pump", "v8", false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);
            Assert.That(first.ResourceNodeId, Is.Not.EqualTo(second.ResourceNodeId));
            var firstNode = (ResourceState)manager.Find(first.ResourceNodeId)!;
            var secondNode = (ResourceState)manager.Find(second.ResourceNodeId)!;
            var children = new List<BaseInstanceState>();
            group.GetChildren(manager.SystemContext, children);
            ResourceState[] resources = children.OfType<ResourceState>().ToArray();

            Assert.That(resources, Has.Length.EqualTo(1),
                "A Group exposes one logical Resource, not one flattened child per Version.");
            ResourceState logical = resources[0];
            Assert.That(logical.NodeId, Is.Not.EqualTo(firstNode.NodeId).And.Not.EqualTo(secondNode.NodeId));
            Assert.That(logical.ResourceId!.Value, Is.EqualTo("pump"));
            Assert.That(logical.VersionId!.Value, Is.EqualTo("v8"));
            Assert.That(logical.Versions, Is.InstanceOf<ResourceVersionsState>());
            Assert.That(logical.Versions!.Parent, Is.SameAs(logical));
            Assert.That(firstNode.Parent, Is.SameAs(logical.Versions));
            Assert.That(secondNode.Parent, Is.SameAs(logical.Versions));
            Assert.That(firstNode.VersionId!.Value, Is.EqualTo(firstVersion));
            Assert.That(secondNode.VersionId!.Value, Is.EqualTo("v8"));

            DeleteMethodStateResult versionDeleted = await firstNode.Delete!.OnCallAsync!(
                manager.SystemContext, firstNode.Delete, firstNode.NodeId,
                firstNode.Epoch!.Value, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(versionDeleted.ServiceResult), Is.True);
            Assert.That(manager.Find(firstNode.NodeId), Is.Null);
            Assert.That(manager.Find(logical.NodeId), Is.SameAs(logical));
            Assert.That(manager.Find(secondNode.NodeId), Is.SameAs(secondNode));

            DeleteMethodStateResult resourceDeleted = await logical.Delete!.OnCallAsync!(
                manager.SystemContext, logical.Delete, logical.NodeId,
                logical.MetaEpoch!.Value, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(resourceDeleted.ServiceResult), Is.True);
            Assert.That(manager.Find(logical.NodeId), Is.Null);
            Assert.That(manager.Find(secondNode.NodeId), Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task LogicalLabelsUseDefaultVersionAndPreserveMetaOwnershipAsync(bool eventsEnabled)
        {
            (XRegistryRegistrationNodeManager nodeManager, RegistryState registry, _) =
                await CreateAddressSpaceAsync(eventsEnabled).ConfigureAwait(false);
            using XRegistryRegistrationNodeManager manager = nodeManager;
            var events = new List<BaseEventState>();
            registry.OnReportEvent = (_, _, target) =>
            {
                if (target is BaseEventState evt)
                {
                    events.Add(evt);
                }
            };
            CreateGroupMethodStateResult groupResult = await registry.CreateGroup!.OnCallAsync!(
                manager.SystemContext, registry.CreateGroup, registry.NodeId, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(groupResult.ServiceResult), Is.True);
            var group = (GroupState)manager.Find(groupResult.GroupNodeId)!;
            ResourceState first = await CreateVersionAsync(manager, group, "v1").ConfigureAwait(false);
            ResourceState second = await CreateVersionAsync(manager, group, "v2").ConfigureAwait(false);
            ResourceState logical = LogicalResourceOf(first);
            uint metaEpoch = logical.MetaEpoch!.Value;
            DateTimeUtc metaTime = logical.MetaModifiedAt!.Value;
            AddAttributeMethodStateResult firstLabel = await AddLabelAsync(
                manager, first.Labels!, "lane", "first", first.Epoch!.Value).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(firstLabel.ServiceResult), Is.True);
            DateTimeUtc firstTime = first.ModifiedAt!.Value;

            AddAttributeMethodStateResult defaultLabel = await AddLabelAsync(
                manager, logical.Labels!, "lane", "default", second.Epoch!.Value).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(defaultLabel.ServiceResult), Is.True);
                Assert.That(first.Epoch.Value, Is.EqualTo(2u));
                Assert.That(first.ModifiedAt.Value, Is.EqualTo(firstTime));
                Assert.That(second.Epoch.Value, Is.EqualTo(2u));
                Assert.That(logical.Epoch!.Value, Is.EqualTo(second.Epoch.Value));
                Assert.That(Label(manager.SystemContext, first.Labels!, "lane"), Is.EqualTo("first"));
                Assert.That(Label(manager.SystemContext, second.Labels!, "lane"), Is.EqualTo("default"));
                Assert.That(Label(manager.SystemContext, logical.Labels!, "lane"), Is.EqualTo("default"));
                Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch));
                Assert.That(logical.MetaModifiedAt.Value, Is.EqualTo(metaTime));
                Assert.That(logical.Epoch.ChangeMasks, Is.EqualTo(NodeStateChangeMasks.None));
            });
            DateTimeUtc versionTime = second.ModifiedAt!.Value;
            events.Clear();
            AddAttributeMethodStateResult repeated = await AddLabelAsync(
                manager, logical.Labels!, "lane", "default", second.Epoch.Value).ConfigureAwait(false);
            AddAttributeMethodStateResult stale = await AddLabelAsync(
                manager, logical.Labels!, "lane", "stale", second.Epoch.Value + 1).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(repeated.ServiceResult), Is.True);
                Assert.That(stale.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(second.Epoch.Value, Is.EqualTo(2u));
                Assert.That(second.ModifiedAt.Value, Is.EqualTo(versionTime));
                Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch));
                Assert.That(logical.MetaModifiedAt.Value, Is.EqualTo(metaTime));
                Assert.That(events, Is.Empty);
            });

            AddAttributeMethodStateResult addedMeta = await AddLabelAsync(
                manager, logical.MetaLabels!, "owner", "plant-1", metaEpoch).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(addedMeta.ServiceResult), Is.True);
            Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch + 1));
            DateTimeUtc changedMetaTime = logical.MetaModifiedAt.Value;
            events.Clear();
            AddAttributeMethodStateResult repeatedMeta = await AddLabelAsync(
                manager, logical.MetaLabels!, "owner", "plant-1", logical.MetaEpoch.Value).ConfigureAwait(false);
            RemoveAttributeMethodStateResult missingMeta = await RemoveLabelAsync(
                manager, logical.MetaLabels!, "absent", logical.MetaEpoch.Value).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(repeatedMeta.ServiceResult), Is.True);
                Assert.That(missingMeta.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(first.MetaEpoch!.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(second.MetaEpoch!.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(logical.MetaModifiedAt.Value, Is.EqualTo(changedMetaTime));
                Assert.That(first.Epoch.Value, Is.EqualTo(2u));
                Assert.That(second.Epoch.Value, Is.EqualTo(2u));
                Assert.That(second.ModifiedAt.Value, Is.EqualTo(versionTime));
                Assert.That(Label(manager.SystemContext, first.MetaLabels!, "owner"), Is.EqualTo("plant-1"));
                Assert.That(Label(manager.SystemContext, second.MetaLabels!, "owner"), Is.EqualTo("plant-1"));
                Assert.That(events, Is.Empty);
            });

            RemoveAttributeMethodStateResult removed = await RemoveLabelAsync(
                manager, logical.Labels!, "lane", second.Epoch.Value).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(removed.ServiceResult), Is.True);
                Assert.That(first.Epoch.Value, Is.EqualTo(2u));
                Assert.That(first.ModifiedAt.Value, Is.EqualTo(firstTime));
                Assert.That(second.Epoch.Value, Is.EqualTo(3u));
                Assert.That(logical.Epoch!.Value, Is.EqualTo(3u));
                Assert.That(Label(manager.SystemContext, first.Labels!, "lane"), Is.EqualTo("first"));
                Assert.That(Label(manager.SystemContext, second.Labels!, "lane"), Is.Null);
                Assert.That(Label(manager.SystemContext, logical.Labels!, "lane"), Is.Null);
                Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(logical.MetaModifiedAt.Value, Is.EqualTo(changedMetaTime));
                Assert.That(events.Select(evt => evt.GetType()), eventsEnabled
                    ? Is.EquivalentTo(new[] { typeof(VersionUpdatedEventState), typeof(ResourceUpdatedEventState) })
                    : Is.Empty);
            });
            events.Clear();
            DateTimeUtc afterRemoval = second.ModifiedAt.Value;
            RemoveAttributeMethodStateResult removedMeta = await RemoveLabelAsync(
                manager, logical.MetaLabels!, "owner", logical.MetaEpoch.Value).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(removedMeta.ServiceResult), Is.True);
                Assert.That(logical.MetaEpoch.Value, Is.EqualTo(metaEpoch + 2));
                Assert.That(first.Epoch.Value, Is.EqualTo(2u));
                Assert.That(second.Epoch.Value, Is.EqualTo(3u));
                Assert.That(second.ModifiedAt.Value, Is.EqualTo(afterRemoval));
                Assert.That(Label(manager.SystemContext, first.MetaLabels!, "owner"), Is.Null);
                Assert.That(Label(manager.SystemContext, logical.MetaLabels!, "owner"), Is.Null);
                Assert.That(events.Select(evt => evt.GetType()), eventsEnabled
                    ? Is.EquivalentTo(new[] { typeof(ResourceUpdatedEventState) })
                    : Is.Empty);
            });
        }

        private static async Task<ResourceState> CreateVersionAsync(
            XRegistryRegistrationNodeManager manager,
            GroupState group,
            string versionId)
        {
            CreateResourceMethodStateResult created = await group.CreateResource!.OnCallAsync!(
                manager.SystemContext, group.CreateResource, group.NodeId,
                "pump", versionId, false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);
            return (ResourceState)manager.Find(created.ResourceNodeId)!;
        }

        private static ValueTask<AddAttributeMethodStateResult> AddLabelAsync(
            XRegistryRegistrationNodeManager manager,
            AttributesState labels,
            string key,
            string value,
            uint epoch)
        {
            return labels.AddAttribute!.OnCallAsync!(
                manager.SystemContext, labels.AddAttribute, labels.NodeId, key, value, epoch, CancellationToken.None);
        }

        private static ValueTask<RemoveAttributeMethodStateResult> RemoveLabelAsync(
            XRegistryRegistrationNodeManager manager,
            AttributesState labels,
            string key,
            uint epoch)
        {
            return labels.RemoveAttribute!.OnCallAsync!(
                manager.SystemContext, labels.RemoveAttribute, labels.NodeId, key, epoch, CancellationToken.None);
        }

        private static ResourceState LogicalResourceOf(ResourceState version)
        {
            Assert.That(version.Parent, Is.TypeOf<ResourceVersionsState>());
            var versions = (ResourceVersionsState)version.Parent!;
            Assert.That(versions.Parent, Is.TypeOf<ResourceState>());
            var logical = (ResourceState)versions.Parent!;
            Assert.That(logical.Versions, Is.SameAs(versions));
            Assert.That(logical.NodeId, Is.Not.EqualTo(version.NodeId));
            return logical;
        }
    }
}
