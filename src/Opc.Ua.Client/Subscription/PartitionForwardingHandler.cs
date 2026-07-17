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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// <para>
    /// Notification-handler shim placed between an
    /// <see cref="LogicalSubscription"/>'s underlying partition
    /// subscriptions and the user-supplied
    /// <see cref="ISubscriptionNotificationHandler"/>. Performs two
    /// transformations on every callback:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Re-target the subscription parameter</b>
    /// — the user's handler always sees the logical wrapper (the
    /// instance returned from <see cref="ISubscriptionManager.Add"/>)
    /// rather than the per-partition subscription. This preserves
    /// the caller's mental model: one logical subscription, one
    /// handler call per notification.</description></item>
    /// <item><description><b>Serialise across partitions</b> — when
    /// multiple partitions back the logical subscription, raw
    /// per-partition dispatch may run concurrently. The forwarding
    /// handler funnels every callback through a
    /// <see cref="SemaphoreSlim"/> so the user handler observes one
    /// invocation at a time, matching the single-partition behaviour
    /// callers came to rely on under the V2 engine.</description></item>
    /// </list>
    /// <para>
    /// The semaphore is acquired regardless of partition count; the
    /// uncontested take/release cost on a single-partition
    /// subscription is negligible relative to the per-publish work
    /// the user handler does. Callers that want raw per-partition
    /// concurrency can opt out via
    /// <see cref="SubscriptionOptions.DisableUnboundedItemMode"/>,
    /// which skips the wrapping and forwards the user handler
    /// straight to the single partition the manager creates.
    /// </para>
    /// </summary>
    internal sealed class PartitionForwardingHandler
        : ISubscriptionNotificationHandler, IDisposable
    {
        /// <summary>
        /// Construct a forwarding handler bound to the supplied user
        /// handler. The logical subscription that should be passed
        /// through to callbacks is captured later via
        /// <see cref="BindLogical"/> — this two-phase construction
        /// breaks the cyclic dependency between
        /// <see cref="LogicalSubscription"/> and the partition that
        /// references this handler at construction time.
        /// </summary>
        public PartitionForwardingHandler(ISubscriptionNotificationHandler userHandler)
        {
            m_userHandler = userHandler ?? throw new ArgumentNullException(nameof(userHandler));
        }

        /// <summary>
        /// Late-bind the logical wrapper that will be reported as
        /// the <c>subscription</c> argument on every user-handler
        /// callback. Must be called exactly once, before the first
        /// publish notification is dispatched. The wrapper is
        /// captured in a single volatile reference so the dispatch
        /// path can read it lock-free.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="logical"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void BindLogical(ISubscription logical)
        {
            if (logical == null)
            {
                throw new ArgumentNullException(nameof(logical));
            }
            if (Interlocked.CompareExchange(ref m_logical, logical, null) != null)
            {
                throw new InvalidOperationException(
                    "PartitionForwardingHandler is already bound to a logical subscription.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask, System.Collections.Generic.IReadOnlyList<string> stringTable)
        {
            ISubscription logical = m_logical ?? subscription;
            await m_serialise.WaitAsync().ConfigureAwait(false);
            try
            {
                await m_userHandler.OnDataChangeNotificationAsync(logical,
                    sequenceNumber, publishTime, notification,
                    publishStateMask, stringTable).ConfigureAwait(false);
            }
            finally
            {
                m_serialise.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask, System.Collections.Generic.IReadOnlyList<string> stringTable)
        {
            ISubscription logical = m_logical ?? subscription;
            await m_serialise.WaitAsync().ConfigureAwait(false);
            try
            {
                await m_userHandler.OnEventDataNotificationAsync(logical,
                    sequenceNumber, publishTime, notification,
                    publishStateMask, stringTable).ConfigureAwait(false);
            }
            finally
            {
                m_serialise.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnKeepAliveNotificationAsync(ISubscription subscription,
            uint sequenceNumber, DateTime publishTime,
            PublishState publishStateMask)
        {
            ISubscription logical = m_logical ?? subscription;
            await m_serialise.WaitAsync().ConfigureAwait(false);
            try
            {
                await m_userHandler.OnKeepAliveNotificationAsync(logical,
                    sequenceNumber, publishTime, publishStateMask)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_serialise.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription,
            SubscriptionState state, PublishState publishStateMask,
            CancellationToken ct = default)
        {
            ISubscription logical = m_logical ?? subscription;
            await m_serialise.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await m_userHandler.OnSubscriptionStateChangedAsync(logical,
                    state, publishStateMask, ct).ConfigureAwait(false);
            }
            finally
            {
                m_serialise.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_serialise.Dispose();
        }

        private readonly ISubscriptionNotificationHandler m_userHandler;
        private readonly SemaphoreSlim m_serialise = new(1, 1);
        private ISubscription? m_logical;
    }
}
