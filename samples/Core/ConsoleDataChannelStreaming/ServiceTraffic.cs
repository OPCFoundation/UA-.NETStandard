/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Competes with complete-message traffic using real Read and Publish calls on the borrowed shared Session.
    /// </summary>
    internal sealed class ServiceTraffic(ISession session, SampleOptions options)
        : IAsyncDisposable
    {
        /// <summary>
        /// Gets validated Read responses.
        /// </summary>
        public int Reads => Volatile.Read(ref m_reads);

        /// <summary>
        /// Gets actual Publish responses, including keep-alives.
        /// </summary>
        public int Publishes => Volatile.Read(ref m_publishes);

        /// <summary>
        /// Gets individually decoded and validated monitored-item notifications.
        /// </summary>
        public int Notifications => Volatile.Read(ref m_notifications);

        /// <summary>
        /// Gets the publishing interval the server actually accepted.
        /// </summary>
        public double RevisedPublishingInterval { get; private set; }

        /// <summary>
        /// Creates one real subscription and observes its first notification before measurement.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            CreateSubscriptionResponse created = await session.CreateSubscriptionAsync(
                null, options.PublishingInterval, 100, 10, 0, true, 0, cancellationToken).ConfigureAwait(false);
            m_subscriptionId = created.SubscriptionId;
            RevisedPublishingInterval = created.RevisedPublishingInterval;
            var requests = new MonitoredItemCreateRequest[options.MonitoredItems];
            for (int index = 0; index < requests.Length; index++)
            {
                requests[index] = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = checked((uint)index + 1),
                        SamplingInterval = options.PublishingInterval,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                };
            }
            CreateMonitoredItemsResponse monitored = await session.CreateMonitoredItemsAsync(
                null, m_subscriptionId, TimestampsToReturn.Both, ArrayOf.Wrapped(requests), cancellationToken)
                .ConfigureAwait(false);
            if (monitored.Results.Count != requests.Length)
            {
                throw new InvalidOperationException("The monitored-item result count is incorrect.");
            }
            foreach (MonitoredItemCreateResult result in monitored.Results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new InvalidOperationException("A monitored item was rejected.");
                }
            }
            m_worker = RunAsync();
            await m_firstNotification.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_stop.Cancel();
            try
            {
                if (m_worker != null)
                {
                    await m_worker.ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (m_subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_stop.Dispose();
                }
            }
        }

        /// <summary>
        /// Validates ordinary subscription teardown independently from a cancelled measurement.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            DeleteSubscriptionsResponse deleted = await session.DeleteSubscriptionsAsync(
                null, [m_subscriptionId], shutdown.Token).ConfigureAwait(false);
            if (deleted.Results.Count != 1 || StatusCode.IsBad(deleted.Results[0]))
            {
                throw new InvalidOperationException("The competing subscription did not close.");
            }
        }

        /// <summary>
        /// Validates decoded Service results while the flow independently sends and receives payload.
        /// </summary>
        private async Task RunAsync()
        {
            CancellationToken cancellationToken = m_stop.Token;
            try
            {
                ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];
                while (true)
                {
                    ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value
                        }
                    ], cancellationToken).ConfigureAwait(false);
                    if (read.Results.Count != 1 || StatusCode.IsBad(read.Results[0].StatusCode) ||
                        !read.Results[0].WrappedValue.TryGetValue(out DateTimeUtc _))
                    {
                        throw new InvalidOperationException("Read did not return the actual server clock.");
                    }
                    Interlocked.Increment(ref m_reads);
                    PublishResponse published = await session.PublishAsync(null, acknowledgements, cancellationToken)
                        .ConfigureAwait(false);
                    if (published.SubscriptionId != m_subscriptionId)
                    {
                        throw new InvalidOperationException("Publish returned a different subscription.");
                    }
                    Interlocked.Increment(ref m_publishes);
                    foreach (ExtensionObject notification in published.NotificationMessage.NotificationData)
                    {
                        if (!notification.TryGetValue(out DataChangeNotification? change) || change == null)
                        {
                            continue;
                        }
                        foreach (MonitoredItemNotification item in change.MonitoredItems)
                        {
                            if (item.ClientHandle == 0 || item.ClientHandle > options.MonitoredItems ||
                                StatusCode.IsBad(item.Value.StatusCode) ||
                                !item.Value.WrappedValue.TryGetValue(out DateTimeUtc _))
                            {
                                throw new InvalidOperationException(
                                    "A Publish notification is not the requested clock.");
                            }
                            Interlocked.Increment(ref m_notifications);
                            m_firstNotification.TrySetResult();
                        }
                    }
                    acknowledgements = published.NotificationMessage.NotificationData.Count == 0
                        ? []
                        :
                        [
                            new SubscriptionAcknowledgement
                            {
                                SubscriptionId = m_subscriptionId,
                                SequenceNumber = published.NotificationMessage.SequenceNumber
                            }
                        ];
                }
            }
            catch (OperationCanceledException) when (m_stop.IsCancellationRequested)
            {
                m_firstNotification.TrySetCanceled();
            }
            catch (ServiceResultException exception) when (m_stop.IsCancellationRequested &&
                exception.StatusCode == StatusCodes.BadRequestInterrupted)
            {
                // The Service stack represents cancellation of an outstanding Publish with this result.
                m_firstNotification.TrySetCanceled();
            }
            catch (Exception exception)
            {
                m_firstNotification.TrySetException(exception);
                throw;
            }
        }

        private readonly CancellationTokenSource m_stop = new();
        private readonly TaskCompletionSource m_firstNotification =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private uint m_subscriptionId;
        private Task? m_worker;
        private int m_reads;
        private int m_publishes;
        private int m_notifications;
    }
}
