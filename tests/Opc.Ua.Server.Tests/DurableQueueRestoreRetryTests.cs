// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;
using Quickstarts.Servers;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A durable subscription restore that fails is retried on the next
    /// start. The stored queue snapshots must survive the failed attempt, so
    /// that the retry still delivers the queued notifications; they are only
    /// removed once the restore completes.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class DurableQueueRestoreRetryTests
    {
        [Test]
        public void QueueSnapshotsSurviveARestoreThatIsRetried()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string directory = Path.Combine(
                Path.GetTempPath(),
                "DurableQueueRestoreRetry_" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var running = new DurableMonitoredItemQueueFactory(
                    telemetry, ServiceMessageContext.Create(telemetry)))
                {
                    IDataChangeMonitoredItemQueue values = running.CreateDataChangeQueue(true, 101);
                    values.ResetQueue(10, false);
                    values.Enqueue(new DataValue(new Variant(1)), null!);
                    values.Enqueue(new DataValue(new Variant(2)), null!);
                    IEventMonitoredItemQueue events = running.CreateEventQueue(true, 102);
                    events.SetQueueSize(10, false);
                    events.Enqueue(new EventFieldList { ClientHandle = 7 });
                    running.PersistQueues([101u, 102u], directory);
                }

                // A first start restores the queues, but another subscription
                // fails, so the restore does not complete.
                using (var failedStart = new DurableMonitoredItemQueueFactory(
                    telemetry, ServiceMessageContext.Create(telemetry)))
                {
                    Assert.That(failedStart.RestoreDataChangeQueue(101, directory), Is.Not.Null);
                    Assert.That(failedStart.RestoreEventQueue(102, directory), Is.Not.Null);
                }

                using var retry = new DurableMonitoredItemQueueFactory(
                    telemetry, ServiceMessageContext.Create(telemetry));
                IDataChangeMonitoredItemQueue? restoredValues = retry.RestoreDataChangeQueue(101, directory);
                IEventMonitoredItemQueue? restoredEvents = retry.RestoreEventQueue(102, directory);
                Assert.That(restoredValues, Is.Not.Null, "the data change snapshot survives the failed restore");
                Assert.That(restoredEvents, Is.Not.Null, "the event snapshot survives the failed restore");

                Assert.That(restoredValues!.Dequeue(out DataValue first, out _), Is.True);
                Assert.That(restoredValues.Dequeue(out DataValue second, out _), Is.True);
                Assert.That(restoredEvents!.Dequeue(out QueuedEvent eventFields), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(first.WrappedValue, Is.EqualTo(new Variant(1)));
                    Assert.That(second.WrappedValue, Is.EqualTo(new Variant(2)));
                    Assert.That(eventFields.ClientHandle, Is.EqualTo(7u));
                });

                // Once the restore completes, the snapshots are removed.
                retry.CleanStoredQueues(directory, [101u, 102u]);
                Assert.That(retry.RestoreDataChangeQueue(101, directory), Is.Null);
                Assert.That(retry.RestoreEventQueue(102, directory), Is.Null);
                restoredValues.Dispose();
                restoredEvents.Dispose();
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
