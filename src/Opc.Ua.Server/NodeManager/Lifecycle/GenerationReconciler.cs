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
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reconciles a published NodeManager generation with the server's active sessions and all-events items.
    /// </summary>
    internal sealed class GenerationReconciler
    {
        /// <summary>
        /// Creates a reconciler for one running server instance.
        /// </summary>
        /// <param name="server">The server whose live bindings are reconciled.</param>
        internal GenerationReconciler(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Binds a generation to the currently active sessions and all-events monitored items.
        /// </summary>
        /// <param name="generation">The generation to bind.</param>
        /// <param name="callerContext">The lifecycle caller context, if any.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The bindings successfully established for this generation.</returns>
        internal async ValueTask<Bindings> BindAsync(
            NodeManagerGeneration generation,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            IAsyncNodeManager nodeManager = generation.NodeManager;
            var bindings = new Bindings();
            try
            {
                foreach (ISession session in m_server.SessionManager.GetSessions())
                {
                    if (!session.Activated || session.IsClosing)
                    {
                        continue;
                    }

                    SessionBinding? binding = await ActivateSessionAsync(
                        generation, session, cancellationToken).ConfigureAwait(false);
                    if (binding is not null)
                    {
                        bindings.Sessions[session.Id] = binding;
                    }
                }

                foreach (IEventMonitoredItem monitoredItem in m_server.EventManager.GetMonitoredItems())
                {
                    if (!monitoredItem.MonitoringAllEvents)
                    {
                        continue;
                    }

                    if (await SubscribeToAllEventsAsync(
                        nodeManager, monitoredItem, callerContext, cancellationToken).ConfigureAwait(false))
                    {
                        bindings.EventMonitoredItems[monitoredItem.Id] = monitoredItem;
                    }
                }
                return bindings;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                try
                {
                    await UnbindAsync(generation, bindings, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "NodeManager binding and cleanup both failed.", failure, cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>
        /// Reconciles bindings after publication against the server's current session and item sets.
        /// </summary>
        /// <param name="nodeManager">The generation's manager.</param>
        /// <param name="bindings">The bindings established before publication.</param>
        /// <param name="callerContext">The lifecycle caller context, if any.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the current binding set has been reconciled.</returns>
        internal async ValueTask ReconcileAsync(
            IAsyncNodeManager nodeManager,
            Bindings bindings,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            NodeManagerGeneration generation = GetCurrentGeneration(nodeManager);
            ISession[] currentSessions =
            [
                .. m_server.SessionManager
                    .GetSessions()
                    .Where(session => session.Activated && !session.IsClosing)
            ];
            Dictionary<NodeId, ISession> currentSessionsById = currentSessions.ToDictionary(session => session.Id);
            foreach (KeyValuePair<NodeId, GenerationSessionBindingRecord> binding in
                generation.SessionBindings.ToArray())
            {
                if (currentSessionsById.ContainsKey(binding.Key))
                {
                    continue;
                }

                await CloseSessionAsync(generation, binding.Key, cancellationToken).ConfigureAwait(false);
                bindings.Sessions.Remove(binding.Key);
            }

            foreach (ISession session in currentSessions)
            {
                SessionBinding? newBinding = await ActivateSessionAsync(
                    generation, session, cancellationToken).ConfigureAwait(false);
                if (newBinding is not null)
                {
                    bindings.Sessions[session.Id] = newBinding;
                }
            }

            IList<IEventMonitoredItem> currentItems = m_server.EventManager.GetMonitoredItems();
            var currentItemsById = currentItems.ToDictionary(item => item.Id);
            foreach (KeyValuePair<IEventMonitoredItem, GenerationEventBindingRecord> binding in
                generation.EventBindings.ToArray())
            {
                if (currentItemsById.TryGetValue(binding.Key.Id, out IEventMonitoredItem? currentItem) &&
                    ReferenceEquals(currentItem, binding.Key) &&
                    currentItem.MonitoringAllEvents)
                {
                    continue;
                }

                await ChangeAllEventsSubscriptionAsync(
                    generation,
                    binding.Key,
                    (unsubscribe, ct) => InvokeAllEventsAsync(
                        nodeManager, binding.Key, unsubscribe, ct),
                    unsubscribe: true,
                    cancellationToken).ConfigureAwait(false);
                bindings.EventMonitoredItems.Remove(binding.Key.Id);
            }

            foreach (IEventMonitoredItem monitoredItem in currentItems)
            {
                if (!monitoredItem.MonitoringAllEvents)
                {
                    continue;
                }

                if (await SubscribeToAllEventsAsync(
                    nodeManager, monitoredItem, callerContext, cancellationToken).ConfigureAwait(false))
                {
                    bindings.EventMonitoredItems[monitoredItem.Id] = monitoredItem;
                }
            }
        }

        /// <summary>
        /// Removes a manager's server bindings by enumerating the current server state.
        /// </summary>
        /// <param name="nodeManager">The manager to unbind.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="unsubscribeAllEvents">Whether to remove all-events subscriptions.</param>
        /// <returns>A task that completes when the bindings have been removed.</returns>
        internal async ValueTask UnbindAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken cancellationToken,
            bool unsubscribeAllEvents = true)
        {
            if (unsubscribeAllEvents)
            {
                foreach (IEventMonitoredItem monitoredItem in m_server.EventManager.GetMonitoredItems())
                {
                    if (!monitoredItem.MonitoringAllEvents)
                    {
                        continue;
                    }

                    using var eventContext = new OperationContext(monitoredItem);
                    ServiceResult result = await nodeManager.SubscribeToAllEventsAsync(
                        eventContext, monitoredItem.SubscriptionId, monitoredItem, true, cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported)
                    {
                        throw new ServiceResultException(result);
                    }
                }
            }

            foreach (ISession session in m_server.SessionManager.GetSessions())
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                await nodeManager.SessionClosingAsync(
                    context, session.Id, deleteSubscriptions: false, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes a retired generation's bindings. All-events releases go through the generation's
        /// binding records, so a failed release is retained and retried exactly, even after the
        /// monitored item was deleted from the server.
        /// </summary>
        /// <param name="generation">The retired generation to unbind.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="unsubscribeAllEvents">Whether to remove all-events subscriptions.</param>
        /// <returns>A task that completes when the bindings have been removed.</returns>
        /// <exception cref="ServiceResultException">A manager rejected an all-events release.</exception>
        internal async ValueTask UnbindRetiredAsync(
            NodeManagerGeneration generation,
            bool unsubscribeAllEvents,
            CancellationToken cancellationToken)
        {
            IAsyncNodeManager nodeManager = generation.NodeManager;
            if (unsubscribeAllEvents)
            {
                await ReleaseRetiredEventsAsync(generation, cancellationToken).ConfigureAwait(false);
            }

            foreach (ISession session in m_server.SessionManager.GetSessions())
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                await nodeManager.SessionClosingAsync(
                    context, session.Id, deleteSubscriptions: false, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Seals a retired generation's all-events bindings and releases every recorded binding exactly once.
        /// Every all-events binding of a published generation is created through its record, so an item created
        /// against a replacement is never released on the retired generation. Later calls retry only releases
        /// that failed and were retained.
        /// </summary>
        /// <param name="generation">The retired generation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the bindings were released.</returns>
        /// <exception cref="ServiceResultException">A manager rejected an all-events release.</exception>
        internal async ValueTask ReleaseRetiredEventsAsync(
            NodeManagerGeneration generation,
            CancellationToken cancellationToken)
        {
            generation.TrySealEventBindings();

            IAsyncNodeManager nodeManager = generation.NodeManager;
            List<Exception>? failures = null;
            foreach (KeyValuePair<IEventMonitoredItem, GenerationEventBindingRecord> binding in
                generation.EventBindings.ToArray())
            {
                IEventMonitoredItem target = binding.Key;
                try
                {
                    ServiceResult result = await ChangeAllEventsSubscriptionAsync(
                        generation,
                        target,
                        (unsubscribe, ct) => InvokeAllEventsAsync(nodeManager, target, unsubscribe, ct),
                        unsubscribe: true,
                        cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported)
                    {
                        throw new ServiceResultException(result);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures is not null)
            {
                if (failures.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(failures[0]).Throw();
                }
                throw new AggregateException(
                    "Retired NodeManager all-events bindings could not be released.", failures);
            }
        }

        /// <summary>
        /// Removes exactly the bindings owned by a partially prepared generation.
        /// </summary>
        /// <param name="generation">The generation to unbind.</param>
        /// <param name="bindings">The captured bindings to remove.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the captured bindings have been removed.</returns>
        internal async ValueTask UnbindAsync(
            NodeManagerGeneration generation,
            Bindings bindings,
            CancellationToken cancellationToken)
        {
            IAsyncNodeManager nodeManager = generation.NodeManager;
            foreach (IEventMonitoredItem monitoredItem in bindings.EventMonitoredItems.Values)
            {
                ServiceResult result = await ChangeAllEventsSubscriptionAsync(
                    generation,
                    monitoredItem,
                    (unsubscribe, ct) => InvokeAllEventsAsync(nodeManager, monitoredItem, unsubscribe, ct),
                    unsubscribe: true,
                    cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported)
                {
                    throw new ServiceResultException(result);
                }
            }

            foreach (KeyValuePair<NodeId, SessionBinding> binding in bindings.Sessions)
            {
                if (binding.Value.Generation.TryGetGeneration(out NodeManagerGeneration? boundGeneration))
                {
                    await CloseSessionAsync(boundGeneration!, binding.Key, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var sessionContext = new OperationContext(binding.Value.Session, DiagnosticsMasks.None);
                    await nodeManager.SessionClosingAsync(
                        sessionContext, binding.Key, deleteSubscriptions: false, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private NodeManagerGeneration GetCurrentGeneration(IAsyncNodeManager nodeManager)
        {
            if (m_server.NodeManager is MasterNodeManager master &&
                master.TryGetCurrentGeneration(nodeManager, out NodeManagerGeneration? generation))
            {
                return generation!;
            }
            throw new InvalidOperationException("The NodeManager is not present in the current routing view.");
        }

        internal async ValueTask<SessionBinding?> ActivateSessionAsync(
            NodeManagerGeneration generation,
            ISession session,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                GenerationSessionBindingRecord bindingRecord = generation.SessionBindings.GetOrAdd(
                    session.Id,
                    static _ => new GenerationSessionBindingRecord());
                await bindingRecord.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!generation.SessionBindings.TryGetValue(session.Id, out GenerationSessionBindingRecord? current) ||
                    !ReferenceEquals(current, bindingRecord))
                {
                    bindingRecord.Semaphore.Release();
                    continue;
                }

                try
                {
                    if (bindingRecord.Binding is { } existing &&
                        ReferenceEquals(existing.Session, session) &&
                        ReferenceEquals(existing.Identity, session.EffectiveIdentity))
                    {
                        return existing;
                    }

                    if (bindingRecord.Binding is { } previous)
                    {
                        using var previousContext = new OperationContext(previous.Session, DiagnosticsMasks.None);
                        await generation.NodeManager.SessionClosingAsync(
                            previousContext,
                            session.Id,
                            deleteSubscriptions: false,
                            cancellationToken).ConfigureAwait(false);
                        bindingRecord.Binding = null;
                    }

                    SessionBinding? binding = await ActivateSessionCallbackAsync(
                        generation, session, cancellationToken).ConfigureAwait(false);
                    bindingRecord.Binding = binding;
                    if (binding is null)
                    {
                        RemoveSessionBindingRecord(generation, session.Id, bindingRecord);
                    }
                    return binding;
                }
                finally
                {
                    bindingRecord.Semaphore.Release();
                }
            }
        }

        internal static async ValueTask<bool> CloseSessionAsync(
            NodeManagerGeneration generation,
            NodeId sessionId,
            CancellationToken cancellationToken,
            bool deleteSubscriptions = false)
        {
            if (!generation.SessionBindings.TryGetValue(sessionId, out GenerationSessionBindingRecord? bindingRecord))
            {
                return false;
            }

            await bindingRecord.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (bindingRecord.Binding is not { } binding)
                {
                    RemoveSessionBindingRecord(generation, sessionId, bindingRecord);
                    return false;
                }

                using var sessionContext = new OperationContext(binding.Session, DiagnosticsMasks.None);
                await generation.NodeManager.SessionClosingAsync(
                    sessionContext,
                    sessionId,
                    deleteSubscriptions,
                    cancellationToken).ConfigureAwait(false);
                bindingRecord.Binding = null;
                RemoveSessionBindingRecord(generation, sessionId, bindingRecord);
                return true;
            }
            finally
            {
                bindingRecord.Semaphore.Release();
            }
        }

        private static void RemoveSessionBindingRecord(
            NodeManagerGeneration generation,
            NodeId sessionId,
            GenerationSessionBindingRecord bindingRecord)
        {
            ((ICollection<KeyValuePair<NodeId, GenerationSessionBindingRecord>>)generation.SessionBindings).Remove(
                new KeyValuePair<NodeId, GenerationSessionBindingRecord>(sessionId, bindingRecord));
        }

        private async ValueTask<SessionBinding?> ActivateSessionCallbackAsync(
            NodeManagerGeneration generation,
            ISession session,
            CancellationToken cancellationToken)
        {
            IAsyncNodeManager nodeManager = generation.NodeManager;
            while (true)
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                IUserIdentity identity = context.UserIdentity;
                try
                {
                    await nodeManager.SessionActivatedAsync(context, session.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    try
                    {
                        await nodeManager.SessionClosingAsync(
                            context, session.Id, deleteSubscriptions: false, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "NodeManager session activation and cleanup both failed.", failure, cleanupFailure);
                    }
                    throw;
                }

                if (ReferenceEquals(identity, session.EffectiveIdentity) &&
                    session.Activated &&
                    !session.IsClosing &&
                    m_server.SessionManager.GetSessions().Any(current => ReferenceEquals(current, session)))
                {
                    return new SessionBinding(session, identity, generation.Reference);
                }
                if (!session.Activated ||
                    session.IsClosing ||
                    !m_server.SessionManager.GetSessions().Any(current => ReferenceEquals(current, session)))
                {
                    await nodeManager.SessionClosingAsync(
                        context, session.Id, deleteSubscriptions: false, cancellationToken).ConfigureAwait(false);
                    return null;
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async ValueTask<bool> SubscribeToAllEventsAsync(
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem monitoredItem,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            if ((m_server.NodeManager as IDynamicNodeManagerBatchHost)?.IsRemovingBinding(
                callerContext, monitoredItem) == true)
            {
                return false;
            }

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            ServiceResult result = await ChangeAllEventsSubscriptionAsync(
                generation,
                monitoredItem,
                async (unsubscribe, ct) =>
                {
                    using var context = new OperationContext(monitoredItem);
                    return await nodeManager.SubscribeToAllEventsAsync(
                        context, monitoredItem.SubscriptionId, monitoredItem, unsubscribe, ct)
                        .ConfigureAwait(false);
                },
                unsubscribe: false,
                cancellationToken,
                refreshExisting: false).ConfigureAwait(false);
            if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported)
            {
                throw new ServiceResultException(result);
            }
            return result.StatusCode != StatusCodes.BadNotSupported;
        }

        /// <summary>
        /// Changes one generation's all-events binding without holding synchronization across manager callbacks.
        /// </summary>
        /// <param name="generation">The generation owning the binding.</param>
        /// <param name="monitoredItem">The exact item instance being changed.</param>
        /// <param name="dispatchAsync">The callback that changes the binding.</param>
        /// <param name="unsubscribe">Whether to remove the binding.</param>
        /// <param name="cancellationToken">The operation cancellation token.</param>
        /// <param name="retainFailedRelease">
        /// Whether a rejected release keeps the record for a later retry. A release for an item being deleted
        /// reports the failure to the client instead, because the item no longer exists to retry for.
        /// </param>
        /// <param name="refreshExisting">
        /// Whether an existing binding receives another callback for modification. Reconciliation only ensures
        /// that a binding exists and never replays its initial callback.
        /// </param>
        /// <returns>The manager's subscription result.</returns>
        internal ValueTask<ServiceResult> ChangeAllEventsSubscriptionAsync(
            NodeManagerGeneration generation,
            IEventMonitoredItem monitoredItem,
            Func<bool, CancellationToken, ValueTask<ServiceResult>> dispatchAsync,
            bool unsubscribe,
            CancellationToken cancellationToken,
            bool retainFailedRelease = true,
            bool refreshExisting = true)
        {
            GenerationEventBindingRecord? record = generation.GetEventBinding(monitoredItem, unsubscribe);
            if (record is null)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
            if (unsubscribe)
            {
                return UnsubscribeAllEventsAsync(
                    generation, monitoredItem, record, dispatchAsync, retainFailedRelease, cancellationToken);
            }

            return SubscribeAllEventsAsync(
                generation, monitoredItem, record, dispatchAsync, refreshExisting, cancellationToken);
        }

        private async ValueTask<ServiceResult> SubscribeAllEventsAsync(
            NodeManagerGeneration generation,
            IEventMonitoredItem monitoredItem,
            GenerationEventBindingRecord record,
            Func<bool, CancellationToken, ValueTask<ServiceResult>> dispatchAsync,
            bool refreshExisting,
            CancellationToken cancellationToken)
        {
            Task<bool>? pending = null;
            TaskCompletionSource<bool>? owner = null;
            bool rebinding = false;
            lock (record.Sync)
            {
                if (record.State == GenerationEventBindingState.Removed ||
                    record.RemovalRequested ||
                    (!refreshExisting && record.State == GenerationEventBindingState.Bound))
                {
                    return ServiceResult.Good;
                }
                if (record.State is GenerationEventBindingState.Subscribing or
                    GenerationEventBindingState.Unsubscribing)
                {
                    pending = record.Completion!.Task;
                }
                else
                {
                    // Re-subscribing a bound item (for example on Modify) still reaches the manager; a rejection
                    // leaves the existing binding in place.
                    rebinding = record.State == GenerationEventBindingState.Bound;
                    record.State = GenerationEventBindingState.Subscribing;
                    record.Completion = owner = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            if (pending is not null)
            {
                bool bound = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                return bound && !refreshExisting ? ServiceResult.Good : await SubscribeAllEventsAsync(
                    generation, monitoredItem, record, dispatchAsync, refreshExisting, cancellationToken)
                    .ConfigureAwait(false);
            }

            Exception? failure = null;
            bool threw = false;
            ServiceResult result;
            try
            {
                result = await dispatchAsync(false, cancellationToken).ConfigureAwait(false) ?? ServiceResult.Good;
                if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported)
                {
                    failure = new ServiceResultException(result);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure = ex;
                threw = true;
                result = ServiceResult.Create(
                    ex, StatusCodes.BadUnexpectedError, "The all-events binding callback failed.");
            }

            // A manager that does not source all events keeps a bound record so that the item's release is still
            // delivered to it exactly once, as for every other attempted owner.
            bool itemRemains = monitoredItem.MonitoringAllEvents &&
                m_server.EventManager.Contains(monitoredItem);
            bool removeAfterBind;
            lock (record.Sync)
            {
                removeAfterBind = record.RemovalRequested || !itemRemains || (failure is not null && !rebinding);
                record.State = removeAfterBind
                    ? GenerationEventBindingState.Unsubscribing
                    : GenerationEventBindingState.Bound;
                if (!removeAfterBind)
                {
                    record.Completion = null;
                }
            }

            if (removeAfterBind)
            {
                try
                {
                    ServiceResult cleanup = await dispatchAsync(true, CancellationToken.None).ConfigureAwait(false)
                        ?? ServiceResult.Good;
                    if (ServiceResult.IsBad(cleanup) && cleanup.StatusCode != StatusCodes.BadNotSupported)
                    {
                        throw new ServiceResultException(cleanup);
                    }
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    // The release failed, so the manager may still hold the binding: keep it recorded so a later
                    // release retries it instead of leaking.
                    lock (record.Sync)
                    {
                        record.State = GenerationEventBindingState.Bound;
                        record.RemovalRequested = true;
                        record.Completion = null;
                    }
                    owner!.TrySetResult(true);
                    if (failure is not null)
                    {
                        throw new AggregateException(
                            "NodeManager event binding and cleanup both failed.", failure, cleanupFailure);
                    }
                    throw;
                }

                MarkRemoved(generation, monitoredItem, record);
                owner!.TrySetResult(false);
                if (failure is not null)
                {
                    throw failure;
                }
                return result;
            }

            owner!.TrySetResult(true);
            if (threw)
            {
                throw failure!;
            }
            return result;
        }

        private async ValueTask<ServiceResult> UnsubscribeAllEventsAsync(
            NodeManagerGeneration generation,
            IEventMonitoredItem monitoredItem,
            GenerationEventBindingRecord record,
            Func<bool, CancellationToken, ValueTask<ServiceResult>> dispatchAsync,
            bool retainFailedRelease,
            CancellationToken cancellationToken)
        {
            Task<bool>? pending = null;
            TaskCompletionSource<bool>? owner = null;
            lock (record.Sync)
            {
                if (record.State == GenerationEventBindingState.Removed)
                {
                    return ServiceResult.Good;
                }
                if (record.State == GenerationEventBindingState.Subscribing)
                {
                    // The in-flight bind observes the request when its callback returns and releases the
                    // binding itself, so the release does not wait for a manager callback it does not own.
                    record.RemovalRequested = true;
                    return ServiceResult.Good;
                }
                if (record.State == GenerationEventBindingState.Unsubscribing)
                {
                    // The active release reports its real outcome.
                    record.RemovalRequested = true;
                    pending = record.Completion!.Task;
                }
                else
                {
                    record.State = GenerationEventBindingState.Unsubscribing;
                    record.RemovalRequested = true;
                    record.Completion = owner = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            if (pending is not null)
            {
                bool stillBound = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                return stillBound
                    ? await UnsubscribeAllEventsAsync(
                        generation, monitoredItem, record, dispatchAsync, retainFailedRelease, cancellationToken)
                        .ConfigureAwait(false)
                    : ServiceResult.Good;
            }

            try
            {
                ServiceResult result = await dispatchAsync(true, cancellationToken).ConfigureAwait(false)
                    ?? ServiceResult.Good;
                if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadNotSupported &&
                    retainFailedRelease)
                {
                    lock (record.Sync)
                    {
                        record.State = GenerationEventBindingState.Bound;
                        record.RemovalRequested = true;
                        record.Completion = null;
                    }
                    owner!.TrySetResult(true);
                    return result;
                }

                MarkRemoved(generation, monitoredItem, record);
                owner!.TrySetResult(false);
                return result;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                if (!retainFailedRelease)
                {
                    MarkRemoved(generation, monitoredItem, record);
                    owner!.TrySetResult(false);
                    throw;
                }
                lock (record.Sync)
                {
                    record.State = GenerationEventBindingState.Bound;
                    record.RemovalRequested = true;
                    record.Completion = null;
                }
                owner!.TrySetResult(true);
                throw;
            }
        }

        private static void MarkRemoved(
            NodeManagerGeneration generation,
            IEventMonitoredItem monitoredItem,
            GenerationEventBindingRecord record)
        {
            // A sealed generation keeps the removed record so a later release is not dispatched twice.
            if (!generation.EventBindingsSealed)
            {
                ((ICollection<KeyValuePair<IEventMonitoredItem, GenerationEventBindingRecord>>)
                    generation.EventBindings).Remove(
                    new KeyValuePair<IEventMonitoredItem, GenerationEventBindingRecord>(monitoredItem, record));
            }
            lock (record.Sync)
            {
                record.State = GenerationEventBindingState.Removed;
                record.Completion = null;
            }
        }

        private static async ValueTask<ServiceResult> InvokeAllEventsAsync(
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe,
            CancellationToken cancellationToken)
        {
            using var context = new OperationContext(monitoredItem);
            return await nodeManager.SubscribeToAllEventsAsync(
                context, monitoredItem.SubscriptionId, monitoredItem, unsubscribe, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Holds the session and all-events bindings established for one generation.
        /// </summary>
        internal sealed class Bindings
        {
            /// <summary>
            /// Gets session bindings keyed by the session identity.
            /// </summary>
            internal Dictionary<NodeId, SessionBinding> Sessions { get; } = [];

            /// <summary>
            /// Gets all-events bindings keyed by monitored-item identity.
            /// </summary>
            internal Dictionary<uint, IEventMonitoredItem> EventMonitoredItems { get; } = [];
        }

        internal sealed class SessionBinding
        {
            /// <summary>
            /// Creates a binding record for one activated session.
            /// </summary>
            /// <param name="session">The activated session.</param>
            /// <param name="identity">The identity used during activation.</param>
            /// <param name="generation">The generation receiving the activation callback.</param>
            internal SessionBinding(
                ISession session,
                IUserIdentity identity,
                NodeManagerGenerationReference generation)
            {
                Session = session;
                Identity = identity;
                Generation = generation;
            }

            /// <summary>
            /// Gets the activated session.
            /// </summary>
            internal ISession Session { get; }

            /// <summary>
            /// Gets the identity used during activation.
            /// </summary>
            internal IUserIdentity Identity { get; }

            /// <summary>
            /// Gets the generation identity associated with the binding.
            /// </summary>
            internal NodeManagerGenerationReference Generation { get; }
        }

        private readonly IServerInternal m_server;
    }
}
