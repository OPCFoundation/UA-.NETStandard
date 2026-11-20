// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Explicit instance capabilities and finite aggregate observation bounds.
    /// </summary>
    public sealed record GmsSystemOptions
    {
        /// <summary>
        /// Gets the selected optional GMS capabilities.
        /// </summary>
        public GmsFeatures Features { get; init; }

        /// <summary>
        /// Gets the maximum combined single sensors and multisensors, from one through 4096.
        /// </summary>
        public int MaximumSensors { get; init; } = 128;

        /// <summary>
        /// Gets the maximum combined static accessory and additional-sensor slots, from one through 4096.
        /// </summary>
        public int MaximumEquipment { get; init; } = 128;

        /// <summary>
        /// Gets the maximum retained application-side calibration observations, from one through 1024.
        /// </summary>
        public int MaximumCalibrationHistory { get; init; } = 32;

        /// <summary>
        /// Gets the maximum number of persistent sensor-warning identities, from one through 1024.
        /// </summary>
        public int MaximumSensorWarnings { get; init; } = 128;

        /// <summary>
        /// Gets the maximum entries in any native array field, from one through 4096.
        /// </summary>
        public int MaximumArrayLength { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum length of native text fields, from one through 65536.
        /// </summary>
        public int MaximumTextLength { get; init; } = 4096;

        /// <summary>
        /// Rejects unbounded or inconsistent selection before provider acquisition or root allocation.
        /// </summary>
        internal void Validate()
        {
            if ((Features & ~GmsFeatures.All) != 0 ||
                ((Features & GmsFeatures.DynamicSensors) != 0 && (Features & GmsFeatures.Sensors) == 0) ||
                ((Features & GmsFeatures.ToolMonitoring) != 0 && (Features & GmsFeatures.Sensors) == 0) ||
                ((Features & GmsFeatures.SensorWarnings) != 0 && (Features & GmsFeatures.AdditionalSensors) == 0) ||
                ((Features & GmsFeatures.DynamicProduction) != 0 && (Features & GmsFeatures.Production) == 0) ||
                MaximumSensors is < 1 or > 4096 || MaximumEquipment is < 1 or > 4096 ||
                MaximumCalibrationHistory is < 1 or > 1024 || MaximumSensorWarnings is < 1 or > 1024 ||
                MaximumArrayLength is < 1 or > 4096 || MaximumTextLength is < 1 or > 65536)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "GMS capabilities require compatible feature selections and finite positive bounds.");
            }
        }
    }
}
