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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the in-memory monitored item queues and their handlers.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemQueueHandlerTests
    {
        [Test]
        public void EventQueueDuplicateCheckScansNewestEntries()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queue = new EventMonitoredItemQueue(false, 1, telemetry);
            queue.SetQueueSize(5000, true);

            for (int i = 0; i < 1500; i++)
            {
                queue.Enqueue(new EventFieldList
                {
                    EventFields = [new Variant(i)],
                    Handle = new AuditSessionEventState(null)
                });
            }

            var newest = new AuditSessionEventState(null);
            queue.Enqueue(new EventFieldList { EventFields = [new Variant(true)], Handle = newest });

            Assert.That(queue.IsEventContainedInQueue(newest), Is.True);
            Assert.That(
                queue.IsEventContainedInQueue(new AuditSessionEventState(null)),
                Is.False);
        }
    }
}
