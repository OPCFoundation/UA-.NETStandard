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
using Opc.Ua;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Immutable, bounded distribution of actual completed-operation measurements.
/// Schema 1 uses the fixed edges defined by <see cref="LatencyHistogram"/>.
/// </summary>
internal sealed class BenchmarkDistribution
{
    public BenchmarkDistribution(ArrayOf<long> bucketCounts, double meanMs, double maximumMs)
    {
        if (bucketCounts.Count != LatencyHistogram.BucketCount)
        {
            throw new ArgumentException("The latency bucket count does not match schema 1.", nameof(bucketCounts));
        }
        if (!double.IsFinite(meanMs) || meanMs < 0 ||
            !double.IsFinite(maximumMs) || maximumMs < meanMs)
        {
            throw new ArgumentException("The measured mean and maximum must be finite and consistent.");
        }

        long total = 0;
        int lastOccupied = -1;
        for (int i = 0; i < bucketCounts.Count; i++)
        {
            long count = bucketCounts[i];
            if (count < 0 || count > long.MaxValue - total)
            {
                throw new ArgumentException("Latency bucket counts must be nonnegative and bounded.");
            }
            total += count;
            if (count > 0)
            {
                lastOccupied = i;
            }
        }
        if (total == 0 ? meanMs != 0 || maximumMs != 0 :
            LatencyHistogram.BucketIndex(maximumMs) != lastOccupied)
        {
            throw new ArgumentException("The measured maximum does not match the observed buckets.");
        }

        BucketCounts = new ArrayOf<long>(bucketCounts.Span.ToArray());
        SampleCount = total;
        MeanMs = meanMs;
        MaximumMs = maximumMs;
    }

    public ArrayOf<long> BucketCounts { get; }
    public long SampleCount { get; }
    public long OverflowCount => BucketCounts[^1];
    public double MeanMs { get; }
    public double MaximumMs { get; }

    public double GetPercentile(double percentile)
    {
        return GetPercentile(BucketCounts.Span, SampleCount, MaximumMs, percentile);
    }

    internal static double GetPercentile(
        ReadOnlySpan<long> counts,
        long total,
        double maximumMs,
        double percentile)
    {
        if (!double.IsFinite(percentile) || percentile < 0 || percentile > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }
        if (total == 0 || percentile == 0)
        {
            return 0;
        }
        if (percentile == 1)
        {
            return maximumMs;
        }

        double target = Math.Ceiling(percentile * total);
        long cumulative = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            cumulative += counts[i];
            if (cumulative >= target)
            {
                return Math.Min(LatencyHistogram.BucketUpperMs(i), maximumMs);
            }
        }
        return maximumMs;
    }

    public const int SchemaVersion = 1;
}
