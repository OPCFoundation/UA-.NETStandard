/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Verifies the channel-security policy: a registry mutation always needs a
    /// <c>SignAndEncrypt</c> channel because a document and its content lookup are
    /// integrity-critical, while reads follow
    /// <see cref="XRegistryServerOptions.RequireEncryptionForReads"/>.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistrySecurityTests
    {
        /// <summary>
        /// Authenticated encrypted sessionless requests cannot open or consume another request's staged file.
        /// </summary>
        [TestCase((byte)1, false)]
        [TestCase((byte)2, false)]
        [TestCase((byte)3, false)]
        [TestCase((byte)1, true)]
        [TestCase((byte)2, true)]
        [TestCase((byte)3, true)]
        [Category("SecurityRegression")]
        public async Task SecurityR39SessionlessCallerCannotAllocateOrBorrowResourceFileHandleAsync(
            byte mode, bool explicitNull)
        {
            var store = new RecordingResourceStore();
            (XRegistryRegistrationNodeManager registry, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(options => options.ResourceStore = store).ConfigureAwait(false);
            using XRegistryRegistrationNodeManager manager = registry;
            ResourceState resource = await CreateHandleResourceAsync(manager).ConfigureAwait(false);
            ServerSystemContext owner = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            using OperationContext ownerOperation = owner.OperationContext!;
            await WriteDocumentAsync(owner, resource, ByteString.From("draft-A"u8)).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                owner, resource.Open, resource.NodeId, mode, CancellationToken.None).ConfigureAwait(false);
            Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            int callsBefore = store.Calls;
            foreach (string caller in new[] { "remote-A", "remote-B" })
            {
                ServerSystemContext remote = SessionlessContext(server, caller);
                using OperationContext remoteOperation = remote.OperationContext!;
                if (explicitNull)
                {
                    remote.SessionId = NodeId.Null;
                }
                OpenMethodStateResult denied = await resource.Open.OnCallAsync!(
                    remote, resource.Open, resource.NodeId, mode, CancellationToken.None).ConfigureAwait(false);
                Assert.That(denied.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSessionIdInvalid));
                Assert.That(denied.FileHandle, Is.Zero);
                await AssertHandleDeniedAsync(remote, resource, opened.FileHandle, StatusCodes.BadSessionIdInvalid)
                    .ConfigureAwait(false);
            }
            Assert.That(store.Calls, Is.EqualTo(callsBefore), "Denied handles cannot reach storage.");
            GetPositionMethodStateResult position = await resource.GetPosition!.OnCallAsync!(
                owner, resource.GetPosition, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(position.Position, Is.Zero);
            Assert.That(resource.OpenCount!.Value, Is.EqualTo((ushort)1));
            Assert.That(store.Replacements, Is.EqualTo(1));
            Assert.That(store.LastDocument, Is.EqualTo(ByteString.From("draft-A"u8)));
        }

        /// <summary>
        /// A session owner alone can inspect and atomically commit its exact staged document.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR39RealSessionOwnerReadsAndCommitsExactStagedBytesAsync()
        {
            var store = new RecordingResourceStore();
            (XRegistryRegistrationNodeManager registry, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(options => options.ResourceStore = store).ConfigureAwait(false);
            using XRegistryRegistrationNodeManager manager = registry;
            ResourceState resource = await CreateHandleResourceAsync(manager).ConfigureAwait(false);
            ServerSystemContext owner = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            using OperationContext ownerOperation = owner.OperationContext!;
            await WriteDocumentAsync(owner, resource, ByteString.From("draft-A"u8)).ConfigureAwait(false);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                owner, resource.Open, resource.NodeId, kReadMode | kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            ReadMethodStateResult baseline = await resource.Read!.OnCallAsync!(
                owner, resource.Read, resource.NodeId, opened.FileHandle, 7, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(baseline.Data, Is.EqualTo(ByteString.From("draft-A"u8)));
            ServerSystemContext foreign = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            using OperationContext foreignOperation = foreign.OperationContext!;
            await AssertHandleDeniedAsync(foreign, resource, opened.FileHandle, StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
            OpenMethodStateResult excluded = await resource.Open.OnCallAsync!(
                foreign, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None).ConfigureAwait(false);
            Assert.That(excluded.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
            await resource.SetPosition!.OnCallAsync!(
                owner, resource.SetPosition, resource.NodeId, opened.FileHandle, 0, CancellationToken.None)
                .ConfigureAwait(false);
            WriteMethodStateResult changed = await resource.Write!.OnCallAsync!(
                owner, resource.Write, resource.NodeId, opened.FileHandle, ByteString.From("final-A"u8),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(changed.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(store.LastDocument, Is.EqualTo(ByteString.From("draft-A"u8)),
                "Staging must not publish before the owning close.");
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                owner, resource.Close, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(closed.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(store.LastDocument, Is.EqualTo(ByteString.From("final-A"u8)));
            Assert.That(store.Replacements, Is.EqualTo(2), "One initial and one replacement commit.");
            Assert.That(resource.Size!.Value, Is.EqualTo(7UL));
            Assert.That(resource.OpenCount!.Value, Is.Zero);
            ushort ns = manager.SystemContext.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            Assert.That(manager.Find(new NodeId(ByteString.From("final-A"u8), ns)), Is.Not.Null);
            ReadMethodStateResult retired = await resource.Read.OnCallAsync!(
                owner, resource.Read, resource.NodeId, opened.FileHandle, 7, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(retired.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(retired.Data.IsNull, Is.True);
        }

        /// <summary>
        /// RequestFileOpen fails before creation, while non-handle operations and explicit in-process calls remain valid.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR39AllRequestOpenAndHandleModesPreserveOwnerIsolationAsync(bool getOrCreate)
        {
            var store = new RecordingResourceStore();
            (XRegistryRegistrationNodeManager registry, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(options => options.ResourceStore = store).ConfigureAwait(false);
            using XRegistryRegistrationNodeManager manager = registry;
            CreateGroupMethodStateResult group = await manager.OnCreateGroupAsync(
                manager.SystemContext, null!, NodeId.Null, "handle-tests", CancellationToken.None)
                .ConfigureAwait(false);
            ServerSystemContext remote = SessionlessContext(server, "remote");
            using OperationContext remoteOperation = remote.OperationContext!;
            int calls = store.Calls;
            NodeId rejectedNode;
            uint rejectedHandle;
            ServiceResult result;
            if (getOrCreate)
            {
                GetOrCreateResourceMethodStateResult denied = await manager.OnGetOrCreateResourceAsync(
                    remote, null!, group.GroupNodeId, "request-open", "1", true, CancellationToken.None)
                    .ConfigureAwait(false);
                result = denied.ServiceResult;
                rejectedNode = denied.ResourceNodeId;
                rejectedHandle = denied.FileHandle;
            }
            else
            {
                CreateResourceMethodStateResult denied = await manager.OnCreateResourceAsync(
                    remote, null!, group.GroupNodeId, "request-open", "1", true, CancellationToken.None)
                    .ConfigureAwait(false);
                result = denied.ServiceResult;
                rejectedNode = denied.ResourceNodeId;
                rejectedHandle = denied.FileHandle;
            }
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadSessionIdInvalid));
            Assert.That(rejectedNode.IsNull, Is.True);
            Assert.That(rejectedHandle, Is.Zero);
            Assert.That(store.Calls, Is.EqualTo(calls));
            CreateResourceMethodStateResult stateless = await manager.OnCreateResourceAsync(
                remote, null!, group.GroupNodeId, "request-open", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(stateless.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good),
                "Denied RequestFileOpen must not partially register the requested resource.");
            var resource = (ResourceState)manager.Find(stateless.ResourceNodeId)!;
            OpenMethodStateResult internalOpen = await resource.Open!.OnCallAsync!(
                manager.SystemContext, resource.Open, resource.NodeId, kWriteMode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(internalOpen.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            await AssertHandleDeniedAsync(remote, resource, internalOpen.FileHandle, StatusCodes.BadSessionIdInvalid)
                .ConfigureAwait(false);
            CloseMethodStateResult earlyClose = await resource.Close!.OnCallAsync!(
                manager.SystemContext, resource.Close, resource.NodeId, internalOpen.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(earlyClose.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(resource.OpenCount!.Value, Is.Zero);
            Assert.That(store.Replacements, Is.Zero);
        }

        /// <summary>
        /// Runs each consuming method and verifies precise rejection without returned staged bytes.
        /// </summary>
        private static async ValueTask AssertHandleDeniedAsync(
            ServerSystemContext context, ResourceState resource, uint handle, StatusCode expected)
        {
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                context, resource.Read, resource.NodeId, handle, 7, CancellationToken.None).ConfigureAwait(false);
            WriteMethodStateResult write = await resource.Write!.OnCallAsync!(
                context, resource.Write, resource.NodeId, handle, ByteString.From("forged!"u8),
                CancellationToken.None).ConfigureAwait(false);
            GetPositionMethodStateResult get = await resource.GetPosition!.OnCallAsync!(
                context, resource.GetPosition, resource.NodeId, handle, CancellationToken.None).ConfigureAwait(false);
            SetPositionMethodStateResult set = await resource.SetPosition!.OnCallAsync!(
                context, resource.SetPosition, resource.NodeId, handle, 0, CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult close = await resource.Close!.OnCallAsync!(
                context, resource.Close, resource.NodeId, handle, CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.ServiceResult.StatusCode, Is.EqualTo((StatusCode)expected));
            Assert.That(read.Data.IsNull, Is.True);
            Assert.That(write.ServiceResult.StatusCode, Is.EqualTo((StatusCode)expected));
            Assert.That(get.ServiceResult.StatusCode, Is.EqualTo((StatusCode)expected));
            Assert.That(set.ServiceResult.StatusCode, Is.EqualTo((StatusCode)expected));
            Assert.That(close.ServiceResult.StatusCode, Is.EqualTo((StatusCode)expected));
        }

        /// <summary>
        /// Creates an authenticated remote request whose lack of a session is explicit.
        /// </summary>
        private static ServerSystemContext SessionlessContext(Mock<IServerInternal> server, string name)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.DisplayName).Returns(name);
            var channel = new SecureChannelContext(name, new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, RequestEncoding.Binary);
            return new ServerSystemContext(server.Object, new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object));
        }

        /// <summary>
        /// Creates an ordinary accessible file without reserving a handle.
        /// </summary>
        private static async ValueTask<ResourceState> CreateHandleResourceAsync(XRegistryRegistrationNodeManager manager)
        {
            CreateGroupMethodStateResult group = await manager.OnCreateGroupAsync(
                manager.SystemContext, null!, NodeId.Null, "handle-tests", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await manager.OnCreateResourceAsync(
                manager.SystemContext, null!, group.GroupNodeId, "document", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            return (ResourceState)manager.Find(created.ResourceNodeId)!;
        }

        /// <summary>
        /// Seeds committed bytes through the real FileType methods.
        /// </summary>
        private static async ValueTask WriteDocumentAsync(
            ServerSystemContext context, ResourceState resource, ByteString document)
        {
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                context, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(opened.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                context, resource.Write, resource.NodeId, opened.FileHandle, document, CancellationToken.None)
                .ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                context, resource.Close, resource.NodeId, opened.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(written.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(closed.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        /// <summary>
        /// Registered and seeded documents use the same encryption policy for service reads and monitor admission.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29OpaqueReadsAndMonitoredAdmissionFollowEncryptionPolicy(
            [Values] bool seeded,
            [Values] bool requireEncryptionForReads,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)]
            MessageSecurityMode mode)
        {
            (AsyncCustomNodeManager owner, Mock<IServerInternal> server) =
                await CreateOpaqueAddressSpaceAsync(seeded, requireEncryptionForReads).ConfigureAwait(false);
            using var queues = new MonitoredItemQueueFactory(server.Object.Telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            await using MasterNodeManager master = XRegistryServerTestHarness.CreateMasterNodeManager(server, owner);
            NodeId nodeId = OpaqueNodeId(owner);
            bool allowed = !requireEncryptionForReads || mode == MessageSecurityMode.SignAndEncrypt;
            using OperationContext read = ContextWith(server, mode, requestType: RequestType.Read).OperationContext!;
            (ArrayOf<DataValue> values, _) = await master.ReadAsync(read, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            Assert.That(values, Has.Count.EqualTo(1));
            AssertOpaqueRead(values[0], allowed);

            using OperationContext monitor = ContextWith(
                server, mode, requestType: RequestType.CreateMonitoredItems).OperationContext!;
            var errors = new ServiceResult[1];
            var filters = new MonitoringFilterResult[1];
            var items = new IMonitoredItem[1];
            await master.CreateMonitoredItemsAsync(monitor, 1, 100, TimestampsToReturn.Both,
                [new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                }], errors, filters, items, false).ConfigureAwait(false);
            Assert.That(errors[0].StatusCode, Is.EqualTo(
                allowed ? StatusCodes.Good : StatusCodes.BadSecurityModeInsufficient));
            if (!allowed)
            {
                Assert.That(items[0], Is.Null, "A denied request cannot retain a document-bearing monitored item.");
                return;
            }
            Assert.That(items[0], Is.InstanceOf<MonitoredItem>());
            var item = (MonitoredItem)items[0];
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(monitor.Session);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(monitor.UserIdentity);
            item.SubscriptionCallback = subscription.Object;
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            item.Publish(monitor, notifications, diagnostics, 1, NullLogger.Instance);
            Assert.That(notifications, Has.Count.EqualTo(1));
            AssertOpaqueRead(notifications.Dequeue().Value, allowed: true);
        }

        /// <summary>
        /// A previously permitted subscription cannot retain encrypted-read eligibility after its channel changes.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29OpaqueDeliveryRechecksSubscriberEncryption(
            [Values] bool seeded,
            [Values] bool requireEncryptionForReads)
        {
            (AsyncCustomNodeManager owner, Mock<IServerInternal> server) =
                await CreateOpaqueAddressSpaceAsync(seeded, requireEncryptionForReads).ConfigureAwait(false);
            await using MasterNodeManager master = XRegistryServerTestHarness.CreateMasterNodeManager(server, owner);
            NodeState node = owner.Find(OpaqueNodeId(owner))!;
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var identity = new UserIdentity();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId("opaque-subscriber", 1));
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            session.SetupGet(value => value.EndpointDescription).Returns(endpoint);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            var item = new Mock<IDataChangeMonitoredItem2>();
            item.SetupGet(value => value.Id).Returns(1u);
            item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            item.SetupGet(value => value.NodeId).Returns(node.NodeId);
            item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
            item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
            item.SetupGet(value => value.Session).Returns(session.Object);
            item.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            var delivered = new TaskCompletionSource<DataValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            item.Setup(value => value.QueueValue(It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>()))
                .Callback(new QueueValueCallback((in value, _) =>
                    delivered.TrySetResult(value)));
            await using var monitored = new MonitoredNode2(owner, server.Object, node);
            monitored.Add(item.Object);
            await monitored.OnMonitoredNodeChangedAsync(owner.SystemContext, node, NodeStateChangeMasks.Value)
                .ConfigureAwait(false);
            AssertOpaqueRead(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), true);

            foreach (MessageSecurityMode mode in new[] { MessageSecurityMode.Sign, MessageSecurityMode.None })
            {
                endpoint.SecurityMode = mode;
                delivered = new TaskCompletionSource<DataValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                await monitored.OnMonitoredNodeChangedAsync(owner.SystemContext, node, NodeStateChangeMasks.Value)
                    .ConfigureAwait(false);
                AssertOpaqueRead(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                    allowed: !requireEncryptionForReads);
            }
        }

        /// <summary>
        /// Content sharing retains the same protected document until its final independent resource is deleted.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF29SharedOpaqueDocumentKeepsEncryptionPolicyUntilLastReferenceIsDeleted()
        {
            (XRegistryRegistrationNodeManager registry, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(options => options.RequireEncryptionForReads = true)
                    .ConfigureAwait(false);
            await using MasterNodeManager master = XRegistryServerTestHarness.CreateMasterNodeManager(server, registry);
            ResourceState first = await RegisterOpaqueResourceAsync(registry, server, "first").ConfigureAwait(false);
            NodeId nodeId = OpaqueNodeId(registry);
            NodeState original = registry.Find(nodeId)!;
            ResourceState second = await RegisterOpaqueResourceAsync(registry, server, "second").ConfigureAwait(false);
            await registry.OnDeleteResourceAsync(first, first.Epoch!.Value).ConfigureAwait(false);
            Assert.That(registry.Find(nodeId), Is.SameAs(original));
            using OperationContext signed = ContextWith(
                server, MessageSecurityMode.Sign, requestType: RequestType.Read).OperationContext!;
            (ArrayOf<DataValue> refused, _) = await master.ReadAsync(signed, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            AssertOpaqueRead(refused[0], allowed: false);
            using OperationContext encrypted = ContextWith(
                server, MessageSecurityMode.SignAndEncrypt, requestType: RequestType.Read).OperationContext!;
            (ArrayOf<DataValue> readable, _) = await master.ReadAsync(encrypted, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            AssertOpaqueRead(readable[0], allowed: true);
            await registry.OnDeleteResourceAsync(second, second.Epoch!.Value).ConfigureAwait(false);
            (ArrayOf<DataValue> retired, _) = await master.ReadAsync(encrypted, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }]).ConfigureAwait(false);
            Assert.That(retired[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(retired[0].WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task CreateGroupIsRejectedOnAChannelThatIsNotEncryptedAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task CreateGroupIsAcceptedOnAnEncryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            CreateGroupMethodStateResult result = await nm.OnCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
        }

        [Test]
        public async Task GetOrCreateGroupIsRejectedOnAnUnencryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            GetOrCreateGroupMethodStateResult result = await nm.OnGetOrCreateGroupAsync(
                ContextWith(server, MessageSecurityMode.None), null!, NodeId.Null, "schemas",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task CreateResourceIsRejectedOnAnUnencryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);

            CreateResourceMethodStateResult result = await nm.OnCreateResourceAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, group.GroupNodeId, "r1", "1", false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task GetOrCreateResourceIsRejectedOnAnUnencryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);

            GetOrCreateResourceMethodStateResult result = await nm.OnGetOrCreateResourceAsync(
                ContextWith(server, MessageSecurityMode.Sign), null!, group.GroupNodeId, "r1", "1", false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task AnInProcessCallCarriesNoChannelAndIsAllowedAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            Assert.That(XRegistryRegistrationNodeManager.IsWriteChannelSecure(nm.SystemContext), Is.True,
                "The server's own bootstrap has no secure channel and must not be blocked.");
        }

        [Test]
        public async Task ReadsAreAllowedOnAnyChannelByDefaultAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            Assert.Multiple(() =>
            {
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.None)), Is.True);
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.Sign)), Is.True);
            });
        }

        [Test]
        public async Task ReadsRequireEncryptionWhenTheOptionIsSetAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(o => o.RequireEncryptionForReads = true)
                    .ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            Assert.Multiple(() =>
            {
                Assert.That(nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.Sign)), Is.False);
                Assert.That(
                    nm.IsReadChannelSecure(ContextWith(server, MessageSecurityMode.SignAndEncrypt)), Is.True);
            });
        }

        [Test]
        public async Task WritesRequireEncryptionRegardlessOfTheReadOptionAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(o => o.RequireEncryptionForReads = false)
                    .ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;

            Assert.Multiple(() =>
            {
                Assert.That(
                    XRegistryRegistrationNodeManager.IsWriteChannelSecure(
                        ContextWith(server, MessageSecurityMode.Sign)),
                    Is.False,
                    "The read option cannot relax the write requirement.");
                Assert.That(
                    XRegistryRegistrationNodeManager.IsWriteChannelSecure(
                        ContextWith(server, MessageSecurityMode.SignAndEncrypt)),
                    Is.True);
            });
        }

        [Test]
        public async Task FileMethodsAreRejectedOnAnUnencryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            // One session, two channels. Holding the session constant isolates the security policy
            // from the separate rule that a handle belongs to the session that opened it.
            var sessionId = new NodeId(7001u);
            ServerSystemContext encrypted =
                ContextWith(server, MessageSecurityMode.SignAndEncrypt, sessionId);
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign, sessionId);

            OpenMethodStateResult refused = await resource.Open!.OnCallAsync!(
                signedOnly, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);

            // Open and write over the encrypted channel so the Close below is a real commit.
            OpenMethodStateResult opened = await resource.Open.OnCallAsync!(
                encrypted, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                signedOnly, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            await resource.Write.OnCallAsync!(
                encrypted, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                signedOnly, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);
            DeleteMethodStateResult deleted = await resource.Delete!.OnCallAsync!(
                signedOnly, resource.Delete, resource.NodeId, resource.Epoch!.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refused.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient), "Open for writing.");
                Assert.That(written.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(closed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient),
                    "A Close that commits a document is a mutation.");
                Assert.That(deleted.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task ClosingAHandleThatWroteNothingIsAllowedOnAnyChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            var sessionId = new NodeId(7002u);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt, sessionId), resource.Open,
                resource.NodeId, kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);

            // Releasing a handle changes nothing, so it must not be gated on the write policy —
            // otherwise a handle opened on a permitted channel could never be closed and would
            // hold the upload budget forever.
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.Sign, sessionId), resource.Close,
                resource.NodeId, opened.FileHandle, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
        }

        [Test]
        public async Task AttributeMethodsAreRejectedOnAnUnencryptedChannelAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            var groupState = (GroupState)nm.Find(group.GroupNodeId)!;
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign);

            AddAttributeMethodStateResult added = await groupState.Labels!.AddAttribute!.OnCallAsync!(
                signedOnly, groupState.Labels.AddAttribute, groupState.NodeId, "k", "v",
                groupState.Epoch!.Value, CancellationToken.None).ConfigureAwait(false);
            RemoveAttributeMethodStateResult removed =
                await groupState.Labels.RemoveAttribute!.OnCallAsync!(
                    signedOnly, groupState.Labels.RemoveAttribute, groupState.NodeId, "k",
                    groupState.Epoch!.Value, CancellationToken.None).ConfigureAwait(false);
            DeleteMethodStateResult deleted = await groupState.Delete!.OnCallAsync!(
                signedOnly, groupState.Delete, groupState.NodeId, groupState.Epoch!.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(added.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(removed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
                Assert.That(deleted.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task ReadingIsRejectedWhenEncryptionIsRequiredForReadsAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(o => o.RequireEncryptionForReads = true)
                    .ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;
            ServerSystemContext signedOnly = ContextWith(server, MessageSecurityMode.Sign);

            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                signedOnly, resource.Open, resource.NodeId, kReadMode, CancellationToken.None)
                .ConfigureAwait(false);
            ReadMethodStateResult read = await resource.Read!.OnCallAsync!(
                signedOnly, resource.Read, resource.NodeId, 1u, 16, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(opened.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient), "Open for reading.");
                Assert.That(read.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            });
        }

        [Test]
        public async Task AHandleCannotBeUsedFromAnotherSessionAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync().ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            ServerSystemContext sessionA = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            ServerSystemContext sessionB = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                sessionA, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);

            // Session B tries to drive session A's handle.
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                sessionB, resource.Write, resource.NodeId, opened.FileHandle,
                ByteString.From([1, 2]), CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                sessionB, resource.Close, resource.NodeId, opened.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
                Assert.That(written.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied),
                    "A handle belongs to the session that opened it.");
                Assert.That(closed.ServiceResult.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
            });
        }

        [Test]
        public async Task ClosingASessionReleasesItsHandlesAsync()
        {
            (XRegistryRegistrationNodeManager nodeManager, Mock<IServerInternal> server) =
                await CreateAddressSpaceAsync(o => o.MaxConcurrentUploads = 1)
                    .ConfigureAwait(false);
            using XRegistryRegistrationNodeManager nm = nodeManager;
            CreateGroupMethodStateResult group = await nm.OnCreateGroupAsync(
                nm.SystemContext, null!, NodeId.Null, "schemas", CancellationToken.None)
                .ConfigureAwait(false);
            CreateResourceMethodStateResult created = await nm.OnCreateResourceAsync(
                nm.SystemContext, null!, group.GroupNodeId, "r1", "1", false, CancellationToken.None)
                .ConfigureAwait(false);
            var resource = (ResourceState)nm.Find(created.ResourceNodeId)!;

            var sessionId = new NodeId(4242u);
            ServerSystemContext session = ContextWith(
                server, MessageSecurityMode.SignAndEncrypt, sessionId);
            OpenMethodStateResult opened = await resource.Open!.OnCallAsync!(
                session, resource.Open, resource.NodeId, kWriteMode | kEraseExistingMode,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True, "Precondition.");

            // The session goes away without closing the handle; the budget must come back.
            await nm.SessionClosingAsync(
                session.OperationContext!,
                sessionId,
                deleteSubscriptions: true,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            OpenMethodStateResult next = await resource.Open.OnCallAsync!(
                ContextWith(server, MessageSecurityMode.SignAndEncrypt), resource.Open,
                resource.NodeId, kWriteMode | kEraseExistingMode, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(next.ServiceResult), Is.True,
                "An abandoned session must not hold the upload budget forever.");
        }

        /// <summary>
        /// Builds a system context that looks like a call arriving over a channel with the given
        /// security mode.
        /// </summary>
        private static ServerSystemContext ContextWith(
            Mock<IServerInternal> server,
            MessageSecurityMode mode,
            RequestType requestType = RequestType.Call)
        {
            return ContextWith(
                server, mode, new NodeId(Utils.IncrementIdentifier(ref s_sessionCounter)), requestType);
        }

        /// <summary>
        /// Builds a channel context with an explicitly supplied protocol session identifier.
        /// </summary>
        private static ServerSystemContext ContextWith(
            Mock<IServerInternal> server,
            MessageSecurityMode mode,
            NodeId sessionId,
            RequestType requestType = RequestType.Call)
        {
            var channel = new SecureChannelContext(
                "test",
                new EndpointDescription { SecurityMode = mode },
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);

            // OperationContext derives the SessionId from its session, so a mocked session is what
            // makes two contexts look like two distinct clients.
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(sessionId);
            session.SetupGet(s => s.EffectiveIdentity).Returns(new UserIdentity());
            session.SetupGet(s => s.PreferredLocales).Returns([]);
            session.SetupGet(s => s.EndpointDescription).Returns(channel.EndpointDescription!);

            var operation = new OperationContext(
                new RequestHeader(), channel, requestType, RequestLifetime.None, session.Object);
            return new ServerSystemContext(server.Object, operation);
        }

        /// <summary>
        /// Publishes real registered or seeded content without starting a server or accepting a network connection.
        /// </summary>
        private static async Task<(AsyncCustomNodeManager Owner, Mock<IServerInternal> Server)>
            CreateOpaqueAddressSpaceAsync(bool seeded, bool requireEncryptionForReads)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider(),
                RequireEncryptionForReads = requireEncryptionForReads,
                PublishSeedResource = seeded,
                SeedDocument = s_confidentialDocument
            };
            Mock<IServerInternal> server = XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            AsyncCustomNodeManager owner = seeded
                ? new XRegistryFastPathNodeManager(server.Object, null!, options)
                : new XRegistryRegistrationNodeManager(server.Object, null!, options);
            try
            {
                await owner.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
                if (owner is XRegistryRegistrationNodeManager registry)
                {
                    await RegisterOpaqueResourceAsync(registry, server, "first").ConfigureAwait(false);
                }
                return (owner, server);
            }
            catch
            {
                await owner.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Commits a document through the real encrypted registration and FileType write path.
        /// </summary>
        private static async Task<ResourceState> RegisterOpaqueResourceAsync(
            XRegistryRegistrationNodeManager registry,
            Mock<IServerInternal> server,
            string resourceId)
        {
            ServerSystemContext context = ContextWith(server, MessageSecurityMode.SignAndEncrypt);
            using OperationContext operation = context.OperationContext!;
            GetOrCreateGroupMethodStateResult group = await registry.OnGetOrCreateGroupAsync(
                context, null!, NodeId.Null, "schemas", CancellationToken.None).ConfigureAwait(false);
            Assert.That(group.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            CreateResourceMethodStateResult created = await registry.OnCreateResourceAsync(
                context, null!, group.GroupNodeId, resourceId, "1", true, CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            var resource = (ResourceState)registry.Find(created.ResourceNodeId)!;
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                context, resource.Write, resource.NodeId, created.FileHandle,
                s_confidentialDocument, CancellationToken.None).ConfigureAwait(false);
            Assert.That(written.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                context, resource.Close, resource.NodeId, created.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(closed.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            return resource;
        }

        /// <summary>
        /// Uses the fake content-id provider's exact on-wire identity, not a resource's structural identifier.
        /// </summary>
        private static NodeId OpaqueNodeId(AsyncCustomNodeManager owner)
        {
            return new NodeId(s_confidentialDocument,
                owner.SystemContext.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri));
        }

        /// <summary>
        /// Requires exact document bytes on success and a security status without bytes on rejection.
        /// </summary>
        private static void AssertOpaqueRead(in DataValue value, bool allowed)
        {
            Assert.That(value.StatusCode, Is.EqualTo(
                allowed ? StatusCodes.Good : StatusCodes.BadSecurityModeInsufficient));
            if (allowed)
            {
                Assert.That(value.WrappedValue.TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.Span.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
            }
            else
            {
                Assert.That(value.WrappedValue.IsNull, Is.True);
            }
        }

        private static async Task<(
            XRegistryRegistrationNodeManager Manager,
            Mock<IServerInternal> Server)> CreateAddressSpaceAsync(
            Action<XRegistryServerOptions>? configure = null)
        {
            var options = new XRegistryServerOptions
            {
                ContentIdProvider = new XRegistryServerTestHarness.FakeContentIdProvider()
            };
            configure?.Invoke(options);

            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(options.RegistryNamespaceUri);
            var nm = new XRegistryRegistrationNodeManager(server.Object, null!, options);
            await nm.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>(),
                CancellationToken.None).ConfigureAwait(false);
            return (nm, server);
        }

        private const byte kWriteMode = 2;
        private const byte kReadMode = 1;
        private const byte kEraseExistingMode = 4;
        private static uint s_sessionCounter;

        /// <summary>The document bytes used for the content-addressed read policy regressions.</summary>
        private static readonly ByteString s_confidentialDocument = ByteString.From([0x11, 0x22, 0x33]);

        /// <summary>
        /// Records external persistence while retaining the actual in-memory atomic store.
        /// </summary>
        private sealed class RecordingResourceStore : IXRegistryAtomicResourceStore
        {
            /// <summary>
            /// Gets the number of calls that reached persistence.
            /// </summary>
            public int Calls { get; private set; }

            /// <summary>
            /// Gets the number of complete atomic commits.
            /// </summary>
            public int Replacements { get; private set; }

            /// <summary>
            /// Gets the last committed bytes.
            /// </summary>
            public ByteString LastDocument { get; private set; }

            /// <inheritdoc/>
            public ValueTask<ByteString> ReadAsync(string key, long offset, int count, CancellationToken ct = default)
            {
                Calls++;
                return m_store.ReadAsync(key, offset, count, ct);
            }

            /// <inheritdoc/>
            public ValueTask WriteAsync(string key, long offset, ByteString data, CancellationToken ct = default)
            {
                Calls++;
                return m_store.WriteAsync(key, offset, data, ct);
            }

            /// <inheritdoc/>
            public ValueTask<long> GetLengthAsync(string key, CancellationToken ct = default)
            {
                Calls++;
                return m_store.GetLengthAsync(key, ct);
            }

            /// <inheritdoc/>
            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                Calls++;
                return m_store.DeleteAsync(key, ct);
            }

            /// <inheritdoc/>
            public async ValueTask ReplaceAsync(string key, ByteString document, CancellationToken ct = default)
            {
                Calls++;
                await m_store.ReplaceAsync(key, document, ct).ConfigureAwait(false);
                Replacements++;
                LastDocument = document;
            }

            private readonly InMemoryResourceStore m_store = new();
        }

        /// <summary>
        /// Captures real monitored-node deliveries without a broker, socket or polling loop.
        /// </summary>
        private delegate void QueueValueCallback(in DataValue value, ServiceResult? error);
    }
}
