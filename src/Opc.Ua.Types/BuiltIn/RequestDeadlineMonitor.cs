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
using System.Diagnostics;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Cancels <see cref="RequestLifetime"/>s whose timeout elapsed or whose linked tokens were
    /// cancelled, with one periodic sweep for all requests instead of a timer and token
    /// registrations per request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server request used to create a timer for its TimeoutHint and register with two
    /// long-lived tokens (the server's request queue and the transport's). Every worker thread
    /// registered and unregistered with the same two token sources, whose registration lock
    /// then showed as spinning in profiles, and the timer cost a TimerQueueTimer and a timer
    /// queue update per request.
    /// </para>
    /// <para>
    /// Lifetimes are kept in intrusive lists, one per shard with its own lock, chosen by the
    /// current processor, so adding and removing take an uncontended lock and allocate
    /// nothing. The sweep runs every <see cref="Resolution"/> while any lifetime is monitored
    /// and stops when none is. A timeout therefore fires up to one resolution late, and a
    /// cancelled linked token reaches the request's token up to one resolution later than a
    /// registration would; <see cref="RequestLifetime.TryCancel"/> checks the linked tokens
    /// itself, so an earlier external cancellation still owns the terminal state.
    /// </para>
    /// </remarks>
    internal sealed class RequestDeadlineMonitor : IDisposable
    {
        /// <summary>
        /// Creates a monitor.
        /// </summary>
        /// <param name="resolution">The sweep period.</param>
        /// <param name="clock">The clock in <see cref="Stopwatch"/> ticks, or <c>null</c> for
        /// <see cref="Stopwatch.GetTimestamp"/>.</param>
        /// <param name="automatic"><c>false</c> to sweep only when <see cref="Sweep"/> is
        /// called, for tests.</param>
        internal RequestDeadlineMonitor(
            TimeSpan resolution,
            Func<long>? clock = null,
            bool automatic = true)
        {
            Resolution = resolution;
            m_clock = clock ?? Stopwatch.GetTimestamp;
            int count = 4;
            while (count < Environment.ProcessorCount && count < 64)
            {
                count <<= 1;
            }
            m_shards = new Shard[count];
            for (int ii = 0; ii < count; ii++)
            {
                m_shards[ii] = new Shard();
            }
            m_mask = count - 1;
            if (automatic)
            {
                m_timer = new Timer(
                    static state => ((RequestDeadlineMonitor)state!).OnTimer(),
                    this,
                    Timeout.Infinite,
                    Timeout.Infinite);
            }
        }

        /// <summary>
        /// The monitor used by request lifetimes, with a resolution of 20 ms.
        /// </summary>
        public static RequestDeadlineMonitor Shared { get; } = new(TimeSpan.FromMilliseconds(20));

        /// <summary>
        /// The sweep period, and so the precision of timeouts and linked token cancellation.
        /// </summary>
        public TimeSpan Resolution { get; }

        /// <summary>
        /// The current time in <see cref="Stopwatch"/> ticks.
        /// </summary>
        public long Now => m_clock();

        /// <summary>
        /// Returns the deadline for a timeout that starts now.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        public long GetDeadline(TimeSpan timeout)
        {
            double ticks = timeout.TotalSeconds * Stopwatch.Frequency;
            long now = Now;
            return ticks >= long.MaxValue - now ? long.MaxValue : now + (long)ticks;
        }

        /// <summary>
        /// The number of monitored lifetimes; it takes every shard lock, for tests.
        /// </summary>
        internal int Count
        {
            get
            {
                int count = 0;
                foreach (Shard shard in m_shards)
                {
                    lock (shard.Lock)
                    {
                        count += shard.Count;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Starts monitoring a lifetime.
        /// </summary>
        /// <param name="lifetime">The lifetime, which is not monitored yet.</param>
        public void Add(RequestLifetime lifetime)
        {
            Shard shard = m_shards[GetShardIndex()];
            lock (shard.Lock)
            {
                lifetime.m_monitorShard = shard;
                lifetime.m_monitorPrevious = null;
                lifetime.m_monitorNext = shard.Head;
                if (shard.Head != null)
                {
                    shard.Head.m_monitorPrevious = lifetime;
                }
                shard.Head = lifetime;
                shard.Count++;
            }

            // The sweep that stops the timer for lack of lifetimes clears the flag and then
            // looks at every shard again: it sees this lifetime, or this sees the flag cleared.
            Interlocked.MemoryBarrier();
            if (Volatile.Read(ref m_running) == 0)
            {
                Start();
            }
        }

        /// <summary>
        /// Stops monitoring a lifetime. A lifetime that is not monitored (any more) is ignored.
        /// </summary>
        /// <param name="lifetime">The lifetime.</param>
        public void Remove(RequestLifetime lifetime)
        {
            Shard? shard = Volatile.Read(ref lifetime.m_monitorShard);
            if (shard == null)
            {
                return;
            }

            lock (shard.Lock)
            {
                // the sweep may have taken it out in the meantime.
                if (lifetime.m_monitorShard == shard)
                {
                    Unlink(shard, lifetime);
                }
            }
        }

        /// <summary>
        /// Cancels the lifetimes whose deadline passed or whose linked token was cancelled.
        /// </summary>
        /// <returns>The number of lifetimes this sweep took out of the monitor.</returns>
        internal int Sweep()
        {
            long now = Now;
            List<RequestLifetime>? due = null;
            int remaining = 0;
            foreach (Shard shard in m_shards)
            {
                lock (shard.Lock)
                {
                    RequestLifetime? current = shard.Head;
                    while (current != null)
                    {
                        RequestLifetime? next = current.m_monitorNext;
                        if (current.IsDue(now))
                        {
                            Unlink(shard, current);
                            (due ??= []).Add(current);
                        }
                        current = next;
                    }
                    remaining += shard.Count;
                }
            }

            // Cancelled outside the shard locks: cancellation runs the request's callbacks,
            // which may complete the lifetime or start others.
            if (due != null)
            {
                foreach (RequestLifetime lifetime in due)
                {
                    lifetime.OnDue();
                }
            }

            if (remaining == 0 && m_timer != null)
            {
                StopIfIdle();
            }

            return due?.Count ?? 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_timer?.Dispose();
        }

        private void OnTimer()
        {
            // a slow sweep must not overlap the next one.
            if (Interlocked.Exchange(ref m_sweeping, 1) != 0)
            {
                return;
            }

            try
            {
                Sweep();
            }
            finally
            {
                Volatile.Write(ref m_sweeping, 0);
            }
        }

        private void Start()
        {
            if (m_timer != null && Interlocked.CompareExchange(ref m_running, 1, 0) == 0)
            {
                m_timer.Change(Resolution, Resolution);
            }
        }

        private void StopIfIdle()
        {
            if (Interlocked.CompareExchange(ref m_running, 0, 1) != 1)
            {
                return;
            }

            m_timer!.Change(Timeout.Infinite, Timeout.Infinite);

            // A lifetime added while the flag was still set did not start the timer.
            foreach (Shard shard in m_shards)
            {
                lock (shard.Lock)
                {
                    if (shard.Count != 0)
                    {
                        Start();
                        return;
                    }
                }
            }
        }

        private static void Unlink(Shard shard, RequestLifetime lifetime)
        {
            if (lifetime.m_monitorPrevious != null)
            {
                lifetime.m_monitorPrevious.m_monitorNext = lifetime.m_monitorNext;
            }
            else
            {
                shard.Head = lifetime.m_monitorNext;
            }

            if (lifetime.m_monitorNext != null)
            {
                lifetime.m_monitorNext.m_monitorPrevious = lifetime.m_monitorPrevious;
            }

            lifetime.m_monitorPrevious = null;
            lifetime.m_monitorNext = null;
            Volatile.Write(ref lifetime.m_monitorShard, null);
            shard.Count--;
        }

        private int GetShardIndex()
        {
#if NETCOREAPP3_0_OR_GREATER
            return Thread.GetCurrentProcessorId() & m_mask;
#else
            return Environment.CurrentManagedThreadId & m_mask;
#endif
        }

        /// <summary>
        /// One list of monitored lifetimes and its lock. The padding keeps the locks of
        /// neighbouring shards, which are allocated one after the other, off a shared cache line.
        /// </summary>
        internal sealed class Shard
        {
            public Lock Lock { get; } = new();
            public RequestLifetime? Head { get; set; }
            public int Count { get; set; }
            public byte[] Padding { get; } = new byte[64];
        }

        private readonly Func<long> m_clock;
        private readonly Shard[] m_shards;
        private readonly int m_mask;
        private readonly Timer? m_timer;
        private int m_running;
        private int m_sweeping;
    }
}
