// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Non-throwing <see cref="TimeSpan"/> conversions for values revised by
    /// the server. A server may negotiate durations and counts up to the
    /// limits of their wire types (Part 4 §5.14.2.2), and products such as
    /// publishing interval x keep-alive count can exceed
    /// <see cref="TimeSpan.MaxValue"/>. The throwing framework APIs would then
    /// abort processing after the service call already succeeded, so these
    /// helpers saturate instead.
    /// </summary>
    internal static class SaturatingTimeSpan
    {
        /// <summary>
        /// Convert a duration in milliseconds, saturating at
        /// <see cref="TimeSpan.MaxValue"/>. Negative values map to
        /// <see cref="TimeSpan.Zero"/> and NaN to <paramref name="fallback"/>.
        /// </summary>
        /// <param name="milliseconds">The duration in milliseconds.</param>
        /// <param name="fallback">The value to use for NaN.</param>
        public static TimeSpan FromMilliseconds(double milliseconds, TimeSpan fallback)
        {
            if (double.IsNaN(milliseconds))
            {
                return fallback;
            }
            if (milliseconds <= 0)
            {
                return TimeSpan.Zero;
            }
            if (milliseconds >= kMaxMilliseconds)
            {
                return TimeSpan.MaxValue;
            }
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        /// <summary>
        /// Multiply a duration, saturating at <see cref="TimeSpan.MaxValue"/>
        /// and <see cref="TimeSpan.MinValue"/>.
        /// </summary>
        /// <param name="value">The duration.</param>
        /// <param name="factor">The factor.</param>
        public static TimeSpan Multiply(TimeSpan value, double factor)
        {
            double ticks = value.Ticks * factor;
            if (double.IsNaN(ticks))
            {
                return TimeSpan.Zero;
            }
            if (ticks >= long.MaxValue)
            {
                return TimeSpan.MaxValue;
            }
            if (ticks <= long.MinValue)
            {
                return TimeSpan.MinValue;
            }
            return TimeSpan.FromTicks((long)ticks);
        }

        /// <summary>
        /// Convert a whole number of hours, saturating at
        /// <see cref="TimeSpan.MaxValue"/>.
        /// </summary>
        /// <param name="hours">The number of hours.</param>
        public static TimeSpan FromHours(uint hours)
        {
            return hours >= kMaxHours ? TimeSpan.MaxValue : TimeSpan.FromHours(hours);
        }

        /// <summary>
        /// Whole milliseconds that still convert without overflow on every
        /// target framework (net48 rounds to the millisecond first).
        /// </summary>
        private const double kMaxMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

        private const uint kMaxHours = (uint)(long.MaxValue / TimeSpan.TicksPerHour);
    }
}
