// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Opc.Ua.PubSub.Scheduling;

namespace Opc.Ua.PubSub.Tests.Benchmarks
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
