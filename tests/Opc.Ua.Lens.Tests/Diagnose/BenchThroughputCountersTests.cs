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

using NUnit.Framework;
using UaLens.Plugins.SubscriptionBench;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchThroughputCountersTests
{
    [Test]
    public void RotateReportsThePreviousSecondsThroughput()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(100, 0);

        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(sample.Last1s, Is.EqualTo(100));
        Assert.That(sample.TotalValues, Is.EqualTo(100));
        Assert.That(sample.TotalErrors, Is.Zero);
    }

    [Test]
    public void CumulativeTotalsAccumulateAcrossSeconds()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(10, 1);
        counters.Rotate();
        counters.Record(20, 2);
        counters.Rotate();

        Assert.That(counters.TotalValues, Is.EqualTo(30));
        Assert.That(counters.TotalErrors, Is.EqualTo(3));
    }

    [Test]
    public void AveragesDivideTheWindowedSumByTheWindowLength()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(60, 0);

        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(sample.Last1s, Is.EqualTo(60));
        Assert.That(sample.Avg10s, Is.EqualTo(6.0).Within(1e-9));
        Assert.That(sample.Avg60s, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void ResetClearsTotalsAndBuckets()
    {
        var counters = new BenchThroughputCounters();
        counters.Record(50, 5);

        counters.Reset();
        BenchThroughputCounters.Sample sample = counters.Rotate();

        Assert.That(counters.TotalValues, Is.Zero);
        Assert.That(counters.TotalErrors, Is.Zero);
        Assert.That(sample.Last1s, Is.Zero);
    }
}
