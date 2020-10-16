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
    public class CountAggregateCalculator : AggregateCalculator
    {
        #region Constructors
        /// <summary>
        /// Initializes the aggregate calculator.
        /// </summary>
        /// <param name="aggregateId">The aggregate function to apply.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="processingInterval">The processing interval.</param>
        /// <param name="stepped">Whether to use stepped interpolation.</param>
        /// <param name="configuration">The aggregate configuration.</param>
        public CountAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        : 
            base(aggregateId, startTime, endTime, processingInterval, stepped, configuration)
        {
            SetPartialBit = true;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Computes the value for the timeslice.
        /// </summary>
        protected override DataValue ComputeValue(TimeSlice slice)
        {
            uint? id = AggregateId.Identifier as uint?;

            if (id != null)
            {
                switch (id.Value)
                {
                    case Objects.AggregateFunction_Count:
                    {
                        return ComputeCount(slice);
                    }

                    case Objects.AggregateFunction_AnnotationCount:
                    {
                        return ComputeAnnotationCount(slice);
                    }

                    case Objects.AggregateFunction_DurationInStateZero:
                    {
                        return ComputeDurationInState(slice, false);
                    }

                    case Objects.AggregateFunction_DurationInStateNonZero:
                    {
                        return ComputeDurationInState(slice, true);
                    }

                    case Objects.AggregateFunction_NumberOfTransitions:
                    {
                        return ComputeNumberOfTransitions(slice);
                    }
                }
            }

            return base.ComputeValue(slice);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

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
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(count, TypeInfo.Scalars.Int32);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);           
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the AnnotationCount aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeAnnotationCount(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

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
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(count, TypeInfo.Scalars.Int32);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the DurationInStateZero and DurationInStateNonZero aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeDurationInState(TimeSlice slice, bool isNonZero)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            double duration = 0;

            for (int ii = 0; ii < regions.Count; ii++)
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
                else
                {
                    if (regions[ii].StartValue == 0)
                    {
                        duration += regions[ii].Duration;
                    }
                }
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(duration, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetTimeBasedStatusCode(regions, value.StatusCode);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the Count aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeNumberOfTransitions(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null)
            {
                return GetNoDataValue(slice);
            }

            // determine whether a transition occurs at the StartTime
            double lastValue = Double.NaN;

            if (slice.EarlyBound != null)
            {
                if (StatusCode.IsGood(slice.EarlyBound.Value.StatusCode))
                {
                    try
                    {
                        lastValue = CastToDouble(slice.EarlyBound.Value);
                    }
                    catch (Exception)
                    {
                        lastValue = Double.NaN;
                    }
                }
            }

            // count the transitions.
            int count = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (!IsGood(values[ii]))
                {
                    continue;
                }

                double nextValue = 0;

                try
                {
                    nextValue = CastToDouble(values[ii]);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!Double.IsNaN(lastValue))
                {
                    if (lastValue != nextValue)
                    {
                        count++;
                    }
                }

                lastValue = nextValue;
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(count, TypeInfo.Scalars.Int32);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            // return result.
            return value;
        }
        #endregion
    }
}
