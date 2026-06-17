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
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Opc.Ua.PubSub.Scheduling;

namespace Opc.Ua.PubSub.Bench
{
    /// <summary>
    /// Scheduler tick dispatch latency under load. Registers
    /// <see cref="TaskCount"/> schedules with periodic 1 ms callbacks
    /// and measures the time it takes for one full tick burst to
    /// drain (every callback acquires a global counter once).
    /// </summary>
    /// <remarks>
    /// Implements the periodic publishing model required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Periodic publishing</see>.
    /// </remarks>
    [MemoryDiagnoser]
    public class SchedulerBenchmarks
    {
        private PubSubScheduler m_scheduler = null!;

        /// <summary>
        /// Number of independent schedules to register before
        /// measuring tick dispatch.
        /// </summary>
        [Params(1, 10, 100, 1000)]
        public int TaskCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_scheduler = new PubSubScheduler();
        }

        [Benchmark]
        public async Task<int> RegisterAndDispatchAsync()
        {
            int counter = 0;
            var registrations = new IAsyncDisposable[TaskCount];
            var schedule = new PubSubSchedule(
                period: TimeSpan.FromMilliseconds(1),
                keepAliveTime: TimeSpan.FromSeconds(60),
                publishingOffset: TimeSpan.Zero,
                receiveOffset: TimeSpan.Zero);

            for (int i = 0; i < TaskCount; i++)
            {
                registrations[i] = await m_scheduler.ScheduleAsync(
                    schedule,
                    _ =>
                    {
                        Interlocked.Increment(ref counter);
                        return default;
                    }).ConfigureAwait(false);
            }

            // Allow at least one tick to fire on every registration.
            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);

            for (int i = 0; i < TaskCount; i++)
            {
                await registrations[i].DisposeAsync().ConfigureAwait(false);
            }
            return counter;
        }
    }
}
