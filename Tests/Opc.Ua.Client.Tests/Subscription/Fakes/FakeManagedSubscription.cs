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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IManagedSubscription"/>. Records
    /// every invocation and exposes settable state for ISubscription /
    /// IMessageProcessor properties. Replaces
    /// <c>Mock&lt;IManagedSubscription&gt;</c>.
    /// </summary>
    internal sealed class FakeManagedSubscription : IManagedSubscription
    {
        /// <summary>
        /// ISubscription / IMessageProcessor settable state
        /// </summary>
        public uint Id { get; set; }
        public bool Created { get; set; }
        public TimeSpan CurrentPublishingInterval { get; set; }
        public byte CurrentPriority { get; set; }
        public uint CurrentLifetimeCount { get; set; }
        public uint CurrentKeepAliveCount { get; set; }
        public bool CurrentPublishingEnabled { get; set; }
        public uint CurrentMaxNotificationsPerPublish { get; set; }
        public IMonitoredItemCollection MonitoredItems { get; set; } = null!;
        public long MissingMessageCount { get; set; }
        public long RepublishMessageCount { get; set; }

        /// <summary>
        /// Recorded calls
        /// </summary>
        public int DisposeAsyncCalls { get; private set; }
        public int RecreateAsyncCalls { get; private set; }
        public int ConditionRefreshAsyncCalls { get; private set; }
        public List<bool> NotifySubscriptionManagerPausedCalls { get; } = [];
        public List<TryCompleteTransferCall> TryCompleteTransferCalls { get; } = [];
        public List<OnPublishReceivedCall> OnPublishReceivedCalls { get; } = [];

        /// <summary>
        /// Optional overrides for behaviour
        /// </summary>
        public Func<NotificationMessage, IReadOnlyList<uint>?,
            IReadOnlyList<string>, ValueTask>? OnPublishReceivedAsyncFunc { get; set; }
        public Func<IReadOnlyList<uint>, CancellationToken, ValueTask<bool>>?
            OnTryCompleteTransferAsync { get; set; }
        public Func<CancellationToken, ValueTask>? OnRecreateAsync { get; set; }
        public Func<CancellationToken, ValueTask>? OnConditionRefreshAsync { get; set; }
        public Func<ValueTask>? OnDisposeAsync { get; set; }

        public ValueTask OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable)
        {
            OnPublishReceivedCalls.Add(new OnPublishReceivedCall(message,
                availableSequenceNumbers, stringTable));
            return OnPublishReceivedAsyncFunc?.Invoke(message,
                availableSequenceNumbers, stringTable) ??
                default;
        }

        public ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct = default)
        {
            TryCompleteTransferCalls.Add(new TryCompleteTransferCall(
                availableSequenceNumbers));
            return OnTryCompleteTransferAsync?.Invoke(availableSequenceNumbers, ct)
                ?? new ValueTask<bool>(true);
        }

        public ValueTask RecreateAsync(CancellationToken ct = default)
        {
            RecreateAsyncCalls++;
            return OnRecreateAsync?.Invoke(ct) ?? default;
        }

        public void NotifySubscriptionManagerPaused(bool paused)
        {
            NotifySubscriptionManagerPausedCalls.Add(paused);
        }

        public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
        {
            ConditionRefreshAsyncCalls++;
            return OnConditionRefreshAsync?.Invoke(ct) ?? default;
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCalls++;
            return OnDisposeAsync?.Invoke() ?? default;
        }

        internal readonly record struct OnPublishReceivedCall(
            NotificationMessage Message,
            IReadOnlyList<uint>? AvailableSequenceNumbers,
            IReadOnlyList<string> StringTable);

        internal readonly record struct TryCompleteTransferCall(
            IReadOnlyList<uint> AvailableSequenceNumbers);
    }
}
