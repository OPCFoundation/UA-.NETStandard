/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
