/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies the routing view borrowed by a derived master's legacy lookup.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerLegacyLookupScopeTests
    {
        /// <summary>
        /// A captured lookup uses the pinned view until its override returns or throws.
        /// Later calls from that context use the current view.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyLookupReleasesCapturedViewAfterCompletionAsync(bool fail)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);

                var table = new NodeManagerRoutingTable();
                await using var master = new PausedLookupMaster(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    table);
                server.SetupGet(value => value.NodeManager).Returns(master);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                var routing = (IRequestRouting)master;
                routing.PinRequest(context);
                NodeManagerRoutingTable.RoutingSnapshot pinned = context.RoutingSnapshot!;
                var nodeId = new NodeId("CapturedLookup", 1);
                object handle = new();
                var published = new Mock<IAsyncNodeManager>();
                published.Setup(value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(handle);

                Task<(object? handle, IAsyncNodeManager? nodeManager)> pending =
                    routing.GetManagerHandleAsync(context, nodeId, timeout.Token).AsTask();
                try
                {
                    ExecutionContext captured = await master.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                    table.Add(published.Object, [1]);
                    Assert.That(context.RoutingSnapshot, Is.SameAs(pinned));

                    Task<(object? handle, IAsyncNodeManager? nodeManager)>? active = null;
                    ExecutionContext.Run(
                        captured.CreateCopy(),
                        _ => active = master.LookupBaseAsync(nodeId, timeout.Token).AsTask(),
                        null);
                    Assert.That(active, Is.Not.Null);
                    (object? activeHandle, IAsyncNodeManager? activeOwner) =
                        await active!.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(activeHandle, Is.Null);
                    Assert.That(activeOwner, Is.Null);
                    published.Verify(
                        value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()),
                        Times.Never);

                    master.Complete(fail);
                    if (fail)
                    {
                        await Assert.ThatAsync(
                            async () =>
                            {
                                await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                            },
                            Throws.TypeOf<InvalidOperationException>()
                                .With.Message.EqualTo(PausedLookupMaster.Error)).ConfigureAwait(false);
                    }
                    else
                    {
                        (object? completedHandle, IAsyncNodeManager? completedOwner) =
                            await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(completedHandle, Is.Null);
                        Assert.That(completedOwner, Is.Null);
                    }

                    Task<(object? handle, IAsyncNodeManager? nodeManager)>? expired = null;
                    ExecutionContext.Run(
                        captured.CreateCopy(),
                        _ => expired = master.LookupBaseAsync(nodeId, timeout.Token).AsTask(),
                        null);
                    Assert.That(expired, Is.Not.Null);
                    (object? currentHandle, IAsyncNodeManager? currentOwner) =
                        await expired!.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Assert.That(currentHandle, Is.SameAs(handle));
                    Assert.That(currentOwner, Is.SameAs(published.Object));
                    published.Verify(
                        value => value.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()),
                        Times.Once);
                }
                finally
                {
                    master.Complete(false);
                    if (!pending.IsCompleted)
                    {
                        await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Captures a legacy lookup context and keeps the override active until released.
        /// </summary>
        private sealed class PausedLookupMaster : MasterNodeManager
        {
            /// <summary>
            /// Creates a master using the supplied routing owner.
            /// </summary>
            public PausedLookupMaster(
                IServerInternal server,
                ApplicationConfiguration configuration,
                NodeManagerRoutingTable table)
                : base(server, configuration, table)
            {
            }

            /// <summary>
            /// The execution context captured by the active override.
            /// </summary>
            public Task<ExecutionContext> Entered => m_entered.Task;

            /// <summary>
            /// Ends the override with either an empty result or the expected failure.
            /// </summary>
            public void Complete(bool fail)
            {
                if (fail)
                {
                    m_result.TrySetException(new InvalidOperationException(Error));
                }
                else
                {
                    m_result.TrySetResult((null, null));
                }
            }

            /// <summary>
            /// Invokes the base lookup in the caller's execution context.
            /// </summary>
            public ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> LookupBaseAsync(
                NodeId nodeId,
                CancellationToken cancellationToken)
            {
                return base.GetManagerHandleAsync(nodeId, cancellationToken);
            }

            /// <inheritdoc/>
            public override ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(
                    ExecutionContext.Capture()
                    ?? throw new InvalidOperationException("Execution context is unavailable."));
                return new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>(m_result.Task);
            }

            /// <summary>The exception message used by the failure case.</summary>
            public const string Error = "Legacy lookup failed.";

            /// <summary>The context captured by the override.</summary>
            private readonly TaskCompletionSource<ExecutionContext> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The result that releases the override.</summary>
            private readonly TaskCompletionSource<(object? handle, IAsyncNodeManager? nodeManager)> m_result =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
