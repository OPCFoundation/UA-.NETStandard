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
    public class AverageAggregateCalculator : AggregateCalculator
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
        public AverageAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration)
        : 
            base(aggregateId, startTime, endTime, processingInterval, stepped, configuration)
        {
            SetPartialBit = aggregateId != Opc.Ua.ObjectIds.AggregateFunction_Average;
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
                    case Objects.AggregateFunction_Average:
                    {
                        return ComputeAverage(slice);
                    }

                    case Objects.AggregateFunction_TimeAverage:
                    {
                        return ComputeTimeAverage(slice, false, 1);
                    }

                    case Objects.AggregateFunction_Total:
                    {
                        return ComputeTimeAverage(slice, false, 2);
                    }

                    case Objects.AggregateFunction_TimeAverage2:
                    {
                        return ComputeTimeAverage(slice, true, 1);
                    }

                    case Objects.AggregateFunction_Total2:
                    {
                        return ComputeTimeAverage(slice, true, 2);
                    }
                }
            }

            return base.ComputeValue(slice);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Calculates the RegSlope, RegConst and RegStdDev aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeAverage(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // calculate total and count.
            int count = 0;
            double total = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (StatusCode.IsGood(values[ii].StatusCode))
                {
                    try
                    {
                        double sample = CastToDouble(values[ii]);
                        total += sample;
                        count++;
                    }
                    catch
                    {
                        // ignore conversion errors.
                    }
                }
            }

            // check for empty slice.
            if (count == 0)
            {
                return GetNoDataValue(slice);
            }

            // select the result.
            double result = total/count;

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(result, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
            value.StatusCode = GetValueBasedStatusCode(slice, values, value.StatusCode);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the StdDev, Variance, StdDev2 and Variance2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeTimeAverage(TimeSlice slice, bool useSimpleBounds, int valueType)
        {
            // get the values in the slice.
            List<DataValue> values = null;

            if (useSimpleBounds)
            {
                values = GetValuesWithSimpleBounds(slice);
            }
            else
            {
                values = GetValuesWithInterpolatedBounds(slice);
            }

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, !useSimpleBounds, Stepped);

            double total = 0;
            double totalDuration = 0;
            bool nonGoodRegionsExists = false;

            for (int ii = 0; ii < regions.Count; ii++)
            {
                double duration = regions[ii].Duration/1000.0;

                if (StatusCode.IsNotBad(regions[ii].StatusCode))
                {
                    total += (regions[ii].StartValue + regions[ii].EndValue) * duration / 2;
                    totalDuration += duration;
                }

                if (StatusCode.IsNotGood(regions[ii].StatusCode))
                {
                    nonGoodRegionsExists = true;
                }
            }

            // check if no good data.
            if (totalDuration == 0)
            {
                return GetNoDataValue(slice);
            }

            // select the result.
            double result = 0;

            switch (valueType)
            {
                case 1: { result = total/totalDuration; break; }
                case 2: { result = total; break; }
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(result, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);

            if (useSimpleBounds)
            {
                value.StatusCode = GetTimeBasedStatusCode(regions, value.StatusCode);
            }
            else
            {
                value.StatusCode = StatusCodes.Good;

                if (nonGoodRegionsExists)
                {
                    value.StatusCode = StatusCodes.UncertainDataSubNormal;
                }
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }
        #endregion
    }
}
