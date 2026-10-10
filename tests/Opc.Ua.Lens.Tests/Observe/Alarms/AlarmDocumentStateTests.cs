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

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Alarms;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmDocumentStateTests
{
    [Test]
    public async Task TypedFactoryCreatesFreshOfflineDocuments()
    {
        var host = new ObserveTestHost();
        await using var hostLifetime = host.ConfigureAwait(false);
        var backend = new Mock<IAlarmBackend>(MockBehavior.Strict);
        int created = 0;
        var factory = new AlarmsPluginFactory(_ =>
        {
            created++;
            return backend.Object;
        });
        AlarmsPlugin first = factory.Create(host.Host);
        await using var firstLifetime = first.ConfigureAwait(false);
        AlarmsPlugin second = factory.Create(host.Host);
        await using var secondLifetime = second.ConfigureAwait(false);

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(created, Is.EqualTo(2));
        Assert.That(first.IsObserving, Is.False);
        Assert.That(second.IsObserving, Is.False);
        Assert.That(first.SourceId, Is.EqualTo("i=2253"));
        Assert.That(first.SourceName, Is.EqualTo("Server"));
        backend.VerifyNoOtherCalls();
    }

    [TestCase(NodeClass.Object)]
    [TestCase(NodeClass.View)]
    public async Task ConstructionSeedsSelectedObjectOrViewWithoutObservation(NodeClass nodeClass)
    {
        var host = new ObserveTestHost();
        await using var hostLifetime = host.ConfigureAwait(false);
        PluginHost context = SelectedHost(host, nodeClass, new NodeId(500u));
        await using var contextLifetime = context.ConfigureAwait(false);
        var backend = new Mock<IAlarmBackend>(MockBehavior.Strict);
        var plugin = new AlarmsPlugin(context, backend.Object);
        await using var pluginLifetime = plugin.ConfigureAwait(false);

        Assert.That(plugin.SourceId, Is.EqualTo("i=500"));
        Assert.That(plugin.SourceName, Is.EqualTo("Boiler events"));
        Assert.That(plugin.CaptureState().GetProperty("sourceId").GetString(), Is.EqualTo("i=500"));
        Assert.That(plugin.ActionStatus, Does.Contain("Select Observe"));
        Assert.That(plugin.IsObserving, Is.False);
        Assert.That(plugin.CanOperate, Is.False);
        backend.VerifyNoOtherCalls();
    }

    [TestCase(NodeClass.Variable, "i=500")]
    [TestCase(NodeClass.Method, "i=500")]
    [TestCase(NodeClass.Object, "i=0")]
    [TestCase(NodeClass.Object, "ns=2;s=Events")]
    [TestCase(NodeClass.View, "ns=2;s=Events")]
    public async Task ConstructionKeepsServerForIneligibleOrUnresolvedSelection(NodeClass nodeClass, string sourceId)
    {
        var host = new ObserveTestHost();
        await using var hostLifetime = host.ConfigureAwait(false);
        Assert.That(NodeId.TryParse(sourceId, out NodeId nodeId), Is.True);
        PluginHost context = SelectedHost(host, nodeClass, nodeId);
        await using var contextLifetime = context.ConfigureAwait(false);
        var backend = new Mock<IAlarmBackend>(MockBehavior.Strict);
        var plugin = new AlarmsPlugin(context, backend.Object);
        await using var pluginLifetime = plugin.ConfigureAwait(false);

        Assert.That(plugin.SourceId, Is.EqualTo("i=2253"));
        Assert.That(plugin.SourceName, Is.EqualTo("Server"));
        Assert.That(plugin.CaptureState().GetProperty("sourceId").GetString(), Is.EqualTo("i=2253"));
        Assert.That(plugin.IsObserving, Is.False);
        backend.VerifyNoOtherCalls();
    }

    [Test]
    public async Task OpensAndRestoresOfflineWithoutListenersOrCommands()
    {
        var host = new ObserveTestHost();
        await using var lifetime = host.ConfigureAwait(false);
        var backend = new Mock<IAlarmBackend>(MockBehavior.Strict);
        var plugin = new AlarmsPlugin(host.Host, backend.Object);
        await using (plugin.ConfigureAwait(false))
        {
            var saved = new AlarmsDocumentState(
                1, "Boiler alarms", "nsu=urn:test:boiler;s=Events", "Boiler", 500, false);

            await plugin.RestoreStateAsync(AlarmsStateCodec.Capture(saved)).ConfigureAwait(false);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(plugin.Kind, Is.EqualTo(PluginKind.Alarms));
            Assert.That(plugin.Title, Is.EqualTo("Boiler alarms"));
            Assert.That(plugin.SourceId, Is.EqualTo("nsu=urn:test:boiler;s=Events"));
            Assert.That(plugin.SourceName, Is.EqualTo("Boiler"));
            Assert.That(plugin.PublishingInterval, Is.EqualTo(500));
            Assert.That(plugin.RetainedOnly, Is.False);
            Assert.That(plugin.IsObserving, Is.False);
            Assert.That(plugin.IsOffline, Is.True);
            Assert.That(plugin.Conditions, Is.Empty);
            Assert.That(plugin.Comment, Is.Empty);
            Assert.That(plugin.CanOperate, Is.False);
            Assert.That(AlarmsStateCodec.Restore(plugin.CaptureState()), Is.EqualTo(saved));
            backend.VerifyNoOtherCalls();
        }
    }

    [Test]
    public async Task PersistenceExcludesCommandTextEventIdsAndLiveState()
    {
        var host = new ObserveTestHost();
        await using var lifetime = host.ConfigureAwait(false);
        var plugin = new AlarmsPlugin(host.Host)
        {
            Comment = "An unsent operator note",
            ShelvingMilliseconds = 12345,
            ResponseIndex = 2
        };
        await using (plugin.ConfigureAwait(false))
        {
            JsonElement state = plugin.CaptureState();
            string json = state.GetRawText();

            Assert.That(json, Does.Not.Contain("operator note"));
            Assert.That(json, Does.Not.Contain("eventId"));
            Assert.That(json, Does.Not.Contain("conditionId"));
            Assert.That(json, Does.Not.Contain("subscription"));
            Assert.That(json, Does.Not.Contain("observing"));
            Assert.That(json, Does.Not.Contain("shelving"));
            Assert.That(json, Does.Not.Contain("responseIndex"));
            Assert.That(state.GetProperty("version").GetInt32(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task InvalidRestoreDoesNotMutateExistingConfiguration()
    {
        var host = new ObserveTestHost();
        await using var lifetime = host.ConfigureAwait(false);
        var plugin = new AlarmsPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            JsonElement before = plugin.CaptureState();
            using JsonDocument document = JsonDocument.Parse(
                """{"version":99,"title":"Wrong","sourceId":"i=2253","sourceName":"Bad","publishingInterval":500}""");

            await Assert.ThatAsync(() => plugin.RestoreStateAsync(document.RootElement),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);

            Assert.That(plugin.CaptureState().GetRawText(), Is.EqualTo(before.GetRawText()));
            Assert.That(plugin.IsObserving, Is.False);
        }
    }

    [TestCase("ns=2;s=Events")]
    [TestCase("svr=1;i=2253")]
    [TestCase("not a node id")]
    [TestCase("i=0")]
    public void RejectsNonPortableRemoteOrInvalidSources(string id)
    {
        var state = new AlarmsDocumentState(1, "Alarms", id, "Source", 250, true);
        Assert.That(() => AlarmsStateCodec.Capture(state), Throws.InstanceOf<JsonException>());
    }

    [TestCase(0)]
    [TestCase(49)]
    [TestCase(60001)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void RejectsUnboundedPublishingIntervals(double interval)
    {
        var state = new AlarmsDocumentState(1, "Alarms", "i=2253", "Server", interval, true);
        Assert.That(() => AlarmsStateCodec.Capture(state), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public async Task SelectingServerOfflineDoesNotCreateObservation()
    {
        var host = new ObserveTestHost();
        await using var lifetime = host.ConfigureAwait(false);
        var backend = new Mock<IAlarmBackend>(MockBehavior.Strict);
        var plugin = new AlarmsPlugin(host.Host, backend.Object);
        await using (plugin.ConfigureAwait(false))
        {
            await plugin.SeedSourceAsync(ObjectIds.Server, "Selected server").ConfigureAwait(false);

            Assert.That(plugin.SourceName, Is.EqualTo("Selected server"));
            Assert.That(plugin.IsObserving, Is.False);
            Assert.That(plugin.ActionStatus, Does.Contain("Select Observe"));
            backend.VerifyNoOtherCalls();
        }
    }

    private static PluginHost SelectedHost(ObserveTestHost host, NodeClass nodeClass, NodeId nodeId)
    {
        var workspace = new Mock<IPluginWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(value => value.SelectedNode).Returns(
            new NodeViewModel(host.Host.Browser, NodeId.Null, nodeId, "Boiler events", nodeClass));
        return new PluginHost(workspace.Object, host.Connection, host.Host.Browser, host.Telemetry);
    }
}
