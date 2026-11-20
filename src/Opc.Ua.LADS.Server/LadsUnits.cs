// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// UNECE engineering units commonly used by laboratory devices.
    /// </summary>
    public static class LadsUnits
    {
        /// <summary>
        /// The namespace of the UNECE CEFACT unit codes.
        /// </summary>
        public const string UnitsNamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact";

        /// <summary>
        /// Degree Celsius.
        /// </summary>
        public static EUInformation Celsius => Create("CEL", "°C", "degree Celsius");

        /// <summary>
        /// Revolutions per minute.
        /// </summary>
        public static EUInformation RevolutionsPerMinute => Create("RPM", "r/min", "revolutions per minute");

        /// <summary>
        /// Percent.
        /// </summary>
        public static EUInformation Percent => Create("P1", "%", "percent");

        /// <summary>
        /// Second.
        /// </summary>
        public static EUInformation Second => Create("SEC", "s", "second");

        /// <summary>
        /// Millisecond.
        /// </summary>
        public static EUInformation Millisecond => Create("C26", "ms", "millisecond");

        /// <summary>
        /// Parts per million.
        /// </summary>
        public static EUInformation PartsPerMillion => Create("59", "ppm", "parts per million");

        /// <summary>
        /// Creates the engineering unit of a UNECE common code.
        /// </summary>
        /// <param name="commonCode">The UNECE common code of up to three characters.</param>
        /// <param name="symbol">The display symbol.</param>
        /// <param name="description">The description of the unit.</param>
        /// <returns>The engineering unit.</returns>
        /// <exception cref="ArgumentException">The common code is empty or longer than three characters.</exception>
        public static EUInformation Create(string commonCode, string symbol, string description)
        {
            if (string.IsNullOrEmpty(commonCode) || commonCode.Length > 3)
            {
                throw new ArgumentException("A UNECE common code has one to three characters.", nameof(commonCode));
            }
            int unitId = 0;
            foreach (char c in commonCode)
            {
                unitId = (unitId << 8) | (c & 0xFF);
            }
            return new EUInformation
            {
                NamespaceUri = UnitsNamespaceUri,
                UnitId = unitId,
                DisplayName = new LocalizedText(symbol),
                Description = new LocalizedText(description)
            };
        }
    }
}
