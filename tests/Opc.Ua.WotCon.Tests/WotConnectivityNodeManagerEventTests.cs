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

        [Test]
        public async Task RebuildMaterializesConditionEventAffordanceAsConditionTypeSubtypeAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(
                harness,
                conditionType: "ua:AlarmConditionType").ConfigureAwait(false);

            (BaseObjectTypeState eventType, _) = entry.Events.Values.First();

            Assert.That(eventType.SuperTypeId, Is.EqualTo(Ua.ObjectTypeIds.AlarmConditionType),
                "A WoT Condition affordance must materialize below the named ConditionType.");
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
        /// A TD that omits the severity gets the default, and an in-range one
        /// is published verbatim.
        /// </summary>
        [TestCase(null, (ushort)500)]
        [TestCase((ushort)250, (ushort)250)]
        [TestCase((ushort)1, (ushort)1)]
        [TestCase((ushort)1000, (ushort)1000)]
        public async Task AuthoredSeverityInRangeIsPublishedAsync(
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
        /// The WoT Binding "Event severity range" rule makes an out-of-range
        /// severity invalid and forbids silently clamping it, so the affordance
        /// is skipped rather than published with a rewritten value.
        /// </summary>
        [TestCase((ushort)0)]
        [TestCase((ushort)1001)]
        [TestCase((ushort)5000)]
        public async Task AnOutOfRangeSeverityIsRejectedRatherThanClampedAsync(ushort authored)
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(
                harness, severity: authored).ConfigureAwait(false);

            Assert.That(entry.Events, Is.Empty,
                "An out-of-range severity must not be silently rewritten into a valid one.");
        }

        /// <summary>
        /// Rejecting one affordance must not leave a half-built event type
        /// advertised on the asset.
        /// </summary>
        [Test]
        public async Task ARejectedEventLeavesNoGeneratesEventReferenceAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(
                harness, severity: 5000).ConfigureAwait(false);

            NodeId eventTypeId = harness.Manager.AllocateChildNodeId(
                entry.Name, "events", "Overheating");

            Assert.That(
                entry.Asset.ReferenceExists(Ua.ReferenceTypeIds.GeneratesEvent, false, eventTypeId),
                Is.False,
                "A rejected affordance must not advertise an event type.");
            Assert.That(
                harness.Manager.FindPredefinedNode<NodeState>(eventTypeId),
                Is.Null,
                "A rejected affordance must not leave its event type registered.");
        }

        /// <summary>
        /// An affordance the Server cannot materialise is skipped so the rest
        /// of the asset stays usable, but the caller must be told. Reporting a
        /// plain Good would leave an operator believing an alarm they authored
        /// is configured when it silently does not exist.
        /// </summary>
        [Test]
        public async Task ARejectedEventIsReportedAsAnIncompleteResultAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            ServiceResult status = await RebuildWithOverheatingEventAsync(
                harness, severity: 5000).ConfigureAwait(false);

            Assert.That(
                ServiceResult.IsGood(status),
                Is.True,
                "The asset is still usable, so the result stays in the Good class.");
            Assert.That(
                status.StatusCode.Code,
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete),
                "A silently dropped alarm must not be reported as a plain Good.");
        }

        /// <summary>
        /// A Thing Description whose affordances all materialise reports a
        /// plain Good, so an incomplete result stays meaningful.
        /// </summary>
        [Test]
        public async Task AnAcceptedEventIsReportedAsAPlainGoodAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);

            ServiceResult status = await RebuildWithOverheatingEventAsync(
                harness, severity: 900).ConfigureAwait(false);

            Assert.That(status.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
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

        /// <summary>
        /// A provider whose subscription outlives its Thing Description
        /// generation must not be able to report an occurrence against an
        /// event type that has been removed.
        /// </summary>
        [Test]
        public async Task StaleProviderCallbackFromAReplacedTdIsIgnoredAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            // Capture the generation's provider before the TD is replaced,
            // then keep raising on it as a provider that ignored unsubscribe
            // would.
            var stale = (SimulatedWotAssetProvider)entry.Provider!;

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001"
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            var reported = new List<IFilterTarget>();
            entry.Asset.OnReportEvent += (_, _, e) => reported.Add(e);

            int invoked = stale.RaiseEventIgnoringUnsubscribe("Overheating", s_overheatingFields);

            Assert.That(invoked, Is.EqualTo(1),
                "The test must actually reach the retired callback, or it proves nothing.");
            Assert.That(reported, Is.Empty,
                "An occurrence from a replaced TD generation must not be reported.");
        }

        /// <summary>
        /// Replacing a Thing Description must unsubscribe the outgoing
        /// provider, not just drop the reference to it.
        /// </summary>
        [Test]
        public async Task ReplacingATdUnsubscribesThePreviousProviderAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);
            var previous = (SimulatedWotAssetProvider)entry.Provider!;

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001"
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                previous.RaiseEvent("Overheating", s_overheatingFields),
                Is.Zero,
                "The outgoing provider must have no subscribers left.");
        }

        /// <summary>
        /// The event type's field properties are indexed with the type, so
        /// dropping the type has to drop them too or a re-applied Thing
        /// Description leaks a node per field.
        /// </summary>
        [Test]
        public async Task RemovingAnEventTypeAlsoRemovesItsFieldPropertiesAsync()
        {
            using var harness = new ManagerHarness(
                _tempFolder,
                new SimulatedWotAssetProviderFactory());
            await harness.StartAsync().ConfigureAwait(false);
            AssetEntry entry = await CreateAssetWithOverheatingEventAsync(harness).ConfigureAwait(false);

            (BaseObjectTypeState eventType, _) = entry.Events.Values.First();
            var fieldNodes = new List<BaseInstanceState>();
            eventType.GetChildren(harness.Manager.SystemContext, fieldNodes);
            NodeId fieldNodeId = fieldNodes.Single().NodeId;

            Assert.That(harness.Manager.FindPredefinedNode<NodeState>(fieldNodeId), Is.Not.Null,
                "The field property must be registered while the event type exists.");

            await harness.Registry.RebuildAsync(
                entry,
                new ThingDescription
                {
                    Name = "asset-001",
                    Base = "sim://opcua.test/wot/asset-001"
                },
                persistOnSuccess: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(harness.Manager.FindPredefinedNode<NodeState>(fieldNodeId), Is.Null,
                "A leaked field property would outlive the event type that declared it.");
        }

        private static async Task<AssetEntry> CreateAssetWithOverheatingEventAsync(
            ManagerHarness harness,
            ushort? severity = null,
            string? conditionType = null)
        {
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            await RebuildWithOverheatingEventAsync(harness, entry, severity, conditionType)
                .ConfigureAwait(false);
            return entry;
        }

        /// <summary>
        /// Applies the overheating TD and hands back the status, for a test
        /// that asserts on what the caller is told rather than on the nodes.
        /// </summary>
        private static async Task<ServiceResult> RebuildWithOverheatingEventAsync(
            ManagerHarness harness,
            ushort? severity)
        {
            (_, NodeId assetId) = await harness.Registry
                .CreateAssetAsync("asset-001", CancellationToken.None).ConfigureAwait(false);
            AssetEntry entry = harness.Registry.FindByNodeId(assetId)!;
            return await RebuildWithOverheatingEventAsync(harness, entry, severity)
                .ConfigureAwait(false);
        }

        private static ValueTask<ServiceResult> RebuildWithOverheatingEventAsync(
            ManagerHarness harness,
            AssetEntry entry,
            ushort? severity,
            string? conditionType = null)
        {
            return harness.Registry.RebuildAsync(
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
                            ConditionType = conditionType,
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
                CancellationToken.None);
        }

        private static readonly Variant[] s_overheatingFields = [new Variant(93.5)];
        private static readonly string[] s_expectedOverheatingFieldNames = ["Temperature"];
    }
}
