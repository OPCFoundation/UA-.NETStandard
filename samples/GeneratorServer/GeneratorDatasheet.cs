/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Generators
{
    /// <summary>
    /// The GenX-500 product datasheet as compile-time constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single source of truth shared by <c>DATASHEET.md</c>, the
    /// address space (nameplate, engineering ranges, trip points), the simulation
    /// and the conformance tests. Changing a value here moves the published model,
    /// the simulated behaviour and the OpenUSD gauges together, and
    /// <c>GeneratorDatasheetConformanceTests</c> fails if the document and these
    /// constants ever disagree.
    /// </para>
    /// <para>
    /// The machine is fictitious. Its parameters are ordinary engineering values
    /// for a mid-size industrial diesel generating set - a 50 Hz, four-pole,
    /// 1500 min-1 set in the 400 kW prime class on a turbocharged and aftercooled
    /// inline-six - chosen so the simulation is physically self-consistent rather
    /// than to represent any particular product.
    /// </para>
    /// <para>
    /// Units here are the datasheet's own engineering units (kW, bar, degrees
    /// Celsius, litres per hour). OPC UA publishes SI, so the node manager
    /// converts on the way out; see <see cref="Convert"/>.
    /// </para>
    /// </remarks>
    internal static class GeneratorDatasheet
    {
        /// <summary>
        /// Nameplate identity.
        /// </summary>
        public static class Identity
        {
            /// <summary>
            /// Manufacturer name carried by the DI and Machinery nameplates.
            /// </summary>
            public const string Manufacturer = "SimGen Systems";

            /// <summary>
            /// Marketing model designation.
            /// </summary>
            public const string Model = "GenX-500";

            /// <summary>
            /// Manufacturer's product page, used as ManufacturerUri.
            /// </summary>
            public const string ManufacturerUri = "https://simgen.example.com";

            /// <summary>
            /// Orderable product code.
            /// </summary>
            public const string ProductCode = "GX500-400-50-4W";

            /// <summary>
            /// Hardware revision of the skid and driveline.
            /// </summary>
            public const string HardwareRevision = "3.1";

            /// <summary>
            /// Controller firmware revision.
            /// </summary>
            public const string SoftwareRevision = "5.4.2";

            /// <summary>
            /// Combined device revision reported through DI.
            /// </summary>
            public const string DeviceRevision = "3.1/5.4.2";

            /// <summary>
            /// Device class reported through the Machinery identification add-in.
            /// </summary>
            public const string DeviceClass = "GeneratingSet";

            /// <summary>
            /// Year the set left the factory.
            /// </summary>
            public const ushort YearOfConstruction = 2025;

            /// <summary>
            /// Month the set left the factory.
            /// </summary>
            public const byte MonthOfConstruction = 4;
        }

        /// <summary>
        /// ISO 8528 duty ratings.
        /// </summary>
        public static class Ratings
        {
            /// <summary>
            /// Prime power (PRP): unlimited hours at variable load, in watts.
            /// </summary>
            public const double PrimePowerWatts = 400_000.0;

            /// <summary>
            /// Prime apparent power, in volt-amperes.
            /// </summary>
            public const double PrimeApparentPowerVoltAmperes =
                PrimePowerWatts / Electrical.RatedPowerFactor;

            /// <summary>
            /// Standby power (ESP): emergency use at varying load, in watts.
            /// </summary>
            public const double StandbyPowerWatts = 440_000.0;

            /// <summary>
            /// Standby apparent power, in volt-amperes.
            /// </summary>
            public const double StandbyApparentPowerVoltAmperes =
                StandbyPowerWatts / Electrical.RatedPowerFactor;

            /// <summary>
            /// Reference ambient temperature the ratings assume, in degrees Celsius.
            /// </summary>
            public const double ReferenceAmbientCelsius = 25.0;

            /// <summary>
            /// Reference site altitude the ratings assume, in metres.
            /// </summary>
            public const double ReferenceAltitudeMetres = 150.0;
        }

        /// <summary>
        /// Alternator and electrical system.
        /// </summary>
        public static class Electrical
        {
            /// <summary>
            /// Nominal line-to-line voltage, in volts.
            /// </summary>
            public const double RatedLineVoltage = 400.0;

            /// <summary>
            /// Nominal line-to-neutral voltage, in volts.
            /// </summary>
            public const double RatedPhaseVoltage = 230.0;

            /// <summary>
            /// Rated power factor, lagging.
            /// </summary>
            public const double RatedPowerFactor = 0.8;

            /// <summary>
            /// Nominal output frequency, in hertz.
            /// </summary>
            public const double RatedFrequency = 50.0;

            /// <summary>
            /// Number of alternator poles.
            /// </summary>
            public const int Poles = 4;

            /// <summary>
            /// Number of phases.
            /// </summary>
            public const int Phases = 3;

            /// <summary>
            /// Rated line current at prime power and rated power factor, in amperes.
            /// </summary>
            /// <remarks>
            /// I = S / (sqrt(3) * V_LL). Computed rather than tabulated so it can
            /// never drift away from the rating it is derived from.
            /// </remarks>
            public static double RatedCurrentAmperes =>
                Ratings.PrimeApparentPowerVoltAmperes / (Math.Sqrt(3.0) * RatedLineVoltage);
        }

        /// <summary>
        /// Engine mechanical data.
        /// </summary>
        public static class Engine
        {
            /// <summary>
            /// Rated engine speed, in revolutions per minute.
            /// </summary>
            /// <remarks>
            /// Fixed by the pole count and the output frequency:
            /// N = 120 * f / p = 120 * 50 / 4.
            /// </remarks>
            public const double RatedSpeedRpm =
                120.0 * Electrical.RatedFrequency / Electrical.Poles;

            /// <summary>
            /// Number of cylinders.
            /// </summary>
            public const int Cylinders = 6;

            /// <summary>
            /// Swept volume, in litres.
            /// </summary>
            public const double DisplacementLitres = 12.5;

            /// <summary>
            /// Cylinder bore, in millimetres.
            /// </summary>
            public const double BoreMillimetres = 130.0;

            /// <summary>
            /// Piston stroke, in millimetres.
            /// </summary>
            public const double StrokeMillimetres = 157.0;

            /// <summary>
            /// Compression ratio.
            /// </summary>
            public const double CompressionRatio = 16.5;

            /// <summary>
            /// Oil pressure at rated speed and normal temperature, in bar.
            /// </summary>
            public const double RatedOilPressureBar = 4.8;

            /// <summary>
            /// Coolant thermostat opening temperature, in degrees Celsius.
            /// </summary>
            public const double ThermostatCelsius = 82.0;
        }

        /// <summary>
        /// Fuel system and consumption.
        /// </summary>
        public static class Fuel
        {
            /// <summary>
            /// Usable capacity of the base tank, in litres.
            /// </summary>
            public const double TankCapacityLitres = 1000.0;

            /// <summary>
            /// Density of the reference diesel fuel, in kilograms per litre.
            /// </summary>
            public const double DensityKilogramsPerLitre = 0.832;

            /// <summary>
            /// Lower heating value of the reference diesel fuel, in megajoules per kilogram.
            /// </summary>
            public const double LowerHeatingValueMegajoulesPerKilogram = 42.7;

            /// <summary>
            /// Usable energy per litre of fuel, in kilowatt-hours.
            /// </summary>
            /// <remarks>
            /// (MJ/kg * kg/L) / 3.6 MJ per kWh.
            /// </remarks>
            public const double EnergyPerLitreKilowattHours =
                LowerHeatingValueMegajoulesPerKilogram * DensityKilogramsPerLitre / 3.6;
        }

        /// <summary>
        /// Physical envelope.
        /// </summary>
        public static class Dimensions
        {
            /// <summary>
            /// Overall length of the skid, in metres.
            /// </summary>
            public const double LengthMetres = 4.00;

            /// <summary>
            /// Overall width of the skid, in metres.
            /// </summary>
            public const double WidthMetres = 1.50;

            /// <summary>
            /// Overall height of the set, in metres.
            /// </summary>
            public const double HeightMetres = 2.20;

            /// <summary>
            /// Dry mass, in kilograms.
            /// </summary>
            public const double DryMassKilograms = 4500.0;

            /// <summary>
            /// Sound pressure level at one metre with the enclosure fitted, in decibels A.
            /// </summary>
            public const double SoundPressureDecibelsA = 75.0;
        }

        /// <summary>
        /// The published characteristic curves.
        /// </summary>
        /// <remarks>
        /// Load fraction is the only independent variable in the simulation.
        /// Everything the set reports follows from these curves, which is what
        /// keeps the published values mutually consistent at every tick.
        /// </remarks>
        public static class Curves
        {
            /// <summary>
            /// No-load intercept of the fuel-consumption curve, in litres per hour.
            /// </summary>
            public const double FuelNoLoadLitresPerHour = 3.67;

            /// <summary>
            /// Slope of the fuel-consumption curve, in litres per hour at full load.
            /// </summary>
            public const double FuelSlopeLitresPerHour = 100.0;

            /// <summary>
            /// Coolant temperature rise from thermostat to full load, in kelvin.
            /// </summary>
            public const double CoolantRiseKelvin = 13.0;

            /// <summary>
            /// Exhaust temperature at no load, in degrees Celsius.
            /// </summary>
            public const double ExhaustNoLoadCelsius = 250.0;

            /// <summary>
            /// Exhaust temperature rise from no load to full load, in kelvin.
            /// </summary>
            public const double ExhaustRiseKelvin = 300.0;

            /// <summary>
            /// Fuel consumption at a given load fraction, in litres per hour.
            /// </summary>
            /// <param name="loadFraction">
            /// Electrical output as a fraction of prime power.
            /// </param>
            /// <returns>
            /// Volumetric fuel rate in litres per hour.
            /// </returns>
            public static double FuelLitresPerHour(double loadFraction)
            {
                return FuelNoLoadLitresPerHour + (FuelSlopeLitresPerHour * loadFraction);
            }

            /// <summary>
            /// Electrical efficiency at a given load fraction, in percent.
            /// </summary>
            /// <param name="loadFraction">
            /// Electrical output as a fraction of prime power.
            /// </param>
            /// <returns>
            /// Fuel-to-electricity conversion efficiency in percent, or zero when the
            /// set is not producing power.
            /// </returns>
            /// <remarks>
            /// The identity that makes the model self-consistent:
            /// eta = P / (Vdot * rho * LHV). Because both P and Vdot are functions of
            /// the same load fraction, the published efficiency always reconciles with
            /// the published power and fuel rate.
            /// </remarks>
            public static double EfficiencyPercent(double loadFraction)
            {
                if (loadFraction <= 0.0)
                {
                    return 0.0;
                }
                double outputKilowatts = loadFraction * Ratings.PrimePowerWatts / 1000.0;
                double inputKilowatts =
                    FuelLitresPerHour(loadFraction) * Fuel.EnergyPerLitreKilowattHours;
                return 100.0 * outputKilowatts / inputKilowatts;
            }

            /// <summary>
            /// Coolant temperature at a given load fraction, in degrees Celsius.
            /// </summary>
            /// <param name="loadFraction">
            /// Electrical output as a fraction of prime power.
            /// </param>
            /// <returns>
            /// Jacket-water temperature in degrees Celsius.
            /// </returns>
            public static double CoolantCelsius(double loadFraction)
            {
                return Engine.ThermostatCelsius + (CoolantRiseKelvin * loadFraction);
            }

            /// <summary>
            /// Exhaust gas temperature at a given load fraction, in degrees Celsius.
            /// </summary>
            /// <param name="loadFraction">
            /// Electrical output as a fraction of prime power.
            /// </param>
            /// <returns>
            /// Exhaust temperature in degrees Celsius.
            /// </returns>
            public static double ExhaustCelsius(double loadFraction)
            {
                return ExhaustNoLoadCelsius + (ExhaustRiseKelvin * loadFraction);
            }
        }

        /// <summary>
        /// Protection trip points.
        /// </summary>
        public static class TripPoints
        {
            /// <summary>
            /// Low oil pressure shutdown, in bar.
            /// </summary>
            public const double LowOilPressureBar = 1.7;

            /// <summary>
            /// High coolant temperature shutdown, in degrees Celsius.
            /// </summary>
            public const double HighCoolantCelsius = 98.0;

            /// <summary>
            /// Overspeed shutdown, in revolutions per minute.
            /// </summary>
            public const double OverspeedRpm = 1.15 * Engine.RatedSpeedRpm;

            /// <summary>
            /// Overload alarm, as a fraction of prime power.
            /// </summary>
            public const double OverloadFraction = 1.10;

            /// <summary>
            /// Low fuel level warning, in percent of tank capacity.
            /// </summary>
            public const double LowFuelPercent = 15.0;

            /// <summary>
            /// Low battery voltage warning, in volts.
            /// </summary>
            public const double LowBatteryVolts = 22.0;
        }

        /// <summary>
        /// Engineering ranges published as EURange on the measured variables.
        /// </summary>
        public static class Ranges
        {
            /// <summary>
            /// Upper bound of the real-power range, in watts.
            /// </summary>
            public const double RealPowerMaxWatts = 550_000.0;

            /// <summary>
            /// Lower bound of the frequency range, in hertz.
            /// </summary>
            public const double FrequencyMinHertz = 45.0;

            /// <summary>
            /// Upper bound of the frequency range, in hertz.
            /// </summary>
            public const double FrequencyMaxHertz = 55.0;

            /// <summary>
            /// Upper bound of the voltage range, in volts.
            /// </summary>
            public const double VoltageMaxVolts = 480.0;

            /// <summary>
            /// Upper bound of the current range, in amperes.
            /// </summary>
            public const double CurrentMaxAmperes = 800.0;

            /// <summary>
            /// Upper bound of the engine-speed range, in revolutions per minute.
            /// </summary>
            public const double SpeedMaxRpm = 2000.0;

            /// <summary>
            /// Upper bound of the coolant-temperature range, in degrees Celsius.
            /// </summary>
            public const double CoolantMaxCelsius = 120.0;

            /// <summary>
            /// Upper bound of the exhaust-temperature range, in degrees Celsius.
            /// </summary>
            public const double ExhaustMaxCelsius = 700.0;

            /// <summary>
            /// Upper bound of the oil-pressure range, in bar.
            /// </summary>
            public const double OilPressureMaxBar = 8.0;

            /// <summary>
            /// Upper bound of the fuel-rate range, in litres per hour.
            /// </summary>
            public const double FuelRateMaxLitresPerHour = 120.0;

            /// <summary>
            /// Upper bound of the battery-voltage range, in volts.
            /// </summary>
            public const double BatteryVoltageMaxVolts = 32.0;

            /// <summary>
            /// Upper bound of the load-percent range.
            /// </summary>
            public const double LoadPercentMax = 120.0;
        }

        /// <summary>
        /// Nominal values the simulation starts from and returns to.
        /// </summary>
        public static class Simulation
        {
            /// <summary>
            /// Nominal battery voltage of the starting system, in volts.
            /// </summary>
            public const double BatteryVolts = 24.0;

            /// <summary>
            /// Ambient air temperature, in degrees Celsius.
            /// </summary>
            public const double AmbientCelsius = 25.0;

            /// <summary>
            /// Fuel level the tank starts at, in percent of capacity.
            /// </summary>
            public const double InitialFuelPercent = 88.0;

            /// <summary>
            /// Load fraction the sets settle around when loaded.
            /// </summary>
            public const double NominalLoadFraction = 0.72;

            /// <summary>
            /// Peak-to-peak load swing about the nominal load fraction.
            /// </summary>
            public const double LoadSwingFraction = 0.18;
        }

        /// <summary>
        /// Conversions between the datasheet's engineering units and the SI units
        /// OPC UA publishes.
        /// </summary>
        public static class Convert
        {
            /// <summary>
            /// Zero degrees Celsius expressed in kelvin.
            /// </summary>
            public const double KelvinOffset = 273.15;

            /// <summary>
            /// Pascals per bar.
            /// </summary>
            public const double PascalsPerBar = 100_000.0;

            /// <summary>
            /// Seconds per hour.
            /// </summary>
            public const double SecondsPerHour = 3600.0;

            /// <summary>
            /// Converts degrees Celsius to kelvin.
            /// </summary>
            /// <param name="celsius">Temperature in degrees Celsius.</param>
            /// <returns>Temperature in kelvin.</returns>
            public static double ToKelvin(double celsius)
            {
                return celsius + KelvinOffset;
            }

            /// <summary>
            /// Converts bar to pascal.
            /// </summary>
            /// <param name="bar">Pressure in bar.</param>
            /// <returns>Pressure in pascal.</returns>
            public static double ToPascal(double bar)
            {
                return bar * PascalsPerBar;
            }

            /// <summary>
            /// Converts litres per hour to cubic metres per second.
            /// </summary>
            /// <param name="litresPerHour">Volumetric rate in litres per hour.</param>
            /// <returns>Volumetric rate in cubic metres per second.</returns>
            public static double ToCubicMetresPerSecond(double litresPerHour)
            {
                return litresPerHour / 1000.0 / SecondsPerHour;
            }
        }
    }
}
