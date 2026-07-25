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
    }
}
