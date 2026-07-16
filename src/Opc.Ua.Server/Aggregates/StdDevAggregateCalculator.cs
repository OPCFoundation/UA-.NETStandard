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
using System.Diagnostics;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the value of an aggregate.
    /// </summary>
    public class StdDevAggregateCalculator : AggregateCalculator
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
        public StdDevAggregateCalculator(
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
                // valueType == 1: StandardDeviation, valueType == 2: Variance.
                // isSample == true: sample (divisor n-1); isSample == false: population (divisor n).
                // Part 13 v1.05.07 §5.4.3.37/.39: both operate on the Good raw values in the interval
                // (UseBounds = None); only the divisor differs.

                case Objects.AggregateFunction_StandardDeviationPopulation:
                    return ComputeStdDev(slice, false, 1);
                case Objects.AggregateFunction_StandardDeviationSample:
                    return ComputeStdDev(slice, true, 1);
                case Objects.AggregateFunction_VariancePopulation:
                    return ComputeStdDev(slice, false, 2);
                case Objects.AggregateFunction_VarianceSample:
                    return ComputeStdDev(slice, true, 2);
                default:
                    return base.ComputeValue(slice);
            }
        }

        /// <summary>
        /// Calculates the RegSlope, RegConst and RegStdDev aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeRegression(TimeSlice slice, int valueType)
        {
            // get the values in the slice.
            List<DataValue>? values = GetValuesWithSimpleBounds(slice);

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion>? regions = GetRegionsInValueSet(values, false, true);

            var xData = new List<double>();
            var yData = new List<double>();

            double duration = 0;
            bool nonGoodDataExists = false;

            for (int ii = 0; ii < regions!.Count; ii++)
            {
                if (StatusCode.IsGood(regions[ii].StatusCode))
                {
                    xData.Add(regions[ii].StartValue);
                    yData.Add(duration);
                }
                else
                {
                    nonGoodDataExists = true;
                }

                // normalize to seconds.
                duration += regions[ii].Duration / 1000.0;
            }

            // check if no good data.
            if (xData.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // compute the regression parameters.
            double regSlope = 0;
            double regConst = 0;
            double regStdDev = 0;

            if (xData.Count > 1)
            {
                double xAvg = 0;
                double yAvg = 0;
                double xxAgv = 0;
                double xyAvg = 0;

                for (int ii = 0; ii < xData.Count; ii++)
                {
                    xAvg += xData[ii];
                    yAvg += yData[ii];
                    xxAgv += xData[ii] * xData[ii];
                    xyAvg += xData[ii] * yData[ii];
                }

                xAvg /= xData.Count;
                yAvg /= xData.Count;
                xxAgv /= xData.Count;
                xyAvg /= xData.Count;

                regSlope = (xyAvg - (xAvg * yAvg)) / (xxAgv - (xAvg * xAvg));
                regConst = yAvg - (regSlope * xAvg);

                var errors = new List<double>();

                double eAvg = 0;

                for (int ii = 0; ii < xData.Count; ii++)
                {
                    double error = yData[ii] - regConst - (regSlope * xData[ii]);
                    errors.Add(error);
                    eAvg += error;
                }

                eAvg /= errors.Count;

                double variance = 0;

                for (int ii = 0; ii < errors.Count; ii++)
                {
                    double error = errors[ii] - eAvg;
                    variance += error * error;
                }

                variance /= errors.Count;
                regStdDev = Math.Sqrt(variance);
            }

            // select the result.
            double result = 0;

            switch (valueType)
            {
                case 1:
                    result = regSlope;
                    break;
                case 2:
                    result = regConst;
                    break;
                case 3:
                    result = regStdDev;
                    break;
                default:
                    Debug.Fail($"Unexpected value type {valueType}");
                    break;
            }

            // set the timestamp and status.
            var value = new DataValue(
                Variant.From(result),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));

            if (nonGoodDataExists)
            {
                value = value.WithStatus(StatusCodes.UncertainDataSubNormal);
            }

            // return result.
            return value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));
        }

        /// <summary>
        /// Calculates the StandardDeviation/Variance (sample or population) for the timeslice.
        /// </summary>
        /// <remarks>
        /// Part 13 v1.05.07 §5.4.3.37 (sample) and §5.4.3.39 (population) operate on the Good raw
        /// values in the interval (UseBounds = None). The sum of squared deviations is divided by
        /// (n-1) for the sample variants and by n for the population variants. This is a value-count
        /// calculation, not a time-weighted/sub-region one, so all n Good raw values participate
        /// (computing over sub-regions would silently drop the last raw value).
        /// </remarks>
        protected DataValue ComputeStdDev(TimeSlice slice, bool isSample, int valueType)
        {
            // Good raw values in the slice (endTime exclusive, no bounds).
            List<DataValue>? values = GetValues(slice);
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            var xData = new List<double>(values.Count);
            double sum = 0;
            bool nonGoodDataExists = false;

            for (int ii = 0; ii < values.Count; ii++)
            {
                if (IsGood(values[ii]))
                {
                    try
                    {
                        double x = CastToDouble(values[ii]);
                        xData.Add(x);
                        sum += x;
                    }
                    catch (Exception)
                    {
                        nonGoodDataExists = true;
                    }
                }
                else
                {
                    nonGoodDataExists = true;
                }
            }

            // check if no good data.
            if (xData.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            double average = sum / xData.Count;

            // sum of squared deviations from the mean.
            double variance = 0;
            for (int ii = 0; ii < xData.Count; ii++)
            {
                double error = xData[ii] - average;
                variance += error * error;
            }

            if (isSample)
            {
                // Part 13 §5.4.3.37: sample divides by (n-1); n == 1 yields 0.
                variance = xData.Count <= 1 ? 0 : variance / (xData.Count - 1);
            }
            else
            {
                // Part 13 §5.4.3.39: population divides by n.
                variance /= xData.Count;
            }

            double result = valueType == 1 ? Math.Sqrt(variance) : variance;

            var value = new DataValue(
                Variant.From(result),
                StatusCodes.Good,
                GetTimestamp(slice),
                GetTimestamp(slice));

            if (nonGoodDataExists)
            {
                value = value.WithStatus(StatusCodes.UncertainDataSubNormal);
            }

            return value.WithStatus(value.StatusCode.WithAggregateBits(AggregateBits.Calculated));
        }
    }
}
