// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Internal contract between
    /// <see cref="ISubscriptionManager.LoadAsync"/> and the V2
    /// <see cref="Subscription"/> constructor that pre-installs
    /// server-assigned identifiers + per-item state so the V2 state
    /// machine can take over an existing server-side subscription via
    /// <c>TransferSubscriptions</c> instead of issuing a fresh
    /// <c>CreateSubscription</c>. This struct deliberately does NOT
    /// surface the public <see cref="SubscriptionStateSnapshot"/> shape
    /// to the engine so the engine has no DTO coupling.
    /// </summary>
    internal sealed record SubscriptionLoadState(
        uint ServerId,
        IReadOnlyList<MonitoredItemLoadState> MonitoredItems);

    /// <summary>
    /// Per-item version of <see cref="SubscriptionLoadState"/>.
    /// </summary>
    /// <param name="Name">Stable, manager-unique monitored item name.</param>
    /// <param name="Options">Saved item options.</param>
    /// <param name="ClientHandle">Client-assigned handle at snapshot time.</param>
    /// <param name="ServerId">Server-assigned monitored item id.</param>
    /// <param name="TriggeredByNames">
    /// Saved desired triggering set (OPC UA Part 4 §5.13.5). Each entry
    /// is the stable name of a triggering item that should report this
    /// item; the engine restores this into the runtime
    /// <c>DesiredTriggeredByNames</c> on the recreated
    /// <see cref="MonitoredItems.MonitoredItem"/>.
    /// </param>
    /// <param name="HasRevisedSettings">Whether server-revised runtime values were captured.</param>
    /// <param name="RevisedSamplingInterval">The server-revised sampling interval.</param>
    /// <param name="RevisedQueueSize">The server-revised queue size.</param>
    /// <param name="RevisedFilterResult">The server filter result.</param>
    internal sealed record MonitoredItemLoadState(
        string Name,
        IOptionsMonitor<MonitoredItems.MonitoredItemOptions> Options,
        uint ClientHandle,
        uint ServerId,
        IReadOnlyList<string> TriggeredByNames,
        bool HasRevisedSettings = false,
        TimeSpan RevisedSamplingInterval = default,
        uint RevisedQueueSize = 0,
        MonitoringFilterResult? RevisedFilterResult = null);
}
