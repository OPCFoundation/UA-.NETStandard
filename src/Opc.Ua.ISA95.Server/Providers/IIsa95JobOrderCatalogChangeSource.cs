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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System.Collections.Generic;
using System.Threading;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Publishes committed job order catalog changes that project onto the server's
    /// <c>JobOrderList</c> but are not life-cycle state changes, so that a projection
    /// layer (for example a node manager) can keep the list current. Life-cycle
    /// state changes and job order additions are published by
    /// <see cref="IIsa95JobStatusSourceV2"/>. A V2 Update is both a standard
    /// self-transition and a catalog content change, so it is intentionally
    /// visible on both streams.
    /// <para>
    /// Each subscriber receives exactly one change per committed catalog mutation
    /// that occurs after it subscribes; subscribers are independent and cancellation
    /// or disposal of one does not affect others.
    /// </para>
    /// </summary>
    public interface IIsa95JobOrderCatalogChangeSource
    {
        /// <summary>
        /// Subscribes to committed job order catalog changes.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token that ends the subscription when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of catalog changes.
        /// </returns>
        IAsyncEnumerable<Isa95JobOrderCatalogChange> SubscribeCatalogChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
