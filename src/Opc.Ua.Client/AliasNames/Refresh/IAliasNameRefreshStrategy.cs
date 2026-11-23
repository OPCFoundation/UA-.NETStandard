// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
