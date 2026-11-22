// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class HistorianStateTests
{
    [Test]
    public void RoundTripPreservesTargetModeRangeAndUpdateFields()
    {
        DateTime start = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime end = new(2026, 1, 2, 4, 4, 5, DateTimeKind.Utc);
        var snapshot = new HistorianStateSnapshot(
            "Boiler history",
            new NodeId("Temperature", 2),
            "Temperature",
            HistorianReadMode.Processed,
            ReturnBounds: true,
            ReadModified: false,
            NumValuesPerNode: 250,
            new NodeId(Objects.AggregateFunction_Minimum),
            ProcessingIntervalMs: 2000,
            [start.AddMinutes(1), start.AddMinutes(2)],
            start,
            end,
            HistorianUpdateOp.DeleteRaw,
            start.AddMinutes(3),
            "42.5",
            start,
            end,
            [start.AddMinutes(4)]);

        JsonElement element = HistorianStateCodec.Capture(snapshot);
        HistorianStateSnapshot restored = HistorianStateCodec.Restore(element);

        Assert.That(restored.Title, Is.EqualTo("Boiler history"));
        Assert.That(restored.TargetNodeId, Is.EqualTo(new NodeId("Temperature", 2)));
        Assert.That(restored.ReadMode, Is.EqualTo(HistorianReadMode.Processed));
        Assert.That(restored.ReturnBounds, Is.True);
        Assert.That(restored.NumValuesPerNode, Is.EqualTo(250));
        Assert.That(restored.AggregateNodeId, Is.EqualTo(new NodeId(Objects.AggregateFunction_Minimum)));
        Assert.That(restored.ProcessingIntervalMs, Is.EqualTo(2000));
        Assert.That(restored.AtTimes.Count, Is.EqualTo(2));
        Assert.That(restored.CustomStart, Is.EqualTo(start));
        Assert.That(restored.CustomEnd, Is.EqualTo(end));
        Assert.That(restored.UpdateOp, Is.EqualTo(HistorianUpdateOp.DeleteRaw));
        Assert.That(restored.UpdateValueText, Is.EqualTo("42.5"));
        Assert.That(restored.UpdateAtTimes.Count, Is.EqualTo(1));
    }

    [Test]
    public void RestoreRejectsAnUnknownReadMode()
    {
        using JsonDocument document = JsonDocument.Parse(
            "{\"version\":1,\"readMode\":99,\"updateOp\":0,\"processingIntervalMs\":1000," +
            "\"atTimes\":[],\"updateAtTimes\":[]}");
        JsonElement state = document.RootElement.Clone();

        Assert.That(() => HistorianStateCodec.Restore(state), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void RestoreRejectsANonPositiveProcessingInterval()
    {
        using JsonDocument document = JsonDocument.Parse(
            "{\"version\":1,\"readMode\":0,\"updateOp\":0,\"processingIntervalMs\":0," +
            "\"atTimes\":[],\"updateAtTimes\":[]}");
        JsonElement state = document.RootElement.Clone();

        Assert.That(() => HistorianStateCodec.Restore(state), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void RestoreRejectsAnUnknownVersion()
    {
        using JsonDocument document = JsonDocument.Parse(
            "{\"version\":42,\"readMode\":0,\"updateOp\":0,\"processingIntervalMs\":1000," +
            "\"atTimes\":[],\"updateAtTimes\":[]}");
        JsonElement state = document.RootElement.Clone();

        Assert.That(() => HistorianStateCodec.Restore(state), Throws.InstanceOf<JsonException>());
    }
}
