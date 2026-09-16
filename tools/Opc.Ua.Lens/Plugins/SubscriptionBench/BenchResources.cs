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
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Creates the live subscription resources the bench scales. The desktop uses a
/// V2-stack-backed factory bound to the primary session; tests supply a fake that
/// records created and disposed resources without a server. Configuration of the
/// session-wide publish pipeline is deliberately absent: that belongs to the
/// primary connection, not the bench.
/// </summary>
internal interface IBenchResourceFactory
{
    /// <summary>
    /// True when the connected engine can host bench subscriptions (the V2 channel
    /// engine). When false, <see cref="BenchTopology"/> refuses to grow and reports
    /// that the V2 engine is required rather than silently doing nothing.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Creates one new subscription bound to the shared notification sink. Throws
    /// when the engine rejects the creation; the caller surfaces and stops growing.
    /// </summary>
    IBenchSubscription CreateSubscription();
}

/// <summary>
/// A single live bench subscription and the monitored items scaled beneath it.
/// </summary>
internal interface IBenchSubscription : IAsyncDisposable
{
    /// <summary>
    /// Server-revised publishing interval in milliseconds, used as the
    /// representative interval for the whole bench (all subscriptions share
    /// parameters).
    /// </summary>
    double RevisedPublishingIntervalMs { get; }

    /// <summary>
    /// Applies the shared subscription parameters to this subscription. Used both
    /// when creating a subscription and when the user edits the shared settings, so
    /// every current and subsequently created subscription tracks the same values.
    /// </summary>
    void ApplySubscriptionConfig(SubscriptionConfig config);

    /// <summary>
    /// Adds one monitored item for the given node with the shared item settings.
    /// Returns null when the collection rejects the add.
    /// </summary>
    IBenchItem? TryAddItem(string key, NodeId node, MonitoredItemSettings settings);
}

/// <summary>
/// A single live monitored item beneath a bench subscription.
/// </summary>
internal interface IBenchItem
{
    /// <summary>
    /// True when the item currently carries a bad status/result.
    /// </summary>
    bool IsBad { get; }

    /// <summary>
    /// Applies the shared monitored-item settings (sampling, queue, mode, filter)
    /// without recreating the item, preserving its node and attribute.
    /// </summary>
    void ApplySettings(MonitoredItemSettings settings);

    /// <summary>
    /// Removes this item from its owning subscription. Returns false when the
    /// collection reports the handle was already gone.
    /// </summary>
    bool Remove();
}
