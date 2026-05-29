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
using Opc.Ua;
using Range = Opc.Ua.Range;

namespace Boiler
{
    /// <summary>
    /// An object representing a generic controller.
    /// </summary>
    public partial class GenericControllerState
    {
        /// <summary>
        /// Updates the measurement and calculates the new control output.
        /// </summary>
        public double UpdateMeasurement(AnalogItemState<double> source)
        {
            PropertyState<double> measurement = m_measurement!;
            PropertyState<double> setPoint = m_setPoint!;
            PropertyState<double> controlOut = m_controlOut!;

            Range? range = source.EURange?.Value;
            measurement.Value = source.Value;

            // clamp the setpoint.
            if (range != null)
            {
                if (setPoint.Value > range.High)
                {
                    setPoint.Value = range.High;
                }

                if (setPoint.Value < range.Low)
                {
                    setPoint.Value = range.Low;
                }
            }

            // calculate error.
            controlOut.Value = setPoint.Value - measurement.Value;

            if (range != null)
            {
                controlOut.Value /= range.Magnitude;

                if (Math.Abs(controlOut.Value) > 1.0)
                {
                    controlOut.Value = controlOut.Value < 0 ? -1.0 : +1.0;
                }
            }

            // return the new output.
            return controlOut.Value;
        }
    }
}
