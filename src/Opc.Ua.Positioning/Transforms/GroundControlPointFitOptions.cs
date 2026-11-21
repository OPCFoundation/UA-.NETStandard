// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
