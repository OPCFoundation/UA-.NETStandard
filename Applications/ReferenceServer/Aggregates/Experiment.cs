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
using Opc.Ua;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Calculates the value of an aggregate. 
    /// </summary>
    public class AggregateCalculator2
    {
        /// <summary>
        /// Initializes the calculation stream.
        /// </summary>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        public void Initialize(
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            AggregateConfiguration configuration)
        {
            m_startTime = startTime;
            m_endTime = endTime;
            m_configuration = configuration;
            m_processingInterval = processingInterval;
            m_timeFlowsBackward = (endTime < startTime);
            m_values = new LinkedList<DataValue>();
            m_lastRawTimestamp = (m_timeFlowsBackward) ? DateTime.MaxValue : DateTime.MinValue;

            TimeSlice slice = new TimeSlice();
            slice.StartTime = startTime;
            slice.EndTime = slice.StartTime.AddMilliseconds((m_timeFlowsBackward) ? -m_processingInterval : m_processingInterval);
            slice.EarlyBound = null;
            slice.LateBound = null;
            slice.Complete = false;
            m_nextSlice = slice;
        }

        /// <summary>
        /// Pushes the next raw value into the stream.
        /// </summary>
        /// <param name="value">The data value to append to the stream.</param>
        /// <returns>True if successful, false if the source timestamp has been superceeded by values already in the stream.</returns>
        public bool PushRawValue(DataValue value)
        {
            if (value == null)
            {
                return false;
            }

            if (CompareTimestamps(value.SourceTimestamp, m_lastRawTimestamp) < 0)
            {
                return false;
            }

            LinkedListNode<DataValue> node = m_values.AddLast(value);
            m_lastRawTimestamp = value.SourceTimestamp;

            if (IsGood(value))
            {
                if (CompareTimestamps(m_lastRawTimestamp, m_nextSlice.StartTime) <= 0)
                {
                    m_nextSlice.EarlyBound = node;
                }

                if (CompareTimestamps(m_lastRawTimestamp, m_nextSlice.EndTime) >= 0)
                {
                    m_nextSlice.LateBound = node;
                    m_nextSlice.Complete = true;
                }
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
            // do nothing if slice not complete and partial values not requested.
            if (!m_nextSlice.Complete)
            {
                if (CompareTimestamps(m_endTime, m_nextSlice.EndTime) > 0)
                {
                    if (!returnPartial)
                    {
                        return null;
                    }
                }
            }

            // check for end.
            if (CompareTimestamps(m_endTime, m_nextSlice.StartTime) <= 0)
            {
                return null;
            }

            // compute the value.
            DataValue value = ComputeValue(m_nextSlice);

            TimeSlice slice = new TimeSlice();
            slice.StartTime = m_nextSlice.EndTime;
            slice.EndTime = slice.StartTime.AddMilliseconds((m_timeFlowsBackward) ? -m_processingInterval : m_processingInterval);
            slice.EarlyBound = FindEarlyBound(slice.StartTime);
            slice.LateBound = FindLateBound(slice.EarlyBound, slice.EndTime);
            slice.Complete = slice.LateBound != null;

            m_nextSlice = slice;

            // remove all data prior to the early bound.
            if (slice.EarlyBound != null)
            {
                LinkedListNode<DataValue> ii = slice.EarlyBound.Previous;

                while (ii != null)
                {
                    LinkedListNode<DataValue> next = ii.Previous;
                    m_values.Remove(ii);
                    ii = next;
                }
            }

            return value;
        }

        /// <summary>
        /// Interpolates the value at the specified timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp to use,</param>
        /// <param name="start">The start position.</param>
        /// <returns>The interpolated value.</returns>
        private DataValue Interpolate(DateTime timestamp, LinkedListNode<DataValue> start)
        {
            if (start == null)
            {
                start = m_values.First;
            }

            if (start == null)
            {
                return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
            }

            LinkedListNode<DataValue> firstBound = null;
            LinkedListNode<DataValue> lastBound = null;
            bool firstBoundBad = false;
            bool lastBoundBad = false;

            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                int difference = CompareTimestamps(ii.Value.SourceTimestamp, timestamp);

                // check for an exact match.
                if (difference == 0)
                {
                    if (IsGood(ii.Value))
                    {
                        return ii.Value;
                    }
                }

                // find the first good value before the timestamp.
                if (difference <= 0)
                {
                    if (IsGood(ii.Value))
                    {
                        firstBound = ii;
                    }
                    else
                    {
                        firstBoundBad = true;
                    }
                }

                // find the first good value after the timestamp.
                if (difference >= 0)
                {
                    if (IsGood(ii.Value))
                    {
                        lastBound = ii;
                        break;
                    }
                    else
                    {
                        lastBoundBad = true;
                    }
                }
            }

            // check if first bound found.
            if (firstBound == null)
            {
                // can't extrapolate backwards in time.
                if (!m_timeFlowsBackward)
                {
                    return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
                }
            }

            // check if last bound found.
            if (lastBound == null)
            {
                // can't extrapolate backwards in time.
                if (m_timeFlowsBackward)
                {
                    return new DataValue(Variant.Null, StatusCodes.BadNoData, timestamp, timestamp);
                }
            }

            // use stepped interpolation/extrapolation if a bound is missing.
            if (!m_configuration.UseSlopedExtrapolation || lastBound == null || firstBound == null)
            {
                if (m_timeFlowsBackward)
                {
                    StatusCode statusCode = (lastBoundBad) ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good;
                    statusCode = statusCode.SetAggregateBits(AggregateBits.Interpolated);
                    return new DataValue(lastBound.Value.WrappedValue, statusCode, timestamp, timestamp);
                }
                else
                {
                    StatusCode statusCode = (firstBoundBad) ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good;
                    statusCode = statusCode.SetAggregateBits(AggregateBits.Interpolated);
                    return new DataValue(firstBound.Value.WrappedValue, statusCode, timestamp, timestamp);
                }
            }

            // calculate sloped interpolation.
            else
            {
                DataValue dataValue = new DataValue();
                dataValue.SourceTimestamp = timestamp;
                dataValue.ServerTimestamp = timestamp;

                try
                {
                    // convert to doubles.
                    TypeInfo sourceType = firstBound.Value.WrappedValue.TypeInfo;
                    double firstValue = (double)TypeInfo.Cast(firstBound.Value.Value, sourceType, BuiltInType.Double);
                    double lastValue = (double)TypeInfo.Cast(lastBound.Value.Value, lastBound.Value.WrappedValue.TypeInfo, BuiltInType.Double);

                    // do interpolation.
                    double range = (lastBound.Value.SourceTimestamp - firstBound.Value.SourceTimestamp).TotalMilliseconds;
                    double slope = (lastValue - firstValue) / range;
                    double doubleValue = slope * (timestamp - firstBound.Value.SourceTimestamp).TotalMilliseconds + firstValue;

                    // convert back to original type.
                    object value = TypeInfo.Cast(doubleValue, TypeInfo.Scalars.Double, sourceType.BuiltInType);
                    dataValue.WrappedValue = new Variant(value, sourceType);
                }
                catch (Exception)
                {
                    // handle data conversion error.
                    return new DataValue(Variant.Null, StatusCodes.BadTypeMismatch, timestamp, timestamp);
                }

                // set the aggregate bits as required.
                StatusCode statusCode = (firstBoundBad || lastBoundBad) ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good;
                dataValue.StatusCode = statusCode.SetAggregateBits(AggregateBits.Interpolated);
                return dataValue;
            }
        }

        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        /// <param name="slice">The time slice to use for the computation.</param>
        /// <returns>The value for the time slice.</returns>
        protected DataValue ComputeValue(TimeSlice slice)
        {
            return Interpolate(slice.StartTime, slice.EarlyBound);
        }

        /// <summary>
        /// Finds the early bound for the timestamp.
        /// </summary>
        /// <param name="timestamp">The timestamp to search.</param>
        /// <returns>The first good value that preceeds the timestamp.</returns>
        private LinkedListNode<DataValue> FindEarlyBound(DateTime timestamp)
        {
            for (LinkedListNode<DataValue> ii = m_values.Last; ii != null; ii = ii.Previous)
            {
                if (IsGood(ii.Value))
                {
                    if (CompareTimestamps(ii.Value.SourceTimestamp, timestamp) <= 0)
                    {
                        return ii;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the late bound for the timestamp.
        /// </summary>
        /// <param name="start">The starting point for the search.</param>
        /// <param name="timestamp">The timestamp to search.</param>
        /// <returns>The first good value that follows the timestamp.</returns>
        private LinkedListNode<DataValue> FindLateBound(LinkedListNode<DataValue> start, DateTime timestamp)
        {
            if (start == null)
            {
                start = m_values.First;
            }

            if (start == null)
            {
                return null;
            }

            for (LinkedListNode<DataValue> ii = start; ii != null; ii = ii.Next)
            {
                if (IsGood(ii.Value))
                {
                    if (CompareTimestamps(ii.Value.SourceTimestamp, timestamp) >= 0)
                    {
                        return ii;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Detremines the relative position of two timestamps in the stream.
        /// </summary>
        /// <param name="timestamp1">The first timestamp.</param>
        /// <param name="timestamp2">The second timestamp.</param>
        /// <returns>
        /// Returns less than zero if timestamp1 preceeds timestamp2
        /// Returns greater than zero if timestamp2 preceeds timestamp1
        /// Returns zero if timestamp1 equals timestamp2
        /// </returns>
        public int CompareTimestamps(DateTime timestamp1, DateTime timestamp2)
        {
            int difference = timestamp1.CompareTo(timestamp2);

            if (difference == 0)
            {
                return 0;
            }

            if (m_timeFlowsBackward)
            {
                return -difference;

            }

            return difference;
        }

        /// <summary>
        /// Checks if the value is good according to the configuration rules.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>True if the value is good.</returns>
        public bool IsGood(DataValue value)
        {
            if (value == null)
            {
                return false;
            }

            if (m_configuration.TreatUncertainAsBad)
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

        protected class TimeSlice
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public LinkedListNode<DataValue> EarlyBound;
            public LinkedListNode<DataValue> LateBound;
            public bool Complete { get; set; }
        }

        private TimeSlice m_nextSlice;
        private AggregateConfiguration m_configuration;
        private DateTime m_lastRawTimestamp;
        private double m_processingInterval;
        private bool m_timeFlowsBackward;
        private LinkedList<DataValue> m_values;
        private DateTime m_startTime;
        private DateTime m_endTime;
    }
}
