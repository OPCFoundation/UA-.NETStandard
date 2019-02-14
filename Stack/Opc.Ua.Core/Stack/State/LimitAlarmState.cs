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

namespace Opc.Ua
{
    /// <summary>
    /// The possible states for a limit alarm.
    /// </summary>
    [Flags]
    public enum LimitAlarmStates
    {
        /// <summary>
        /// The alarm ia inactive.
        /// </summary>
        Inactive = 0x0,

        /// <summary>
        /// The alarm is in the HighHigh state.
        /// </summary>
        HighHigh = 0x1,

        /// <summary>
        /// The alarm is in the High state.
        /// </summary>
        High = 0x2,

        /// <summary>
        /// The alarm is in the Low state.
        /// </summary>
        Low = 0x4,

        /// <summary>
        /// The alarm is in the LowLow state.
        /// </summary>
        LowLow  =0x8
    }
}
