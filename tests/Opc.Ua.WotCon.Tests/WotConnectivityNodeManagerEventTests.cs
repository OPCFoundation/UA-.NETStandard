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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// A WoT event affordance has to reach an OPC UA client: it must
    /// materialize as an EventType the asset notifies, and an occurrence the
    /// provider reports must be published on that asset.
    /// </summary>
    [TestFixture]
    public sealed partial class WotConnectivityNodeManagerTests
    {
        [Test]
        public async Task RebuildMaterializesEventAffordanceAsAnEventTypeAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            Assert.That(entry.Events, Has.Count.EqualTo(1));

            (BaseObjectTypeState eventType, WotEventTag tag) = entry.Events.Values.First();

            Assert.That(tag.Name, Is.EqualTo("Overheating"));
            Assert.That(eventType.SuperTypeId, Is.EqualTo(Ua.ObjectTypeIds.BaseEventType),
                "A WoT event must materialize as a BaseEventType subtype.");
            Assert.That(eventType.IsAbstract, Is.False,
                "The type has to be instantiable for the asset to raise it.");
        }

        /// <summary>
        /// Without the GeneratesEvent reference and the notifier bit a client
        /// cannot discover or subscribe to the event, so both are asserted.
        /// </summary>
        [Test]
        public async Task RebuildMakesTheAssetNotifyItsEventsAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            NodeId eventTypeId = entry.Events.Keys.First();

            Assert.That(
                entry.Asset.ReferenceExists(Ua.ReferenceTypeIds.GeneratesEvent, false, eventTypeId),
                Is.True,
                "The asset must declare it generates the materialized event type.");
            Assert.That(
                entry.Asset.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents),
                "The asset must be a notifier or no client can subscribe.");
        }

        /// <summary>
        /// The event fields come from the WoT <c>data</c> schema, and the tag
        /// order is the contract a provider fills in.
        /// </summary>
        [Test]
        public async Task RebuildDerivesEventFieldsFromTheDataSchemaAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            (_, WotEventTag tag) = entry.Events.Values.First();

            Assert.That(tag.Fields.Select(f => f.Name), Is.EqualTo(s_expectedOverheatingFieldNames));
        }

        [Test]
        public async Task RebuildSubscribesTheProviderToEveryEventAffordanceAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            int notified = ((SimulatedWotAssetProvider)entry.Provider!).RaiseEvent(
                "Overheating", s_overheatingFields);

            Assert.That(notified, Is.EqualTo(1),
                "The registry must subscribe to the affordance when the TD is applied.");
        }

        /// <summary>
        /// The end-to-end path: a provider-reported occurrence is published as
        /// an OPC UA event on the asset carrying the WoT payload.
        /// </summary>
        [Test]
        public async Task ProviderRaisedEventIsReportedOnTheAssetAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            var reported = new List<IFilterTarget>();
            entry.Asset.OnReportEvent += (_, _, e) => reported.Add(e);

            NodeId eventTypeId = entry.Events.Keys.First();
            var raisedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

            ((SimulatedWotAssetProvider)entry.Provider!).RaiseEvent(
                "Overheating",
                s_overheatingFields,
                new LocalizedText("Pump is overheating"),
                severity: 700,
                timestamp: raisedAt);

            Assert.That(reported, Has.Count.EqualTo(1));

            var raised = (BaseEventState)reported[0];
            Assert.That(raised.EventType?.Value, Is.EqualTo(eventTypeId));
            Assert.That(raised.SourceNode?.Value, Is.EqualTo(entry.Asset.NodeId));
            Assert.That(raised.Severity?.Value, Is.EqualTo((ushort)700));
            Assert.That(raised.Message?.Value.Text, Is.EqualTo("Pump is overheating"));
            Assert.That(raised.Time?.Value, Is.EqualTo(new DateTimeUtc(raisedAt)));
        }

        /// <summary>
        /// A provider that supplies no severity must fall back to the value
        /// authored on the affordance, not to an arbitrary constant.
        /// </summary>
        [Test]
        public async Task ReportedEventFallsBackToTheAuthoredSeverityAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(
                harness, severity: 900).ConfigureAwait(false);

            var reported = new List<IFilterTarget>();
            entry.Asset.OnReportEvent += (_, _, e) => reported.Add(e);

            ((SimulatedWotAssetProvider)entry.Provider!).RaiseEvent(
                "Overheating", s_overheatingFields);

            Assert.That(((BaseEventState)reported[0]).Severity?.Value, Is.EqualTo((ushort)900));
        }

        /// <summary>
        /// A TD that omits the severity has to produce a valid OPC 10000-5
        /// severity, and an out-of-range one must be clamped rather than
        /// published verbatim.
        /// </summary>
        [TestCase(null, (ushort)500)]
        [TestCase((ushort)0, (ushort)500)]
        [TestCase((ushort)5000, (ushort)1000)]
        [TestCase((ushort)250, (ushort)250)]
        public async Task AuthoredSeverityIsNormalisedIntoTheValidRangeAsync(
            ushort? authored,
            ushort expected)
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(
                harness, severity: authored).ConfigureAwait(false);

            (_, WotEventTag tag) = entry.Events.Values.First();

            Assert.That(tag.Severity, Is.EqualTo(expected));
        }

        /// <summary>
        /// Re-applying a TD must not accumulate event types from the previous
        /// generation.
        /// </summary>
        [Test]
        public async Task ReapplyingATdWithoutEventsDropsThePreviousEventTypesAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);
            NodeId eventTypeId = entry.Events.Keys.First();

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001"
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(entry.Events, Is.Empty);
            Assert.That(
                entry.Asset.ReferenceExists(Ua.ReferenceTypeIds.GeneratesEvent, false, eventTypeId),
                Is.False,
                "A stale GeneratesEvent reference would advertise a type that no longer exists.");
        }

        private static async Task<AssetEntry> CreateAssetWithOverheatingEventAsync(
            ManagerHarness harness,
            ushort? severity = null)
        {
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001",
                    Events = new Dictionary<string, WotEvent>
                    {
                        ["Overheating"] = new WotEvent
                        {
                            Title = "Overheating",
                            Severity = severity,
                            Data = new WotActionSchema
                            {
                                Type = "object",
                                Properties = new Dictionary<string, WotActionMember>
                                {
                                    ["Temperature"] = new WotActionMember { Type = "number" }
                                }
                            }
                        }
                    }
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            return entry;
        }

        private static readonly Variant[] s_overheatingFields = [new Variant(93.5)];
        private static readonly string[] s_expectedOverheatingFieldNames = ["Temperature"];
    }
}
