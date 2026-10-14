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

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// UNECE units used for standard extrusion dimensions.
    /// Arbitrary additional measuring devices require an explicit application-selected unit.
    /// </summary>
    public static class ExtrusionUnits
    {
        /// <summary>
        /// Gets degree Celsius (CEL).
        /// </summary>
        public static EUInformation Celsius => Create("CEL", "°C", "degree Celsius");

        /// <summary>
        /// Gets bar (BAR), explicitly permitted for melt and vacuum measurements by OPC 40084.
        /// </summary>
        public static EUInformation Bar => Create("BAR", "bar", "bar");

        /// <summary>
        /// Gets millimetre (MMT).
        /// </summary>
        public static EUInformation Millimetre => Create("MMT", "mm", "millimetre");

        /// <summary>
        /// Gets micrometre (4H).
        /// </summary>
        public static EUInformation Micrometre => Create("4H", "µm", "micrometre");

        /// <summary>
        /// Gets square centimetre (CMK).
        /// </summary>
        public static EUInformation SquareCentimetre => Create("CMK", "cm²", "square centimetre");

        /// <summary>
        /// Gets kilogram per hour (KGH).
        /// </summary>
        public static EUInformation KilogramPerHour => Create("KGH", "kg/h", "kilogram per hour");

        /// <summary>
        /// Gets metre per second (MTS).
        /// </summary>
        public static EUInformation MetrePerSecond => Create("MTS", "m/s", "metre per second");

        /// <summary>
        /// Gets kilogram (KGM).
        /// </summary>
        public static EUInformation Kilogram => Create("KGM", "kg", "kilogram");

        /// <summary>
        /// Gets percent (P1).
        /// </summary>
        public static EUInformation Percent => Create("P1", "%", "percent");

        /// <summary>
        /// Gets newton (NEW).
        /// </summary>
        public static EUInformation Newton => Create("NEW", "N", "newton");

        /// <summary>
        /// Gets kilowatt (KWT).
        /// </summary>
        public static EUInformation Kilowatt => Create("KWT", "kW", "kilowatt");

        /// <summary>
        /// Gets ampere (AMP).
        /// </summary>
        public static EUInformation Ampere => Create("AMP", "A", "ampere");

        /// <summary>
        /// Sets a concrete analog instance's unit without changing its value or source timestamp.
        /// </summary>
        public static void Configure(ISystemContext context, BaseAnalogState variable, EUInformation unit)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }
            variable.AddEngineeringUnits(context);
            variable.EngineeringUnits!.Value = unit;
        }

        /// <summary>
        /// Applies dimensions fixed by the selected part while leaving application-defined dimensions explicit.
        /// </summary>
        internal static void ApplyDefaults(ISystemContext context, NodeState root)
        {
            foreach ((string path, BaseInstanceState node) in ExtrusionNodeAccess.Walk(context, root))
            {
                if (node is not BaseAnalogState analog)
                {
                    continue;
                }
                EUInformation? unit = node.BrowseName.Name switch
                {
                    "FiltrationFineness" => Micrometre,
                    "Area" => SquareCentimetre,
                    "Weight" => Kilogram,
                    "NominalHeatingPower" or "NominalCoolingPower" => Kilowatt,
                    "ControllerOutput" => Percent,
                    "CutGap" or "Length" or "LengthCorrection" or "WallThickeningLength" or
                        "SampleCuttingLength" or "WasteCuttingLength" or "TotalWasteLength" => Millimetre,
                    "BackflushPressure" => Bar,
                    "Throughput" => KilogramPerHour,
                    "LineSpeed" => MetrePerSecond,
                    _ => MeasurementUnit(path)
                };
                if (unit != null)
                {
                    Configure(context, analog, unit);
                }
            }
        }

        /// <summary>
        /// Resolves dimensions of standardized monitored/measuring groups from their actual ancestor path.
        /// </summary>
        private static EUInformation? MeasurementUnit(string path)
        {
            if (path.Contains("ElectricalCurrent", StringComparison.Ordinal))
            {
                return Ampere;
            }
            if (path.Contains("SpecificWasteOutput", StringComparison.Ordinal))
            {
                return Percent;
            }
            if (path.Contains("Throughput", StringComparison.Ordinal) ||
                path.Contains("WasteOutput", StringComparison.Ordinal))
            {
                return KilogramPerHour;
            }
            if (path.Contains("Temperature", StringComparison.Ordinal))
            {
                return Celsius;
            }
            if (path.Contains("Pressure", StringComparison.Ordinal) || path.Contains("Vacuum",
                StringComparison.Ordinal))
            {
                return Bar;
            }
            if (path.Contains("Distance", StringComparison.Ordinal) ||
                path.Contains("Position", StringComparison.Ordinal) ||
                path.Contains("CrossAxis", StringComparison.Ordinal))
            {
                return Millimetre;
            }
            if (path.Contains("Force", StringComparison.Ordinal))
            {
                return Newton;
            }
            if (path.Contains("ProductSpeed", StringComparison.Ordinal))
            {
                return MetrePerSecond;
            }
            return null;
        }

        /// <summary>
        /// Encodes a UNECE common code according to OPC 10000-8, rather than inventing numeric unit identities.
        /// </summary>
        private static EUInformation Create(string code, string symbol, string description)
        {
            int id = 0;
            foreach (char character in code)
            {
                id = (id << 8) | character;
            }
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = id,
                DisplayName = new LocalizedText("en", symbol),
                Description = new LocalizedText("en", description)
            };
        }
    }
}
