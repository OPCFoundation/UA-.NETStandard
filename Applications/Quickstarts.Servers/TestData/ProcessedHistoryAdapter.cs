/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Bridges the legacy history source to the existing aggregate calculators
    /// without changing the HistoryFile storage contract.
    /// </summary>
    internal static class ProcessedHistoryAdapter
    {
        internal const int PageSize = 1000;
        internal const int MaxBufferedOutputs = 100000;

        public static ServiceResult Read(
            ServerSystemContext context,
            IServerInternal server,
            NodeId variableId,
            IHistoryDataSource source,
            ReadProcessedDetails details,
            NodeId aggregateId,
            IReadOnlyList<DateTime> annotationTimes,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result)
        {
            if (details.StartTime == details.EndTime)
            {
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return StatusCodes.BadInvalidArgument;
            }

            ProcessedHistoryContinuationState state = null;
            if (nodeToRead.ContinuationPoint != null &&
                nodeToRead.ContinuationPoint.Length > 0)
            {
                state = Restore(context, nodeToRead.ContinuationPoint);
                if (state == null ||
                    state.VariableId != variableId ||
                    state.AggregateId != aggregateId)
                {
                    state?.Dispose();
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = null;
                    return StatusCodes.BadContinuationPointInvalid;
                }
            }

            if (state == null)
            {
                ServiceResult error = Compute(
                    server,
                    variableId,
                    source,
                    details,
                    aggregateId,
                    annotationTimes,
                    out List<DataValue> outputs);
                if (ServiceResult.IsBad(error))
                {
                    result.StatusCode = error.StatusCode;
                    return error;
                }

                state = new ProcessedHistoryContinuationState(
                    variableId,
                    aggregateId,
                    outputs);
            }

            EmitPage(context, state, timestampsToReturn, nodeToRead, result);
            return ServiceResult.Good;
        }

        public static ProcessedHistoryContinuationState Restore(
            ServerSystemContext context,
            byte[] continuationPoint)
        {
            if (context?.OperationContext?.Session == null)
            {
                return null;
            }

            return context.OperationContext.Session.RestoreHistoryContinuationPoint(
                continuationPoint) as ProcessedHistoryContinuationState;
        }

        private static ServiceResult Compute(
            IServerInternal server,
            NodeId variableId,
            IHistoryDataSource source,
            ReadProcessedDetails details,
            NodeId aggregateId,
            IReadOnlyList<DateTime> annotationTimes,
            out List<DataValue> outputs)
        {
            outputs = [];

            if (source == null)
            {
                return StatusCodes.BadNotReadable;
            }

            AggregateConfiguration configuration = ResolveConfiguration(
                server.AggregateManager,
                variableId,
                details.AggregateConfiguration);

            if (configuration.PercentDataGood > 100 ||
                configuration.PercentDataBad > 100 ||
                configuration.PercentDataGood < 100 - configuration.PercentDataBad)
            {
                return StatusCodes.BadAggregateInvalidInputs;
            }

            double processingInterval = details.ProcessingInterval;
            if (processingInterval > 0 &&
                Math.Abs((details.EndTime - details.StartTime).TotalMilliseconds) /
                processingInterval >
                MaxBufferedOutputs)
            {
                return StatusCodes.BadTooManyOperations;
            }

            if (aggregateId == Opc.Ua.ObjectIds.AggregateFunction_AnnotationCount)
            {
                return ComputeAnnotationCount(
                    details,
                    annotationTimes ?? Array.Empty<DateTime>(),
                    outputs);
            }

            IAggregateCalculator calculator = server.AggregateManager.CreateCalculator(
                aggregateId,
                details.StartTime,
                details.EndTime,
                processingInterval,
                false,
                configuration);
            if (calculator == null)
            {
                return StatusCodes.BadAggregateNotSupported;
            }

            bool isForward = details.StartTime < details.EndTime;
            DataValue value = source.FirstRaw(
                details.StartTime,
                !isForward,
                false,
                out int position);

            // If the leading bound does not exist, begin with the first value
            // in the requested direction.
            if (value == null)
            {
                value = source.FirstRaw(
                    details.StartTime,
                    isForward,
                    false,
                    out position);
            }

            while (value != null)
            {
                if (!calculator.QueueRawValue(value))
                {
                    Flush(calculator, outputs, false);
                }

                if (outputs.Count > MaxBufferedOutputs)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                bool passedEnd = isForward
                    ? value.ServerTimestamp > details.EndTime
                    : value.ServerTimestamp < details.EndTime;
                if (passedEnd)
                {
                    break;
                }

                value = source.NextRaw(
                    value.ServerTimestamp,
                    isForward,
                    false,
                    ref position);
            }

            Flush(calculator, outputs, true);
            return outputs.Count > MaxBufferedOutputs
                ? StatusCodes.BadTooManyOperations
                : ServiceResult.Good;
        }

        private static ServiceResult ComputeAnnotationCount(
            ReadProcessedDetails details,
            IReadOnlyList<DateTime> annotationTimes,
            List<DataValue> outputs)
        {
            bool isForward = details.StartTime < details.EndTime;
            if (details.ProcessingInterval <= 0)
            {
                DateTime low = isForward ? details.StartTime : details.EndTime;
                DateTime high = isForward ? details.EndTime : details.StartTime;
                outputs.Add(CreateAnnotationCountValue(
                    CountAnnotations(annotationTimes, low, high),
                    details.StartTime));
                return ServiceResult.Good;
            }

            TimeSpan interval = TimeSpan.FromMilliseconds(details.ProcessingInterval);
            if (isForward)
            {
                for (DateTime start = details.StartTime;
                    start < details.EndTime;
                    start = start.Add(interval))
                {
                    DateTime end = start.Add(interval);
                    if (end > details.EndTime)
                    {
                        end = details.EndTime;
                    }

                    outputs.Add(CreateAnnotationCountValue(
                        CountAnnotations(annotationTimes, start, end),
                        start));
                }
            }
            else
            {
                for (DateTime start = details.StartTime;
                    start > details.EndTime;
                    start = start.Subtract(interval))
                {
                    DateTime end = start.Subtract(interval);
                    if (end < details.EndTime)
                    {
                        end = details.EndTime;
                    }

                    outputs.Add(CreateAnnotationCountValue(
                        CountAnnotations(annotationTimes, end, start),
                        start));
                }
            }

            return ServiceResult.Good;
        }

        private static int CountAnnotations(
            IReadOnlyList<DateTime> annotationTimes,
            DateTime lowInclusive,
            DateTime highExclusive)
        {
            int count = 0;
            for (int ii = 0; ii < annotationTimes.Count; ii++)
            {
                DateTime timestamp = annotationTimes[ii];
                if (timestamp >= lowInclusive && timestamp < highExclusive)
                {
                    count++;
                }
            }

            return count;
        }

        private static DataValue CreateAnnotationCountValue(int count, DateTime timestamp)
        {
            var value = new DataValue(
                new Variant(count),
                StatusCodes.Good,
                timestamp,
                timestamp);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            return value;
        }

        private static AggregateConfiguration ResolveConfiguration(
            AggregateManager manager,
            NodeId variableId,
            AggregateConfiguration requested)
        {
            bool implicitDefault = requested != null &&
                !requested.UseServerCapabilitiesDefaults &&
                requested.PercentDataBad == 0 &&
                requested.PercentDataGood == 0 &&
                !requested.TreatUncertainAsBad &&
                !requested.UseSlopedExtrapolation;

            if (requested == null ||
                requested.UseServerCapabilitiesDefaults ||
                implicitDefault)
            {
                return manager.GetDefaultConfiguration(variableId);
            }

            return requested;
        }

        private static void Flush(
            IAggregateCalculator calculator,
            List<DataValue> outputs,
            bool returnPartial)
        {
            DataValue value;
            while ((value = calculator.GetProcessedValue(returnPartial)) != null)
            {
                outputs.Add(value);
            }
        }

        private static void EmitPage(
            ServerSystemContext context,
            ProcessedHistoryContinuationState state,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result)
        {
            int count = Math.Min(PageSize, state.Outputs.Count - state.Offset);
            var data = new HistoryData();

            for (int ii = 0; ii < count; ii++)
            {
                DataValue value = (DataValue)state.Outputs[state.Offset + ii].Clone();
                ApplyResultOptions(value, timestampsToReturn, nodeToRead);
                data.DataValues.Add(value);
            }

            state.Offset += count;
            result.HistoryData = new ExtensionObject(data);
            result.StatusCode = StatusCodes.Good;

            if (state.Offset >= state.Outputs.Count)
            {
                result.ContinuationPoint = null;
                state.Dispose();
                return;
            }

            state.Id = Guid.NewGuid();
            context.OperationContext?.Session?.SaveHistoryContinuationPoint(state.Id, state);
            result.ContinuationPoint = state.Id.ToByteArray();
        }

        private static void ApplyResultOptions(
            DataValue value,
            TimestampsToReturn timestampsToReturn,
            HistoryReadValueId nodeToRead)
        {
            if (StatusCode.IsGood(value.StatusCode))
            {
                object valueToReturn = value.Value;
                if (nodeToRead.ParsedIndexRange != NumericRange.Empty)
                {
                    StatusCode error = nodeToRead.ParsedIndexRange.ApplyRange(ref valueToReturn);
                    if (StatusCode.IsBad(error))
                    {
                        value.Value = null;
                        value.StatusCode = error;
                    }
                    else
                    {
                        value.Value = valueToReturn;
                    }
                }

                if (!QualifiedName.IsNull(nodeToRead.DataEncoding))
                {
                    value.Value = null;
                    value.StatusCode = StatusCodes.BadDataEncodingUnsupported;
                }
            }

            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Server)
            {
                value.SourceTimestamp = DateTime.MinValue;
            }

            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Source)
            {
                value.ServerTimestamp = DateTime.MinValue;
            }
        }
    }

    internal sealed class ProcessedHistoryContinuationState : IDisposable
    {
        public ProcessedHistoryContinuationState(
            NodeId variableId,
            NodeId aggregateId,
            List<DataValue> outputs)
        {
            Id = Guid.NewGuid();
            VariableId = variableId;
            AggregateId = aggregateId;
            Outputs = outputs;
        }

        public Guid Id { get; set; }

        public NodeId VariableId { get; }

        public NodeId AggregateId { get; }

        public List<DataValue> Outputs { get; }

        public int Offset { get; set; }

        public void Dispose()
        {
            // The state owns only managed, immutable-for-the-read data.
        }
    }
}
