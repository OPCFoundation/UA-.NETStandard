/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for working with <see cref="TimeProvider"/> in monotonic duration math
    /// and for bridging legacy public APIs that expose <see cref="int"/> tick counts.
    /// </summary>
    /// <remarks>
    /// These helpers are intentionally <c>internal</c> because they exist purely to
    /// support the migration off <see cref="HiResClock"/>; new public APIs should
    /// consume <see cref="TimeProvider"/> directly via constructor injection.
    /// </remarks>
    internal static class TimeProviderExtensions
    {
        /// <summary>
        /// Returns the current monotonic timestamp normalised to milliseconds.
        /// </summary>
        /// <remarks>
        /// Equivalent to <see cref="HiResClock.TickCount64"/>, computed from
        /// <see cref="TimeProvider.GetTimestamp"/> and the provider's
        /// <see cref="TimeProvider.TimestampFrequency"/>.
        /// </remarks>
        public static long GetTimestampMilliseconds(this TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            long ticks = timeProvider.GetTimestamp();
            long frequency = timeProvider.TimestampFrequency;
            return ticks / (frequency / 1000L);
        }

        /// <summary>
        /// Returns a monotonic 32-bit tick count derived from the provider's
        /// millisecond timestamp.
        /// </summary>
        /// <remarks>
        /// Provided strictly for back-compatibility with public APIs that still
        /// expose <see cref="int"/> tick values (e.g.
        /// <c>Session.LastKeepAliveTickCount</c>). The value wraps every
        /// ~49.7 days, matching the semantics of <see cref="Environment.TickCount"/>.
        /// New code should store the underlying <see cref="long"/> timestamp instead.
        /// </remarks>
        public static int GetTickCount(this TimeProvider timeProvider)
        {
            return unchecked((int)timeProvider.GetTimestampMilliseconds());
        }

        /// <summary>
        /// Returns the elapsed time since the supplied start timestamp obtained
        /// from <see cref="TimeProvider.GetTimestamp"/>.
        /// </summary>
        public static TimeSpan GetElapsedTimeSince(
            this TimeProvider timeProvider,
            long startTimestamp)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return timeProvider.GetElapsedTime(startTimestamp);
        }
    }
}
