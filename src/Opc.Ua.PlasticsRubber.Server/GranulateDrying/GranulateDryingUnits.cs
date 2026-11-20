// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.GranulateDrying.Server
{
    /// <summary>
    /// The UNECE SI units used by the drying runtime. Duration variables retain UA millisecond semantics.
    /// </summary>
    public static class GranulateDryingUnits
    {
        /// <summary>
        /// Gets degree Celsius (CEL).
        /// </summary>
        public static EUInformation Celsius => Create("CEL", "°C", "degree Celsius");

        /// <summary>
        /// Gets kelvin (KEL), used for relative temperature reductions.
        /// </summary>
        public static EUInformation Kelvin => Create("KEL", "K", "kelvin");

        /// <summary>
        /// Gets cubic metre per hour (MQH), used by bin airflow.
        /// </summary>
        public static EUInformation CubicMetrePerHour => Create("MQH", "m³/h", "cubic metre per hour");

        /// <summary>
        /// Gets litre per second (G51), used by desiccant-bed airflow.
        /// </summary>
        public static EUInformation LitrePerSecond => Create("G51", "l/s", "litre per second");

        /// <summary>
        /// Gets cubic metre (MTQ), used by total air consumption.
        /// </summary>
        public static EUInformation CubicMetre => Create("MTQ", "m³", "cubic metre");

        /// <summary>
        /// Gets kilowatt hour (KWH), used by energy counters.
        /// </summary>
        public static EUInformation KilowattHour => Create("KWH", "kWh", "kilowatt hour");

        /// <summary>
        /// Gets kilowatt (KWT), used by actual power.
        /// </summary>
        public static EUInformation Kilowatt => Create("KWT", "kW", "kilowatt");

        /// <summary>
        /// Encodes a UNECE common code according to OPC 10000-8.
        /// </summary>
        private static EUInformation Create(string code, string symbol, string description)
        {
            int identifier = 0;
            foreach (char character in code)
            {
                identifier = (identifier << 8) | character;
            }
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = identifier,
                DisplayName = new LocalizedText("en", symbol),
                Description = new LocalizedText("en", description)
            };
        }
    }
}
