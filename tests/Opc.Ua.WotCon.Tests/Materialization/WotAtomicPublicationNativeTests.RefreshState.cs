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
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistryRefreshStateMatchesTheCompletedNativeInvocation(bool nativeMethod)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            var client = new WotRegistryClient(m_session,
                ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris),
                NUnitTelemetryContext.Create());
            const string requestId = "native-refresh-state";
            WoTRefreshSummaryDataType summary;
            uint generation;
            if (nativeMethod)
            {
                (summary, _, generation) = await client.Proxy.RefreshAsync(
                    [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 0, requestId)
                    .ConfigureAwait(false);
            }
            else
            {
                WotRefreshResult response = await m_coordinator.RefreshAsync(HandoffRequest(requestId))
                    .ConfigureAwait(false);
                summary = response.Summary;
                generation = response.NewGeneration;
            }
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            NodeId registry = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris);
            var properties = (await BrowseStockAsync(registry, Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .ToDictionary(reference => reference.BrowseName.Name!);
            Assert.That(properties.ContainsKey("RefreshGeneration"), Is.True);
            Assert.That(properties.ContainsKey("LastRefreshTime"), Is.True);
            Assert.That(properties.ContainsKey("LastRefreshSummary"), Is.True);
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                s_refreshStateNames.Select(name =>
                    new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(properties[name].NodeId, m_session.NamespaceUris),
                        AttributeId = Attributes.Value
                    }).ToArrayOf(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(generation, Is.EqualTo(1u));
            Assert.That(read.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out uint observedGeneration), Is.True);
            Assert.That(observedGeneration, Is.EqualTo(generation));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
            Assert.That(time, Is.EqualTo(summary.EndTime));
            Assert.That(read.Results[2].WrappedValue.TryGetStructure<WoTRefreshSummaryDataType>(
                m_session.MessageContext, out WoTRefreshSummaryDataType? observed), Is.True);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.RequestId, Is.EqualTo(requestId));
            Assert.That(observed.Generation, Is.EqualTo(generation));
            Assert.That(observed.Total, Is.EqualTo(summary.Total));
            Assert.That(observed.Succeeded, Is.EqualTo(summary.Succeeded));
            Assert.That(observed.Outcome, Is.EqualTo(summary.Outcome));
        }

        [Test]
        public async Task RegistryRefreshStateStartsWaitingUntilAnActualCompletion()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            WotRegistryClient client = await CreateRefreshStateClientAsync().ConfigureAwait(false);
            ReadResponse initial = await ReadRefreshStateAsync().ConfigureAwait(false);
            Assert.That(initial.Results[0].WrappedValue.TryGetValue(out uint generation), Is.True);
            Assert.That(generation, Is.Zero);
            Assert.That(initial.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(initial.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(m_coordinator.LastRefreshSummary, Is.Null);

            var empty = await client.Proxy.RefreshAsync(
                [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 0, "empty-completion")
                .ConfigureAwait(false);

            Assert.That(empty.newGeneration, Is.Zero);
            Assert.That(empty.summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Unchanged));
            await AssertRefreshStateAsync(empty.summary).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistryRefreshStateTracksNoOpAndFailureWithoutAdvancingGeneration(bool fail)
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true, converter: m_converter)
                .ConfigureAwait(false);
            WotResource source = await AddAsync("state-source").ConfigureAwait(false);
            WotRegistryClient client = await CreateRefreshStateClientAsync().ConfigureAwait(false);
            var initial = await client.Proxy.RefreshAsync(
                [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 0, "initial-state")
                .ConfigureAwait(false);
            if (fail)
            {
                await UpdateHandoffResourceAsync(source).ConfigureAwait(false);
                m_converter.MarkInvalid(source.ResourceId);
            }
            m_events.Clear();

            var completed = await client.Proxy.RefreshAsync(
                [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 1, "checked-state")
                .ConfigureAwait(false);

            Assert.That(initial.newGeneration, Is.EqualTo(1u));
            Assert.That(completed.newGeneration, Is.EqualTo(1u));
            Assert.That(completed.summary.Outcome, Is.EqualTo(fail ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Unchanged));
            Assert.That(completed.summary.Failed, Is.EqualTo(fail ? 1u : 0u));
            await AssertRefreshStateAsync(completed.summary).ConfigureAwait(false);
            Assert.That(m_events.Single(change => change.Kind == WotMaterializationEventKind.RefreshCompleted)
                .Generation, Is.EqualTo(1u));
            Assert.That((await ReadNodeClassAsync(Root(source)).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task NativeDryRunAndStaleGenerationDoNotReplaceTheLastCompletedState()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotRegistryClient client = await CreateRefreshStateClientAsync().ConfigureAwait(false);
            var initial = await client.Proxy.RefreshAsync(
                [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 0, "retained-state")
                .ConfigureAwait(false);
            m_events.Clear();

            var dry = await client.Proxy.RefreshAsync([], new WoTRefreshOptionsDataType
            {
                Atomicity = WoTAtomicityEnum.PerRegistry, Force = true, DryRun = true
            }, 1, "uncommitted-preview").ConfigureAwait(false);

            Assert.That(dry.newGeneration, Is.EqualTo(1u));
            await AssertRefreshStateAsync(initial.summary).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await client.Proxy.RefreshAsync(
                [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry }, 2, "stale-state")
                .ConfigureAwait(false), Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadInvalidState))
                .ConfigureAwait(false);
            await AssertRefreshStateAsync(initial.summary).ConfigureAwait(false);
            Assert.That(m_events, Is.Empty);
        }

        [Test]
        public async Task CallerEditsCannotChangeTheCachedCompletedSummary()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotRefreshResult result = await m_coordinator.RefreshAsync(HandoffRequest("immutable-state"))
                .ConfigureAwait(false);
            WoTRefreshSummaryDataType expected = CoreUtils.Clone(result.Summary)!;
            result.Summary.RequestId = "caller-result-edit";
            result.Summary.Generation = 999;
            WoTRefreshSummaryDataType detached = m_coordinator.LastRefreshSummary!;
            detached.RequestId = "caller-property-edit";
            detached.Generation = 998;

            await CreateRefreshStateClientAsync().ConfigureAwait(false);
            await AssertRefreshStateAsync(expected).ConfigureAwait(false);
            Assert.That(m_coordinator.LastRefreshSummary!.RequestId, Is.EqualTo("immutable-state"));
            Assert.That(m_coordinator.LastRefreshSummary!.Generation, Is.EqualTo(1u));
        }

        [Test]
        public async Task AutomaticRefreshPublishesTheSameRegistryCompletionState()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, autoRefresh: true).ConfigureAwait(false);
            var completion = new TaskCompletionSource<WoTRefreshSummaryDataType>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WotMaterializationEventArgs> completed = (_, change) =>
            {
                if (change.Kind == WotMaterializationEventKind.RefreshCompleted && change.RequestId == "auto")
                {
                    completion.TrySetResult(CoreUtils.Clone(change.Summary)!);
                }
            };
            m_coordinator.Event += completed;
            try
            {
                await UpsertStockSourceAsync(false).ConfigureAwait(false);
                WoTRefreshSummaryDataType summary = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
                await CreateRefreshStateClientAsync().ConfigureAwait(false);
                await AssertRefreshStateAsync(summary).ConfigureAwait(false);
                Assert.That(summary.Generation, Is.EqualTo(1u));
            }
            finally
            {
                m_coordinator.Event -= completed;
            }
        }

        [Test]
        public async Task CommittedObserverWarningIsRetainedInTheCompletedRegistryState()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            await CreateRefreshStateClientAsync().ConfigureAwait(false);
            EventHandler<WotMaterializationEventArgs> reject = (_, change) =>
            {
                if (change.Kind == WotMaterializationEventKind.RefreshCompleted)
                {
                    throw new InvalidOperationException("The completion observer failed.");
                }
            };
            m_coordinator.Event += reject;
            WotRefreshResult result;
            try
            {
                result = await m_coordinator.RefreshAsync(HandoffRequest("completed-warning")).ConfigureAwait(false);
            }
            finally
            {
                m_coordinator.Event -= reject;
            }

            Assert.That(result.Summary.Outcome, Is.EqualTo(WoTOutcomeEnum.Warning));
            Assert.That(result.NewGeneration, Is.EqualTo(1u));
            await AssertRefreshStateAsync(result.Summary).ConfigureAwait(false);
        }

        [Test]
        public async Task NativeRegistryRefreshPropertiesNotifyTheirCompletedValues()
        {
            using var views = new LifecycleWotViewProjectionHost(m_server.NodeManagerLifecycle);
            await ConfigureStockViewsAsync(views, allowNativeRefresh: true).ConfigureAwait(false);
            await UpsertStockSourceAsync(false).ConfigureAwait(false);
            WotRegistryClient client = await CreateRefreshStateClientAsync().ConfigureAwait(false);
            NodeId registry = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris);
            var properties = (await BrowseStockAsync(registry, Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .ToDictionary(reference => reference.BrowseName.Name!);
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 100, 1000, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse monitored = await m_session.CreateMonitoredItemsAsync(
                    null, subscription.SubscriptionId, TimestampsToReturn.Neither,
                    s_refreshStateNames.Select((name, index) => new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ExpandedNodeId.ToNodeId(properties[name].NodeId, m_session.NamespaceUris),
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = (uint)index + 1, SamplingInterval = 0, QueueSize = 10, DiscardOldest = true
                        }
                    }).ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(monitored.Results.ToList().All(item => item.StatusCode == StatusCodes.Good), Is.True);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                PublishResponse initial = await m_session.PublishAsync(null, [], timeout.Token).ConfigureAwait(false);
                Assert.That(initial.NotificationMessage.NotificationData.IsEmpty, Is.False);

                var result = await client.Proxy.RefreshAsync(
                    [], new WoTRefreshOptionsDataType { Atomicity = WoTAtomicityEnum.PerRegistry },
                    0, "notified-refresh-state").ConfigureAwait(false);

                var values = new Dictionary<uint, DataValue>();
                for (int attempt = 0; attempt < 20 && values.Count != 3; attempt++)
                {
                    PublishResponse response = await m_session.PublishAsync(null, [], timeout.Token)
                        .ConfigureAwait(false);
                    foreach (ExtensionObject extension in response.NotificationMessage.NotificationData)
                    {
                        if (!extension.TryGetValue(out DataChangeNotification? notification) || notification is null)
                        {
                            continue;
                        }
                        foreach (MonitoredItemNotification item in notification.MonitoredItems)
                        {
                            if (item.Value.StatusCode == StatusCodes.Good &&
                                (item.ClientHandle != 1 ||
                                 item.Value.WrappedValue.TryGetValue(out uint generation) && generation == 1))
                            {
                                values[item.ClientHandle] = item.Value;
                            }
                        }
                    }
                }
                Assert.That(values.Keys, Is.EquivalentTo(new uint[] { 1, 2, 3 }));
                Assert.That(values[2].WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo(result.summary.EndTime));
                Assert.That(values[3].WrappedValue.TryGetStructure<WoTRefreshSummaryDataType>(
                    m_session.MessageContext, out WoTRefreshSummaryDataType? summary), Is.True);
                Assert.That(summary!.IsEqual(result.summary), Is.True);
            }
            finally
            {
                await m_session.DeleteSubscriptionsAsync(null, [subscription.SubscriptionId], CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async Task<WotRegistryClient> CreateRefreshStateClientAsync()
        {
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            return new WotRegistryClient(m_session,
                ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris), NUnitTelemetryContext.Create());
        }

        private async Task<ReadResponse> ReadRefreshStateAsync()
        {
            NodeId registry = ExpandedNodeId.ToNodeId(ObjectIds.WoTRegistry, m_session.NamespaceUris);
            var properties = (await BrowseStockAsync(registry, Ua.ReferenceTypeIds.HasProperty).ConfigureAwait(false))
                .ToDictionary(reference => reference.BrowseName.Name!);
            return await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                s_refreshStateNames.Select(name => new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(properties[name].NodeId, m_session.NamespaceUris),
                    AttributeId = Attributes.Value
                }).ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task AssertRefreshStateAsync(WoTRefreshSummaryDataType expected)
        {
            await AwaitStockRegistryProjectionAsync().ConfigureAwait(false);
            ReadResponse read = await ReadRefreshStateAsync().ConfigureAwait(false);
            Assert.That(read.Results.ToList().All(value => value.StatusCode == StatusCodes.Good), Is.True);
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out uint generation), Is.True);
            Assert.That(generation, Is.EqualTo(expected.Generation));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
            Assert.That(time, Is.EqualTo(expected.EndTime));
            Assert.That(read.Results[2].WrappedValue.TryGetStructure<WoTRefreshSummaryDataType>(
                m_session.MessageContext, out WoTRefreshSummaryDataType? summary), Is.True);
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary!.IsEqual(expected), Is.True,
                "The native root must retain the exact completed summary, not only its generation.");
        }

        private static readonly string[] s_refreshStateNames =
            ["RefreshGeneration", "LastRefreshTime", "LastRefreshSummary"];
    }
}
