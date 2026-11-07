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
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Formats process CPU time and reads its unrounded tick count when present.
    /// </summary>
    internal static class ProcessCpuReport
    {
        /// <summary>
        /// Reports the compatibility millisecond field and the captured TimeSpan ticks.
        /// </summary>
        internal static string Format(TimeSpan cpu)
        {
            if (cpu.Ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cpu));
            }

            return FormattableString.Invariant($"cpuMs={cpu.TotalMilliseconds:F0} cpuTicks={cpu.Ticks}");
        }

        /// <summary>
        /// Reads CPU time from ticks, or from the rounded millisecond field in older reports.
        /// </summary>
        internal static double GetMilliseconds(IReadOnlyDictionary<string, string> fields)
        {
            if (fields is null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            double milliseconds = fields.TryGetValue("cpuTicks", out string? ticks)
                ? long.Parse(ticks, NumberStyles.None, CultureInfo.InvariantCulture) /
                    (double)TimeSpan.TicksPerMillisecond
                : double.Parse(fields["cpuMs"], CultureInfo.InvariantCulture);
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
            {
                throw new FormatException("Process CPU time must be finite and nonnegative.");
            }
            return milliseconds;
        }

        /// <summary>
        /// Formats CPU microseconds per measured operation.
        /// </summary>
        internal static string FormatPerOperation(double milliseconds, double operations)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            }
            if (double.IsNaN(operations) || double.IsInfinity(operations) || operations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(operations));
            }

            double microseconds = milliseconds / operations * 1000;
            if (double.IsInfinity(microseconds))
            {
                throw new OverflowException("CPU time per operation is too large to represent.");
            }
            return microseconds.ToString("F6", CultureInfo.InvariantCulture);
        }
    }
}
