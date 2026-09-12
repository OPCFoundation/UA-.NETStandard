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
using System.IO;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Quickstarts.Servers;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Parallelizable]
    public sealed class DurableBatchRestoreRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DataBatchMustBecomeResidentBeforeDequeue(bool persisted)
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableDataChangeMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.ResetQueue(2, false);
            var expected = new DataValue(new Variant(42));
            queue.Enqueue(expected, null);
            DataChangeBatch batch = queue.ToStorableQueue().DequeueBatch;
            if (persisted)
            {
                batch.SetPersisted();
            }
            else
            {
                batch.PersistingInProgress = true;
            }

            Assert.That(queue.Dequeue(out DataValue pending, out ServiceResult error), Is.False);
            Assert.That(pending.IsNull, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));
            persistor.Verify(p => p.RequestBatchRestore(batch), Times.Once);

            batch.PersistingInProgress = false;
            batch.Restore([(expected, null)]);
            Assert.That(queue.Dequeue(out DataValue actual, out _), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EventBatchMustBecomeResidentBeforeDequeue(bool persisted)
        {
            var persistor = new Mock<IBatchPersistor>();
            using var queue = new DurableEventMonitoredItemQueue(
                true, 1, persistor.Object, NUnitTelemetryContext.Create());
            queue.SetQueueSize(2, false);
            var expected = new EventFieldList { ClientHandle = 42 };
            queue.Enqueue(expected);
            EventBatch batch = queue.ToStorableQueue().DequeueBatch;
            if (persisted)
            {
                batch.SetPersisted();
            }
            else
            {
                batch.PersistingInProgress = true;
            }

            Assert.That(queue.Dequeue(out EventFieldList pending), Is.False);
            Assert.That(pending, Is.Null);
            Assert.That(queue.ItemsInQueue, Is.EqualTo(1));
            persistor.Verify(p => p.RequestBatchRestore(batch), Times.Once);

            batch.PersistingInProgress = false;
            batch.Restore([expected]);
            Assert.That(queue.Dequeue(out EventFieldList actual), Is.True);
            Assert.That(actual.ClientHandle, Is.EqualTo(42));
            Assert.That(queue.ItemsInQueue, Is.Zero);
        }

        [Test]
        [NonParallelizable]
        public void RestoreClosesItsReaderBeforeDeletingTheBatchFile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable scope = AmbientMessageContext.SetScopedContext(
                ServiceMessageContext.Create(telemetry));
            var expected = new DataValue(new Variant(42));
            var batch = new DataChangeBatch([(expected, null)], 1, 42);
            var persistor = new BatchPersistor(telemetry);
            string path = Path.Combine(
                Environment.CurrentDirectory,
                "Durable Subscriptions",
                "Batches",
                $"{batch.MonitoredItemId}_{batch.Id}_batch.bin");
            try
            {
                persistor.PersistSynchronously(batch);
                Assert.That(batch.IsPersisted, Is.True);
                Assert.That(File.Exists(path), Is.True);

                persistor.RestoreSynchronously(batch);

                Assert.That(batch.IsPersisted, Is.False);
                Assert.That(batch.Values, Has.Count.EqualTo(1));
                Assert.That(batch.Values[0].Item1, Is.EqualTo(expected));
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
