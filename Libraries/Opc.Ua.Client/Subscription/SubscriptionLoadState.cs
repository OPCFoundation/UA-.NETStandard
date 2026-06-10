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
    internal sealed record MonitoredItemLoadState(
        string Name,
        IOptionsMonitor<MonitoredItems.MonitoredItemOptions> Options,
        uint ClientHandle,
        uint ServerId,
        IReadOnlyList<string> TriggeredByNames);
}
