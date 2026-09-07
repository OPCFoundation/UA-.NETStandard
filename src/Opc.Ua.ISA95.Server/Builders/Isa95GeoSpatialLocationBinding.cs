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
