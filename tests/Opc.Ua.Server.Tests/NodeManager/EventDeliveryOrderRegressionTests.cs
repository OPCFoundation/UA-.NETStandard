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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("MonitoredNode")]
    public sealed class EventDeliveryOrderRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ParallelEventItemsKeepEnqueueOrderWhileIndependentItemsProgressAsync(bool serverNode)
        {
            var node = new BaseObjectState(null) { NodeId = serverNode ? ObjectIds.Server : new NodeId(100, 1) };
            var server = new Mock<IServerInternal>();
            var manager = new Mock<IAsyncNodeManager>();
            IFilterTarget first = Mock.Of<IFilterTarget>();
            IFilterTarget second = Mock.Of<IFilterTarget>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var independentFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int blocked = 0;
            int delivered = 0;
            manager.Setup(value => value.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (IEventMonitoredItem _, IFilterTarget target, CancellationToken ct) =>
                {
                    if (ReferenceEquals(target, first) && Interlocked.CompareExchange(ref blocked, 1, 0) == 0)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return ServiceResult.Good;
                });
            using var monitored = new MonitoredNode2(manager.Object, server.Object, node, !serverNode);
            var queues = new ConcurrentQueue<int>[2];
            for (int i = 0; i < queues.Length; i++)
            {
                var queue = new ConcurrentQueue<int>();
                queues[i] = queue;
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns((uint)i + 1);
                item.Setup(value => value.QueueEvent(It.IsAny<IFilterTarget>()))
                    .Callback<IFilterTarget>(target =>
                    {
                        int value = ReferenceEquals(target, first) ? 1 : 2;
                        queue.Enqueue(value);
                        if (value == 1 && !release.Task.IsCompleted)
                        {
                            independentFirst.TrySetResult(true);
                        }
                        if (value == 2)
                        {
                            secondEntered.TrySetResult(true);
                        }
                        if (Interlocked.Increment(ref delivered) == 4)
                        {
                            finished.TrySetResult(true);
                        }
                    });
                monitored.Add(item.Object);
            }
            ISystemContext context = Mock.Of<ISystemContext>();
            await monitored.OnReportEventAsync(context, node, first).ConfigureAwait(false);
            bool independentProgress = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await monitored.OnReportEventAsync(context, node, second).ConfigureAwait(false);
                await Task.WhenAny(independentFirst.Task, secondEntered.Task)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                independentProgress = independentFirst.Task.IsCompleted;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(independentProgress, Is.True);
                Assert.That(queues[0].ToArray(), Is.EqualTo(s_order));
                Assert.That(queues[1].ToArray(), Is.EqualTo(s_order));
            });
        }

        private static readonly int[] s_order = [1, 2];
    }
}
