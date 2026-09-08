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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class XRegistryAsyncNodeManagerTests
    {
        [TestCase("registration")]
        [TestCase("fast-path")]
        [TestCase("federation")]
        public void CancelledStartupPublishesNoNodes(string managerKind)
        {
            var options = new XRegistryServerOptions();
            using AsyncCustomNodeManager manager = CreateManager(managerKind, options);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await manager.CreateAddressSpaceAsync(
                    new Dictionary<NodeId, IList<IReference>>(),
                    cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(
                manager.Find(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.RegistryType,
                    manager.SystemContext.NamespaceUris)),
                Is.Null);
        }

        [TestCase("registration")]
        [TestCase("fast-path")]
        [TestCase("federation")]
        public async Task InterruptedRecursiveStartupRollsBackPublishedNodes(string managerKind)
        {
            var options = new XRegistryServerOptions();
            IServerInternal server = XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri).Object;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool interrupt = true;
            RegistryState? partialRegistry = null;
            using AsyncCustomNodeManager manager = managerKind switch
            {
                "registration" => CreateInterceptedManager<XRegistryRegistrationNodeManager>(
                    server, options, BeforeAdd),
                "fast-path" => CreateInterceptedManager<XRegistryFastPathNodeManager>(server, options, BeforeAdd),
                "federation" => CreateInterceptedManager<XRegistryFederationNodeManager>(server, options, BeforeAdd),
                _ => throw new ArgumentOutOfRangeException(nameof(managerKind))
            };
            var existingReference = new NodeStateReference(
                ReferenceTypeIds.HasNotifier, false, new NodeId("another-registry", 1));
            var externalReferences = new Dictionary<NodeId, IList<IReference>>
            {
                [Ua.ObjectIds.Server] = [existingReference]
            };
            using var cancellation = new CancellationTokenSource();
            Task startup = manager.CreateAddressSpaceAsync(externalReferences, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var registryTypeId = ExpandedNodeId.ToNodeId(
                ObjectTypeIds.RegistryType, manager.SystemContext.NamespaceUris);
            Assert.That(manager.Find(registryTypeId), Is.Not.Null);
            cancellation.Cancel();
            Assert.That(() => startup, Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(manager.Find(registryTypeId), Is.Null);
                Assert.That(externalReferences, Has.Count.EqualTo(1));
                Assert.That(externalReferences[Ua.ObjectIds.Server], Has.Count.EqualTo(1));
                Assert.That(externalReferences[Ua.ObjectIds.Server][0], Is.SameAs(existingReference));
                if (partialRegistry is not null)
                {
                    Assert.That(manager.Find(partialRegistry.NodeId), Is.Null);
                }
            });
            if (partialRegistry is not null)
            {
                Assert.That(
                    async () => await partialRegistry.GetOrCreateGroup!.OnCallAsync!(
                        manager.SystemContext, partialRegistry.GetOrCreateGroup, partialRegistry.NodeId,
                        "not-initialized", CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>());
            }

            interrupt = false;
            await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            Assert.That(manager.Find(registryTypeId), Is.Not.Null);
            if (manager is XRegistryRegistrationNodeManager registration)
            {
                Assert.That(RegistryOf(registration), Is.Not.Null);
            }

            async ValueTask<NodeState> BeforeAdd(NodeState node, CancellationToken ct)
            {
                bool shouldInterrupt = managerKind == "registration"
                    ? node is BaseInstanceState { Parent: RegistryState }
                    : node is BaseInstanceState { Parent: BaseObjectTypeState parent } &&
                        parent.BrowseName.Name == "RegistryType";
                if (interrupt && shouldInterrupt)
                {
                    partialRegistry = (node as BaseInstanceState)?.Parent as RegistryState;
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                return node;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DeletionNotificationsCanReenterTheRegistry(bool attributeDeletion)
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            RegistryState registry = RegistryOf(manager);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool notified = false;
            NodeState target = resource;
            if (attributeDeletion)
            {
                await manager.OnAddAttributeAsync(resource.Labels!, resource.Epoch, "to-delete", "value", 0)
                    .ConfigureAwait(false);
                target = resource.Labels!.FindChild(manager.SystemContext,
                    new QualifiedName("to-delete", resource.NodeId.NamespaceIndex))!;
            }
            target.OnStateChangedAsync = async (context, node, changes, ct) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    GetOrCreateGroupMethodStateResult result = await registry.GetOrCreateGroup!.OnCallAsync!(
                        manager.SystemContext, registry.GetOrCreateGroup, registry.NodeId, "schemas", timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    notified = true;
                }
            };

            ServiceResult result;
            if (attributeDeletion)
            {
                RemoveAttributeMethodStateResult deleted = await manager.OnRemoveAttributeAsync(
                    resource.Labels!, resource.Epoch, "to-delete", 0, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                result = deleted.ServiceResult;
            }
            else
            {
                DeleteMethodStateResult deleted = await manager.OnDeleteResourceAsync(resource, 0, timeout.Token)
                    .ConfigureAwait(false);
                result = deleted.ServiceResult;
            }

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(notified, Is.True);
                Assert.That(manager.Find(target.NodeId), Is.Null);
            });
        }

        [Test]
        public async Task RetiringFastPathCannotDeleteItsReentrantReplacement()
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            var contentId = new NodeId(ByteString.From([1, 2, 3, 4]), resource.NodeId.NamespaceIndex);
            NodeState previous = manager.Find(contentId)!;
            ResourceState? replacement = null;
            previous.OnStateChangedAsync = async (context, node, changes, ct) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    replacement = await CreateCommittedResourceAsync(manager, "replacement").ConfigureAwait(false);
                }
            };

            DeleteMethodStateResult deleted = await manager.OnDeleteResourceAsync(resource, 0)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True);
                Assert.That(replacement, Is.Not.Null);
                Assert.That(manager.Find(contentId), Is.Not.Null.And.Not.SameAs(previous));
                Assert.That(manager.Find(resource.NodeId), Is.Null);
            });
            await manager.OnDeleteResourceAsync(replacement!, 0).ConfigureAwait(false);
            Assert.That(manager.Find(contentId), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiringResourceCannotAllocateAnotherHandle(bool writing)
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            NodeState fastPath = manager.Find(new NodeId(
                ByteString.From([1, 2, 3, 4]), resource.NodeId.NamespaceIndex))!;
            OpenMethodStateResult opened = new();
            AddAttributeMethodStateResult attribute = new();
            bool notified = false;
            AttributesState labels = resource.Labels!;
            fastPath.OnStateChangedAsync = async (context, node, changes, ct) =>
            {
                if ((changes & NodeStateChangeMasks.Deleted) != 0)
                {
                    opened = await resource.Open!.OnCallAsync!(
                        manager.SystemContext, resource.Open, resource.NodeId, writing ? (byte)2 : (byte)1,
                        CancellationToken.None).ConfigureAwait(false);
                    attribute = await labels.AddAttribute!.OnCallAsync!(
                        manager.SystemContext, labels.AddAttribute, labels.NodeId, "late", "value", 0,
                        CancellationToken.None).ConfigureAwait(false);
                    notified = true;
                }
            };

            await manager.OnDeleteResourceAsync(resource, 0).ConfigureAwait(false);

            Assert.That(notified, Is.True);
            Assert.That(opened.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(opened.FileHandle, Is.Zero);
            Assert.That(attribute.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            ResourceState replacement = await CreateCommittedResourceAsync(manager, "replacement")
                .ConfigureAwait(false);
            Assert.That(manager.Find(replacement.NodeId), Is.SameAs(replacement));
        }

        [TestCase("fast-path")]
        [TestCase("resource")]
        [TestCase("child")]
        public async Task FailingDeletionNotificationDoesNotAbandonCommittedCleanup(string failureTarget)
        {
            var contents = new InMemoryResourceStore();
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(contents)
                .ConfigureAwait(false);
            ResourceState first = await CreateCommittedResourceAsync(manager, "a").ConfigureAwait(false);
            ResourceState second = await CreateCommittedResourceAsync(manager, "b", ByteString.From([5, 6, 7, 8]))
                .ConfigureAwait(false);
            var group = (GroupState)first.Parent!;
            var originalNodes = new List<NodeState>();
            CollectNodes(group, manager.SystemContext, originalNodes);
            NodeState fastPath = manager.Find(new NodeId(
                ByteString.From([1, 2, 3, 4]), first.NodeId.NamespaceIndex))!;
            originalNodes.Add(fastPath);
            originalNodes.Add(manager.Find(new NodeId(
                ByteString.From([5, 6, 7, 8]), second.NodeId.NamespaceIndex))!);
            NodeState failure = failureTarget switch
            {
                "resource" => first,
                "child" => first.OpenCount!,
                _ => fastPath
            };
            failure.OnStateChangedAsync = (_, _, changes, _) =>
                (changes & NodeStateChangeMasks.Deleted) == 0
                    ? default
                    : throw new InvalidOperationException("Injected deletion notification failure.");

            Assert.That(
                async () => await manager.OnDeleteGroupAsync(group, 0).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("Injected deletion notification failure."));

            foreach (NodeState node in originalNodes)
            {
                Assert.That(manager.Find(node.NodeId), Is.Null, $"Node {node.NodeId} was left behind.");
            }
            Assert.That(
                (await contents.ReadAsync(first.NodeId.ToString(), 0, 8).ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(
                (await contents.ReadAsync(second.NodeId.ToString(), 0, 8).ConfigureAwait(false)).IsNull, Is.True);
            var remaining = new List<BaseInstanceState>();
            RegistryOf(manager).GetChildren(manager.SystemContext, remaining);
            Assert.That(remaining, Does.Not.Contain(group));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedStoreDeletionIsRetainedForRetry(bool deleteGroup)
        {
            var contents = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateStore(contents);
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            string storeKey = resource.NodeId.ToString();
            var group = (GroupState)resource.Parent!;
            bool fail = true;
            store.Setup(s => s.DeleteAsync(storeKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => fail
                    ? throw new IOException("Injected store deletion failure.")
                    : contents.DeleteAsync(key, ct));

            Assert.That(
                () => deleteGroup
                    ? manager.OnDeleteGroupAsync(group, 0).AsTask()
                    : manager.OnDeleteResourceAsync(resource, 0).AsTask(),
                Throws.TypeOf<IOException>().With.Message.EqualTo("Injected store deletion failure."));
            Assert.That(manager.Find(resource.NodeId), Is.Null);
            Assert.That((await contents.ReadAsync(storeKey, 0, 4).ConfigureAwait(false)).IsNull, Is.False);

            fail = false;
            uint epoch = RegistryOf(manager).Epoch!.Value;
            DeleteMethodStateResult retried = deleteGroup
                ? await manager.OnDeleteGroupAsync(group, 0).ConfigureAwait(false)
                : await manager.OnDeleteResourceAsync(resource, 0).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(retried.ServiceResult), Is.True);
            Assert.That((await contents.ReadAsync(storeKey, 0, 4).ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(RegistryOf(manager).Epoch!.Value, Is.EqualTo(epoch));
        }

        [TestCase("open")]
        [TestCase("create")]
        [TestCase("close")]
        public async Task RetainedCleanupFailureCannotInterruptAnotherUpload(string operation)
        {
            var contents = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateStore(contents);
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState deleted = await CreateCommittedResourceAsync(manager, "a").ConfigureAwait(false);
            ResourceState target = await CreateCommittedResourceAsync(manager, "b", ByteString.From([5, 6, 7, 8]))
                .ConfigureAwait(false);
            var group = (GroupState)target.Parent!;
            uint handle = 0;
            if (operation == "close")
            {
                OpenMethodStateResult opened = await target.Open!.OnCallAsync!(
                    manager.SystemContext, target.Open, target.NodeId, 2, CancellationToken.None).ConfigureAwait(false);
                handle = opened.FileHandle;
                await target.Write!.OnCallAsync!(
                    manager.SystemContext, target.Write, target.NodeId, handle, ByteString.From([9, 9]),
                    CancellationToken.None).ConfigureAwait(false);
            }
            string deletedKey = deleted.NodeId.ToString();
            bool fail = true;
            store.Setup(s => s.DeleteAsync(deletedKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => fail
                    ? throw new IOException("Injected retained cleanup failure.")
                    : contents.DeleteAsync(key, ct));
            Assert.That(
                async () => await manager.OnDeleteResourceAsync(deleted, 0).ConfigureAwait(false),
                Throws.TypeOf<IOException>());

            if (operation == "create")
            {
                CreateResourceMethodStateResult created = await group.CreateResource!.OnCallAsync!(
                    manager.SystemContext, group.CreateResource, group.NodeId, "c", "v1", true,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);
                target = (ResourceState)manager.Find(created.ResourceNodeId)!;
                handle = created.FileHandle;
            }
            else if (operation == "open")
            {
                OpenMethodStateResult opened = await target.Open!.OnCallAsync!(
                    manager.SystemContext, target.Open, target.NodeId, 2, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
                handle = opened.FileHandle;
            }
            if (operation != "close")
            {
                await target.Write!.OnCallAsync!(
                    manager.SystemContext, target.Write, target.NodeId, handle, ByteString.From([9, 9]),
                    CancellationToken.None).ConfigureAwait(false);
            }
            CloseMethodStateResult closed = await target.Close!.OnCallAsync!(
                manager.SystemContext, target.Close, target.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
            ByteString bytes = await contents.ReadAsync(target.NodeId.ToString(), 0, 8).ConfigureAwait(false);
            Assert.That(bytes, Is.EqualTo(operation == "create"
                ? ByteString.From([9, 9])
                : ByteString.From([9, 9, 7, 8])));

            fail = false;
            await manager.OnDeleteResourceAsync(deleted, 0).ConfigureAwait(false);
            Assert.That((await contents.ReadAsync(deletedKey, 0, 8).ConfigureAwait(false)).IsNull, Is.True);
            OpenMethodStateResult reopened = await target.Open!.OnCallAsync!(
                manager.SystemContext, target.Open, target.NodeId, 2, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(reopened.ServiceResult), Is.True);
        }

        [Test]
        public async Task MultipleDeletionFailuresAreReportedAfterAllNodesAreRemoved()
        {
            var contents = new InMemoryResourceStore();
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(contents)
                .ConfigureAwait(false);
            ResourceState first = await CreateCommittedResourceAsync(manager, "a").ConfigureAwait(false);
            ResourceState second = await CreateCommittedResourceAsync(manager, "b", ByteString.From([5, 6, 7, 8]))
                .ConfigureAwait(false);
            var group = (GroupState)first.Parent!;
            NodeState firstFastPath = manager.Find(new NodeId(
                ByteString.From([1, 2, 3, 4]), first.NodeId.NamespaceIndex))!;
            NodeState secondFastPath = manager.Find(new NodeId(
                ByteString.From([5, 6, 7, 8]), second.NodeId.NamespaceIndex))!;
            firstFastPath.OnStateChangedAsync = (_, _, _, _) =>
                throw new InvalidOperationException("First sink failed.");
            secondFastPath.OnStateChangedAsync = (_, _, _, _) =>
                throw new InvalidOperationException("Second sink failed.");

            Assert.That(
                async () => await manager.OnDeleteGroupAsync(group, 0).ConfigureAwait(false),
                Throws.TypeOf<AggregateException>().With.Property("InnerExceptions").Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(manager.Find(first.NodeId), Is.Null);
                Assert.That(manager.Find(second.NodeId), Is.Null);
                Assert.That(manager.Find(group.NodeId), Is.Null);
                Assert.That(manager.Find(firstFastPath.NodeId), Is.Null);
                Assert.That(manager.Find(secondFastPath.NodeId), Is.Null);
            });
            Assert.That(
                (await contents.ReadAsync(first.NodeId.ToString(), 0, 4).ConfigureAwait(false)).IsNull, Is.True);
            Assert.That(
                (await contents.ReadAsync(second.NodeId.ToString(), 0, 4).ConfigureAwait(false)).IsNull, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedHandleNotificationReleasesTheUndisclosedReservation(bool duringCreation)
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            var group = (GroupState)resource.Parent!;
            NodeState changedNode = duringCreation ? group : resource.OpenCount!;
            changedNode.OnStateChangedAsync = (_, _, _, _) =>
                throw new InvalidOperationException("Injected notification failure.");

            if (duringCreation)
            {
                Assert.That(
                    async () => await group.CreateResource!.OnCallAsync!(
                        manager.SystemContext, group.CreateResource, group.NodeId,
                        "undisclosed", "v1", true, CancellationToken.None).ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.EqualTo("Injected notification failure."));
            }
            else
            {
                Assert.That(
                    async () => await resource.Open!.OnCallAsync!(
                        manager.SystemContext, resource.Open, resource.NodeId, 2, CancellationToken.None)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.EqualTo("Injected notification failure."));
            }
            changedNode.OnStateChangedAsync = null;

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, 2, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
            Assert.That(resource.OpenCount!.Value, Is.Zero);
        }

        [Test]
        public async Task CancelledBaselineReadReleasesTheWriterReservation()
        {
            var contents = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateStore(contents);
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(s => s.ReadAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, long offset, int count, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return ByteString.Empty;
                });
            using var cancellation = new CancellationTokenSource();
            Task<OpenMethodStateResult> opening = resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, 2, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            cancellation.Cancel();

            Assert.That(() => opening,
                Throws.InstanceOf<OperationCanceledException>());
            store.Setup(s => s.ReadAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, int count, CancellationToken ct) =>
                    contents.ReadAsync(key, offset, count, ct));

            OpenMethodStateResult reopened = await resource.Open.OnCallAsync(
                manager.SystemContext, resource.Open, resource.NodeId, 2, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(reopened.ServiceResult), Is.True);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, reopened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
            Assert.That(resource.OpenCount!.Value, Is.Zero);
        }

        [Test]
        public async Task CancelledReadDoesNotConsumeItsReservedRange()
        {
            var contents = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateStore(contents);
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, 1, CancellationToken.None)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(s => s.ReadAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, long offset, int count, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return ByteString.Empty;
                });
            using var cancellation = new CancellationTokenSource();
            Task<ReadMethodStateResult> reading = resource.Read!.OnCallAsync!(
                manager.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 2, cancellation.Token)
                .AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            cancellation.Cancel();
            Assert.That(() => reading,
                Throws.InstanceOf<OperationCanceledException>());

            store.Setup(s => s.ReadAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, int count, CancellationToken ct) =>
                    contents.ReadAsync(key, offset, count, ct));
            ReadMethodStateResult retried = await resource.Read.OnCallAsync(
                manager.SystemContext, resource.Read, resource.NodeId, opened.FileHandle, 2, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(retried.ServiceResult), Is.True);
            Assert.That(retried.Data, Is.EqualTo(ByteString.From([1, 2])));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TeardownDrainsCloseAndPreservesTheInjectedStore(bool synchronousDispose)
        {
            var contents = new InMemoryResourceStore();
            Mock<IXRegistryResourceStore> store = CreateStore(contents);
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(store.Object)
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, 2, CancellationToken.None)
                .ConfigureAwait(false);
            await resource.Write!.OnCallAsync!(
                manager.SystemContext, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([5, 6]), CancellationToken.None).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(s => s.WriteAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, long offset, ByteString data, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    await contents.WriteAsync(key, offset, data, ct).ConfigureAwait(false);
                });
            Task<CloseMethodStateResult> closing = resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task teardown;
            if (synchronousDispose)
            {
                manager.Dispose();
                teardown = Task.CompletedTask;
            }
            else
            {
                teardown = manager.DeleteAddressSpaceAsync().AsTask();
            }
            try
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(teardown.IsCompleted, Is.EqualTo(synchronousDispose));
            }
            finally
            {
                release.TrySetResult(true);
            }
            CloseMethodStateResult closed = await closing.ConfigureAwait(false);
            await teardown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ByteString persisted = await store.Object.ReadAsync(resource.NodeId.ToString(), 0, 4)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
                Assert.That(manager.Find(resource.NodeId), Is.Null);
                Assert.That(persisted, Is.EqualTo(ByteString.From([5, 6, 3, 4])));
            });
        }

        [Test]
        public async Task StartupAwaitsAnAsynchronousModelLoader()
        {
            var options = new XRegistryServerOptions();
            using var manager = new DelayedRegistrationNodeManager(
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri).Object,
                options);
            Task startup = manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).AsTask();
            await manager.LoadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            try
            {
                Assert.That(startup.IsCompleted, Is.False);
                Assert.That(manager.Find(new NodeId(
                    XRegistryWellKnown.RegistryObject,
                    manager.SystemContext.NamespaceUris.GetIndexOrAppend(options.RegistryNamespaceUri))), Is.Null);
            }
            finally
            {
                manager.ReleaseLoad.TrySetResult(true);
            }
            await startup.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(RegistryOf(manager), Is.Not.Null);
        }

        [Test]
        public async Task CancellingAQueuedMutationLeavesTheGraphUnchanged()
        {
            var options = new XRegistryServerOptions();
            using var manager = new DelayedRegistrationNodeManager(
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri).Object,
                options);
            manager.ReleaseLoad.TrySetResult(true);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            RegistryState registry = RegistryOf(manager);
            Task<CreateGroupMethodStateResult> first = registry.CreateGroup!.OnCallAsync!(
                manager.SystemContext, registry.CreateGroup, registry.NodeId, "held", CancellationToken.None)
                .AsTask();
            await manager.GroupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task<CreateGroupMethodStateResult> queued = registry.CreateGroup.OnCallAsync(
                manager.SystemContext, registry.CreateGroup, registry.NodeId, "cancelled", cancellation.Token)
                .AsTask();
            try
            {
                Assert.That(queued.IsCompleted, Is.False);
                cancellation.Cancel();
                Assert.That(() => queued, Throws.InstanceOf<OperationCanceledException>());
            }
            finally
            {
                manager.ReleaseGroup.TrySetResult(true);
            }
            CreateGroupMethodStateResult completed = await first.ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            registry.GetChildren(manager.SystemContext, children);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(completed.ServiceResult), Is.True);
                Assert.That(children.Exists(node => node is GroupState group &&
                    group.GroupId?.Value == "cancelled"), Is.False);
            });
        }

        [Test]
        public async Task SessionClosingRefreshesOpenCountWithoutClosingOtherSessions()
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            var firstContext = new Mock<ISystemContext>();
            var firstSessionId = new NodeId("first-session", 1);
            firstContext.As<ISessionSystemContext>().Setup(c => c.SessionId).Returns(firstSessionId);
            var secondContext = new Mock<ISystemContext>();
            secondContext.As<ISessionSystemContext>().Setup(c => c.SessionId).Returns(new NodeId("second-session", 1));
            OpenMethodStateResult first = await resource.Open!.OnCallAsync!(
                firstContext.Object, resource.Open, resource.NodeId, 1, CancellationToken.None).ConfigureAwait(false);
            OpenMethodStateResult second = await resource.Open.OnCallAsync(
                secondContext.Object, resource.Open, resource.NodeId, 1, CancellationToken.None).ConfigureAwait(false);
            Assert.That(resource.OpenCount!.Value, Is.EqualTo(2));

            await manager.SessionClosingAsync(null!, firstSessionId, deleteSubscriptions: true)
                .ConfigureAwait(false);
            ReadMethodStateResult stillOpen = await resource.Read!.OnCallAsync!(
                secondContext.Object, resource.Read, resource.NodeId, second.FileHandle, 4, CancellationToken.None)
                .ConfigureAwait(false);
            ReadMethodStateResult removed = await resource.Read.OnCallAsync(
                firstContext.Object, resource.Read, resource.NodeId, first.FileHandle, 4, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(resource.OpenCount.Value, Is.EqualTo(1));
                Assert.That(ServiceResult.IsGood(stillOpen.ServiceResult), Is.True);
                Assert.That(stillOpen.Data, Is.EqualTo(ByteString.From([1, 2, 3, 4])));
                Assert.That(removed.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            });
        }

        [Test]
        public async Task FilePropertiesAwaitAsynchronousChangeNotifications()
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(new InMemoryResourceStore())
                .ConfigureAwait(false);
            ResourceState resource = await CreateCommittedResourceAsync(manager).ConfigureAwait(false);
            var observed = new List<ushort>();
            resource.OpenCount!.OnStateChangedAsync = async (context, node, changes, ct) =>
            {
                await Task.Yield();
                observed.Add(resource.OpenCount.Value);
            };

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(observed, Has.Count.EqualTo(1));
            await resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed, Has.Count.EqualTo(2));
                Assert.That(observed[0], Is.EqualTo(1));
                Assert.That(observed[1], Is.Zero);
            });
        }

        [Test]
        public async Task AwaitingAnEventSinkDoesNotHoldTheMutationGate()
        {
            using XRegistryRegistrationNodeManager manager = await CreateRegistrationAsync(
                new InMemoryResourceStore(), eventsEnabled: true).ConfigureAwait(false);
            RegistryState registry = RegistryOf(manager);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reports = 0;
            registry.OnReportEventAsync = async (context, node, evt, ct) =>
            {
                if (Interlocked.Increment(ref reports) == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                }
            };
            Task<CreateGroupMethodStateResult> first = registry.CreateGroup!.OnCallAsync!(
                manager.SystemContext, registry.CreateGroup, registry.NodeId, "first", CancellationToken.None)
                .AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                CreateGroupMethodStateResult second = await registry.CreateGroup.OnCallAsync(
                    manager.SystemContext, registry.CreateGroup, registry.NodeId, "second", CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);
                Assert.That(manager.Find(second.GroupNodeId), Is.Not.Null);
            }
            finally
            {
                release.TrySetResult(true);
            }
            CreateGroupMethodStateResult completed = await first.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(completed.ServiceResult), Is.True);
            Assert.That(manager.Find(completed.GroupNodeId), Is.Not.Null);
        }

        private static async Task<XRegistryRegistrationNodeManager> CreateRegistrationAsync(
            IXRegistryResourceStore store,
            bool eventsEnabled = false)
        {
            var options = new XRegistryServerOptions
            {
                ResourceStore = store,
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider(),
                MaxConcurrentUploads = 1,
                EventsEnabled = eventsEnabled,
                EventSourceUrl = "https://registry.example.test"
            };
            var manager = (XRegistryRegistrationNodeManager)CreateManager("registration", options);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            return manager;
        }

        private static async Task<ResourceState> CreateCommittedResourceAsync(
            XRegistryRegistrationNodeManager manager,
            string resourceId = "document",
            ByteString document = default)
        {
            RegistryState registry = RegistryOf(manager);
            GetOrCreateGroupMethodStateResult createdGroup = await registry.GetOrCreateGroup!.OnCallAsync!(
                manager.SystemContext, registry.GetOrCreateGroup, registry.NodeId, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            var group = (GroupState)manager.Find(createdGroup.GroupNodeId)!;
            CreateResourceMethodStateResult created = await group.CreateResource!.OnCallAsync!(
                manager.SystemContext, group.CreateResource, group.NodeId, resourceId, "v1", true,
                CancellationToken.None).ConfigureAwait(false);
            var resource = (ResourceState)manager.Find(created.ResourceNodeId)!;
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                manager.SystemContext, resource.Write, resource.NodeId, created.FileHandle,
                document.IsNull ? ByteString.From([1, 2, 3, 4]) : document, CancellationToken.None)
                .ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, created.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(written.ServiceResult), Is.True);
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
            });
            return resource;
        }

        private static void CollectNodes(NodeState node, ISystemContext context, List<NodeState> nodes)
        {
            nodes.Add(node);
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                CollectNodes(child, context, nodes);
            }
        }

        private static RegistryState RegistryOf(XRegistryRegistrationNodeManager manager)
        {
            return (RegistryState)manager.Find(new NodeId(
                XRegistryWellKnown.RegistryObject,
                manager.SystemContext.NamespaceUris.GetIndexOrAppend(
                    XRegistryWellKnown.XRegistryNamespaceUri)))!;
        }

        private static Mock<IXRegistryResourceStore> CreateStore(InMemoryResourceStore contents)
        {
            var store = new Mock<IXRegistryResourceStore>(MockBehavior.Strict);
            store.Setup(s => s.ReadAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, int count, CancellationToken ct) =>
                    contents.ReadAsync(key, offset, count, ct));
            store.Setup(s => s.WriteAsync(
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, long offset, ByteString data, CancellationToken ct) =>
                    contents.WriteAsync(key, offset, data, ct));
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => contents.DeleteAsync(key, ct));
            store.Setup(s => s.GetLengthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => contents.GetLengthAsync(key, ct));
            return store;
        }

        private static AsyncCustomNodeManager CreateManager(
            string managerKind,
            XRegistryServerOptions options)
        {
            IServerInternal server = XRegistryServerTestHarness
                .CreateServer(options.RegistryNamespaceUri).Object;
            return managerKind switch
            {
                "registration" => new XRegistryRegistrationNodeManager(server, null!, options),
                "fast-path" => new XRegistryFastPathNodeManager(server, null!, options),
                "federation" => new XRegistryFederationNodeManager(server, null!, options),
                _ => throw new ArgumentOutOfRangeException(nameof(managerKind))
            };
        }

        private static TManager CreateInterceptedManager<TManager>(
            IServerInternal server,
            XRegistryServerOptions options,
            Func<NodeState, CancellationToken, ValueTask<NodeState>> beforeAdd)
            where TManager : AsyncCustomNodeManager
        {
            var manager = new Mock<TManager>(server, null!, options) { CallBase = true };
            manager.Protected().Setup<ValueTask<NodeState>>(
                    "AddBehaviourToPredefinedNodeAsync",
                    ItExpr.IsAny<ISystemContext>(),
                    ItExpr.IsAny<NodeState>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns((ISystemContext _, NodeState node, CancellationToken ct) => beforeAdd(node, ct));
            return manager.Object;
        }

        private sealed class DelayedRegistrationNodeManager(
            IServerInternal server,
            XRegistryServerOptions options) : XRegistryRegistrationNodeManager(server, null!, options)
        {
            public TaskCompletionSource<bool> LoadEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseLoad { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> GroupEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseGroup { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                LoadEntered.TrySetResult(true);
                await ReleaseLoad.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return await base.LoadPredefinedNodesAsync(context, cancellationToken).ConfigureAwait(false);
            }

            protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
                ISystemContext context,
                NodeState predefinedNode,
                CancellationToken cancellationToken = default)
            {
                if (predefinedNode is GroupState group && group.GroupId?.Value == "held")
                {
                    GroupEntered.TrySetResult(true);
                    await ReleaseGroup.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                        .ConfigureAwait(false);
                }
                return await base.AddBehaviourToPredefinedNodeAsync(context, predefinedNode, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
