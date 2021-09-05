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
    public class MinMaxAggregateCalculator : AggregateCalculator
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
        public MinMaxAggregateCalculator(
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
                    case Objects.AggregateFunction_Minimum:
                    {
                        return ComputeMinMax(slice, 1, false);
                    }

                    case Objects.AggregateFunction_MinimumActualTime:
                    {
                        return ComputeMinMax(slice, 1, true);
                    }

                    case Objects.AggregateFunction_Maximum:
                    {
                        return ComputeMinMax(slice, 2, false);
                    }

                    case Objects.AggregateFunction_MaximumActualTime:
                    {
                        return ComputeMinMax(slice, 2, true);
                    }

                    case Objects.AggregateFunction_Range:
                    {
                        return ComputeMinMax(slice, 3, false);
                    }

                    case Objects.AggregateFunction_Minimum2:
                    {
                        return ComputeMinMax2(slice, 1, false);
                    }

                    case Objects.AggregateFunction_MinimumActualTime2:
                    {
                        return ComputeMinMax2(slice, 1, true);
                    }

                    case Objects.AggregateFunction_Maximum2:
                    {
                        return ComputeMinMax2(slice, 2, false);
                    }

                    case Objects.AggregateFunction_MaximumActualTime2:
                    {
                        return ComputeMinMax2(slice, 2, true);
                    }

                    case Objects.AggregateFunction_Range2:
                    {
                        return ComputeMinMax2(slice, 3, false);
                    }
                }
            }

            return base.ComputeValue(slice);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Calculate the Minimum, Maximum, MinimumActualTime and MaximumActualTime aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue> values = GetValues(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double minimumGoodValue = Double.MaxValue;
            double minimumUncertainValue = Double.MaxValue;
            double maximumGoodValue = Double.MinValue;
            double maximumUncertainValue = Double.MinValue;

            DateTime minimumGoodTimestamp = DateTime.MinValue;
            DateTime maximumGoodTimestamp = DateTime.MinValue;

            TypeInfo minimumOriginalType = null;
            TypeInfo maximumOriginalType = null;

            bool badValuesExist = false;
            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool goodValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                double currentValue = 0;
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values.
                if (!IsGood(values[ii]))
                {
                    badValuesExist = true;
                    continue;
                }

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

                // check for uncertain.
                if (StatusCode.IsUncertain(currentStatus))
                {
                    if (minimumUncertainValue > currentValue)
                    {
                        minimumUncertainValue = currentValue;
                    }

                    if (maximumUncertainValue < currentValue)
                    {
                        maximumUncertainValue = currentValue;
                    }

                    continue;
                }

                // check for new minimum.
                if (minimumGoodValue > currentValue)
                {
                    minimumGoodValue = currentValue;
                    minimumGoodTimestamp = currentTime;
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

            // check if at least on good value exists.
            if (!goodValueExists)
            {
                return GetNoDataValue(slice);
            }

            // set the status code.
            StatusCode statusCode = StatusCodes.Good;

            // uncertain if any bad values exist.
            if (badValuesExist)
            {
                statusCode = StatusCodes.UncertainDataSubNormal;
            }

            // determine the calculated value to return.
            object processedValue = null;
            TypeInfo processedType = null;
            DateTime processedTimestamp = DateTime.MinValue;
            bool uncertainValueExists = false;
            bool duplicatesExist = false;

            if (valueType == 1)
            {
                processedValue = minimumGoodValue;
                processedTimestamp = minimumGoodTimestamp;
                processedType = minimumOriginalType;
                uncertainValueExists = minimumGoodValue > minimumUncertainValue;
                duplicatesExist = duplicatesMinimumsExist;
            }

            else if (valueType == 2)
            {
                processedValue = maximumGoodValue;
                processedTimestamp = maximumGoodTimestamp;
                processedType = maximumOriginalType;
                uncertainValueExists = maximumGoodValue < maximumUncertainValue;
                duplicatesExist = duplicatesMaximumsExist;
            }

            else if (valueType == 3)
            {
                processedValue = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedType = TypeInfo.Scalars.Double;
                uncertainValueExists = maximumGoodValue < maximumUncertainValue || minimumGoodValue > minimumUncertainValue;
            }

            // set calculated if not returning actual time and value is not at the start time.
            if (!returnActualTime && processedTimestamp != slice.StartTime)
            {
                statusCode = statusCode.SetAggregateBits(AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // convert back to original datatype.
            if (processedType != null && processedType.BuiltInType != BuiltInType.Double)
            {
                processedValue = TypeInfo.Cast(processedValue, TypeInfo.Scalars.Double, processedType.BuiltInType);
            }
            else
            {
                processedType = TypeInfo.Scalars.Double;
            }

            // create processed value.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(processedValue, processedType);
            value.StatusCode = statusCode;

            if (returnActualTime)
            {
                value.SourceTimestamp = processedTimestamp;
                value.ServerTimestamp = processedTimestamp;
            }
            else
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);
            }

            return value;
        }

        /// <summary>
        /// Calculate the Minimum2, Maximum2, MinimumActualTime2, MaximumActualTime2 and Range2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeMinMax2(TimeSlice slice, int valueType, bool returnActualTime)
        {
            // get the values in the slice.
            List<DataValue> values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double minimumGoodValue = Double.MaxValue;
            double maximumGoodValue = Double.MinValue;

            DateTime minimumGoodTimestamp = DateTime.MinValue;
            DateTime maximumGoodTimestamp = DateTime.MinValue;

            StatusCode minimumGoodStatusCode = StatusCodes.Good;
            StatusCode maximumGoodStatusCode = StatusCodes.Good;

            TypeInfo minimumOriginalType = null;
            TypeInfo maximumOriginalType = null;

            bool duplicatesMinimumsExist = false;
            bool duplicatesMaximumsExist = false;
            bool goodValueExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                double currentValue = 0;
                DateTime currentTime = values[ii].SourceTimestamp;
                StatusCode currentStatus = values[ii].StatusCode;

                // ignore bad values (as determined by the TreatUncertainAsBad parameter).
                if (!IsGood(values[ii]))
                {
                    continue;
                }

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
                if (currentTime == slice.EndTime)
                {
                    if (Stepped)
                    {
                        break;
                    }
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

            // check if at least on good value exists.
            if (!goodValueExists)
            {
                return GetNoDataValue(slice);
            }

            // determine the calculated value to return.
            object processedValue = null;
            TypeInfo processedType = null;
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
                processedValue = Math.Abs(maximumGoodValue - minimumGoodValue);
                processedType = TypeInfo.Scalars.Double;
            }

            // set the status code.
            StatusCode statusCode = processedStatusCode;

            // set calculated if not returning actual time and value is not at the start time.
            if (!returnActualTime && processedTimestamp != slice.StartTime && (statusCode.AggregateBits & AggregateBits.Interpolated) == 0)
            {
                statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.Calculated);
            }

            // set the multiple values flags.
            if (duplicatesExist)
            {
                statusCode = statusCode.SetAggregateBits(statusCode.AggregateBits | AggregateBits.MultipleValues);
            }

            // convert back to original datatype.
            if (processedType != null && processedType.BuiltInType != BuiltInType.Double)
            {
                processedValue = TypeInfo.Cast(processedValue, TypeInfo.Scalars.Double, processedType.BuiltInType);
            }
            else
            {
                processedType = TypeInfo.Scalars.Double;
            }

            // create processed value.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(processedValue, processedType);
            value.StatusCode = GetTimeBasedStatusCode(slice, values, statusCode);

            // zero value if status is bad.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value.WrappedValue = Variant.Null;
            }

            if (returnActualTime)
            {
                // calculate effective time if end bound is used.
                if (TimeFlowsBackward)
                {
                    if (processedTimestamp == slice.StartTime)
                    {
                        processedTimestamp = processedTimestamp.AddMilliseconds(+1);
                        value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Interpolated);
                    }
                }
                else
                {
                    if (processedTimestamp == slice.EndTime)
                    {
                        processedTimestamp = processedTimestamp.AddMilliseconds(-1);
                        value.StatusCode = value.StatusCode.SetAggregateBits(value.StatusCode.AggregateBits | AggregateBits.Interpolated);
                    }
                }

                value.SourceTimestamp = processedTimestamp;
                value.ServerTimestamp = processedTimestamp;
            }
            else
            {
                value.SourceTimestamp = GetTimestamp(slice);
                value.ServerTimestamp = GetTimestamp(slice);
            }

            return value;
        }
        #endregion
    }
}
