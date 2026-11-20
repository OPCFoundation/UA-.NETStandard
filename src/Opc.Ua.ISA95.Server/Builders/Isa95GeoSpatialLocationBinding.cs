/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.ISA95.Server.Builders
{
    /// <summary>
    /// The result of <see cref="IIsa95ModelBuilder.CreateGeoSpatialLocationAsync"/>:
    /// the created OPC-10030 <see cref="GeoSpatialLocationState"/> variable and,
    /// when a provider was supplied, the handle that stops the provider update
    /// loop. Disposing the binding stops the update loop; it is a no-op when no
    /// provider was bound.
    /// </summary>
    public sealed class Isa95GeoSpatialLocationBinding : IDisposable
    {
        internal Isa95GeoSpatialLocationBinding(
            GeoSpatialLocationState state,
            IDisposable? binding)
        {
            State = state;
            m_binding = binding;
        }

        /// <summary>
        /// The created geospatial location variable.
        /// </summary>
        public GeoSpatialLocationState State { get; }

        /// <summary>
        /// Stops the optional provider update loop.
        /// </summary>
        public void Dispose()
        {
            m_binding?.Dispose();
        }

        private readonly IDisposable? m_binding;
    }
}
