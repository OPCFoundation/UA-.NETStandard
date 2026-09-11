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
 *
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
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    internal sealed partial class OpcUaWotBindingChannel
    {
        private sealed partial class OpcUaMonitoredItemSubscription
        {
            internal void PublishPathNotification(
                MonitoredItem item,
                IEncodeable value,
                Func<MonitoredItem, IEncodeable, IServiceMessageContext, WotNotification?> translate)
            {
                m_pathRefresh?.Publish(item, value, translate);
            }

            internal ValueTask StartPathMaintenanceAsync(CancellationToken token)
            {
                return m_pathRefresh?.StartAsync(token) ?? default;
            }

            internal ValueTask StopPathMaintenanceAsync()
            {
                return m_pathRefresh?.DisposeAsync() ?? default;
            }

            private sealed class PathRefresh : IAsyncDisposable
            {
                public PathRefresh(
                    OpcUaMonitoredItemSubscription lifetime,
                    OpcUaWotBindingChannel owner,
                    Action<WotNotification> onNotification,
                    PathSessionState sessionState)
                {
                    m_lifetime = lifetime;
                    m_owner = owner;
                    m_onNotification = onNotification;
                    m_sessionState = sessionState;
                }

                public void Publish(
                    MonitoredItem item,
                    IEncodeable value,
                    Func<MonitoredItem, IEncodeable, IServiceMessageContext, WotNotification?> translate)
                {
                    PathSessionState state;
                    lock (m_gate)
                    {
                        if (m_stopping || !m_ready || !ReferenceEquals(item, m_lifetime.m_item))
                        {
                            return;
                        }
                        state = m_sessionState;
                    }
                    WotNotification? notification = translate(item, value, state.CreateContext());
                    if (notification is null)
                    {
                        return;
                    }
                    if (!m_owner.TryUsePathSessionState(state, () =>
                    {
                        lock (m_gate)
                        {
                            if (!m_stopping &&
                                m_ready &&
                                ReferenceEquals(item, m_lifetime.m_item) &&
                                m_sessionState.Matches(state))
                            {
                                m_onNotification(notification);
                            }
                        }
                    }))
                    {
                        Invalidate();
                    }
                }

                public async ValueTask StartAsync(CancellationToken token)
                {
                    m_owner.m_session.SessionConfigurationChanged += OnConfigurationChanged;
                    await EnableReportingAsync(m_sessionState, token).ConfigureAwait(false);
                    m_timer = m_owner.m_options.TimeProvider.CreateTimer(
                        _ => m_changed.Set(), null,
                        m_owner.m_options.BrowsePathRefreshInterval,
                        m_owner.m_options.BrowsePathRefreshInterval);
                    m_worker = RunAsync();
                }

                public async ValueTask DisposeAsync()
                {
                    bool first;
                    lock (m_gate)
                    {
                        first = !m_stopping;
                        m_stopping = true;
                        m_ready = false;
                    }
                    if (!first)
                    {
                        await m_stopped.Task.ConfigureAwait(false);
                        return;
                    }
                    try
                    {
                        m_owner.m_session.SessionConfigurationChanged -= OnConfigurationChanged;
                        if (m_timer is not null)
                        {
                            await m_timer.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            await m_stop.CancelAsync().ConfigureAwait(false);
                            if (m_worker is not null)
                            {
                                await m_worker.ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            m_stop.Dispose();
                            m_stopped.TrySetResult(true);
                        }
                    }
                }

                private void OnConfigurationChanged(object? sender, EventArgs arguments)
                {
                    Invalidate();
                }

                private void Invalidate()
                {
                    lock (m_gate)
                    {
                        if (m_stopping)
                        {
                            return;
                        }
                        m_ready = false;
                    }
                    m_changed.Set();
                }

                private async Task RunAsync()
                {
                    CancellationToken token = m_stop.Token;
                    try
                    {
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            await m_changed.WaitAsync(token).ConfigureAwait(false);
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                await RefreshAsync(token).ConfigureAwait(false);
                            }
                            catch (ServiceResultException error)
                            {
                                lock (m_gate)
                                {
                                    m_ready = false;
                                }
                                PublishFailure(error.StatusCode);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // The subscription owns this loop, independently of the caller's creation token.
                    }
                    finally
                    {
                        lock (m_gate)
                        {
                            m_ready = false;
                        }
                        if (!token.IsCancellationRequested)
                        {
                            PublishFailure(StatusCodes.BadUnexpectedError);
                        }
                    }
                }

                private void PublishFailure(StatusCode status)
                {
                    m_owner.PublishPathStatus(() =>
                    {
                        lock (m_gate)
                        {
                            if (!m_stopping)
                            {
                                m_onNotification(new WotNotification(DataValue.FromStatusCode(status)));
                            }
                        }
                    });
                }

                private async ValueTask RefreshAsync(CancellationToken token)
                {
                    MonitoredItem previous = m_lifetime.m_item;
                    NodeClass expected = previous.AttributeId == Attributes.EventNotifier
                        ? NodeClass.Object | NodeClass.View
                        : NodeClass.Variable;
                    ResolvedPathTarget target = await m_owner.ResolveTargetAsync(expected, token).ConfigureAwait(false);
                    PathSessionState current = target.State ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Path maintenance requires an admitted source context.");
                    bool replace;
                    lock (m_gate)
                    {
                        replace = !m_ready ||
                            !previous.Created ||
                            target.NodeId != previous.StartNodeId ||
                            !m_sessionState.Matches(current);
                        if (replace)
                        {
                            m_ready = false;
                        }
                    }
                    if (replace)
                    {
                        var item = new MonitoredItem(m_lifetime.m_subscription.DefaultItem)
                        {
                            StartNodeId = target.NodeId,
                            NodeClass = previous.NodeClass,
                            AttributeId = previous.AttributeId,
                            DisplayName = previous.DisplayName,
                            SamplingInterval = previous.SamplingInterval,
                            QueueSize = previous.QueueSize,
                            DiscardOldest = previous.DiscardOldest,
                            MonitoringMode = MonitoringMode.Disabled,
                            Filter = previous.AttributeId == Attributes.EventNotifier
                                ? m_owner.BuildEventFilter(current.NamespaceUris)
                                : null
                        };
                        previous.Notification -= m_lifetime.m_handler;
                        m_lifetime.m_subscription.RemoveItem(previous);
                        item.Notification += m_lifetime.m_handler;
                        lock (m_gate)
                        {
                            m_lifetime.m_item = item;
                        }
                        m_lifetime.m_subscription.AddItem(item);
                        await m_lifetime.m_subscription.ApplyChangesAsync(token).ConfigureAwait(false);
                        if (!item.Created)
                        {
                            throw new ServiceResultException(
                                item.Status.Error?.StatusCode ?? StatusCodes.BadNodeIdUnknown,
                                "The Server rejected the re-resolved browse-path monitored item.");
                        }
                        m_reportPending = true;
                    }
                    await EnableReportingAsync(current, token).ConfigureAwait(false);
                }

                private async ValueTask EnableReportingAsync(PathSessionState current, CancellationToken token)
                {
                    m_owner.ValidatePathSessionState(current);
                    bool admitted = false;
                    if (!m_owner.TryUsePathSessionState(current, () =>
                    {
                        lock (m_gate)
                        {
                            if (!m_stopping)
                            {
                                m_sessionState = current;
                                m_ready = true;
                                admitted = true;
                            }
                        }
                    }) ||
                        !admitted)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The monitored item's source admission is no longer current.");
                    }
                    if (m_reportPending)
                    {
                        var errors = await m_lifetime.m_subscription.SetMonitoringModeAsync(
                            MonitoringMode.Reporting, [m_lifetime.m_item], token).ConfigureAwait(false);
                        if (errors is not null)
                        {
                            foreach (ServiceResult? error in errors)
                            {
                                if (error is not null && ServiceResult.IsBad(error))
                                {
                                    throw new ServiceResultException(error);
                                }
                            }
                        }
                        m_owner.ValidatePathSessionState(current);
                        if (!m_owner.TryUsePathSessionState(current, () =>
                        {
                            lock (m_gate)
                            {
                                if (!m_stopping && m_ready && m_sessionState.Matches(current))
                                {
                                    m_reportPending = false;
                                }
                            }
                        }))
                        {
                            Invalidate();
                        }
                    }
                }

                private readonly OpcUaMonitoredItemSubscription m_lifetime;
                private readonly OpcUaWotBindingChannel m_owner;
                private readonly Action<WotNotification> m_onNotification;
                private readonly AsyncAutoResetEvent m_changed = new();
                private readonly CancellationTokenSource m_stop = new();

                private readonly TaskCompletionSource<bool> m_stopped =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);

                private readonly Lock m_gate = new();
                private ITimer? m_timer;
                private Task? m_worker;
                private PathSessionState m_sessionState;
                private bool m_ready;
                private bool m_stopping;
                private bool m_reportPending = true;
            }

            private readonly PathRefresh? m_pathRefresh;
        }
    }
}
