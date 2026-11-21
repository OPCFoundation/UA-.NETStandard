// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;

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
