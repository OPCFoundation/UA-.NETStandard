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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that a sampling group validates the Read permission of an item when it starts
    /// sampling it and again when the inputs of the verdict change, instead of on every sample.
    /// </summary>
    /// <remarks>
    /// Each test changes the permission state from inside the Read of a known sampling cycle,
    /// so the assertions do not depend on timing.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    public sealed class SamplingGroupPermissionCacheTests
    {
        private const double kInterval = 50;

        /// <summary>
        /// Between revalidations the verdict of the previous cycle is reused: the node manager is
        /// asked once when the loop starts, not once per sample.
        /// </summary>
        /// <remarks>
        /// Non-parallelizable: any NodeState created by a concurrent test with RolePermissions
        /// also invalidates the cached verdicts.
        /// </remarks>
        [Test]
        [NonParallelizable]
        public async Task VerdictIsReusedBetweenSamplesAsync()
        {
            using var harness = new Harness();
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(10).ConfigureAwait(false);

            // the loop validated in its first cycle and reused the verdict in cycles 1..8.
            Assert.That(harness.ValidationsBeforeRead(10), Is.EqualTo(1));
            Assert.That(harness.QueuedStatus(9), Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A permission revoked on the sampled node stops the data changes in the cycle in which
        /// the RolePermissions of the node changed: the item receives BadUserAccessDenied.
        /// </summary>
        [Test]
        public async Task RevokedNodePermissionStopsDataChangesInTheSameCycleAsync()
        {
            using var harness = new Harness();
            var node = new BaseDataVariableState(null);
            harness.OnRead(5, () =>
            {
                harness.Denied = true;
                node.RolePermissions =
                [
                    new RolePermissionType { RoleId = ObjectIds.WellKnownRole_Operator, Permissions = 0 }
                ];
            });
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(7).ConfigureAwait(false);

            Assert.That(harness.QueuedStatus(4), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.QueuedStatus(5), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.QueuedStatus(6), Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A change of the default permissions of a namespace revalidates every item.
        /// </summary>
        [Test]
        public async Task NamespaceDefaultPermissionChangeRevalidatesAsync()
        {
            using var harness = new Harness();
            harness.OnRead(5, () =>
            {
                harness.Denied = true;
                harness.ConfigurationNodeManager.Raise(
                    m => m.DefaultPermissionsChanged += null, EventArgs.Empty);
            });
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(7).ConfigureAwait(false);

            Assert.That(harness.QueuedStatus(4), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.QueuedStatus(5), Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// A new effective identity of the Session, as after ActivateSession with another user or
        /// after a role change refreshed the granted roles, is validated in the next cycle.
        /// </summary>
        [Test]
        public async Task SessionIdentityChangeRevalidatesInTheNextCycleAsync()
        {
            using var harness = new Harness();
            harness.OnRead(5, () =>
            {
                harness.Denied = true;
                harness.Identity = new UserIdentity();
            });
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(8).ConfigureAwait(false);

            // the context of cycle 5 was created before the identity changed.
            Assert.That(harness.QueuedStatus(6), Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.QueuedStatus(7), Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Without any change signal, an item is still revalidated once per second.
        /// </summary>
        [Test]
        public async Task VerdictIsRevalidatedPeriodicallyAsync()
        {
            using var harness = new Harness();
            await harness.StartAsync().ConfigureAwait(false);

            // 1000 ms / 50 ms: cycle 0 and cycle 20 validate.
            await harness.WaitForReadAsync(22).ConfigureAwait(false);
            Assert.That(harness.ValidationsBeforeRead(22), Is.GreaterThanOrEqualTo(2));
            Assert.That(harness.ValidationsBeforeRead(22), Is.LessThan(21));
        }

        /// <summary>
        /// A disabled item is neither read nor validated, and is sampled again once it is enabled.
        /// </summary>
        [Test]
        public async Task DisabledItemIsNotReadAsync()
        {
            using var harness = new Harness();
            harness.OnRead(3, () => harness.Mode = MonitoringMode.Disabled);
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(3).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(5 * kInterval)).ConfigureAwait(false);

            Assert.That(harness.ReadCount, Is.EqualTo(4), "a disabled item is not read");
            harness.Mode = MonitoringMode.Reporting;
            await harness.WaitForReadAsync(5).ConfigureAwait(false);
            Assert.That(harness.QueuedStatus(5), Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A Read that throws is logged and skipped; the loop keeps sampling in the next cycle.
        /// </summary>
        [Test]
        public async Task FailedReadDoesNotStopTheLoopAsync()
        {
            using var harness = new Harness();
            harness.OnRead(3, () => throw new InvalidOperationException("read failed"));
            await harness.StartAsync().ConfigureAwait(false);
            await harness.WaitForReadAsync(5).ConfigureAwait(false);

            Assert.That(harness.QueuedStatus(3), Is.Null);
            Assert.That(harness.QueuedStatus(4), Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.QueuedStatus(5), Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Items of one group that TransferSubscriptions moved to different Sessions are read and
        /// permission-checked with the context of their own Session in every cycle.
        /// </summary>
        [Test]
        public async Task ItemsOfDifferentSessionsAreSampledWithTheirOwnContextAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                ISession first = CreateSession(1);
                ISession second = CreateSession(2);
                var validatedSessions = new List<ISession>();
                var validatedLock = new Lock();
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager.Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, double, ArrayOf<ReadValueId>, IList<DataValue>,
                        IList<ServiceResult>, CancellationToken>(
                        (_, _, nodesToRead, values, _, _) =>
                        {
                            for (int ii = 0; ii < nodesToRead.Count; ii++)
                            {
                                nodesToRead[ii].Processed = true;
                                values[ii] = new DataValue(new Variant(ii));
                            }
                            return default;
                        });
                nodeManager.Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, NodeId, PermissionType, CancellationToken>((context, _, _, _) =>
                    {
                        lock (validatedLock)
                        {
                            validatedSessions.Add(context.Session);
                        }
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });

                using var context = new OperationContext(
                    new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, first);
                using var group = new SamplingGroup(
                    server.Object,
                    nodeManager.Object,
                    [new SamplingRateGroup((int)kInterval, (int)kInterval, 4)],
                    context,
                    kInterval);
                var firstQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondQueued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Mock<ISampledDataChangeMonitoredItem> firstItem = CreateItem(1, first, firstQueued);
                Mock<ISampledDataChangeMonitoredItem> secondItem = CreateItem(2, second, secondQueued);

                Assert.That(group.StartMonitoring(context, firstItem.Object, null, initialValueQueued: true), Is.True);
                Assert.That(group.StartMonitoring(context, secondItem.Object, null, initialValueQueued: true), Is.True);
                Assert.That(group.ApplyChanges(), Is.False);
                await Task.WhenAll(firstQueued.Task, secondQueued.Task)
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                lock (validatedLock)
                {
                    Assert.That(validatedSessions, Does.Contain(first));
                    Assert.That(validatedSessions, Does.Contain(second));
                }
            }

            static ISession CreateSession(uint id)
            {
                var session = new Mock<ISession>();
                session.SetupGet(s => s.Id).Returns(new NodeId(id));
                session.SetupGet(s => s.EffectiveIdentity).Returns(new UserIdentity());
                session.SetupGet(s => s.PreferredLocales).Returns([]);
                return session.Object;
            }

            static Mock<ISampledDataChangeMonitoredItem> CreateItem(
                uint id,
                ISession session,
                TaskCompletionSource<bool> queued)
            {
                var item = new Mock<ISampledDataChangeMonitoredItem>();
                item.SetupGet(m => m.Id).Returns(id);
                item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                item.SetupGet(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
                item.SetupGet(m => m.SamplingInterval).Returns(kInterval);
                item.SetupGet(m => m.Session).Returns(session);
                item.Setup(m => m.GetReadValueId()).Returns(() => new ReadValueId
                {
                    NodeId = new NodeId(id, 1),
                    AttributeId = Attributes.Value
                });
                item.Setup(m => m.QueueValue(in It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult?>()))
                    .Callback(() => queued.TrySetResult(true));
                return item;
            }
        }

        /// <summary>
        /// Drives a sampling group with one item and records, per Read, the queued value, the
        /// number of permission validations so far and whether the item was marked processed.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                m_server = DeterministicServerMock.Create(out m_queueFactory);
                m_server.SetupGet(s => s.ConfigurationNodeManager).Returns(ConfigurationNodeManager.Object);

                var session = new Mock<ISession>();
                session.SetupGet(s => s.Id).Returns(new NodeId(1));
                session.SetupGet(s => s.EffectiveIdentity).Returns(() => Identity);
                session.SetupGet(s => s.PreferredLocales).Returns([]);
                m_session = session.Object;

                m_nodeManager.Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, double, ArrayOf<ReadValueId>, IList<DataValue>,
                        IList<ServiceResult>, CancellationToken>(
                        (_, _, nodesToRead, values, _, _) =>
                        {
                            int read;
                            lock (m_recordsLock)
                            {
                                read = m_records.Count;
                                m_records.Add(new Record
                                {
                                    ValidationsBefore = Volatile.Read(ref m_validations)
                                });
                            }
                            if (m_onRead.TryGetValue(read, out Action? action))
                            {
                                action();
                            }
                            if (!nodesToRead[0].Processed)
                            {
                                nodesToRead[0].Processed = true;
                                values[0] = new DataValue(new Variant(read));
                            }
                            Volatile.Write(ref m_reads, read + 1);
                            return default;
                        });
                m_nodeManager.Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref m_validations);
                        return new ValueTask<ServiceResult>(
                            Denied ? new ServiceResult(StatusCodes.BadUserAccessDenied) : ServiceResult.Good);
                    });

                m_item.SetupGet(m => m.Id).Returns(1);
                m_item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                m_item.SetupGet(m => m.MonitoringMode).Returns(() => Mode);
                m_item.SetupGet(m => m.SamplingInterval).Returns(kInterval);
                m_item.SetupGet(m => m.Session).Returns(m_session);
                m_item.SetupGet(m => m.ManagerHandle).Returns(m_handle);
                m_item.Setup(m => m.GetReadValueId()).Returns(() => new ReadValueId
                {
                    NodeId = new NodeId(1, 1),
                    AttributeId = Attributes.Value
                });
                m_item.Setup(m => m.QueueValue(in It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult?>()))
                    .Callback(new QueueValueCallback((in DataValue value, ServiceResult _) =>
                    {
                        lock (m_recordsLock)
                        {
                            m_records[^1].Queued = value.StatusCode;
                        }
                    }));
            }

            public Mock<IConfigurationNodeManager> ConfigurationNodeManager { get; } = new();

            public bool Denied
            {
                get => Volatile.Read(ref m_denied);
                set => Volatile.Write(ref m_denied, value);
            }

            public IUserIdentity Identity { get; set; } = new UserIdentity();

            public MonitoringMode Mode { get; set; } = MonitoringMode.Reporting;

            /// <summary>
            /// Runs an action inside the Read of the given cycle, before its values are set.
            /// </summary>
            public void OnRead(int cycle, Action action)
            {
                m_onRead[cycle] = action;
            }

            public Task StartAsync()
            {
                m_context = new OperationContext(
                    new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, m_session);
                m_group = new SamplingGroup(
                    m_server.Object,
                    m_nodeManager.Object,
                    [new SamplingRateGroup((int)kInterval, (int)kInterval, 4)],
                    m_context,
                    kInterval);

                // the initial value counts as queued, so every Read is a cycle of the loop.
                Assert.That(m_group.StartMonitoring(m_context, m_item.Object, null, initialValueQueued: true), Is.True);
                Assert.That(m_group.ApplyChanges(), Is.False);
                return Task.CompletedTask;
            }

            /// <summary>
            /// Waits until the given number of cycles completed their Read and queued their value.
            /// </summary>
            public async Task WaitForReadAsync(int reads)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (Volatile.Read(ref m_reads) <= reads)
                {
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
            }

            public int ValidationsBeforeRead(int cycle)
            {
                lock (m_recordsLock)
                {
                    return m_records[cycle].ValidationsBefore;
                }
            }

            public StatusCode? QueuedStatus(int cycle)
            {
                lock (m_recordsLock)
                {
                    return m_records[cycle].Queued;
                }
            }

            public int ReadCount => Volatile.Read(ref m_reads);

            public void Dispose()
            {
                m_group?.Dispose();
                m_context?.Dispose();
                m_queueFactory.Dispose();
            }

            private delegate void QueueValueCallback(in DataValue value, ServiceResult error);

            private sealed class Record
            {
                public int ValidationsBefore { get; set; }

                public StatusCode? Queued { get; set; }
            }

            private readonly Mock<IServerInternal> m_server;
            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly ISession m_session;
            private readonly Mock<IAsyncNodeManager> m_nodeManager = new();
            private readonly Mock<ISampledDataChangeMonitoredItem> m_item = new();
            private readonly object m_handle = new();
            private readonly List<Record> m_records = [];
            private readonly Lock m_recordsLock = new();
            private readonly Dictionary<int, Action> m_onRead = [];
            private OperationContext? m_context;
            private SamplingGroup? m_group;
            private int m_validations;
            private int m_reads;
            private bool m_denied;
        }
    }
}
