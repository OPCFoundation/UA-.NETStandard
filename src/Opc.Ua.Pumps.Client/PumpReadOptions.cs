// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Pumps.Client
{
    /// <summary>
    /// Controls how much a group read fetches.
    /// </summary>
    /// <remarks>
    /// The defaults fetch engineering units and instrument ranges, because an
    /// OPC 40223 reading without its unit is not interpretable - most of the
    /// analog variables are <c>AnalogUnitType</c> or
    /// <c>AnalogUnitRangeType</c> and the specification fixes no unit for
    /// them. Read once with the defaults, retain the metadata, then poll with
    /// <see cref="ValuesOnly"/> to skip the metadata round trip. Re-read
    /// metadata after a device or server configuration change.
    /// </remarks>
    public sealed record PumpReadOptions
    {
        /// <summary>The defaults: values, engineering units and ranges.</summary>
        public static readonly PumpReadOptions Default = new();

        /// <summary>
        /// Values only - one browse and one read, no metadata round trip.
        /// </summary>
        public static readonly PumpReadOptions ValuesOnly = new()
        {
            IncludeEngineeringUnits = false,
            IncludeRanges = false
        };

        /// <summary>
        /// Gets whether to read each variable's <c>EngineeringUnits</c>
        /// property. Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeEngineeringUnits { get; init; } = true;

        /// <summary>
        /// Gets whether to read each variable's <c>EURange</c> property.
        /// Defaults to <see langword="true"/>.
        /// </summary>
        public bool IncludeRanges { get; init; } = true;

        /// <summary>
        /// Gets whether to return source timestamps with the values. Defaults
        /// to <see langword="false"/>.
        /// </summary>
        public bool IncludeTimestamps { get; init; }

        internal bool IncludeMetadata => IncludeEngineeringUnits || IncludeRanges;
    }
}
