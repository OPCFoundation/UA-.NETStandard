// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.EventView;
using UaLens.ViewModels;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class EventViewPluginTests
{
    [Test]
    public async Task OpensOfflineWithoutCreatingASubscriptionOrThrowing()
    {
        await using ObserveTestHost host = new();
        var plugin = new EventViewPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            Assert.That(plugin.Kind, Is.EqualTo(PluginKind.EventView));
            Assert.That(plugin.IsOffline, Is.True);
            Assert.That(plugin.EventSources, Is.Empty);
            Assert.That(plugin.Title, Does.StartWith("Event View"));

            // A disconnected lifecycle delivery is an awaited no-op that neither
            // creates a subscription nor throws.
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(plugin.EventSources, Is.Empty);
            Assert.That(plugin.IsOffline, Is.True);
        }
    }

    [Test]
    public async Task SeedingWhileDisconnectedDoesNotCreateAStrayLiveSource()
    {
        await using ObserveTestHost host = new();
        var plugin = new EventViewPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.SeedSourceAsync(new NodeId(2258u), "CurrentTime").ConfigureAwait(false);
            Assert.That(plugin.EventSources, Is.Empty);
        }
    }

    [Test]
    public async Task RestoreAppliesConfigurationWithoutLiveHandlesAndReCaptures()
    {
        await using ObserveTestHost host = new();
        var plugin = new EventViewPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            var filter = new EventFilterConfig(
                400,
                new List<string> { "EventId", "Message", "Severity" },
                ObjectTypeIds.BaseEventType,
                null);
            var snapshot = new EventViewStateSnapshot(
                "Configured offline",
                filter,
                DisplayPaused: true,
                [new EventSourceSelection(new NodeId("Boiler", 2), "Boiler")]);
            JsonElement element = EventViewStateCodec.Capture(snapshot, host.MessageContext);

            await plugin.RestoreStateAsync(element).ConfigureAwait(false);

            Assert.That(plugin.Title, Is.EqualTo("Configured offline"));
            Assert.That(plugin.IsPaused, Is.True);
            Assert.That(plugin.Filter.SeverityThreshold, Is.EqualTo(400));
            Assert.That(plugin.EventSources, Has.Count.EqualTo(1));
            Assert.That(plugin.EventSources[0].NodeId, Is.EqualTo(new NodeId("Boiler", 2)));
            Assert.That(plugin.EventSources[0].MonitoredItem, Is.Null);

            EventViewStateSnapshot recaptured = EventViewStateCodec.Restore(plugin.CaptureState(), host.MessageContext);
            Assert.That(recaptured.Title, Is.EqualTo("Configured offline"));
            Assert.That(recaptured.DisplayPaused, Is.True);
            Assert.That(recaptured.Filter.Fields, Is.EqualTo(s_fields));
            Assert.That(recaptured.Sources.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RestoreRejectsInvalidStateWithoutMutatingConfiguration()
    {
        await using ObserveTestHost host = new();
        var plugin = new EventViewPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            string originalTitle = plugin.Title;
            using JsonDocument document = JsonDocument.Parse("{\"version\":7,\"title\":\"nope\",\"fields\":[],\"sources\":[]}");
            JsonElement state = document.RootElement.Clone();

            await Assert.ThatAsync(
                () => plugin.RestoreStateAsync(state),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);

            Assert.That(plugin.Title, Is.EqualTo(originalTitle));
            Assert.That(plugin.EventSources, Is.Empty);
        }
    }

    private static readonly string[] s_fields = ["EventId", "Message", "Severity"];
}
