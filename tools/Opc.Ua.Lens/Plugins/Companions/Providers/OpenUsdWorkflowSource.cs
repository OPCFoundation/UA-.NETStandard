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
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Connection;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed record OpenUsdWorkflowChange(
        NodeId Source, DataValue Value, uint SequenceNumber = 0, DateTime PublishTime = default);

    internal interface IOpenUsdWorkflowSource
    {
        IAsyncEnumerable<OpenUsdWorkflowChange> ObserveAsync(
            ArrayOf<NodeId> nodes, CancellationToken cancellationToken);

        IAsyncEnumerable<DataValue> ReadHistoryAsync(
            NodeId node, DateTime start, DateTime end, uint maximum, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Owns one V2 subscription per enumeration. Capture is bounded before
    /// notification callbacks return; history uses the shared Part 11 client.
    /// </summary>
    internal sealed class OpenUsdWorkflowSource : IOpenUsdWorkflowSource
    {
        public OpenUsdWorkflowSource(CompanionContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public IAsyncEnumerable<OpenUsdWorkflowChange> ObserveAsync(
            ArrayOf<NodeId> nodes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes.Count is 0 or > 128 ||
                !m_context.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager) ||
                manager is null)
            {
                throw new InvalidOperationException("Bounded live observation requires the V2 subscription engine.");
            }
            var unique = new HashSet<NodeId>();
            foreach (NodeId node in nodes)
            {
                if (node.IsNull || !unique.Add(node))
                {
                    throw new ArgumentException("Observation requires unique non-null source nodes.", nameof(nodes));
                }
            }
            return ObserveCoreAsync(nodes, manager, cancellationToken);
        }

        private async IAsyncEnumerable<OpenUsdWorkflowChange> ObserveCoreAsync(
            ArrayOf<NodeId> nodes, ISubscriptionManager manager,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            for (int index = 0; index < nodes.Count; index++)
            {
                names.Add(index.ToString(CultureInfo.InvariantCulture), nodes[index]);
            }
            var capture = new Capture(m_context, names);
            var options = new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions
            {
                PublishingEnabled = false,
                PublishingInterval = TimeSpan.FromMilliseconds(100),
                KeepAliveCount = 2,
                LifetimeCount = 30,
                MaxNotificationsPerPublish = 128
            });
            ISubscription subscription = manager.Add(capture, options);
            var owned = new CaptureLease(capture, subscription);
            await using (owned.ConfigureAwait(false))
            {
                foreach ((string name, NodeId node) in names)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!subscription.MonitoredItems.TryAdd(name,
                        new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                        {
                            StartNodeId = node,
                            AttributeId = Attributes.Value,
                            SamplingInterval = TimeSpan.FromMilliseconds(100),
                            MonitoringMode = MonitoringMode.Reporting,
                            QueueSize = 128,
                            DiscardOldest = false
                        }), out IMonitoredItem? item) ||
                        item is null)
                    {
                        throw new InvalidOperationException("A capture monitored item could not be registered.");
                    }
                }
                options.CurrentValue = options.CurrentValue with { PublishingEnabled = true };
                await foreach (CapturedChange change in capture.Changes.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    capture.Release(change.Size);
                    capture.ThrowIfFailed();
                    yield return change.Value;
                }
            }
        }

        public IAsyncEnumerable<DataValue> ReadHistoryAsync(
            NodeId node, DateTime start, DateTime end, uint maximum, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.IsNull || end <= start || maximum is 0 or > 128)
            {
                throw new ArgumentException("A history source, increasing range and 1-128 values are required.");
            }
            var history = new HistoryClient(m_context.Session, new HistoryClientOptions
            {
                MaxPagesPerRead = 128,
                MaxReadDuration = TimeSpan.FromSeconds(15)
            });
            return ReadHistoryCoreAsync(history, node, start, end, maximum, cancellationToken);
        }

        private static async IAsyncEnumerable<DataValue> ReadHistoryCoreAsync(
            HistoryClient history, NodeId node, DateTime start, DateTime end, uint maximum,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int count = 0;
            await foreach (DataValue value in history.ReadRawAsync(node, start, end, maximum,
                timestampsToReturn: TimestampsToReturn.Both, cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
            {
                CellCompanionSupport.CheckCount(++count, (int)maximum, "Received history values");
                yield return value;
            }
        }

        private readonly CompanionContext m_context;

        private sealed record CapturedChange(OpenUsdWorkflowChange Value, int Size);

        private sealed class CaptureLease(Capture capture, ISubscription subscription) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                capture.Close();
                try
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanup) when (capture.Failure is not null &&
                    cleanup is ServiceResultException or InvalidOperationException or
                        System.IO.IOException or OperationCanceledException)
                {
                    throw new AggregateException("OpenUSD capture and subscription cleanup failed.",
                        capture.Failure, cleanup);
                }
                capture.ThrowIfFailed();
            }
        }

        private sealed class Capture(
            CompanionContext context, Dictionary<string, NodeId> nodes) : ISubscriptionNotificationHandler
        {
            public ChannelReader<CapturedChange> Changes => m_changes.Reader;

            public Exception? Failure => Volatile.Read(ref m_failure);

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                if (Volatile.Read(ref m_closed) != 0 || Failure is not null)
                {
                    return ValueTask.CompletedTask;
                }
                try
                {
                    foreach (DataValueChange change in notification.Span)
                    {
                        if (change.MonitoredItem is null ||
                            !nodes.TryGetValue(change.MonitoredItem.Name, out NodeId node))
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                                "A notification did not belong to the selected capture items.");
                        }
                        int size = OpenUsdWorkflowMetadata.EncodeSample(context, change.Value, default).Length;
                        DataValue value = change.Value.IsNull ? change.Value : change.Value.WithWrappedValue(
                            DataValueCodec.Snapshot(change.Value.WrappedValue, context.Session.MessageContext));
                        if (Interlocked.Add(ref m_bytes, size) > OpenUsdWorkflowMetadata.MaximumTotalSampleBytes)
                        {
                            Release(size);
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                "The live capture exceeded its queued-byte limit.");
                        }
                        if (!m_changes.Writer.TryWrite(new CapturedChange(
                            new OpenUsdWorkflowChange(node, value, sequenceNumber, publishTime), size)))
                        {
                            Release(size);
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                "The live capture queue filled; incomplete capture is not exported.");
                        }
                    }
                }
                catch (Exception error) when (error is ServiceResultException or ArgumentException or
                    InvalidOperationException or OverflowException)
                {
                    Fail(error);
                }
                return ValueTask.CompletedTask;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                Fail(new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "An event arrived on a value-only capture."));
                return ValueTask.CompletedTask;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription, uint sequenceNumber, DateTime publishTime, PublishState publishStateMask)
            {
                ValidateItems(subscription);
                return ValueTask.CompletedTask;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription, SubscriptionState state, PublishState publishStateMask,
                CancellationToken ct = default)
            {
                if (state is SubscriptionState.Deleted or SubscriptionState.Error ||
                    (publishStateMask &
                        (PublishState.Stopped |
                            PublishState.Timeout |
                            PublishState.Transferred |
                            PublishState.Completed)) != 0)
                {
                    Fail(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid,
                        "The capture subscription stopped, failed or changed ownership."));
                }
                else
                {
                    ValidateItems(subscription);
                }
                return ValueTask.CompletedTask;
            }

            public void Release(int bytes)
            {
                Interlocked.Add(ref m_bytes, -bytes);
            }

            public void Close()
            {
                Interlocked.Exchange(ref m_closed, 1);
                m_changes.Writer.TryComplete();
            }

            public void ThrowIfFailed()
            {
                if (Failure is { } failure)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            private void ValidateItems(ISubscription subscription)
            {
                if (Volatile.Read(ref m_closed) != 0)
                {
                    return;
                }
                foreach (IMonitoredItem item in subscription.MonitoredItems.Items)
                {
                    if (StatusCode.IsBad(item.Error.StatusCode))
                    {
                        Fail(new ServiceResultException(item.Error));
                        return;
                    }
                }
            }

            private void Fail(Exception error)
            {
                if (Volatile.Read(ref m_closed) == 0 &&
                    Interlocked.CompareExchange(ref m_failure, error, null) is null)
                {
                    m_changes.Writer.TryComplete(error);
                }
            }

            private readonly Channel<CapturedChange> m_changes = Channel.CreateBounded<CapturedChange>(
                new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

            private Exception? m_failure;
            private int m_bytes;
            private int m_closed;
        }
    }
}
