// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Tunables for
    /// <see cref="MonitoredItemAliasNameRefreshStrategy"/>.
    /// </summary>
    public sealed class MonitoredItemAliasNameRefreshStrategyOptions
    {
        /// <summary>
        /// When non-<c>null</c>, the strategy adds the
        /// <c>LastChange</c> monitored item to this existing
        /// <see cref="Subscription"/> instead of creating its own.
        /// Disposal will <c>not</c> delete the subscription in this case
        /// — only the monitored item is removed.
        /// </summary>
        public Subscription? SharedSubscription { get; set; }

        /// <summary>
        /// Display name to apply to the <see cref="Subscription"/> the
        /// strategy creates (ignored when
        /// <see cref="SharedSubscription"/> is set). Defaults to
        /// <c>"AliasNameLastChange"</c>.
        /// </summary>
        public string SubscriptionDisplayName { get; set; }
            = "AliasNameLastChange";

        /// <summary>
        /// Publishing interval (ms) of the owned
        /// <see cref="Subscription"/> (ignored when
        /// <see cref="SharedSubscription"/> is set). Defaults to 1000ms.
        /// </summary>
        public double PublishingIntervalMs { get; set; } = 1000.0;

        /// <summary>
        /// Sampling interval (ms) for the <c>LastChange</c> monitored
        /// item. Defaults to 1000ms.
        /// </summary>
        public double SamplingIntervalMs { get; set; } = 1000.0;
    }
}
