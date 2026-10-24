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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Exercises semantic writes and forced notifications through real node managers, items and Publish queues.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SemanticReadAuthorizationRegressionTests
    {
        /// <summary>
        /// A permitted property write cannot force a revoked parent value into the synchronous item's queue.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public Task SecurityR08SyncSemanticWriteCannotResampleRevokedValueAsync(bool samplingGroups)
        {
            return VerifyRevocationAsync(synchronous: true, samplingGroups);
        }

        /// <summary>
        /// A permitted property write cannot force a revoked parent value into the asynchronous item's queue.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public Task SecurityR29AsyncSemanticWriteCannotResampleRevokedValueAsync(bool samplingGroups)
        {
            return VerifyRevocationAsync(synchronous: false, samplingGroups);
        }

        /// <summary>
        /// An authorized synchronous subscriber retains forced delivery and the SemanticsChanged flag.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public Task SecurityR08SyncAuthorizedSemanticChangePreservesValueAndStatusAsync(bool samplingGroups)
        {
            return VerifyAuthorizedAsync(synchronous: true, samplingGroups);
        }

        /// <summary>
        /// An authorized asynchronous subscriber retains forced delivery and the SemanticsChanged flag.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public Task SecurityR29AuthorizedSemanticResamplePreservesValueAndFlagAsync(bool samplingGroups)
        {
            return VerifyAuthorizedAsync(synchronous: false, samplingGroups);
        }

        /// <summary>
        /// A lookup replacement cannot supply a retained item's forced semantic sample.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SecurityR29RetainedSemanticSourceCannotReadReplacementAsync(
            bool synchronous, bool samplingGroups)
        {
            await using var harness = new Harness(synchronous, samplingGroups);
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.Value = 99;
            int replacementReads = 0;
            var replacement = new BaseDataVariableState(null)
            {
                NodeId = harness.Node.NodeId,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                Value = 777,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                RolePermissions = harness.Node.RolePermissions
            };
            replacement.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
            {
                replacementReads++;
                return ServiceResult.Good;
            };
            if (synchronous)
            {
                harness.Synchronous!.ReplaceLookup(replacement);
            }
            else
            {
                harness.Asynchronous!.ReplaceLookup(replacement);
            }

            await harness.WriteRangeAsync(200).ConfigureAwait(false);

            DataValue value = harness.PublishSingle();
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(99));
            Assert.That(value.StatusCode.SemanticsChanged, Is.True);
            Assert.That(harness.ValueReads, Is.EqualTo(1));
            Assert.That(replacementReads, Is.Zero);
        }

        /// <summary>
        /// Synchronous semantic rereads honor the subscriber's channel and existing virtual policy.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task SecurityR08SyncSemanticResampleRevalidatesCurrentSubscriberAsync(
            bool denyVirtualPolicy, bool samplingGroups)
        {
            await using var harness = new Harness(synchronous: true, samplingGroups);
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.Value = 99;
            if (denyVirtualPolicy)
            {
                harness.Synchronous!.Policy = StatusCodes.BadUserAccessDenied;
            }
            else
            {
                harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                harness.Endpoint.SecurityMode = MessageSecurityMode.Sign;
            }

            await harness.WriteRangeAsync(200).ConfigureAwait(false);
            DataValue value = harness.PublishSingle();

            Assert.That(value.WrappedValue.IsNull, Is.True);
            Assert.That(value.StatusCode.Code,
                Is.EqualTo(denyVirtualPolicy
                    ? StatusCodes.BadUserAccessDenied : StatusCodes.BadSecurityModeInsufficient));
            Assert.That(harness.ValueReads, Is.Zero);
        }

        /// <summary>
        /// Revocation while a semantic reread is suspended cannot release the freshly read parent value.
        /// </summary>
        [TestCase("roles", false)]
        [TestCase("channel", false)]
        [TestCase("lease", false)]
        [TestCase("unchanged", false)]
        [TestCase("roles", true)]
        [TestCase("channel", true)]
        [TestCase("lease", true)]
        [TestCase("unchanged", true)]
        public async Task SecurityR29SemanticReadRevalidatesRolesChannelAndAuthorizerLeaseAsync(
            string change, bool samplingGroups)
        {
            await using var harness = new Harness(synchronous: false, samplingGroups);
            await harness.InitializeAsync().ConfigureAwait(false);
            harness.Node.Value = 99;
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable registration = harness.Asynchronous!.RegisterValueReadAuthorization(
                harness.Node, async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            Task write = harness.WriteRangeAsync(200);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                switch (change)
                {
                    case "roles":
                        harness.RevokeRead();
                        break;
                    case "channel":
                        harness.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                    case "lease":
                        registration.Dispose();
                        break;
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            await write.ConfigureAwait(false);
            DataValue value = harness.PublishSingle();
            if (change == "unchanged")
            {
                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(99));
                Assert.That(value.StatusCode.SemanticsChanged, Is.True);
                Assert.That(harness.ValueReads, Is.EqualTo(1));
            }
            else
            {
                StatusCode expected = change switch
                {
                    "roles" => StatusCodes.BadUserAccessDenied,
                    "channel" => StatusCodes.BadSecurityModeInsufficient,
                    _ => StatusCodes.BadShutdown
                };
                Assert.That(value.StatusCode.CodeBits, Is.EqualTo(expected.CodeBits));
                Assert.That(value.StatusCode.SemanticsChanged, Is.True);
                Assert.That(value.WrappedValue.IsNull, Is.True);
            }
        }

        /// <summary>
        /// Verifies denied and authorized neighbors independently on the same semantic-change operation.
        /// </summary>
        private static async Task VerifyRevocationAsync(bool synchronous, bool samplingGroups)
        {
            await using var harness = new Harness(synchronous, samplingGroups);
            await harness.InitializeAsync().ConfigureAwait(false);
            MonitoredItem neighbor = await harness.AddAuthorizedNeighborAsync().ConfigureAwait(false);
            harness.RevokeRead();
            harness.Node.Value = 99;

            await harness.WriteRangeAsync(200).ConfigureAwait(false);

            DataValue denied = harness.PublishSingle();
            Assert.That(denied.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.WrappedValue.IsNull, Is.True);
            DataValue allowed = harness.PublishSingle(neighbor);
            Assert.That(allowed.WrappedValue.GetInt32(), Is.EqualTo(99));
            Assert.That(allowed.StatusCode.SemanticsChanged, Is.True);
            Assert.That(harness.Range.Value.High, Is.EqualTo(200));
            Assert.That(harness.ValueReads, Is.EqualTo(1), "Only the authorized neighbor may read the source.");
        }

        /// <summary>
        /// Unchanged properties do not reread, while changed and forced reports bypass the unchanged-value filter.
        /// </summary>
        private static async Task VerifyAuthorizedAsync(bool synchronous, bool samplingGroups)
        {
            await using var harness = new Harness(synchronous, samplingGroups);
            await harness.InitializeAsync().ConfigureAwait(false);
            await harness.WriteRangeAsync(100).ConfigureAwait(false);
            Assert.That(harness.ValueReads, Is.Zero);
            Assert.That(harness.Item.IsReadyToPublish, Is.False);

            harness.Node.Value = 99;
            await harness.WriteRangeAsync(200).ConfigureAwait(false);
            DataValue changed = harness.PublishSingle();
            Assert.That(changed.WrappedValue.GetInt32(), Is.EqualTo(99));
            Assert.That(changed.StatusCode.SemanticsChanged, Is.True);

            harness.ReportSemanticChange();
            await harness.WaitAsync().ConfigureAwait(false);
            DataValue forced = harness.PublishSingle();
            Assert.That(forced.WrappedValue.GetInt32(), Is.EqualTo(99));
            Assert.That(forced.StatusCode.SemanticsChanged, Is.True);
            Assert.That(harness.ValueReads, Is.EqualTo(2));
        }

        /// <summary>
        /// Owns deterministic services and real monitored items without a network listener or polling delays.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Creates the selected real node-manager implementation and a separately authorized semantic property.
            /// </summary>
            public Harness(bool synchronous, bool samplingGroups)
            {
                Server = DeterministicServerMock.Create(out m_queues, new FakeTimeProvider());
                var types = (TypeTable)Server.Object.TypeTree;
                types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
                types.AddSubtype(DataTypeIds.Range, DataTypeIds.Structure);
                if (synchronous)
                {
                    Synchronous = new SynchronousManager(Server.Object, samplingGroups);
                    Manager = Synchronous.ToAsyncNodeManager();
                    Context = Synchronous.SystemContext;
                    m_namespaceIndex = Synchronous.NamespaceIndex;
                }
                else
                {
                    Asynchronous = new AsynchronousManager(Server.Object, samplingGroups);
                    Manager = Asynchronous;
                    Context = Asynchronous.SystemContext;
                    m_namespaceIndex = Asynchronous.NamespaceIndexes[0];
                }
                Node = new AnalogItemState(null)
                {
                    NodeId = new NodeId("SemanticValue", m_namespaceIndex),
                    BrowseName = new QualifiedName("SemanticValue", m_namespaceIndex),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    Value = 42,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    RolePermissions = Permissions(PermissionType.Browse | PermissionType.Read,
                        ObjectIds.WellKnownRole_Anonymous, ObjectIds.WellKnownRole_AuthenticatedUser)
                };
                Range = new PropertyState<Range>.Implementation<StructureBuilder<Range>>(Node)
                {
                    NodeId = new NodeId("SemanticValue.EURange", m_namespaceIndex),
                    BrowseName = QualifiedName.From(BrowseNames.EURange),
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    DataType = DataTypeIds.Range,
                    ValueRank = ValueRanks.Scalar,
                    Value = new Range { Low = 0, High = 100 },
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    RolePermissions = Permissions(PermissionType.Browse | PermissionType.Read | PermissionType.Write,
                        ObjectIds.WellKnownRole_Anonymous)
                };
                Node.EURange = Range;
                Endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
                m_session = CreateSession(ObjectIds.WellKnownRole_Anonymous, 1);
                Mock.Get(Server.Object.NodeManager)
                    .Setup(master => master.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns<NodeId, CancellationToken>((id, _) =>
                        new ValueTask<(object? Handle, IAsyncNodeManager? Manager)>(
                            id == Node.NodeId ? (new NodeHandle(id, Node), Manager) :
                            id == Range.NodeId ? (new NodeHandle(id, Range), Manager) : (null, null)));
                Mock.Get(Server.Object.NodeManager)
                    .Setup(master => master.GetManagerHandle(It.IsAny<NodeId>(), out It.Ref<INodeManager?>.IsAny))
                    .Returns((NodeId id, out INodeManager? manager) =>
                    {
                        manager = Synchronous;
                        return id == Node.NodeId ? new NodeHandle(id, Node) :
                            id == Range.NodeId ? new NodeHandle(id, Range) : null;
                    });
            }

            /// <summary>
            /// Gets the asynchronous implementation, when selected.
            /// </summary>
            public AsynchronousManager? Asynchronous { get; }

            /// <summary>
            /// Gets the synchronous implementation, when selected.
            /// </summary>
            public SynchronousManager? Synchronous { get; }

            /// <summary>
            /// Gets the operation adapter used for the actual services.
            /// </summary>
            public IAsyncNodeManager Manager { get; }

            /// <summary>
            /// Gets the trusted context used for server-side changes.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the deterministic server services.
            /// </summary>
            public Mock<IServerInternal> Server { get; }

            /// <summary>
            /// Gets the exact monitored source.
            /// </summary>
            public AnalogItemState Node { get; }

            /// <summary>
            /// Gets the separately writable semantic property.
            /// </summary>
            public PropertyState<Range> Range { get; }

            /// <summary>
            /// Gets the current subscriber's accepted endpoint.
            /// </summary>
            public EndpointDescription Endpoint { get; }

            /// <summary>
            /// Gets the retained real reporting item.
            /// </summary>
            public MonitoredItem Item { get; private set; } = null!;

            /// <summary>
            /// Gets the number of protected Value reads after initial admission.
            /// </summary>
            public int ValueReads { get; private set; }

            /// <summary>
            /// Registers the node, admits a real item, drains its initial sample and starts counting protected reads.
            /// </summary>
            public async Task InitializeAsync()
            {
                if (Synchronous != null)
                {
                    Synchronous.Register(Node);
                }
                else
                {
                    await Asynchronous!.RegisterAsync(Node).ConfigureAwait(false);
                }
                Item = await CreateItemAsync(m_session).ConfigureAwait(false);
                Assert.That(PublishSingle().WrappedValue.GetInt32(), Is.EqualTo(42));
                Node.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
                {
                    ValueReads++;
                    return ServiceResult.Good;
                };
            }

            /// <summary>
            /// Admits a neighboring subscriber whose role remains authorized when the original subscriber is revoked.
            /// </summary>
            public async Task<MonitoredItem> AddAuthorizedNeighborAsync()
            {
                MonitoredItem item = await CreateItemAsync(
                    CreateSession(ObjectIds.WellKnownRole_AuthenticatedUser, 2)).ConfigureAwait(false);
                Assert.That(PublishSingle(item).WrappedValue.GetInt32(), Is.EqualTo(42));
                ValueReads = 0;
                return item;
            }

            /// <summary>
            /// Revokes only the original subscriber's Read permission, preserving property Write and the neighbor.
            /// </summary>
            public void RevokeRead()
            {
                Node.RolePermissions = Permissions(PermissionType.Browse | PermissionType.Read,
                    ObjectIds.WellKnownRole_AuthenticatedUser);
            }

            /// <summary>
            /// Writes a valid Range through the node-manager service adapter and awaits its tracked notifications.
            /// </summary>
            public async Task WriteRangeAsync(double high)
            {
                using var operation = new OperationContext(
                    new RequestHeader(), new SecureChannelContext("semantic-write", Endpoint, RequestEncoding.Binary),
                    RequestType.Write, RequestLifetime.None, m_session.Object);
                var errors = new ServiceResult[1];
                await Manager.WriteAsync(operation,
                [
                    new WriteValue
                    {
                        NodeId = Range.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(new ExtensionObject(new Range { Low = 0, High = high })))
                    }
                ], errors).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(errors[0]), Is.True, errors[0]?.ToString());
                await WaitAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Forces the unchanged property's semantic report using the public manager API.
            /// </summary>
            public void ReportSemanticChange()
            {
                if (Synchronous != null)
                {
                    Synchronous.ReportSemanticChange(Context, Range);
                }
                else
                {
                    Asynchronous!.ReportSemanticChange(Context, Range);
                }
            }

            /// <summary>
            /// Places a fence behind every semantic notification admitted by the property write.
            /// </summary>
            public async Task WaitAsync()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (Synchronous != null)
                {
                    await Synchronous.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    await Asynchronous!.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Publishes exactly one real notification and rejects extra queued values.
            /// </summary>
            public DataValue PublishSingle(MonitoredItem? selected = null)
            {
                using OperationContext operation = MonitoredReadAuthorization.CreateContext(m_session.Object);
                var notifications = new Queue<MonitoredItemNotification>();
                (selected ?? Item).Publish(
                    operation, notifications, new Queue<DiagnosticInfo>(), 10, NullLogger.Instance);
                Assert.That(notifications, Has.Count.EqualTo(1));
                return notifications.Dequeue().Value;
            }

            /// <summary>
            /// Drains manager-owned consumers before releasing their queue factory.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                if (Synchronous != null)
                {
                    await Synchronous.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await Asynchronous!.DisposeAsync().ConfigureAwait(false);
                }
                m_queues.Dispose();
            }

            /// <summary>
            /// Creates a current subscriber with a fixed role and accepted channel.
            /// </summary>
            private Mock<ISession> CreateSession(NodeId role, uint id)
            {
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([role]);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(id));
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
                session.SetupGet(value => value.EndpointDescription).Returns(Endpoint);
                return session;
            }

            /// <summary>
            /// Admits and binds an item with queue capacity greater than one, preserving the original exploit boundary.
            /// </summary>
            private async Task<MonitoredItem> CreateItemAsync(Mock<ISession> session)
            {
                using var operation = new OperationContext(
                    new RequestHeader(), new SecureChannelContext("semantic-create", Endpoint, RequestEncoding.Binary),
                    RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
                var errors = new ServiceResult[1];
                var filterErrors = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];
                await Manager.CreateMonitoredItemsAsync(operation, 1, 1000, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = Node.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            SamplingInterval = 0, QueueSize = 3, DiscardOldest = true, ClientHandle = 1
                        }
                    }
                ], errors, filterErrors, items, false, m_ids).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
                var item = (MonitoredItem)items[0];
                var subscription = new Mock<ISubscription>();
                subscription.SetupGet(value => value.Session).Returns(session.Object);
                subscription.SetupGet(value => value.EffectiveIdentity).Returns(() => session.Object.EffectiveIdentity);
                item.SubscriptionCallback = subscription.Object;
                return item;
            }

            /// <summary>
            /// Builds independent role entries for the given permission set.
            /// </summary>
            private static ArrayOf<RolePermissionType> Permissions(PermissionType permission, params NodeId[] roles)
            {
                var entries = new RolePermissionType[roles.Length];
                for (int index = 0; index < roles.Length; index++)
                {
                    entries[index] = new RolePermissionType { RoleId = roles[index], Permissions = (uint)permission };
                }
                return entries;
            }

            /// <summary>The owner of actual queue storage.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
            /// <summary>The original subscriber.</summary>
            private readonly Mock<ISession> m_session;
            /// <summary>The manager's namespace index.</summary>
            private readonly ushort m_namespaceIndex;
            /// <summary>The shared monitored-item id allocator.</summary>
            private readonly MonitoredItemIdFactory m_ids = new();
        }

        /// <summary>
        /// Exposes the synchronous manager's exact notification consumer and existing virtual permission hook.
        /// </summary>
        private sealed class SynchronousManager : CustomNodeManager2
        {
            /// <summary>
            /// Creates the real synchronous manager with report-by-exception monitoring.
            /// </summary>
            public SynchronousManager(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <summary>
            /// Gets or sets an additional application policy.
            /// </summary>
            public ServiceResult Policy { get; set; } = ServiceResult.Good;

            /// <summary>
            /// Registers an exact source and its semantic property.
            /// </summary>
            public void Register(NodeState node)
            {
                AddPredefinedNode(SystemContext, node);
            }

            /// <summary>
            /// Replaces lookup without rebinding or retiring the original monitored source.
            /// </summary>
            public void ReplaceLookup(NodeState node)
            {
                PredefinedNodes[node.NodeId] = node;
            }

            /// <summary>
            /// Gets the real queue consumer for deterministic fences.
            /// </summary>
            public ValueTask WaitAsync(CancellationToken cancellationToken)
            {
                return m_monitoredItemManager.WaitForPendingSemanticResamplesAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public override ServiceResult ValidateRolePermissions(
                OperationContext operationContext, NodeId nodeId, PermissionType requestedPermission)
            {
                return ServiceResult.IsBad(Policy) ? Policy : base.ValidateRolePermissions(
                    operationContext, nodeId, requestedPermission);
            }
        }

        /// <summary>
        /// Exposes the asynchronous manager's exact notification consumer.
        /// </summary>
        private sealed class AsynchronousManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the real asynchronous manager with report-by-exception monitoring.
            /// </summary>
            public AsynchronousManager(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <summary>
            /// Registers an exact source and its semantic property.
            /// </summary>
            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }

            /// <summary>
            /// Replaces lookup without rebinding or retiring the original monitored source.
            /// </summary>
            public void ReplaceLookup(NodeState node)
            {
                PredefinedNodes[node.NodeId] = node;
            }

            /// <summary>
            /// Gets the real queue consumer for deterministic fences.
            /// </summary>
            public ValueTask WaitAsync(CancellationToken cancellationToken)
            {
                return m_monitoredItemManager.WaitForPendingSemanticResamplesAsync(cancellationToken);
            }
        }
    }
}
