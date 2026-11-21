// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Calculates the standard Server.ServiceLevel value from session capacity headroom.
    /// </summary>
    internal static class ServerServiceLevelCalculator
    {
        /// <summary>
        /// Calculates the target ServiceLevel for the current session load.
        /// </summary>
        /// <param name="currentSessionCount">The number of sessions currently admitted.</param>
        /// <param name="maxSessionCount">The configured session capacity, or 0 for unlimited.</param>
        /// <returns>The target ServiceLevel value.</returns>
        internal static byte CalculateTarget(int currentSessionCount, int maxSessionCount)
        {
            if (maxSessionCount <= 0 || currentSessionCount <= 0)
            {
                return MaxServiceLevel;
            }

            if (currentSessionCount >= maxSessionCount)
            {
                return HealthyMinimumServiceLevel;
            }

            double usedFraction = (double)currentSessionCount / maxSessionCount;
            if (usedFraction <= FullServiceLevelUsedFraction)
            {
                return MaxServiceLevel;
            }

            double normalizedHeadroom = (1d - usedFraction) / (1d - FullServiceLevelUsedFraction);
            int serviceLevel = HealthyMinimumServiceLevel +
                (int)Math.Round((MaxServiceLevel - HealthyMinimumServiceLevel) * normalizedHeadroom);

            return (byte)Math.Max(HealthyMinimumServiceLevel, Math.Min(MaxServiceLevel, serviceLevel));
        }

        /// <summary>
        /// Determines whether the advertised value should be changed.
        /// </summary>
        /// <param name="currentServiceLevel">The currently advertised value.</param>
        /// <param name="targetServiceLevel">The newly calculated value.</param>
        /// <returns><c>true</c> when the server should publish the new value.</returns>
        internal static bool ShouldUpdate(byte currentServiceLevel, byte targetServiceLevel)
        {
            if (targetServiceLevel is MaxServiceLevel or HealthyMinimumServiceLevel)
            {
                return currentServiceLevel != targetServiceLevel;
            }

            int delta = Math.Abs(targetServiceLevel - currentServiceLevel);

            // Hysteresis: small capacity changes are intentionally coalesced so
            // clients do not receive a data-change notification for every single
            // session when the server is operating away from the extrema.
            return delta >= UpdateThreshold;
        }

        private const byte HealthyMinimumServiceLevel = 200;
        private const byte MaxServiceLevel = 255;
        private const double FullServiceLevelUsedFraction = 0.2d;
        private const int UpdateThreshold = 5;
    }
}
