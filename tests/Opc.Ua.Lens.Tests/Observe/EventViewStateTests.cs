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

using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.EventView;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class EventViewStateTests
{
    [Test]
    public void RoundTripPreservesFilterSourcesAndPauseWithoutLiveHandles()
    {
        IServiceMessageContext context = ServiceMessageContext.Create(null);
        var filter = new EventFilterConfig(
            300,
            new List<string> { "EventId", "Severity", "Message" },
            new NodeId(1234u),
            null);
        var snapshot = new EventViewStateSnapshot(
            "Boiler events",
            filter,
            DisplayPaused: true,
            [
                new EventSourceSelection(new NodeId("Boiler", 2), "Boiler"),
                new EventSourceSelection(ObjectIds.Server, "Server")
            ]);

        JsonElement element = EventViewStateCodec.Capture(snapshot, context);
        EventViewStateSnapshot restored = EventViewStateCodec.Restore(element, context);

        Assert.That(restored.Title, Is.EqualTo("Boiler events"));
        Assert.That(restored.DisplayPaused, Is.True);
        Assert.That(restored.Filter.SeverityThreshold, Is.EqualTo(300));
        Assert.That(restored.Filter.Fields, Is.EqualTo(s_fields));
        Assert.That(restored.Filter.EventTypeNodeId, Is.EqualTo(new NodeId(1234u)));
        Assert.That(restored.Sources.Count, Is.EqualTo(2));
        Assert.That(restored.Sources[0].NodeId, Is.EqualTo(new NodeId("Boiler", 2)));
        Assert.That(restored.Sources[0].Name, Is.EqualTo("Boiler"));
        Assert.That(restored.Sources[1].NodeId, Is.EqualTo(ObjectIds.Server));

        string json = element.GetRawText();
        Assert.That(json, Does.Not.Contain("MonitoredItem")
            .And.Not.Contain("ClientHandle")
            .And.Not.Contain("password"));
    }

    [Test]
    public void RoundTripPreservesAnAdvancedWhereClause()
    {
        IServiceMessageContext context = ServiceMessageContext.Create(null);
        var whereClause = new ContentFilter();
        whereClause.Push(FilterOperator.GreaterThan, new Variant((ushort)500), new Variant((ushort)100));
        Assume.That(whereClause.Elements.Count, Is.GreaterThan(0));
        var filter = new EventFilterConfig(0, new List<string> { "Severity" }, null, whereClause);
        var snapshot = new EventViewStateSnapshot("With where", filter, DisplayPaused: false, []);

        JsonElement element = EventViewStateCodec.Capture(snapshot, context);
        EventViewStateSnapshot restored = EventViewStateCodec.Restore(element, context);

        Assert.That(restored.Filter.WhereClause, Is.Not.Null);
        Assert.That(restored.Filter.WhereClause!.Elements.Count, Is.EqualTo(whereClause.Elements.Count));
    }

    [Test]
    public void RestoreRejectsAnUnknownVersion()
    {
        IServiceMessageContext context = ServiceMessageContext.Create(null);
        using JsonDocument document = JsonDocument.Parse(
            "{\"version\":99,\"title\":\"keep\",\"fields\":[],\"sources\":[]}");
        JsonElement state = document.RootElement.Clone();

        Assert.That(() => EventViewStateCodec.Restore(state, context), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void RestoreRejectsASourceMissingItsNodeId()
    {
        IServiceMessageContext context = ServiceMessageContext.Create(null);
        using JsonDocument document = JsonDocument.Parse(
            "{\"version\":1,\"title\":\"keep\",\"fields\":[\"EventId\"]," +
            "\"sources\":[{\"nodeId\":\"\",\"name\":\"broken\"}]}");
        JsonElement state = document.RootElement.Clone();

        Assert.That(() => EventViewStateCodec.Restore(state, context), Throws.InstanceOf<JsonException>());
    }

    private static readonly string[] s_fields = ["EventId", "Severity", "Message"];
}
