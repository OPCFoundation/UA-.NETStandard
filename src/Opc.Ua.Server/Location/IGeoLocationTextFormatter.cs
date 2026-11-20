// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Renders a <see cref="GeoLocationSample"/> as the location literals a
    /// text-valued location variable carries.
    /// </summary>
    /// <remarks>
    /// OPC 10030 (ISA-95) models a location as text rather than as coordinates,
    /// so a structured sample has to be projected before it can be published.
    /// Replace the default <see cref="WktGeoLocationTextFormatter"/> to publish
    /// a different geometry encoding or a site-specific literal.
    /// </remarks>
    public interface IGeoLocationTextFormatter
    {
        /// <summary>
        /// Renders a sample as location literals.
        /// </summary>
        /// <param name="sample">The sample to render.</param>
        /// <returns>
        /// The literals to publish, most specific first. May be empty when the
        /// sample carries no location at all.
        /// </returns>
        ArrayOf<string> Format(in GeoLocationSample sample);
    }
}
