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

namespace Opc.Ua
{
    /// <summary>
    /// A geodetic position expressed as latitude, longitude and height above
    /// the reference ellipsoid.
    /// </summary>
    /// <remarks>
    /// This is the technology-neutral shape an <see cref="IGeoLocationProvider"/>
    /// reports. Each consuming companion model maps it onto its own
    /// specification type: OPC 10000-211 (GPOS) builds a structured global
    /// location from it, while OPC 10030 (ISA-95) formats it into the string
    /// literal its <c>GeoSpatialLocationType</c> variable carries.
    /// </remarks>
    /// <param name="Latitude">
    /// Latitude in decimal degrees, positive north of the equator.
    /// </param>
    /// <param name="Longitude">
    /// Longitude in decimal degrees, positive east of the prime meridian.
    /// </param>
    /// <param name="Height">
    /// Height in metres above the reference ellipsoid, or <c>null</c> for a
    /// two-dimensional fix that carries no altitude. Published as OPC
    /// 10000-211's optional <c>Elevation</c>.
    /// </param>
    /// <param name="Accuracy">
    /// Estimated horizontal accuracy in metres, or <c>null</c> when the source
    /// does not report one.
    /// </param>
    /// <param name="Floor">
    /// Building floor the position sits on, for indoor sources, or <c>null</c>
    /// when not applicable.
    /// </param>
    /// <param name="EpsgCode">
    /// EPSG code of the coordinate reference system the values are expressed
    /// in, or <c>null</c> when the provider does not state one. A consumer that
    /// is configured for a specific reference system rejects a position that
    /// explicitly declares a different one, and treats <c>null</c> as "trust
    /// the configured system". WGS84 is <c>4326</c>.
    /// </param>
    public readonly record struct GeoPosition(
        double Latitude,
        double Longitude,
        double? Height = null,
        double? Accuracy = null,
        float? Floor = null,
        uint? EpsgCode = null);
}
