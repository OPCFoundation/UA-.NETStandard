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
using System.Diagnostics.CodeAnalysis;
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
            : this(server, configuration, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the manager with its configuration.
        /// </summary>
        public SubscriptionManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider? timeProvider)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<SubscriptionManager>();
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

            m_minPublishingInterval = configuration.ServerConfiguration!.MinPublishingInterval;
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
            m_abandonedSubscriptions = [];
            m_expiringSubscriptions = [];
            m_publishQueues = [];
            m_statusMessages = [];
            m_lastSubscriptionId = BitConverter.ToUInt32(
                Nonce.CreateRandomNonceData(sizeof(uint)),
                0);

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);

            m_backgroundWork = new BackgroundTaskScope(
                nameof(SubscriptionManager),
                server.Telemetry);

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
                List<ISubscription>? subscriptions = null;
                List<SessionPublishQueue>? publishQueues = null;

                m_semaphoreSlim.Wait();
                try
                {
                    SignalConditionRefreshWorkerShutdown();
                    m_workerCts?.Cancel();

                    publishQueues = [.. m_publishQueues.Values];
                    m_publishQueues.Clear();

                    subscriptions = [.. m_subscriptions.Values];
                    m_subscriptions.Clear();
                    m_expiringSubscriptions.Clear();
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }

                foreach (SessionPublishQueue publishQueue in publishQueues)
                {
                    publishQueue?.Dispose();
                }

                foreach (ISubscription subscription in subscriptions)
                {
                    subscription?.Dispose();
                }

                m_backgroundWork.Dispose();
                m_shutdownEvent.Dispose();
                m_conditionRefreshEvent.Dispose();
                m_semaphoreSlim.Dispose();
                m_workerCts?.Dispose();
                m_workerCts = null;
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

        /// <inheritdoc/>
        public IList<ISubscription> GetSubscriptions()
        {
            return [.. m_subscriptions.Values];
        }

        /// <inheritdoc/>
        public bool TryGetSubscription(uint id, [NotNullWhen(true)] out ISubscription? subscription)
        {
            return m_subscriptions.TryGetValue(id, out subscription);
        }

        /// <summary>
        /// Raises an event related to a subscription.
        /// </summary>
        protected virtual void RaiseSubscriptionEvent(ISubscription subscription, bool deleted)
        {
            SubscriptionEventHandler? handler;
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
                    m_logger.SubscriptionEventHandlerRaisedAnException(e);
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

                // Recreated on every startup: a token source cannot be reset once
                // ShutdownAsync has cancelled it, and the manager supports restart.
                m_workerCts?.Dispose();
                m_workerCts = new CancellationTokenSource();

                m_publishWorkerTask = StartPublishWorker(m_workerCts.Token);
                m_conditionRefreshWorkerTask = StartConditionRefreshWorker();
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
                // stop the publishing thread and trigger the condition refresh thread.
                SignalConditionRefreshWorkerShutdown();

                // Cancel so the publish loop's inter-cycle delay is abandoned
                // immediately instead of running to the end of its resolution.
                m_workerCts?.Cancel();

                Task? publishWorkerTask = m_publishWorkerTask;
                if (publishWorkerTask is not null)
                {
                    await publishWorkerTask.ConfigureAwait(false);
                    m_publishWorkerTask = null;
                }

                Task? conditionRefreshWorkerTask = m_conditionRefreshWorkerTask;
                if (conditionRefreshWorkerTask is not null)
                {
                    await conditionRefreshWorkerTask.ConfigureAwait(false);
                    m_conditionRefreshWorkerTask = null;
                }

                m_workerCts?.Dispose();
                m_workerCts = null;

                // Expired-subscription cleanups scheduled by the publish sweep
                // still delete subscriptions through the server, so drain them
                // before the queues and subscriptions go away.
                await m_backgroundWork.DisposeAsync().ConfigureAwait(false);

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
                m_expiringSubscriptions.Clear();
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
                if (await m_subscriptionStore
                    .StoreSubscriptionsAsync(subscriptionsToStore, cancellationToken)
                    .ConfigureAwait(false))
                {
                    m_logger.CountSubscriptionsStored(subscriptionsToStore.Count);
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToStoreCountSubscriptions(ex, subscriptionsToStore.Count);
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
                restoreResult = await m_subscriptionStore
                    .RestoreSubscriptionsAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.FailedToRestoreSubscriptions(ex);
                return;
            }

            if (!restoreResult.Success ||
                restoreResult.Subscriptions == null ||
                !restoreResult.Subscriptions.Any())
            {
                return;
            }

            var createdSubscriptions = new Dictionary<uint, ArrayOf<uint>>();

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
                    m_logger.FailedToRestoreSubscritptionWithIdSubscriptionId(ex, storedSubscription.Id);
                    continue;
                }

                subscription.GetMonitoredItems(out ArrayOf<uint> monitoredItemsIds, out _);
                createdSubscriptions.Add(subscription.Id, monitoredItemsIds);
            }

            m_lastSubscriptionId = restoreResult.Subscriptions.Max(s => s.Id);

            await m_subscriptionStore
                .OnSubscriptionRestoreCompleteAsync(createdSubscriptions, cancellationToken)
                .ConfigureAwait(false);
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

            m_server.UpdateServerDiagnostics(diagnostics =>
            {
                diagnostics.CurrentSubscriptionCount++;
                diagnostics.CumulatedSubscriptionCount++;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            });

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
            IList<ISubscription>? sessionSubscriptions = null;
            SessionPublishQueue? publishQueue = null;

            // close the publish queue for the session.
            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_publishQueues.TryRemove(sessionId, out publishQueue))
                {
                    sessionSubscriptions = publishQueue.Close();

                    // remove the subscriptions.
                    if (deleteSubscriptions)
                    {
                        for (int ii = 0; ii < sessionSubscriptions.Count; ii++)
                        {
                            m_subscriptions.TryRemove(sessionSubscriptions[ii].Id, out _);
                        }
                    }
                    else
                    {
                        for (int ii = 0; ii < sessionSubscriptions.Count; ii++)
                        {
                            ISubscription subscription = sessionSubscriptions[ii];
                            if (m_abandonedSubscriptions.TryAdd(
                                    subscription.Id,
                                    subscription))
                            {
                                m_logger.SubscriptionABANDONEDIdSubscriptionId(subscription.Id);
                            }
                        }
                    }
                }
            }
            finally
            {
                m_semaphoreSlim.Release();
                publishQueue?.Dispose();
            }

            // remove the expired subscription status change notifications for this session
            lock (m_statusMessagesLock)
            {
                if (m_statusMessages.TryGetValue(sessionId, out _))
                {
                    m_statusMessages.Remove(sessionId);
                }
            }

            // process all subscriptions in the queue.
            if (deleteSubscriptions && sessionSubscriptions != null)
            {
                for (int ii = 0; ii < sessionSubscriptions.Count; ii++)
                {
                    ISubscription subscription = sessionSubscriptions[ii];

                    // raise subscription event.
                    RaiseSubscriptionEvent(subscription, true);

                    // delete subscription.
                    await subscription.DeleteAsync(context, cancellationToken).ConfigureAwait(false);

                    // get the count for the diagnostics.
                    uint publishingIntervalCount = GetPublishingIntervalCount();
                    m_server.UpdateServerDiagnostics(diagnostics =>
                    {
                        diagnostics.CurrentSubscriptionCount--;
                        diagnostics.PublishingIntervalCount = publishingIntervalCount;
                    });
                }
            }
        }

        /// <summary>
        /// Refreshes the conditions for the specified subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void ConditionRefresh(OperationContext context, uint subscriptionId)
        {
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSubscriptionIdInvalid,
                    "Cannot refresh conditions for a subscription that does not exist.");
            }

            // ensure a condition refresh is allowed.
            subscription.ValidateConditionRefresh(context);

            var conditionRefreshTask = new ConditionRefreshTask(subscription, 0);

            ServiceResultException? serviceResultException = null;
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
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
                m_logger.SubscriptionConditionRefreshStartedIdSubscriptionId(
                    subscription.Id,
                    subscription.SessionId);
                await subscription.ConditionRefreshAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.SubscriptionDoConditionRefreshExitedUnexpectedly(e);
            }
        }

        /// <summary>
        /// Completes a refresh conditions request.
        /// </summary>
        private async ValueTask DoConditionRefresh2Async(ISubscription subscription, uint monitoredItemId, CancellationToken cancellationToken = default)
        {
            try
            {
                m_logger.SubscriptionConditionRefresh2StartedIdSubscriptionId(
                    subscription.Id,
                    subscription.SessionId,
                    monitoredItemId);
                await subscription.ConditionRefresh2Async(monitoredItemId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.SubscriptionDoConditionRefresh2ExitedUnexpectedly(e);
            }
        }

        /// <summary>
        /// Deletes the specified subscription.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<StatusCode> DeleteSubscriptionAsync(OperationContext context, uint subscriptionId, CancellationToken cancellationToken = default)
        {
            ISubscription? subscription = null;

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

                        if (m_publishQueues.TryGetValue(sessionId, out SessionPublishQueue? queue))
                        {
                            queue.Remove(subscription, true);
                        }
                    }
                }

                // check for abandoned subscription.
                if (m_abandonedSubscriptions.TryRemove(subscriptionId, out _))
                {
                    m_logger.SubscriptionDELETEDABANDONEDIdSubscriptionId(subscriptionId);
                }

                m_expiringSubscriptions.Remove(subscriptionId);

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

                m_server.UpdateServerDiagnostics(diagnostics =>
                {
                    diagnostics.CurrentSubscriptionCount--;
                    diagnostics.PublishingIntervalCount = publishingIntervalCount;
                });

                if (context != null && context.Session != null)
                {
                    context.Session.UpdateDiagnostics(diagnostics =>
                    {
                        diagnostics.CurrentSubscriptionsCount--;
                        UpdateCurrentMonitoredItemsCount(diagnostics, -monitoredItemCount);
                    });
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
            if (session.IsClosing)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }

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
                            m_maxPublishRequestCount,
                            m_timeProvider);

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
                    out Queue<StatusMessage>? messagesQueue))
                {
                    m_statusMessages[session.Id] = new Queue<StatusMessage>();
                }
            }

            m_server.UpdateServerDiagnostics(diagnostics =>
            {
                diagnostics.CurrentSubscriptionCount++;
                diagnostics.CumulatedSubscriptionCount++;
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            });

            if (context.Session != null)
            {
                context.Session.UpdateDiagnostics(
                    diagnostics => diagnostics.CurrentSubscriptionsCount++);
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
            ArrayOf<uint> subscriptionIds,
            CancellationToken cancellationToken = default)
        {
            bool diagnosticsExist = false;
            var results = new List<StatusCode>(subscriptionIds.Count);
            var diagnosticInfos = new List<DiagnosticInfo>(subscriptionIds.Count);

            foreach (uint subscriptionId in subscriptionIds.ToList())
            {
                try
                {
                    StatusCode result = await DeleteSubscriptionAsync(context, subscriptionId, cancellationToken).ConfigureAwait(false);
                    results.Add(result);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null!);
                    }
                }
                catch (Exception e)
                {
                    m_logger.ErrorOccurredInDeleteSubscriptions(e, context.SessionId, subscriptionId);

                    var result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        string.Empty);
                    results.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo? diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            result,
                            m_logger);
                        diagnosticInfos.Add(diagnosticInfo!);
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
                        out Queue<StatusMessage>? queue))
                {
                    queue.Enqueue(message);
                }
            }
        }

        /// <summary>
        /// Claims an expiration from the exact current session queue entry.
        /// </summary>
        internal bool TryClaimSubscriptionExpiration(
            SessionPublishQueue sourceQueue,
            ISession sourceSession,
            SessionPublishQueue.QueuedSubscription queuedSubscription)
        {
            ISubscription subscription = queuedSubscription.Subscription;
            m_semaphoreSlim.Wait();
            try
            {
                if (!m_subscriptions.TryGetValue(
                        subscription.Id,
                        out ISubscription? currentSubscription) ||
                    !ReferenceEquals(currentSubscription, subscription) ||
                    m_expiringSubscriptions.ContainsKey(subscription.Id) ||
                    !ReferenceEquals(subscription.Session, sourceSession) ||
                    !m_publishQueues.TryGetValue(
                        sourceSession.Id,
                        out SessionPublishQueue? currentQueue) ||
                    !ReferenceEquals(currentQueue, sourceQueue))
                {
                    return false;
                }

                m_expiringSubscriptions.Add(subscription.Id, subscription);
                if (sourceQueue.TryRemoveForExpiration(queuedSubscription))
                {
                    return true;
                }

                m_expiringSubscriptions.Remove(subscription.Id);
                return false;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        private bool TryClaimAbandonedSubscriptionExpiration(ISubscription subscription)
        {
            m_semaphoreSlim.Wait();
            try
            {
                if (!m_subscriptions.TryGetValue(
                        subscription.Id,
                        out ISubscription? currentSubscription) ||
                    !ReferenceEquals(currentSubscription, subscription) ||
                    m_expiringSubscriptions.ContainsKey(subscription.Id) ||
                    subscription.Session != null)
                {
                    return false;
                }

                m_expiringSubscriptions.Add(subscription.Id, subscription);
                if (TryRemoveAbandonedSubscription(subscription))
                {
                    return true;
                }

                m_expiringSubscriptions.Remove(subscription.Id);
                return false;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        private bool TryRemoveAbandonedSubscription(ISubscription subscription)
        {
            var entry = new KeyValuePair<uint, ISubscription>(
                subscription.Id,
                subscription);
            return ((ICollection<KeyValuePair<uint, ISubscription>>)m_abandonedSubscriptions)
                .Remove(entry);
        }

        private bool ContainsAbandonedSubscription(ISubscription subscription)
        {
            return m_abandonedSubscriptions.TryGetValue(
                    subscription.Id,
                    out ISubscription? currentSubscription) &&
                ReferenceEquals(currentSubscription, subscription);
        }

        /// <summary>
        /// Publishes a subscription. When the request parks (waits for the next
        /// notification), the supplied park sink is notified so the request-processing
        /// worker can be released for the duration of the wait.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<PublishResponse> PublishAsync(
            OperationContext context,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            IRequestParkSink? parkSink,
            CancellationToken cancellationToken = default)
        {
            // get publish queue for session.
            if (!m_publishQueues.TryGetValue(context.Session.Id, out SessionPublishQueue? queue))
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
                out ArrayOf<StatusCode> acknowledgeResults,
                out ArrayOf<DiagnosticInfo> acknowledgeDiagnosticInfos);

            // update diagnostics.
            if (context.Session != null)
            {
                context.Session.UpdateDiagnostics(
                    diagnostics => diagnostics.CurrentPublishRequestsInQueue++);
            }

            try
            {
                m_logger.PublishClientHandleReceivedFromClient(context.ClientHandle, context.SessionId);

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
                    // Publish requests always carry a channel context.
                    ISubscription subscription = await queue.PublishAsync(
                        context.ChannelContext!.SecureChannelId,
                        context.OperationDeadline,
                        requeue,
                        parkSink,
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
                        NotificationMessage? message = subscription.Publish(
                            context,
                            out ArrayOf<uint> availableSequenceNumbers,
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
                        m_logger.PublishFalseAlarmRequestClientHandleRequeued(
                            context.ClientHandle,
                            context.SessionId,
                            subscription.Id);
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
                    context.Session.UpdateDiagnostics(
                        diagnostics => diagnostics.CurrentPublishRequestsInQueue--);
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

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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

            m_server.UpdateServerDiagnostics(diagnostics =>
            {
                diagnostics.PublishingIntervalCount = publishingIntervalCount;
            });
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

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
            ArrayOf<uint> subscriptionIds,
            out ArrayOf<StatusCode> results,
            out ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            bool diagnosticsExist = false;

            var resultList = new List<StatusCode>(subscriptionIds.Count);
            var diagnosticInfoList = new List<DiagnosticInfo>(subscriptionIds.Count);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                try
                {
                    // find subscription.

                    if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out ISubscription? subscription))
                    {
                        throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                    }

                    // update the subscription.
                    subscription.SetPublishingMode(context, publishingEnabled);

                    // save results.
                    resultList.Add(StatusCodes.Good);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfoList.Add(null!);
                    }
                }
                catch (Exception e)
                {
                    if (e is not ServiceResultException)
                    {
                        m_logger.ErrorOccurredInSetPublishingMode(e, context.SessionId, subscriptionIds[ii]);
                    }

                    var result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        string.Empty);
                    resultList.Add(result.Code);

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo? diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            m_server,
                            context,
                            result,
                            m_logger);
                        diagnosticInfoList.Add(diagnosticInfo!);
                        diagnosticsExist = true;
                    }
                }

                if (!diagnosticsExist)
                {
                    diagnosticInfoList.Clear();
                }
            }
            results = resultList;
            diagnosticInfos = diagnosticInfoList;
        }

        /// <summary>
        /// Attaches a groups of subscriptions to a different session.
        /// </summary>
        public async ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            OperationContext context,
            ArrayOf<uint> subscriptionIds,
            bool sendInitialValues,
            CancellationToken cancellationToken = default)
        {
            if (context.Session.IsClosing)
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }

            var results = new List<TransferResult>();
            var diagnosticInfos = new List<DiagnosticInfo>();

            m_logger.TransferSubscriptionsToSessionIdSessionIdCount(
                context.Session.Id,
                subscriptionIds.Count,
                sendInitialValues);

            for (int ii = 0; ii < subscriptionIds.Count; ii++)
            {
                var result = new TransferResult();
                try
                {
                    // find subscription.
                    if (!m_subscriptions.TryGetValue(subscriptionIds[ii], out ISubscription? subscription))
                    {
                        result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                        results.Add(result);
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos.Add(null!);
                        }
                        continue;
                    }

                    subscription.UpdateDiagnostics(
                        diagnostics => diagnostics.TransferRequestCount++);

                    ISession ownerSession = null!;
                    var concreteSubscription = subscription as Subscription;
                    SessionPublishQueue? sourcePublishQueue = null;
                    SessionPublishQueue.SubscriptionTransferClaim? sourceQueueClaim = null;
                    bool sourceIsAbandoned = false;
                    bool sourceRemoved = false;
                    bool transferStarted = false;
                    Subscription.PreparedSessionTransfer? preparedTransfer = null;
                    SessionPublishQueue? destinationPublishQueue = null;
                    bool destinationAdded = false;
                    await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (!m_subscriptions.TryGetValue(
                                subscriptionIds[ii],
                                out ISubscription? currentSubscription) ||
                            !ReferenceEquals(currentSubscription, subscription) ||
                            m_expiringSubscriptions.ContainsKey(subscription.Id))
                        {
                            result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                            results.Add(result);
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                diagnosticInfos.Add(null!);
                            }
                            continue;
                        }

                        // check if new and old sessions are different
                        ownerSession = subscription.Session;
                        if (ownerSession != null &&
                            !ownerSession.Id.IsNull &&
                            ownerSession.Id == context.Session.Id)
                        {
                            result.StatusCode = StatusCodes.BadNothingToDo;
                            results.Add(result);
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                diagnosticInfos.Add(null!);
                            }
                            continue;
                        }

                        // Validate that the old and new Sessions represent the same
                        // ClientUserId. Issued tokens may be refreshed while preserving
                        // the authenticated owner, so raw token equality is not sufficient.
                        bool validIdentity = subscription.IsTransferIdentityCompatible(context.Session);

                        // Test if anonymous user is using a secure session using Sign or SignAndEncrypt
                        if (validIdentity &&
                            subscription.EffectiveIdentity.TokenType == UserTokenType.Anonymous)
                        {
                            MessageSecurityMode securityMode = context!.ChannelContext!
                                .EndpointDescription!
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
                                diagnosticInfos.Add(null!);
                            }
                            continue;
                        }

                        // Claim the exact current source before any fallible monitored-item
                        // callback can run. Lock order is manager semaphore, then queue lock,
                        // then subscription lock; rollback follows the same order.
                        if (ownerSession != null)
                        {
                            if (!m_publishQueues.TryGetValue(
                                    ownerSession.Id,
                                    out sourcePublishQueue) ||
                                sourcePublishQueue == null)
                            {
                                result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                results.Add(result);
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    diagnosticInfos.Add(null!);
                                }
                                continue;
                            }

                            if (concreteSubscription != null)
                            {
                                if (!sourcePublishQueue.TryClaimForTransfer(
                                        concreteSubscription,
                                        ownerSession,
                                        out sourceQueueClaim))
                                {
                                    result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                    results.Add(result);
                                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                    {
                                        diagnosticInfos.Add(null!);
                                    }
                                    continue;
                                }
                                sourceRemoved = true;
                                transferStarted = true;
                            }
                            else
                            {
                                sourceRemoved = sourcePublishQueue.TryRemoveForTransfer(subscription);
                                if (!sourceRemoved)
                                {
                                    result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                    results.Add(result);
                                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                    {
                                        diagnosticInfos.Add(null!);
                                    }
                                    continue;
                                }
                            }
                        }
                        else if (ContainsAbandonedSubscription(subscription))
                        {
                            sourceIsAbandoned = true;
                            if (concreteSubscription != null &&
                                !concreteSubscription.TryBeginTransfer(null))
                            {
                                result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                results.Add(result);
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    diagnosticInfos.Add(null!);
                                }
                                continue;
                            }
                            transferStarted = concreteSubscription != null;
                            sourceRemoved = TryRemoveAbandonedSubscription(subscription);
                            if (!sourceRemoved)
                            {
                                concreteSubscription?.AbortTransfer(null);
                                result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                results.Add(result);
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    diagnosticInfos.Add(null!);
                                }
                                continue;
                            }
                        }
                        else if (m_abandonedSubscriptions.ContainsKey(subscription.Id))
                        {
                            result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                            results.Add(result);
                            if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                            {
                                diagnosticInfos.Add(null!);
                            }
                            continue;
                        }
                        else if (concreteSubscription != null)
                        {
                            if (!concreteSubscription.TryBeginTransfer(null))
                            {
                                result.StatusCode = StatusCodes.BadSubscriptionIdInvalid;
                                results.Add(result);
                                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                                {
                                    diagnosticInfos.Add(null!);
                                }
                                continue;
                            }
                            transferStarted = true;
                        }

                        try
                        {
                            if (concreteSubscription != null)
                            {
                                preparedTransfer = await concreteSubscription
                                    .PrepareSessionTransferAsync(
                                        context,
                                        ownerSession,
                                        sendInitialValues,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                preparedTransfer.CommitOwnership();
                            }
                            else
                            {
                                await subscription.TransferSessionAsync(
                                        context,
                                        sendInitialValues,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            // add to queue in new session, create queue if necessary
                            if (!m_publishQueues.TryGetValue(
                                    context.SessionId,
                                    out destinationPublishQueue) ||
                                destinationPublishQueue == null)
                            {
                                m_publishQueues[context.SessionId]
                                    = destinationPublishQueue = new SessionPublishQueue(
                                    m_server,
                                    context.Session,
                                    m_maxPublishRequestCount,
                                    m_timeProvider);
                            }
                            destinationPublishQueue.Add(subscription);
                            destinationAdded = true;
                            preparedTransfer?.CommitMonitoredItemEffects();
                            if (concreteSubscription != null)
                            {
                                concreteSubscription.CompleteTransfer(context.Session);
                            }
                            if (sourceQueueClaim != null)
                            {
                                sourcePublishQueue!.CompleteTransferClaim(sourceQueueClaim);
                            }
                            preparedTransfer?.Complete();
                        }
                        catch (Exception transferError)
                        {
                            var rollbackErrors = new List<Exception>();
                            if (destinationAdded && destinationPublishQueue != null)
                            {
                                destinationPublishQueue.TryRemoveForTransfer(subscription);
                            }

                            if (preparedTransfer != null)
                            {
                                try
                                {
                                    await preparedTransfer.RollbackAsync(CancellationToken.None)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception rollbackError)
                                {
                                    rollbackErrors.Add(rollbackError);
                                }
                            }
                            else if (!ReferenceEquals(subscription.Session, ownerSession) &&
                                concreteSubscription != null &&
                                !concreteSubscription.TryRestoreSessionAfterFailedTransfer(
                                    context.Session,
                                    ownerSession))
                            {
                                rollbackErrors.Add(
                                    new ServiceResultException(
                                        StatusCodes.BadSubscriptionIdInvalid,
                                        "Subscription ownership could not be restored."));
                            }

                            if (sourceQueueClaim != null &&
                                !sourcePublishQueue!.RestoreTransferClaim(sourceQueueClaim))
                            {
                                rollbackErrors.Add(
                                    new ServiceResultException(
                                        StatusCodes.BadSubscriptionIdInvalid,
                                        "Subscription source queue could not be restored."));
                            }
                            else if (sourceQueueClaim == null &&
                                sourceRemoved &&
                                sourcePublishQueue != null)
                            {
                                sourcePublishQueue.Add(subscription);
                            }
                            else if (sourceRemoved && sourceIsAbandoned &&
                                !m_abandonedSubscriptions.TryAdd(
                                    subscription.Id,
                                    subscription))
                            {
                                rollbackErrors.Add(
                                    new ServiceResultException(
                                        StatusCodes.BadSubscriptionIdInvalid,
                                        "Abandoned subscription source could not be restored."));
                            }

                            if (transferStarted)
                            {
                                concreteSubscription!.AbortTransfer(ownerSession);
                            }

                            if (rollbackErrors.Count > 0)
                            {
                                rollbackErrors.Insert(0, transferError);
                                throw new AggregateException(rollbackErrors);
                            }
                            throw;
                        }
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
                                out Queue<StatusMessage>? messagesQueue) &&
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
                        context.Session.UpdateDiagnostics(
                            diagnostics => diagnostics.CurrentSubscriptionsCount++);
                    }

                    // raise subscription event.
                    RaiseSubscriptionEvent(subscription, false);
                    result.StatusCode = StatusCodes.Good;

                    // Notify old session with Good_SubscriptionTransferred.
                    if (ownerSession != null)
                    {
                        ownerSession.UpdateDiagnostics(
                            diagnostics => diagnostics.CurrentSubscriptionsCount--);

                        // queue the Good_SubscriptionTransferred message
                        bool statusQueued = false;
                        lock (m_statusMessagesLock)
                        {
                            if (!ownerSession.Id.IsNull &&
                                m_statusMessages.TryGetValue(
                                    ownerSession.Id,
                                    out Queue<StatusMessage>? queue))
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
                                    out SessionPublishQueue? ownerPublishQueue) &&
                                ownerPublishQueue != null)
                            {
                                if (statusQueued)
                                {
                                    // queue the status message
                                    bool success = ownerPublishQueue.TryPublishCustomStatus(
                                        StatusCodes.GoodSubscriptionTransferred);
                                    if (!success)
                                    {
                                        m_logger.FailedToQueueGoodSubscriptionTransferredForSessionId(
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

                    subscription.UpdateDiagnostics(
                        diagnostics => diagnostics.TransferredToSameClientCount++);

                    // save results.
                    results.Add(result);
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null!);
                    }

                    m_logger.TransferredSubscriptionIdSubscriptionIdToSessionId(subscription.Id, context.Session!.Id);
                }
                catch (Exception e)
                {
                    result.StatusCode = StatusCodes.Bad;
                    if (results.Count == ii)
                    {
                        results.Add(result);
                    }
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0 &&
                        diagnosticInfos.Count == ii)
                    {
                        diagnosticInfos.Add(
                            new DiagnosticInfo(e, context.DiagnosticsMask, false, null!, m_logger));
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
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
            ArrayOf<uint> linksToAdd,
            ArrayOf<uint> linksToRemove,
            out ArrayOf<StatusCode> addResults,
            out ArrayOf<DiagnosticInfo> addDiagnosticInfos,
            out ArrayOf<StatusCode> removeResults,
            out ArrayOf<DiagnosticInfo> removeDiagnosticInfos)
        {
            // find subscription.

            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
                context.Session.UpdateDiagnostics(
                    diagnostics => UpdateCurrentMonitoredItemsCount(
                        diagnostics, monitoredItemCountIncrement));
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
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
            ArrayOf<uint> monitoredItemIds,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
                context.Session.UpdateDiagnostics(
                    diagnostics => UpdateCurrentMonitoredItemsCount(
                        diagnostics, monitoredItemCountIncrement));
            }

            return response;
        }

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)> SetMonitoringModeAsync(
            OperationContext context,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            ArrayOf<uint> monitoredItemIds,
            CancellationToken cancellationToken = default)
        {
            // find subscription.
            if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
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
        /// Calculates the revised sampling interval of a data change monitored item.
        /// </summary>
        /// <remarks>
        /// A requested sampling interval below zero is resolved to the publishing interval of
        /// the subscription. Nodes that declare
        /// <see cref="MinimumSamplingIntervals.Continuous"/> report by exception and are
        /// therefore not bound by the server wide minimum supported sample rate. For every
        /// other item the revised interval is raised to the larger of the minimum sampling
        /// interval declared by the node and the minimum supported sample rate of the server.
        /// Callers pass <see cref="MinimumSamplingIntervals.Indeterminate"/> when the node does
        /// not declare a minimum sampling interval, for example when an Attribute other than
        /// Value is monitored.
        /// </remarks>
        /// <param name="requestedSamplingInterval">The sampling interval requested by the client.</param>
        /// <param name="publishingInterval">The publishing interval of the subscription.</param>
        /// <param name="nodeMinimumSamplingInterval">
        /// The minimum sampling interval declared by the monitored node.
        /// </param>
        /// <param name="minSupportedSampleRate">
        /// The minimum sample rate supported by the server.
        /// </param>
        /// <returns>The revised sampling interval.</returns>
        public static double CalculateRevisedSamplingInterval(
            double requestedSamplingInterval,
            double publishingInterval,
            double nodeMinimumSamplingInterval,
            double minSupportedSampleRate)
        {
            double samplingInterval = requestedSamplingInterval;

            if (samplingInterval < 0)
            {
                samplingInterval = publishingInterval;
            }

            // items that report by exception are not bound by a sampling rate.
            if (nodeMinimumSamplingInterval != MinimumSamplingIntervals.Continuous)
            {
                double minimumSamplingInterval = Math.Max(
                    nodeMinimumSamplingInterval,
                    minSupportedSampleRate);

                if (samplingInterval < minimumSamplingInterval)
                {
                    samplingInterval = minimumSamplingInterval;
                }
            }

            // put a large upper limit on sampling.
            if (samplingInterval == double.MaxValue)
            {
                samplingInterval = 365 * 24 * 3600 * 1000.0;
            }

            return samplingInterval;
        }

        /// <summary>
        /// Calculates the revised sampling interval of a data change monitored item that
        /// monitors an Attribute of the specified node.
        /// </summary>
        /// <remarks>
        /// The minimum sampling interval declared by a node only applies to the Value
        /// Attribute of a Variable. For every other Attribute
        /// <see cref="MinimumSamplingIntervals.Indeterminate"/> is assumed, which leaves the
        /// server wide minimum supported sample rate as the only lower bound.
        /// </remarks>
        /// <param name="requestedSamplingInterval">The sampling interval requested by the client.</param>
        /// <param name="publishingInterval">The publishing interval of the subscription.</param>
        /// <param name="node">The monitored node.</param>
        /// <param name="attributeId">The monitored Attribute.</param>
        /// <param name="minSupportedSampleRate">
        /// The minimum sample rate supported by the server.
        /// </param>
        /// <returns>The revised sampling interval.</returns>
        public static double CalculateRevisedSamplingInterval(
            double requestedSamplingInterval,
            double publishingInterval,
            NodeState? node,
            uint attributeId,
            double minSupportedSampleRate)
        {
            double nodeMinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;

            if (attributeId == Attributes.Value && node is BaseVariableState variable)
            {
                nodeMinimumSamplingInterval = variable.MinimumSamplingInterval;
            }

            return CalculateRevisedSamplingInterval(
                requestedSamplingInterval,
                publishingInterval,
                nodeMinimumSamplingInterval,
                minSupportedSampleRate);
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
            if (maxNotificationsPerPublish == 0)
            {
                return m_maxNotificationsPerPublish;
            }

            if (m_maxNotificationsPerPublish == 0 ||
                maxNotificationsPerPublish <= m_maxNotificationsPerPublish)
            {
                return maxNotificationsPerPublish;
            }

            return m_maxNotificationsPerPublish;
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
            message = null!;
            subscriptionId = 0;

            // check for status messages.
            lock (m_statusMessagesLock)
            {
                if (m_statusMessages.TryGetValue(
                        context.SessionId,
                        out Queue<StatusMessage>? statusQueue) &&
                    statusQueue.Count > 0)
                {
                    StatusMessage status = statusQueue.Dequeue();
                    subscriptionId = status.SubscriptionId;
                    message = status.Message!;
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
                m_logger.SubscriptionPublishTaskTaskIdX8Started(Task.CurrentId);

                int timeToWait = sleepCycle;

                while (true)
                {
                    // ConcurrentDictionary enumeration is thread-safe and provides a stable
                    // snapshot for the current pass without taking the manager semaphore.
                    SessionPublishQueue[] queues = [.. m_publishQueues.Values];
                    IReadOnlyList<ISubscription> abandonedSubscriptions =
                        CaptureAbandonedPublishTimerSnapshot();

                    // check the publish timer for each subscription. Each queue is
                    // independent (its own lock and subscription state), so at high
                    // session counts the O(N) sweep is parallelized across cores to
                    // keep a single publishing cycle within its resolution budget.
                    if (queues.Length >= kParallelPublishThreshold)
                    {
                        Parallel.For(
                            0,
                            queues.Length,
                            s_parallelPublishOptions,
                            ii => queues[ii].PublishTimerExpired());
                    }
                    else
                    {
                        for (int ii = 0; ii < queues.Length; ii++)
                        {
                            queues[ii].PublishTimerExpired();
                        }
                    }

                    ProcessAbandonedPublishTimers(abandonedSubscriptions);

                    if (m_shutdownEvent.WaitOne(0))
                    {
                        m_logger.SubscriptionPublishTaskTaskIdX8ExitedNormally(Task.CurrentId);
                        break;
                    }

                    await m_timeProvider.Delay(TimeSpan.FromMilliseconds(timeToWait), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                m_logger.SubscriptionPublishTaskTaskIdX8ExitedNormally2(Task.CurrentId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_logger.SubscriptionPublishTaskTaskIdX8ExitedNormally2(Task.CurrentId);
            }
            catch (Exception e)
            {
                m_logger.SubscriptionPublishTaskTaskIdX8ExitedUnexpectedly(e, Task.CurrentId);
            }
        }

        /// <summary>
        /// Captures the abandoned subscriptions processed by one publish timer pass.
        /// </summary>
        internal IReadOnlyList<ISubscription> CaptureAbandonedPublishTimerSnapshot()
        {
            return [.. m_abandonedSubscriptions.Values];
        }

        /// <summary>
        /// Checks the publish timer for an exact abandoned subscription snapshot.
        /// </summary>
        internal void ProcessAbandonedPublishTimers(
            IReadOnlyList<ISubscription> abandonedSubscriptions)
        {
            if (abandonedSubscriptions.Count == 0)
            {
                return;
            }

            var subscriptionsToDelete = new List<ISubscription>();
            for (int ii = 0; ii < abandonedSubscriptions.Count; ii++)
            {
                ISubscription subscription = abandonedSubscriptions[ii];
                if (!ContainsAbandonedSubscription(subscription) ||
                    subscription.PublishTimerExpired() != PublishingState.Expired ||
                    !TryClaimAbandonedSubscriptionExpiration(subscription))
                {
                    continue;
                }

                subscriptionsToDelete.Add(subscription);
                SubscriptionExpired(subscription);
                m_logger.SubscriptionAbandonedSubscriptionIdSubscriptionId(subscription.Id);
            }

            CleanupSubscriptions(m_server, subscriptionsToDelete, m_logger, m_backgroundWork);
        }

        /// <summary>
        /// A single thread to execute the condition refresh.
        /// </summary>
        private async Task ConditionRefreshWorkerAsync()
        {
            try
            {
                m_logger.SubscriptionConditionRefreshTaskTaskIdX8Started(Task.CurrentId);

                while (true)
                {
                    ConditionRefreshTask? conditionRefreshTask = null;
                    bool shutdownRequested = false;

                    lock (m_conditionRefreshLock)
                    {
                        if (m_conditionRefreshQueue.Count > 0)
                        {
                            conditionRefreshTask = m_conditionRefreshQueue.Dequeue();
                        }
                        else if (m_shutdownEvent.WaitOne(0))
                        {
                            shutdownRequested = true;
                        }
                        else
                        {
                            m_conditionRefreshEvent.Reset();
                        }
                    }

                    if (shutdownRequested)
                    {
                        m_logger.SubscriptionConditionRefreshTaskTaskIdX8Exited(Task.CurrentId);
                        break;
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
                        m_logger.SubscriptionConditionRefreshTaskTaskIdX8Exited(Task.CurrentId);
                        break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                m_logger.SubscriptionConditionRefreshTaskTaskIdX8Exited2(Task.CurrentId);
            }
            catch (Exception e)
            {
                m_logger.SubscriptionConditionRefreshTaskTaskIdX8Exited3(e, Task.CurrentId);
            }
        }

        /// <summary>
        /// Cleanups the subscriptions.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="subscriptionsToDelete">The subscriptions to delete.</param>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="backgroundWork">Owns the deletion so it is drained
        /// before the caller that scheduled it goes away.</param>
        internal static void CleanupSubscriptions(
            IServerInternal server,
            IList<ISubscription> subscriptionsToDelete,
            ILogger logger,
            BackgroundTaskScope backgroundWork)
        {
            if (subscriptionsToDelete != null && subscriptionsToDelete.Count > 0)
            {
                logger.ServerCountSubscriptionsScheduledForDelete(subscriptionsToDelete.Count);

                backgroundWork.Run(
                    nameof(CleanupSubscriptionsCoreAsync),
                    async ct => await CleanupSubscriptionsCoreAsync(
                        server, subscriptionsToDelete, logger, ct).ConfigureAwait(false));
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
                logger.ServerCleanupSubscriptionsTaskStarted();

                foreach (ISubscription subscription in subscriptionsToDelete)
                {
                    await server.DeleteSubscriptionAsync(subscription.Id, cancellationToken).ConfigureAwait(false);
                }

                logger.ServerCleanupSubscriptionsTaskCompleted();
            }
            catch (Exception e)
            {
                logger.ServerCleanupSubscriptionsTaskHaltedUnexpectedly(e);
            }
        }

        private class StatusMessage
        {
            public uint SubscriptionId;
            public NotificationMessage? Message;
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

            public override bool Equals(object? obj)
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

        /// <summary>
        /// Above this many session publish queues the per-cycle sweep is
        /// parallelized across cores so one publishing cycle keeps up with
        /// thousands of subscriptions instead of serializing on a single thread.
        /// </summary>
        private const int kParallelPublishThreshold = 256;

        private static readonly ParallelOptions s_parallelPublishOptions = new()
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);
        private uint m_lastSubscriptionId;
        private readonly ILogger m_logger;
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
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
        private readonly ConcurrentDictionary<uint, ISubscription> m_abandonedSubscriptions;
        private readonly Dictionary<uint, ISubscription> m_expiringSubscriptions;
        private readonly NodeIdDictionary<Queue<StatusMessage>> m_statusMessages;
        private readonly NodeIdDictionary<SessionPublishQueue> m_publishQueues;
        private readonly ManualResetEvent m_shutdownEvent;
        private readonly Queue<ConditionRefreshTask> m_conditionRefreshQueue;
        private readonly ManualResetEvent m_conditionRefreshEvent;
        private readonly ISubscriptionStore m_subscriptionStore;
        private Task? m_conditionRefreshWorkerTask;
        private readonly BackgroundTaskScope m_backgroundWork;
        private Task? m_publishWorkerTask;
        private CancellationTokenSource? m_workerCts;

        private readonly Lock m_statusMessagesLock = new();
        private readonly Lock m_eventLock = new();
        private readonly Lock m_conditionRefreshLock = new();
        private event SubscriptionEventHandler? m_SubscriptionCreated;
        private event SubscriptionEventHandler? m_SubscriptionDeleted;

        private Task StartConditionRefreshWorker()
        {
            m_conditionRefreshEvent.Reset();
            return Task.Factory.StartNew(
                    static state => ((SubscriptionManager)state!).ConditionRefreshWorkerAsync(),
                    this,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .Unwrap();
        }

        /// <summary>
        /// Starts the publish timer loop and returns a task that completes when the
        /// loop has actually exited.
        /// </summary>
        /// <remarks>
        /// The inner <c>AsTask</c> plus <c>Unwrap</c> matter: <see cref="Task.Factory"/>
        /// hands back a task that completes as soon as the loop first yields, so
        /// awaiting the raw <see cref="Task.Factory"/> result would only await the
        /// scheduling of the loop and let shutdown race ahead of it.
        /// </remarks>
        private Task StartPublishWorker(CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                    static state =>
                    {
                        (SubscriptionManager manager, CancellationToken ct) =
                            ((SubscriptionManager, CancellationToken))state!;
                        return manager
                            .PublishSubscriptionsAsync(manager.m_publishingResolution, ct)
                            .AsTask();
                    },
                    (this, cancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .Unwrap();
        }

        private void SignalConditionRefreshWorkerShutdown()
        {
            lock (m_conditionRefreshLock)
            {
                m_shutdownEvent.Set();
                m_conditionRefreshEvent.Set();
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for SubscriptionManager.
    /// </summary>
    internal static partial class SubscriptionManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 0, Level = LogLevel.Error,
            Message = "Subscription event handler raised an exception.")]
        public static partial void SubscriptionEventHandlerRaisedAnException(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 1, Level = LogLevel.Information,
            Message = "{Count} Subscriptions stored")]
        public static partial void CountSubscriptionsStored(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 2, Level = LogLevel.Error,
            Message = "Failed to store {Count} subscriptions")]
        public static partial void FailedToStoreCountSubscriptions(this ILogger logger, Exception ex, int count);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 3, Level = LogLevel.Error,
            Message = "Failed to restore subscriptions")]
        public static partial void FailedToRestoreSubscriptions(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 4, Level = LogLevel.Error,
            Message = "Failed to restore subscritption with id {SubscriptionId}")]
        public static partial void FailedToRestoreSubscritptionWithIdSubscriptionId(
            this ILogger logger,
            Exception ex,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 5, Level = LogLevel.Warning,
            Message = "Subscription ABANDONED, Id={SubscriptionId}.")]
        public static partial void SubscriptionABANDONEDIdSubscriptionId(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 6, Level = LogLevel.Trace,
            Message = "Subscription ConditionRefresh started, Id={SubscriptionId}, SessionId={SessionId}.")]
        public static partial void SubscriptionConditionRefreshStartedIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId,
            NodeId? sessionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 7, Level = LogLevel.Error,
            Message = "Subscription - DoConditionRefresh Exited Unexpectedly")]
        public static partial void SubscriptionDoConditionRefreshExitedUnexpectedly(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 8, Level = LogLevel.Trace,
            Message = "Subscription ConditionRefresh2 started, Id={SubscriptionId}, " +
                "SessionId={SessionId}, MonitoredItemId={MonitoredItemId}.")]
        public static partial void SubscriptionConditionRefresh2StartedIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId,
            NodeId? sessionId,
            uint monitoredItemId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 9, Level = LogLevel.Error,
            Message = "Subscription - DoConditionRefresh2 Exited Unexpectedly")]
        public static partial void SubscriptionDoConditionRefresh2ExitedUnexpectedly(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 10, Level = LogLevel.Warning,
            Message = "Subscription DELETED(ABANDONED), Id={SubscriptionId}.")]
        public static partial void SubscriptionDELETEDABANDONEDIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 11, Level = LogLevel.Error,
            Message = "Error occurred in DeleteSubscriptions, SessionId={SessionId}, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void ErrorOccurredInDeleteSubscriptions(
            this ILogger logger,
            Exception ex,
            NodeId? sessionId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 12, Level = LogLevel.Trace,
            Message = "Publish #{ClientHandle} ReceivedFromClient, SessionId={SessionId}")]
        public static partial void PublishClientHandleReceivedFromClient(
            this ILogger logger,
            uint clientHandle,
            NodeId? sessionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 13, Level = LogLevel.Trace,
            Message = "Publish False Alarm - Request #{ClientHandle} Requeued, " +
                "SessionId={SessionId}, SubscriptionId={SubscriptionId}.")]
        public static partial void PublishFalseAlarmRequestClientHandleRequeued(
            this ILogger logger,
            uint clientHandle,
            NodeId? sessionId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 14, Level = LogLevel.Error,
            Message = "Error occurred in SetPublishingMode, SessionId={SessionId}, " +
                "SubscriptionId={SubscriptionId}")]
        public static partial void ErrorOccurredInSetPublishingMode(
            this ILogger logger,
            Exception ex,
            NodeId? sessionId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 15, Level = LogLevel.Information,
            Message = "TransferSubscriptions to SessionId={SessionId}, Count={Count}, " +
                "sendInitialValues={SendInitialValues}")]
        public static partial void TransferSubscriptionsToSessionIdSessionIdCount(
            this ILogger logger,
            NodeId sessionId,
            int count,
            bool sendInitialValues);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 16, Level = LogLevel.Warning,
            Message = "Failed to queue Good_SubscriptionTransferred for SessionId {SessionId}, SubscriptionId " +
                "{SubscriptionId} due to an empty request queue.")]
        public static partial void FailedToQueueGoodSubscriptionTransferredForSessionId(
            this ILogger logger,
            NodeId sessionId,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 17, Level = LogLevel.Information,
            Message = "Transferred subscription Id {SubscriptionId} to SessionId {SessionId}")]
        public static partial void TransferredSubscriptionIdSubscriptionIdToSessionId(
            this ILogger logger,
            uint subscriptionId,
            NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 18, Level = LogLevel.Information,
            Message = "Subscription - Publish Task {TaskId:X8} Started.")]
        public static partial void SubscriptionPublishTaskTaskIdX8Started(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 19, Level = LogLevel.Information,
            Message = "Subscription - Abandoned Subscription Id={SubscriptionId} Delete Scheduled.")]
        public static partial void SubscriptionAbandonedSubscriptionIdSubscriptionId(
            this ILogger logger,
            uint subscriptionId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 20, Level = LogLevel.Information,
            Message = "Subscription - Publish Task {TaskId:X8} Exited Normally.")]
        public static partial void SubscriptionPublishTaskTaskIdX8ExitedNormally(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 21, Level = LogLevel.Information,
            Message = "Subscription - Publish Task {TaskId:X8} Exited Normally (disposed during shutdown).")]
        public static partial void SubscriptionPublishTaskTaskIdX8ExitedNormally2(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 22, Level = LogLevel.Error,
            Message = "Subscription - Publish Task {TaskId:X8} Exited Unexpectedly.")]
        public static partial void SubscriptionPublishTaskTaskIdX8ExitedUnexpectedly(
            this ILogger logger,
            Exception ex,
            int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 23, Level = LogLevel.Information,
            Message = "Subscription - ConditionRefresh Task {TaskId:X8} Started.")]
        public static partial void SubscriptionConditionRefreshTaskTaskIdX8Started(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 24, Level = LogLevel.Information,
            Message = "Subscription - ConditionRefresh Task {TaskId:X8} Exited Normally.")]
        public static partial void SubscriptionConditionRefreshTaskTaskIdX8Exited(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 25, Level = LogLevel.Information,
            Message = "Subscription - ConditionRefresh Task {TaskId:X8} Exited Normally (disposed during shutdown).")]
        public static partial void SubscriptionConditionRefreshTaskTaskIdX8Exited2(this ILogger logger, int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 26, Level = LogLevel.Error,
            Message = "Subscription - ConditionRefresh Task {TaskId:X8} Exited Unexpectedly.")]
        public static partial void SubscriptionConditionRefreshTaskTaskIdX8Exited3(
            this ILogger logger,
            Exception ex,
            int? taskId);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 27, Level = LogLevel.Information,
            Message = "Server - {Count} Subscriptions scheduled for delete.")]
        public static partial void ServerCountSubscriptionsScheduledForDelete(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 28, Level = LogLevel.Information,
            Message = "Server - CleanupSubscriptions Task Started")]
        public static partial void ServerCleanupSubscriptionsTaskStarted(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 29, Level = LogLevel.Information,
            Message = "Server - CleanupSubscriptions Task Completed")]
        public static partial void ServerCleanupSubscriptionsTaskCompleted(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.SubscriptionManager + 30, Level = LogLevel.Error,
            Message = "Server - CleanupSubscriptions Task Halted Unexpectedly")]
        public static partial void ServerCleanupSubscriptionsTaskHaltedUnexpectedly(this ILogger logger, Exception ex);
    }

}
