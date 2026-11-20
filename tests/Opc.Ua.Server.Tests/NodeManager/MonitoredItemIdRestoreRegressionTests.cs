// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Concurrent;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies monotonic monitored-item identifier restoration, concurrent uniqueness, and nonzero wraparound.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class MonitoredItemIdRestoreRegressionTests
    {
        /// <summary>
        /// Verifies that restoring an older or equal identifier never moves the allocation counter backward.
        /// </summary>
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

        /// <summary>
        /// Verifies that concurrent lower restore values cannot duplicate identifiers or reduce the active counter.
        /// </summary>
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

        /// <summary>
        /// Verifies that allocation wraps from the maximum identifier to one while reserving zero.
        /// </summary>
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
