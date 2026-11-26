// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class MinMaxAggregateCalculator : AggregateCalculator
    {
        /// <summary>
        /// Initializes the aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public MinMaxAggregateCalculator(
            NodeId aggregateId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration,
            ITelemetryContext telemetry)
            : base(aggregateId, startTime, endTime, processingInterval, stepped, configuration, telemetry)
        {
            SetPartialBit = true;
        }

        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        protected override DataValue ComputeValue(TimeSlice slice)
        {
            if (!AggregateId.TryGetValue(out uint numericId))
            {
                return base.ComputeValue(slice);
            }
            switch (numericId)
            {
                case Objects.AggregateFunction_Minimum:
                    return ComputeMinMax(slice, 1, false);
                case Objects.AggregateFunction_MinimumActualTime:
                    return ComputeMinMax(slice, 1, true);
                case Objects.AggregateFunction_Maximum:
                    return ComputeMinMax(slice, 2, false);
                case Objects.AggregateFunction_MaximumActualTime:
                    return ComputeMinMax(slice, 2, true);
                case Objects.AggregateFunction_Range:
                    return ComputeMinMax(slice, 3, false);
                case Objects.AggregateFunction_Minimum2:
                    return ComputeMinMax2(slice, 1, false);
                case Objects.AggregateFunction_MinimumActualTime2:
                    return ComputeMinMax2(slice, 1, true);
                case Objects.AggregateFunction_Maximum2:
                    return ComputeMinMax2(slice, 2, false);
                case Objects.AggregateFunction_MaximumActualTime2:
                    return ComputeMinMax2(slice, 2, true);
                case Objects.AggregateFunction_Range2:
                    return ComputeMinMax2(slice, 3, false);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculate the Minimum, Maximum, MinimumActualTime and MaximumActualTime aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // Mantis 11426 ~0025852 (OPC Classic semantics of Minimum, Maximum, MinimumActualTime,
            // MaximumActualTime and Range): Uncertain values are looked at as if they are Good and are
            // candidates whatever TreatUncertainAsBad is; the status is Good unless Bad values exist in
            // the interval or the selected extremum is Uncertain (then Uncertain_DataSubNormal), and
            // Bad_NoData if all values are Bad.
            double minimumValue = double.MaxValue;
            double maximumValue = double.MinValue;

            DateTime minimumTimestamp = DateTime.MinValue;
            DateTime maximumTimestamp = DateTime.MinValue;

            TypeInfo minimumOriginalType = default;
            TypeInfo maximumOriginalType = default;

            bool minimumIsUncertain = false;
            bool maximumIsUncertain = false;
            bool badValuesExist = false;
            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool nonBadValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                var currentTime = (DateTime)values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values.
                if (!IsBoundCandidate(values[ii]))
                {
                    badValuesExist = true;
                    continue;
                }

                double currentValue;
                // convert to double.
                try
                {
                    currentValue = CastToDouble(values[ii]);
                }
                catch (Exception)
                {
                    badValuesExist = true;
                    continue;
                }

                bool currentIsUncertain = StatusCode.IsUncertain(currentStatus);

                // check for new minimum.
                if (!nonBadValueExists || minimumValue > currentValue)
                {
                    minimumValue = currentValue;
                    minimumTimestamp = currentTime;
                    minimumOriginalType = values[ii].WrappedValue.TypeInfo;
                    minimumIsUncertain = currentIsUncertain;
                    duplicatesMinimumsExist = false;
                }
                // check for duplicate minimums.
                else if (minimumValue == currentValue)
                {
                    duplicatesMinimumsExist = true;
                }

                // check for new maximum.
                if (!nonBadValueExists || maximumValue < currentValue)
                {
                    maximumValue = currentValue;
                    maximumTimestamp = currentTime;
                    maximumOriginalType = values[ii].WrappedValue.TypeInfo;
                    maximumIsUncertain = currentIsUncertain;
                    duplicatesMaximumsExist = false;
                }
                // check for duplicate maximums.
                else if (maximumValue == currentValue)
                {
                    duplicatesMaximumsExist = true;
                }

                nonBadValueExists = true;
            }

            // check if at least one non-bad value exists.
            if (!nonBadValueExists)
            {
                return GetNoDataValue(slice);
            }

            // set the status code.
            StatusCode statusCode = StatusCodes.Good;

            if (badValuesExist ||
                (valueType is 1 or 3 && minimumIsUncertain) ||
                (valueType is 2 or 3 && maximumIsUncertain))
            {
                statusCode = StatusCodes.UncertainDataSubNormal;
            }

            // determine the calculated value to return.
            Variant processedValue = default;
            TypeInfo processedType = default;
            DateTime processedTimestamp = DateTime.MinValue;
            bool duplicatesExist = false;

            if (valueType == 1)
            {
                processedValue = minimumValue;
                processedTimestamp = minimumTimestamp;
                processedType = minimumOriginalType;
                duplicatesExist = duplicatesMinimumsExist;
            }
            else if (valueType == 2)
            {
                processedValue = maximumValue;
                processedTimestamp = maximumTimestamp;
                processedType = maximumOriginalType;
                duplicatesExist = duplicatesMaximumsExist;
            }
            else if (valueType == 3)
            {
                double range = Math.Abs(maximumValue - minimumValue);
                processedValue = range;
                processedType = GetRangeType(minimumOriginalType, range);
            }

            // Non-Good inputs that affect quality also make ActualTime results Calculated.
            // Otherwise, preserve Raw for ActualTime and for an extremum at the
            // request-direction interval start (the later bound for reverse reads).
            if (StatusCode.IsUncertain(statusCode) ||
                (!returnActualTime && processedTimestamp != GetTimestamp(slice)))
            {
                statusCode = statusCode.WithAggregateBits(AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.WithAggregateBits(
                    statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // convert back to original datatype.
            if (processedType.IsUnknown ||
                processedType.BuiltInType == BuiltInType.Double)
            {
                processedType = TypeInfo.Scalars.Double;
            }
            processedValue = processedValue.ConvertTo(processedType.BuiltInType);

            // create processed value.
            var value = new DataValue(processedValue, statusCode);

            if (returnActualTime)
            {
                DateTimeUtc minMaxStamp = processedTimestamp;
                return value
                    .WithSourceTimestamp(minMaxStamp)
                    .WithServerTimestamp(minMaxStamp);
            }
            DateTimeUtc sliceStamp = GetTimestamp(slice);

            return value
                .WithSourceTimestamp(sliceStamp)
                .WithServerTimestamp(sliceStamp);
        }

        /// <summary>
        /// Calculate the Minimum2, Maximum2, MinimumActualTime2, MaximumActualTime2 and Range2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax2(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double minimumGoodValue = double.MaxValue;
            double maximumGoodValue = double.MinValue;

            DateTime minimumGoodTimestamp = DateTime.MinValue;
            DateTime maximumGoodTimestamp = DateTime.MinValue;

            StatusCode minimumGoodStatusCode = StatusCodes.Good;
            StatusCode maximumGoodStatusCode = StatusCodes.Good;

            TypeInfo minimumOriginalType = default;
            TypeInfo maximumOriginalType = default;

            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool goodValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                var currentTime = (DateTime)values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values (as determined by the TreatUncertainAsBad parameter).
                if (!IsGood(values[ii]))
                {
                    continue;
                }

                double currentValue;
                // convert to double.
                try
                {
                    currentValue = CastToDouble(values[ii]);
                }
                catch (Exception)
                {
                    continue;
                }

                // skip endpoint if stepped.
                if (currentTime == slice.EndTime && Stepped)
                {
                    break;
                }

                // check for new minimum.
                if (minimumGoodValue > currentValue)
                {
                    minimumGoodValue = currentValue;
                    minimumGoodTimestamp = currentTime;
                    minimumGoodStatusCode = currentStatus;
                    minimumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMinimumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate minimums.
                else if (minimumGoodValue == currentValue)
                {
                    duplicatesMinimumsExist = true;
                }

                // check for new maximum.
                if (maximumGoodValue < currentValue)
                {
                    maximumGoodValue = currentValue;
                    maximumGoodTimestamp = currentTime;
                    maximumGoodStatusCode = currentStatus;
                    maximumOriginalType = values[ii].WrappedValue.TypeInfo;
                    duplicatesMaximumsExist = false;
                    goodValueExists = true;
                }
                // check for duplicate maximums.
                else if (maximumGoodValue == currentValue)
                {
                    duplicatesMaximumsExist = true;
                }
            }

            // check if at least one good value exists.
            if (!goodValueExists)
            {
                DataValue noDataValue = GetNoDataValue(slice);
                // check if interval is partial and set the flag accordingly
                if (slice.Partial)
                {
                    noDataValue = noDataValue.WithStatus(
                        noDataValue.StatusCode.WithAggregateBits(AggregateBits.Partial));
                }
                return noDataValue;
            }

            // determine the calculated value to return.
            Variant processedValue = default;
            TypeInfo processedType = default;
            DateTime processedTimestamp = DateTime.MinValue;
            StatusCode processedStatusCode = StatusCodes.Good;
            bool duplicatesExist = false;

            if (valueType == 1)
            {
                processedValue = minimumGoodValue;
                processedTimestamp = minimumGoodTimestamp;
                processedStatusCode = minimumGoodStatusCode;
                processedType = minimumOriginalType;
                duplicatesExist = duplicatesMinimumsExist;
            }
            else if (valueType == 2)
            {
                processedValue = maximumGoodValue;
                processedTimestamp = maximumGoodTimestamp;
                processedStatusCode = maximumGoodStatusCode;
                processedType = maximumOriginalType;
                duplicatesExist = duplicatesMaximumsExist;
            }
            else if (valueType == 3)
            {
                double range = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedValue = range;
                processedType = GetRangeType(minimumOriginalType, range);
            }

            // set the status code.
            StatusCode statusCode = processedStatusCode;

            // set calculated if not returning actual time and the selected sample is not at
            // the request-direction interval start. For reverse reads (Part 11) the interval
            // start is the later timestamp, so compare against the interval timestamp
            // (GetTimestamp) rather than the chronological lower bound.
            if (!returnActualTime &&
                processedTimestamp != GetTimestamp(slice) &&
                (statusCode.AggregateBits & AggregateBits.Interpolated) == 0)
            {
                statusCode = statusCode.WithAggregateBits(
                    statusCode.AggregateBits | AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.WithAggregateBits(
                    statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            if (processedType.IsUnknown)
            {
                processedType = TypeInfo.Scalars.Double;
            }

            // convert back to original datatype.
            processedValue = processedValue.ConvertTo(processedType.BuiltInType);
            // create processed value.
            var value = new DataValue(
                processedValue,
                GetTimeBasedStatusCode(slice, values, statusCode));

            // zero value if status is bad.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value = value.WithWrappedValue(Variant.Null);
            }

            if (returnActualTime)
            {
                // calculate effective time if end bound is used.
                if (TimeFlowsBackward)
                {
                    if (processedTimestamp == slice.StartTime)
                    {
                        processedTimestamp = processedTimestamp.AddMilliseconds(+1);
                        value = value.WithStatus(value.StatusCode.WithAggregateBits(
                            value.StatusCode.AggregateBits | AggregateBits.Interpolated));
                    }
                }
                else if (processedTimestamp == slice.EndTime)
                {
                    processedTimestamp = processedTimestamp.AddMilliseconds(-1);
                    value = value.WithStatus(value.StatusCode.WithAggregateBits(
                        value.StatusCode.AggregateBits | AggregateBits.Interpolated));
                }

                return value
                    .WithSourceTimestamp(processedTimestamp)
                    .WithServerTimestamp(processedTimestamp);
            }
            DateTimeUtc sliceStamp = GetTimestamp(slice);

            return value
                .WithSourceTimestamp(sliceStamp)
                .WithServerTimestamp(sliceStamp);
        }

        /// <summary>
        /// Returns the data type of a Range/Range2 result: the source type
        /// (Part 13 Tables 62 and 67), or Double when the source is not a
        /// numeric type or the range does not fit into it.
        /// </summary>
        private static TypeInfo GetRangeType(TypeInfo sourceType, double range)
        {
            if (sourceType.IsUnknown ||
                sourceType.BuiltInType is < BuiltInType.SByte or > BuiltInType.Double)
            {
                return TypeInfo.Scalars.Double;
            }
            try
            {
                _ = new Variant(range).ConvertTo(sourceType.BuiltInType);
                return sourceType;
            }
            catch (Exception)
            {
                return TypeInfo.Scalars.Double;
            }
        }
    }
}
