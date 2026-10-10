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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Shares one observation stream between a variable's active monitored items.
    /// </summary>
    internal sealed class WotObservedPropertySource : IAsyncDisposable
    {
        public WotObservedPropertySource(
            INodeBuilder builder,
            WotBindingChannelSlot channel,
            bool hasReadBinding,
            int maxQueuedValues,
            CancellationToken generationToken)
            : this(builder, async (notification, cancellationToken) =>
            {
                IWotBindingChannel opened = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
                return await opened.ObserveAsync(notification, cancellationToken).ConfigureAwait(false);
            }, hasReadBinding, maxQueuedValues, generationToken)
        {
        }

        public WotObservedPropertySource(
            INodeBuilder builder,
            Func<Action<WotNotification>, CancellationToken, ValueTask<IAsyncDisposable>> observe,
            bool hasReadBinding,
            int maxQueuedValues,
            CancellationToken generationToken)
        {
            m_observe = observe;
            m_context = builder.Builder.Context;
            m_logger = m_context.Telemetry.CreateLogger<WotObservedPropertySource>();
            m_nodeId = builder.Node.NodeId;
            builder.OnCreateMonitoredItem((context, _) => new ValueTask<MonitoredItemCreateDecision>(
                context.Request.ItemToMonitor.AttributeId == Attributes.Value
                    ? MonitoredItemCreateDecision.Use(factoryContext => factoryContext.CreatePushMonitoredItem())
                    : MonitoredItemCreateDecision.UseDefault()))
                .OnMonitoredItemCreated(OnCreated)
                .OnMonitoredItemDeleted(OnDeletedAsync)
                .OnMonitoringModeChanged(OnModeChangedAsync)
                .OnMonitoredItemAttached(OnAttachedAsync)
                .OnMonitoredItemDetached(OnDeletedAsync);
            builder.OnFirstSubscriber(Attributes.Value, StartAsync);
            builder.OnLastSubscriber(Attributes.Value, StopAsync);
            if (!hasReadBinding)
            {
                builder.OnRead(ReadAsync);
            }
            m_publications = Channel.CreateBounded<Publication>(new BoundedChannelOptions(maxQueuedValues)
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
            m_deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(generationToken);
            m_deliveryToken = m_deliveryCancellation.Token;
            m_delivery = DeliverAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Task pending = Task.CompletedTask;
            TaskCompletionSource<bool>? completion = null;
            Task disposing;
            lock (m_sync)
            {
                if (m_disposeTask is null)
                {
                    m_disposed = true;
                    m_active = false;
                    m_epoch++;
                    m_items.Clear();
                    pending = m_operations == 0 ? Task.CompletedTask :
                        (m_drained ??= new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposeTask = completion.Task;
                }
                disposing = m_disposeTask;
            }
            if (completion is not null)
            {
                try
                {
                    await DisposeCoreAsync(pending).ConfigureAwait(false);
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
            await disposing.ConfigureAwait(false);
        }

        internal static DataValue TranslateNotification(WotNotification notification, ISystemContext context)
        {
            DataValue value = notification.Value;
            if (StatusCode.IsBad(value.StatusCode) || !WotBindingValueMapper.RequiresContext(value.WrappedValue))
            {
                return value;
            }
            IServiceMessageContext? source = notification.Context;
            if (source is null)
            {
                var fallback = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory);
                if (notification.NamespaceUris.ToArray() is { Length: > 0 } namespaceUris)
                {
                    fallback.NamespaceUris = new NamespaceTable(namespaceUris);
                }
                source = fallback;
            }
            return value.WithWrappedValue(WotBindingValueMapper.Translate(
                value.WrappedValue, source, context.AsMessageContext(), allowNamespaceGrowth: true));
        }

        private async Task DisposeCoreAsync(Task pending)
        {
            List<Exception>? errors = null;
            try
            {
                m_deliveryCancellation.Cancel();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                errors = [exception];
            }
            m_publications.Writer.TryComplete();
            await pending.ConfigureAwait(false);
            try
            {
                await m_delivery.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                (errors ??= []).Add(exception);
            }
            try
            {
                IAsyncDisposable? subscription = m_subscription;
                m_subscription = null;
                if (subscription is not null)
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                (errors ??= []).Add(exception);
            }
            finally
            {
                m_sourceCancellation?.Dispose();
                m_sourceCancellation = null;
                m_gate.Dispose();
                m_deliveryCancellation.Dispose();
            }
            if (errors is { Count: > 0 })
            {
                throw new AggregateException("WoT property observation cleanup failed.", errors);
            }
        }

        private void OnCreated(ISystemContext context, NodeState source, ISampledDataChangeMonitoredItem item)
        {
            if (item is not MonitoredItem monitored || item.AttributeId != Attributes.Value)
            {
                return;
            }
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return;
                }
                m_items[item.Id] = (monitored, context);
                if (m_hasValue)
                {
                    Enqueue(new Publication(m_epoch, monitored.Id, m_last));
                }
            }
        }

        private ValueTask OnDeletedAsync(
            ISystemContext context, NodeState source, ISampledDataChangeMonitoredItem item, CancellationToken token)
        {
            lock (m_sync)
            {
                if (m_items.TryGetValue(item.Id, out (MonitoredItem Item, ISystemContext Context) tracked) &&
                    ReferenceEquals(tracked.Item, item))
                {
                    m_items.Remove(item.Id);
                }
            }
            return default;
        }

        private ValueTask OnAttachedAsync(
            ISystemContext context, NodeState source, ISampledDataChangeMonitoredItem item, CancellationToken token)
        {
            OnCreated(context, source, item);
            return default;
        }

        private ValueTask OnModeChangedAsync(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem item,
            MonitoringMode previousMode,
            MonitoringMode mode,
            CancellationToken token)
        {
            lock (m_sync)
            {
                if (!m_disposed &&
                    m_active &&
                    m_hasValue &&
                    previousMode == MonitoringMode.Disabled &&
                    mode != MonitoringMode.Disabled &&
                    m_items.TryGetValue(item.Id, out (MonitoredItem Item, ISystemContext Context) tracked) &&
                    ReferenceEquals(tracked.Item, item))
                {
                    Enqueue(new Publication(m_epoch, tracked.Item.Id, m_last));
                }
            }
            return default;
        }

        private async ValueTask StartAsync(ISystemContext context, NodeState source, CancellationToken token)
        {
            if (!TryEnterOperation())
            {
                return;
            }
            bool entered = false;
            bool keepLifetime = false;
            CancellationTokenSource? sourceCancellation = null;
            CancellationToken sourceToken = default;
            long epoch = 0;
            try
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, m_deliveryToken);
                Task gate;
                long queuedEpoch;
                lock (m_sync)
                {
                    if (m_disposed || !HasActiveItems())
                    {
                        return;
                    }
                    queuedEpoch = m_epoch;
                    gate = m_gate.WaitAsync(waiting.Token);
                }
                await gate.ConfigureAwait(false);
                entered = true;
                lock (m_sync)
                {
                    if (m_disposed || m_subscription is not null || !HasActiveItems() || m_epoch != queuedEpoch)
                    {
                        return;
                    }
                    if (m_delivery.IsCompleted)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Property observation delivery is unavailable.");
                    }
                    sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, m_deliveryToken);
                    sourceToken = sourceCancellation.Token;
                    m_sourceCancellation = sourceCancellation;
                    m_active = true;
                    epoch = ++m_epoch;
                }
                m_subscription = await m_observe(
                    notification => OnNotification(epoch, notification), sourceToken).ConfigureAwait(false);
                keepLifetime = true;
            }
            catch (OperationCanceledException) when (
                sourceToken.IsCancellationRequested ||
                token.IsCancellationRequested ||
                m_deliveryToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ObservationFailed(exception, m_nodeId);
                lock (m_sync)
                {
                    if (!m_disposed && m_epoch == epoch)
                    {
                        m_active = false;
                        m_epoch++;
                        m_last = DataValue.FromStatusCode(exception is ServiceResultException serviceException
                            ? serviceException.StatusCode
                            : StatusCodes.BadCommunicationError);
                        m_hasValue = true;
                        Enqueue(new Publication(m_epoch, 0, m_last));
                    }
                }
                throw;
            }
            finally
            {
                if (!keepLifetime && sourceCancellation is not null)
                {
                    bool ownsCancellation;
                    lock (m_sync)
                    {
                        ownsCancellation = ReferenceEquals(m_sourceCancellation, sourceCancellation);
                        if (ownsCancellation)
                        {
                            m_sourceCancellation = null;
                            m_active = false;
                            if (m_epoch == epoch)
                            {
                                m_epoch++;
                            }
                        }
                    }
                    if (ownsCancellation)
                    {
                        sourceCancellation.Dispose();
                    }
                }
                if (entered)
                {
                    m_gate.Release();
                }
                ExitOperation();
            }
        }

        private async ValueTask StopAsync(ISystemContext context, NodeState source, CancellationToken token)
        {
            if (!TryEnterOperation())
            {
                return;
            }
            try
            {
                await RetireAsync(requireNoSubscribers: true, expectedEpoch: null).ConfigureAwait(false);
            }
            finally
            {
                ExitOperation();
            }
        }

        private async ValueTask RetireAsync(bool requireNoSubscribers, long? expectedEpoch)
        {
            CancellationTokenSource? retired = null;
            bool entered = false;
            List<Exception>? errors = null;
            try
            {
                Task gate;
                long retiredEpoch;
                lock (m_sync)
                {
                    if (m_disposed ||
                        (requireNoSubscribers && HasActiveItems()) ||
                        (expectedEpoch.HasValue && (m_epoch != expectedEpoch.Value || m_active)))
                    {
                        return;
                    }
                    m_active = false;
                    retiredEpoch = ++m_epoch;
                    retired = m_sourceCancellation;
                    m_sourceCancellation = null;
                    gate = m_gate.WaitAsync(CancellationToken.None);
                }
                try
                {
                    retired?.Cancel();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    errors = [exception];
                }
                await gate.ConfigureAwait(false);
                entered = true;
                bool ownsSubscription;
                lock (m_sync)
                {
                    ownsSubscription = !m_disposed && m_epoch == retiredEpoch;
                }
                if (ownsSubscription)
                {
                    IAsyncDisposable? subscription = m_subscription;
                    m_subscription = null;
                    if (subscription is not null)
                    {
                        await subscription.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                (errors ??= []).Add(exception);
            }
            finally
            {
                retired?.Dispose();
                if (entered)
                {
                    m_gate.Release();
                }
            }
            if (errors is { Count: > 0 })
            {
                throw new AggregateException("Stopping the WoT property observation failed.", errors);
            }
        }

        private bool TryEnterOperation()
        {
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return false;
                }
                m_operations++;
                return true;
            }
        }

        private void ExitOperation()
        {
            lock (m_sync)
            {
                m_operations--;
                if (m_operations == 0 && m_disposed)
                {
                    m_drained?.TrySetResult(true);
                }
            }
        }

        private bool HasActiveItems()
        {
            foreach ((MonitoredItem item, ISystemContext _) in m_items.Values)
            {
                if (item.MonitoringMode != MonitoringMode.Disabled)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnNotification(long epoch, WotNotification notification)
        {
            lock (m_sync)
            {
                if (!m_active || m_disposed || m_epoch != epoch)
                {
                    return;
                }
            }
            try
            {
                Publish(epoch, TranslateNotification(notification, m_context));
            }
            catch (ServiceResultException exception)
            {
                m_logger.ObservationFailed(exception, m_nodeId);
                Publish(epoch, DataValue.FromStatusCode(exception.StatusCode));
            }
        }

        private void Publish(long epoch, in DataValue value)
        {
            lock (m_sync)
            {
                if (!m_active || m_disposed || m_epoch != epoch)
                {
                    return;
                }
                m_last = value.Copy();
                m_hasValue = true;
                Enqueue(new Publication(epoch, 0, m_last));
            }
        }

        private void Enqueue(Publication publication)
        {
            if (!m_publications.Writer.TryWrite(publication))
            {
                m_active = false;
                m_epoch++;
                m_last = DataValue.FromStatusCode(StatusCodes.BadResourceUnavailable);
                m_hasValue = true;
                m_overflow = new Publication(m_epoch, 0, m_last);
                m_logger.ObservationFailed(
                    new ServiceResultException(
                        StatusCodes.BadResourceUnavailable, "Property observation queue is full."),
                    m_nodeId);
            }
        }

        private async Task DeliverAsync()
        {
            try
            {
                await foreach (Publication publication in m_publications.Reader.ReadAllAsync(m_deliveryToken)
                    .ConfigureAwait(false))
                {
                    await DeliverAsync(publication).ConfigureAwait(false);
                    Publication? overflow;
                    lock (m_sync)
                    {
                        overflow = m_overflow;
                        m_overflow = null;
                    }
                    if (overflow is { } failure)
                    {
                        await DeliverAsync(failure).ConfigureAwait(false);
                        await DisposeFaultedSubscriptionAsync(failure.Epoch).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (m_deliveryToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (m_sync)
                {
                    m_active = false;
                    m_epoch++;
                    m_last = DataValue.FromStatusCode(StatusCodes.BadUnexpectedError);
                    m_hasValue = true;
                }
                m_logger.ObservationFailed(exception, m_nodeId);
                throw;
            }
        }

        private async ValueTask DeliverAsync(Publication publication)
        {
            (MonitoredItem Item, ISystemContext Context)[] items;
            lock (m_sync)
            {
                if (m_disposed || m_epoch != publication.Epoch)
                {
                    return;
                }
                items = publication.ItemId == 0
                    ? [.. m_items.Values]
                    : m_items.TryGetValue(
                        publication.ItemId, out (MonitoredItem Item, ISystemContext Context) tracked)
                        ? [tracked]
                        : [];
            }
            foreach ((MonitoredItem item, ISystemContext context) in items)
            {
                IAsyncNodeManager owner = item.NodeManager;
                ServiceResult permission;
                try
                {
                    ISession? session = item.Session ??
                        (context as ServerSystemContext)?.OperationContext?.Session;
                    using OperationContext operation = session is not null
                        ? new OperationContext(session, item.DiagnosticsMasks)
                        : new OperationContext(item);
                    permission = operation.UserIdentity is null
                        ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                        : await owner.ValidateRolePermissionsAsync(
                            operation, item.NodeId, PermissionType.Read, m_deliveryToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (m_deliveryToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    m_logger.ObservationFailed(exception, m_nodeId);
                    permission = new ServiceResult(exception);
                }
                lock (m_sync)
                {
                    if (!m_disposed &&
                        m_epoch == publication.Epoch &&
                        m_items.TryGetValue(item.Id, out (MonitoredItem Item, ISystemContext Context) tracked) &&
                        ReferenceEquals(tracked.Item, item) &&
                        ReferenceEquals(owner, item.NodeManager))
                    {
                        Queue(item, context, publication.Value, permission);
                    }
                }
            }
        }

        private ValueTask DisposeFaultedSubscriptionAsync(long epoch)
        {
            return RetireAsync(requireNoSubscribers: false, epoch);
        }

        private static void Queue(
            MonitoredItem item, ISystemContext context, in DataValue value, ServiceResult permission)
        {
            if (item.MonitoringMode == MonitoringMode.Disabled)
            {
                return;
            }
            Variant payload = value.WrappedValue.Copy();
            ServiceResult status = ServiceResult.IsBad(permission) ? permission : StatusCode.IsBad(value.StatusCode)
                ? new ServiceResult(value.StatusCode)
                : BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context, item.IndexRange, item.DataEncoding, ref payload);
            DataValue queued = ServiceResult.IsBad(status)
                ? value.WithWrappedValue(Variant.Null).WithStatus(status.StatusCode)
                : value.WithWrappedValue(payload);
            item.QueueValue(queued, status, false);
        }

        private ValueTask<AttributeReadResult> ReadAsync(
            ISystemContext context, NodeState node, NumericRange range, QualifiedName encoding, CancellationToken token)
        {
            DataValue value;
            lock (m_sync)
            {
                value = m_last;
            }
            Variant payload = value.WrappedValue;
            ServiceResult status = StatusCode.IsBad(value.StatusCode)
                ? new ServiceResult(value.StatusCode)
                : BaseVariableState.ApplyIndexRangeAndDataEncoding(context, range, encoding, ref payload);
            return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                status, payload, value.StatusCode, value.SourceTimestamp));
        }

        private readonly Func<Action<WotNotification>, CancellationToken, ValueTask<IAsyncDisposable>> m_observe;
        private readonly ISystemContext m_context;
        private readonly ILogger m_logger;
        private readonly NodeId m_nodeId;
        private readonly Lock m_sync = new();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Dictionary<uint, (MonitoredItem Item, ISystemContext Context)> m_items = [];
        private readonly Channel<Publication> m_publications;
        private readonly CancellationTokenSource m_deliveryCancellation;
        private readonly CancellationToken m_deliveryToken;
        private readonly Task m_delivery;
        private IAsyncDisposable? m_subscription;
        private CancellationTokenSource? m_sourceCancellation;
        private Publication? m_overflow;
        private DataValue m_last = DataValue.FromStatusCode(StatusCodes.BadWaitingForInitialData);
        private bool m_hasValue;
        private bool m_active;
        private bool m_disposed;
        private long m_epoch;
        private int m_operations;
        private TaskCompletionSource<bool>? m_drained;
        private Task? m_disposeTask;

        private readonly record struct Publication(long Epoch, uint ItemId, DataValue Value);
    }

    internal static partial class WotObservedPropertySourceLog
    {
        [LoggerMessage(
            EventId = WotConServerEventIds.WotObservedPropertySource,
            Level = LogLevel.Error,
            Message = "WoT property observation failed for local Node {NodeId}.")]
        public static partial void ObservationFailed(this ILogger logger, Exception exception, NodeId nodeId);
    }
}
