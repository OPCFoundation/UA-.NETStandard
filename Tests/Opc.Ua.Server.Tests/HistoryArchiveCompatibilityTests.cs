/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using TestData;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Aggregators")]
    [Parallelizable]
    public class HistoryArchiveCompatibilityTests
    {
        [Test]
        public void LegacyHistoryRecordStorageLayoutIsUnchanged()
        {
            string[] fields = typeof(HistoryRecord)
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Select(field => $"{field.FieldType.Name} {field.Name}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                fields,
                Is.EqualTo(s_historyRecordFields));
        }

        [Test]
        public void ProcessedReadDoesNotModifyLegacyRawHistory()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var nodeId = new NodeId("history-layout-test", 1);
            var archive = new HistoryArchive(telemetry);
            archive.CreateRecord(nodeId, BuiltInType.Int32);
            archive.Dispose();

            HistoryFile history = archive.GetHistoryFile(nodeId);
            List<DataValue> before = ReadAll(history);
            Assert.That(before, Has.Count.EqualTo(1001));
            Assert.That(before[0].Value, Is.EqualTo(1000));
            Assert.That(before[^1].Value, Is.EqualTo(0));

            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            using var manager = new AggregateManager(server.Object);
            server.SetupGet(value => value.AggregateManager).Returns(manager);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Average,
                Aggregators.GetNameForStandardAggregate(
                    ObjectIds.AggregateFunction_Average).Name,
                Aggregators.CreateStandardCalculator);

            var details = new ReadProcessedDetails
            {
                StartTime = before[0].ServerTimestamp,
                EndTime = before[^1].ServerTimestamp,
                ProcessingInterval = 60000,
                AggregateConfiguration = new AggregateConfiguration
                {
                    UseServerCapabilitiesDefaults = true
                }
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ParsedIndexRange = NumericRange.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = ProcessedHistoryAdapter.Read(
                new ServerSystemContext(server.Object),
                server.Object,
                nodeId,
                history,
                details,
                ObjectIds.AggregateFunction_Average,
                archive.GetAnnotationTimestamps(nodeId),
                TimestampsToReturn.Both,
                nodeToRead,
                result);

            Assert.That(ServiceResult.IsGood(error), Is.True, error?.ToString());
            Assert.That(result.HistoryData?.Body, Is.TypeOf<HistoryData>());

            List<DataValue> after = ReadAll(history);
            Assert.That(after, Has.Count.EqualTo(before.Count));
            for (int ii = 0; ii < before.Count; ii++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(after[ii].Value, Is.EqualTo(before[ii].Value));
                    Assert.That(after[ii].StatusCode, Is.EqualTo(before[ii].StatusCode));
                    Assert.That(
                        after[ii].SourceTimestamp,
                        Is.EqualTo(before[ii].SourceTimestamp));
                    Assert.That(
                        after[ii].ServerTimestamp,
                        Is.EqualTo(before[ii].ServerTimestamp));
                });
            }
        }

        [Test]
        public async Task ParallelProcessedReadsUseIndependentSnapshots()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var nodeId = new NodeId("parallel-history-test", 1);
            var archive = new HistoryArchive(telemetry);
            archive.CreateRecord(nodeId, BuiltInType.Double, true);
            archive.Dispose();

            HistoryFile history = archive.GetHistoryFile(nodeId);
            List<DataValue> before = ReadAll(history);

            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            using var manager = new AggregateManager(server.Object);
            server.SetupGet(value => value.AggregateManager).Returns(manager);
            manager.RegisterFactory(
                ObjectIds.AggregateFunction_Average,
                Aggregators.GetNameForStandardAggregate(
                    ObjectIds.AggregateFunction_Average).Name,
                Aggregators.CreateStandardCalculator);

            Task<(ServiceResult Error, int Count)>[] reads = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    var details = new ReadProcessedDetails
                    {
                        StartTime = before[0].ServerTimestamp,
                        EndTime = before[^1].ServerTimestamp,
                        ProcessingInterval = 120000,
                        AggregateConfiguration = new AggregateConfiguration
                        {
                            UseServerCapabilitiesDefaults = true
                        }
                    };
                    var result = new HistoryReadResult();
                    ServiceResult error = ProcessedHistoryAdapter.Read(
                        new ServerSystemContext(server.Object),
                        server.Object,
                        nodeId,
                        history,
                        details,
                        ObjectIds.AggregateFunction_Average,
                        archive.GetAnnotationTimestamps(nodeId),
                        TimestampsToReturn.Source,
                        new HistoryReadValueId
                        {
                            NodeId = nodeId,
                            ParsedIndexRange = NumericRange.Empty
                        },
                        result);
                    int count = result.HistoryData?.Body is HistoryData data
                        ? data.DataValues.Count
                        : 0;
                    return (error, count);
                }))
                .ToArray();

            (ServiceResult Error, int Count)[] results = await Task.WhenAll(reads)
                .ConfigureAwait(false);
            Assert.That(results.All(value => ServiceResult.IsGood(value.Error)), Is.True);
            Assert.That(
                results.Select(value => value.Count).Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(results[0].Count, Is.GreaterThan(0));

            List<DataValue> after = ReadAll(history);
            Assert.That(after, Has.Count.EqualTo(before.Count));
            Assert.That(
                after.Select(value => value.Value),
                Is.EqualTo(before.Select(value => value.Value)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RawReaderDoesNotCreateEmptyFinalPage(bool forward)
        {
            DateTime origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var source = new ListHistoryDataSource(
            [
                CreateValue(1, origin.AddSeconds(1)),
                CreateValue(2, origin.AddSeconds(2)),
                CreateValue(3, origin.AddSeconds(3))
            ]);
            var details = new ReadRawModifiedDetails
            {
                StartTime = forward ? origin : origin.AddSeconds(4),
                EndTime = forward ? origin.AddSeconds(4) : origin,
                NumValuesPerNode = 1,
                ReturnBounds = false
            };
            using var reader = new HistoryDataReader(
                new NodeId("paged-history", 1),
                source);

            var pages = new List<DataValueCollection>();
            var hasContinuationPoint = new List<bool>();
            bool firstPage = true;
            bool complete;
            do
            {
                var values = new DataValueCollection();
                if (firstPage)
                {
                    reader.BeginReadRaw(
                        null,
                        details,
                        TimestampsToReturn.Both,
                        NumericRange.Empty,
                        QualifiedName.Null,
                        values);
                    firstPage = false;
                }

                complete = reader.NextReadRaw(
                    null,
                    TimestampsToReturn.Both,
                    NumericRange.Empty,
                    QualifiedName.Null,
                    values);
                pages.Add(values);
                hasContinuationPoint.Add(!complete);
            } while (!complete);

            Assert.That(pages, Has.Count.EqualTo(3));
            Assert.That(pages.All(page => page.Count == 1), Is.True);
            Assert.That(hasContinuationPoint, Is.EqualTo(new[] { true, true, false }));
            Assert.That(
                pages.Select(page => (int)page[0].Value),
                Is.EqualTo(forward ? new[] { 1, 2, 3 } : new[] { 3, 2, 1 }));
        }

        private static DataValue CreateValue(int value, DateTime timestamp)
        {
            return new DataValue
            {
                Value = value,
                StatusCode = StatusCodes.Good,
                SourceTimestamp = timestamp,
                ServerTimestamp = timestamp
            };
        }

        private static List<DataValue> ReadAll(HistoryFile source)
        {
            var values = new List<DataValue>();
            DataValue value = source.FirstRaw(
                DateTime.MinValue,
                true,
                false,
                out int position);
            while (value != null)
            {
                values.Add(value);
                value = source.NextRaw(
                    value.ServerTimestamp,
                    true,
                    false,
                    ref position);
            }

            return values;
        }

        private static readonly string[] s_historyRecordFields =
        [
            "Boolean Historizing",
            "List`1 RawData"
        ];

        private sealed class ListHistoryDataSource : IHistoryDataSource
        {
            public ListHistoryDataSource(IReadOnlyList<DataValue> values)
            {
                m_values = values;
            }

            public DataValue FirstRaw(
                DateTime startTime,
                bool isForward,
                bool isReadModified,
                out int position)
            {
                position = -1;
                if (isForward)
                {
                    for (int ii = 0; ii < m_values.Count; ii++)
                    {
                        if (m_values[ii].ServerTimestamp >= startTime)
                        {
                            position = ii;
                            break;
                        }
                    }
                }
                else
                {
                    for (int ii = m_values.Count - 1; ii >= 0; ii--)
                    {
                        if (m_values[ii].ServerTimestamp <= startTime)
                        {
                            position = ii;
                            break;
                        }
                    }
                }
                return GetValue(position);
            }

            public DataValue NextRaw(
                DateTime lastTime,
                bool isForward,
                bool isReadModified,
                ref int position)
            {
                position += isForward ? 1 : -1;
                return GetValue(position);
            }

            private DataValue GetValue(int position)
            {
                if (position < 0 || position >= m_values.Count)
                {
                    return null;
                }

                DataValue value = m_values[position];
                return new DataValue
                {
                    WrappedValue = value.WrappedValue,
                    StatusCode = value.StatusCode,
                    SourceTimestamp = value.SourceTimestamp,
                    ServerTimestamp = value.ServerTimestamp
                };
            }

            private readonly IReadOnlyList<DataValue> m_values;
        }
    }
}
