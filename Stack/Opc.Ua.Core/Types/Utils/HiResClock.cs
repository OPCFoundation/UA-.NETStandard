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
using System.Diagnostics;

namespace Opc.Ua
{
    /// <summary>
    /// Produces high resolution timestamps.
    /// </summary>
    public sealed class HiResClock
    {
        /// <summary>
        /// Returns the current UTC time with a high resolution.
        /// </summary>
        /// <remarks>
        /// This Utc time does not change if the system time is changed.
        /// It returns the Utc time elapsed since the application started.
        /// </remarks>
        public static DateTime UtcNow
        {
            get
            {
                if (s_default.m_disabled)
                {
                    return DateTime.UtcNow;
                }
                long counter = Stopwatch.GetTimestamp();
                decimal ticks = (counter - s_default.m_baseline) * s_default.m_ratio;
                return new DateTime((long)ticks + s_default.m_offset);
            }
        }

        /// <summary>
        /// Returns a monotonic increasing tick count in milliseconds.
        /// </summary>
        public static long TickCount64
            => (long)(s_default.m_ticksDelegate() / s_default.m_ticksPerMillisecond);

        /// <summary>
        /// Returns a monotonic increasing tick count based on the frequency of the underlying timer.
        /// </summary>
        public static long Ticks => s_default.m_ticksDelegate();

        /// <summary>
        /// Return the frequency of the ticks.
        /// </summary>
        public static long Frequency => s_default.m_frequency;

        /// <summary>
        /// Return the number of ticks per millisecond.
        /// </summary>
        public static double TicksPerMillisecond => s_default.m_ticksPerMillisecond;

        /// <summary>
        /// A monotonic tick count in milliseconds as a 32 bit signed number.
        /// It starts at 0 when the system starts.
        /// After 24.9 days it wraps around to negative numbers.
        /// It wraps around completely every 49.7 days.
        /// </summary>
        /// <remarks>
        /// It's resolution might not be the highest, typically it is based on a system timer @16ms.
        /// Use for relative time measurements which do not require a high resolution and
        /// which should be independent of the system time, which can be changed by a user.
        /// </remarks>
        public static int TickCount => Environment.TickCount;

        /// <summary>
        /// Returns the Utc time with respect to the current tick count.
        /// </summary>
        public static DateTime UtcTickCount(int tickCount)
        {
            DateTime utcNow = DateTime.UtcNow;
            int delta = tickCount - TickCount;
            return utcNow.AddMilliseconds(delta);
        }

        /// <summary>
        /// Disables the hires clock.
        /// </summary>
        public static bool Disabled
        {
            get => s_default.m_disabled;
            set
            {
                // do not enable if unsupported
                if (Stopwatch.IsHighResolution)
                {
                    // check if already initialized.
                    if (!s_default.m_initialized)
                    {
                        if (s_default.m_disabled != value)
                        {
                            // reset baseline
                            s_default = new HiResClock(value);
                        }
                        s_default.m_initialized = true;
                    }
                    else
                    {
                        // do not allow to set the value multiple times since
                        // it affects the notifications for existing monitored items.
                    }
                }
            }
        }

        /// <summary>
        /// Reset the baseline and allow a new initialization.
        /// </summary>
        public static void Reset()
        {
            // reset baseline
            s_default = new HiResClock(s_default.m_disabled);
        }

        /// <summary>
        /// Constructs a HiRes clock class.
        /// </summary>
        private HiResClock(bool disabled)
        {
            m_initialized = false;
            m_offset = DateTime.UtcNow.Ticks;
            if (!Stopwatch.IsHighResolution || disabled)
            {
                m_ticksDelegate = UtcNowTicks;
                m_frequency = TimeSpan.TicksPerSecond;
                m_ticksPerMillisecond = TimeSpan.TicksPerMillisecond;
                m_baseline = m_offset;
                m_disabled = true;
            }
            else
            {
                m_baseline = Stopwatch.GetTimestamp();
                m_ticksDelegate = Stopwatch.GetTimestamp;
                m_frequency = Stopwatch.Frequency;
                m_ticksPerMillisecond = m_frequency / 1000.0;
                m_disabled = false;
            }
            m_ratio = ((decimal)TimeSpan.TicksPerSecond) / m_frequency;
        }

        /// <summary>
        /// Helper for tick functions.
        /// </summary>
        private delegate long TicksDelegate();

        private long UtcNowTicks()
        {
            return DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Defines a global instance.
        /// </summary>
        private static HiResClock s_default = new(false);
        private readonly TicksDelegate m_ticksDelegate;
        private readonly long m_frequency;
        private readonly long m_baseline;
        private readonly long m_offset;
        private readonly double m_ticksPerMillisecond;
        private readonly decimal m_ratio;
        private readonly bool m_disabled;
        private bool m_initialized;
    }
}
