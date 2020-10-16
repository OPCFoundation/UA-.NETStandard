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
    public class StdDevAggregateCalculator : AggregateCalculator
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
        public StdDevAggregateCalculator(
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
                    case Objects.AggregateFunction_StandardDeviationPopulation:
                    {
                        return ComputeStdDev(slice, false, 1);
                    }

                    case Objects.AggregateFunction_StandardDeviationSample:
                    {
                        return ComputeStdDev(slice, false, 2);
                    }

                    case Objects.AggregateFunction_VariancePopulation:
                    {
                        return ComputeStdDev(slice, true, 1);
                    }

                    case Objects.AggregateFunction_VarianceSample:
                    {
                        return ComputeStdDev(slice, true, 2);
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
        protected DataValue ComputeRegression(TimeSlice slice, int valueType)
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

            List<double> xData = new List<double>();
            List<double> yData = new List<double>();

            double duration = 0;
            bool nonGoodDataExists = false;

            for (int ii = 0; ii < regions.Count; ii++)
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
                duration += regions[ii].Duration/1000.0;
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

                regSlope = (xyAvg - xAvg * yAvg) / (xxAgv - xAvg * xAvg);
                regConst = yAvg - regSlope * xAvg;
                
                List<double> errors = new List<double>();

                double eAvg = 0;

                for (int ii = 0; ii < xData.Count; ii++)
                {
                    double error = yData[ii] - regConst - regSlope * xData[ii];
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
                case 1: { result = regSlope;  break; }
                case 2: { result = regConst;  break; }
                case 3: { result = regStdDev; break; }
            }
            
            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(result, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);

            if (nonGoodDataExists)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }

        /// <summary>
        /// Calculates the StdDev, Variance, StdDev2 and Variance2 aggregates for the timeslice.
        /// </summary>
        protected DataValue ComputeStdDev(TimeSlice slice, bool includeBounds, int valueType)
        {
            // get the values in the slice.
            List<DataValue> values = null;

            if (includeBounds)
            {
                values = GetValuesWithSimpleBounds(slice);
            }
            else
            {
                values = GetValues(slice);
            }

            // check for empty slice.
            if (values == null || values.Count == 0)
            {
                return GetNoDataValue(slice);
            }

            // get the regions.
            List<SubRegion> regions = GetRegionsInValueSet(values, false, true);

            List<double> xData = new List<double>();
            double average = 0;
            bool nonGoodDataExists = false;

            for (int ii = 0; ii < regions.Count; ii++)
            {
                if (StatusCode.IsGood(regions[ii].StatusCode))
                {
                    xData.Add(regions[ii].StartValue);
                    average += regions[ii].StartValue;
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

            average /= xData.Count;

            // calculate variance.
            double variance = 0;

            for (int ii = 0; ii < xData.Count; ii++)
            {
                double error = xData[ii] - average;
                variance += error*error;
            }

            // use the sample variance if bounds are included.
            if (includeBounds)
            {
                variance /= (xData.Count + 1);
            }
            
           // use the population variance if bounds are not included.
            else
            {
                variance /= xData.Count;
            }

            // select the result.
            double result = 0;

            switch (valueType)
            {
                case 1: { result = Math.Sqrt(variance); break; }
                case 2: { result = variance; break; }
            }

            // set the timestamp and status.
            DataValue value = new DataValue();
            value.WrappedValue = new Variant(result, TypeInfo.Scalars.Double);
            value.SourceTimestamp = GetTimestamp(slice);
            value.ServerTimestamp = GetTimestamp(slice);

            if (nonGoodDataExists)
            {
                value.StatusCode = StatusCodes.UncertainDataSubNormal;
            }

            value.StatusCode = value.StatusCode.SetAggregateBits(AggregateBits.Calculated);

            // return result.
            return value;
        }
        #endregion
    }
}
