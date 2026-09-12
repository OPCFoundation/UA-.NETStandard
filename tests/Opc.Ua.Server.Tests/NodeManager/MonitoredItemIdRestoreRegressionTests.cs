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

using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class MonitoredItemIdRestoreRegressionTests
    {
        [Test]
        public void RestoringLowerMonitoredItemIdsDoesNotMoveCounterBackwards()
        {
            var factory = new MonitoredItemIdFactory();
            factory.SetStartValue(100);
            Assert.That(factory.GetNextId(), Is.EqualTo(101));

            factory.SetStartValue(10);
            factory.SetStartValue(0);
            Assert.That(factory.GetNextId(), Is.EqualTo(102));

            factory.SetStartValue(200);
            Assert.That(factory.GetNextId(), Is.EqualTo(201));
            factory.SetStartValue(200);
            Assert.That(factory.GetNextId(), Is.EqualTo(202));
        }

        [Test]
        public async Task RestoringLowerMonitoredItemIdsDuringConcurrentAllocationsKeepsIdsUniqueAsync()
        {
            var factory = new MonitoredItemIdFactory();
            factory.SetStartValue(1000);
            var ids = new ConcurrentBag<uint>();
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = new Task[4];
            for (int ii = 0; ii < tasks.Length; ii++)
            {
                tasks[ii] = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    for (int jj = 0; jj < 64; jj++)
                    {
                        factory.SetStartValue(100);
                        ids.Add(factory.GetNextId());
                    }
                });
            }

            start.SetResult(true);
            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ids, Has.Count.EqualTo(256));
                Assert.That(ids, Is.Unique);
                Assert.That(ids, Has.All.GreaterThan(1000u));
                Assert.That(factory.GetNextId(), Is.EqualTo(1257));
            });
        }

        [Test]
        public void RestoredMonitoredItemCounterStillWrapsWithoutReturningZero()
        {
            var factory = new MonitoredItemIdFactory();
            factory.SetStartValue(uint.MaxValue - 1);

            Assert.Multiple(() =>
            {
                Assert.That(factory.GetNextId(), Is.EqualTo(uint.MaxValue));
                Assert.That(factory.GetNextId(), Is.EqualTo(1));
                Assert.That(factory.GetNextId(), Is.EqualTo(2));
            });
        }
    }
}
