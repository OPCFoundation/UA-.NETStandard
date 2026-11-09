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
using Opc.Ua;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Bounded, thread-safe counters for observed operation latencies. Seventy
/// log-spaced buckets cover latencies below 10 seconds; the final bucket
/// counts all latencies at or above 10 seconds. No individual samples are retained.
/// </summary>
internal sealed class LatencyHistogram
{
    /// <summary>
    /// Total recorded samples, including the overflow bucket.
    /// </summary>
    public long Count
    {
        get
        {
            lock (m_gate)
            {
                return m_count;
            }
        }
    }

    /// <summary>
    /// Samples at or above <see cref="MaxMs"/>.
    /// </summary>
    public long Overflow
    {
        get
        {
            lock (m_gate)
            {
                return m_buckets[^1];
            }
        }
    }

    /// <summary>
    /// Highest latency observed since the last reset, in milliseconds.
    /// </summary>
    public double MaxMsObserved
    {
        get
        {
            lock (m_gate)
            {
                return m_maxMs;
            }
        }
    }

    /// <summary>
    /// Record a finite, nonnegative latency. The first bucket includes zero
    /// and sub-microsecond measurements; overflow has no finite upper bound.
    /// </summary>
    public void Record(double latencyMs)
    {
        int idx = BucketIndex(latencyMs);
        lock (m_gate)
        {
            m_buckets[idx]++;
            m_count++;
            m_meanMs += (latencyMs - m_meanMs) / m_count;
            m_maxMs = Math.Max(m_maxMs, latencyMs);
        }
    }

    /// <summary>
    /// Returns a bucket upper-bound percentile estimate, capped by the observed
    /// maximum. Overflow percentiles use that maximum, not an invented bucket edge.
    /// </summary>
    public double GetPercentile(double p)
    {
        lock (m_gate)
        {
            return BenchmarkDistribution.GetPercentile(m_buckets, m_count, m_maxMs, p);
        }
    }

    /// <summary>
    /// Resets the counters, measured mean and observed maximum together.
    /// </summary>
    public void Reset()
    {
        lock (m_gate)
        {
            Array.Clear(m_buckets);
            m_count = 0;
            m_meanMs = 0;
            m_maxMs = 0;
        }
    }

    /// <summary>
    /// Copies a consistent snapshot into a caller-owned rendering buffer.
    /// </summary>
    public long Snapshot(Span<long> destination)
    {
        if (destination.Length < BucketCount)
        {
            throw new ArgumentException(
                $"destination must have at least {BucketCount} entries.", nameof(destination));
        }
        lock (m_gate)
        {
            m_buckets.AsSpan().CopyTo(destination);
            return m_count;
        }
    }

    /// <summary>
    /// Freezes genuine counts and measured moments without retaining sample arrays.
    /// </summary>
    public BenchmarkDistribution Capture()
    {
        lock (m_gate)
        {
            return new BenchmarkDistribution(new ArrayOf<long>(m_buckets), m_meanMs, m_maxMs);
        }
    }

    /// <summary>
    /// Inclusive lower edge in milliseconds. The first bucket starts at zero.
    /// </summary>
    public static double BucketLowerMs(int i)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, BucketCount);
        return i == 0 ? 0 : MinMs * Math.Pow(10.0, i * Step);
    }

    /// <summary>
    /// Exclusive upper edge in milliseconds; overflow is unbounded.
    /// </summary>
    public static double BucketUpperMs(int i)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, BucketCount);
        return i == BucketCount - 1 ? double.PositiveInfinity : MinMs * Math.Pow(10.0, (i + 1) * Step);
    }

    /// <summary>
    /// Finds the bucket for a finite nonnegative sample, including exact edges.
    /// </summary>
    public static int BucketIndex(double latencyMs)
    {
        if (!double.IsFinite(latencyMs) || latencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyMs));
        }
        if (latencyMs <= MinMs)
        {
            return 0;
        }
        if (latencyMs >= MaxMs)
        {
            return BucketCount - 1;
        }

        int idx = Math.Clamp((int)(Math.Log10(latencyMs / MinMs) / Step), 0, BucketCount - 2);
        // Correct logarithm rounding at an exact bucket edge.
        while (idx > 0 && latencyMs < BucketLowerMs(idx))
        {
            idx--;
        }
        while (idx < BucketCount - 2 && latencyMs >= BucketUpperMs(idx))
        {
            idx++;
        }
        return idx;
    }

    public const double MinMs = 0.001;
    public const double MaxMs = 10_000.0;
    public const double Step = 0.1;
    public const int BucketCount = 71;

    private readonly Lock m_gate = new();
    private readonly long[] m_buckets = new long[BucketCount];
    private long m_count;
    private double m_meanMs;
    private double m_maxMs;
}
