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

namespace MachinerySample
{
    /// <summary>
    /// The published data of the simulated machine. The simulation and the
    /// address space both read it from here, so a value can never disagree
    /// between the two.
    /// </summary>
    internal static class PressDatasheet
    {
        public const string Manufacturer = "Acme Machine Tools";
        public const string ManufacturerUri = "urn:acme-machine-tools.example";
        public const string Model = "HydraPress 500";
        public const string ProductCode = "HP-500-EU";
        public const string SerialNumber = "HP500-2024-0001";
        public const string ProductInstanceUri =
            "urn:acme-machine-tools.example:hydrapress:HP500-2024-0001";
        public const string DeviceClass = "HydraulicPress";
        public const string HardwareRevision = "C";
        public const string SoftwareRevision = "4.2.1";
        public const string Location = "Hall 3 / Line 2";
        public const ushort YearOfConstruction = 2024;
        public const byte MonthOfConstruction = 6;

        /// <summary>Nominal press force, in kN.</summary>
        public const double NominalForceKiloNewton = 5000.0;

        /// <summary>Hydraulic oil temperature alarm limits, in °C.</summary>
        public const double OilTemperatureLowLimit = 15.0;
        public const double OilTemperatureHighLimit = 65.0;
        public const double OilTemperatureLowLowLimit = 5.0;
        public const double OilTemperatureHighHighLimit = 80.0;
        public const double OilTemperatureSetpoint = 45.0;

        /// <summary>Instrument range of the oil-temperature sensor, in °C.</summary>
        public const double OilTemperatureRangeLow = -20.0;
        public const double OilTemperatureRangeHigh = 120.0;

        /// <summary>
        /// Compressed-air supply pressure. OPC 40001-4 pins
        /// <c>3:Pressure</c> to pascal, so 6.2 bar is published as 620 000 Pa.
        /// </summary>
        public const float CompressedAirPressurePascal = 620_000.0f;

        /// <summary>
        /// Compressed-air supply temperature. OPC 40001-4 pins
        /// <c>3:Temperature</c> to kelvin, so 21.5 °C is published as
        /// 294.65 K.
        /// </summary>
        public const float CompressedAirTemperatureKelvin = 294.65f;

        /// <summary>Cumulative compressed-air volume, in m³.</summary>
        public const float CompressedAirVolume = 184_320.0f;

        /// <summary>Compressed-air volume flow rate, in m³/h.</summary>
        public const float CompressedAirVolumeFlowRate = 18.4f;

        /// <summary>Cumulative electrical energy drawn, in W·h.</summary>
        public const double ElectricalEnergyImportWattHours = 4_812_600.0;

        /// <summary>Total active power the press draws, in W.</summary>
        public const float ElectricalActivePowerTotal = 41_250.0f;

        /// <summary>Upper die lifetime, in press cycles.</summary>
        public const double DieLifetimeCycles = 250_000.0;
        public const double DieRemainingCycles = 118_400.0;
        public const double DieWarningCycles = 25_000.0;

        /// <summary>Hours the press has been powered on since commissioning.</summary>
        public const double PowerOnDurationHours = 12_480.0;

        /// <summary>Hours the press has been pressing since commissioning.</summary>
        public const double OperationDurationHours = 9_215.5;

        /// <summary>Seal lifetime, in press cycles.</summary>
        public const double SealLifetimeCycles = 2_000_000.0;
        public const double SealRemainingCycles = 1_412_500.0;
        public const double SealWarningCycles = 200_000.0;

        /// <summary>Nominal cycle time of one press stroke.</summary>
        public const double CycleSeconds = 4.0;
    }
}
