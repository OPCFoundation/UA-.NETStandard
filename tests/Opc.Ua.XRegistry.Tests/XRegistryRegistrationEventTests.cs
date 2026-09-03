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
            await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v1",
                false,
                CancellationToken.None).ConfigureAwait(false);
            await manager.OnCreateResourceAsync(
                manager.SystemContext,
                null!,
                group.NodeId,
                "pump",
                "v2",
                false,
                CancellationToken.None).ConfigureAwait(false);
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
            });
        }

        private static XRegistryRegistrationNodeManager CreateAddressSpace(
            out RegistryState registry,
            out Dictionary<NodeId, IList<IReference>> externalReferences)
        {
            var options = new XRegistryServerOptions
            {
                EventsEnabled = true,
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

        private static readonly string[] s_versionChanged =
            ["epoch", "modifiedat", "resource"];
    }
}
