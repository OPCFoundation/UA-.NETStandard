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
    public class StatusAggregateCalculator : AggregateCalculator
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
        public StatusAggregateCalculator(
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
                    case Objects.AggregateFunction_DurationGood:
                    {
                        return ComputeDurationGoodBad(slice, false, false);
                    }

                    case Objects.AggregateFunction_DurationBad:
                    {
                        return ComputeDurationGoodBad(slice, true, false);
                    }

                    case Objects.AggregateFunction_PercentGood:
                    {
                        return ComputeDurationGoodBad(slice, false, true);
                    }

                    case Objects.AggregateFunction_PercentBad:
                    {
                        return ComputeDurationGoodBad(slice, true, true);
                    }

                    case Objects.AggregateFunction_WorstQuality:
                    {
                        return ComputeWorstQuality(slice, false);
                    }

                    case Objects.AggregateFunction_WorstQuality2:
                    {
                        return ComputeWorstQuality(slice, true);
                    }
                }
            }

            return base.ComputeValue(slice);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Calculates the DurationGood and DurationBad aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeDurationGoodBad(TimeSlice slice, bool isBad, bool usePercent)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            double duration = 0;
            double total = 0;

            for (int ii = 0; ii < regions.Count; ii++)
            {
                total += regions[ii].Duration;

                if (isBad)
                {
                    if (StatusCode.IsBad(regions[ii].StatusCode))
                    {
                        duration += regions[ii].Duration;
                    }
                }
                else
                {
                    if (StatusCode.IsGood(regions[ii].StatusCode))
                    {
                        duration += regions[ii].Duration;
                    }
                }
            }

            if (usePercent)
            {
                duration = (duration / total) * 100;
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(duration, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);            
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the DurationGood and DurationBad aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeWorstQuality(TimeSlice slice, bool includeBounds)
        {
            // get the values in the slice.
            List<DataValue> values = null;
            
            if (!includeBounds)
            {
                values = GetValues(slice);
            }
            else
            {
                values = GetValuesWithSimpleBounds(slice);
            }

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            StatusCode worstQuality = StatusCodes.Good;
            int badQualityCount = 0;
            int uncertainQualityCount = 0;

            for (int ii = 0; ii < values.Count; ii++)
            {
                StatusCode quality = values[ii].StatusCode;

                if (StatusCode.IsBad(quality))
                {
                    badQualityCount++;

                    if (StatusCode.IsNotBad(worstQuality))
                    {
                        worstQuality = quality.CodeBits;
                    }

                    continue;
                }

                if (StatusCode.IsUncertain(quality))
                {
                    uncertainQualityCount++;

                    if (StatusCode.IsGood(worstQuality))
                    {
                        worstQuality = quality.CodeBits;
                    }

                    continue;
                }
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(worstQuality, TypeInfo.Scalars.StatusCode);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);
            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            if ((StatusCode.IsBad(worstQuality) && badQualityCount > 1) || (StatusCode.IsUncertain(worstQuality) && uncertainQualityCount > 1))
            {
                value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // return result.
            return value;
        }
        #endregion
    }
}
