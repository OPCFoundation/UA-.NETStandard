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

using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Immutable snapshot of a V2 <see cref="ISubscription"/>'s
    /// configuration plus the server-side identifiers needed to take
    /// over the subscription on a different session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by <see cref="ISubscription.Snapshot"/> and consumed by
    /// <see cref="ISubscriptionManager.RestoreAsync"/>.
    /// </para>
    /// <para>
    /// Field semantics:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="Options"/> — the live
    /// <see cref="SubscriptionOptions"/> at snapshot time.</description></item>
    /// <item><description><see cref="ServerId"/> — the
    /// server-assigned subscription id. <c>0</c> indicates the
    /// subscription had not been created on the server yet
    /// (snapshot-before-create). Used by the transfer leg of
    /// <see cref="ISubscriptionManager.RestoreAsync"/> to drive
    /// <c>TransferSubscriptions</c>.</description></item>
    /// <item><description><see cref="AvailableSequenceNumbers"/> — the
    /// server's published list of sequence numbers in its
    /// retransmission queue at snapshot time. Captured for diagnostics
    /// and for the non-transfer restore path; the transfer leg uses the
    /// <c>TransferSubscriptions</c> response's list as authoritative
    /// instead.</description></item>
    /// <item><description><see cref="MonitoredItems"/> — per-item
    /// state.</description></item>
    /// </list>
    /// </remarks>
    public sealed record SubscriptionStateSnapshot
    {
        /// <summary>
        /// The live <see cref="SubscriptionOptions"/> at snapshot time.
        /// </summary>
        public required SubscriptionOptions Options { get; init; }

        /// <summary>
        /// Server-assigned subscription id, or <c>0</c> if the
        /// subscription had not been created on the server yet.
        /// </summary>
        public uint ServerId { get; init; }

        /// <summary>
        /// Server's reported retransmission-queue sequence numbers at
        /// snapshot time. Diagnostic-only for the transfer leg of
        /// restore; transfer uses the <c>TransferSubscriptions</c>
        /// response's authoritative list.
        /// </summary>
        public ArrayOf<uint> AvailableSequenceNumbers { get; init; }

        /// <summary>
        /// Per-item state at snapshot time.
        /// </summary>
        public ArrayOf<MonitoredItemStateSnapshot> MonitoredItems { get; init; }
    }
}
