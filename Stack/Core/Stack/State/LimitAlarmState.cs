/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
