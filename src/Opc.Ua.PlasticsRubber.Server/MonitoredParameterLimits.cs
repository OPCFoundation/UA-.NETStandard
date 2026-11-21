// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Specifies absolute limits and signed offsets around a monitored setpoint.
    /// </summary>
    public sealed record MonitoredParameterLimits
    {
        /// <summary>
        /// Gets the reference value for the relative tolerance bands.
        /// </summary>
        public double? SetValue { get; init; }

        /// <summary>
        /// Gets the absolute lower alarm boundary.
        /// </summary>
        public double? Minimum { get; init; }

        /// <summary>
        /// Gets the absolute upper alarm boundary.
        /// </summary>
        public double? Maximum { get; init; }

        /// <summary>
        /// Gets the first nonpositive lower offset from the setpoint.
        /// </summary>
        public double? LowerTolerance { get; init; }

        /// <summary>
        /// Gets the first nonnegative upper offset from the setpoint.
        /// </summary>
        public double? UpperTolerance { get; init; }

        /// <summary>
        /// Gets the outer nonpositive lower offset from the setpoint.
        /// </summary>
        public double? LowerTolerance2 { get; init; }

        /// <summary>
        /// Gets the outer nonnegative upper offset from the setpoint.
        /// </summary>
        public double? UpperTolerance2 { get; init; }

        /// <summary>
        /// Validates the selected limits without introducing absent limits or clamping them.
        /// </summary>
        public MonitoredParameterLimits Validate()
        {
            RequireFinite(SetValue, nameof(SetValue));
            RequireFinite(Minimum, nameof(Minimum));
            RequireFinite(Maximum, nameof(Maximum));
            RequireOffset(LowerTolerance, nameof(LowerTolerance), lower: true);
            RequireOffset(UpperTolerance, nameof(UpperTolerance), lower: false);
            RequireOffset(LowerTolerance2, nameof(LowerTolerance2), lower: true);
            RequireOffset(UpperTolerance2, nameof(UpperTolerance2), lower: false);
            if (Minimum > Maximum)
            {
                throw new ArgumentException("The minimum must not exceed the maximum.");
            }
            if (!SetValue.HasValue &&
                (LowerTolerance.HasValue || UpperTolerance.HasValue ||
                    LowerTolerance2.HasValue || UpperTolerance2.HasValue))
            {
                throw new ArgumentException("Relative tolerances require a setpoint.");
            }
            if (LowerTolerance2 > LowerTolerance || UpperTolerance2 < UpperTolerance)
            {
                throw new ArgumentException("The second tolerance band must contain the first.");
            }
            return this;
        }

        /// <summary>
        /// Evaluates the OPC 40083 UInt16 status without rounding or changing the measurement.
        /// </summary>
        /// <remarks>
        /// Exact boundaries are within their limit. Absolute alarms take precedence over
        /// relative warnings. Suppressing an alarm does not change this measured status.
        /// </remarks>
        public ushort Evaluate(double actualValue, StatusCode quality)
        {
            Validate();
            if (!StatusCode.IsGood(quality) || double.IsNaN(actualValue) || double.IsInfinity(actualValue))
            {
                return 1;
            }
            if (Minimum.HasValue && actualValue < Minimum.Value)
            {
                return 2;
            }
            if (Maximum.HasValue && actualValue > Maximum.Value)
            {
                return 8;
            }
            if (SetValue.HasValue)
            {
                double deviation = actualValue - SetValue.Value;
                if (LowerTolerance2.HasValue && deviation < LowerTolerance2.Value)
                {
                    return 3;
                }
                if (LowerTolerance.HasValue && deviation < LowerTolerance.Value)
                {
                    return 4;
                }
                if (UpperTolerance2.HasValue && deviation > UpperTolerance2.Value)
                {
                    return 7;
                }
                if (UpperTolerance.HasValue && deviation > UpperTolerance.Value)
                {
                    return 6;
                }
            }
            return Minimum.HasValue || Maximum.HasValue || LowerTolerance.HasValue ||
                UpperTolerance.HasValue || LowerTolerance2.HasValue || UpperTolerance2.HasValue
                ? (ushort)5
                : (ushort)0;
        }

        /// <summary>
        /// Rejects nonfinite configuration values.
        /// </summary>
        private static void RequireFinite(double? value, string name)
        {
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
            {
                throw new ArgumentOutOfRangeException(name, "A finite value is required.");
            }
        }

        /// <summary>
        /// Rejects invalid relative offsets.
        /// </summary>
        private static void RequireOffset(double? value, string name, bool lower)
        {
            RequireFinite(value, name);
            if (lower ? value > 0 : value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Lower tolerances must be nonpositive; upper tolerances must be nonnegative.");
            }
        }
    }
}
