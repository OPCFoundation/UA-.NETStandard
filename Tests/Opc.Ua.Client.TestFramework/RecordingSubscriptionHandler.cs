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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Reusable <see cref="ISubscriptionNotificationHandler"/> for V2
    /// subscription integration tests. Counts data changes, keep-alives,
    /// and events; tracks the per-subscription last sequence number;
    /// records all <see cref="DataValueChange"/>s for inspection; exposes
    /// helpers to wait for the first data change, the first keep-alive,
    /// or a specific data-change count.
    /// </summary>
    /// <remarks>
    /// Safe to use from concurrent V2 publish dispatches — counters use
    /// <see cref="Interlocked"/>, the per-subscription sequence-number
    /// table is a <see cref="ConcurrentDictionary{TKey,TValue}"/>, and
    /// the captured-values list is wrapped in a lock on its accessor.
    /// </remarks>
    public sealed class RecordingSubscriptionHandler : ISubscriptionNotificationHandler
    {
        /// <summary>
        /// Total number of <see cref="DataValueChange"/> records observed
        /// across every <see cref="OnDataChangeNotificationAsync"/>
        /// dispatch.
        /// </summary>
        public int DataChangeCount => Volatile.Read(ref m_dataChangeCount);

        /// <summary>
        /// Total number of <see cref="EventNotification"/> records observed.
        /// </summary>
        public int EventCount => Volatile.Read(ref m_eventCount);

        /// <summary>
        /// Total number of keep-alive notifications observed.
        /// </summary>
        public int KeepAliveCount => Volatile.Read(ref m_keepAliveCount);

        /// <summary>
        /// Most recently observed publish time across all dispatch
        /// callbacks for this handler.
        /// </summary>
        public DateTime LastPublishTime { get; private set; }

        /// <summary>
        /// Last observed sequence number per subscription instance. Use
        /// the subscription as the key.
        /// </summary>
        public IReadOnlyDictionary<ISubscription, uint> LastSequenceNumberBySubscription
            => m_lastSequenceNumber;

        /// <summary>
        /// Captured data-value changes. Use <see cref="GetSnapshot"/>
        /// for a stable read; this exposes the raw list for callers
        /// that need direct access.
        /// </summary>
        public List<RecordedDataValueChange> RecordedChanges
        {
            get
            {
                lock (m_recordedChangesLock)
                {
                    return [.. m_recordedChanges];
                }
            }
        }

        /// <summary>
        /// Take a snapshot of the recorded data-value changes.
        /// </summary>
        public IReadOnlyList<RecordedDataValueChange> GetSnapshot()
        {
            lock (m_recordedChangesLock)
            {
                return m_recordedChanges.ToArray();
            }
        }

        /// <inheritdoc/>
        public ValueTask OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            m_lastSequenceNumber[subscription] = sequenceNumber;
            LastPublishTime = publishTime;
            int added = notification.Length;
            Interlocked.Add(ref m_dataChangeCount, added);

            // Project each pooled DataValueChange struct into a stable
            // record we own. The DataValue reference inside the V2
            // notification is safe to retain (the lifetime note on
            // ISubscriptionNotificationHandler.DataValueChange spells
            // that out), but the wrapping struct is on a pooled buffer.
            lock (m_recordedChangesLock)
            {
                ReadOnlySpan<DataValueChange> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    DataValueChange ch = span[i];
                    m_recordedChanges.Add(new RecordedDataValueChange(
                        ch.MonitoredItem,
                        ch.Value,
                        sequenceNumber,
                        publishTime));
                }
            }

            m_firstData.TrySetResult(true);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            m_lastSequenceNumber[subscription] = sequenceNumber;
            LastPublishTime = publishTime;
            Interlocked.Add(ref m_eventCount, notification.Length);
            m_firstEvent.TrySetResult(true);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            m_lastSequenceNumber[subscription] = sequenceNumber;
            LastPublishTime = publishTime;
            Interlocked.Increment(ref m_keepAliveCount);
            m_firstKeepAlive.TrySetResult(true);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnSubscriptionStateChangedAsync(
            ISubscription subscription,
            Opc.Ua.Client.Subscriptions.SubscriptionState state,
            PublishState publishStateMask,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref m_stateChangedCount);
            lock (m_stateChangesLock)
            {
                m_stateChanges.Add(new RecordedStateChange(
                    subscription, state, publishStateMask, DateTime.UtcNow));
            }
            if (publishStateMask.HasFlag(PublishState.Transferred))
            {
                m_firstTransferred.TrySetResult(true);
            }
            return default;
        }

        /// <summary>
        /// Wait until at least one data-change notification has been
        /// observed, or until the timeout / cancellation fires.
        /// </summary>
        public Task<bool> WaitForFirstDataAsync(TimeSpan timeout,
            CancellationToken ct = default)
        {
            return WaitAsync(m_firstData, timeout, ct);
        }

        /// <summary>
        /// Wait until at least one keep-alive notification has been
        /// observed.
        /// </summary>
        public Task<bool> WaitForFirstKeepAliveAsync(TimeSpan timeout,
            CancellationToken ct = default)
        {
            return WaitAsync(m_firstKeepAlive, timeout, ct);
        }

        /// <summary>
        /// Wait until at least one event notification has been observed.
        /// </summary>
        public Task<bool> WaitForFirstEventAsync(TimeSpan timeout,
            CancellationToken ct = default)
        {
            return WaitAsync(m_firstEvent, timeout, ct);
        }

        /// <summary>
        /// Wait until a state-change callback with the
        /// <see cref="PublishState.Transferred"/> bit set has been
        /// observed.
        /// </summary>
        public Task<bool> WaitForTransferredStateAsync(TimeSpan timeout,
            CancellationToken ct = default)
        {
            return WaitAsync(m_firstTransferred, timeout, ct);
        }

        /// <summary>
        /// Total number of <see cref="OnSubscriptionStateChangedAsync"/>
        /// callbacks observed.
        /// </summary>
        public int StateChangedCount => Volatile.Read(ref m_stateChangedCount);

        /// <summary>
        /// Snapshot of recorded state-change callbacks.
        /// </summary>
        public IReadOnlyList<RecordedStateChange> GetStateChangeSnapshot()
        {
            lock (m_stateChangesLock)
            {
                return m_stateChanges.ToArray();
            }
        }

        /// <summary>
        /// Poll until <see cref="DataChangeCount"/> reaches
        /// <paramref name="atLeast"/>, or until the timeout / cancellation
        /// fires.
        /// </summary>
        public async Task<bool> WaitForDataCountAsync(int atLeast,
            TimeSpan timeout, CancellationToken ct = default)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (DataChangeCount >= atLeast)
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return DataChangeCount >= atLeast;
        }

        /// <summary>
        /// Reset all counters and recorded data so the handler can be
        /// reused across phases of a test.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref m_dataChangeCount, 0);
            Interlocked.Exchange(ref m_eventCount, 0);
            Interlocked.Exchange(ref m_keepAliveCount, 0);
            Interlocked.Exchange(ref m_stateChangedCount, 0);
            m_lastSequenceNumber.Clear();
            lock (m_recordedChangesLock)
            {
                m_recordedChanges.Clear();
            }
            lock (m_stateChangesLock)
            {
                m_stateChanges.Clear();
            }
            m_firstData = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_firstEvent = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_firstKeepAlive = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_firstTransferred = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<bool> WaitAsync(
            TaskCompletionSource<bool> tcs, TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource
                .CreateLinkedTokenSource(timeoutCts.Token, ct);
            using (linked.Token.Register(() => tcs.TrySetResult(false)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        private int m_dataChangeCount;
        private int m_eventCount;
        private int m_keepAliveCount;
        private int m_stateChangedCount;
        private TaskCompletionSource<bool> m_firstData =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> m_firstKeepAlive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> m_firstEvent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> m_firstTransferred =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<ISubscription, uint> m_lastSequenceNumber = new();
        private readonly List<RecordedDataValueChange> m_recordedChanges = [];
        private readonly Lock m_recordedChangesLock = new();
        private readonly List<RecordedStateChange> m_stateChanges = [];
        private readonly Lock m_stateChangesLock = new();
    }

    /// <summary>
    /// A captured data-value change for inspection in tests. Owns its
    /// <see cref="DataValue"/> reference; safe to retain past the
    /// V2 dispatch return.
    /// </summary>
    public sealed record RecordedDataValueChange(
        Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem? MonitoredItem,
        DataValue Value,
        uint SequenceNumber,
        DateTime PublishTime);

    /// <summary>
    /// A captured state-change callback for inspection in tests.
    /// </summary>
    public sealed record RecordedStateChange(
        ISubscription Subscription,
        Opc.Ua.Client.Subscriptions.SubscriptionState State,
        PublishState PublishStateMask,
        DateTime ObservedAt);
}
