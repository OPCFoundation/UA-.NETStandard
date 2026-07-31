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

using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua
{
    /// <summary>
    /// Renders a sample's position as an OGC Well-Known Text geometry, then
    /// appends the sample's own literals.
    /// </summary>
    /// <remarks>
    /// A position becomes <c>POINT Z (longitude latitude height)</c>, using
    /// WKT's x-y-z axis order and the invariant culture, so the literal is
    /// machine-parseable and round-trips. When the sample declares an EPSG code
    /// the geometry is prefixed as <c>SRID=code;POINT Z (…)</c>, following the
    /// widely used extended-WKT convention, because a coordinate literal is
    /// ambiguous without its reference system.
    /// </remarks>
    public sealed class WktGeoLocationTextFormatter : IGeoLocationTextFormatter
    {
        /// <summary>
        /// A shared instance; the formatter is stateless.
        /// </summary>
        public static WktGeoLocationTextFormatter Instance { get; } = new();

        /// <inheritdoc/>
        public ArrayOf<string> Format(in GeoLocationSample sample)
        {
            ArrayOf<string> labels = sample.Labels;
            if (sample.Position is not GeoPosition position)
            {
                return labels;
            }

            var literals = new List<string>(labels.Count + 1)
            {
                FormatPoint(position)
            };
            for (int ii = 0; ii < labels.Count; ii++)
            {
                literals.Add(labels[ii]);
            }
            return literals.ToArray().ToArrayOf();
        }

        /// <summary>
        /// Renders a position as a WKT point geometry.
        /// </summary>
        /// <param name="position">The position to render.</param>
        /// <returns>
        /// The WKT literal, prefixed with an SRID when the position declares
        /// an EPSG code.
        /// </returns>
        public static string FormatPoint(GeoPosition position)
        {
            // A position without a height is a two-dimensional geometry; WKT
            // spells that POINT rather than POINT Z.
            string point = position.Height is double height
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "POINT Z ({0} {1} {2})",
                    FormatCoordinate(position.Longitude),
                    FormatCoordinate(position.Latitude),
                    FormatCoordinate(height))
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "POINT ({0} {1})",
                    FormatCoordinate(position.Longitude),
                    FormatCoordinate(position.Latitude));
            return position.EpsgCode is uint epsg
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "SRID={0};{1}",
                    epsg,
                    point)
                : point;
        }

        private static string FormatCoordinate(double value)
        {
            // A fixed-point round-trip format: enough digits to preserve a
            // double, without the exponent notation "R" produces for small
            // magnitudes, which many WKT readers reject.
            return value.ToString("0.#################", CultureInfo.InvariantCulture);
        }
    }
}
