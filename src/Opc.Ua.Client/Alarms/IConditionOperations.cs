/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Client-side operations for an OPC UA <c>ConditionType</c> instance.
    /// </summary>
    /// <remarks>
    /// All operations target a specific condition instance identified by
    /// the <c>conditionId</c> argument. The well-known method ids on
    /// <c>ConditionType</c> are used; servers that do not expose
    /// condition instances in the address space accept the
    /// <c>ConditionId</c> as the <c>ObjectId</c> per Part 9 §5.5.4–5.5.7.
    /// </remarks>
    public interface IConditionOperations
    {
        /// <summary>
        /// Enables a condition instance.
        /// </summary>
        ValueTask EnableAsync(
            NodeId conditionId,
            CancellationToken ct = default);

        /// <summary>
        /// Disables a condition instance.
        /// </summary>
        ValueTask DisableAsync(
            NodeId conditionId,
            CancellationToken ct = default);

        /// <summary>
        /// Adds a comment to the specific state identified by <paramref name="eventId"/>.
        /// </summary>
        ValueTask AddCommentAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default);

        /// <summary>
        /// Requests a refresh of all conditions for the given subscription.
        /// </summary>
        ValueTask ConditionRefreshAsync(
            uint subscriptionId,
            CancellationToken ct = default);

        /// <summary>
        /// Requests a refresh of conditions for a single monitored item.
        /// </summary>
        ValueTask ConditionRefresh2Async(
            uint subscriptionId,
            uint monitoredItemId,
            CancellationToken ct = default);
    }
}
