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
using System.Diagnostics;
using System.Threading;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Lock-free throughput bookkeeping for the Subscription Bench: a monotonic
/// cumulative value/error total plus a 60-entry, one-second ring buffer used to
/// compute the 1 s / 10 s / 30 s / 60 s rolling averages. The notification
/// hot path calls <see cref="Record"/>; the 1 Hz aggregation timer calls
/// <see cref="Rotate"/>. Both are safe to call concurrently: writes use
/// <see cref="Interlocked"/> and a single-bucket drift at the rollover boundary
/// is tolerated because it does not change the cumulative totals.
/// </summary>
internal sealed class BenchThroughputCounters
{
    /// <summary>One-second resolution history depth (60 s window).</summary>
    public const int BucketCount = 60;

    private readonly int[] m_bucketsPerSec = new int[BucketCount];
    private long m_totalValues;
    private long m_totalErrors;
    private int m_currentBucket;
    private long m_runStartTicks = Stopwatch.GetTimestamp();

    /// <summary>
    /// Aggregated one-second snapshot produced by <see cref="Rotate"/>.
    /// </summary>
    public readonly record struct Sample(
        double Seconds,
        int Last1s,
        double Avg10s,
        double Avg30s,
        double Avg60s,
        long TotalValues,
        long TotalErrors);

    /// <summary>
    /// Cumulative number of values delivered since the last <see cref="Reset"/>.
    /// </summary>
    public long TotalValues => Interlocked.Read(ref m_totalValues);

    /// <summary>
    /// Cumulative number of bad-status values observed since the last
    /// <see cref="Reset"/>.
    /// </summary>
    public long TotalErrors => Interlocked.Read(ref m_totalErrors);

    /// <summary>
    /// Records a batch of delivered values, of which <paramref name="badValues"/>
    /// carried a bad status code. Called from the notification dispatcher thread.
    /// </summary>
    public void Record(int values, int badValues)
    {
        if (values > 0)
        {
            Interlocked.Add(ref m_totalValues, values);
            int idx = Volatile.Read(ref m_currentBucket);
            Interlocked.Add(ref m_bucketsPerSec[idx], values);
        }
        if (badValues > 0)
        {
            Interlocked.Add(ref m_totalErrors, badValues);
        }
    }

    /// <summary>
    /// Advances the ring buffer by one second and returns the aggregated
    /// snapshot for the second that just elapsed. Called by the aggregation timer.
    /// </summary>
    public Sample Rotate()
    {
        int prev = m_currentBucket;
        int next = (prev + 1) % BucketCount;
        Volatile.Write(ref m_bucketsPerSec[next], 0);
        Volatile.Write(ref m_currentBucket, next);

        int last1 = Volatile.Read(ref m_bucketsPerSec[prev]);
        double avg10 = AverageOver(prev, 10);
        double avg30 = AverageOver(prev, 30);
        double avg60 = AverageOver(prev, 60);
        double seconds = (Stopwatch.GetTimestamp() - Interlocked.Read(ref m_runStartTicks))
            / (double)Stopwatch.Frequency;
        return new Sample(seconds, last1, avg10, avg30, avg60, TotalValues, TotalErrors);
    }

    /// <summary>
    /// Clears the cumulative totals and the ring buffer and restarts the elapsed
    /// clock, so the next <see cref="Rotate"/> reports a fresh run.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref m_totalValues, 0);
        Interlocked.Exchange(ref m_totalErrors, 0);
        for (int i = 0; i < m_bucketsPerSec.Length; i++)
        {
            Volatile.Write(ref m_bucketsPerSec[i], 0);
        }
        Volatile.Write(ref m_currentBucket, 0);
        Interlocked.Exchange(ref m_runStartTicks, Stopwatch.GetTimestamp());
    }

    private double AverageOver(int latestBucket, int window)
    {
        long sum = 0;
        for (int i = 0; i < window && i < BucketCount; i++)
        {
            int idx = latestBucket - i;
            if (idx < 0)
            {
                idx += BucketCount;
            }
            sum += Volatile.Read(ref m_bucketsPerSec[idx]);
        }
        return sum / (double)window;
    }
}
