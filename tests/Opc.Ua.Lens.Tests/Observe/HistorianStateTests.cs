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
