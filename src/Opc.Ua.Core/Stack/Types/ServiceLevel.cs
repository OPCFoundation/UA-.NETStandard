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
