/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Moq;
using Opc.Ua.Tests;
using TestData;

namespace Opc.Ua.Server.Tests
{
    internal static partial class HistorianBackportTestCompatibility
    {
        public static List<DataValue> RunLegacyProcessed(
            NodeId aggregateId,
            List<DataValue> rawValues,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            AggregateConfiguration configuration,
            out ServiceResult error,
            out HistoryReadResult result)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Telemetry).Returns(telemetry);

            using var manager = new AggregateManager(server.Object);
            server.SetupGet(s => s.AggregateManager).Returns(manager);
            QualifiedName aggregateName = Aggregators.GetNameForStandardAggregate(aggregateId);
            manager.RegisterFactory(
                aggregateId,
                aggregateName.Name,
                Aggregators.CreateStandardCalculator);

            var source = new MemoryHistorySource(rawValues);
            var nodeId = new NodeId("legacy-processed-test", 1);
            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                AggregateConfiguration = configuration
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ParsedIndexRange = NumericRange.Empty
            };
            result = new HistoryReadResult();
            var context = new ServerSystemContext(server.Object);

            error = ProcessedHistoryAdapter.Read(
                context,
                server.Object,
                nodeId,
                source,
                details,
                aggregateId,
                Array.Empty<DateTime>(),
                TimestampsToReturn.Source,
                nodeToRead,
                result);

            return result.HistoryData?.Body is HistoryData historyData
                ? [.. historyData.DataValues]
                : [];
        }

        private sealed class MemoryHistorySource : IHistoryDataSource
        {
            public MemoryHistorySource(List<DataValue> values)
            {
                m_values = new List<DataValue>(values);
                m_values.Sort((left, right) =>
                    left.ServerTimestamp.CompareTo(right.ServerTimestamp));
            }

            public DataValue FirstRaw(
                DateTime startTime,
                bool isForward,
                bool isReadModified,
                out int position)
            {
                _ = isReadModified;
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

                return position < 0 ? null : (DataValue)m_values[position].Clone();
            }

            public DataValue NextRaw(
                DateTime lastTime,
                bool isForward,
                bool isReadModified,
                ref int position)
            {
                _ = lastTime;
                _ = isReadModified;
                position += isForward ? 1 : -1;
                return position < 0 || position >= m_values.Count
                    ? null
                    : (DataValue)m_values[position].Clone();
            }

            private readonly List<DataValue> m_values;
        }
    }
}
