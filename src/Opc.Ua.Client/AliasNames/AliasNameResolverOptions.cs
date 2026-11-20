// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Client.AliasNames.Refresh;

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// Controls how <see cref="AliasNameResolver"/> refreshes its cache.
    /// </summary>
    /// <remarks>
    /// For finer control set
    /// <see cref="AliasNameResolverOptions.RefreshStrategy"/> directly to
    /// an <see cref="IAliasNameRefreshStrategy"/> instance — it takes
    /// precedence over this enum.
    /// </remarks>
    public enum AliasNameResolverRefreshMode
    {
        /// <summary>
        /// The cache is loaded once via <see cref="AliasNameResolver.RefreshAsync"/>
        /// and never updated automatically. This is the safe default — it
        /// works even when the server does not allow subscriptions.
        /// </summary>
        Manual = 0,

        /// <summary>
        /// On the first call, the resolver polls the category's
        /// <c>LastChange</c> property (Part 17 §6.3.1) every
        /// <see cref="AliasNameResolverOptions.PublishingIntervalMs"/>
        /// and invalidates the cache on any value-difference (covers
        /// <c>VersionTime</c> wraparound; see Part 5 §6.4.10). Works on
        /// servers that do not support subscriptions.
        /// </summary>
        AutoOnLastChangePolling = 1,

        /// <summary>
        /// On the first call, the resolver creates an OPC UA
        /// <c>Subscription</c> + <c>MonitoredItem</c> on the category's
        /// <c>LastChange</c> variable and invalidates the cache on every
        /// notification whose value differs from the cached one
        /// (wrap-safe). Requires a server that supports subscriptions
        /// and exposes <c>LastChange</c>.
        /// </summary>
        AutoOnLastChangeMonitoredItem = 2
    }

    /// <summary>
    /// Tunables for an <see cref="AliasNameResolver"/>.
    /// </summary>
    public sealed class AliasNameResolverOptions
    {
        /// <summary>
        /// How the resolver refreshes its cache; defaults to
        /// <see cref="AliasNameResolverRefreshMode.Manual"/>.
        /// Ignored when <see cref="RefreshStrategy"/> is set.
        /// </summary>
        public AliasNameResolverRefreshMode RefreshMode { get; set; }
            = AliasNameResolverRefreshMode.Manual;

        /// <summary>
        /// Optional explicit refresh strategy; takes precedence over
        /// <see cref="RefreshMode"/> when non-<c>null</c>. Use this to
        /// plug in custom strategies (e.g. the Annex D PubSub bridge in
        /// <c>Opc.Ua.Client.AliasNames.PubSub</c>).
        /// </summary>
        public IAliasNameRefreshStrategy? RefreshStrategy { get; set; }

        /// <summary>
        /// Sampling interval (ms) for the <c>LastChange</c> monitored
        /// item created in
        /// <see cref="AliasNameResolverRefreshMode.AutoOnLastChangeMonitoredItem"/>
        /// mode. Defaults to 1000ms; ignored by polling and manual modes.
        /// </summary>
        public double LastChangeSamplingIntervalMs { get; set; } = 1000.0;

        /// <summary>
        /// Publishing interval (ms) for the polling /
        /// monitored-item-based refresh strategies. Defaults to 1000ms.
        /// </summary>
        public double PublishingIntervalMs { get; set; } = 1000.0;

        /// <summary>
        /// When <c>true</c>, <see cref="AliasNameResolver.RefreshAsync"/>
        /// prefers <c>FindAliasVerbose</c> and exposes per-target
        /// <c>ServerUris</c>; when <c>false</c> (default) it uses the
        /// less expensive <c>FindAlias</c>.
        /// </summary>
        public bool UseVerbose { get; set; }

        internal AliasNameResolverOptions Clone()
        {
            return new AliasNameResolverOptions
            {
                RefreshMode = RefreshMode,
                RefreshStrategy = RefreshStrategy,
                LastChangeSamplingIntervalMs = LastChangeSamplingIntervalMs,
                PublishingIntervalMs = PublishingIntervalMs,
                UseVerbose = UseVerbose
            };
        }
    }
}
