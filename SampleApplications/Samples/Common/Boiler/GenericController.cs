/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Boiler
{       
    /// <summary>
    /// A object representing a generic controller.
    /// </summary>
    public partial class GenericControllerState
    {
        #region Public Interface
        /// <summary>
        /// Updates the measurement and calculates the new control output.
        /// </summary>
        public double UpdateMeasurement(AnalogItemState<double> source)
        {
            Range range = source.EURange.Value;
            m_measurement.Value = source.Value;

            // clamp the setpoint.
            if (range != null)
            {
                if (m_setPoint.Value > range.High)
                {
                    m_setPoint.Value = range.High;
                }
                
                if (m_setPoint.Value < range.Low)
                {
                    m_setPoint.Value = range.Low;
                }                
            }

            // calculate error.
            m_controlOut.Value = m_setPoint.Value - m_measurement.Value;

            if (range != null)
            {
                m_controlOut.Value /= range.Magnitude;

                if (Math.Abs(m_controlOut.Value) > 1.0)
                {
                    m_controlOut.Value = (m_controlOut.Value < 0)?-1.0:+1.0;
                }
            }

            // return the new output.
            return m_controlOut.Value;            
        }
        #endregion
    }
}
