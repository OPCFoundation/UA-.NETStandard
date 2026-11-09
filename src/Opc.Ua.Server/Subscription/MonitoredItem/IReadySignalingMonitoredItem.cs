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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A monitored item that tells its subscription when it has something to report, so the
    /// publish timer only looks at those items instead of every item of the subscription.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The item owns a ready signal: it acquires the signal when it gains pending
    /// notifications and calls <see cref="IMonitoredItemReadySink.OnMonitoredItemReady"/> only
    /// when the acquisition succeeded, so the subscription hears about it once. The
    /// subscription keeps the signal while it tracks the item as a candidate and releases it
    /// when it stops tracking it (published, nothing pending, deleted).
    /// </para>
    /// <para>
    /// After releasing the signal the subscription checks
    /// <see cref="HasPendingNotifications"/> again; the item acquires the signal with a full
    /// fence after it set its flags, so no notification is missed in between.
    /// </para>
    /// <para>
    /// Monitored items that do not implement this interface are found by scanning all items
    /// of the subscription on every publishing cycle, as before.
    /// </para>
    /// </remarks>
    internal interface IReadySignalingMonitoredItem
    {
        /// <summary>
        /// Whether the item has notifications to report, may become ready to report
        /// without another value (sampling deadline, aggregate interval, trigger), or has
        /// to trigger its linked items.
        /// </summary>
        bool HasPendingNotifications { get; }

        /// <summary>
        /// Acquires the ready signal; <c>false</c> when it is held already.
        /// </summary>
        bool TryAcquireReadySignal();

        /// <summary>
        /// Releases the ready signal.
        /// </summary>
        void ReleaseReadySignal();
    }

    /// <summary>
    /// Receives the ready signals of the monitored items of a subscription.
    /// </summary>
    internal interface IMonitoredItemReadySink
    {
        /// <summary>
        /// Called by a monitored item that acquired its ready signal. Called while the item
        /// holds its own lock, so the sink must not take locks that are held while calling
        /// into monitored items.
        /// </summary>
        void OnMonitoredItemReady(IMonitoredItem monitoredItem);
    }
}
