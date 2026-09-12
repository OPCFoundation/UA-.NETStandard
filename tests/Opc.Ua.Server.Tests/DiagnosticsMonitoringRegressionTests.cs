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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("DiagnosticsNodeManager")]
    public sealed class DiagnosticsMonitoringRegressionTests
    {
        [TestCase(VariableTypes.ServerDiagnosticsSummaryType)]
        [TestCase(VariableTypes.SessionDiagnosticsVariableType)]
        [TestCase(VariableTypes.SessionDiagnosticsArrayType)]
        [TestCase(VariableTypes.SessionSecurityDiagnosticsType)]
        [TestCase(VariableTypes.SessionSecurityDiagnosticsArrayType)]
        [TestCase(VariableTypes.SubscriptionDiagnosticsType)]
        [TestCase(VariableTypes.SubscriptionDiagnosticsArrayType)]
        [TestCase(VariableTypes.SamplingIntervalDiagnosticsArrayType)]
        public void StandardDiagnosticStructureIsAlwaysReportedAndStartsOneTimer(uint typeId)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var timers = new TimerTracker();
                using (var manager = new DiagnosticHooks(server.Object, timers.Provider))
                {
                    NodeHandle handle = CreateHandle(1, new NodeId(typeId));
                    using MonitoredItem item = CreateItem(server.Object, manager, handle, 1);
                    manager.Created(handle, item);
                    Assert.That(item.AlwaysReportUpdates, Is.True);
                    Assert.That(timers.Active, Is.EqualTo(1));
                    Assert.That(timers.Created, Is.EqualTo(1));
                }
                Assert.That(timers.Active, Is.Zero);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task NonstandardTypeDoesNotBecomeDiagnosticDuringModeChangesAsync(int kind)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var timers = new TimerTracker();
                using var manager = new DiagnosticHooks(server.Object, timers.Provider);
                NodeId typeId = kind switch
                {
                    0 => NodeId.Null,
                    1 => new NodeId("SessionDiagnosticsVariableType", 0),
                    _ => new NodeId(VariableTypes.SessionDiagnosticsVariableType, 1)
                };
                NodeHandle handle = CreateHandle(1, typeId);
                using MonitoredItem item = CreateItem(server.Object, manager, handle, 1);
                manager.Created(handle, item);
                await manager.ChangeAsync(handle, item, MonitoringMode.Disabled).ConfigureAwait(false);
                await manager.ChangeAsync(handle, item, MonitoringMode.Reporting).ConfigureAwait(false);
                Assert.That(item.AlwaysReportUpdates, Is.False);
                Assert.That(timers.Created, Is.Zero);
            }
        }

        [Test]
        public async Task DiagnosticMonitoringOwnsOneTimerAcrossAllLifecycleTransitionsAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var timers = new TimerTracker();
                using var manager = new DiagnosticHooks(server.Object, timers.Provider);
                NodeHandle ordinaryHandle = CreateHandle(1, VariableTypeIds.BaseDataVariableType);
                using MonitoredItem ordinary = CreateItem(server.Object, manager, ordinaryHandle, 1);
                manager.Created(ordinaryHandle, ordinary);
                await manager.ChangeAsync(ordinaryHandle, ordinary, MonitoringMode.Disabled).ConfigureAwait(false);
                Assert.That(timers.Active, Is.Zero);

                NodeHandle firstHandle = CreateHandle(2, VariableTypeIds.SubscriptionDiagnosticsType);
                NodeHandle secondHandle = CreateHandle(3, VariableTypeIds.SessionDiagnosticsVariableType);
                using MonitoredItem first = CreateItem(server.Object, manager, firstHandle, 2);
                using MonitoredItem second = CreateItem(server.Object, manager, secondHandle, 3);
                manager.Created(firstHandle, first);
                manager.Created(secondHandle, second);
                Assert.That(timers.Active, Is.EqualTo(1));
                Assert.That(timers.Created, Is.EqualTo(1));

                await manager.ChangeAsync(firstHandle, first, MonitoringMode.Disabled).ConfigureAwait(false);
                Assert.That(timers.Active, Is.EqualTo(1));
                Assert.That(timers.Created, Is.EqualTo(1));
                await manager.DeletedAsync(secondHandle, second).ConfigureAwait(false);
                Assert.That(timers.Active, Is.Zero);

                await manager.ChangeAsync(firstHandle, first, MonitoringMode.Reporting).ConfigureAwait(false);
                Assert.That(timers.Active, Is.EqualTo(1));
                Assert.That(timers.Created, Is.EqualTo(2));
                await manager.ChangeAsync(firstHandle, first, MonitoringMode.Sampling).ConfigureAwait(false);
                Assert.That(timers.Created, Is.EqualTo(2));
                await manager.SetDiagnosticsEnabledAsync(
                    server.Object.DefaultSystemContext, false).ConfigureAwait(false);
                Assert.That(timers.Active, Is.Zero);
                await manager.SetDiagnosticsEnabledAsync(
                    server.Object.DefaultSystemContext, true).ConfigureAwait(false);
                Assert.That(timers.Active, Is.EqualTo(1));
                await manager.DeletedAsync(firstHandle, first).ConfigureAwait(false);
                Assert.That(timers.Active, Is.Zero);
            }
        }

        [Test]
        public async Task DiagnosticsArrayReadKeepsAConsistentSnapshotDuringSessionRemovalAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using var manager = new DiagnosticHooks(server.Object, TimeProvider.System);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                var first = new SessionDiagnosticsDataType { SessionName = "first" };
                var second = new SessionDiagnosticsDataType { SessionName = "second" };
                NodeId secondId = default;
                ValueTask removal = default;
                bool removeOnRead = false;
                NodeValueSimpleEventHandler updateFirst =
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(first);
                        if (removeOnRead)
                        {
                            removeOnRead = false;
                            removal = manager.DeleteSessionDiagnosticsAsync(
                                server.Object.DefaultSystemContext, secondId);
                        }
                        return ServiceResult.Good;
                    };
                static ServiceResult UpdateSecurity(ISystemContext context, NodeState node, ref Variant value)
                {
                    value = Variant.FromStructure(new SessionSecurityDiagnosticsDataType());
                    return ServiceResult.Good;
                }
                await manager.CreateSessionDiagnosticsAsync(
                    server.Object.DefaultSystemContext,
                    first,
                    updateFirst,
                    new SessionSecurityDiagnosticsDataType(),
                    UpdateSecurity).ConfigureAwait(false);
                secondId = await manager.CreateSessionDiagnosticsAsync(
                    server.Object.DefaultSystemContext,
                    second,
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(second);
                        return ServiceResult.Good;
                    },
                    new SessionSecurityDiagnosticsDataType(),
                    UpdateSecurity).ConfigureAwait(false);
                removeOnRead = true;
                manager.ForceDiagnosticsScan();

                ArrayOf<SessionDiagnosticsDataType> during = manager.ReadSessionArray();
                await removal.ConfigureAwait(false);
                Assert.That(during, Has.Count.EqualTo(2));
                Assert.That(during[0].SessionName, Is.EqualTo("first"));
                Assert.That(during[1].SessionName, Is.EqualTo("second"));
                manager.ForceDiagnosticsScan();
                ArrayOf<SessionDiagnosticsDataType> after = manager.ReadSessionArray();
                Assert.That(after, Has.Count.EqualTo(1));
                Assert.That(after[0].SessionName, Is.EqualTo("first"));
            }
        }

        [Test]
        public async Task SessionRemovalCanCompleteWhileDiagnosticsCallbackIsRunningAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var release = new ManualResetEventSlim())
            {
                using var manager = new DiagnosticHooks(server.Object, TimeProvider.System);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var session = new SessionDiagnosticsDataType { SessionName = "removed" };
                bool pauseOnRead = false;
                NodeId sessionId = await manager.CreateSessionDiagnosticsAsync(
                    server.Object.DefaultSystemContext,
                    session,
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(session);
                        if (pauseOnRead)
                        {
                            pauseOnRead = false;
                            entered.TrySetResult(true);
                            if (!release.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException("Diagnostics callback was not released.");
                            }
                        }
                        return ServiceResult.Good;
                    },
                    new SessionSecurityDiagnosticsDataType(),
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(new SessionSecurityDiagnosticsDataType());
                        return ServiceResult.Good;
                    }).ConfigureAwait(false);
                pauseOnRead = true;
                manager.ForceDiagnosticsScan();
                Task<ArrayOf<SessionDiagnosticsDataType>> read = Task.Run(manager.ReadSessionArray);
                Task removal = Task.CompletedTask;
                ArrayOf<SessionDiagnosticsDataType> snapshot;
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    removal = Task.Run(async () =>
                        await manager.DeleteSessionDiagnosticsAsync(
                            server.Object.DefaultSystemContext, sessionId).ConfigureAwait(false));
                    await removal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    release.Set();
                    snapshot = await read.ConfigureAwait(false);
                    await removal.ConfigureAwait(false);
                }
                Assert.That(snapshot, Has.Count.EqualTo(1));
                Assert.That(snapshot[0].SessionName, Is.EqualTo("removed"));
                manager.ForceDiagnosticsScan();
                Assert.That(manager.ReadSessionArray().Count, Is.Zero);
            }
        }

        [Test]
        public async Task SubscriptionDiagnosticsReadKeepsTheSnapshotDuringRemovalAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using var manager = new DiagnosticHooks(server.Object, TimeProvider.System);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                var first = new SubscriptionDiagnosticsDataType { SubscriptionId = 11 };
                var second = new SubscriptionDiagnosticsDataType { SubscriptionId = 12 };
                NodeId secondId = default;
                ValueTask removal = default;
                bool removeOnRead = false;
                await manager.CreateSubscriptionDiagnosticsAsync(
                    server.Object.DefaultSystemContext,
                    first,
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(first);
                        if (removeOnRead)
                        {
                            removeOnRead = false;
                            removal = manager.DeleteSubscriptionDiagnosticsAsync(
                                server.Object.DefaultSystemContext, secondId);
                        }
                        return ServiceResult.Good;
                    }).ConfigureAwait(false);
                secondId = await manager.CreateSubscriptionDiagnosticsAsync(
                    server.Object.DefaultSystemContext,
                    second,
                    (ISystemContext _, NodeState _, ref Variant value) =>
                    {
                        value = Variant.FromStructure(second);
                        return ServiceResult.Good;
                    }).ConfigureAwait(false);
                removeOnRead = true;
                manager.ForceDiagnosticsScan();
                ArrayOf<SubscriptionDiagnosticsDataType> during = manager.ReadSubscriptionArray();
                await removal.ConfigureAwait(false);
                Assert.That(during, Has.Count.EqualTo(2));
                Assert.That(during[0].SubscriptionId, Is.EqualTo(11));
                Assert.That(during[1].SubscriptionId, Is.EqualTo(12));
                manager.ForceDiagnosticsScan();
                ArrayOf<SubscriptionDiagnosticsDataType> after = manager.ReadSubscriptionArray();
                Assert.That(after, Has.Count.EqualTo(1));
                Assert.That(after[0].SubscriptionId, Is.EqualTo(11));
            }
        }

        private static NodeHandle CreateHandle(uint id, NodeId typeId)
        {
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(id, 1),
                TypeDefinitionId = typeId
            };
            return new NodeHandle { NodeId = node.NodeId, Node = node };
        }

        private static MonitoredItem CreateItem(
            IServerInternal server,
            IAsyncNodeManager manager,
            NodeHandle handle,
            uint id)
        {
            return new MonitoredItem(
                server, manager, handle, 1, id,
                new ReadValueId { NodeId = handle.NodeId, AttributeId = Attributes.Value },
                DiagnosticsMasks.None, TimestampsToReturn.Both, MonitoringMode.Reporting,
                id, null, null, null, 1000, 1, true, 0);
        }

        private sealed class DiagnosticHooks : DiagnosticsNodeManager
        {
            public DiagnosticHooks(IServerInternal server, TimeProvider timeProvider)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    NullLogger.Instance,
                    timeProvider)
            {
                m_context = server.DefaultSystemContext;
            }

            public void Created(NodeHandle handle, MonitoredItem item)
            {
                OnMonitoredItemCreated(m_context, handle, item);
            }

            public ValueTask ChangeAsync(NodeHandle handle, MonitoredItem item, MonitoringMode mode)
            {
                MonitoringMode previous = item.SetMonitoringMode(mode);
                return OnMonitoringModeChangedAsync(m_context, handle, item, previous, mode);
            }

            public ValueTask DeletedAsync(NodeHandle handle, MonitoredItem item)
            {
                return OnMonitoredItemDeletedAsync(m_context, handle, item);
            }

            public ArrayOf<SessionDiagnosticsDataType> ReadSessionArray()
            {
                return ReadArray(
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                    .GetStructureArray<SessionDiagnosticsDataType>();
            }

            public ArrayOf<SubscriptionDiagnosticsDataType> ReadSubscriptionArray()
            {
                return ReadArray(VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                    .GetStructureArray<SubscriptionDiagnosticsDataType>();
            }

            private Variant ReadArray(NodeId nodeId)
            {
                Variant value = default;
                var node = new BaseDataVariableState(null) { NodeId = nodeId };
                ServiceResult result = OnReadDiagnosticsArray(null!, node, ref value);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                return value;
            }

            private readonly ServerSystemContext m_context;
        }

        private sealed class TimerTracker
        {
            public TimerTracker()
            {
                var provider = new Mock<TimeProvider>();
                provider.Setup(time => time.CreateTimer(
                        It.IsAny<TimerCallback>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                    .Returns(() =>
                    {
                        Created++;
                        Active++;
                        int disposed = 0;
                        var timer = new Mock<ITimer>();
                        timer.Setup(value => value.Dispose()).Callback(() =>
                        {
                            if (Interlocked.Exchange(ref disposed, 1) == 0)
                            {
                                Active--;
                            }
                        });
                        return timer.Object;
                    });
                Provider = provider.Object;
            }

            public TimeProvider Provider { get; }
            public int Created { get; private set; }
            public int Active { get; private set; }
        }
    }
}
