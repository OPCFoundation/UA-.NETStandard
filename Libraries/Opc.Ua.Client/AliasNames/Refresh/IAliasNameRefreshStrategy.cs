/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Pluggable cache-invalidation strategy for
    /// <see cref="AliasNameResolver"/>. Implementations detect that a
    /// resolver's view of the server is stale and invoke the supplied
    /// callback to trigger a reload on the next <c>ResolveAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built-in strategies cover the three common shapes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>ManualAliasNameRefreshStrategy</c>
    ///     — never invalidates; the caller controls refreshes.</description></item>
    ///   <item><description><c>PollingAliasNameRefreshStrategy</c>
    ///     — periodically reads the category's <c>LastChange</c> via the
    ///     standard <c>Read</c> service and invalidates on any
    ///     value-difference (wrap-safe).</description></item>
    ///   <item><description><c>MonitoredItemAliasNameRefreshStrategy</c>
    ///     — creates an OPC UA <c>Subscription</c> + <c>MonitoredItem</c>
    ///     on the category's <c>LastChange</c> variable and invalidates
    ///     on every notification with a different value.</description></item>
    /// </list>
    /// <para>
    /// Custom strategies (e.g. a Part 17 Annex D PubSub-driven bridge)
    /// can implement this interface and plug in via
    /// <see cref="AliasNameResolverOptions.RefreshStrategy"/>.
    /// </para>
    /// </remarks>
    public interface IAliasNameRefreshStrategy : IAsyncDisposable
    {
        /// <summary>
        /// Begins watching for cache-invalidation triggers. Invoked once
        /// by <see cref="AliasNameResolver"/> on first use.
        /// Implementations invoke <paramref name="onInvalidate"/>
        /// whenever they decide the resolver's cache may be stale.
        /// </summary>
        /// <param name="client">The resolver's underlying client.
        /// Strategies can use it to look up the category's
        /// <c>LastChange</c> NodeId, issue reads, or create
        /// subscriptions on its <see cref="AliasNameClient.Session"/>.</param>
        /// <param name="onInvalidate">Callback to fire when the cache
        /// should be invalidated. May be called from any thread; the
        /// resolver handles synchronisation.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask StartAsync(
            AliasNameClient client,
            Action onInvalidate,
            CancellationToken ct);
    }
}
