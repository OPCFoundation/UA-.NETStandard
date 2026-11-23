// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
