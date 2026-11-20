/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Explicit sample-only equipment coefficients. These do not advance production and are not physical calibration.
    /// All live selection and residence progress comes from the committed coordinator image.
    /// </summary>
    public sealed record RecyclingNativeProcessOptions
    {
        /// <summary>
        /// Synthetic conditioned-bin and desiccant-bed temperature, degrees Celsius.
        /// </summary>
        public double DryingTemperatureC { get; init; } = 80;

        /// <summary>
        /// Synthetic powered dry-air supply, cubic metres per hour. Bed publication converts this to litres/second.
        /// </summary>
        public double DryingAirFlowM3Hr { get; init; } = 60;

        /// <summary>
        /// Synthetic conditioned-air dewpoint, degrees Celsius.
        /// </summary>
        public double DryDewPointC { get; init; } = -40;

        /// <summary>
        /// Synthetic unconditioned-air dewpoint, degrees Celsius.
        /// </summary>
        public double AmbientDewPointC { get; init; } = 10;

        /// <summary>
        /// Synthetic selected filter mesh specification, micrometres; not an inferred contamination measurement.
        /// </summary>
        public double FiltrationFinenessMicrometres { get; init; } = 100;

        /// <summary>
        /// Synthetic pelletizer shaft speed while material is actually executing, revolutions per minute.
        /// This is independent of mass throughput and pellet size.
        /// </summary>
        public double PelletizerSpeedRpm { get; init; } = 300;

        /// <summary>
        /// Validates finite display coefficients and their declared ranges.
        /// </summary>
        public void Validate()
        {
            if (!double.IsFinite(DryingTemperatureC) || DryingTemperatureC is < 20 or > 200 ||
                !double.IsFinite(DryingAirFlowM3Hr) || DryingAirFlowM3Hr is <= 0 or > 10000 ||
                !double.IsFinite(DryDewPointC) || DryDewPointC is < -100 or > 20 ||
                !double.IsFinite(AmbientDewPointC) || AmbientDewPointC < DryDewPointC || AmbientDewPointC > 50 ||
                !double.IsFinite(FiltrationFinenessMicrometres) ||
                FiltrationFinenessMicrometres is <= 0 or > 10000 ||
                !double.IsFinite(PelletizerSpeedRpm) || PelletizerSpeedRpm is <= 0 or > 10000)
            {
                throw new ArgumentException("Invalid synthetic native-equipment coefficients.");
            }
        }
    }
}
