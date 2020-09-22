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
    public static class HiResClock
    {
        // In Net Core 3.0+ has monotonic timer from system start.
        // https://docs.microsoft.com/ru-ru/dotnet/api/system.environment.tickcount64?view=netcore-3.1
#if NETCORE3_0 || NETCORE3_1
        public static long TickCount64 { get { return Environment.TickCount64; } } 
#else

        /// <summary>Get monotonic timer from system starts in Windows in Msec. In other systems using DateTime. (posible lost monitored items when combuter time shift. see #1121)</summary>
        public static long TickCount64
        {
            get
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    return GetTickCount64();
                else
                    return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern long GetTickCount64();

#endif


        /// <summary>Returns the current UTC time (bugs in HALs on some computers can result in time jumping backwards). </summary>
        public static DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        /// <summary>Disables the hi-res clock (may be necessary on some machines with bugs in the HAL).</summary>
        public static bool Disabled { get; set; }
    }

}
