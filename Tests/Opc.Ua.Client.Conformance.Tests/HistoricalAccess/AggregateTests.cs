/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Historical Access aggregate (processed) reads
    /// and additional raw-read scenarios on the ReferenceServer.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("HistoricalAccess")]
    [Category("Aggregate")]
    public class AggregateTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawReturnsValuesForHistorizingVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadRawHistoryOrIgnoreAsync(
                nodeId, startTime, endTime, 100).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one historical value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawWithTimeRangeFiltersAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadRawHistoryOrIgnoreAsync(
                nodeId, startTime, endTime, 1000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);

            // The server is expected to return values within (or near)
            // the requested time range. Boundary values may slightly
            // exceed the range per the OPC UA spec.
            if (values.Count >= 2)
            {
                DateTimeUtc first = values[0].ServerTimestamp;
                DateTimeUtc last = values[^1].ServerTimestamp;
                Assert.That((DateTime)first, Is.GreaterThanOrEqualTo(startTime.AddSeconds(-30)),
                    "First value is too far before the start time.");
                Assert.That((DateTime)last, Is.LessThanOrEqualTo(endTime.AddSeconds(30)),
                    "Last value is too far after the end time.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawWithNumValuesPerNodeLimitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);
            const uint limit = 5;

            List<DataValue> values = await ReadRawHistoryOrIgnoreAsync(
                nodeId, startTime, endTime, limit).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Has.Count.LessThanOrEqualTo((int)limit));
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawWithContinuationPointPaginationAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            // Request only 1 value to force a continuation point.
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 1,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode sc = response.Results[0].StatusCode;

            if (!StatusCode.IsGood(sc))
            {
                Assert.Fail($"History not supported: {sc}");
            }

            ByteString cp = response.Results[0].ContinuationPoint;

            if (!cp.IsEmpty)
            {
                // Read second page.
                HistoryReadResponse page2 = await Session.HistoryReadAsync(
                    null,
                    new ExtensionObject(new ReadRawModifiedDetails
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        NumValuesPerNode = 1,
                        IsReadModified = false,
                        ReturnBounds = false
                    }),
                    TimestampsToReturn.Both,
                    false,
                    new HistoryReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            ContinuationPoint = cp
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(page2.Results.Count, Is.EqualTo(1));

                var page2Data = ExtensionObject.ToEncodeable(
                    page2.Results[0].HistoryData) as HistoryData;

                Assert.That(page2Data?.DataValues, Is.Not.Null);

                // Release any remaining continuation point.
                ByteString cp2 = page2.Results[0].ContinuationPoint;
                if (!cp2.IsEmpty)
                {
                    await Session.HistoryReadAsync(
                        null,
                        new ExtensionObject(new ReadRawModifiedDetails()),
                        TimestampsToReturn.Both,
                        true,
                        new HistoryReadValueId[]
                        {
                            new() {
                                NodeId = nodeId,
                                ContinuationPoint = cp2
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawReturnsValuesOrderedByTimestampAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadRawHistoryOrIgnoreAsync(
                nodeId, startTime, endTime, 50).ConfigureAwait(false);

            if (values.Count < 2)
            {
                Assert.Fail("Not enough values to verify ordering.");
            }

            for (int i = 1; i < values.Count; i++)
            {
                Assert.That(
                    values[i].ServerTimestamp,
                    Is.GreaterThanOrEqualTo(values[i - 1].ServerTimestamp),
                    "Values are not ordered by timestamp.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadWithTimestampsToReturnSourceAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 10,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Source,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            StatusCode sc = response.Results[0].StatusCode;

            if (!StatusCode.IsGood(sc))
            {
                Assert.Fail($"History not supported: {sc}");
            }

            var historyData = ExtensionObject.ToEncodeable(
                response.Results[0].HistoryData) as HistoryData;

            if (historyData?.DataValues == null || historyData.DataValues.Count == 0)
            {
                Assert.Fail("No history data returned.");
            }

            foreach (DataValue dv in historyData.DataValues)
            {
                Assert.That(
                    dv.SourceTimestamp,
                    Is.Not.EqualTo(DateTimeUtc.MinValue),
                    "Source timestamp should be present.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadOnNonHistorizingVariableReturnsBadStatusAsync()
        {
            // ScalarStaticString does not have history enabled.
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 10,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False,
                "Expected a bad status for a non-historizing variable.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task ReadHistorizingAttributeOnHistoricalVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Historizing
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
            Assert.That((bool)readResponse.Results[0].WrappedValue, Is.True,
                "Historizing attribute should be true.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task ReadAccessLevelIncludesHistoryReadBitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);

            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.AccessLevel
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);

            byte accessLevel = (byte)readResponse.Results[0].WrappedValue;
            Assert.That(
                (accessLevel & AccessLevels.HistoryRead) != 0,
                Is.True,
                "AccessLevel should include the HistoryRead bit.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadWithStartTimeAfterEndTimeReturnsResultAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            // Reversed time range.
            DateTime startTime = DateTime.UtcNow;
            DateTime endTime = startTime.AddHours(-3);

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = 10,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            // Any result is acceptable (values in reverse order, empty, or error).
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadRawOnInt32VariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalInt32);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadRawHistoryOrIgnoreAsync(
                nodeId, startTime, endTime, 10).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected historical data for Int32 variable.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithAverageAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Average,
                3600000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one average aggregate value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithMinAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Minimum,
                3600000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one minimum aggregate value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithMaxAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Maximum,
                3600000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one maximum aggregate value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithCountAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Count,
                3600000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one count aggregate value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithInterpolativeAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Interpolative,
                3600000).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Is.Not.Empty,
                "Expected at least one interpolative aggregate value.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithProcessingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-3);
            // 30-minute intervals over a 3-hour window → expect multiple buckets.
            const double interval = 1800000;

            List<DataValue> values = await ReadProcessedHistoryOrIgnoreAsync(
                nodeId,
                startTime,
                endTime,
                ObjectIds.AggregateFunction_Average,
                interval).ConfigureAwait(false);

            Assert.That(values, Is.Not.Null);
            Assert.That(values, Has.Count.GreaterThan(1),
                "Expected multiple aggregate intervals.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedOnNonHistorizingVariableReturnsBadStatusAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);

            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = 3600000,
                AggregateType = new NodeId[]
                {
                    ObjectIds.AggregateFunction_Average
                }.ToArrayOf(),
                AggregateConfiguration = new AggregateConfiguration
                {
                    UseServerCapabilitiesDefaults = true
                }
            };

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False,
                "Expected bad status for non-historizing variable.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task HistoryReadProcessedWithUnsupportedAggregateAsync()
        {
            NodeId nodeId = ToNodeId(Constants.HistoricalDouble);
            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-1);
            // Use a made-up aggregate ID.
            var fakeAggregateId = new NodeId(99999);

            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = 3600000,
                AggregateType = new NodeId[]
                {
                    fakeAggregateId
                }.ToArrayOf(),
                AggregateConfiguration = new AggregateConfiguration
                {
                    UseServerCapabilitiesDefaults = true
                }
            };

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.False,
                "Expected bad status for unsupported aggregate.");
        }

        [Test]
        [Property("ConformanceUnit", "HA Aggregate")]
        [Property("Tag", "N/A")]
        public async Task BrowseAggregateFunctionsFolderContainsNodesAsync()
        {
            BrowseResponse browseResponse = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerCapabilities_AggregateFunctions,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(browseResponse.Results.Count, Is.EqualTo(1));
            BrowseResult result = browseResponse.Results[0];

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    $"Cannot browse AggregateFunctions: {result.StatusCode}");
            }

            Assert.That(result.References, Is.Not.Null);
            Assert.That(result.References.Count, Is.GreaterThan(0),
                "Expected at least one aggregate function node.");

            // Verify standard aggregates are present.
            List<string> names = [];
            foreach (ReferenceDescription r in result.References)
            {
                names.Add(r.BrowseName.Name);
            }

            Assert.That(names, Does.Contain("Interpolative"));
            Assert.That(names, Does.Contain("Average"));
        }

        /// <summary>
        /// Reads raw history and returns the data values, or ignores
        /// the test when history is not supported.
        /// </summary>
        private async Task<List<DataValue>> ReadRawHistoryOrIgnoreAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValues = 0)
        {
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    NumValuesPerNode = maxValues,
                    IsReadModified = false,
                    ReturnBounds = false
                }),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            StatusCode sc = response.Results[0].StatusCode;
            if (!StatusCode.IsGood(sc))
            {
                Assert.Ignore($"History not supported: {sc}");
            }

            var historyData = ExtensionObject.ToEncodeable(
                response.Results[0].HistoryData) as HistoryData;

            return historyData?.DataValues.ToList() ?? [];
        }

        /// <summary>
        /// Reads processed (aggregate) history and returns the result,
        /// or ignores the test when aggregates are not supported.
        /// </summary>
        private async Task<List<DataValue>> ReadProcessedHistoryOrIgnoreAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            NodeId aggregateId,
            double processingInterval)
        {
            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                AggregateType = new NodeId[] { aggregateId }.ToArrayOf(),
                AggregateConfiguration = new AggregateConfiguration
                {
                    UseServerCapabilitiesDefaults = true
                }
            };

            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Both,
                false,
                new HistoryReadValueId[]
                {
                    new() { NodeId = nodeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            StatusCode sc = response.Results[0].StatusCode;
            if (sc == StatusCodes.BadAggregateNotSupported ||
                sc == StatusCodes.BadHistoryOperationUnsupported ||
                sc == StatusCodes.BadHistoryOperationInvalid ||
                sc == StatusCodes.BadNotSupported)
            {
                Assert.Ignore($"Aggregate not supported: {sc}");
            }

            var historyData = ExtensionObject.ToEncodeable(
                response.Results[0].HistoryData) as HistoryData;

            return historyData?.DataValues.ToList() ?? [];
        }
    }
}
