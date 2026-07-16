/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
                return MinRunningServiceLevel;
            }

            double usedFraction = (double)currentSessionCount / maxSessionCount;
            if (usedFraction <= FullServiceLevelUsedFraction)
            {
                return MaxServiceLevel;
            }

            double normalizedHeadroom = (1d - usedFraction) / (1d - FullServiceLevelUsedFraction);
            int serviceLevel = MinRunningServiceLevel +
                (int)Math.Round((MaxServiceLevel - MinRunningServiceLevel) * normalizedHeadroom);

            return (byte)Math.Max(MinRunningServiceLevel, Math.Min(MaxServiceLevel, serviceLevel));
        }

        /// <summary>
        /// Determines whether the advertised value should be changed.
        /// </summary>
        /// <param name="currentServiceLevel">The currently advertised value.</param>
        /// <param name="targetServiceLevel">The newly calculated value.</param>
        /// <returns><c>true</c> when the server should publish the new value.</returns>
        internal static bool ShouldUpdate(byte currentServiceLevel, byte targetServiceLevel)
        {
            if (targetServiceLevel is MaxServiceLevel or MinRunningServiceLevel)
            {
                return currentServiceLevel != targetServiceLevel;
            }

            int delta = Math.Abs(targetServiceLevel - currentServiceLevel);

            // Hysteresis: small capacity changes are intentionally coalesced so
            // clients do not receive a data-change notification for every single
            // session when the server is operating away from the extrema.
            return delta >= UpdateThreshold;
        }

        private const byte MinRunningServiceLevel = 1;
        private const byte MaxServiceLevel = 255;
        private const double FullServiceLevelUsedFraction = 0.2d;
        private const int UpdateThreshold = 5;
    }
}
