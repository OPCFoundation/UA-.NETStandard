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
using NUnit.Framework;
using UaLens.Plugins.Performance;

namespace UaLens.Tests.Diagnose;

[TestFixture]
public sealed class BenchmarkRunCsvTests
{
    [Test]
    public void RoundTripsThroughCsv()
    {
        var run = new BenchmarkRun
        {
            TimestampUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            TargetRate = 250.5,
            AchievedRate = 240.25,
            TotalOps = 12345,
            MeanLatencyMs = 1.5,
            P50Ms = 1.2,
            P90Ms = 2.3,
            P99Ms = 4.5,
            ErrorCount = 7,
            Notes = "mode=Write; gen=Random"
        };

        BenchmarkRun? parsed = BenchmarkRun.TryParseCsvRow(run.ToCsvRow());

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.TimestampUtc, Is.EqualTo(run.TimestampUtc));
        Assert.That(parsed.TargetRate, Is.EqualTo(250.5).Within(1e-3));
        Assert.That(parsed.TotalOps, Is.EqualTo(12345));
        Assert.That(parsed.P99Ms, Is.EqualTo(4.5).Within(1e-3));
        Assert.That(parsed.ErrorCount, Is.EqualTo(7));
        Assert.That(parsed.Notes, Is.EqualTo("mode=Write; gen=Random"));
    }

    [Test]
    public void QuotedNotesWithCommasRoundTrip()
    {
        var run = new BenchmarkRun
        {
            TimestampUtc = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc),
            Notes = "a,b,\"c\""
        };

        BenchmarkRun? parsed = BenchmarkRun.TryParseCsvRow(run.ToCsvRow());

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Notes, Is.EqualTo("a,b,\"c\""));
    }

    [Test]
    public void MalformedRowsReturnNull()
    {
        Assert.That(BenchmarkRun.TryParseCsvRow("not,enough,columns"), Is.Null);
        Assert.That(BenchmarkRun.TryParseCsvRow(string.Empty), Is.Null);
    }
}
