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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Injectable, synthetic display coefficients. These are not recovered producer defaults,
    /// calibration, or input to the authoritative material engine.
    /// </summary>
    public sealed record RecyclingSignalDefaults
    {
        /// <summary>
        /// Conveyor speed while a transfer is committed, metres per second.
        /// </summary>
        public double BeltSpeedMps { get; init; } = 1;

        /// <summary>
        /// Synthetic motor load while electrical demand is met, percent.
        /// </summary>
        public double RunningMotorLoadPct { get; init; } = 75;

        /// <summary>
        /// Nominal granulated flake size, millimetres; not a particle-size simulation.
        /// </summary>
        public double FlakeSizeMm { get; init; } = 8;

        /// <summary>
        /// Nominal pellet size, millimetres; not a particle-size simulation.
        /// </summary>
        public double PelletSizeMm { get; init; } = 3;

        /// <summary>
        /// Synthetic ambient/prewash temperature, degrees Celsius.
        /// </summary>
        public double AmbientTemperatureC { get; init; } = 20;

        /// <summary>
        /// Synthetic bath temperature when committed thermal demand is met, degrees Celsius.
        /// </summary>
        public double BathTemperatureC { get; init; } = 80;

        /// <summary>
        /// Synthetic melt temperature when committed thermal demand is met, degrees Celsius.
        /// </summary>
        public double MeltTemperatureC { get; init; } = 200;

        /// <summary>
        /// Synthetic working melt pressure, bar.
        /// </summary>
        public double MeltPressureBar { get; init; } = 120;

        /// <summary>
        /// NTU multiplier for the committed removed-dirt/added-water ratio.
        /// </summary>
        public double TurbidityMultiplier { get; init; } = 1000;

        /// <summary>
        /// Retained particle mass corresponding to full synthetic filter loading, kilograms.
        /// </summary>
        public double FilterCapacityKg { get; init; } = 1;

        /// <summary>
        /// Synthetic differential pressure of an unloaded filter, bar.
        /// </summary>
        public double CleanFilterPressureBar { get; init; } = 0.4;

        /// <summary>
        /// Additional differential pressure at full synthetic filter loading, bar.
        /// </summary>
        public double LoadedFilterPressureBar { get; init; } = 4;

        /// <summary>
        /// Rejects nonfinite or physically nonsensical display coefficients.
        /// </summary>
        public void Validate()
        {
            double[] positive =
            [
                BeltSpeedMps, FlakeSizeMm, PelletSizeMm, MeltPressureBar, TurbidityMultiplier,
                FilterCapacityKg, CleanFilterPressureBar, LoadedFilterPressureBar
            ];
            foreach (double value in positive)
            {
                if (!double.IsFinite(value) || value <= 0 || value > 1_000_000)
                {
                    throw new ArgumentException("Synthetic signal coefficients must be finite and positive.");
                }
            }
            if (!double.IsFinite(RunningMotorLoadPct) || RunningMotorLoadPct is < 0 or > 100 ||
                BeltSpeedMps > 100 || FlakeSizeMm > 1000 || PelletSizeMm > 1000 || MeltPressureBar > 1000 ||
                CleanFilterPressureBar + LoadedFilterPressureBar > 1000 ||
                !double.IsFinite(AmbientTemperatureC) || AmbientTemperatureC is < -50 or > 100 ||
                !double.IsFinite(BathTemperatureC) || BathTemperatureC < AmbientTemperatureC ||
                BathTemperatureC > 100 || !double.IsFinite(MeltTemperatureC) ||
                MeltTemperatureC < BathTemperatureC || MeltTemperatureC > 400)
            {
                throw new ArgumentException("Invalid synthetic load or temperature coefficients.");
            }
        }
    }
}
