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
using System.Threading;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Publishes job responses as the provider receives them, so that a
    /// projection layer can keep the <c>JobOrderResponseList</c> that
    /// <see cref="IIsa95JobResponseCatalog"/> feeds current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A response usually arrives without a life-cycle state change — a
    /// machine reports progress such as runs completed while the order keeps
    /// running — so neither <see cref="IIsa95JobStatusSourceV2"/> nor
    /// <see cref="IIsa95JobOrderCatalogChangeSource"/> sees it, and a list
    /// refreshed only from those two stays stale until the order next changes
    /// state.
    /// </para>
    /// <para>
    /// Each subscriber receives exactly one change per response received after
    /// it subscribes; subscribers are independent and cancellation or disposal
    /// of one does not affect others.
    /// </para>
    /// </remarks>
    public interface IIsa95JobResponseChangeSource
    {
        /// <summary>
        /// Subscribes to received job responses.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token that ends the subscription when cancelled.
        /// </param>
        /// <returns>
        /// An asynchronous stream of response changes.
        /// </returns>
        IAsyncEnumerable<Isa95JobResponseChange> SubscribeResponseChangesAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One job response a provider received.
    /// </summary>
    public readonly record struct Isa95JobResponseChange
    {
        /// <summary>
        /// The identifier of the received job response.
        /// </summary>
        public required string JobResponseId { get; init; }

        /// <summary>
        /// The identifier of the job order the response belongs to.
        /// </summary>
        public required string JobOrderId { get; init; }

        /// <summary>
        /// A monotonically increasing sequence number assigned to the change
        /// within the response-change stream.
        /// </summary>
        public required ulong SequenceNumber { get; init; }

        /// <summary>
        /// The time at which the response was received, as reported by the
        /// injected <see cref="System.TimeProvider"/>.
        /// </summary>
        public required DateTimeUtc Timestamp { get; init; }
    }
}
