/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.Diagnostics;
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
                if (s_Default.m_disabled)
                {
                    return DateTime.UtcNow;
                }

                long counter = 0;
#if NETFRAMEWORK
                if (NativeMethods.QueryPerformanceCounter(out counter) == 0)
                {
                    return DateTime.UtcNow;
                }
#else
                counter = Stopwatch.GetTimestamp();
#endif
                decimal ticks = (counter - s_Default.m_baseline) * s_Default.m_ratio;
                return new DateTime((long)ticks + s_Default.m_offset);
            }
        }

        /// <summary>
        /// Returns a monotonic increasing tick count in milliseconds.
        /// </summary>
        public static long TickCount64
        {
            get
            {
                if (s_Default.m_disabled)
                {
                    return (long)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                }
#if NETFRAMEWORK
                return NativeMethods.GetTickCount64();
#else
                if (s_Default.m_isWindowsOS)
                {
                    return NativeMethods.GetTickCount64();
                }
                long counter = 0;
                counter = Stopwatch.GetTimestamp();
                return (long)(counter / s_Default.m_ticksPerMillisecond);
#endif
            }
        }

        /// <summary>
        /// Return the frequency of the ticks.
        /// </summary>
        public static long Frequency => s_Default.m_frequency;

        /// <summary>
        /// Return the number of ticks per millisecond.
        /// </summary>
        public static double TicksPerMillisecond => s_Default.m_ticksPerMillisecond;

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
#if NETFRAMEWORK
            if (NativeMethods.QueryPerformanceFrequency(out m_frequency) == 0)
            {
                m_frequency = TimeSpan.TicksPerSecond;
            }
            m_offset = DateTime.UtcNow.Ticks;
            if (NativeMethods.QueryPerformanceCounter(out m_baseline) == 0)
            {
                m_baseline = m_offset;
                m_disabled = true;
            }
            m_ratio = ((decimal)TimeSpan.TicksPerSecond) / m_frequency;
#else
            m_offset = DateTime.UtcNow.Ticks;
            if (!Stopwatch.IsHighResolution)
            {
                m_frequency = TimeSpan.TicksPerSecond;
                m_baseline = m_offset;
                m_disabled = true;
            }
            else
            {
                m_baseline = Stopwatch.GetTimestamp();
                m_frequency = Stopwatch.Frequency;
            }
            m_ratio = ((decimal)TimeSpan.TicksPerSecond) / m_frequency;
#endif
            m_ticksPerMillisecond = m_frequency / 1000.0;
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
#if NETFRAMEWORK
            [DllImport("Kernel32.dll")]
            public static extern int QueryPerformanceFrequency(out long lpFrequency);

            [DllImport("Kernel32.dll")]
            public static extern int QueryPerformanceCounter(out long lpFrequency);
#endif
            [DllImport("kernel32.dll")]
            public static extern long GetTickCount64();
        }

        private long m_frequency;
        private long m_baseline;
        private long m_offset;
        private double m_ticksPerMillisecond;
        private decimal m_ratio;
#if !NETFRAMEWORK
        private bool m_isWindowsOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

        private bool m_disabled;
    }

}
