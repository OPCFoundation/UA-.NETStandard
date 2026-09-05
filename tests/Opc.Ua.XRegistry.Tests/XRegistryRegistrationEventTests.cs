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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class XRegistryRegistrationEventTests
    {
        [Test]
        public async Task RecursiveGroupDeleteReportsLeavesBeforeContainersAsync()
        {
            using XRegistryRegistrationNodeManager manager = CreateAddressSpace(
                out RegistryState registry,
                out _);
            var events = new List<BaseEventState>();
            registry.OnReportEvent = (_, _, target) =>
            {
                if (target is BaseEventState evt)
                {
                    events.Add(evt);
                }
            };
            CreateGroupMethodStateResult groupResult = await manager.OnCreateGroupAsync(
                manager.SystemContext,
                null!,
                registry.NodeId,
                "schemas",
                CancellationToken.None).ConfigureAwait(false);
            var group = (GroupState)manager.Find(groupResult.GroupNodeId)!;
            CreateResourceMethodStateResult first = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v1",
                false,
                CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult second = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v2",
                false,
                CancellationToken.None).ConfigureAwait(false);
            var v1 = (ResourceState)manager.Find(first.ResourceNodeId)!;
            var v2 = (ResourceState)manager.Find(second.ResourceNodeId)!;
            bool deletedNodeReported = false;
            v1.OnReportEvent += (_, _, _) => deletedNodeReported = true;
            v2.OnReportEvent += (_, _, _) => deletedNodeReported = true;
            events.Clear();

            DeleteMethodStateResult deleted = await manager.OnDeleteGroupAsync(
                group,
                group.Epoch!.Value).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True);
                Assert.That(events.Select(evt => evt.GetType()).ToArray(), Is.EqualTo(new[]
                {
                    typeof(VersionDeletedEventState),
                    typeof(VersionDeletedEventState),
                    typeof(ResourceDeletedEventState),
                    typeof(GroupDeletedEventState),
                    typeof(RegistryUpdatedEventState)
                }));
                Assert.That(events.Select(evt => evt.Time!.Value).Distinct().ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(events.OfType<VersionDeletedEventState>()
                    .Select(evt => evt.SourceNode!.Value).ToArray(),
                    Is.EquivalentTo(new[] { v1.NodeId, v2.NodeId }));
                Assert.That(events.OfType<ResourceDeletedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
                Assert.That(events.OfType<GroupDeletedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(group.NodeId));
                Assert.That(deletedNodeReported, Is.False);
            });
        }

        [Test]
        public async Task RegistrationLifecycleEmitsCoalescedNativeEventsAsync()
        {
            using XRegistryRegistrationNodeManager manager = CreateAddressSpace(
                out RegistryState registry,
                out Dictionary<NodeId, IList<IReference>> externalReferences);
            var events = new List<BaseEventState>();
            registry.OnReportEvent = (_, _, target) =>
            {
                if (target is BaseEventState evt)
                {
                    events.Add(evt);
                }
            };
            Assert.Multiple(() =>
            {
                Assert.That(registry.EventNotifier,
                    Is.EqualTo(EventNotifiers.SubscribeToEvents));
                Assert.That(registry.EventSourceUrl!.Value,
                    Is.EqualTo("https://registry.example.test"));
                Assert.That(externalReferences[global::Opc.Ua.ObjectIds.Server].Any(reference =>
                    reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                    !reference.IsInverse &&
                    reference.TargetId == registry.NodeId), Is.True);
            });

            CreateGroupMethodStateResult groupResult = await manager.OnCreateGroupAsync(
                manager.SystemContext,
                null!,
                registry.NodeId,
                "schemas",
                CancellationToken.None).ConfigureAwait(false);
            var group = (GroupState)manager.Find(groupResult.GroupNodeId)!;
            Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
            {
                typeof(GroupCreatedEventState),
                typeof(RegistryUpdatedEventState)
            }));
            Assert.Multiple(() =>
            {
                Assert.That(events.OfType<GroupCreatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(group.NodeId));
                Assert.That(events.OfType<RegistryUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(registry.NodeId));
            });
            events.Clear();

            CreateResourceMethodStateResult first = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v1",
                false,
                CancellationToken.None).ConfigureAwait(false);
            var v1 = (ResourceState)manager.Find(first.ResourceNodeId)!;
            Assert.Multiple(() =>
            {
                Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(ResourceCreatedEventState),
                    typeof(VersionCreatedEventState),
                    typeof(GroupUpdatedEventState)
                }));
                Assert.That(v1.MetaEpoch!.Value, Is.EqualTo(1u));
                Assert.That(
                    events.Select(evt => evt.Time!.Value).Distinct().ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(events.OfType<ResourceCreatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v1.NodeId));
                Assert.That(events.OfType<VersionCreatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v1.NodeId));
            });
            events.Clear();

            GetOrCreateResourceMethodStateResult existing =
                await manager.OnGetOrCreateResourceAsync(
                    manager.SystemContext,
                    null!,
                    group.NodeId,
                    "pump",
                    "v1",
                    false,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(existing.Created, Is.False);
                Assert.That(events, Is.Empty);
            });

            CreateResourceMethodStateResult second = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v2",
                true,
                CancellationToken.None).ConfigureAwait(false);
            var v2 = (ResourceState)manager.Find(second.ResourceNodeId)!;
            Assert.Multiple(() =>
            {
                Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(VersionCreatedEventState),
                    typeof(ResourceUpdatedEventState)
                }));
                Assert.That(v1.MetaEpoch!.Value, Is.EqualTo(2u));
                Assert.That(v2.MetaEpoch!.Value, Is.EqualTo(2u));
                Assert.That(events.OfType<ResourceUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
                Assert.That(events.OfType<VersionCreatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
            });
            events.Clear();

            await v2.Write!.OnCallAsync!(
                manager.SystemContext,
                v2.Write,
                v2.NodeId,
                second.FileHandle,
                ByteString.From([1, 2, 3]),
                CancellationToken.None).ConfigureAwait(false);
            await v2.Close!.OnCallAsync!(
                manager.SystemContext,
                v2.Close,
                v2.NodeId,
                second.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(VersionUpdatedEventState),
                    typeof(ResourceUpdatedEventState)
                }));
                var versionUpdated = (VersionUpdatedEventState)events.Single(
                    evt => evt is VersionUpdatedEventState);
                Assert.That(versionUpdated.Changed!.Value,
                    Is.EqualTo(s_versionChanged));
                Assert.That(((XRegistryEventState)versionUpdated).CorrelationId, Is.Null);
                Assert.That(versionUpdated.SourceNode!.Value, Is.EqualTo(v2.NodeId));
                Assert.That(events.OfType<ResourceUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
            });
            events.Clear();

            GetOrCreateResourceMethodStateResult cleanHandle =
                await manager.OnGetOrCreateResourceAsync(
                    manager.SystemContext,
                    null!,
                    group.NodeId,
                    "pump",
                    "v2",
                    true,
                    CancellationToken.None).ConfigureAwait(false);
            await v2.Close!.OnCallAsync!(
                manager.SystemContext,
                v2.Close,
                v2.NodeId,
                cleanHandle.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(events, Is.Empty);

            bool v2ReportedDeletedVersion = false;
            v2.OnReportEvent += (_, _, target) =>
                v2ReportedDeletedVersion |= target is VersionDeletedEventState;
            DeleteMethodStateResult deletedOne = await manager.OnDeleteResourceAsync(v1, 1)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deletedOne.ServiceResult), Is.True);
                Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(VersionDeletedEventState),
                    typeof(ResourceUpdatedEventState)
                }));
                Assert.That(v2.MetaEpoch!.Value, Is.EqualTo(3u));
                VersionDeletedEventState deletedVersion =
                    events.OfType<VersionDeletedEventState>().Single();
                Assert.That(deletedVersion.SourceNode!.Value, Is.EqualTo(v1.NodeId));
                Assert.That(deletedVersion.SourceName!.Value, Is.EqualTo(v1.DisplayName.Text));
                Assert.That(events.OfType<ResourceUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
                Assert.That(v2ReportedDeletedVersion, Is.True);
            });
            events.Clear();

            DeleteMethodStateResult stale = await manager.OnDeleteResourceAsync(v2, 1)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(stale.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(events, Is.Empty);
            });

            DeleteMethodStateResult deletedLast = await manager.OnDeleteResourceAsync(
                v2,
                v2.Epoch!.Value).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deletedLast.ServiceResult), Is.True);
                Assert.That(events.Select(evt => evt.GetType()), Is.EquivalentTo(new[]
                {
                    typeof(VersionDeletedEventState),
                    typeof(ResourceDeletedEventState),
                    typeof(GroupUpdatedEventState)
                }));
                Assert.That(events.OfType<VersionDeletedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
                Assert.That(events.OfType<ResourceDeletedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(v2.NodeId));
                Assert.That(events.OfType<GroupUpdatedEventState>().Single().SourceNode!.Value,
                    Is.EqualTo(group.NodeId));
            });
        }

        [Test]
        public async Task ByteIdenticalCloseLeavesVersionFieldsAndEventsUnchangedAsync()
        {
            using XRegistryRegistrationNodeManager manager = CreateAddressSpace(
                out RegistryState registry,
                out _);
            var events = new List<BaseEventState>();
            registry.OnReportEvent = (_, _, target) =>
            {
                if (target is BaseEventState evt)
                {
                    events.Add(evt);
                }
            };
            CreateGroupMethodStateResult group = await manager.OnCreateGroupAsync(
                manager.SystemContext,
                null!,
                registry.NodeId,
                "schemas",
                CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult created = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.GroupNodeId,
                "pump",
                "v1",
                true,
                CancellationToken.None).ConfigureAwait(false);
            var resource = (ResourceState)manager.Find(created.ResourceNodeId)!;
            byte[] document = [1, 2, 3];
            await resource.Write!.OnCallAsync!(
                manager.SystemContext,
                resource.Write,
                resource.NodeId,
                created.FileHandle,
                ByteString.From(document),
                CancellationToken.None).ConfigureAwait(false);
            await resource.Close!.OnCallAsync!(
                manager.SystemContext,
                resource.Close,
                resource.NodeId,
                created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            uint epoch = resource.Epoch!.Value;
            DateTimeUtc modifiedAt = resource.ModifiedAt!.Value;
            events.Clear();

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                manager.SystemContext,
                resource.Open,
                resource.NodeId,
                6,
                CancellationToken.None).ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                manager.SystemContext,
                resource.Write,
                resource.NodeId,
                opened.FileHandle,
                ByteString.From(document),
                CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                manager.SystemContext,
                resource.Close,
                resource.NodeId,
                opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
                Assert.That(resource.Epoch.Value, Is.EqualTo(epoch));
                Assert.That(resource.ModifiedAt.Value, Is.EqualTo(modifiedAt));
                Assert.That(events, Is.Empty);
            });
        }

        [Test]
        public async Task VersionAndResourceMetaLabelsHaveIndependentOwnershipAsync()
        {
            using XRegistryRegistrationNodeManager manager = CreateAddressSpace(
                out RegistryState registry,
                out _);
            var events = new List<BaseEventState>();
            registry.OnReportEvent = (_, _, target) =>
            {
                if (target is BaseEventState evt)
                {
                    events.Add(evt);
                }
            };
            CreateGroupMethodStateResult group = await manager.OnCreateGroupAsync(
                manager.SystemContext,
                null!,
                registry.NodeId,
                "schemas",
                CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult first = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.GroupNodeId,
                "pump",
                "v1",
                false,
                CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult second = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.GroupNodeId,
                "pump",
                "v2",
                false,
                CancellationToken.None).ConfigureAwait(false);
            var v1 = (ResourceState)manager.Find(first.ResourceNodeId)!;
            var v2 = (ResourceState)manager.Find(second.ResourceNodeId)!;
            DateTimeUtc metaCreatedAt = v1.MetaCreatedAt!.Value;
            uint metaEpoch = v1.MetaEpoch!.Value;
            events.Clear();

            AddAttributeMethodStateResult staleVersion =
                await v1.Labels!.AddAttribute!.OnCallAsync!(
                    manager.SystemContext,
                    v1.Labels.AddAttribute,
                    v1.NodeId,
                    "version",
                    "stale",
                    v1.Epoch!.Value + 1,
                    CancellationToken.None).ConfigureAwait(false);
            AddAttributeMethodStateResult versionLabel =
                await v1.Labels.AddAttribute.OnCallAsync!(
                    manager.SystemContext,
                    v1.Labels.AddAttribute,
                    v1.NodeId,
                    "version",
                    "one",
                    v1.Epoch.Value,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    staleVersion.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(ServiceResult.IsGood(versionLabel.ServiceResult), Is.True);
                Assert.That(v1.Epoch.Value, Is.EqualTo(2u));
                Assert.That(v2.Epoch!.Value, Is.EqualTo(1u));
                Assert.That(v1.MetaEpoch.Value, Is.EqualTo(metaEpoch));
                Assert.That(v2.MetaEpoch!.Value, Is.EqualTo(metaEpoch));
                Assert.That(events.Select(evt => evt.GetType()),
                    Is.EquivalentTo(new[] { typeof(VersionUpdatedEventState) }));
            });
            events.Clear();

            AddAttributeMethodStateResult staleMeta =
                await v1.MetaLabels!.AddAttribute!.OnCallAsync!(
                    manager.SystemContext,
                    v1.MetaLabels.AddAttribute,
                    v1.NodeId,
                    "owner",
                    "stale",
                    metaEpoch + 1,
                    CancellationToken.None).ConfigureAwait(false);
            AddAttributeMethodStateResult metaLabel =
                await v1.MetaLabels.AddAttribute.OnCallAsync!(
                    manager.SystemContext,
                    v1.MetaLabels.AddAttribute,
                    v1.NodeId,
                    "owner",
                    "plant-1",
                    metaEpoch,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    staleMeta.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(ServiceResult.IsGood(metaLabel.ServiceResult), Is.True);
                Assert.That(v1.MetaEpoch!.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(v2.MetaEpoch!.Value, Is.EqualTo(metaEpoch + 1));
                Assert.That(v1.MetaCreatedAt!.Value, Is.EqualTo(metaCreatedAt));
                Assert.That(v2.MetaCreatedAt!.Value, Is.EqualTo(metaCreatedAt));
                Assert.That(v1.MetaModifiedAt!.Value, Is.EqualTo(v2.MetaModifiedAt!.Value));
                Assert.That(
                    Label(manager.SystemContext, v1.MetaLabels!, "owner"),
                    Is.EqualTo("plant-1"));
                Assert.That(
                    Label(manager.SystemContext, v2.MetaLabels!, "owner"),
                    Is.EqualTo("plant-1"));
                Assert.That(v1.Epoch!.Value, Is.EqualTo(2u));
                Assert.That(v2.Epoch!.Value, Is.EqualTo(1u));
                ResourceUpdatedEventState resourceUpdated =
                    events.OfType<ResourceUpdatedEventState>().Single();
                Assert.That(resourceUpdated.SourceNode!.Value, Is.EqualTo(v2.NodeId));
                Assert.That(
                    resourceUpdated.Changed!.Value,
                    Is.EqualTo(s_metaChanged));
            });
        }

        [Test]
        public async Task EventsDisabledStillAdvanceCanonicalEpochsAsync()
        {
            using XRegistryRegistrationNodeManager manager = CreateAddressSpace(
                out RegistryState registry,
                out _,
                eventsEnabled: false);
            uint registryEpoch = registry.Epoch!.Value;
            CreateGroupMethodStateResult groupResult = await manager.OnCreateGroupAsync(
                manager.SystemContext,
                null!,
                registry.NodeId,
                "schemas",
                CancellationToken.None).ConfigureAwait(false);
            var group = (GroupState)manager.Find(groupResult.GroupNodeId)!;
            CreateResourceMethodStateResult first = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v1",
                false,
                CancellationToken.None).ConfigureAwait(false);
            CreateResourceMethodStateResult second = await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v2",
                false,
                CancellationToken.None).ConfigureAwait(false);
            var v1 = (ResourceState)manager.Find(first.ResourceNodeId)!;
            var v2 = (ResourceState)manager.Find(second.ResourceNodeId)!;

            await v1.Labels!.AddAttribute!.OnCallAsync!(
                manager.SystemContext,
                v1.Labels.AddAttribute,
                v1.NodeId,
                "version",
                "one",
                v1.Epoch!.Value,
                CancellationToken.None).ConfigureAwait(false);
            await v1.MetaLabels!.AddAttribute!.OnCallAsync!(
                manager.SystemContext,
                v1.MetaLabels.AddAttribute,
                v1.NodeId,
                "owner",
                "plant-1",
                v1.MetaEpoch!.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(registry.Epoch.Value, Is.EqualTo(registryEpoch + 1));
                Assert.That(group.Epoch!.Value, Is.EqualTo(2u));
                Assert.That(v1.Epoch.Value, Is.EqualTo(2u));
                Assert.That(v2.Epoch!.Value, Is.EqualTo(1u));
                Assert.That(v1.MetaEpoch.Value, Is.EqualTo(3u));
                Assert.That(v2.MetaEpoch!.Value, Is.EqualTo(3u));
                Assert.That(v1.MetaCreatedAt, Is.Not.Null);
                Assert.That(v1.MetaModifiedAt, Is.Not.Null);
                Assert.That(v1.MetaLabels, Is.Not.Null);
            });
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace(
            out RegistryState registry,
            out Dictionary<NodeId, IList<IReference>> externalReferences,
            bool eventsEnabled = true)
        {
            var options = new XRegistryServerOptions
            {
                EventsEnabled = eventsEnabled,
                EventSourceUrl = "https://registry.example.test",
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var manager = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            externalReferences = [];
            manager.CreateAddressSpace(externalReferences);
            ushort ns = (ushort)manager.SystemContext.NamespaceUris.GetIndex(
                XRegistryWellKnown.XRegistryNamespaceUri);
            registry = (RegistryState)manager.Find(
                new NodeId(XRegistryWellKnown.RegistryObject, ns))!;
            return manager;
        }

        private static string? Label(
            ISystemContext context,
            AttributesState labels,
            string key)
        {
            var children = new List<BaseInstanceState>();
            labels.GetChildren(context, children);
            return children.OfType<PropertyState<string>>()
                .FirstOrDefault(child =>
                    string.Equals(child.BrowseName.Name, key, System.StringComparison.Ordinal))
                ?.Value;
        }

        private static readonly string[] s_versionChanged =
            ["epoch", "modifiedat", "resource"];
        private static readonly string[] s_metaChanged =
            ["meta.epoch", "meta.labels", "meta.modifiedat"];
    }
}
