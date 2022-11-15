/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a range.
    /// </summary>
    public partial class Range
    {
        /// <summary>
        /// Initializes the object with the high and low limits.
        /// </summary>
        public Range(double high, double low)
        {
            m_low  = low;
            m_high = high;

            // swap values if high is not actually higher.
            if (low > high)
            {
                m_high = low;
                m_low  = high;
            }
        }

        /// <summary>
        /// Returns the difference between high and low.
        /// </summary>
        public double Magnitude
        {
            get { return Math.Abs(m_high - m_low); }
        }
    }
}
