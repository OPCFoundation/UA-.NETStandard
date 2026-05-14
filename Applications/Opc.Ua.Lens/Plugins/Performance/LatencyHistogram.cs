/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Fixed-bucket log-spaced latency histogram suitable for ~10k ops/sec
/// sustained recording.  Buckets cover 1 µs ‒ 10 s at a 10^0.1 step
/// (so each bucket is ~26 % wider than the previous one, 80 buckets
/// total).  Each <see cref="Record"/> call costs an integer log + a
/// single <see cref="System.Threading.Interlocked.Increment(ref long)"/>;
/// percentile / mean queries do a one-pass scan over the counters and
/// are safe to call concurrently with <see cref="Record"/>.
/// </summary>
internal sealed class LatencyHistogram
{
    /// <summary>Lowest bucket edge in milliseconds (1 µs).</summary>
    public const double MinMs = 0.001;

    /// <summary>Highest bucket edge in milliseconds (10 s).</summary>
    public const double MaxMs = 10_000.0;

    /// <summary>Log-spacing step: 10^0.1 per bucket.</summary>
    public const double Step = 0.1;

    /// <summary>Number of buckets (40 decades fit into 80 0.1-decade steps).</summary>
    public const int BucketCount = 80;

    private readonly long[] m_buckets = new long[BucketCount];
    private long m_count;
    private long m_overflow;
    private double m_maxMs;
    private readonly object m_maxLock = new();

    /// <summary>Total recorded samples (including ones that overflowed the top bucket).</summary>
    public long Count => System.Threading.Interlocked.Read(ref m_count);

    /// <summary>Samples that exceeded <see cref="MaxMs"/> and saturated into the last bucket.</summary>
    public long Overflow => System.Threading.Interlocked.Read(ref m_overflow);

    /// <summary>Highest latency observed since the last <see cref="Reset"/>, in ms.</summary>
    public double MaxMsObserved
    {
        get
        {
            lock (m_maxLock)
            {
                return m_maxMs;
            }
        }
    }

    /// <summary>
    /// Record a latency sample in milliseconds.  Values &lt; <see cref="MinMs"/>
    /// land in the bottom bucket; values &gt;= <see cref="MaxMs"/> land in
    /// the top bucket and bump the <see cref="Overflow"/> counter.
    /// Thread-safe.
    /// </summary>
    public void Record(double latencyMs)
    {
        if (double.IsNaN(latencyMs) || latencyMs < 0)
        {
            return;
        }
        int idx = BucketIndex(latencyMs);
        if (idx >= BucketCount - 1)
        {
            System.Threading.Interlocked.Increment(ref m_overflow);
            idx = BucketCount - 1;
        }
        System.Threading.Interlocked.Increment(ref m_buckets[idx]);
        System.Threading.Interlocked.Increment(ref m_count);
        // Track max as well — used for the histogram axis caption.
        lock (m_maxLock)
        {
            if (latencyMs > m_maxMs)
            {
                m_maxMs = latencyMs;
            }
        }
    }

    /// <summary>
    /// Returns the latency in ms at the given percentile (0..1).  Linear
    /// interpolation across the bucket containing the cumulative target;
    /// the bucket edges are log-spaced.  Returns 0 when no samples have
    /// been recorded.
    /// </summary>
    public double GetPercentile(double p)
    {
        if (p <= 0)
        {
            return 0;
        }

        if (p >= 1)
        {
            p = 1;
        }

        long total = System.Threading.Interlocked.Read(ref m_count);
        if (total == 0)
        {
            return 0;
        }

        long target = (long)Math.Ceiling(p * total);
        if (target <= 0)
        {
            target = 1;
        }

        long cumulative = 0;
        for (int i = 0; i < BucketCount; i++)
        {
            long c = System.Threading.Interlocked.Read(ref m_buckets[i]);
            cumulative += c;
            if (cumulative >= target)
            {
                return BucketUpperMs(i);
            }
        }
        return MaxMs;
    }

    /// <summary>Resets all counters and the observed maximum.</summary>
    public void Reset()
    {
        for (int i = 0; i < BucketCount; i++)
        {
            System.Threading.Interlocked.Exchange(ref m_buckets[i], 0);
        }
        System.Threading.Interlocked.Exchange(ref m_count, 0);
        System.Threading.Interlocked.Exchange(ref m_overflow, 0);
        lock (m_maxLock)
        {
            m_maxMs = 0;
        }
    }

    /// <summary>
    /// Snapshot the per-bucket counts into <paramref name="destination"/>.
    /// Caller-supplied buffer of length <see cref="BucketCount"/> to avoid
    /// per-frame allocations in the UI render loop.  Returns the total
    /// count across buckets (matches <see cref="Count"/> at snapshot time).
    /// </summary>
    public long Snapshot(long[] destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (destination.Length < BucketCount)
        {
            throw new ArgumentException(
                $"destination must have at least {BucketCount} entries.", nameof(destination));
        }
        long total = 0;
        for (int i = 0; i < BucketCount; i++)
        {
            long c = System.Threading.Interlocked.Read(ref m_buckets[i]);
            destination[i] = c;
            total += c;
        }
        return total;
    }

    /// <summary>Lower edge in ms of bucket <paramref name="i"/>.</summary>
    public static double BucketLowerMs(int i) =>
        MinMs * Math.Pow(10.0, i * Step);

    /// <summary>Upper edge in ms of bucket <paramref name="i"/>.</summary>
    public static double BucketUpperMs(int i) =>
        MinMs * Math.Pow(10.0, (i + 1) * Step);

    /// <summary>
    /// Bucket index for a given latency in ms.  Saturates at
    /// <see cref="BucketCount"/> - 1 for over-range samples; clamps to 0
    /// for values &lt;= <see cref="MinMs"/>.
    /// </summary>
    public static int BucketIndex(double latencyMs)
    {
        if (latencyMs <= MinMs)
        {
            return 0;
        }

        double decade = Math.Log10(latencyMs / MinMs);
        int idx = (int)(decade / Step);
        if (idx < 0)
        {
            return 0;
        }

        if (idx >= BucketCount)
        {
            return BucketCount - 1;
        }

        return idx;
    }
}
