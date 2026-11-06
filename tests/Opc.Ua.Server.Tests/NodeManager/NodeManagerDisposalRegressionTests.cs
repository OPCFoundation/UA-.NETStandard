/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies two-phase node-manager shutdown retains resources until admitted operations drain.
    /// </summary>
    [TestFixture]
    public sealed class NodeManagerDisposalRegressionTests
    {
        /// <summary>
        /// Verifies that shutdown rejects new writes but lets already-admitted writes drain before releasing nodes.
        /// </summary>
        [Test]
        // TODO: Remove when CA2025 can represent a two-phase shutdown race test.
        [SuppressMessage("Reliability", "CA2025:Do not pass disposables into unawaited tasks",
            Justification = "This regression deliberately begins disposal with admitted writes still active, then drains them.")]
        public async Task ShutdownRetainsAddressSpaceUntilAdmittedWritesHaveUnwoundAsync(
            [Values(false, true)] bool cancelQueued)
        {
            var manager = new BlockingNodeManager(NewServer());
            var cancellation = new CancellationTokenSource();
            Task first = WriteAsync(manager, CancellationToken.None);
            Task? queued = null;
            Task? disposal = null;
            try
            {
                await manager.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                queued = WriteAsync(manager, cancellation.Token);
                if (cancelQueued)
                {
                    cancellation.Cancel();
                    Assert.That(() => queued, Throws.InstanceOf<OperationCanceledException>());
                }
                manager.Dispose();
                disposal = DisposeAsync(manager);
                Assert.That(manager.RetainedNodes, Is.EqualTo(1));
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(() => WriteAsync(manager, CancellationToken.None),
                    Throws.InstanceOf<ObjectDisposedException>());
            }
            finally
            {
                manager.Release.TrySetResult(true);
                await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (queued != null && !cancelQueued)
                {
                    await queued.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                if (disposal != null)
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                cancellation.Dispose();
            }
            Assert.That(manager.RetainedNodes, Is.Zero);
        }

        /// <summary>
        /// Verifies cleanup waits for owners of the manager's write and monitored-item semaphores.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CleanupWaitsForOwnedSemaphoreOwnerBeforeDisposingAsync(bool monitoredItemSemaphore)
        {
            var manager = new BlockingNodeManager(NewServer());
            await manager.AcquireCleanupSemaphoreAsync(monitoredItemSemaphore).ConfigureAwait(false);
            Task disposal = DisposeAsync(manager);
            try
            {
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                manager.ReleaseCleanupSemaphore(monitoredItemSemaphore);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            manager.AssertCleanupSemaphoreDisposed(monitoredItemSemaphore);
        }

        /// <summary>
        /// Verifies disposal waits for an admitted monitored-item transfer to finish.
        /// </summary>
        [Test]
        // TODO: Remove when CA2025 can represent a two-phase shutdown race test.
        [SuppressMessage("Reliability", "CA2025:Do not pass disposables into unawaited tasks",
            Justification = "This regression starts shutdown while an admitted transfer owns manager state.")]
        public async Task DisposalWaitsForAdmittedMonitoredItemTransferAsync()
        {
            var manager = new BlockingNodeManager(NewServer());
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nodeId = new NodeId(1u, 1);
            var handle = new NodeHandle(nodeId, new BaseDataVariableState(null) { NodeId = nodeId });
            var monitoredItem = new Mock<IMonitoredItem>(MockBehavior.Strict);
            monitoredItem.SetupGet(item => item.ManagerHandle).Returns(handle);
            monitoredItem.Setup(item => item.SetupResendDataTrigger()).Callback(() =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The monitored-item transfer was not released.");
                }
            });
            var operationContext = new OperationContext(
                new RequestHeader(), null, RequestType.TransferSubscriptions, RequestLifetime.None);
            Task transfer = Task.Run(async () => await manager.TransferMonitoredItemsAsync(
                operationContext,
                true,
                [monitoredItem.Object],
                [false],
                [ServiceResult.Good],
                new MonitoredItemTransferOptions()).ConfigureAwait(false));
            Task? disposal = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = DisposeAsync(manager);
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                release.Set();
                await transfer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal ??= DisposeAsync(manager);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies disposal waits for an admitted component-cache lookup to finish.
        /// </summary>
        [Test]
        // TODO: Remove when CA2025 can represent a two-phase shutdown race test.
        [SuppressMessage("Reliability", "CA2025:Do not pass disposables into unawaited tasks",
            Justification = "This regression starts shutdown while a cache lookup owns manager state.")]
        public async Task DisposalWaitsForAdmittedComponentCacheLookupAsync()
        {
            var manager = new BlockingNodeManager(NewServer());
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nodeId = new NodeId(2u, 1);
            var node = new BlockingComponentNode(entered, release);
            var handle = new NodeHandle(nodeId, node)
            {
                RootId = nodeId,
                ComponentPath = "child"
            };
            _ = manager.AddComponentToCache(handle, node);
            Task lookup = Task.Run(() => manager.LookupComponentInCache(handle));
            Task? disposal = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = DisposeAsync(manager);
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                release.Set();
                await lookup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal ??= DisposeAsync(manager);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies disposal waits for an admitted diagnostics restoration to finish.
        /// </summary>
        [Test]
        // TODO: Remove when CA2025 can represent a two-phase shutdown race test.
        [SuppressMessage("Reliability", "CA2025:Do not pass disposables into unawaited tasks",
            Justification = "This regression starts shutdown while a diagnostics transition owns manager state.")]
        public async Task DisposalWaitsForAdmittedDiagnosticsRestorationAsync()
        {
            IServerInternal server = NewServer();
            var manager = new BlockingDiagnosticsNodeManager(
                server, new ApplicationConfiguration { ServerConfiguration = new() });
            Task? enable = null;
            Task? disposal = null;
            try
            {
                await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, false).ConfigureAwait(false);

                static ServiceResult UpdateCallback(ISystemContext context, NodeState node, ref Variant value)
                {
                    return ServiceResult.Good;
                }

                _ = await manager.CreateSessionDiagnosticsAsync(
                    manager.SystemContext,
                    new SessionDiagnosticsDataType { SessionName = "Shutdown" },
                    UpdateCallback,
                    new SessionSecurityDiagnosticsDataType(),
                    UpdateCallback).ConfigureAwait(false);

                enable = manager.SetDiagnosticsEnabledAsync(manager.SystemContext, true).AsTask();
                await manager.RestorationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = DisposeAsync(manager);
                Assert.That(disposal.IsCompleted, Is.False);
                manager.ReleaseRestoration.TrySetResult(true);
                await enable.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                manager.ReleaseRestoration.TrySetResult(true);
                if (enable is not null)
                {
                    await enable.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                disposal ??= DisposeAsync(manager);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that master-manager disposal waits for a child's outstanding write before releasing its nodes.
        /// </summary>
        [Test]
        public async Task MasterDisposalWaitsForItsChildManagersOutstandingOperationAsync()
        {
            IServerInternal server = NewServer();
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(Mock.Of<IConfigurationNodeManager>());
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(Mock.Of<ICoreNodeManager>());
            Mock.Get(server).SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            var child = new BlockingNodeManager(server);
            var master = new MasterNodeManager(server,
                new ApplicationConfiguration { ServerConfiguration = new() },
                null, new IAsyncNodeManager[] { child });
            Task operation = WriteAsync(child, CancellationToken.None);
            Task? disposal = null;
            try
            {
                await child.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                disposal = master.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(child.RetainedNodes, Is.EqualTo(1));
            }
            finally
            {
                child.Release.TrySetResult(true);
                await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (disposal != null)
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                await master.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(child.RetainedNodes, Is.Zero);
        }

        /// <summary>
        /// Awaits asynchronous manager disposal when supported, otherwise invokes synchronous disposal.
        /// </summary>
        /// <param name="manager">The manager to dispose.</param>
        /// <returns>The completion task for manager disposal.</returns>
        private static Task DisposeAsync(AsyncCustomNodeManager manager)
        {
            if (manager is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync().AsTask();
            }
            manager.Dispose();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Submits one value write that can remain admitted while the node manager begins shutdown.
        /// </summary>
        private static Task WriteAsync(AsyncCustomNodeManager manager, CancellationToken ct)
        {
            var context = new OperationContext(
                new RequestHeader(), null, RequestType.Write, RequestLifetime.None);
            return manager.WriteAsync(context, [
                new WriteValue
                {
                    NodeId = new NodeId(1u, 1),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(1))
                }
            ], new List<ServiceResult> { ServiceResult.Good }, ct).AsTask();
        }

        /// <summary>
        /// Creates the minimal server services and context required by the disposal test managers.
        /// </summary>
        private static IServerInternal NewServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            var context = new ServerSystemContext(server.Object);
            server.SetupGet(value => value.DefaultSystemContext).Returns(context);
            return server.Object;
        }

        /// <summary>
        /// Retains one node while pausing handle resolution to expose an admitted operation during shutdown.
        /// </summary>
        private sealed class BlockingNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Registers the node whose retention is observed while outstanding writes drain.
            /// </summary>
            public BlockingNodeManager(IServerInternal server)
                : base(server, "urn:disposal-regression")
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1u, 1),
                    BrowseName = new QualifiedName("Retained", 1),
                    Value = new Variant(1)
                };
                PredefinedNodes[node.NodeId] = node;
            }

            /// <summary>
            /// Gets the number of predefined nodes not yet released by shutdown.
            /// </summary>
            public int RetainedNodes => PredefinedNodes.Count;

            /// <summary>
            /// Gets the signal raised when a write enters the blocking handle lookup.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the gate allowing the admitted handle lookup to finish.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Adds a node through the manager's component-cache operation.
            /// </summary>
            /// <param name="handle">The node handle.</param>
            /// <param name="node">The node state.</param>
            /// <returns>The cached component.</returns>
            public NodeState AddComponentToCache(NodeHandle handle, NodeState node)
            {
                return AddNodeToComponentCache(SystemContext, handle, node);
            }

            /// <summary>
            /// Looks up a node through the manager's component-cache operation.
            /// </summary>
            /// <param name="handle">The node handle.</param>
            /// <returns>The matching component, if found.</returns>
            public NodeState? LookupComponentInCache(NodeHandle handle)
            {
                return LookupNodeInComponentCache(SystemContext, handle);
            }

            /// <summary>
            /// Acquires a manager-owned semaphore directly to isolate its disposal drain from operation admission.
            /// </summary>
            /// <param name="monitoredItemSemaphore">Whether to acquire the monitored-item semaphore.</param>
            /// <returns>Completion of the semaphore acquisition.</returns>
            public Task AcquireCleanupSemaphoreAsync(bool monitoredItemSemaphore)
            {
                return (monitoredItemSemaphore ? m_monitoredItemSemaphore : m_writeSemaphore).WaitAsync();
            }

            /// <summary>
            /// Releases the manager-owned semaphore held by the regression test.
            /// </summary>
            /// <param name="monitoredItemSemaphore">Whether to release the monitored-item semaphore.</param>
            public void ReleaseCleanupSemaphore(bool monitoredItemSemaphore)
            {
                (monitoredItemSemaphore ? m_monitoredItemSemaphore : m_writeSemaphore).Release();
            }

            /// <summary>
            /// Verifies that cleanup disposed the selected manager-owned semaphore.
            /// </summary>
            /// <param name="monitoredItemSemaphore">Whether to inspect the monitored-item semaphore.</param>
            public void AssertCleanupSemaphoreDisposed(bool monitoredItemSemaphore)
            {
                Assert.That(
                    () => (monitoredItemSemaphore ? m_monitoredItemSemaphore : m_writeSemaphore).Wait(0),
                    Throws.InstanceOf<ObjectDisposedException>());
            }

            /// <summary>
            /// Pauses handle resolution until released or cancelled, keeping the write admitted for the lifetime test.
            /// </summary>
            protected override async ValueTask<NodeHandle> GetManagerHandleAsync(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return null!;
            }
        }

        /// <summary>
        /// Blocks symbolic child lookup until the component-cache regression releases it.
        /// </summary>
        private sealed class BlockingComponentNode : BaseObjectState
        {
            /// <summary>
            /// Initializes a blocking component node.
            /// </summary>
            /// <param name="entered">Signals that lookup entered the node.</param>
            /// <param name="release">Releases the blocked lookup.</param>
            public BlockingComponentNode(
                TaskCompletionSource<bool> entered,
                ManualResetEventSlim release)
                : base(null)
            {
                m_entered = entered;
                m_release = release;
            }

            /// <inheritdoc/>
            public override BaseInstanceState? FindChildBySymbolicName(
                ISystemContext context,
                string symbolicPath)
            {
                m_entered.TrySetResult(true);
                if (!m_release.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The component-cache lookup was not released.");
                }

                return null;
            }

            /// <summary>
            /// Signals that lookup entered the node.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_entered;

            /// <summary>
            /// Releases the blocked lookup.
            /// </summary>
            private readonly ManualResetEventSlim m_release;
        }

        /// <summary>
        /// Pauses restoration of a session diagnostics node during a diagnostics transition.
        /// </summary>
        private sealed class BlockingDiagnosticsNodeManager : DiagnosticsNodeManager
        {
            /// <summary>
            /// Initializes a diagnostics manager that can pause restoration.
            /// </summary>
            /// <param name="server">The internal server.</param>
            /// <param name="configuration">The application configuration.</param>
            public BlockingDiagnosticsNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            /// <summary>
            /// Gets the signal raised when restoration reaches predefined-node registration.
            /// </summary>
            public TaskCompletionSource<bool> RestorationEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the gate that allows diagnostics restoration to continue.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseRestoration { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            protected override async ValueTask AddPredefinedNodeAsync(
                ISystemContext context,
                NodeState node,
                CancellationToken cancellationToken = default)
            {
                RestorationEntered.TrySetResult(true);
                await ReleaseRestoration.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await base.AddPredefinedNodeAsync(context, node, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
