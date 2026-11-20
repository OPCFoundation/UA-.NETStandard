// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    /// (<see cref="Ua.DeadbandType"/>).</param>
    /// <param name="DeadbandValue">Deadband magnitude. For
    /// <see cref="DeadbandType.Absolute"/> this is an absolute
    /// difference. For
    /// <see cref="DeadbandType.Percent"/> this is a percentage
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
            if (!previous.StatusCode.Equals(current.StatusCode, StatusCodeComparison.AllBits))
            {
                return true;
            }
            if (!previous.SourceTimestamp.Equals(current.SourceTimestamp) &&
                deadband.DeadbandType != DeadbandType.None &&
                deadband.DeadbandValue > 0 &&
                TryGetDouble(previous.Value, out double prev) &&
                TryGetDouble(current.Value, out double now))
            {
                return PassesNumeric(prev, now, deadband);
            }
            if (deadband.DeadbandType == DeadbandType.None ||
                deadband.DeadbandValue <= 0)
            {
                return !previous.Value.Equals(current.Value);
            }
            if (TryGetDouble(previous.Value, out double oldVal) &&
                TryGetDouble(current.Value, out double newVal))
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
                    if (deadband.EuRange > 0)
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
            try
            {
                result = value.ConvertToDouble().GetDouble();
                return true;
            }
            catch (Exception ex) when (
                ex is InvalidCastException
                    or FormatException
                    or OverflowException
                    or ServiceResultException)
            {
                result = 0;
                return false;
            }
        }
    }
}
