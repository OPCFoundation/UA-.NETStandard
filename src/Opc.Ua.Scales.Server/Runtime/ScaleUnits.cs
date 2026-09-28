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
    /// The mass units a scale commonly publishes and accepts, in UNECE
    /// Recommendation 20 codes as OPC 10000-8 encodes them into
    /// <see cref="EUInformation"/>.
    /// </summary>
    public static class ScaleUnits
    {
        /// <summary>The UNECE namespace URI of <see cref="EUInformation.NamespaceUri"/>.</summary>
        public const string UneceNamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact";

        /// <summary>
        /// Kilogram (KGM), the SI base unit of mass.
        /// </summary>
        public static EUInformation Kilogram { get; } = Create("KGM", "kg", "kilogram");

        /// <summary>
        /// Gram (GRM).
        /// </summary>
        public static EUInformation Gram { get; } = Create("GRM", "g", "gram");

        /// <summary>
        /// Milligram (MGM).
        /// </summary>
        public static EUInformation Milligram { get; } = Create("MGM", "mg", "milligram");

        /// <summary>
        /// Tonne (TNE), a non-SI unit accepted for use with the SI.
        /// </summary>
        public static EUInformation Tonne { get; } = Create("TNE", "t", "tonne");

        /// <summary>
        /// Pound (LBR), an imperial unit.
        /// </summary>
        public static EUInformation Pound { get; } = Create("LBR", "lb", "pound");

        /// <summary>
        /// Ounce (ONZ), an imperial unit.
        /// </summary>
        public static EUInformation Ounce { get; } = Create("ONZ", "oz", "ounce");

        /// <summary>
        /// Percent (P1).
        /// </summary>
        public static EUInformation Percent { get; } = Create("P1", "%", "percent");

        /// <summary>
        /// Encodes a UNECE common code into the <c>UnitId</c> OPC 10000-8
        /// §5.6.3 prescribes: the code's characters packed big-endian into an
        /// Int32.
        /// </summary>
        /// <param name="commonCode">The one to three character code.</param>
        public static int UnitIdOf(string commonCode)
        {
            if (string.IsNullOrEmpty(commonCode) || commonCode.Length > 3)
            {
                throw new ArgumentException(
                    "A UNECE common code has one to three characters.",
                    nameof(commonCode));
            }
            int unitId = 0;
            foreach (char c in commonCode)
            {
                unitId = (unitId << 8) | c;
            }
            return unitId;
        }

        /// <summary>
        /// Creates a UNECE engineering unit.
        /// </summary>
        /// <param name="commonCode">The UNECE common code.</param>
        /// <param name="symbol">The display name, the unit symbol.</param>
        /// <param name="name">The description, the unit name.</param>
        public static EUInformation Create(string commonCode, string symbol, string name)
        {
            return new EUInformation
            {
                NamespaceUri = UneceNamespaceUri,
                UnitId = UnitIdOf(commonCode),
                DisplayName = new LocalizedText("en", symbol),
                Description = new LocalizedText("en", name)
            };
        }

        /// <summary>
        /// Compares two engineering units by identity - namespace and unit
        /// id - ignoring the display texts.
        /// </summary>
        /// <param name="left">The first unit.</param>
        /// <param name="right">The second unit.</param>
        public static bool SameUnit(EUInformation? left, EUInformation? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }
            return left.UnitId == right.UnitId &&
                string.Equals(
                    left.NamespaceUri ?? string.Empty,
                    right.NamespaceUri ?? string.Empty,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether a unit is an SI mass unit: the kilogram or a decimal
        /// multiple or submultiple of the gram.
        /// </summary>
        /// <param name="unit">The unit.</param>
        public static bool IsSiMass(EUInformation? unit)
        {
            return unit != null && s_siMass.Contains(unit.UnitId) &&
                string.Equals(unit.NamespaceUri, UneceNamespaceUri, StringComparison.Ordinal);
        }

        /// <summary>
        /// Converts a mass between two units, when both are known mass units.
        /// </summary>
        /// <param name="value">The value in <paramref name="from"/>.</param>
        /// <param name="from">The unit of <paramref name="value"/>.</param>
        /// <param name="to">The unit to convert to.</param>
        /// <param name="converted">The converted value.</param>
        /// <returns>
        /// <see langword="true"/> when the conversion is known. Identical
        /// units always convert.
        /// </returns>
        public static bool TryConvertMass(
            double value,
            EUInformation? from,
            EUInformation? to,
            out double converted)
        {
            if (SameUnit(from, to))
            {
                converted = value;
                return true;
            }
            if (from != null && to != null &&
                s_kilogramsPer.TryGetValue(from.UnitId, out double fromFactor) &&
                s_kilogramsPer.TryGetValue(to.UnitId, out double toFactor))
            {
                converted = value * fromFactor / toFactor;
                return true;
            }
            converted = double.NaN;
            return false;
        }

        private static readonly HashSet<int> s_siMass =
        [
            UnitIdOf("KGM"),
            UnitIdOf("GRM"),
            UnitIdOf("MGM"),
            UnitIdOf("MC")
        ];

        private static readonly Dictionary<int, double> s_kilogramsPer = new()
        {
            [UnitIdOf("KGM")] = 1.0,
            [UnitIdOf("GRM")] = 1e-3,
            [UnitIdOf("MGM")] = 1e-6,
            [UnitIdOf("MC")] = 1e-9,
            [UnitIdOf("TNE")] = 1e3,
            [UnitIdOf("LBR")] = 0.45359237,
            [UnitIdOf("ONZ")] = 0.028349523125
        };
    }
}
