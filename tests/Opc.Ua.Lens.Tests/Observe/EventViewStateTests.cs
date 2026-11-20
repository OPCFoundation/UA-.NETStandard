/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
