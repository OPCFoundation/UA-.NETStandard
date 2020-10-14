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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Aggregates
{
    /// <summary>
    /// The bounding value for a timeslice.
    /// </summary>
    public class BoundingValue : AggregateCursor
    {
        #region Constructors
        #endregion

        #region Public Members
        /// <summary>
        /// Indicates how the bounding value was obtained.
        /// </summary>
        public BoundingValueType DerivationType { get; set; }

        /// <summary>
        /// Timestamp of the boundary
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// A good data point coincident in time to the bounding value we want to compute.
        /// </summary>
        public DataValue RawPoint { get; set; }

        /// <summary>
        /// The value for the bound.
        /// </summary>
        public DataValue Value
        {
            get
            {
                if (_value != null)
                {                    
                    return _value;
                }

                StatusCode code = StatusCodes.Good;
 
                switch (DerivationType)
                {
                    case BoundingValueType.Raw:
                        if (RawPoint != null)
                        {
                            _value = new DataValue(RawPoint);
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code.AggregateBits = AggregateBits.Raw;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.QualityRaw:
                        if (RawPoint != null)
                        {
                            _value = new DataValue();
                            _value.Value = RawPoint.StatusCode;
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code.AggregateBits = AggregateBits.Raw;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.SlopedExtrapolation:
                        if ((PriorPoint != null) && (EarlyPoint != null))
                        {
                            _value = new DataValue();
                            _value.Value = ProjectedValue(PriorPoint, EarlyPoint, Timestamp);
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = StatusCodes.UncertainDataSubNormal;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.SlopedInterpolation:
                        if ((EarlyPoint != null) && (LatePoint != null))
                        {
                            _value = new DataValue(EarlyPoint);
                            _value.Value = ProjectedValue(EarlyPoint, LatePoint, Timestamp);
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = (CurrentBadPoints.Count > 0) ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.SteppedExtrapolation:
                        if (EarlyPoint != null)
                        {
                            _value = new DataValue(EarlyPoint);
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = StatusCodes.UncertainDataSubNormal;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.SteppedInterpolation:
                        if (EarlyPoint != null)
                        {
                            _value = new DataValue(EarlyPoint);
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = (CurrentBadPoints.Count > 0) ? StatusCodes.UncertainDataSubNormal : StatusCodes.Good;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.QualityExtrapolation:
                        if (EarlyPoint != null)
                        {
                            _value = new DataValue();
                            DataValue valueToUse = EarlyPoint;
                            foreach (DataValue dv in CurrentBadPoints)
                                if ((dv.SourceTimestamp > valueToUse.SourceTimestamp) && (dv.SourceTimestamp < Timestamp))
                                    valueToUse = dv;
                            _value.Value = valueToUse.StatusCode;
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = StatusCodes.UncertainDataSubNormal;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.QualityInterpolation:
                        if (EarlyPoint != null)
                        {
                            _value = new DataValue();
                            DataValue valueToUse = EarlyPoint;
                            foreach (DataValue dv in CurrentBadPoints)
                                if ((dv.SourceTimestamp > valueToUse.SourceTimestamp) && (dv.SourceTimestamp < Timestamp))
                                    valueToUse = dv;
                            _value.Value = valueToUse.StatusCode;
                            _value.SourceTimestamp = Timestamp;
                            _value.ServerTimestamp = DateTime.UtcNow;
                            code = StatusCodes.Good;
                            code.AggregateBits = AggregateBits.Interpolated;
                            _value.StatusCode = code;
                        }
                        break;
                    case BoundingValueType.None:
                    default:
                        break;
                }

                return _value;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Projects the value to the specified time using the two points.
        /// </summary>
        private double ProjectedValue(DataValue p1, DataValue p2, DateTime time)
        {
            double ve = Convert.ToDouble(p1.Value, CultureInfo.InvariantCulture);
            double vl = Convert.ToDouble(p2.Value, CultureInfo.InvariantCulture);
            double fraction = (time - p1.SourceTimestamp).TotalMilliseconds/(p2.SourceTimestamp - p1.SourceTimestamp).TotalMilliseconds;
            return ve + (fraction * (vl - ve));
        }
        #endregion

        #region Private Fields
        private DataValue _value = null;
        #endregion
    }

    /// <summary>
    /// The possible types of bounding values.
    /// </summary>
    public enum BoundingValueType
    {
        /// <summary>
        /// No bounding value.
        /// </summary>
        None,

        /// <summary>
        /// Derived from the raw value.
        /// </summary>
        Raw,

        /// <summary>
        /// Derived from the raw value using the quality of the raw value.
        /// </summary>
        QualityRaw,

        /// <summary>
        /// Calculated with stepped interpolation.
        /// </summary>
        SteppedInterpolation,

        /// <summary>
        /// Calculated with sloped interpolation.
        /// </summary>
        SlopedInterpolation,

        /// <summary>
        /// A value calculated with stepped interpolation using the quality of the closest raw value.
        /// </summary>
        QualityInterpolation,

        /// <summary>
        /// Calculated with stepped extrapolation.
        /// </summary>
        SteppedExtrapolation,

        /// <summary>
        /// Calculated with sloped extrapolation.
        /// </summary>
        SlopedExtrapolation,

        /// <summary>
        /// A value calculated with stepped extrapolation using the quality of the closest raw value.
        /// </summary>
        QualityExtrapolation
    }
}
