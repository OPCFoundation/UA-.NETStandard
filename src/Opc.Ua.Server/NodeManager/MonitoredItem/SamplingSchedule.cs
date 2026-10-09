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

namespace Opc.Ua.Server
{
    /// <summary>
    /// The fixed-rate schedule of a sampling group, in timestamp ticks.
    /// </summary>
    /// <remarks>
    /// Each sample has an absolute deadline one interval after the previous one. Waiting a
    /// fixed interval after each sample instead adds the timer overshoot (about 1 ms on
    /// Linux, up to the 15.6 ms timer resolution on Windows) and the sampling time to every
    /// cycle, so a 10 ms group sampled about 63 to 90 times per second. With deadlines a
    /// late wake-up shortens the next wait, which keeps the average rate at the requested
    /// interval. Missed deadlines are made up by sampling again right away, but not after a
    /// stall longer than <see cref="MaxLag"/>: the schedule then restarts from the current
    /// time instead of bursting.
    /// </remarks>
    internal struct SamplingSchedule
    {
        /// <summary>
        /// Creates a schedule whose first sample is one period after <paramref name="now"/>.
        /// </summary>
        public SamplingSchedule(long now, long period, long maxLag)
        {
            Period = Math.Max(1, period);
            MaxLag = maxLag;
            Deadline = now + Period;
        }

        /// <summary>
        /// The ticks between two samples.
        /// </summary>
        public long Period { get; }

        /// <summary>
        /// How far the schedule may fall behind before it restarts.
        /// </summary>
        public long MaxLag { get; }

        /// <summary>
        /// The timestamp the next sample is due at.
        /// </summary>
        public long Deadline { get; private set; }

        /// <summary>
        /// Creates the schedule of a sampling interval in milliseconds. It tolerates a lag of
        /// four intervals or half a second, whichever is longer, so a garbage collection or
        /// scheduling pause of that length is made up rather than lost.
        /// </summary>
        public static SamplingSchedule Create(double samplingIntervalMs, long frequency, long now)
        {
            long period = Math.Max(1, (long)(samplingIntervalMs * frequency / 1000.0));
            return new SamplingSchedule(now, period, Math.Max(4 * period, frequency / 2));
        }

        /// <summary>
        /// The ticks to wait before the next sample; zero or less when it is due.
        /// </summary>
        public readonly long GetWait(long now)
        {
            return Deadline - now;
        }

        /// <summary>
        /// Moves to the deadline after the sample just taken.
        /// </summary>
        /// <returns><c>true</c> when the schedule had fallen too far behind and restarted.</returns>
        public bool Advance(long now)
        {
            Deadline += Period;
            if (now - Deadline > MaxLag)
            {
                Deadline = now + Period;
                return true;
            }
            return false;
        }
    }
}
