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

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The fault categories of OPC 10000-110 Table 15, each mapped to a band
    /// of alarm <c>Severity</c> values.
    /// </summary>
    /// <remarks>
    /// The categories are ordered by increasing severity. An inactive alarm
    /// uses a severity between <see cref="AssetFaultSeverities.InactiveMinimum"/>
    /// and <see cref="AssetFaultSeverities.InactiveMaximum"/> instead.
    /// </remarks>
    public enum AssetFaultSeverity
    {
        /// <summary>
        /// A limited resource met a threshold beyond which a more serious
        /// fault would occur (201 to 300).
        /// </summary>
        LimitedResourceCapacityNearLimit,

        /// <summary>
        /// Service is required to keep the asset operating within its
        /// designed tolerances (301 to 400).
        /// </summary>
        MaintenanceNeeded,

        /// <summary>
        /// The asset has a problem but continues to operate until somebody
        /// intervenes (401 to 600).
        /// </summary>
        MinorRecoverableFault,

        /// <summary>
        /// The asset can no longer perform its function; intervention is
        /// required (601 to 800).
        /// </summary>
        MajorRecoverableFault,

        /// <summary>
        /// The asset has permanently failed (801 to 1000).
        /// </summary>
        CriticalFault
    }

    /// <summary>
    /// The severity bands of OPC 10000-110 Table 15.
    /// </summary>
    public static class AssetFaultSeverities
    {
        /// <summary>
        /// The lowest severity of an inactive alarm.
        /// </summary>
        public const ushort InactiveMinimum = 1;

        /// <summary>
        /// The highest severity of an inactive alarm.
        /// </summary>
        public const ushort InactiveMaximum = 200;

        /// <summary>
        /// Gets the lowest severity of a fault category.
        /// </summary>
        /// <param name="category">The fault category.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="category"/> is not a defined category.
        /// </exception>
        public static ushort MinimumOf(AssetFaultSeverity category)
        {
            return category switch
            {
                AssetFaultSeverity.LimitedResourceCapacityNearLimit => 201,
                AssetFaultSeverity.MaintenanceNeeded => 301,
                AssetFaultSeverity.MinorRecoverableFault => 401,
                AssetFaultSeverity.MajorRecoverableFault => 601,
                AssetFaultSeverity.CriticalFault => 801,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
            };
        }

        /// <summary>
        /// Gets the highest severity of a fault category.
        /// </summary>
        /// <param name="category">The fault category.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="category"/> is not a defined category.
        /// </exception>
        public static ushort MaximumOf(AssetFaultSeverity category)
        {
            return category switch
            {
                AssetFaultSeverity.LimitedResourceCapacityNearLimit => 300,
                AssetFaultSeverity.MaintenanceNeeded => 400,
                AssetFaultSeverity.MinorRecoverableFault => 600,
                AssetFaultSeverity.MajorRecoverableFault => 800,
                AssetFaultSeverity.CriticalFault => 1000,
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
            };
        }

        /// <summary>
        /// Gets whether a severity lies in the band of a fault category.
        /// </summary>
        /// <param name="category">The fault category.</param>
        /// <param name="severity">The severity.</param>
        public static bool IsWithin(AssetFaultSeverity category, ushort severity)
        {
            return severity >= MinimumOf(category) && severity <= MaximumOf(category);
        }

        /// <summary>
        /// Gets whether a severity is one an inactive alarm uses.
        /// </summary>
        /// <param name="severity">The severity.</param>
        public static bool IsInactive(ushort severity)
        {
            return severity is >= InactiveMinimum and <= InactiveMaximum;
        }

        /// <summary>
        /// Gets the fault category a severity of an active alarm falls into.
        /// </summary>
        /// <param name="severity">The severity.</param>
        /// <returns>
        /// The category, or <see langword="null"/> for an inactive severity
        /// (1 to 200) or a value outside the OPC UA range (0, above 1000).
        /// </returns>
        public static AssetFaultSeverity? Classify(ushort severity)
        {
            return severity switch
            {
                >= 801 and <= 1000 => AssetFaultSeverity.CriticalFault,
                >= 601 and <= 800 => AssetFaultSeverity.MajorRecoverableFault,
                >= 401 and <= 600 => AssetFaultSeverity.MinorRecoverableFault,
                >= 301 and <= 400 => AssetFaultSeverity.MaintenanceNeeded,
                >= 201 and <= 300 => AssetFaultSeverity.LimitedResourceCapacityNearLimit,
                _ => null
            };
        }
    }
}
