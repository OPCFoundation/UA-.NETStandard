// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Identifies the OPC UA <c>ServiceLevel</c> subrange defined by OPC 10000-4 §6.6.2.4.2 Table 105.
    /// </summary>
    public enum ServiceLevelSubrange
    {
        /// <summary>
        /// The server is in maintenance and clients should not connect.
        /// </summary>
        Maintenance,

        /// <summary>
        /// The server is not currently providing process data.
        /// </summary>
        NoData,

        /// <summary>
        /// The server is operational with degraded service.
        /// </summary>
        Degraded,

        /// <summary>
        /// The server is healthy and operational.
        /// </summary>
        Healthy
    }

    /// <summary>
    /// Defines OPC UA <c>ServiceLevel</c> subranges from OPC 10000-4 §6.6.2.4.2 Table 105.
    /// </summary>
    public static class ServiceLevels
    {
        /// <summary>
        /// The server is in maintenance and clients should not connect.
        /// </summary>
        public const byte Maintenance = 0;

        /// <summary>
        /// The server is not currently providing process data.
        /// </summary>
        public const byte NoData = 1;

        /// <summary>
        /// The lowest degraded-but-operational service level.
        /// </summary>
        public const byte DegradedMinimum = 2;

        /// <summary>
        /// The highest degraded-but-operational service level.
        /// </summary>
        public const byte DegradedMaximum = 199;

        /// <summary>
        /// The lowest healthy service level.
        /// </summary>
        public const byte HealthyMinimum = 200;

        /// <summary>
        /// The highest service level.
        /// </summary>
        public const byte Maximum = 255;

        /// <summary>
        /// Gets the subrange that contains the service level.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns>The matching subrange.</returns>
        public static ServiceLevelSubrange GetSubrange(byte level)
        {
            if (level == Maintenance)
            {
                return ServiceLevelSubrange.Maintenance;
            }
            if (level == NoData)
            {
                return ServiceLevelSubrange.NoData;
            }
            if (level < HealthyMinimum)
            {
                return ServiceLevelSubrange.Degraded;
            }

            return ServiceLevelSubrange.Healthy;
        }

        /// <summary>
        /// Returns whether the service level is in the healthy subrange.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns><c>true</c> if the server is healthy.</returns>
        public static bool IsHealthy(byte level)
        {
            return GetSubrange(level) == ServiceLevelSubrange.Healthy;
        }

        /// <summary>
        /// Returns whether the service level is in the degraded subrange.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns><c>true</c> if the server is degraded but operational.</returns>
        public static bool IsDegraded(byte level)
        {
            return GetSubrange(level) == ServiceLevelSubrange.Degraded;
        }

        /// <summary>
        /// Returns whether the service level reports no process data.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns><c>true</c> if the server has no data.</returns>
        public static bool IsNoData(byte level)
        {
            return level == NoData;
        }

        /// <summary>
        /// Returns whether the service level reports maintenance.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns><c>true</c> if the server is in maintenance.</returns>
        public static bool IsMaintenance(byte level)
        {
            return level == Maintenance;
        }

        /// <summary>
        /// Returns whether the service level is operational.
        /// </summary>
        /// <param name="level">The service level.</param>
        /// <returns><c>true</c> if the server is degraded or healthy.</returns>
        public static bool IsOperational(byte level)
        {
            return IsDegraded(level) || IsHealthy(level);
        }
    }
}
