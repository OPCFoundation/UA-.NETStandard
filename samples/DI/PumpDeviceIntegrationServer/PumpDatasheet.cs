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

namespace Pumps
{
    /// <summary>
    /// The published characteristics of the simulated SimPump Corp
    /// PumpX-2000, exactly as documented in
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/blob/master/samples/DI/PumpDeviceIntegrationServer/DATASHEET.md">DATASHEET.md</see>.
    /// Every value the server publishes - nameplate, engineering ranges,
    /// alarm trip points and simulated process values - is derived from
    /// these constants, so the datasheet and the running address space
    /// cannot drift apart.
    /// </summary>
    internal static class PumpDatasheet
    {
        /// <summary>
        /// Nameplate data shared by every unit of the product.
        /// </summary>
        public static class Nameplate
        {
            public const string Manufacturer = "SimPump Corp";
            public const string ManufacturerUri = "https://simpump.example";
            public const string Model = "PumpX-2000";
            public const string ProductCode = "PX2000-32-160";
            public const string DeviceClass = "Pump";
            public const string HardwareRevision = "1.4";
            public const string SoftwareRevision = "2.5.3";
            public const string ArticleNumber = "PX2000-32-160-CI";
            public const string OrderProductCode = "PX2000-32-160-CI-M30";
            public const string TypeOfProduct = "Centrifugal pump, end-suction";
            public const string Supplier = "SimPump Corp";
            public const string CountryOfOrigin = "DE";
            public const string ProductInstanceUriPrefix =
                "urn:simdevice:SimPump:PumpX-2000:";
            public const ushort YearOfConstruction = 2025;
            public const byte MonthOfConstruction = 4;
            public const int DayOfConstruction = 17;
        }

        /// <summary>
        /// Hydraulic design point and characteristic curves (section 3 of
        /// the datasheet). Reference liquid is water at 20 &#176;C.
        /// </summary>
        public static class Hydraulics
        {
            /// <summary>
            /// Density of the reference liquid in kg/m&#179;.
            /// </summary>
            public const double FluidDensity = 998.0;

            /// <summary>
            /// Gravitational acceleration in m/s&#178;.
            /// </summary>
            public const double GravitationalAcceleration = 9.81;

            /// <summary>
            /// Flow at the best efficiency point in m&#179;/h.
            /// </summary>
            public const double RatedFlow = 25.0;

            /// <summary>
            /// Shut-off head in m, the constant term of the head curve.
            /// </summary>
            public const double ShutoffHead = 32.0;

            /// <summary>
            /// Quadratic term of the head curve in m/(m&#179;/h)&#178;.
            /// </summary>
            public const double HeadCurveCoefficient = 0.0104;

            /// <summary>
            /// Efficiency at the best efficiency point in percent.
            /// </summary>
            public const double RatedEfficiency = 72.0;

            /// <summary>
            /// Curvature of the efficiency parabola about the best
            /// efficiency point.
            /// </summary>
            public const double EfficiencyCurveFactor = 0.6;

            /// <summary>
            /// Head at the best efficiency point in m.
            /// </summary>
            public const double RatedHead =
                ShutoffHead - (HeadCurveCoefficient * RatedFlow * RatedFlow);

            /// <summary>
            /// Mass flow at the best efficiency point in kg/s.
            /// </summary>
            public const double RatedMassFlow = FluidDensity * RatedFlow / 3600.0;

            /// <summary>
            /// Rated speed of the pump set in min<sup>-1</sup>. The simulated
            /// shaft turns in proportion to how close the pump runs to its
            /// rated mass flow.
            /// </summary>
            public const double RatedSpeed = 2900.0;

            /// <summary>
            /// Shaft power at the best efficiency point in W.
            /// </summary>
            public const double RatedShaftPower =
                FluidDensity * GravitationalAcceleration * (RatedFlow / 3600.0) *
                RatedHead / (RatedEfficiency / 100.0);
        }

        /// <summary>
        /// Engineering ranges published as the <c>EURange</c> property of
        /// each measurement (section 4 of the datasheet).
        /// </summary>
        public static class Ranges
        {
            public const double DifferentialPressureMin = 0.0;
            public const double DifferentialPressureMax = 400_000.0;
            public const double FluidTemperatureMin = 263.15;
            public const double FluidTemperatureMax = 393.15;
            public const double BearingTemperatureMin = 273.15;
            public const double BearingTemperatureMax = 423.15;
            public const double PumpPowerInputMin = 0.0;
            public const double PumpPowerInputMax = 4_000.0;
            public const double MassFlowMin = 0.0;
            public const double MassFlowMax = 10.0;
            public const double PumpEfficiencyMin = 0.0;
            public const double PumpEfficiencyMax = 100.0;
            public const double LevelMin = 0.0;
            public const double LevelMax = 5.0;
        }

        /// <summary>
        /// Bearing-temperature trip points and supervision thresholds
        /// (section 7 of the datasheet), in K and m.
        /// </summary>
        public static class TripPoints
        {
            public const double BearingTemperatureHighHigh = 373.15;
            public const double BearingTemperatureHigh = 363.15;
            public const double BearingTemperatureLow = 283.15;
            public const double BearingTemperatureLowLow = 278.15;
            public const double MotorOverheatSet = 363.15;
            public const double MotorOverheatClear = 361.15;
            public const double CavitationSetLevel = 2.10;
            public const double CavitationClearLevel = 2.20;
        }

        /// <summary>
        /// Deterministic simulation profile (section 8 of the datasheet).
        /// Angular rates are expressed in radians per 250 ms tick.
        /// </summary>
        public static class Simulation
        {
            /// <summary>
            /// Relative amplitude of the flow modulation about the rated
            /// flow.
            /// </summary>
            public const double FlowModulation = 0.30;

            public const double FlowRate = 0.03;
            public const double FluidTemperatureNominal = 313.15;
            public const double FluidTemperatureAmplitude = 5.0;
            public const double FluidTemperatureRate = 0.01;
            public const double LevelNominal = 2.5;
            public const double LevelAmplitude = 0.5;
            public const double LevelRate = 0.02;

            /// <summary>
            /// Bearing temperature in K at zero load.
            /// </summary>
            public const double BearingTemperatureBase = 323.15;

            /// <summary>
            /// Bearing temperature rise in K at the rated shaft power.
            /// </summary>
            public const double BearingTemperatureLoadRise = 10.0;

            /// <summary>
            /// Length of the bearing-cooling fault cycle in ticks.
            /// </summary>
            public const long CoolingFaultPeriodTicks = 64;

            /// <summary>
            /// Tick within the cycle at which the cooling water is
            /// interrupted.
            /// </summary>
            public const long CoolingFaultOnsetTick = 56;

            /// <summary>
            /// Maximum bearing temperature rise in K caused by the
            /// cooling fault.
            /// </summary>
            public const double CoolingFaultRise = 50.0;

            /// <summary>
            /// Number of ticks between two simulated pump starts.
            /// </summary>
            public const long StartIntervalTicks = 3_600;

            /// <summary>
            /// Phase offset in ticks applied per simulated pump so the
            /// units do not move in lockstep.
            /// </summary>
            public const long PhaseOffsetTicks = 17;
        }
    }
}
