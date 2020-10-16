/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate. 
    /// </summary>
    public class AggregateCalculator : IAggregateCalculator
    {
        #region Constructors
        /// <summary>
        /// Creates a default aggregator.
        /// </summary>
        protected AggregateCalculator(NodeId aggregateId)
        {
            AggregateConfiguration configuration = new AggregateConfiguration();
            configuration.TreatUncertainAsBad = false;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = false;
            Initialize(aggregateId, DateTime.UtcNow, DateTime.MaxValue, 1000, false, configuration);
        }

        /// <summary>
        /// Initializes the calculation stream.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        public AggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            Initialize(aggregateId, startTime, endTime, processingInterval, stepped, configuration);
        }

        /// <summary>
        /// Initializes the calculation stream.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        protected void Initialize(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        {
            AggregateId = aggregateId;
            StartTime = startTime;
            EndTime = endTime;
            ProcessingInterval = processingInterval;
            Stepped = stepped;
            Configuration = configuration;
            TimeFlowsBackward = (endTime < startTime);

            if (processingInterval == 0)
            {
                if (endTime == DateTime.MinValue || startTime == DateTime.MinValue)
                {
                    throw new ArgumentException("Non-zero processingInterval required.", nameof(processingInterval));
                }

                ProcessingInterval = Math.Abs((endTime - startTime).TotalMilliseconds);
            }

            m_values = new LinkedList<DataValue>();
        }
        #endregion

        #region IAggregateCalculator Members
        /// <summary>
        /// The aggregate function applied by the calculator.
        /// </summary>
        public NodeId AggregateId { get; private set; }

        /// <summary>
        /// Queues a raw value for processing.
        /// </summary>
        /// <param name="value">The data value to process.</param>
        /// <returns>True if successful, false if the timestamp has been superceeded by values already in the stream.</returns>
        public bool QueueRawValue(DataValue value)
        {
            // ignore bad data.
            if (value == null)
            {
                return false;
            }

            // ignore placeholders in the stream.
            if (value.StatusCode.CodeBits == StatusCodes.BadNoData)
            {
                return true;
            }

            // check for start of data.
            if (m_startOfData == DateTime.MinValue)
            {
                m_startOfData = value.SourceTimestamp;
            }

            // update end of data.
            m_endOfData = value.SourceTimestamp;

            // ensure values are being queued in the right order.
            if (TimeFlowsBackward)
            {
                if (m_values.First != null && CompareTimestamps(value, m_values.First) > 0)
                {
                    return false;
                }
            }
            else
            {
                if (m_values.Last != null && CompareTimestamps(value, m_values.Last) < 0)
                {
                    return false;
                }
            }

            // ensure value list is always ordered from past to future.
            if (TimeFlowsBackward)
            {
                m_values.AddFirst(value);
            }
            else
            {
                m_values.AddLast(value);
            }

            return true;
        }

        /// <summary>
        /// Returns the next processed value.
        /// </summary>
        /// <param name="returnPartial">If true a partial interval should be processed.</param>
        /// <returns>The processed value. Null if nothing available and returnPartial is false.</returns>
        public DataValue GetProcessedValue(bool returnPartial)
        {
            // check if all done.
            if (Complete)
            {
                return null;
            }

            // update the slice.
            if (CurrentSlice == null)
            {
                CurrentSlice = CreateSlice(null);
            }
            else
            {
                UpdateSlice(CurrentSlice);
            }

            // check if a value can be produced.
            if (!CurrentSlice.Complete && !returnPartial)
            {
                return null;
            }

            // check if the slice extends beyond the range of available data.
            DateTime earlyTime = CurrentSlice.StartTime;
            DateTime lateTime = CurrentSlice.EndTime;

            if (CompareTimestamps(lateTime, m_values.First) < 0 || CompareTimestamps(earlyTime, m_values.Last) > 0)
            {
                CurrentSlice.OutOfDataRange = true;
            }

            Utils.Trace(1, "Computing {0:HH:mm:ss.fff}", CurrentSlice.StartTime);

            // compute the value.
            DataValue value = ComputeValue(CurrentSlice);

            // check if overlapping the start of data.
            if (SetPartialBit)
            {
                if (m_startOfData > earlyTime && m_startOfData < lateTime)
                {
                    value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Partial);
                }

                if (!UsingExtrapolation)
                {
                    if (m_endOfData >= earlyTime && m_endOfData < lateTime)
                    {
                        value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Partial);
                    }
                }
            }

            // force value to null if status code is bad.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value.WrappedValue = Variant.Null;
            }
            
            // delete uneeded data.
            if (TimeFlowsBackward)
            {
                if (CurrentSlice.LateBound != null)
                {
                    LinkedListNode<DataValue> ii = CurrentSlice.LateBound.Next;

                    while (ii != null)
                    {
                        LinkedListNode<DataValue> next = ii.Next;
                        m_values.Remove(ii);
                        ii = next;
                    }
                }
            }
            else
            {
                if (CurrentSlice.EarlyBound != null)
                {
                    LinkedListNode<DataValue> ii = CurrentSlice.EarlyBound.Previous;

                    if (CurrentSlice.SecondEarlyBound != null)
                    {
                        ii = CurrentSlice.SecondEarlyBound.Previous;
                    }

                    while (ii != null)
                    {
                        LinkedListNode<DataValue> next = ii.Previous;
                        m_values.Remove(ii);
                        ii = next;
                    }
                }
            }

            // check if more to be done.
            Complete = ((!TimeFlowsBackward && CurrentSlice.EndTime >= EndTime) || (TimeFlowsBackward && CurrentSlice.StartTime <= EndTime));

            if (Complete)
            {
                // check if overlapping the end of data.
                if (SetPartialBit && !UsingExtrapolation)
                {
                    if (m_endOfData >= earlyTime && m_endOfData < lateTime)
                    {
                        value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Partial);
                    }
                }
            }
            else
            {
                CurrentSlice = CreateSlice(CurrentSlice);
            }

            // return the processed value.
            return value;
        }

        /// <summary>
        /// Returns true if the specified time is later than the end of the current interval.
        /// </summary>
        /// <remarks>Return true if time flows forward and the time is later than the end time.</remarks>
        public bool HasEndTimePassed(DateTime currentTime)
        {
            if (CurrentSlice == null)
            {
                return false;
            }

            if (TimeFlowsBackward)
            {
                return CurrentSlice.EndTime >= currentTime;
            }

            return CurrentSlice.EndTime <= currentTime;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// The start time for the request. 
        /// </summary>
        protected DateTime StartTime { get; private set; }

        /// <summary>
        /// The end time for the request. 
        /// </summary>
        protected DateTime EndTime { get; private set; }

        /// <summary>
        /// The processing interval for the request.
        /// </summary>
        protected double ProcessingInterval { get; private set; }

        /// <summary>
        /// True if the data series requires stepped interpolation.
        /// </summary>
        protected bool Stepped { get; private set; }

        /// <summary>
        /// The configuration to use when processing.
        /// </summary>
        protected AggregateConfiguration Configuration { get; private set; }
        
        /// <summary>
        /// Whether to use the server timestamp for all processing.
        /// </summary>
        protected bool UseServerTimestamp { get; private set; } 

        /// <summary>
        /// True if data is being processed in reverse order.
        /// </summary>
        protected bool TimeFlowsBackward { get; private set; }

        /// <summary>
        /// Whether to use the server timestamp for all processing.
        /// </summary>
        protected TimeSlice CurrentSlice { get; private set; }

        /// <summary>
        /// True if all values required for the request have been received and processed
        /// </summary>
        protected bool Complete { get; private set; }

        /// <summary>
        /// True if the GetProcessedValue method should set the Partial bit when appropriate.
        /// </summary>
        protected bool SetPartialBit { get; set; }

        /// <summary>
        /// True if data is extrapolated after the end of data.
        /// </summary>
        protected bool UsingExtrapolation { get; set; }

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DataValue value1, DataValue value2)
        {
            if (value1 == null)
            {
                return (value2 == null)?0:-1;
            }

            if (value2 == null)
            {
                return +1;
            }

            if (UseServerTimestamp)
            {
                int result = value1.ServerTimestamp.CompareTo(value2.ServerTimestamp);

                if (result == 0)
                {
                    return value1.ServerPicoseconds.CompareTo(value2.ServerPicoseconds);
                }

                return result;
            }
            else
            {
                int result = value1.SourceTimestamp.CompareTo(value2.SourceTimestamp);

                if (result == 0)
                {
                    return value1.SourcePicoseconds.CompareTo(value2.SourcePicoseconds);
                }

                return result;
            }
        }

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DataValue value1, LinkedListNode<DataValue> value2)
        {
            if (value2 == null)
            {
                return (value1 == null)?0:+1;
            }

            return CompareTimestamps(value1, value2.Value);
        }

        /// <summary>
        /// Compares timestamps for two DataValues according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The first value to compare.</param>
        /// <param name="value2">The second value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(LinkedListNode<DataValue> value1, LinkedListNode<DataValue> value2)
        {
            if (value1 == null)
            {
                return (value2 == null)?0:-1;
            }

            if (value2 == null)
            {
                return +1;
            }

            return CompareTimestamps(value1.Value, value2.Value);
        }

        /// <summary>
        /// Compares timestamps for a timestamp to a DataValue according to the current UseServerTimestamp setting.
        /// </summary>
        /// <param name="value1">The timestamp to compare.</param>
        /// <param name="value2">The data value to compare.</param>
        /// <returns>Less than 0 if value1 is earlier than value2; 0 if they are equal; Greater than zero otherwise.</returns>
        protected int CompareTimestamps(DateTime value1, LinkedListNode<DataValue> value2)
        {
            if (value2 == null || value2.Value == null)
            {
                return +1;
            }

            if (UseServerTimestamp)
            {
                return value1.CompareTo(value2.Value.ServerTimestamp);
            }
            else
            {
                return value1.CompareTo(value2.Value.SourceTimestamp);
            }
        }

        /// <summary>
        /// Checks if the value is good according to the configuration rules.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>True if the value is good.</returns>
        protected bool IsGood(DataValue value)
        {
            if (value == null)
            {
                return false;
            }

            if (Configuration.TreatUncertainAsBad)
            {
                if (StatusCode.IsNotGood(value.StatusCode))
                {
                    return false;
                }
            }
            else
            {
                if (StatusCode.IsBad(value.StatusCode))
                {
                    return false;
                }
            }

            return true;
        }
 
        /// <summary>
        /// Stores information about a slice of data to be processed.
        /// </summary>
        protected class TimeSlice
        {
            /// <summary>
            /// The start time for the slice. 
            /// </summary>
            public DateTime StartTime { get; set; }

            /// <summary>
            /// The end time for the slice. 
            /// </summary>
            public DateTime EndTime { get; set; }

            /// <summary>
            /// True if the slice is a partial interval.
            /// </summary>
            public bool Partial { get; set; }

            /// <summary>
            /// True if all of the data required to process the slice has been collected.
            /// </summary>
            public bool Complete { get; set; }

            /// <summary>
            /// True if the slice includes times that are outside of the available dataset.
            /// </summary>
            public bool OutOfDataRange { get; set; }

            /// <summary>
            /// The first early bound for the slice.
            /// </summary>
            public LinkedListNode<DataValue> EarlyBound { get; set; }

            /// <summary>
            /// The second early bound for the slice (always earlier than the first).
            /// </summary>
            public LinkedListNode<DataValue> SecondEarlyBound { get; set; }

            /// <summary>
            /// The beginning of the slice.
            /// </summary>
            public LinkedListNode<DataValue> Begin { get; set; }

            /// <summary>
            /// The end of the slice.
            /// </summary>
            public LinkedListNode<DataValue> End { get; set; }

            /// <summary>
            /// The late bound for the slice.
            /// </summary>
            public LinkedListNode<DataValue> LateBound { get; set; }

            /// <summary>
            /// The last value which was processed.
            /// </summary>
            public LinkedListNode<DataValue> LastProcessedValue { get; set; }
        }

        /// <summary>
        /// Creates a new time slice to process.
        /// </summary>
        /// <param name="previousSlice">The previous processed slice.</param>
        /// <returns>The new time slice.</returns>
        protected TimeSlice CreateSlice(TimeSlice previousSlice)
        {
            TimeSlice slice = new TimeSlice();

            // ensure slice is oriented from past to future even if request is going backwards.
            if (TimeFlowsBackward)
            {
                if (previousSlice == null)
                {
                    slice.EndTime = StartTime;
                }
                else
                {
                    slice.EndTime = previousSlice.StartTime;
                }

                slice.StartTime = slice.EndTime.AddMilliseconds(-ProcessingInterval);

                // check for end of request.
                if (slice.StartTime < EndTime)
                {
                    slice.StartTime = EndTime;
                    slice.Partial = true;
                }
            }
            else
            {
                if (previousSlice == null)
                {
                    slice.StartTime = StartTime;
                }
                else
                {
                    slice.StartTime = previousSlice.EndTime;
                }

                slice.EndTime = slice.StartTime.AddMilliseconds(ProcessingInterval);

                // check for end of request.
                if (slice.EndTime > EndTime)
                {
                    slice.EndTime = EndTime;
                    slice.Partial = true;
                }
            }

            // update the slice with current data.
            UpdateSlice(slice);
            return slice;
        }

        /// <summary>
        /// Creates a new time slice to process.
        /// </summary>
        /// <param name="slice">The slice to update.</param>
        /// <returns>True if the slice is complete.</returns>
        protected bool UpdateSlice(TimeSlice slice)
        {
            // check if nothing to do.
            if (m_values.First == null)
            {
                return slice.Complete;
            }

            // restart processing from where it left off.
            LinkedListNode<DataValue> start = m_values.First;

            if (!TimeFlowsBackward && slice.LastProcessedValue != null)
            {
                start = slice.LastProcessedValue.Next; 
            }

            // reset the begin bound each time we go through the values.
            if (TimeFlowsBackward)
            {
                slice.Begin = null;
            }
            
            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                if (TimeFlowsBackward)
                {
                    // check if before the beginning of the slice.
                    if (CompareTimestamps(slice.StartTime, ii) >= 0)
                    {
                        if (IsGood(ii.Value))
                        {
                            slice.SecondEarlyBound = slice.EarlyBound;
                            slice.EarlyBound = ii;
                        }

                        continue;
                    }

                    // check if after the end if the slice.
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        if (IsGood(ii.Value))
                        {
                            slice.LateBound = ii;
                            break;
                        }

                        continue;
                    }

                    // save first value in the slice.
                    if (slice.End == null)
                    {
                        slice.End = ii;
                    }

                    // save end of slice.
                    if (slice.Begin == null)
                    {
                        slice.Begin = ii;
                        slice.LastProcessedValue = ii;
                    }
                }
                else
                {
                    // check if before the beginning of the slice.
                    if (CompareTimestamps(slice.StartTime, ii) > 0)
                    {
                        if (IsGood(ii.Value))
                        {
                            slice.SecondEarlyBound = slice.EarlyBound;
                            slice.EarlyBound = ii;
                            slice.LastProcessedValue = ii;
                        }

                        continue;
                    }

                    // check if after the end if the slice.
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        if (IsGood(ii.Value))
                        {
                            slice.LateBound = ii;
                            slice.LastProcessedValue = ii;
                            break;
                        }

                        continue;
                    }

                    // save first value in the slice.
                    if (slice.Begin == null)
                    {
                        slice.Begin = ii;
                    }

                    // save end of slice.
                    slice.End = ii;
                    slice.LastProcessedValue = ii;
                }
            }

            // check if no more data needs to be collected.
            LinkedListNode<DataValue> requiredBound = null;

            if (TimeFlowsBackward)
            {
                // only need second early bound if using sloped extrapolation and there is no late bound.
                if (Configuration.UseSlopedExtrapolation && slice.LateBound == null)
                {
                    requiredBound = slice.SecondEarlyBound;
                }
                else
                {
                    requiredBound = slice.EarlyBound;
                }
            }
            else
            {
                requiredBound = slice.LateBound;
            }

            // all done if required bound exists.
            if (requiredBound != null)
            {
                slice.Complete = true;
            }

            return slice.Complete;
        }

        /// <summary>
        /// Calculates the value for the timeslice.
        /// </summary>
        /// <param name="slice">The slice to process.</param>
        /// <returns>The processed value.</returns>
        protected virtual DataValue ComputeValue(TimeSlice slice)
        {
            return Interpolate(slice);
        }

        /// <summary>
        /// Calculate the interpolate aggregate for the timeslice.
        /// </summary>
        protected DataValue Interpolate(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return Interpolate(slice.EndTime, slice);
            }
            else
            {
                return Interpolate(slice.StartTime, slice);
            }
        }

        /// <summary>
        /// Return a value indicating there is no data in the time slice.
        /// </summary>
        protected DataValue GetNoDataValue(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return GetNoDataValue(slice.EndTime);
            }
            else
            {
                return GetNoDataValue(slice.StartTime);
            }
        }

        /// <summary>
        /// Returns the timestamp to use for the slice value.
        /// </summary>
        protected DateTime GetTimestamp(TimeSlice slice)
        {
            if (TimeFlowsBackward)
            {
                return slice.EndTime;
            }
            else
            {
                return slice.StartTime;
            }
        }

        /// <summary>
        /// Return a value indicating there is no data in the time slice.
        /// </summary>
        protected DataValue GetNoDataValue(DateTime timestamp)
        {
            return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
        }

        /// <summary>
        /// Interpolates a value at the timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="reference">The timeslice that contains the timestamp.</param>
        /// <returns>The interpolated value.</returns>
        protected DataValue Interpolate(DateTime timestamp, TimeSlice reference)
        {
            TimeSlice slice = new TimeSlice();
            slice.StartTime = timestamp;
            slice.EndTime = timestamp;
            UpdateSlice(slice);

            // check for value at the timestamp.
            if (slice.Begin != null)
            {
                if (IsGood(slice.Begin.Value))
                {
                    return slice.Begin.Value;
                }                
            }

            DataValue dataValue = null;
            bool stepped = Stepped;

            // check if the required bounds are available.
            if (!Stepped)
            {
                // check if sloped interpolation is possible.
                if (slice.EarlyBound != null && slice.LateBound != null)
                {
                    dataValue = SlopedInterpolate(timestamp, slice.EarlyBound.Value, slice.LateBound.Value);
                    
                    if (!Object.ReferenceEquals(slice.EarlyBound.Next, slice.LateBound))
                    {
                        dataValue.StatusCode = dataValue.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    return dataValue;
                }

                // check if extrapolation is possible.
                if (slice.EarlyBound != null)
                {
                    if (Configuration.UseSlopedExtrapolation)
                    {
                        if (slice.EarlyBound != null && slice.SecondEarlyBound != null)
                        {
                            UsingExtrapolation = true;
                            dataValue = SlopedInterpolate(timestamp, slice.SecondEarlyBound.Value, slice.EarlyBound.Value);
                            dataValue.StatusCode = dataValue.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                            return dataValue;
                        }
                    }

                    // do stepped extrapolation.
                    stepped = true;
                }
            }

            // do stepped interpolation.
            if (stepped)
            {
                if (slice.EarlyBound != null)
                {
                    dataValue = SteppedInterpolate(timestamp, slice.EarlyBound.Value);

                    if (slice.EarlyBound.Next == null || CompareTimestamps(timestamp, slice.EarlyBound.Next) >= 0)
                    {
                        UsingExtrapolation = true;
                        dataValue.StatusCode = dataValue.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    return dataValue;
                }
            }

            // no data found.
            return GetNoDataValue(timestamp);
        }

        /// <summary>
        /// Calculate the value at the timestamp using slopped interpolation.
        /// </summary>
        public static DataValue SteppedInterpolate(DateTime timestamp, DataValue earlyBound)
        {
            // can't interpolate if no start bound.
            if (StatusCode.IsBad(earlyBound.StatusCode))
            {
                return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
            }

            DataValue dataValue = new DataValue();
            dataValue.WrappedValue = earlyBound.WrappedValue;
            dataValue.SourceTimestamp = timestamp;
            dataValue.ServerTimestamp = timestamp;
            dataValue.StatusCode = StatusCodes.Good;

            // update status code.
            if (StatusCode.IsBad(earlyBound.StatusCode))
            {
                dataValue.StatusCode = StatusCodes.BadNoData;
            }

            // update status code.
            if (StatusCode.IsNotGood(earlyBound.StatusCode))
            {
                dataValue.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            dataValue.StatusCode = dataValue.StatusCode.SetAggregateBits(AggregateBits.Interpolated);
            return dataValue;
        }

        /// <summary>
        /// Calculate the value at the timestamp using slopped interpolation.
        /// </summary>
        public static DataValue SlopedInterpolate(DateTime timestamp, DataValue earlyBound, DataValue lateBound)
        {
            try
            {
                // can't interpolate if no start bound.
                if (StatusCode.IsBad(earlyBound.StatusCode))
                {
                    return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
                }

                // revert to stepped if no end bound.
                if (StatusCode.IsBad(lateBound.StatusCode))
                {
                    DataValue dataValue2 = SteppedInterpolate(timestamp, earlyBound);

                    if (StatusCode.IsNotBad(dataValue2.StatusCode))
                    {
                        dataValue2.StatusCode = dataValue2.StatusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
                    }

                    return dataValue2;
                }

                // convert to doubles.
                double earlyValue = CastToDouble(earlyBound);
                double lateValue = CastToDouble(lateBound);

                // do interpolation.
                double range = (lateBound.SourceTimestamp - earlyBound.SourceTimestamp).TotalMilliseconds;
                double slope = (lateValue - earlyValue) / range;
                double calculatedValue = slope * (timestamp - earlyBound.SourceTimestamp).TotalMilliseconds + earlyValue;

                // convert back to original type.
                DataValue dataValue = new DataValue();
                dataValue.WrappedValue = CastToOriginalType(calculatedValue, earlyBound);
                dataValue.SourceTimestamp = timestamp;
                dataValue.ServerTimestamp = timestamp;
                dataValue.StatusCode = StatusCodes.Good;

                // update status code.
                if (StatusCode.IsNotGood(earlyBound.StatusCode) || StatusCode.IsNotGood(lateBound.StatusCode))
                {
                    dataValue.StatusCode = StatusCodes.UncertainDataSubNormal;
                }

                dataValue.StatusCode = dataValue.StatusCode.SetAggregateBits(AggregateBits.Interpolated);

                return dataValue;
            }

            // exception occurs on data conversion errors.
            catch (Exception)
            {
                return new DataValue(Variant.Null, StatusCodes.BadTypeMismatch, timestamp, timestamp);
            }
        }

        /// <summary>
        /// Converts the value to a double for use in calculations (throws exceptions if conversion fails).
        /// </summary>
        protected static double CastToDouble(DataValue value)
        {
            return (double)TypeInfo.Cast(value.Value, value.WrappedValue.TypeInfo, BuiltInType.Double);
        }

        /// <summary>
        /// Converts the value back to its original type (throws exceptions if conversion fails).
        /// </summary>
        protected static Variant CastToOriginalType(double value, DataValue original)
        {
            object castValue = TypeInfo.Cast(value, TypeInfo.Scalars.Double, original.WrappedValue.TypeInfo.BuiltInType);
            return new Variant(castValue, original.WrappedValue.TypeInfo);
        }

        /// <summary>
        /// Returns the simple bound for the timestamp.
        /// </summary>
        protected DataValue GetSimpleBound(DateTime timestamp, TimeSlice slice)
        {
            // choose the start point 
            LinkedListNode<DataValue> start = slice.EarlyBound;

            if (start == null)
            {
                start = m_values.First;
            }

            // look for a raw value at or immediately before the timestamp.
            LinkedListNode<DataValue> startBound = start;

            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                // check for an exact match.
                if (CompareTimestamps(timestamp, ii) == 0)
                {
                    return new DataValue(ii.Value);
                }

                // looking for an end bound.
                if (CompareTimestamps(timestamp, ii) < 0)
                {
                    // only can find an end bound.
                    if (ii.Previous == null)
                    {
                        return GetNoDataValue(timestamp);
                    }

                    startBound = ii.Previous;
                    break;
                }

                // update start bound.
                startBound = ii;
            }

            // check if no data found or if start bound is bad..
            if (startBound == null || !IsGood(startBound.Value))
            {
                return GetNoDataValue(timestamp);
            }

            // look for an end bound.
            bool revertToStepped = false;
            LinkedListNode<DataValue> endBound = startBound.Next;
            
            if (!Stepped)
            {
                if (endBound != null)
                {
                    // do sloped interpolation if two good bounds exist.
                    if (IsGood(endBound.Value))
                    {
                        return SlopedInterpolate(timestamp, startBound.Value, endBound.Value);
                    }
                }

                // have to use stepped because end bound is not good.
                revertToStepped = true;
            }

            // check if end of data.
            if (startBound.Next == null)
            {
                return GetNoDataValue(timestamp);
            }

            // do stepped interpolation for all other cases.
            DataValue value = SteppedInterpolate(timestamp, startBound.Value);

            // need to make it uncertain if interpolation was required but not used.
            if (StatusCode.IsGood(value.StatusCode) && revertToStepped)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
                value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Interpolated);
            }

            return value;
        }

        /// <summary>
        /// Returns the values in the list with simple bounds.
        /// </summary>
        protected List<DataValue> GetValuesWithSimpleBounds(TimeSlice slice)
        {
            // check if slice is beyond end of available data.
            if (CompareTimestamps(slice.StartTime, m_values.Last) > 0 || CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            List<DataValue> values = new List<DataValue>();

            // add the start point.
            DataValue startBound = GetSimpleBound(slice.StartTime, slice);

            if (startBound != null)
            {
                values.Add(startBound);
            }

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (CompareTimestamps(slice.EndTime, ii) <= 0)
                {
                    break;
                }

                if (CompareTimestamps(slice.StartTime, ii) < 0)
                {
                    values.Add(ii.Value);
                }
            }

            // add the end point.
            DataValue endBound = GetSimpleBound(slice.EndTime, slice);

            if (endBound != null)
            {
                values.Add(endBound);
            }

            return values;
        }

        /// <summary>
        /// Returns the values between the start time and the end time for the slice.
        /// </summary>
        protected List<DataValue> GetValues(TimeSlice slice)
        {
            // check if slice is beyond end of available data.
            if (CompareTimestamps(slice.StartTime, m_values.Last) > 0 || CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            List<DataValue> values = new List<DataValue>();

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (TimeFlowsBackward)
                {
                    if (CompareTimestamps(slice.EndTime, ii) < 0)
                    {
                        break;
                    }

                    if (CompareTimestamps(slice.StartTime, ii) < 0)
                    {
                        values.Add(ii.Value);
                    }
                }
                else
                {
                    if (CompareTimestamps(slice.EndTime, ii) <= 0)
                    {
                        break;
                    }

                    if (CompareTimestamps(slice.StartTime, ii) <= 0)
                    {
                        values.Add(ii.Value);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// Returns the values in the list with interpolated bounds.
        /// </summary>
        protected List<DataValue> GetValuesWithInterpolatedBounds(TimeSlice slice)
        {
            // check if slice is before the available data.
            if (CompareTimestamps(slice.EndTime, m_values.First) < 0)
            {
                return null;
            }

            List<DataValue> values = new List<DataValue>();

            // add the start point.
            DataValue startBound = Interpolate(slice.StartTime, slice);

            if (startBound != null)
            {
                values.Add(startBound);
            }

            // initialize slice from value list.
            for (LinkedListNode<DataValue> ii = slice.Begin; ii != null; ii = ii.Next)
            {
                if (CompareTimestamps(slice.EndTime, ii) <= 0)
                {
                    break;
                }

                if (CompareTimestamps(slice.StartTime, ii) < 0)
                {
                    values.Add(ii.Value);
                }
            }

            // add the end point.
            DataValue endBound = Interpolate(slice.EndTime, slice);

            if (endBound != null)
            {
                values.Add(endBound);
            }

            return values;
        }
        
        /// <summary>
        /// A subset of a slice bounded by two raw data points.
        /// </summary>
        protected class SubRegion
        {
            /// <summary>
            /// The value at the start of the region.
            /// </summary>
            public double StartValue { get; set; }

            /// <summary>
            /// The value at the end of the region.
            /// </summary>
            public double EndValue { get; set; }

            /// <summary>
            /// The timestamp at the start of the region.
            /// </summary>
            public DateTime StartTime;

            /// <summary>
            /// The length of the region.
            /// </summary>
            public double Duration { get; set; }

            /// <summary>
            /// The status for the region.
            /// </summary>
            public StatusCode StatusCode;

            /// <summary>
            /// The data point at the start of the region.
            /// </summary>
            public DataValue DataPoint;
        }

        /// <summary>
        /// Returns the values in the list with simple bounds.
        /// </summary>
        protected List<SubRegion> GetRegionsInValueSet(List<DataValue> values, bool ignoreBadData, bool useSteppedCalculations)
        {
            // nothing to do if no data.
            if (values == null)
            {
                return null;
            }

            SubRegion currentRegion = null;
            List<SubRegion> regions = new List<SubRegion>();

            for (int ii = 0; ii < values.Count; ii++)
            {
                double currentValue = 0;
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // convert to doubles to facilitate numeric calculations.
                if (StatusCode.IsNotBad(currentStatus))
                {
                    try
                    {
                        currentValue = CastToDouble(values[ii]);
                    }
                    catch (Exception)
                    {
                        currentStatus = StatusCodes.BadTypeMismatch;
                    }
                }
                else
                {
                    // use the previous value if end of region is bad.
                    if (currentRegion != null)
                    {
                        currentValue = currentRegion.StartValue;
                    }
                }

                // some aggregates ignore bad data so remove them from the set.
                if (ignoreBadData)
                {
                    // always keep the first region.
                    if (currentRegion != null)
                    {
                        if (!IsGood(values[ii]))
                        {
                            // set the status to sub normal if bad end data ignored.
                            if (StatusCode.IsNotBad(currentRegion.StatusCode))
                            {
                                currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                            }

                            // skip everything but the endpoint.
                            if (ii < values.Count - 1)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (!useSteppedCalculations && StatusCode.IsNotGood(values[ii].StatusCode))
                            {
                                currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                            }
                        }
                    }
                }

                if (currentRegion != null)
                {
                    // if using stepped calculations the end value is not used.
                    if (useSteppedCalculations)
                    {
                        currentRegion.EndValue = currentRegion.StartValue;
                    }

                    // using interpolated calculations means the end affects the status of the current region.
                    else
                    {
                        if (IsGood(values[ii]))
                        {
                            // handle case with uncertain end point.
                            if (StatusCode.IsNotGood(values[ii].StatusCode) && StatusCode.IsNotBad(currentRegion.StatusCode))
                            {
                                currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                            }

                            currentRegion.EndValue = currentValue;
                        }
                        else
                        {
                            if (StatusCode.IsNotBad(currentRegion.StatusCode))
                            {
                                currentRegion.StatusCode = StatusCodes.UncertainDataSubNormal;
                            }

                            if (ignoreBadData && StatusCode.IsNotBad(currentStatus))
                            {
                                currentRegion.EndValue = currentValue;
                            }
                        }
                    }

                    // if at end of data then duration is 1 tick.
                    // must be end of data if start of region is good yet end bound is bad.
                    if (!ignoreBadData && currentRegion != null && IsGood(currentRegion.DataPoint) && currentStatus == StatusCodes.BadNoData && ii == values.Count - 1)
                    {
                        currentRegion.Duration = 1;
                    }

                    // calculate region span.
                    else
                    {
                        // set uncertain status to bad if treat uncertain as bad is true.
                        if (StatusCode.IsUncertain(currentStatus) && !IsGood(values[ii]))
                        {
                            currentStatus = StatusCodes.BadNoData;
                        }

                        currentRegion.Duration = (currentTime - currentRegion.StartTime).TotalMilliseconds;
                    }
                     
                    regions.Add(currentRegion);
                }

                // start a new region.
                currentRegion = new SubRegion();
                currentRegion.StartValue = currentValue;
                currentRegion.EndValue = currentValue;
                currentRegion.StartTime = currentTime;
                currentRegion.StatusCode = currentStatus;
                currentRegion.DataPoint = values[ii];
            }

            return regions;
        }

        /// <summary>
        /// Calculates the value based status code for the slice 
        /// </summary>
        protected StatusCode GetValueBasedStatusCode(TimeSlice slice, List<DataValue> values, StatusCode statusCode)
        {
            // compute the total good/bad/uncertain.
            double badCount = 0;
            double goodCount = 0;
            double totalCount = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                totalCount++;

                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    badCount++;
                    continue;
                }

                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    goodCount++;
                }
            }

            // default to good.
            statusCode = statusCode.SetCodeBits(StatusCodes.Good);

            // uncertain if the good duration is less than the configured threshold.
            if ((goodCount / totalCount) * 100 < Configuration.PercentDataGood)
            {
                statusCode = statusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
            }

            // bad if the bad duration is greater than or equal to the configured threshold.
            if ((badCount / totalCount) * 100 >= Configuration.PercentDataBad)
            {
                statusCode = StatusCodes.Bad;
            }

            return statusCode;
        }

        /// <summary>
        /// Calculates the status code for the slice 
        /// </summary>
        protected StatusCode GetTimeBasedStatusCode(TimeSlice slice, List<DataValue> values, StatusCode defaultCode)
        {
            // get the regions in the slice.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, Stepped);

            if (regions == null || regions.Count == 0)
            {
                return StatusCodes.BadNoData;
            }

            return GetTimeBasedStatusCode(regions, defaultCode);
        }

        /// <summary>
        /// Calculates the status code for the slice 
        /// </summary>
        protected StatusCode GetTimeBasedStatusCode(List<SubRegion> regions, StatusCode statusCode)
        {
            // check for empty set.
            if (regions == null || regions.Count == 0)
            {
                return StatusCodes.BadNoData;
            }

            // compute the total good/bad/uncertain.
            double badDuration = 0;
            double goodDuration = 0;
            double totalDuration = 0;

            foreach (SubRegion region in regions)
            {
                totalDuration += region.Duration;

                if (StatusCode.IsBad(region.StatusCode))
                {
                    badDuration += region.Duration;
                    continue;
                }

                if (StatusCode.IsGood(region.StatusCode))
                {
                    goodDuration += region.Duration;
                }
            }

            // default to good.
            statusCode = statusCode.SetCodeBits(StatusCodes.Good);

            // uncertain if the good duration is less than the configured threshold.
            if ((goodDuration/totalDuration)*100 < Configuration.PercentDataGood)
            {
                statusCode = statusCode.SetCodeBits(StatusCodes.UncertainDataSubNormal);
            }

            // bad if the bad duration is greater than or equal to the configured threshold.
            if ((badDuration/totalDuration)*100 >= Configuration.PercentDataBad)
            {
                statusCode = StatusCodes.Bad;
            }

            // always calculated.
            return statusCode;
        }
        #endregion

        #region Private Fields
        private LinkedList<DataValue> m_values;
        private DateTime m_startOfData;
        private DateTime m_endOfData;
        #endregion
    }
}
