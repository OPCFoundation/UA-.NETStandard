/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Opc.Ua
{
    /// <summary>
    /// Produces high resolution timestamps.
    /// </summary>
    public class HiResClock
    {
        /// <summary>
        /// Returns the current UTC time (bugs in HALs on some computers can result in time jumping backwards).
        /// </summary>
        public static DateTime UtcNow
        {
            get
            {
                return DateTime.UtcNow;                
            }
        }

        /// <summary>
        /// Disables the hi-res clock (may be necessary on some machines with bugs in the HAL).
        /// </summary>
        public static bool Disabled
        {
            get
            {
                return s_Default.m_disabled;
            }

            set
            {
                s_Default.m_disabled = value;
            }
        }

        /// <summary>
        /// Constructs a class.
        /// </summary>
        private HiResClock()
        {
        }

        /// <summary>
        /// Defines a global instance.
        /// </summary>
        private static readonly HiResClock s_Default = new HiResClock();
        private bool m_disabled;
    }

}
