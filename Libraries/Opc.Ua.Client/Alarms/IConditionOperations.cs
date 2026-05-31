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
