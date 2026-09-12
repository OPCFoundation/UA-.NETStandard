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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    public sealed class NodeManagerDisposalRegressionTests
    {
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
            Task queued = null;
            Task disposal = null;
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
                    Assert.That(() => queued.WaitAsync(TimeSpan.FromSeconds(5)),
                        Throws.InstanceOf<ObjectDisposedException>());
                }
                if (disposal != null)
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                cancellation.Dispose();
            }
            Assert.That(manager.RetainedNodes, Is.Zero);
        }

        [TestCase("m_writeSemaphore")]
        [TestCase("m_monitoredItemSemaphore")]
        [TestCase("m_componentCacheSemaphore")]
        [TestCase("m_modifyAddressSpaceSemaphoreSlim")]
        public async Task CleanupWaitsForTheActualSemaphoreOwnerBeforeDisposingAsync(string field)
        {
            IServerInternal server = NewServer();
            bool diagnostics = field == "m_modifyAddressSpaceSemaphoreSlim";
            AsyncCustomNodeManager manager = diagnostics
                ? new DiagnosticsNodeManager(server, new ApplicationConfiguration { ServerConfiguration = new() })
                : new BlockingNodeManager(server);
            SemaphoreSlim gate = ReadGate(manager, field, diagnostics);
            await gate.WaitAsync().ConfigureAwait(false);
            Task disposal = null;
            try
            {
                disposal = DisposeAsync(manager);
                Assert.That(disposal.IsCompleted, Is.False);
            }
            finally
            {
                Assert.That(() => gate.Release(), Throws.Nothing);
                if (disposal != null)
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            Assert.That(() => gate.Wait(0), Throws.InstanceOf<ObjectDisposedException>());
            await DisposeAsync(manager).ConfigureAwait(false);
        }

        [Test]
        public async Task QueuedDiagnosticsMutationCannotReleaseOrUseADisposedGateAsync()
        {
            IServerInternal server = NewServer();
            var manager = new DiagnosticsNodeManager(server,
                new ApplicationConfiguration { ServerConfiguration = new() });
            SemaphoreSlim gate = ReadGate(manager, "m_modifyAddressSpaceSemaphoreSlim", true);
            await gate.WaitAsync().ConfigureAwait(false);
            var cancellation = new CancellationTokenSource();
            Task queued = manager.SetDiagnosticsEnabledAsync(
                server.DefaultSystemContext, true, cancellation.Token).AsTask();
            Task disposal = DisposeAsync(manager);
            try
            {
                Assert.That(disposal.IsCompleted, Is.False);
                gate.Release();
                ObjectDisposedException failure = null;
                try
                {
                    await queued.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (ObjectDisposedException error)
                {
                    failure = error;
                }
                Assert.That(failure, Is.Not.Null);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                cancellation.Cancel();
                await DisposeAsync(manager).ConfigureAwait(false);
                cancellation.Dispose();
            }
        }

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
            Task disposal = null;
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

        private static SemaphoreSlim ReadGate(AsyncCustomNodeManager manager, string name, bool diagnostics)
        {
            Type type = diagnostics ? typeof(DiagnosticsNodeManager) : typeof(AsyncCustomNodeManager);
            return (SemaphoreSlim)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        }

        private static Task DisposeAsync(AsyncCustomNodeManager manager)
        {
            if (manager is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync().AsTask();
            }
            manager.Dispose();
            return Task.CompletedTask;
        }

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

        private sealed class BlockingNodeManager : AsyncCustomNodeManager
        {
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

            public int RetainedNodes => PredefinedNodes.Count;
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async ValueTask<NodeHandle> GetManagerHandleAsync(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }
    }
}
