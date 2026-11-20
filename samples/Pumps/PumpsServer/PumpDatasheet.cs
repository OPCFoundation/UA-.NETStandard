// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua;

namespace PumpsSample
{
    /// <summary>
    /// The fixed data of the two sample pumps: a duty/standby pair of
    /// centrifugal cooling-water pumps on one skid.
    /// </summary>
    /// <remarks>
    /// Everything here is what a real server would take from the pump's
    /// datasheet or asset register. The simulation only ever changes the
    /// operational values.
    /// </remarks>
    internal static class PumpDatasheet
    {
        public const string DutyPump = "CoolingPump_A";
        public const string StandbyPump = "CoolingPump_B";

        public const string Manufacturer = "Acme Pumps";
        public const string ManufacturerUri = "https://pumps.example.com";
        public const string Model = "CW-80";
        public const string Location = "Plant 1 / Cooling skid";

        public const string OperationManual = "https://pumps.example.com/manuals/cw-80.pdf";
        public const string TechnicalData = "https://pumps.example.com/datasheets/cw-80.pdf";

        /// <summary>Nominal mass flow at the best efficiency point, kg/s.</summary>
        public const double RatedMassFlow = 25.0;

        /// <summary>Nominal differential pressure, Pa.</summary>
        public const double RatedDifferentialPressure = 350_000.0;

        /// <summary>Nominal speed, 1/min.</summary>
        public const double RatedSpeed = 2_900.0;

        /// <summary>Nominal shaft power input, kW.</summary>
        public const double RatedPowerInput = 11.0;

        /// <summary>Best pump efficiency, percent.</summary>
        public const double BestEfficiency = 78.0;

        public const double MaximumFlow = 32.0;
        public const double MinimumFlow = 6.0;
        public const double MaximumAllowableWorkingPressure = 1_600_000.0;
        public const double MaximumAllowableContinuousSpeed = 3_000.0;

        /// <summary>Bearing temperature at steady state, °C.</summary>
        public const double BearingTemperature = 55.0;

        /// <summary>Operating hours the pumps start the sample with.</summary>
        public const double InitialOperatingHours = 12_480.0;

        /// <summary>How often the duty and standby pumps swap roles.</summary>
        public const int ExchangeIntervalSeconds = 90;

        /// <summary>
        /// Engineering units from the UNECE Recommendation 20 table, which
        /// OPC 10000-8 §5.6.3 names as the source for <see cref="EUInformation"/>.
        /// </summary>
        public static class Units
        {
            public static readonly EUInformation KilogramPerSecond = Create("KGS", "kg/s", "kilogram per second");
            public static readonly EUInformation Pascal = Create("PAL", "Pa", "pascal");
            public static readonly EUInformation RevolutionsPerMinute = Create("RPM", "1/min", "revolutions per minute");
            public static readonly EUInformation Kilowatt = Create("KWT", "kW", "kilowatt");
            public static readonly EUInformation Percent = Create("P1", "%", "percent");
            public static readonly EUInformation DegreeCelsius = Create("CEL", "°C", "degree Celsius");
            public static readonly EUInformation Ampere = Create("AMP", "A", "ampere");
            public static readonly EUInformation Hour = Create("HUR", "h", "hour");
            public static readonly EUInformation MillimetrePerSecond = Create("C16", "mm/s", "millimetre per second");

            /// <summary>
            /// Builds the EUInformation for a UNECE common code. The UnitId is
            /// the code's characters packed into an Int32, as OPC 10000-8
            /// §5.6.3 specifies.
            /// </summary>
            private static EUInformation Create(string code, string symbol, string description)
            {
                int unitId = 0;
                foreach (char character in code)
                {
                    unitId = (unitId << 8) | character;
                }
                return new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = unitId,
                    DisplayName = new LocalizedText("en", symbol),
                    Description = new LocalizedText("en", description)
                };
            }
        }
    }
}
