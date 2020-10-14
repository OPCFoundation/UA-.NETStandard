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
using System.Globalization;

namespace Opc.Ua.Aggregates
{
    /// <summary>
    /// Calculates interpolated values.
    /// </summary>
    /// <remarks>
    /// To correctly get the interpolated value, the raw data passed to this aggregator needs to have "GOOD" boundary values. That means, it's 
    /// the user responsibility to find out a good value before/after the period of interest if the bounding values of that period are not good.
    /// </remarks>
    public class InterpolativeAggregate : FloatInterpolatingCalculator
    {
        /// <summary>
        /// Updates the bounding values.
        /// </summary>
        public override void UpdateBoundingValues(TimeSlice bucket, AggregateState state)
        {
            base.UpdateBoundingValues(bucket, state);
            UpdatePriorPoint(bucket.EarlyBound, state);
        }

        /// <summary>
        /// Updates the value for the time slice.
        /// </summary>
        public override DataValue Compute(IAggregationContext context, TimeSlice bucket, AggregateState state)
        {
            DataValue retval = new DataValue { SourceTimestamp = bucket.From };
            StatusCode code = StatusCodes.BadNoData;
            DataValue boundValue = context.IsReverseAggregation ? bucket.LateBound.Value : bucket.EarlyBound.Value;
            if (boundValue != null)
            {
                code = bucket.EarlyBound.Value.StatusCode.Code;
                code.AggregateBits = bucket.EarlyBound.Value.StatusCode.AggregateBits;
                retval.Value = Convert.ToDouble(bucket.EarlyBound.Value.Value, CultureInfo.InvariantCulture);
            }
            if (bucket.Incomplete) code.AggregateBits |= AggregateBits.Partial;
            retval.StatusCode = code;
            return retval;
        }
    }

}
