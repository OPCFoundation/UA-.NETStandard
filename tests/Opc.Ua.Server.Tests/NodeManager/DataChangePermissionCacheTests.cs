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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies the cached data change permission verdict of a monitored node. A current
    /// verdict, with a reusable exact-source read verdict, is answered without validation;
    /// changed restrictions, invalidated grants and a changed session must still be honored.
    /// </summary>
    /// <remarks>
    /// The owner is a real manager, so the exact-source read verdict is cacheable and repeated
    /// changes take the synchronous cached path. The verdict keys include process-wide versions
    /// (role permissions, namespace defaults) that concurrent tests move, so the fixture does
    /// not run in parallel with them.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [Category("MonitoredItem")]
    [NonParallelizable]
    public sealed class DataChangePermissionCacheTests
    {
        /// <summary>
        /// Repeated changes reuse the role verdict and the exact-source read verdict.
        /// </summary>
        [Test]
        public async Task CurrentVerdictIsReusedAcrossChangesAsync()
        {
            await using var harness = await Harness.CreateAsync().ConfigureAwait(false);

            await harness.ChangeAsync().ConfigureAwait(false);
            int checks = harness.Owner.Checks;
            await harness.ChangeAsync().ConfigureAwait(false);
            await harness.ChangeAsync().ConfigureAwait(false);

            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(1));
            Assert.That(harness.Owner.Checks, Is.EqualTo(checks), "a current verdict reran a check");
            harness.AssertStatuses(StatusCodes.Good, StatusCodes.Good, StatusCodes.Good);
        }

        /// <summary>
        /// Restrictions tightened after a verdict was cached deny the next change, and the grant
        /// returns once they are relaxed.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task CachedVerdictHonorsChangedRestrictionsAsync()
        {
            await using var harness = await Harness.CreateAsync().ConfigureAwait(false);

            await harness.ChangeAsync().ConfigureAwait(false);
            await harness.ChangeAsync().ConfigureAwait(false);
            harness.Node.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
            await harness.ChangeAsync().ConfigureAwait(false);
            harness.Node.AccessRestrictions = AccessRestrictionType.None;
            await harness.ChangeAsync().ConfigureAwait(false);
            await harness.ChangeAsync().ConfigureAwait(false);

            Assert.That(harness.Statuses, Has.Count.EqualTo(5));
            Assert.That(harness.Statuses[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Statuses[1], Is.EqualTo(StatusCodes.Good));
            Assert.That(StatusCode.IsBad(harness.Statuses[2]), Is.True, "a cached grant ignored the restriction");
            Assert.That(harness.Statuses[3], Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Statuses[4], Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// An invalidated grant is validated again: by session, by changed role permissions of
        /// the node and by a revocation that turns the verdict bad.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task CachedVerdictHonorsInvalidatedGrantsAsync()
        {
            await using var harness = await Harness.CreateAsync().ConfigureAwait(false);

            await harness.ChangeAsync().ConfigureAwait(false);
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(1));

            harness.Monitored.InvalidatePermissionCacheForSession(harness.Session.Id);
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(2));

            await harness.Monitored.OnMonitoredNodeChangedAsync(
                harness.Owner.SystemContext, harness.Node, NodeStateChangeMasks.RolePermissions).ConfigureAwait(false);
            harness.Owner.Deny = true;
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(3));

            // a cached bad verdict is answered without validation, and still denies.
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(3));

            harness.Owner.Deny = false;
            harness.Monitored.InvalidatePermissionCacheForSession(harness.Session.Id);
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(4));

            Assert.That(harness.Statuses, Has.Count.EqualTo(6));
            Assert.That(harness.Statuses[2], Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Statuses[3], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Statuses[4], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Statuses[5], Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A verdict is bound to the item's session: a transferred subscription is validated
        /// again for its new session, and a cached denial does not follow it.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task CachedVerdictFollowsSessionChangeAsync()
        {
            await using var harness = await Harness.CreateAsync().ConfigureAwait(false);

            harness.Owner.Deny = true;
            await harness.ChangeAsync().ConfigureAwait(false);
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(1));

            harness.Owner.Deny = false;
            harness.Session = harness.CreateSession(new NodeId(2));
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(2));
            await harness.ChangeAsync().ConfigureAwait(false);
            Assert.That(harness.Owner.RoleValidations, Is.EqualTo(2));

            Assert.That(harness.Statuses, Has.Count.EqualTo(4));
            Assert.That(harness.Statuses[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Statuses[1], Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.Statuses[2], Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Statuses[3], Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// One real owner, one monitored variable and one data change item of a modeled session.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            private Harness()
            {
                m_server = DeterministicServerMock.Create(out m_queues);
                Owner = new CountingNodeManager(
                    m_server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri);
                Node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("ChangedValue", Owner.NamespaceIndex),
                    BrowseName = new QualifiedName("ChangedValue", Owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    AccessRestrictions = AccessRestrictionType.None,
                    Value = 0
                };
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_Anonymous]);
                m_identity = identity.Object;
                // a signed channel: EncryptionRequired denies it.
                m_endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
                m_session = CreateSession(new NodeId(1));

                m_item.SetupGet(m => m.Id).Returns(1);
                m_item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                m_item.SetupGet(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
                m_item.SetupGet(m => m.Session).Returns(() => Session);
                m_item.SetupGet(m => m.EffectiveIdentity).Returns(() => m_identity);
                m_item.SetupGet(m => m.NodeId).Returns(Node.NodeId);
                m_item.SetupGet(m => m.AttributeId).Returns(Attributes.Value);
                m_item.SetupGet(m => m.IndexRange).Returns(NumericRange.Null);
                m_item.SetupGet(m => m.DataEncoding).Returns(QualifiedName.Null);
                m_item.Setup(m => m.QueueValue(in It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult?>()))
                    .Callback(new QueueValueCallback(OnQueued));
                Monitored = new MonitoredNode2(Owner, m_server.Object, Node);
            }

            /// <summary>
            /// Publishes the variable and adds the item.
            /// </summary>
            public static async Task<Harness> CreateAsync()
            {
                var harness = new Harness();
                await harness.Owner.AddNodeAsync(harness.Owner.SystemContext, NodeId.Null, harness.Node)
                    .ConfigureAwait(false);
                harness.Monitored.Add(harness.m_item.Object);
                return harness;
            }

            /// <summary>Gets the owner.</summary>
            public CountingNodeManager Owner { get; }

            /// <summary>Gets the monitored variable.</summary>
            public BaseDataVariableState Node { get; }

            /// <summary>Gets the monitored node.</summary>
            public MonitoredNode2 Monitored { get; }

            /// <summary>Gets or sets the item's current session.</summary>
            public ISession Session
            {
                get => Volatile.Read(ref m_session);
                set => Volatile.Write(ref m_session, value);
            }

            /// <summary>Gets the statuses of the queued values, in order.</summary>
            public IReadOnlyList<StatusCode> Statuses
            {
                get
                {
                    lock (m_statuses)
                    {
                        return [.. m_statuses];
                    }
                }
            }

            /// <summary>
            /// Creates a modeled session on the harness's endpoint and identity.
            /// </summary>
            public ISession CreateSession(NodeId id)
            {
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(id);
                session.SetupGet(value => value.EffectiveIdentity).Returns(() => m_identity);
                session.SetupGet(value => value.SecureChannelId).Returns("channel-" + id);
                session.SetupGet(value => value.EndpointDescription).Returns(() => m_endpoint);
                session.SetupGet(value => value.PreferredLocales).Returns([]);
                session.Setup(value => value.CaptureIdentityRefreshSnapshot())
                    .Returns(() => new IdentityRefreshSnapshot(m_identity, 0));
                return session.Object;
            }

            /// <summary>
            /// Changes the value and waits until the change was delivered to the item.
            /// </summary>
            public async Task ChangeAsync()
            {
                Node.Value = ++m_value;
                await Monitored.OnMonitoredNodeChangedAsync(
                    Owner.SystemContext, Node, NodeStateChangeMasks.Value).ConfigureAwait(false);
                await Monitored.WaitForPendingNotificationsAsync(CancellationToken.None).ConfigureAwait(false);
            }

            /// <summary>
            /// Asserts the statuses of the queued values.
            /// </summary>
            public void AssertStatuses(params StatusCode[] expected)
            {
                Assert.That(Statuses, Is.EqualTo(expected));
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Monitored.DisposeAsync().ConfigureAwait(false);
                Owner.Dispose();
                m_queues.Dispose();
            }

            private void OnQueued(in DataValue value, ServiceResult? error)
            {
                lock (m_statuses)
                {
                    m_statuses.Add(error != null && ServiceResult.IsBad(error) ? error.StatusCode : value.StatusCode);
                }
            }

            /// <summary>
            /// Captures the queue method's by-reference signature.
            /// </summary>
            private delegate void QueueValueCallback(in DataValue value, ServiceResult? error);

            private readonly Mock<IServerInternal> m_server;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly Mock<IDataChangeMonitoredItem2> m_item = new();
            private readonly List<StatusCode> m_statuses = [];
            private readonly IUserIdentity m_identity;
            private readonly EndpointDescription m_endpoint;
#pragma warning disable CA2213 // a modeled session has nothing to dispose
            private ISession m_session;
#pragma warning restore CA2213
            private int m_value;
        }

        /// <summary>
        /// Counts role-permission validations and every permission check that resolves a handle,
        /// and can deny the role permission.
        /// </summary>
        private sealed class CountingNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the owner using the existing manager constructor.
            /// </summary>
            public CountingNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            /// <summary>Gets the number of role-permission validations.</summary>
            public int RoleValidations => Volatile.Read(ref m_roleValidations);

            /// <summary>Gets the number of handle resolutions by permission checks.</summary>
            public int Checks => Volatile.Read(ref m_checks);

            /// <summary>Gets or sets whether the role permission is denied.</summary>
            public bool Deny { get; set; }

            /// <inheritdoc/>
            public override ValueTask<ServiceResult> ValidateRolePermissionsAsync(
                OperationContext operationContext,
                NodeId nodeId,
                PermissionType requestedPermission,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_roleValidations);
                return Deny
                    ? new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied))
                    : base.ValidateRolePermissionsAsync(operationContext, nodeId, requestedPermission, cancellationToken);
            }

            /// <inheritdoc/>
            protected override NodeHandle? IsHandleInNamespace(object? managerHandle)
            {
                if (managerHandle is NodeHandle)
                {
                    Interlocked.Increment(ref m_checks);
                }
                return base.IsHandleInNamespace(managerHandle);
            }

            private int m_roleValidations;
            private int m_checks;
        }
    }
}
