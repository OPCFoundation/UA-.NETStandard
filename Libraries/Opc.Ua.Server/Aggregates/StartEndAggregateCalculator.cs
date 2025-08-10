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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class StartEndAggregateCalculator : AggregateCalculator
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
        public StartEndAggregateCalculator(
            NodeId aggregateId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            bool stepped,
            AggregateConfiguration configuration
        )
            : base(aggregateId, startTime, endTime, processingInterval, stepped, configuration)
        {
            SetPartialBit = true;
        }

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
                    case Objects.AggregateFunction_Start:
                        return ComputeStartEnd(slice, false);

                    case Objects.AggregateFunction_End:
                        return ComputeStartEnd(slice, true);

                    case Objects.AggregateFunction_Delta:
                        return ComputeDelta(slice);

                    case Objects.AggregateFunction_StartBound:
                        return ComputeStartEnd2(slice, false);

                    case Objects.AggregateFunction_EndBound:
                        return ComputeStartEnd2(slice, true);

                    case Objects.AggregateFunction_DeltaBounds:
                        return ComputeDelta2(slice);
                }
            }

            return base.ComputeValue(slice);
        }

        /// <summary>
        /// Calculate the Start and End aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeStartEnd(TimeSlice slice, bool returnEnd)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // return start value.
            if (!returnEnd)
            {
                return values[0];
            }
            // return end value.
            else
            {
                return values[^1];
            }
        }

        /// <summary>
        /// Calculates the Delta aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeDelta(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double startValue = double.NaN;
            TypeInfo originalType = null;
            bool badDataSkipped = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                // find start value.
                DataValue start = values[ii];

                if (IsGood(start))
                {
                    try
                    {
                        startValue = CastToDouble(start);
                        originalType = start.WrappedValue.TypeInfo;
                        break;
                    }
                    catch (Exception)
                    {
                        startValue = double.NaN;
                    }
                }

                badDataSkipped = true;
            }

            double endValue = double.NaN;

            for (int ii = values.Count - 1; ii >= 0; ii--)
            {
                // find end value.
                DataValue end = values[ii];

                if (IsGood(end))
                {
                    try
                    {
                        endValue = CastToDouble(end);
                        break;
                    }
                    catch (Exception)
                    {
                        endValue = double.NaN;
                    }

                    break;
                }

                badDataSkipped = true;
            }

            // check if no good data.
            if (double.IsNaN(startValue) || double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }

            var value = new DataValue { SourceTimestamp = GetTimestamp(slice), ServerTimestamp = GetTimestamp(slice) };

            // set status code.
            if (badDataSkipped)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // calculate delta.
            double delta = endValue - startValue;

            if (originalType != null && originalType.BuiltInType != BuiltInType.Double)
            {
                object delta2 = TypeInfo.Cast(delta, TypeInfo.Scalars.Double, originalType.BuiltInType);
                value.WrappedValue = new Variant(delta2, originalType);
            }
            else
            {
                value.WrappedValue = new Variant(delta, TypeInfo.Scalars.Double);
            }

            // return result.
            return value;
        }

        /// <summary>
        /// Calculate the Start2 and End2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeStartEnd2(TimeSlice slice, bool returnEnd)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            DataValue value;

            // return start bound.
            if ((!returnEnd && !TimeFlowsBackward) || (returnEnd && TimeFlowsBackward))
            {
                value = values[0];
            }
            // return end bound.
            else
            {
                value = values[^1];
            }

            if (!IsGood(value))
            {
                value.StatusCode = StatusCodes.BadNoData;
            }

            if (returnEnd)
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);

                if (StatusCode.IsNotBad(value.StatusCode))
                {
                    value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);
                }
            }

            return value;
        }

        /// <summary>
        /// Calculates the Delta2 aggregate for the timeslice.
        /// </summary>
        protected DataValue ComputeDelta2(TimeSlice slice)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            DataValue start = values[0];
            DataValue end = values[^1];

            // check for bad bounds.
            if (!IsGood(start) || !IsGood(end))
            {
                return GetNoDataValue(slice);
            }

            TypeInfo originalType = null;

            // convert to doubles.
            double startValue;
            try
            {
                startValue = CastToDouble(start);
                originalType = start.WrappedValue.TypeInfo;
            }
            catch (Exception)
            {
                startValue = double.NaN;
            }

            double endValue;
            try
            {
                endValue = CastToDouble(end);
            }
            catch (Exception)
            {
                endValue = double.NaN;
            }

            // check for bad bounds.
            if (double.IsNaN(startValue) || double.IsNaN(endValue))
            {
                return GetNoDataValue(slice);
            }

            var value = new DataValue { SourceTimestamp = GetTimestamp(slice), ServerTimestamp = GetTimestamp(slice) };

            if (!IsGood(start) || !IsGood(end))
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // calculate delta.
            double delta = endValue - startValue;

            if (originalType != null && originalType.BuiltInType != BuiltInType.Double)
            {
                object delta2 = TypeInfo.Cast(delta, TypeInfo.Scalars.Double, originalType.BuiltInType);
                value.WrappedValue = new Variant(delta2, originalType);
            }
            else
            {
                value.WrappedValue = new Variant(delta, TypeInfo.Scalars.Double);
            }

            // return result.
            return value;
        }
    }
}
