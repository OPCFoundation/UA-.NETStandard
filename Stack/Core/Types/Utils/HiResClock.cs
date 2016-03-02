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

        /// <summary>
        /// Defines the native methods used by the class.
        /// </summary>
        private static class NativeMethods
        {
            [DllImport("Kernel32.dll")]
            public static extern int QueryPerformanceFrequency(out long lpFrequency);

            [DllImport("Kernel32.dll")]
            public static extern int QueryPerformanceCounter(out long lpFrequency);
        }
        
        private bool m_disabled;
    }

}
