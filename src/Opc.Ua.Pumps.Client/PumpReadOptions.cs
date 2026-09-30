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
    /// them. The metadata is static, so a caller that polls a group on a cycle
    /// should read it once with the defaults and then poll with
    /// <see cref="ValuesOnly"/>, which saves one round trip per read.
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
