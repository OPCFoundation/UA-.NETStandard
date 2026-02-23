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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class SubscriptionManager : ISubscriptionManager
    {
        /// <summary>
        /// Initializes the manager with its configuration.
        /// </summary>
        public SubscriptionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<SubscriptionManager>();

            m_minPublishingInterval = configuration.ServerConfiguration.MinPublishingInterval;
            m_maxPublishingInterval = configuration.ServerConfiguration.MaxPublishingInterval;
            m_publishingResolution = configuration.ServerConfiguration.PublishingResolution;
            m_maxSubscriptionLifetime = (uint)configuration.ServerConfiguration
                .MaxSubscriptionLifetime;
            m_maxDurableSubscriptionLifetimeInHours = (uint)
                configuration.ServerConfiguration.MaxDurableSubscriptionLifetimeInHours;
            m_durableSubscriptionsEnabled = configuration.ServerConfiguration
                .DurableSubscriptionsEnabled;
            m_minSubscriptionLifetime = (uint)configuration.ServerConfiguration
                .MinSubscriptionLifetime;
            m_maxMessageCount = (uint)configuration.ServerConfiguration.MaxMessageQueueSize;
            m_maxNotificationsPerPublish = (uint)configuration.ServerConfiguration
                .MaxNotificationsPerPublish;
            m_maxPublishRequestCount = configuration.ServerConfiguration.MaxPublishRequestCount;
            m_maxSubscriptionCount = configuration.ServerConfiguration.MaxSubscriptionCount;

            m_subscriptionStore = server.SubscriptionStore;

            m_subscriptions = [];
            m_publishQueues = [];
            m_statusMessages = [];
            m_lastSubscriptionId = BitConverter.ToUInt32(
                Nonce.CreateRandomNonceData(sizeof(uint)),
                0);

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);

            // create queue and event for condition refresh worker
            m_conditionRefreshEvent = new ManualResetEvent(false);
            m_conditionRefreshQueue = new Queue<ConditionRefreshTask>();
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<ISubscription> subscriptions = null;
                List<SessionPublishQueue> publishQueues = null;

                m_semaphoreSlim.Wait();
                try
                {
                    publishQueues = [.. m_publishQueues.Values];
                    m_publishQueues.Clear();

                    subscriptions = [.. m_subscriptions.Values];
                    m_subscriptions.Clear();
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }

                foreach (SessionPublishQueue publishQueue in publishQueues)
                {
                    Utils.SilentDispose(publishQueue);
                }

                foreach (ISubscription subscription in subscriptions)
                {
                    Utils.SilentDispose(subscription);
                }

                Utils.SilentDispose(m_shutdownEvent);
                Utils.SilentDispose(m_conditionRefreshEvent);
                Utils.SilentDispose(m_semaphoreSlim);
            }
        }

        /// <summary>
        /// Raised after a new subscription is created.
        /// </summary>
        public event SubscriptionEventHandler SubscriptionCreated
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SubscriptionCreated += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SubscriptionCreated -= value;
                }
            }
        }

        /// <summary>
        /// Raised before a subscription is deleted.
        /// </summary>
        public event SubscriptionEventHandler SubscriptionDeleted
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SubscriptionDeleted += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SubscriptionDeleted -= value;
                }
            }
        }

        /// <summary>
        /// Returns all of the subscriptions known to the subscription manager.
        /// </summary>
        /// <returns>A list of the subscriptions.</returns>
        public IList<ISubscription> GetSubscriptions()
        {
            return [.. m_subscriptions.Values];
        }

        /// <summary>
        /// Raises an event related to a subscription.
        /// </summary>
        protected virtual void RaiseSubscriptionEvent(ISubscription subscription, bool deleted)
        {
            SubscriptionEventHandler handler = null;

            lock (m_eventLock)
            {
                handler = m_SubscriptionCreated;

                if (deleted)
                {
                    handler = m_SubscriptionDeleted;
                }
            }

            if (handler != null)
            {
                try
                {
                    handler(subscription, deleted);
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Subscription event handler raised an exception.");
                }
            }
        }

        /// <summary>
        /// Starts up the manager makes it ready to create subscriptions.
        /// </summary>
        public virtual async ValueTask StartupAsync(CancellationToken cancellationToken = default)
        {
            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // restore subscriptions on startup
                await RestoreSubscriptionsAsync(cancellationToken)
                    .ConfigureAwait(false);

                m_shutdownEvent.Reset();

                // TODO: Ensure shutdown awaits completion and a cancellation token is passed
                _ = Task.Factory.StartNew(
                    () => PublishSubscriptionsAsync(m_publishingResolution),
                    default,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);

                m_conditionRefreshEvent.Reset();

                // TODO: Ensure shutdown awaits completion and a cancellation token is passed
                _ = Task.Factory.StartNew(
                    ConditionRefreshWorkerAsync,
                    default,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Closes all subscriptions and rejects any new requests.
        /// </summary>
        public virtual async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // stop the publishing thread.
                m_shutdownEvent.Set();

                // trigger the condition refresh thread.
                m_conditionRefreshEvent.Set();

                // dispose of publish queues.
                foreach (SessionPublishQueue queue in m_publishQueues.Values)
                {
                    queue.Dispose();
                }

                m_publishQueues.Clear();

                // store subscriptions to be able to restore them after a restart
                await StoreSubscriptionsAsync(cancellationToken)
                    .ConfigureAwait(false);

                // dispose of subscriptions objects.
                foreach (ISubscription subscription in m_subscriptions.Values)
                {
                    subscription.Dispose();
                }

                m_subscriptions.Clear();
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Stores durable subscriptions to  be able to restore them after a restart
        /// </summary>
        public virtual async ValueTask StoreSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            // only store subscriptions if durable subscriptions are enabled
            if (!m_durableSubscriptionsEnabled || m_subscriptionStore == null)
            {
                return;
            }
            var subscriptionsToStore = new List<IStoredSubscription>();

            foreach (ISubscription subscription in m_subscriptions.Values)
            {
                // only store durable subscriptions
                if (!subscription.IsDurable)
                {
                    continue;
                }
                subscriptionsToStore.Add(subscription.ToStorableSubscription());
            }

            if (subscriptionsToStore.Count == 0)
            {
                return;
            }

            try
            {
                if (m_subscriptionStore.StoreSubscriptions(subscriptionsToStore))
                {
                    m_logger.LogInformation("{Count} Subscriptions stored", subscriptionsToStore.Count);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to store {Count} subscriptions", subscriptionsToStore.Count);
            }
        }

        /// <summary>
        /// Restore durable subscriptions after a server restart
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual async ValueTask RestoreSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            if (m_server.IsRunning)
            {
                throw new InvalidOperationException(
                    "Subscription restore can only occur on startup");
            }

            // only restore subscriptions if durable subscriptions are enabeld
            if (!m_durableSubscriptionsEnabled || m_subscriptionStore == null)
            {
                return;
            }

            RestoreSubscriptionResult restoreResult;

            try
            {
                restoreResult = m_subscriptionStore.RestoreSubscriptions();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to restore subscriptions");
                return;
            }

            if (!restoreResult.Success ||
                restoreResult.Subscriptions == null ||
                !restoreResult.Subscriptions.Any())
            {
                return;
            }

            var createdSubscriptions = new Dictionary<uint, uint[]>();

            foreach (IStoredSubscription storedSubscription in restoreResult.Subscriptions)
            {
                ISubscription subscription;

                try
                {
                    subscription = await RestoreSubscriptionAsync(storedSubscription, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        ex,
                        "Failed to restore subscritption with id {SubscriptionId}",
                        storedSubscription.Id);
                    continue;
                }

                subscription.GetMonitoredItems(out uint[] monitoredItemsIds, out _);
                createdSubscriptions.Add(subscription.Id, monitoredItemsIds);
            }

            m_lastSubscriptionId = restoreResult.Subscriptions.Max(s => s.Id);

            m_subscriptionStore.OnSubscriptionRestoreComplete(createdSubscriptions);
        }

        /// <summary>
        /// Restore a subscription after a restart
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual async ValueTask<ISubscription> RestoreSubscriptionAsync(
            IStoredSubscription storedSubscription,
            CancellationToken cancellationToken = default)
        {
            if (m_subscriptions.Count >= m_maxSubscriptionCount)
            {
                throw new ServiceResultException(StatusCodes.BadTooManySubscriptions);
            }

            // calculate publishing interval.
            storedSubscription.PublishingInterval = CalculatePublishingInterval(
                storedSubscription.PublishingInterval);

            // calculate the keep alive count.
            storedSubscription.MaxKeepaliveCount = CalculateKeepAliveCount(
                storedSubscription.PublishingInterval,
                storedSubscription.MaxKeepaliveCount,
                storedSubscription.IsDurable);

            // calculate the lifetime count.
            storedSubscription.MaxLifetimeCount = CalculateLifetimeCount(
                storedSubscription.PublishingInterval,
                storedSubscription.MaxKeepaliveCount,
                storedSubscription.MaxLifetimeCount,
                storedSubscription.IsDurable);

            // calculate the max notification count.
            storedSubscription.MaxNotificationsPerPublish = CalculateMaxNotificationsPerPublish(
                storedSubscription.MaxNotificationsPerPublish);

            // create the subscription.
            Subscription subscription = await Subscription.RestoreAsync(m_server, storedSubscription, cancellationToken)
                .ConfigureAwait(false);

            uint publishingIntervalCount;

            // save subscription.
            if (!m_subscriptions.TryAdd(subscription.Id, subscription))
            {
                throw new ServiceResultException(StatusCodes.BadInternalError, "Failed to create subscription in Server");
            }

            // get the count for the diagnostics.
            publishingIntervalCount = GetPublishingIntervalCount();

            lock (m_server.DiagnosticsWriteLock)
            {
                ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                diagnostics.CurrentSubscriptionCount++;
                diagnostics.CumulatedSubscriptionCount++;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            }

            // raise subscription event.
            RaiseSubscriptionEvent(subscription, false);

            return subscription;
        }

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        public virtual async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken)
        {
            IList<ISubscription> subscriptionsToDelete = null;

            // close the publish queue for the session.
            if (m_publishQueues.TryRemove(sessionId, out SessionPublishQueue queue))
            {
                subscriptionsToDelete = queue.Close();

                // remove the subscriptions.
                if (deleteSubscriptions && subscriptionsToDelete != null)
                {
                    for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                    {
                        m_subscriptions.TryRemove(subscriptionsToDelete[ii].Id, out _);
                    }
                }
            }

            // remove the expired subscription status change notifications for this session
            lock (m_statusMessagesLock)
            {
                if (m_statusMessages.TryGetValue(sessionId, out Queue<StatusMessage> statusQueue))
                {
                    m_statusMessages.Remove(sessionId);
                }
            }

            // process all subscriptions in the queue.
            if (subscriptionsToDelete != null)
            {
                for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                {
                    ISubscription subscription = subscriptionsToDelete[ii];

                    // delete the subscription.
                    if (deleteSubscriptions)
                    {
                        // raise subscription event.
                        RaiseSubscriptionEvent(subscription, true);

                        // delete subscription.
                        await subscription.DeleteAsync(context, cancellationToken).ConfigureAwait(false);

                        // get the count for the diagnostics.
                        uint publishingIntervalCount = GetPublishingIntervalCount();
                        lock (m_server.DiagnosticsWriteLock)
                        {
                            ServerDiagnosticsSummaryDataType diagnostics = m_server
                                .ServerDiagnostics;
                            diagnostics.CurrentSubscriptionCount--;
                            diagnostics.PublishingIntervalCount = publishingIntervalCount;
                        }
                    }
                    // mark the subscriptions as abandoned.
                    else
                    {
                        await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            (m_abandonedSubscriptions ??= []).Add(subscription);
                            m_logger.LogWarning(
                                "Subscription ABANDONED, Id={SubscriptionId}.",
                                subscription.Id);
                        }
                        finally
                        {
                            m_semaphoreSlim.Release();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ConditionRefresh(OperationContext context, uint subscriptionId)
        {
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSubscriptionIdInvalid,
                    "Cannot refresh conditions for a subscription that does not exist.");
            }

            // ensure a condition refresh is allowed.
            subscription.ValidateConditionRefresh(context);

            var conditionRefreshTask = new ConditionRefreshTask(subscription, 0);

            ServiceResultException serviceResultException = null;
            lock (m_conditionRefreshLock)
            {
                if (!m_conditionRefreshQueue.Contains(conditionRefreshTask))
                {
                    m_conditionRefreshQueue.Enqueue(conditionRefreshTask);
                }
                else
                {
                    serviceResultException = new ServiceResultException(
                        StatusCodes.BadRefreshInProgress);
                }

                // trigger the refresh worker.
                m_conditionRefreshEvent.Set();
            }

            if (serviceResultException != null)
            {
                throw serviceResultException;
            }
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription and monitored item.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ConditionRefresh2(
            OperationContext context,
            uint subscriptionId,
            uint monitoredItemId)
        {
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSubscriptionIdInvalid,
                    "Cannot refresh conditions for a subscription that does not exist.");
            }

            // ensure a condition refresh is allowed.
            subscription.ValidateConditionRefresh2(context, monitoredItemId);

            var conditionRefreshTask = new ConditionRefreshTask(subscription, monitoredItemId);

            lock (m_conditionRefreshLock)
            {
                if (!m_conditionRefreshQueue.Contains(conditionRefreshTask))
                {
                    m_conditionRefreshQueue.Enqueue(conditionRefreshTask);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadRefreshInProgress);
                }

                // trigger the refresh worker.
                m_conditionRefreshEvent.Set();
            }
        }

        /// <summary>
        /// Completes a refresh conditions request.
        /// </summary>
        private async ValueTask DoConditionRefreshAsync(ISubscription subscription, CancellationToken cancellationToken = default)
        {
            try
            {
                if (m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.LogTrace("Subscription ConditionRefresh started, Id={SubscriptionId}.", subscription.Id);
                }
                await subscription.ConditionRefreshAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Subscription - DoConditionRefresh Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Completes a refresh conditions request.
        /// </summary>
        private async ValueTask DoConditionRefresh2Async(ISubscription subscription, uint monitoredItemId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.LogTrace(
                        "Subscription ConditionRefresh2 started, Id={SubscriptionId}, MonitoredItemId={MonitoredItemId}.",
                        subscription.Id,
                        monitoredItemId);
                }
                await subscription.ConditionRefresh2Async(monitoredItemId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Subscription - DoConditionRefresh2 Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<StatusCode> DeleteSubscriptionAsync(OperationContext context, uint subscriptionId, CancellationToken cancellationToken = default)
        {
            ISubscription subscription = null;

            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // remove from publish queue.
                if (m_subscriptions.TryGetValue(subscriptionId, out subscription))
                {
                    NodeId sessionId = subscription.SessionId;

                    if (!sessionId.IsNull)
                    {
                        // check that the subscription is the owner.
                        if (context != null &&
                            !ReferenceEquals(context.Session, subscription.Session))
                        {
                            throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                        }

                        if (m_publishQueues.TryGetValue(sessionId, out SessionPublishQueue queue))
                        {
                            queue.Remove(subscription, true);
                        }
                    }
                }

                // check for abandoned subscription.
                if (m_abandonedSubscriptions != null)
                {
                    for (int ii = 0; ii < m_abandonedSubscriptions.Count; ii++)
                    {
                        if (m_abandonedSubscriptions[ii].Id == subscriptionId)
                        {
                            m_abandonedSubscriptions.RemoveAt(ii);
                            m_logger.LogWarning(
                                "Subscription DELETED(ABANDONED), Id={SubscriptionId}.",
                                subscriptionId);
                            break;
                        }
                    }
                }

                // remove subscription.
                m_subscriptions.TryRemove(subscriptionId, out _);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }

            if (subscription != null)
            {
                int monitoredItemCount = subscription.MonitoredItemCount;

                // raise subscription event.
                RaiseSubscriptionEvent(subscription, true);

                // delete subscription.
                await subscription.DeleteAsync(context, cancellationToken).ConfigureAwait(false);

                // get the count for the diagnostics.
                uint publishingIntervalCount = GetPublishingIntervalCount();

                lock (m_server.DiagnosticsWriteLock)
                {
                    ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                    diagnostics.CurrentSubscriptionCount--;
                    diagnostics.PublishingIntervalCount = publishingIntervalCount;
                }

                if (context != null && context.Session != null)
                {
                    lock (context.Session.DiagnosticsLock)
                    {
                        SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                        diagnostics.CurrentSubscriptionsCount--;
                        UpdateCurrentMonitoredItemsCount(diagnostics, -monitoredItemCount);
                    }
                }

                return StatusCodes.Good;
            }

            return StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Updates the current monitored item count for the session.
        /// </summary>
        private static void UpdateCurrentMonitoredItemsCount(
            SessionDiagnosticsDataType diagnostics,
            int change)
        {
            long monitoredItemsCount = diagnostics.CurrentMonitoredItemsCount;
            monitoredItemsCount += change;

            if (monitoredItemsCount > 0)
            {
                diagnostics.CurrentMonitoredItemsCount = (uint)monitoredItemsCount;
            }
            else
            {
                diagnostics.CurrentMonitoredItemsCount = 0;
            }
        }

        /// <summary>
        /// Gets the total number of publishing intervals in use.
        /// </summary>
        private uint GetPublishingIntervalCount()
        {
            var publishingDiagnostics = new Dictionary<double, uint>();

            foreach (KeyValuePair<uint, ISubscription> kvp in m_subscriptions)
            {
                double publishingInterval = kvp.Value.PublishingInterval;

                if (!publishingDiagnostics.TryGetValue(publishingInterval, out uint total))
                {
                    total = 0;
                }

                publishingDiagnostics[publishingInterval] = total + 1;
            }

            return (uint)publishingDiagnostics.Count;
        }

        /// <summary>
        /// Creates a new subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            OperationContext context,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken cancellationToken = default)
        {
            if (m_subscriptions.Count >= m_maxSubscriptionCount)
            {
                throw new ServiceResultException(StatusCodes.BadTooManySubscriptions);
            }

            uint subscriptionId;
            double revisedPublishingInterval;
            uint revisedLifetimeCount;
            uint revisedMaxKeepAliveCount;

            uint publishingIntervalCount = 0;

            // get session from context.
            ISession session = context.Session;

            // assign new identifier.
            subscriptionId = Utils.IncrementIdentifier(ref m_lastSubscriptionId);

            // calculate publishing interval.
            revisedPublishingInterval = CalculatePublishingInterval(requestedPublishingInterval);

            // calculate the keep alive count.
            revisedMaxKeepAliveCount = CalculateKeepAliveCount(
                revisedPublishingInterval,
                requestedMaxKeepAliveCount);

            // calculate the lifetime count.
            revisedLifetimeCount = CalculateLifetimeCount(
                revisedPublishingInterval,
                revisedMaxKeepAliveCount,
                requestedLifetimeCount);

            // calculate the max notification count.
            maxNotificationsPerPublish = CalculateMaxNotificationsPerPublish(
                maxNotificationsPerPublish);

            // create the subscription.
            ISubscription subscription = CreateSubscription(
                context,
                subscriptionId,
                revisedPublishingInterval,
                revisedLifetimeCount,
                revisedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority,
                publishingEnabled);

            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // save subscription.
                if (!m_subscriptions.TryAdd(subscriptionId, subscription))
                {
                    throw new ServiceResultException(StatusCodes.BadInternalError, "Failed to create subscription in Server");
                }

                // create/update publish queue.
                m_publishQueues.AddOrUpdate(
                    session.Id,
                    (key) =>
                    {
                        var queue = new SessionPublishQueue(
                            m_server,
                            session,
                            m_maxPublishRequestCount);

                        queue.Add(subscription);
                        return queue;
                    },
                    (key, queue) =>
                        {
                            queue.Add(subscription);
                            return queue;
                        }
                );
            }
            finally
            {
                m_semaphoreSlim.Release();
            }

            // get the count for the diagnostics.
            publishingIntervalCount = GetPublishingIntervalCount();

            lock (m_statusMessagesLock)
            {
                if (!m_statusMessages.TryGetValue(
                    session.Id,
                    out Queue<StatusMessage> messagesQueue))
                {
                    m_statusMessages[session.Id] = new Queue<StatusMessage>();
                }
            }

            lock (m_server.DiagnosticsWriteLock)
            {
                ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                diagnostics.CurrentSubscriptionCount++;
                diagnostics.CumulatedSubscriptionCount++;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            }

            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    diagnostics.CurrentSubscriptionsCount++;
                }
            }

            // raise subscription event.
            RaiseSubscriptionEvent(subscription, false);

            return new CreateSubscriptionResponse
            {
                SubscriptionId = subscriptionId,
                RevisedPublishingInterval = revisedPublishingInterval,
                RevisedLifetimeCount = revisedLifetimeCount,
                RevisedMaxKeepAliveCount = revisedMaxKeepAliveCount
            };
        }

        /// <summary>
        /// Deletes group of subscriptions.
        /// </summary>
        public async ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            OperationContext context,
            UInt32Collection subscriptionIds,
            CancellationToken cancellationToken = default)
        {
            bool diagnosticsExist = false;
            var results = new StatusCodeCollection(subscriptionIds.Count);
            var diagnosticInfos = new DiagnosticInfoCollection(subscriptionIds.Count);

            foreach (uint subscriptionId in subscriptionIds)
            {
                try
                {
                    StatusCode result = await DeleteSubscriptionAsync(context, subscriptionId, cancellationToken).ConfigureAwait(false);
                    results.Add(result);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Error occurred in DeleteSubscriptions");

                    var result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        string.Empty);
                    results.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            result,
                            m_logger);
                        diagnosticInfos.Add(diagnosticInfo);
                        diagnosticsExist = true;
                    }
                }
            }

            if (!diagnosticsExist)
            {
                diagnosticInfos.Clear();
            }

            return new DeleteSubscriptionsResponse
            {
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
        }

        /// <summary>
        /// Called when a subscription expires.
        /// </summary>
        /// <param name="subscription">The subscription.</param>
        internal void SubscriptionExpired(ISubscription subscription)
        {
            lock (m_statusMessagesLock)
            {
                var message = new StatusMessage
                {
                    SubscriptionId = subscription.Id,
                    Message = subscription.PublishTimeout()
                };

                if (!subscription.SessionId.IsNull &&
                    m_statusMessages.TryGetValue(
                        subscription.SessionId,
                        out Queue<StatusMessage> queue))
                {
                    queue.Enqueue(message);
                }
            }
        }

        /// <summary>
        /// Publishes a subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<PublishResponse> PublishAsync(
            OperationContext context,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken cancellationToken = default)
        {
            // get publish queue for session.
            if (!m_publishQueues.TryGetValue(context.Session.Id, out SessionPublishQueue queue))
            {
                if (m_subscriptions.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadNoSubscription);
                }

                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }

            // acknowledge previous messages.
            queue.Acknowledge(
                context,
                subscriptionAcknowledgements,
                out StatusCodeCollection acknowledgeResults,
                out DiagnosticInfoCollection acknowledgeDiagnosticInfos);

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    diagnostics.CurrentPublishRequestsInQueue++;
                }
            }

            try
            {
                if (m_logger.IsEnabled(LogLevel.Trace))
                {
                    m_logger.LogTrace("Publish #{ClientHandle} ReceivedFromClient", context.ClientHandle);
                }

                // check for any pending status messages that need to be sent.
                if (ReturnPendingStatusMessage(context, out NotificationMessage statusMessage, out uint statusSubscriptionId))
                {
                    return new PublishResponse
                    {
                        SubscriptionId = statusSubscriptionId,
                        MoreNotifications = false,
                        NotificationMessage = statusMessage,
                        Results = acknowledgeResults,
                        DiagnosticInfos = acknowledgeDiagnosticInfos
                    };
                }

                bool requeue = false;

                do
                {
                    // blocks until a subscription is available or timeout expires.
                    ISubscription subscription = await queue.PublishAsync(
                        context.ChannelContext.SecureChannelId,
                        context.OperationDeadline,
                        requeue,
                        cancellationToken).ConfigureAwait(false);

                    // check for pending status message that may have arrived while waiting.
                    if (ReturnPendingStatusMessage(context, out statusMessage, out statusSubscriptionId))
                    {
                        if (subscription != null)
                        {
                            // requeue the subscription that was ready to publish.
                            queue.Requeue(subscription);
                        }

                        return new PublishResponse
                        {
                            SubscriptionId = statusSubscriptionId,
                            MoreNotifications = false,
                            NotificationMessage = statusMessage,
                            Results = acknowledgeResults,
                            DiagnosticInfos = acknowledgeDiagnosticInfos
                        };
                    }

                    // false alarm or race condition, requeue the request.
                    if (subscription == null)
                    {
                        requeue = true;
                        continue;
                    }

                    bool moreNotifications = false;

                    // publish notifications.
                    try
                    {
                        NotificationMessage message = subscription.Publish(
                            context,
                            out UInt32Collection availableSequenceNumbers,
                            out moreNotifications);

                        // a null message indicates a false alarm; requeue and wait for the next one.
                        if (message != null)
                        {
                            return new PublishResponse
                            {
                                SubscriptionId = subscription.Id,
                                AvailableSequenceNumbers = availableSequenceNumbers,
                                MoreNotifications = moreNotifications,
                                NotificationMessage = message,
                                Results = acknowledgeResults,
                                DiagnosticInfos = acknowledgeDiagnosticInfos
                            };
                        }

                        requeue = true;
                        if (m_logger.IsEnabled(LogLevel.Trace))
                        {
                            m_logger.LogTrace(
                                "Publish False Alarm - Request #{ClientHandle} Requeued.",
                                context.ClientHandle);
                        }
                    }
                    finally
                    {
                        queue.PublishCompleted(subscription, moreNotifications);
                    }
                } while (requeue);

                throw new ServiceResultException(StatusCodes.BadTimeout);
            }
            finally
            {
                // update diagnostics.
                if (context.Session != null)
                {
                    lock (context.Session.DiagnosticsLock)
                    {
                        SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                        diagnostics.CurrentPublishRequestsInQueue--;
                    }
                }
            }
        }

        /// <summary>
        /// Modifies an existing subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ModifySubscription(
            OperationContext context,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount)
        {
            revisedPublishingInterval = requestedPublishingInterval;
            revisedLifetimeCount = requestedLifetimeCount;
            revisedMaxKeepAliveCount = requestedMaxKeepAliveCount;

            // find subscription.

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            _ = subscription.PublishingInterval;

            // calculate publishing interval.
            revisedPublishingInterval = CalculatePublishingInterval(requestedPublishingInterval);

            // calculate the keep alive count.
            revisedMaxKeepAliveCount = CalculateKeepAliveCount(
                revisedPublishingInterval,
                requestedMaxKeepAliveCount,
                subscription.IsDurable);

            // calculate the lifetime count.
            revisedLifetimeCount = CalculateLifetimeCount(
                revisedPublishingInterval,
                revisedMaxKeepAliveCount,
                requestedLifetimeCount,
                subscription.IsDurable);

            // calculate the max notification count.
            maxNotificationsPerPublish = CalculateMaxNotificationsPerPublish(
                maxNotificationsPerPublish);

            // update the subscription.
            subscription.Modify(
                context,
                revisedPublishingInterval,
                revisedLifetimeCount,
                revisedMaxKeepAliveCount,
                maxNotificationsPerPublish,
                priority);

            // get the count for the diagnostics.
            uint publishingIntervalCount = GetPublishingIntervalCount();

            lock (m_server.DiagnosticsWriteLock)
            {
                ServerDiagnosticsSummaryDataType diagnostics = m_server.ServerDiagnostics;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            }
        }

        /// <summary>
        /// Sets a subscription into durable mode
        /// </summary>
        /// <param name="context">the system context.</param>
        /// <param name="subscriptionId">Identifier of the Subscription.</param>
        /// <param name="lifetimeInHours">The requested lifetime in hours for the durable Subscription.</param>
        /// <param name="revisedLifetimeInHours">The revised lifetime in hours the Server applied to the durable Subscription.</param>
        /// <exception cref="ServiceResultException"></exception>
        public ServiceResult SetSubscriptionDurable(
            ISystemContext context,
            uint subscriptionId,
            uint lifetimeInHours,
            out uint revisedLifetimeInHours)
        {
            revisedLifetimeInHours = 0;

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            NodeId curSession = (context as ISessionSystemContext)?.SessionId ?? default;
            if (subscription.SessionId != curSession)
            {
                // user tries to access subscription of different session
                return StatusCodes.BadUserAccessDenied;
            }

            if (subscription.MonitoredItemCount > 0)
            {
                // durable subscription can only be created before monitored items are created
                return StatusCodes.BadInvalidState;
            }

            revisedLifetimeInHours = lifetimeInHours;
            if (revisedLifetimeInHours == 0 ||
                revisedLifetimeInHours > m_maxDurableSubscriptionLifetimeInHours)
            {
                revisedLifetimeInHours = m_maxDurableSubscriptionLifetimeInHours;
            }

            const uint hoursInSeconds = 3_600_000;
            long lifetimeInSeconds = revisedLifetimeInHours * hoursInSeconds;
            uint requestedLifeTimeCount = (uint)(lifetimeInSeconds /
                subscription.PublishingInterval);

            return subscription.SetSubscriptionDurable(requestedLifeTimeCount);
        }

        /// <summary>
        /// Sets the publishing mode for a set of subscriptions.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void SetPublishingMode(
            OperationContext context,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            bool diagnosticsExist = false;

            results = new StatusCodeCollection(subscriptionIds.Count);
            diagnosticInfos = new DiagnosticInfoCollection(subscriptionIds.Count);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                try
                {
                    // find subscription.

                    if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out ISubscription subscription))
                    {
                        throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                    }

                    // update the subscription.
                    subscription.SetPublishingMode(context, publishingEnabled);

                    // save results.
                    results.Add(StatusCodes.Good);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }
                catch (Exception e)
                {
                    if (e is not ServiceResultException)
                    {
                        m_logger.LogError(e, "Error occurred in SetPublishingMode");
                    }

                    var result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        string.Empty);
                    results.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            result,
                            m_logger);
                        diagnosticInfos.Add(diagnosticInfo);
                        diagnosticsExist = true;
                    }
                }

                if (!diagnosticsExist)
                {
                    diagnosticInfos.Clear();
                }
            }
        }

        /// <summary>
        /// Attaches a groups of subscriptions to a different session.
        /// </summary>
        public async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            OperationContext context,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken cancellationToken = default)
        {
            var results = new TransferResultCollection();
            var diagnosticInfos = new DiagnosticInfoCollection();

            m_logger.LogInformation(
                "TransferSubscriptions to SessionId={SessionId}, Count={Count}, sendInitialValues={SendInitialValues}",
                context.Session.Id,
                subscriptionIds.Count,
                sendInitialValues);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                var result = new TransferResult();
                try
                {
                    // find subscription.
                    if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out ISubscription subscription))
                    {
                        result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                        results.Add(result);
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos.Add(null);
                        }
                        continue;
                    }

                    lock (subscription.DiagnosticsLock)
                    {
                        SubscriptionDiagnosticsDataType diagnostics = subscription.Diagnostics;
                        diagnostics.TransferRequestCount++;
                    }

                    // check if new and old sessions are different
                    ISession ownerSession = subscription.Session;
                    if (ownerSession != null &&
                        !ownerSession.Id.IsNull &&
                        ownerSession.Id == context.Session.Id)
                    {
                        result.StatusCode = StatusCodes.BadNothingToDo;
                        results.Add(result);
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos.Add(null);
                        }
                        continue;
                    }

                    // Validate the identity of the user who owns/owned the subscription
                    // is the same as the new owner.
                    bool validIdentity = subscription.EffectiveIdentity.TokenHandler.Equals(
                        context.Session.EffectiveIdentity.TokenHandler);

                    // Test if anonymous user is using a secure session using Sign or SignAndEncrypt
                    if (validIdentity &&
                        subscription.EffectiveIdentity.TokenType == UserTokenType.Anonymous)
                    {
                        MessageSecurityMode securityMode = context.ChannelContext
                            .EndpointDescription
                            .SecurityMode;
                        validIdentity = securityMode
                            is MessageSecurityMode.Sign
                            or MessageSecurityMode.SignAndEncrypt;
                    }

                    // continue if identity check failed
                    if (!validIdentity)
                    {
                        result.StatusCode = StatusCodes.BadUserAccessDenied;
                        results.Add(result);
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos.Add(null);
                        }
                        continue;
                    }

                    // transfer session, add subscription to publish queue
                    await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await subscription.TransferSessionAsync(context, sendInitialValues, cancellationToken).ConfigureAwait(false);

                        // remove from queue in old session
                        if (ownerSession != null &&
                            m_publishQueues.TryGetValue(
                                ownerSession.Id,
                                out SessionPublishQueue ownerPublishQueue) &&
                            ownerPublishQueue != null)
                        {
                            // keep the queued requests for the status message
                            ownerPublishQueue.Remove(subscription, false);
                        }

                        // add to queue in new session, create queue if necessary
                        if (!m_publishQueues.TryGetValue(
                                context.SessionId,
                                out SessionPublishQueue publishQueue) ||
                            publishQueue == null)
                        {
                            m_publishQueues[context.SessionId]
                                = publishQueue = new SessionPublishQueue(
                                m_server,
                                context.Session,
                                m_maxPublishRequestCount);
                        }
                        publishQueue.Add(subscription);
                    }
                    finally
                    {
                        m_semaphoreSlim.Release();
                    }

                    lock (m_statusMessagesLock)
                    {
                        var processedQueue = new Queue<StatusMessage>();
                        if (m_statusMessages.TryGetValue(
                                context.SessionId,
                                out Queue<StatusMessage> messagesQueue) &&
                            messagesQueue != null)
                        {
                            // There must not be any messages left from
                            // the transferred subscription
                            foreach (StatusMessage statusMessage in messagesQueue)
                            {
                                if (statusMessage.SubscriptionId == subscription.Id)
                                {
                                    continue;
                                }
                                processedQueue.Enqueue(statusMessage);
                            }
                        }
                        m_statusMessages[context.SessionId] = processedQueue;
                    }

                    if (context.Session != null)
                    {
                        lock (context.Session.DiagnosticsLock)
                        {
                            SessionDiagnosticsDataType diagnostics = context.Session
                                .SessionDiagnostics;
                            diagnostics.CurrentSubscriptionsCount++;
                        }
                    }

                    // raise subscription event.
                    RaiseSubscriptionEvent(subscription, false);
                    result.StatusCode = StatusCodes.Good;

                    // Notify old session with Good_SubscriptionTransferred.
                    if (ownerSession != null)
                    {
                        lock (ownerSession.DiagnosticsLock)
                        {
                            SessionDiagnosticsDataType diagnostics = ownerSession
                                .SessionDiagnostics;
                            diagnostics.CurrentSubscriptionsCount--;
                        }

                        // queue the Good_SubscriptionTransferred message
                        bool statusQueued = false;
                        lock (m_statusMessagesLock)
                        {
                            if (!ownerSession.Id.IsNull &&
                                m_statusMessages.TryGetValue(
                                    ownerSession.Id,
                                    out Queue<StatusMessage> queue))
                            {
                                var message = new StatusMessage
                                {
                                    SubscriptionId = subscription.Id,
                                    Message = subscription.SubscriptionTransferred()
                                };
                                queue.Enqueue(message);
                                statusQueued = true;
                            }
                        }

                        await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            // trigger publish response to return status immediately
                            if (m_publishQueues.TryGetValue(
                                    ownerSession.Id,
                                    out SessionPublishQueue ownerPublishQueue) &&
                                ownerPublishQueue != null)
                            {
                                if (statusQueued)
                                {
                                    // queue the status message
                                    bool success = ownerPublishQueue.TryPublishCustomStatus(
                                        StatusCodes.GoodSubscriptionTransferred);
                                    if (!success)
                                    {
                                        m_logger.LogWarning(
                                            "Failed to queue Good_SubscriptionTransferred for SessionId {SessionId}, SubscriptionId {SubscriptionId} due to an empty request queue.",
                                            ownerSession.Id,
                                            subscription.Id);
                                    }
                                }

                                // check to remove queued requests if no subscriptions are active
                                ownerPublishQueue.RemoveQueuedRequests();
                            }
                        }
                        finally
                        {
                            m_semaphoreSlim.Release();
                        }
                    }

                    // Return the sequence numbers that are available for retransmission.
                    result.AvailableSequenceNumbers = subscription
                        .AvailableSequenceNumbersForRetransmission();

                    lock (subscription.DiagnosticsLock)
                    {
                        SubscriptionDiagnosticsDataType diagnostics = subscription.Diagnostics;
                        diagnostics.TransferredToSameClientCount++;
                    }

                    // save results.
                    results.Add(result);
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }

                    m_logger.LogInformation(
                        "Transferred subscription Id {SubscriptionId} to SessionId {SessionId}",
                        subscription.Id,
                        context.Session.Id);
                }
                catch (Exception e)
                {
                    result.StatusCode = StatusCodes.Bad;
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(
                            new DiagnosticInfo(e, context.DiagnosticsMask, false, null, m_logger));
                    }
                }

                for (int i = 0; i < results.Count; i++)
                {
                    m_server.ReportAuditTransferSubscriptionEvent(
                        context.AuditEntryId,
                        context.Session,
                        results[i].StatusCode,
                        m_logger);
                }
            }
            return new TransferSubscriptionsResponse
            {
                Results = results,
                DiagnosticInfos = diagnosticInfos
            };
        }

        /// <summary>
        /// Republishes a previously published notification message.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public NotificationMessage Republish(
            OperationContext context,
            uint subscriptionId,
            uint retransmitSequenceNumber)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            // fetch the message.
            return subscription.Republish(context, retransmitSequenceNumber);
        }

        /// <summary>
        /// Updates the triggers for the monitored item.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void SetTriggering(
            OperationContext context,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos)
        {
            // find subscription.

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            // update the triggers.
            subscription.SetTriggering(
                context,
                triggeringItemId,
                linksToAdd,
                linksToRemove,
                out addResults,
                out addDiagnosticInfos,
                out removeResults,
                out removeDiagnosticInfos);
        }

        /// <summary>
        /// Adds monitored items to a subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            int currentMonitoredItemCount = subscription.MonitoredItemCount;

            // create the items.
            CreateMonitoredItemsResponse response = await subscription.CreateMonitoredItemsAsync(
                context,
                timestampsToReturn,
                itemsToCreate,
                cancellationToken).ConfigureAwait(false);

            int monitoredItemCountIncrement = subscription.MonitoredItemCount -
                currentMonitoredItemCount;

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    UpdateCurrentMonitoredItemsCount(diagnostics, monitoredItemCountIncrement);
                }
            }

            return response;
        }

        /// <summary>
        /// Modifies monitored items in a subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            // modify the items.
            return subscription.ModifyMonitoredItemsAsync(
                context,
                timestampsToReturn,
                itemsToModify,
                cancellationToken);
        }

        /// <summary>
        /// Deletes the monitored items in a subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            int currentMonitoredItemCount = subscription.MonitoredItemCount;

            // create the items.
            DeleteMonitoredItemsResponse response = await subscription.DeleteMonitoredItemsAsync(
                context,
                monitoredItemIds,
                cancellationToken).ConfigureAwait(false);

            int monitoredItemCountIncrement = subscription.MonitoredItemCount -
                currentMonitoredItemCount;

            // update diagnostics.
            if (context.Session != null)
            {
                lock (context.Session.DiagnosticsLock)
                {
                    SessionDiagnosticsDataType diagnostics = context.Session.SessionDiagnostics;
                    UpdateCurrentMonitoredItemsCount(diagnostics, monitoredItemCountIncrement);
                }
            }

            return response;
        }

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<(StatusCodeCollection results, DiagnosticInfoCollection diagnosticInfos)> SetMonitoringModeAsync(
            OperationContext context,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription subscription))
            {
                throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
            }

            // create the items.
            return subscription.SetMonitoringModeAsync(
                context,
                monitoringMode,
                monitoredItemIds,
                cancellationToken);
        }

        /// <summary>
        /// Calculate a revised queue size for a monitored item based on the provided maximum allowed queue sizes.
        /// depending if an item is durable
        /// </summary>
        /// <param name="isDurable">the item to create is a part of a durable subscription</param>
        /// <param name="queueSize">the queue size to revise</param>
        /// <param name="maxQueueSize">the maximum queue size for regular subscriptions</param>
        ///  <param name="maxDurableQueueSize">the maxmimum queue size for durable subscriptions</param>
        /// <returns>the revised queue size</returns>
        public static uint CalculateRevisedQueueSize(
            bool isDurable,
            uint queueSize,
            uint maxQueueSize,
            uint maxDurableQueueSize)
        {
            //reqular limit
            if (queueSize > maxQueueSize && !isDurable)
            {
                return maxQueueSize;
            }

            //durable subscription limit
            if (queueSize > maxDurableQueueSize && isDurable)
            {
                return maxDurableQueueSize;
            }

            //no revision needed as size within limits
            return queueSize;
        }

        /// <summary>
        /// Calculates the publishing interval.
        /// </summary>
        protected virtual double CalculatePublishingInterval(double publishingInterval)
        {
            if (double.IsNaN(publishingInterval) || publishingInterval < m_minPublishingInterval)
            {
                publishingInterval = m_minPublishingInterval;
            }

            if (publishingInterval > m_maxPublishingInterval)
            {
                publishingInterval = m_maxPublishingInterval;
            }

            if (publishingInterval < m_publishingResolution)
            {
                publishingInterval = m_publishingResolution;
            }

            if (publishingInterval % m_publishingResolution != 0)
            {
                publishingInterval =
                    ((((int)publishingInterval) / m_publishingResolution) + 1) *
                    m_publishingResolution;
            }

            return publishingInterval;
        }

        /// <summary>
        /// Calculates the keep alive count.
        /// </summary>
        protected virtual uint CalculateKeepAliveCount(
            double publishingInterval,
            uint keepAliveCount,
            bool isDurableSubscription = false)
        {
            // set default.
            if (keepAliveCount == 0)
            {
                keepAliveCount = 3;
            }

            ulong maxSubscriptionLifetime = isDurableSubscription
                ? m_maxDurableSubscriptionLifetimeInHours
                : m_maxSubscriptionLifetime;

            double keepAliveInterval = keepAliveCount * publishingInterval;

            // keep alive interval cannot be longer than the max subscription lifetime.
            if (keepAliveInterval > maxSubscriptionLifetime)
            {
                keepAliveCount = (uint)(maxSubscriptionLifetime / publishingInterval);

                if (keepAliveCount < uint.MaxValue &&
                    maxSubscriptionLifetime % publishingInterval != 0)
                {
                    keepAliveCount++;
                }

                keepAliveInterval = keepAliveCount * publishingInterval;
            }

            // the time between publishes cannot exceed the max publishing interval.
            if (keepAliveInterval > m_maxPublishingInterval)
            {
                keepAliveCount = (uint)(m_maxPublishingInterval / publishingInterval);

                if (keepAliveCount < uint.MaxValue &&
                    m_maxPublishingInterval % publishingInterval != 0)
                {
                    keepAliveCount++;
                }
            }

            return keepAliveCount;
        }

        /// <summary>
        /// Calculates the lifetime count.
        /// </summary>
        protected virtual uint CalculateLifetimeCount(
            double publishingInterval,
            uint keepAliveCount,
            uint lifetimeCount,
            bool isDurableSubscription = false)
        {
            const int kMillisecondsToHours = 3_600_000;

            ulong maxSubscriptionLifetime = isDurableSubscription
                ? m_maxDurableSubscriptionLifetimeInHours * kMillisecondsToHours
                : m_maxSubscriptionLifetime;

            double lifetimeInterval = lifetimeCount * publishingInterval;

            // lifetime cannot be longer than the max subscription lifetime.
            if (lifetimeInterval > maxSubscriptionLifetime)
            {
                lifetimeCount = (uint)(maxSubscriptionLifetime / publishingInterval);

                if (lifetimeCount < uint.MaxValue &&
                    maxSubscriptionLifetime % publishingInterval != 0)
                {
                    lifetimeCount++;
                }
            }

            // the lifetime must be greater than the keepalive.
            if (keepAliveCount < uint.MaxValue / 3)
            {
                if (keepAliveCount * 3 > lifetimeCount)
                {
                    lifetimeCount = keepAliveCount * 3;
                }

                lifetimeInterval = lifetimeCount * publishingInterval;
            }
            else
            {
                lifetimeCount = uint.MaxValue;
                lifetimeInterval = double.MaxValue;
            }

            // apply the minimum.
            if (m_minSubscriptionLifetime > publishingInterval &&
                m_minSubscriptionLifetime > lifetimeInterval)
            {
                lifetimeCount = (uint)(m_minSubscriptionLifetime / publishingInterval);

                if (lifetimeCount < uint.MaxValue &&
                    m_minSubscriptionLifetime % publishingInterval != 0)
                {
                    lifetimeCount++;
                }
            }

            return lifetimeCount;
        }

        /// <summary>
        /// Calculates the maximum number of notifications per publish.
        /// </summary>
        protected virtual uint CalculateMaxNotificationsPerPublish(uint maxNotificationsPerPublish)
        {
            if (maxNotificationsPerPublish == 0 ||
                maxNotificationsPerPublish > m_maxNotificationsPerPublish)
            {
                return m_maxNotificationsPerPublish;
            }

            return maxNotificationsPerPublish;
        }

        /// <summary>
        /// Creates a new instance of a subscription.
        /// </summary>
        protected virtual ISubscription CreateSubscription(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            uint lifetimeCount,
            uint keepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            bool publishingEnabled)
        {
            return new Subscription(
                m_server,
                context.Session,
                subscriptionId,
                publishingInterval,
                lifetimeCount,
                keepAliveCount,
                maxNotificationsPerPublish,
                priority,
                publishingEnabled,
                m_maxMessageCount);
        }

        /// <summary>
        /// Checks if there is a status message to return.
        /// </summary>
        private bool ReturnPendingStatusMessage(
            OperationContext context,
            out NotificationMessage message,
            out uint subscriptionId)
        {
            message = null;
            subscriptionId = 0;

            // check for status messages.
            lock (m_statusMessagesLock)
            {
                if (m_statusMessages.TryGetValue(
                        context.SessionId,
                        out Queue<StatusMessage> statusQueue) &&
                    statusQueue.Count > 0)
                {
                    StatusMessage status = statusQueue.Dequeue();
                    subscriptionId = status.SubscriptionId;
                    message = status.Message;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private async ValueTask PublishSubscriptionsAsync(int sleepCycle, CancellationToken cancellationToken = default)
        {
            try
            {
                m_logger.LogInformation(
                    "Subscription - Publish Task {TaskId:X8} Started.",
                    Task.CurrentId);

                int timeToWait = sleepCycle;

                while (true)
                {
                    DateTime start = DateTime.UtcNow;

                    SessionPublishQueue[] queues = null;
                    ISubscription[] abandonedSubscriptions = null;

                    await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        // collect active session queues.
                        queues = new SessionPublishQueue[m_publishQueues.Count];
                        m_publishQueues.Values.CopyTo(queues, 0);

                        // collect abandoned subscriptions.
                        if (m_abandonedSubscriptions != null && m_abandonedSubscriptions.Count > 0)
                        {
                            abandonedSubscriptions = new ISubscription[m_abandonedSubscriptions
                                .Count];

                            for (int ii = 0; ii < abandonedSubscriptions.Length; ii++)
                            {
                                abandonedSubscriptions[ii] = m_abandonedSubscriptions[ii];
                            }
                        }
                    }
                    finally
                    {
                        m_semaphoreSlim.Release();
                    }

                    // check the publish timer for each subscription.
                    for (int ii = 0; ii < queues.Length; ii++)
                    {
                        queues[ii].PublishTimerExpired();
                    }

                    // check the publish timer for each abandoned subscription.
                    if (abandonedSubscriptions != null)
                    {
                        var subscriptionsToDelete = new List<ISubscription>();

                        for (int ii = 0; ii < abandonedSubscriptions.Length; ii++)
                        {
                            ISubscription subscription = abandonedSubscriptions[ii];

                            if (subscription.PublishTimerExpired() != PublishingState.Expired)
                            {
                                continue;
                            }

                            subscriptionsToDelete.Add(subscription);
                            SubscriptionExpired(subscription);
                            m_logger.LogInformation(
                                "Subscription - Abandoned Subscription Id={SubscriptionId} Delete Scheduled.",
                                subscription.Id);
                        }

                        // schedule cleanup on a background thread.
                        if (subscriptionsToDelete.Count > 0)
                        {
                            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                            try
                            {
                                for (int ii = 0; ii < subscriptionsToDelete.Count; ii++)
                                {
                                    m_abandonedSubscriptions.Remove(subscriptionsToDelete[ii]);
                                }
                            }
                            finally
                            {
                                m_semaphoreSlim.Release();
                            }

                            CleanupSubscriptions(m_server, subscriptionsToDelete, m_logger);
                        }
                    }

                    if (m_shutdownEvent.WaitOne(0))
                    {
                        m_logger.LogInformation(
                            "Subscription - Publish Task {TaskId:X8} Exited Normally.",
                            Task.CurrentId);
                        break;
                    }

                    await Task.Delay(timeToWait, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                m_logger.LogInformation(
                    "Subscription - Publish Task {TaskId:X8} Exited Normally (disposed during shutdown).",
                    Task.CurrentId);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Subscription - Publish Task {TaskId:X8} Exited Unexpectedly.",
                    Task.CurrentId);
            }
        }

        /// <summary>
        /// A single thread to execute the condition refresh.
        /// </summary>
        private async Task ConditionRefreshWorkerAsync()
        {
            try
            {
                m_logger.LogInformation(
                    "Subscription - ConditionRefresh Task {TaskId:X8} Started.",
                    Task.CurrentId);

                while (true)
                {
                    ConditionRefreshTask conditionRefreshTask = null;

                    lock (m_conditionRefreshLock)
                    {
                        if (m_conditionRefreshQueue.Count > 0)
                        {
                            conditionRefreshTask = m_conditionRefreshQueue.Dequeue();
                        }
                        else
                        {
                            m_conditionRefreshEvent.Reset();
                        }
                    }

                    if (conditionRefreshTask == null)
                    {
                        m_conditionRefreshEvent.WaitOne();
                    }
                    else if (conditionRefreshTask.MonitoredItemId == 0)
                    {
                        await DoConditionRefreshAsync(conditionRefreshTask.Subscription)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await DoConditionRefresh2Async(
                            conditionRefreshTask.Subscription,
                            conditionRefreshTask.MonitoredItemId)
                            .ConfigureAwait(false);
                    }

                    // use shutdown event to end loop
                    if (m_shutdownEvent.WaitOne(0))
                    {
                        m_logger.LogInformation(
                            "Subscription - ConditionRefresh Task {TaskId:X8} Exited Normally.",
                            Task.CurrentId);
                        break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                m_logger.LogInformation(
                    "Subscription - ConditionRefresh Task {TaskId:X8} Exited Normally (disposed during shutdown).",
                    Task.CurrentId);
            }
            catch (Exception e)
            {
                m_logger.LogError(
                    e,
                    "Subscription - ConditionRefresh Task {TaskId:X8} Exited Unexpectedly.",
                    Task.CurrentId);
            }
        }

        /// <summary>
        /// Cleanups the subscriptions.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="subscriptionsToDelete">The subscriptions to delete.</param>
        /// <param name="logger">A contextual logger to log to</param>
        internal static void CleanupSubscriptions(
            IServerInternal server,
            IList<ISubscription> subscriptionsToDelete,
            ILogger logger)
        {
            if (subscriptionsToDelete != null && subscriptionsToDelete.Count > 0)
            {
                logger.LogInformation(
                    "Server - {Count} Subscriptions scheduled for delete.",
                    subscriptionsToDelete.Count);

                _ = Task.Run(
                    () => CleanupSubscriptionsCoreAsync(server, subscriptionsToDelete, logger));
            }
        }

        /// <summary>
        /// Deletes any expired subscriptions.
        /// </summary>
        private static async ValueTask CleanupSubscriptionsCoreAsync(
            IServerInternal server,
            IList<ISubscription> subscriptionsToDelete,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogInformation("Server - CleanupSubscriptions Task Started");

                foreach (ISubscription subscription in subscriptionsToDelete)
                {
                    await server.DeleteSubscriptionAsync(subscription.Id, cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation("Server - CleanupSubscriptions Task Completed");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Server - CleanupSubscriptions Task Halted Unexpectedly");
            }
        }

        private class StatusMessage
        {
            public uint SubscriptionId;
            public NotificationMessage Message;
        }

        private class ConditionRefreshTask
        {
            public ConditionRefreshTask(ISubscription subscription, uint monitoredItemId)
            {
                Subscription = subscription;
                MonitoredItemId = monitoredItemId;
            }

            public ISubscription Subscription { get; }

            public uint MonitoredItemId { get; }

            public override bool Equals(object obj)
            {
                return obj is ConditionRefreshTask crt &&
                    Subscription?.Id == crt.Subscription?.Id &&
                    MonitoredItemId == crt.MonitoredItemId;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Subscription.Id, MonitoredItemId);
            }
        }

        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);
        private uint m_lastSubscriptionId;
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly double m_minPublishingInterval;
        private readonly double m_maxPublishingInterval;
        private readonly int m_publishingResolution;
        private readonly uint m_maxSubscriptionLifetime;
        private readonly uint m_maxDurableSubscriptionLifetimeInHours;
        private readonly uint m_minSubscriptionLifetime;
        private readonly uint m_maxMessageCount;
        private readonly uint m_maxNotificationsPerPublish;
        private readonly int m_maxPublishRequestCount;
        private readonly int m_maxSubscriptionCount;
        private readonly bool m_durableSubscriptionsEnabled;
        private readonly ConcurrentDictionary<uint, ISubscription> m_subscriptions;
        private List<ISubscription> m_abandonedSubscriptions;
        private readonly NodeIdDictionary<Queue<StatusMessage>> m_statusMessages;
        private readonly NodeIdDictionary<SessionPublishQueue> m_publishQueues;
        private readonly ManualResetEvent m_shutdownEvent;
        private readonly Queue<ConditionRefreshTask> m_conditionRefreshQueue;
        private readonly ManualResetEvent m_conditionRefreshEvent;
        private readonly ISubscriptionStore m_subscriptionStore;

        private readonly Lock m_statusMessagesLock = new();
        private readonly Lock m_eventLock = new();
        private readonly Lock m_conditionRefreshLock = new();
        private event SubscriptionEventHandler m_SubscriptionCreated;
        private event SubscriptionEventHandler m_SubscriptionDeleted;
    }
}
