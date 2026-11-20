// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using UaLens.Telemetry;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class LogRingBufferTests
{
    [Test]
    public void WraparoundReportsOnlyIntentionalOverwriteAndCommittedCursor()
    {
        var buffer = new LogRingBuffer(16);
        for (int i = 0; i < 40; i++)
        {
            buffer.Add(Entry(i));
        }
        LogSnapshot snapshot = buffer.ReadSince(2);
        Assert.That(snapshot.Cursor, Is.EqualTo(40));
        Assert.That(snapshot.Overwritten, Is.EqualTo(22));
        Assert.That(snapshot.Entries.ToList().Select(entry => entry.Message),
            Is.EqualTo(Enumerable.Range(24, 16).Select(value => value.ToString(CultureInfo.InvariantCulture))));
        Assert.That(buffer.ReadSince(snapshot.Cursor).Entries.Count, Is.Zero);
        buffer.Add(Entry(40));
        LogSnapshot next = buffer.ReadSince(snapshot.Cursor);
        Assert.That(next.Entries[0].Message, Is.EqualTo("40"));
        Assert.That(next.Cursor, Is.EqualTo(41));
        Assert.That(next.Overwritten, Is.Zero);
    }

    [Test]
    public async Task ConcurrentProducersNeverPublishPartialEntriesOrAdvancePastUnseenEntries()
    {
        var buffer = new LogRingBuffer(2048);
        Task producers = Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                buffer.Add(Entry(producer * 200 + i));
            }
        })));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long cursor = 0;
        do
        {
            Consume();
            await Task.Yield();
        }
        while (!producers.IsCompleted);
        await producers.ConfigureAwait(false);
        Consume();
        Assert.That(cursor, Is.EqualTo(1600));
        Assert.That(seen, Has.Count.EqualTo(1600));
        Assert.That(buffer.TotalWritten, Is.EqualTo(cursor));

        void Consume()
        {
            LogSnapshot snapshot = buffer.ReadSince(cursor);
            Assert.That(snapshot.Overwritten, Is.Zero);
            Assert.That(snapshot.Cursor - cursor, Is.EqualTo(snapshot.Entries.Count));
            foreach (LogEntry entry in snapshot.Entries)
            {
                Assert.That(entry.Category, Is.EqualTo("test"));
                Assert.That(entry.TimestampUtc, Is.EqualTo(DateTime.UnixEpoch));
                Assert.That(seen.Add(entry.Message), Is.True, "A retained entry was delivered twice.");
            }
            cursor = snapshot.Cursor;
        }
    }

    private static LogEntry Entry(int id) =>
        new(DateTime.UnixEpoch, LogLevel.Information, "test", id.ToString(CultureInfo.InvariantCulture));
}
