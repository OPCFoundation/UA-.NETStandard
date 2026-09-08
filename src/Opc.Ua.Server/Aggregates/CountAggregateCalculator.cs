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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class CountAggregateCalculator : AggregateCalculator
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
        public CountAggregateCalculator(
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
        /// Calculates AnnotationCount values for the requested time domain.
        /// </summary>
        /// <param name="annotationTimestamps">
        /// Annotation timestamps in any order.
        /// </param>
        /// <param name="startTime">The start of the requested domain.</param>
        /// <param name="endTime">The end of the requested domain.</param>
        /// <param name="processingInterval">
        /// The interval in milliseconds. Zero requests one result over the
        /// complete domain.
        /// </param>
        /// <param name="outputCap">Maximum number of returned values.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The calculated AnnotationCount values.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="annotationTimestamps"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="outputCap"/> is not positive.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The interval is invalid or the output cap is exceeded.
        /// </exception>
        public static ArrayOf<DataValue> CalculateAnnotationCounts(
            ArrayOf<DateTimeUtc> annotationTimestamps,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            double processingInterval,
            int outputCap,
            CancellationToken cancellationToken)
        {
            if (annotationTimestamps.IsNull)
            {
                throw new ArgumentNullException(nameof(annotationTimestamps));
            }
            if (outputCap <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputCap),
                    outputCap,
                    "The output cap must be positive.");
            }
            if (processingInterval < 0 ||
                double.IsNaN(processingInterval) ||
                double.IsInfinity(processingInterval))
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs);
            }
            if (startTime == endTime)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument);
            }
            long intervalTicks = processingInterval == 0
                ? 0
                : GetAnnotationIntervalTicks(processingInterval);
            if (processingInterval > 0)
            {
                long spanTicks = Math.Abs(
                    endTime.ToDateTime().Ticks -
                    startTime.ToDateTime().Ticks);
                long intervalCount = spanTicks / intervalTicks;
                if (spanTicks % intervalTicks != 0)
                {
                    intervalCount++;
                }
                if (intervalCount > outputCap)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sorted = new DateTimeUtc[annotationTimestamps.Count];
            for (int i = 0; i < sorted.Length; i++)
            {
                sorted[i] = annotationTimestamps[i];
            }
            Array.Sort(sorted);

            bool forward = startTime < endTime;
            var values = new List<DataValue>();
            int index = forward ? 0 : sorted.Length - 1;
            DateTimeUtc cursor = startTime;
            while (forward ? cursor < endTime : cursor > endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (values.Count >= outputCap)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations);
                }

                long remainingTicks = Math.Abs(
                    endTime.ToDateTime().Ticks -
                    cursor.ToDateTime().Ticks);
                DateTimeUtc next =
                    processingInterval == 0 ||
                    intervalTicks >= remainingTicks
                    ? endTime
                    : AddAnnotationInterval(
                        cursor,
                        forward ? intervalTicks : -intervalTicks);

                int count = forward
                    ? CountForwardAnnotations(
                        sorted,
                        ref index,
                        cursor,
                        next,
                        cancellationToken)
                    : CountReverseAnnotations(
                        sorted,
                        ref index,
                        cursor,
                        next,
                        cancellationToken);
                values.Add(CreateAnnotationCountValue(count, cursor));
                cursor = next;
            }
            return values.ToArrayOf();
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
                case Objects.AggregateFunction_Count:
                    return ComputeCount(slice);
                case Objects.AggregateFunction_AnnotationCount:
                    return ComputeAnnotationCount(slice);
                case Objects.AggregateFunction_DurationInStateZero:
                    return ComputeDurationInState(slice, false);
                case Objects.AggregateFunction_DurationInStateNonZero:
                    return ComputeDurationInState(slice, true);
                case Objects.AggregateFunction_NumberOfTransitions:
                    return ComputeNumberOfTransitions(slice);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // count the values.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    count++;
                }
            }

            // set the timestamp and status.
            var value = new DataValue(
                Variant.From(count),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));
            value = value.WithStatus(GetValueBasedStatusCode(slice, values, value.StatusCode));

            if (!StatusCode.IsBad(value.StatusCode))
            {
                // set aggregate bits fon non Bad values
                value = value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));
            }
            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the AnnotationCount aggregate for the timeslice.
        /// </summary>
        /// <remarks>
        /// Part 13 v1.05.07 §5.4.3.20 defines AnnotationCount as the number of <c>Annotation</c>s in
        /// the interval, which live on a separate annotation stream that this calculator is not fed.
        /// The authoritative AnnotationCount computation for history reads is performed by
        /// <c>HistorianDispatcher</c> using <c>IHistorianAnnotationProvider</c>; this method counts
        /// whatever values it is given and is retained only for direct/standalone calculator use.
        /// </remarks>
        protected DataValue ComputeAnnotationCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // count the values.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                count++;
            }

            // set the timestamp and status.
            var value = new DataValue(
                Variant.From(count),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));

            // return result.
            return value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));
        }

        /// <summary>
        /// Calculates the DurationInStateZero and DurationInStateNonZero aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeDurationInState(TimeSlice slice, bool isNonZero)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion>? regions = GetRegionsInValueSet(values, false, true);

            double duration = 0;

            for (int ii = 0; ii < regions!.Count; ii++)
            {
                if (StatusCode.IsNotGood(regions[ii].StatusCode))
                {
                    continue;
                }

                if (isNonZero)
                {
                    if (regions[ii].StartValue != 0)
                    {
                        duration += regions[ii].Duration;
                    }
                }
                else if (regions[ii].StartValue == 0)
                {
                    duration += regions[ii].Duration;
                }
            }

            // set the timestamp and status.
            var value = new DataValue(
                Variant.From(duration),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));
            value = value.WithStatus(GetTimeBasedStatusCode(regions, value.StatusCode));
            value = value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeNumberOfTransitions(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // The first non-Bad value is a transition when no previous non-Bad value exists.
            LinkedListNode<DataValue>? previousValue = slice.NonBadEarlyBound;
            bool hasLastValue = previousValue != null;
            Variant lastValue = previousValue != null
                ? previousValue.Value.WrappedValue
                : Variant.Null;

            // count the transitions.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    continue;
                }

                Variant nextValue = values[ii].WrappedValue;
                if (!hasLastValue || lastValue != nextValue)
                {
                    count++;
                }

                hasLastValue = true;
                lastValue = nextValue;
            }

            // set the timestamp and status.
            var value = new DataValue(
                Variant.From(count),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));
            value = value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));
            value = value.WithStatus(GetValueBasedStatusCode(slice, values, value.StatusCode));

            // return result.
            return value;
        }

        private static DateTimeUtc AddAnnotationInterval(
            DateTimeUtc timestamp,
            long ticks)
        {
            DateTimeUtc next;
            try
            {
                long nextTicks = checked(
                    timestamp.ToDateTime().Ticks + ticks);
                next = new DateTimeUtc(
                    new DateTime(nextTicks, DateTimeKind.Utc));
            }
            catch (Exception exception) when (
                exception is ArgumentOutOfRangeException or
                OverflowException)
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs,
                    "The annotation-count interval does not produce a valid timestamp.",
                    exception);
            }
            if (next == timestamp)
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs,
                    "The annotation-count interval does not advance.");
            }
            return next;
        }

        private static long GetAnnotationIntervalTicks(
            double processingInterval)
        {
            double ticks = processingInterval *
                TimeSpan.TicksPerMillisecond;
            if (double.IsInfinity(ticks) ||
                ticks < 1 ||
                ticks >= 9_223_372_036_854_775_808d)
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs);
            }
            try
            {
                return checked((long)ticks);
            }
            catch (OverflowException exception)
            {
                throw new ServiceResultException(
                    StatusCodes.BadAggregateInvalidInputs,
                    "The annotation-count interval exceeds the supported range.",
                    exception);
            }
        }

        private static int CountForwardAnnotations(
            DateTimeUtc[] timestamps,
            ref int index,
            DateTimeUtc lowerInclusive,
            DateTimeUtc upperExclusive,
            CancellationToken cancellationToken)
        {
            while (index < timestamps.Length &&
                timestamps[index] < lowerInclusive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
            }
            int start = index;
            while (index < timestamps.Length &&
                timestamps[index] < upperExclusive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
            }
            return index - start;
        }

        private static int CountReverseAnnotations(
            DateTimeUtc[] timestamps,
            ref int index,
            DateTimeUtc upperInclusive,
            DateTimeUtc lowerExclusive,
            CancellationToken cancellationToken)
        {
            while (index >= 0 && timestamps[index] > upperInclusive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index--;
            }
            int start = index;
            while (index >= 0 && timestamps[index] > lowerExclusive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index--;
            }
            return start - index;
        }

        private static DataValue CreateAnnotationCountValue(
            int count,
            DateTimeUtc timestamp)
        {
            var value = new DataValue(
                Variant.From(count),
                StatusCodes.Good,
                timestamp,
                timestamp);
            return value.WithStatus(value.StatusCode.WithAggregateBits(
                AggregateBits.Calculated));
        }
    }
}
