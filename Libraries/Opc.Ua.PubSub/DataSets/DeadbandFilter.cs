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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Per-field deadband descriptor consumed by the publisher when
    /// deciding whether a sampled value differs sufficiently from the
    /// previously published value to warrant a new delta-frame entry.
    /// </summary>
    /// <param name="DeadbandType">Deadband mode
    /// (<see cref="Opc.Ua.DeadbandType"/>).</param>
    /// <param name="DeadbandValue">Deadband magnitude. For
    /// <see cref="Opc.Ua.DeadbandType.Absolute"/> this is an absolute
    /// difference. For
    /// <see cref="Opc.Ua.DeadbandType.Percent"/> this is a percentage
    /// (0..100) of the engineering-unit range when one is supplied
    /// via <paramref name="EuRange"/>, otherwise it is interpreted as
    /// a percentage of the previous value's magnitude.</param>
    /// <param name="EuRange">Optional engineering-unit range used to
    /// scale percent deadband. Pass <see langword="null"/> when
    /// unknown.</param>
    public readonly record struct DeadbandDescriptor(
        DeadbandType DeadbandType,
        double DeadbandValue,
        double? EuRange);

    /// <summary>
    /// Applies per-field deadband checks when constructing
    /// publisher delta-frames. A change passes the filter (and must
    /// therefore be included in the next delta-frame) when:
    /// <list type="bullet">
    /// <item>The two values differ in status, type, source-timestamp
    /// or any non-numeric scalar payload.</item>
    /// <item>The two values are numeric and the magnitude of the
    /// difference exceeds the configured deadband threshold (per
    /// <see cref="DeadbandDescriptor"/>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Implements the publisher deadband rules described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.11">
    /// Part 14 §6.2.11 DataSetWriter</see>
    /// and §5.3.2 DataSetMetaData /
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-4/v1.05.06/7.22">
    /// Part 4 §7.22 MonitoringFilter</see>.
    /// </remarks>
    public static class DeadbandFilter
    {
        /// <summary>
        /// Returns <see langword="true"/> when the change between
        /// <paramref name="previous"/> and <paramref name="current"/>
        /// passes the configured deadband and must be included in
        /// the next delta-frame. <see langword="false"/> means the
        /// change is below threshold and should be suppressed.
        /// </summary>
        /// <param name="previous">Previously published field.</param>
        /// <param name="current">Newly sampled field.</param>
        /// <param name="deadband">Per-field deadband descriptor.</param>
        public static bool PassesFilter(
            DataSetField previous,
            DataSetField current,
            DeadbandDescriptor deadband)
        {
            if (previous is null)
            {
                return current is not null;
            }
            if (current is null)
            {
                return true;
            }
            if (!previous.StatusCode.Equals(current.StatusCode))
            {
                return true;
            }
            if (!previous.SourceTimestamp.Equals(current.SourceTimestamp)
                && deadband.DeadbandType != DeadbandType.None
                && deadband.DeadbandValue > 0)
            {
                if (TryGetDouble(previous.Value, out double prev)
                    && TryGetDouble(current.Value, out double now))
                {
                    return PassesNumeric(prev, now, deadband);
                }
            }
            if (deadband.DeadbandType == DeadbandType.None
                || deadband.DeadbandValue <= 0)
            {
                return !previous.Value.Equals(current.Value);
            }
            if (TryGetDouble(previous.Value, out double oldVal)
                && TryGetDouble(current.Value, out double newVal))
            {
                return PassesNumeric(oldVal, newVal, deadband);
            }
            return !previous.Value.Equals(current.Value);
        }

        private static bool PassesNumeric(
            double previous, double current, DeadbandDescriptor deadband)
        {
            double diff = Math.Abs(current - previous);
            switch (deadband.DeadbandType)
            {
                case DeadbandType.Absolute:
                    return diff > deadband.DeadbandValue;
                case DeadbandType.Percent:
                    double scale;
                    if (deadband.EuRange.HasValue && deadband.EuRange.Value > 0)
                    {
                        scale = deadband.EuRange.Value;
                    }
                    else
                    {
                        scale = Math.Abs(previous);
                        if (scale == 0)
                        {
                            return diff > 0;
                        }
                    }
                    double threshold = scale * deadband.DeadbandValue / 100.0;
                    return diff > threshold;
                default:
                    return diff > 0;
            }
        }

        private static bool TryGetDouble(Variant value, out double result)
        {
            if (value.TryGetValue(out double dbl))
            {
                result = dbl;
                return true;
            }
            if (value.TryGetValue(out float f))
            {
                result = f;
                return true;
            }
            if (value.TryGetValue(out int i32))
            {
                result = i32;
                return true;
            }
            if (value.TryGetValue(out uint u32))
            {
                result = u32;
                return true;
            }
            if (value.TryGetValue(out long i64))
            {
                result = i64;
                return true;
            }
            if (value.TryGetValue(out ulong u64))
            {
                result = u64;
                return true;
            }
            if (value.TryGetValue(out short i16))
            {
                result = i16;
                return true;
            }
            if (value.TryGetValue(out ushort u16))
            {
                result = u16;
                return true;
            }
            if (value.TryGetValue(out sbyte i8))
            {
                result = i8;
                return true;
            }
            if (value.TryGetValue(out byte u8))
            {
                result = u8;
                return true;
            }
            result = 0;
            return false;
        }
    }
}
