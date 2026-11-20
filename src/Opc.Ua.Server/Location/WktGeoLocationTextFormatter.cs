/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
