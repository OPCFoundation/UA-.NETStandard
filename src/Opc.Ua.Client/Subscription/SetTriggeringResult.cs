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
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Result of an <c>SetTriggeringAsync</c> call on
    /// <see cref="ISubscription"/> (OPC UA Part 4 §5.13.5
    /// SetTriggering).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returned to imperative callers so they can inspect per-link
    /// outcomes without fishing through item state. The triggering
    /// item is the one the caller passed in; per-link entries are
    /// returned in input order so callers can pair them with their
    /// original lists by index.
    /// </para>
    /// <para>
    /// Per Part 4 §5.13.5.4 (Table 74) the only spec-specific
    /// per-link status code is <c>Bad_MonitoredItemIdInvalid</c>;
    /// other Bad statuses inherit from the common Table 179.
    /// </para>
    /// </remarks>
    /// <param name="TriggeringItem">
    /// The triggering item the request was issued for.
    /// </param>
    /// <param name="AddResults">
    /// Per-link results for the <c>linksToAdd</c> list, in input
    /// order. Each entry's <see cref="StatusCode"/> indicates whether
    /// the link was successfully established for the corresponding
    /// triggered item.
    /// </param>
    /// <param name="RemoveResults">
    /// Per-link results for the <c>linksToRemove</c> list, in input
    /// order.
    /// </param>
    /// <param name="ServiceResult">
    /// Service-level result for the RPC. <see cref="StatusCodes.Good"/>
    /// when the call reached per-link processing; one of
    /// <c>Bad_NothingToDo</c>, <c>Bad_TooManyOperations</c>,
    /// <c>Bad_SubscriptionIdInvalid</c>, or <c>Bad_MonitoredItemIdInvalid</c>
    /// (the latter when the triggering item itself was rejected) per
    /// Part 4 §5.13.5.3 (Table 73).
    /// </param>
    public sealed record SetTriggeringResult(
        IMonitoredItem TriggeringItem,
        IReadOnlyList<(IMonitoredItem Item, StatusCode Status)> AddResults,
        IReadOnlyList<(IMonitoredItem Item, StatusCode Status)> RemoveResults,
        StatusCode ServiceResult);
}
