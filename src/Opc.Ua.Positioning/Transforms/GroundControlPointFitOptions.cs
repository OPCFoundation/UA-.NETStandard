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

namespace Opc.Ua.Positioning
{
    /// <summary>
    /// Options controlling how ground control points are fitted.
    /// </summary>
    public sealed class GroundControlPointFitOptions
    {
        /// <summary>
        /// The class of transform to estimate. Defaults to
        /// <see cref="GroundControlPointFitMode.Rigid"/>.
        /// </summary>
        public GroundControlPointFitMode Mode { get; set; } = GroundControlPointFitMode.Rigid;

        /// <summary>
        /// The unit of the angular fields (longitude and latitude) of the
        /// geographic control points. Defaults to <see cref="AngleUnit.Degrees"/>
        /// which matches the EPSG:4326 convention used by
        /// <see cref="Gpos.S3DGeographicCoordinateDataType"/>.
        /// </summary>
        public AngleUnit AngleUnit { get; set; } = AngleUnit.Degrees;

        /// <summary>
        /// When true an <see cref="GroundControlPointFitMode.Affine"/> fit with a
        /// negative determinant (a reflection) is accepted. When false (the
        /// default) an affine fit that would require a reflection is rejected.
        /// Rigid and similarity fits always return a proper rotation regardless
        /// of this flag.
        /// </summary>
        public bool AllowReflection { get; set; }

        /// <summary>
        /// The coordinate reference system transformer used to map the geographic
        /// control points to a metric frame. Defaults to WGS84 when null.
        /// </summary>
        public ICoordinateReferenceSystemTransformer? CoordinateReferenceSystem { get; set; }
    }
}
