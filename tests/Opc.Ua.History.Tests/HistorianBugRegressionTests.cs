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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.History.Tests
{
    [TestFixture]
    [Category("Historian")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class HistorianBugRegressionTests : TestFixture
    {
        [OneTimeSetUp]
        public async Task RegisterHistoryNodesAsync()
        {
            await ReferenceServer.NodeManagerLifecycle.AddAsync(
                new HistoryRegressionNodeManagerFactory(m_streamingProvider),
                callerContext: null).ConfigureAwait(false);
            await Session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            int namespaceIndex = Session.NamespaceUris.GetIndex(kNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            m_namespaceIndex = (ushort)namespaceIndex;
        }

        [TestCase("Stepped", false, 0.0)]
        [TestCase("Sloped", false, 5.0)]
        [TestCase("Stepped", true, 0.0)]
        [TestCase("Sloped", true, 6.0)]
        public async Task ProcessedReadHonorsSteppedNodeCapabilityAsync(
            string identifier,
            bool reverse,
            double expected)
        {
            var nodeId = new NodeId(identifier, m_namespaceIndex);
            var client = new HistoryClient(Session);
            DateTime start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (reverse)
            {
                start = start.AddMinutes(1);
            }
            ArrayOf<StatusCode> statuses = await client.InsertAsync(
                nodeId,
                [
                    new DataValue(0.0, StatusCodes.Good, start, start),
                    new DataValue(10.0, StatusCodes.Good, start.AddSeconds(10), start.AddSeconds(10))
                ]).ConfigureAwait(false);
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(statuses.ToArray(), Has.All.Matches<StatusCode>(StatusCode.IsGood));

            var values = new List<DataValue>();
            await foreach (DataValue value in client.ReadProcessedAsync(
                nodeId,
                ObjectIds.AggregateFunction_Interpolative,
                start.AddSeconds(reverse ? 6 : 5),
                start.AddSeconds(reverse ? 5 : 6),
                1_000).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Has.Count.EqualTo(1));
            DataValue result = values[0];
            Assert.Multiple(() =>
            {
                Assert.That(result.WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(result.SourceTimestamp, Is.EqualTo((DateTimeUtc)start.AddSeconds(reverse ? 6 : 5)));
                Assert.That(
                    result.StatusCode,
                    Is.EqualTo(StatusCodes.Good.WithAggregateBits(AggregateBits.Interpolated)));
            });
        }

        [TestCase("UncertainIsGood", false, 100.0)]
        [TestCase("UncertainIsBad", true, 0.0)]
        public async Task ProcessedReadUsesAdvertisedNodeAggregateDefaultsAsync(
            string identifier,
            bool treatUncertainAsBad,
            double expected)
        {
            var nodeId = new NodeId(identifier, m_namespaceIndex);
            var client = new HistoryClient(Session);
            DateTime start = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            ArrayOf<StatusCode> statuses = await client.InsertAsync(
                nodeId,
                [
                    new DataValue(5.0, StatusCodes.Uncertain, start, start),
                    new DataValue(5.0, StatusCodes.Good, start.AddSeconds(10), start.AddSeconds(10))
                ]).ConfigureAwait(false);
            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(statuses.ToArray(), Has.All.Matches<StatusCode>(StatusCode.IsGood));

            HistoricalDataConfigurationInfo advertised = await client
                .GetConfigurationAsync(nodeId).ConfigureAwait(false);
            Assert.That(advertised.HasConfiguration, Is.True);
            Assert.That(advertised.AggregateConfiguration, Is.Not.Null);
            Assert.That(
                advertised.AggregateConfiguration!.TreatUncertainAsBad,
                Is.EqualTo(treatUncertainAsBad));

            var values = new List<DataValue>();
            await foreach (DataValue value in client.ReadProcessedAsync(
                nodeId,
                ObjectIds.AggregateFunction_PercentGood,
                start,
                start.AddSeconds(10),
                10_000).ConfigureAwait(false))
            {
                values.Add(value);
            }

            Assert.That(values, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(values[0].WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(values[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)start));
                Assert.That(
                    values[0].StatusCode,
                    Is.EqualTo(StatusCodes.Good.WithAggregateBits(AggregateBits.Calculated)));
            });

            values.Clear();
            await foreach (DataValue value in client.ReadProcessedAsync(
                nodeId,
                ObjectIds.AggregateFunction_PercentGood,
                start,
                start.AddSeconds(10),
                10_000,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = !treatUncertainAsBad,
                    PercentDataBad = 100,
                    PercentDataGood = 100
                }).ConfigureAwait(false))
            {
                values.Add(value);
            }
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(values[0].WrappedValue.TryGetValue(out double overridden), Is.True);
            Assert.That(overridden, Is.EqualTo(treatUncertainAsBad ? 100.0 : 0.0));
            HistoricalDataConfigurationInfo afterOverride = await client
                .GetConfigurationAsync(nodeId).ConfigureAwait(false);
            Assert.That(afterOverride.AggregateConfiguration, Is.Not.Null);
            Assert.That(
                afterOverride.AggregateConfiguration!.TreatUncertainAsBad,
                Is.EqualTo(treatUncertainAsBad));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EventHistoryIncludesRequestStartAndExcludesRequestEndAcrossPagesAsync(bool reverse)
        {
            var nodeId = new NodeId(reverse ? "ReverseEvents" : "ForwardEvents", m_namespaceIndex);
            var client = new HistoryClient(Session);
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventType, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Time, Attributes.Value);
            DateTime lower = new(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            DateTime upper = lower.AddSeconds(10);
            ArrayOf<StatusCode> statuses = await client.InsertEventsAsync(
                nodeId,
                filter,
                [
                    CreateEvent(1, lower),
                    CreateEvent(2, lower.AddSeconds(5)),
                    CreateEvent(3, lower.AddSeconds(5)),
                    CreateEvent(4, upper)
                ]).ConfigureAwait(false);
            Assert.That(statuses, Has.Count.EqualTo(4));
            Assert.That(statuses.ToArray(), Has.All.Matches<StatusCode>(StatusCode.IsGood));

            var identifiers = new List<ByteString>();
            await foreach (HistoryEventFieldList item in client.ReadEventsAsync(
                nodeId,
                reverse ? upper : lower,
                reverse ? lower : upper,
                filter,
                maxValuesPerNode: 1).ConfigureAwait(false))
            {
                Assert.That(item.EventFields[0].TryGetValue(out ByteString identifier), Is.True);
                identifiers.Add(identifier);
            }
            ByteString[] expected = reverse
                ? [ByteString.From([4]), ByteString.From([3]), ByteString.From([2])]
                : [ByteString.From([1]), ByteString.From([2]), ByteString.From([3])];
            Assert.That(identifiers, Is.EqualTo(expected));
        }

        private static HistoryEventFieldList CreateEvent(byte identifier, DateTime timestamp)
        {
            return new HistoryEventFieldList
            {
                EventFields =
                [
                    new Variant(ByteString.From([identifier])),
                    new Variant(ObjectTypeIds.BaseEventType),
                    new Variant((DateTimeUtc)timestamp)
                ]
            };
        }

        [TestCase(99_999, true)]
        [TestCase(100_000, true)]
        [TestCase(100_001, false)]
        public async Task ProcessedReadEnforcesExactOutputLimitAndStopsRawPagingAsync(
            int requestedValues,
            bool succeeds)
        {
            var client = new HistoryClient(Session);
            var nodeId = new NodeId("OutputLimit", m_namespaceIndex);
            ServiceResultException? error = null;
            int previousReads = m_streamingProvider.ReadCount;
            int returnedValues = 0;
            DateTimeUtc lastTimestamp = default;
            try
            {
                await foreach (DataValue value in client.ReadProcessedAsync(
                    nodeId,
                    ObjectIds.AggregateFunction_Minimum,
                    StreamingPageProvider.Start,
                    StreamingPageProvider.Start.AddMilliseconds(requestedValues),
                    1).ConfigureAwait(false))
                {
                    Assert.That(succeeds, Is.True, "An oversized result must not expose a successful page.");
                    Assert.That(
                        value.SourceTimestamp,
                        Is.EqualTo((DateTimeUtc)StreamingPageProvider.Start.AddMilliseconds(returnedValues)));
                    lastTimestamp = value.SourceTimestamp;
                    returnedValues++;
                }
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }
            if (succeeds)
            {
                Assert.That(error, Is.Null);
                Assert.That(returnedValues, Is.EqualTo(requestedValues));
                Assert.That(
                    lastTimestamp,
                    Is.EqualTo((DateTimeUtc)StreamingPageProvider.Start.AddMilliseconds(requestedValues - 1)));
                Assert.That(m_streamingProvider.ReadCount - previousReads, Is.EqualTo(2));
            }
            else
            {
                Assert.That(error, Is.Not.Null);
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(returnedValues, Is.Zero);
                Assert.That(m_streamingProvider.ReadCount - previousReads, Is.EqualTo(1));
            }
        }

        [TestCase(Objects.AggregateFunction_Minimum, 5.0)]
        [TestCase(Objects.AggregateFunction_Maximum, 10.0)]
        [TestCase(Objects.AggregateFunction_Range, 5.0)]
        [TestCase(Objects.AggregateFunction_MinimumActualTime, 5.0)]
        [TestCase(Objects.AggregateFunction_MaximumActualTime, 10.0)]
        public async Task MinMaxHistoryPreservesGoodValuesAndReportsUncertainExtremaAsync(
            uint aggregateTypeId,
            double expected)
        {
            var client = new HistoryClient(Session);
            var nodeId = new NodeId("Extrema", m_namespaceIndex);
            DateTime start = new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(aggregateTypeId * 20);
            ArrayOf<StatusCode> statuses = await client.InsertAsync(
                nodeId,
                [
                    new DataValue(5.0, StatusCodes.Good, start, start),
                    new DataValue(1.0, StatusCodes.Uncertain, start.AddSeconds(1), start.AddSeconds(1)),
                    new DataValue(10.0, StatusCodes.Good, start.AddSeconds(2), start.AddSeconds(2)),
                    new DataValue(20.0, StatusCodes.Uncertain, start.AddSeconds(3), start.AddSeconds(3)),
                    new DataValue(7.0, StatusCodes.Good, start.AddSeconds(10), start.AddSeconds(10))
                ]).ConfigureAwait(false);
            Assert.That(statuses.ToArray(), Has.All.Matches<StatusCode>(StatusCode.IsGood));
            var values = new List<DataValue>();
            await foreach (DataValue value in client.ReadProcessedAsync(
                nodeId,
                new NodeId(aggregateTypeId),
                start,
                start.AddSeconds(10),
                10_000,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = false,
                    PercentDataBad = 100,
                    PercentDataGood = 100
                }).ConfigureAwait(false))
            {
                values.Add(value);
            }
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(values[0].WrappedValue.TryGetValue(out double actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(
                    values[0].StatusCode,
                    Is.EqualTo(StatusCodes.UncertainDataSubNormal.WithAggregateBits(AggregateBits.Calculated)));
                DateTime expectedTimestamp = aggregateTypeId == Objects.AggregateFunction_MaximumActualTime
                    ? start.AddSeconds(2)
                    : start;
                Assert.That(values[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)expectedTimestamp));
            });
        }

        private const string kNamespaceUri =
            "urn:opcfoundation:history-tests:server-core-regressions";

        private ushort m_namespaceIndex;
        private readonly StreamingPageProvider m_streamingProvider = new();

        private sealed class HistoryRegressionNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public HistoryRegressionNodeManagerFactory(StreamingPageProvider streamingProvider)
            {
                m_streamingProvider = streamingProvider;
            }

            public ArrayOf<string> NamespacesUris => [kNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(
                    new HistoryRegressionNodeManager(server, configuration, m_streamingProvider));
            }

            private readonly StreamingPageProvider m_streamingProvider;
        }

        private sealed class HistoryRegressionNodeManager : AsyncCustomNodeManager
        {
            public HistoryRegressionNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                StreamingPageProvider streamingProvider)
                : base(server, configuration, kNamespaceUri)
            {
                m_streamingProvider = streamingProvider;
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await AddVariableAsync(
                    "Stepped",
                    HistorianNodeCapabilities.DataReadWrite with { Stepped = true },
                    cancellationToken).ConfigureAwait(false);
                await AddVariableAsync(
                    "Sloped",
                    HistorianNodeCapabilities.DataReadWrite,
                    cancellationToken).ConfigureAwait(false);
                await AddVariableAsync(
                    "UncertainIsGood",
                    HistorianNodeCapabilities.DataReadWrite with
                    {
                        DefaultAggregateConfiguration = new AggregateConfiguration
                        {
                            TreatUncertainAsBad = false,
                            PercentDataBad = 100,
                            PercentDataGood = 100
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
                await AddVariableAsync(
                    "UncertainIsBad",
                    HistorianNodeCapabilities.DataReadWrite,
                    cancellationToken).ConfigureAwait(false);
                await AddNotifierAsync("ForwardEvents", cancellationToken).ConfigureAwait(false);
                await AddNotifierAsync("ReverseEvents", cancellationToken).ConfigureAwait(false);
                await AddVariableAsync(
                    "OutputLimit",
                    HistorianNodeCapabilities.ReadOnly,
                    cancellationToken).ConfigureAwait(false);
                await AddVariableAsync(
                    "Extrema",
                    HistorianNodeCapabilities.DataReadWrite,
                    cancellationToken).ConfigureAwait(false);
            }

            protected override IHistorianProvider? GetHistorianProvider(NodeState node)
            {
                return node.NodeId == new NodeId("OutputLimit", NamespaceIndex)
                    ? m_streamingProvider
                    : m_provider;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_provider.Dispose();
                }
                base.Dispose(disposing);
            }

            private async ValueTask AddVariableAsync(
                string identifier,
                HistorianNodeCapabilities capabilities,
                CancellationToken cancellationToken)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(SystemContext, cancellationToken);
                variable.NodeId = new NodeId(identifier, NamespaceIndex);
                variable.BrowseName = new QualifiedName(identifier, NamespaceIndex);
                variable.DisplayName = new LocalizedText(identifier);
                variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
                variable.DataType = DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.Value = new Variant(0.0);
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;

                await using (var builder = new HistorianBuilder(Server))
                {
                    builder.UseProvider(m_provider).Historize(
                        variable,
                        systemContext: SystemContext,
                        capabilities: capabilities,
                        autoCapture: false);
                }
                await HistoricalDataConfigurationInstaller.EnsureInstalledAsync(
                    SystemContext, variable, m_provider, cancellationToken).ConfigureAwait(false);
                await AddPredefinedNodeAsync(
                    SystemContext, variable, cancellationToken).ConfigureAwait(false);
            }

            private async ValueTask AddNotifierAsync(string identifier, CancellationToken cancellationToken)
            {
                var notifier = new BaseObjectState(null);
                notifier.CreateAsPredefinedNode(SystemContext, cancellationToken);
                notifier.NodeId = new NodeId(identifier, NamespaceIndex);
                notifier.BrowseName = new QualifiedName(identifier, NamespaceIndex);
                notifier.DisplayName = new LocalizedText(identifier);
                notifier.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
                notifier.EventNotifier = EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
                m_provider.Register(
                    notifier.NodeId,
                    HistorianNodeCapabilities.EventReadWrite with
                    {
                        EventTypes = [ObjectTypeIds.BaseEventType]
                    });
                await AddPredefinedNodeAsync(
                    SystemContext, notifier, cancellationToken).ConfigureAwait(false);
            }

            private readonly InMemoryHistorianProvider m_provider = new();
            private readonly StreamingPageProvider m_streamingProvider;
        }

        private sealed class StreamingPageProvider : HistorianProviderBase, IHistorianDataProvider
        {
            public static DateTime Start { get; } = new(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc);

            public int ReadCount { get; private set; }

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext context,
                HistorianRawReadRequest request,
                HistorianResumeToken resumeToken,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                ReadCount++;
                if (!resumeToken.IsEmpty)
                {
                    return new ValueTask<HistorianPage<HistoricalDataValue>>(
                        HistorianPage<HistoricalDataValue>.Empty);
                }
                DateTime end = Start.AddMilliseconds(100_003);
                return new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                        [
                            new HistoricalDataValue(new DataValue(5.0, StatusCodes.Good, Start, Start)),
                            new HistoricalDataValue(new DataValue(7.0, StatusCodes.Good, end, end))
                        ],
                        new HistorianResumeToken(ByteString.From([1]))));
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteRawAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                bool isDeleteModified,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteAtTimeAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DateTimeUtc> timestamps,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
