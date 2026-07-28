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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Immutable snapshot of the value of an OPC-10030
    /// <c>GeoSpatialLocationType</c> property. The type models the geospatial
    /// location as a single human readable <see cref="string"/> (for example a
    /// WKT or address literal); it is not the GPOS/RSL coordinate model.
    /// </summary>
    public readonly record struct Isa95GeoSpatialLocation
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="Isa95GeoSpatialLocation"/> struct.
        /// </summary>
        /// <param name="value">
        /// The geospatial location literal, or <c>null</c> when unknown.
        /// </param>
        /// <param name="statusCode">
        /// The status code describing the quality of the value.
        /// </param>
        /// <param name="sourceTimestamp">
        /// The UTC source timestamp for the value. <see cref="DateTime.MinValue"/>
        /// requests that the current UTC time be substituted at read time.
        /// </param>
        public Isa95GeoSpatialLocation(
            string? value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            Value = value;
            StatusCode = statusCode;
            SourceTimestamp = sourceTimestamp;
        }

        /// <summary>
        /// The geospatial location literal, or <c>null</c> when unknown.
        /// </summary>
        public string? Value { get; }

        /// <summary>
        /// The status code describing the quality of <see cref="Value"/>.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// The UTC source timestamp associated with <see cref="Value"/>.
        /// </summary>
        public DateTime SourceTimestamp { get; }

        /// <summary>
        /// Creates a good-quality snapshot carrying <paramref name="value"/>.
        /// </summary>
        /// <param name="value">
        /// The geospatial location literal.
        /// </param>
        /// <param name="sourceTimestamp">
        /// The optional UTC source timestamp; when omitted the current UTC time
        /// is substituted when the value is read.
        /// </param>
        /// <returns>
        /// A good-quality <see cref="Isa95GeoSpatialLocation"/>.
        /// </returns>
        public static Isa95GeoSpatialLocation Good(
            string? value,
            DateTime? sourceTimestamp = null)
        {
            return new Isa95GeoSpatialLocation(
                value,
                StatusCodes.Good,
                sourceTimestamp ?? DateTime.MinValue);
        }
    }

    /// <summary>
    /// Provides the current value of an OPC-10030 <c>GeoSpatialLocationType</c>
    /// property and, optionally, an asynchronous stream of subsequent updates.
    /// Implementations are injected into the ISA-95 server so the address space
    /// variable can be backed by a live source without blocking the stack on
    /// synchronous I/O.
    /// </summary>
    public interface IIsa95GeoSpatialLocationProvider
    {
        /// <summary>
        /// Reads the current geospatial location.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token used to cancel the operation.
        /// </param>
        /// <returns>
        /// The current geospatial location snapshot.
        /// </returns>
        ValueTask<Isa95GeoSpatialLocation> GetCurrentAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribes to the stream of geospatial location updates. Returns
        /// <c>null</c> when the provider does not support push updates; callers
        /// then rely on <see cref="GetCurrentAsync"/> alone.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token that ends the subscription when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of updates, or <c>null</c> when unsupported.
        /// </returns>
        IAsyncEnumerable<Isa95GeoSpatialLocation>? SubscribeAsync(
            CancellationToken cancellationToken = default);
    }
}
