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

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// The result of evaluating the raw load of a scale against its zero
    /// point, tare and weighing ranges.
    /// </summary>
    internal readonly record struct WeighingResult(
        double Gross,
        double Net,
        double Tare,
        double HighResolutionGross,
        double HighResolutionNet,
        double HighResolutionTare,
        bool Overload,
        bool Underload,
        bool InsideZero,
        bool CenterOfZero,
        bool GrossNegative,
        int RangeIndex,
        double Interval);

    /// <summary>
    /// The weighing arithmetic behind a <c>WeightItemType</c> (OPC 40200 §9.3)
    /// and the zero and tare methods (§7.4.4 - §7.4.8), free of any address
    /// space so it can be reasoned about and tested on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The application publishes the raw load. The gross weight is the load
    /// minus the zero offset <c>SetZero</c> established, the net weight the
    /// gross minus the tare. The weighing range is the first one whose upper
    /// limit is not exceeded; the published values are rounded to its
    /// verification scale interval <c>e</c> on a scale that is legal for
    /// trade and to its actual scale interval <c>d</c> otherwise, while the
    /// high-resolution values keep full precision.
    /// </para>
    /// <para>
    /// Overload is the gross weight above the largest range's capacity.
    /// Underload is the gross weight below zero by more than the zero-setting
    /// range - a scale that rests slightly below zero is in need of a zero,
    /// not underloaded.
    /// </para>
    /// </remarks>
    internal sealed class ScaleWeighingEngine
    {
        public ScaleWeighingEngine(
            IReadOnlyList<WeighingRangeDefinition> ranges,
            double zeroSettingRange)
        {
            if (ranges == null || ranges.Count == 0)
            {
                throw new ArgumentException(
                    "A scale needs at least one weighing range (OPC 40200 §7.4.2).",
                    nameof(ranges));
            }
            var sorted = new List<WeighingRangeDefinition>(ranges.Count);
            foreach (WeighingRangeDefinition range in ranges)
            {
                sorted.Add(range.Validate());
            }
            sorted.Sort((a, b) => a.High.CompareTo(b.High));
            m_ranges = sorted.ToArrayOf();
            m_zeroSettingRange = zeroSettingRange;
        }

        public ArrayOf<WeighingRangeDefinition> Ranges => m_ranges;

        public double Capacity => m_ranges[m_ranges.Count - 1].High;

        public double RawLoad { get; set; }

        public double ZeroOffset { get; private set; }

        public double Tare { get; private set; }

        public TareMode TareMode { get; private set; } = TareMode.None_0;

        public bool? Stable { get; set; }

        public bool LegalForTrade { get; set; }

        public WeighingResult Evaluate()
        {
            double gross = RawLoad - ZeroOffset;
            bool overload = gross > Capacity;
            double zeroBand = m_zeroSettingRange * Capacity;
            bool underload = gross < -zeroBand;

            int rangeIndex = m_ranges.Count - 1;
            for (int ii = 0; ii < m_ranges.Count; ii++)
            {
                if (gross <= m_ranges[ii].High)
                {
                    rangeIndex = ii;
                    break;
                }
            }
            WeighingRangeDefinition range = m_ranges[rangeIndex];
            double interval = LegalForTrade
                ? range.VerificationScaleInterval
                : range.ActualScaleInterval;

            double net = gross - Tare;
            return new WeighingResult(
                Gross: Round(gross, interval),
                Net: Round(net, interval),
                Tare: Round(Tare, interval),
                HighResolutionGross: gross,
                HighResolutionNet: net,
                HighResolutionTare: Tare,
                Overload: overload,
                Underload: underload,
                InsideZero: Math.Abs(gross) <= zeroBand,
                CenterOfZero: Math.Abs(gross) <= interval / 4,
                GrossNegative: gross < 0,
                RangeIndex: rangeIndex,
                Interval: interval);
        }

        /// <summary>
        /// Sets the zero point to the current gross weight (§7.4.4).
        /// </summary>
        public ServiceResult SetZero()
        {
            if (Stable == false)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "The weight is not stable; the zero point cannot be set.");
            }
            WeighingResult current = Evaluate();
            if (!current.InsideZero)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The gross weight {0} is outside the zero-setting range.",
                    current.HighResolutionGross);
            }
            ZeroOffset += current.HighResolutionGross;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Takes the current gross weight as the tare (§7.4.5).
        /// </summary>
        public ServiceResult SetTare()
        {
            if (Stable == false)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "The weight is not stable; the tare cannot be set.");
            }
            WeighingResult current = Evaluate();
            if (current.Overload || current.Underload)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "The scale is overloaded or underloaded; the tare cannot be set.");
            }
            if (current.HighResolutionGross < 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "A negative gross weight cannot be taken as tare.");
            }
            Tare = current.HighResolutionGross;
            TareMode = TareMode.MeasuredTare_1;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Clears the tare (§7.4.6).
        /// </summary>
        public void ClearTare()
        {
            Tare = 0;
            TareMode = TareMode.None_0;
        }

        /// <summary>
        /// Sets a preset tare given in the scale's unit (§7.4.7).
        /// </summary>
        public ServiceResult SetPresetTare(double presetTare)
        {
            if (double.IsNaN(presetTare) || double.IsInfinity(presetTare) ||
                presetTare < 0 || presetTare > Capacity)
            {
                return ServiceResult.Create(
                    StatusCodes.BadOutOfRange,
                    "The preset tare {0} is outside 0 .. {1}.",
                    presetTare,
                    Capacity);
            }
            Tare = presetTare;
            TareMode = TareMode.PresetTare_2;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks the current weight can be registered (§7.4.8): it has to be
        /// stable and within the weighing range.
        /// </summary>
        public ServiceResult CanRegister()
        {
            if (Stable == false)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "The weight is not stable and cannot be registered.");
            }
            WeighingResult current = Evaluate();
            if (current.Overload || current.Underload)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "The weight is outside the weighing range and cannot be registered.");
            }
            return ServiceResult.Good;
        }

        private static double Round(double value, double interval)
        {
            if (!(interval > 0))
            {
                return value;
            }
            double steps = Math.Round(value / interval, MidpointRounding.AwayFromZero);

            // steps * interval carries binary noise (0.1 becomes
            // 0.09999999999999999 for d = 1e-7); snapping to the decimal
            // places of the interval publishes the value the display shows.
            int decimals = (int)Math.Min(Math.Max(Math.Ceiling(-Math.Log10(interval)), 0), 15);
            return Math.Round(steps * interval, decimals, MidpointRounding.AwayFromZero);
        }

        private readonly ArrayOf<WeighingRangeDefinition> m_ranges;
        private readonly double m_zeroSettingRange;
    }
}
