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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;
using UaLens.ViewModels;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class HistorianPluginTests
{
    [Test]
    public async Task OpensOfflineAndDisposesCleanlyWithoutASession()
    {
        await using ObserveTestHost host = new();
        var plugin = new HistorianPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            Assert.That(plugin.Kind, Is.EqualTo(PluginKind.Historian));
            Assert.That(plugin.HasTarget, Is.False);
            Assert.That(plugin.Rows, Is.Empty);

            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(plugin.IsOffline, Is.True);
            Assert.That(plugin.IsReading, Is.False);
        }
    }

    [Test]
    public async Task RestoreAppliesConfigurationWithoutReadingHistory()
    {
        await using ObserveTestHost host = new();
        var plugin = new HistorianPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            DateTime start = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            var snapshot = new HistorianStateSnapshot(
                "Boiler history",
                new NodeId("Temperature", 2),
                "Temperature",
                HistorianReadMode.Processed,
                ReturnBounds: false,
                ReadModified: false,
                NumValuesPerNode: 500,
                new NodeId(Objects.AggregateFunction_Minimum),
                ProcessingIntervalMs: 2000,
                [start.AddMinutes(1)],
                start,
                start.AddHours(1),
                HistorianUpdateOp.DeleteRaw,
                start,
                "42",
                start,
                start.AddHours(1),
                []);
            JsonElement element = HistorianStateCodec.Capture(snapshot);

            await plugin.RestoreStateAsync(element).ConfigureAwait(false);

            Assert.That(plugin.Title, Is.EqualTo("Boiler history"));
            Assert.That(plugin.TargetNodeId, Is.EqualTo(new NodeId("Temperature", 2)));
            Assert.That(plugin.ReadMode, Is.EqualTo(HistorianReadMode.Processed));
            Assert.That(plugin.SelectedAggregate.NodeId, Is.EqualTo(new NodeId(Objects.AggregateFunction_Minimum)));
            Assert.That(plugin.SelectedUpdateOp, Is.EqualTo(HistorianUpdateOp.DeleteRaw));
            // Restore must never execute a read.
            Assert.That(plugin.Rows, Is.Empty);
            Assert.That(plugin.IsReading, Is.False);
        }
    }

    [Test]
    public async Task RestoreRejectsInvalidStateWithoutMutatingConfiguration()
    {
        await using ObserveTestHost host = new();
        var plugin = new HistorianPlugin(host.Host);
        await using (plugin.ConfigureAwait(false))
        {
            string originalTitle = plugin.Title;
            using JsonDocument document = JsonDocument.Parse(
                "{\"version\":1,\"readMode\":99,\"updateOp\":0,\"processingIntervalMs\":1000," +
                "\"atTimes\":[],\"updateAtTimes\":[]}");
            JsonElement state = document.RootElement.Clone();

            await Assert.ThatAsync(
                async () => await plugin.RestoreStateAsync(state).ConfigureAwait(false),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);

            Assert.That(plugin.Title, Is.EqualTo(originalTitle));
            Assert.That(plugin.HasTarget, Is.False);
        }
    }
}
