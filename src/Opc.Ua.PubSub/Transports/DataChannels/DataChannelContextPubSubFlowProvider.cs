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
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Opens or routes PubSub flows on an application-owned authenticated context with bounded pending admission.
    /// In accept mode this provider is the sole consumer of that context's AcceptAsync.
    /// </summary>
    public sealed class DataChannelContextPubSubFlowProvider : IDataChannelPubSubFlowProvider, IAsyncDisposable
    {
        /// <summary>
        /// Binds an already started context. An optional grant provider selects Session-authorized opening;
        /// otherwise opening is configured and requires no Session or model.
        /// </summary>
        public DataChannelContextPubSubFlowProvider(
            DataChannelContext context,
            bool openLocally,
            int maximumPendingFlows = 16,
            IDataChannelPubSubGrantProvider? grantProvider = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            if (maximumPendingFlows is < 1 or > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPendingFlows));
            }
            if (!openLocally && grantProvider is not null)
            {
                throw new ArgumentException("Only the flow opener acquires a grant.", nameof(grantProvider));
            }
            m_openLocally = openLocally;
            m_maximumPendingFlows = maximumPendingFlows;
            m_grantProvider = grantProvider;
            m_lifetime = new CancellationTokenSource();
            m_lifetimeToken = m_lifetime.Token;
        }

        /// <inheritdoc/>
        public async ValueTask<DataChannelPubSubFlow> OpenAsync(
            DataChannelPubSubFlowRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(DataChannelContextPubSubFlowProvider));
                }
                if (request.CarrierKind != m_context.CarrierKind || !m_context.IsReady)
                {
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                if (m_acquisitions >= m_maximumPendingFlows)
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                m_acquisitions++;
            }
            DataChannel? channel = null;
            uint reservedChannelId = 0;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetimeToken);
                DataChannelParametersDataType parameters = request.Parameters;
                if (m_openLocally)
                {
                    uint channelId = 0;
                    Guid? grantId = null;
                    DataChannelAuthorizationMode authorization = DataChannelAuthorizationMode.Configured;
                    if (m_grantProvider is not null)
                    {
                        channelId = m_context.ReserveChannelId();
                        reservedChannelId = channelId;
                        grantId = await m_grantProvider.AcquireAsync(m_context, channelId, parameters, linked.Token)
                            .ConfigureAwait(false);
                        if (grantId == Guid.Empty)
                        {
                            throw new DataChannelException(DataChannelResult.NotAuthorized);
                        }
                        authorization = DataChannelAuthorizationMode.Session;
                    }
                    channel = await m_context.OpenAsync(parameters, authorization, grantId, channelId, linked.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    channel = await AcceptAsync(parameters, linked.Token).ConfigureAwait(false);
                }
                linked.Token.ThrowIfCancellationRequested();
                var result = new DataChannelPubSubFlow(channel, m_context.CarrierKind);
                channel = null;
                return result;
            }
            finally
            {
                try
                {
                    if (channel is not null)
                    {
                        await channel.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (reservedChannelId != 0)
                    {
                        _ = m_context.ReleaseChannelId(reservedChannelId);
                    }
                    lock (m_gate)
                    {
                        m_acquisitions--;
                        if (m_acquisitions == 0)
                        {
                            m_drained?.TrySetResult(true);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Task? acceptLoop;
            Task? drained;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                acceptLoop = m_acceptLoop;
                if (m_acquisitions == 0)
                {
                    drained = null;
                }
                else
                {
                    m_drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    drained = m_drained.Task;
                }
            }
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            if (acceptLoop is not null)
            {
                await acceptLoop.ConfigureAwait(false);
            }
            if (drained is not null)
            {
                await drained.ConfigureAwait(false);
            }
            List<DataChannel> pending;
            lock (m_gate)
            {
                pending = [.. m_pending];
                m_pending.Clear();
            }
            try
            {
                foreach (DataChannel channel in pending)
                {
                    await channel.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_lifetime.Dispose();
            }
        }

        /// <summary>
        /// Registers one bounded exact-resource/protocol waiter or takes an already accepted matching flow.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask<DataChannel> AcceptAsync(
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken)
        {
            PendingAccept waiter;
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int i = 0; i < m_pending.Count; i++)
                {
                    DataChannel channel = m_pending[i];
                    if (channel.State is DataChannelState.Closed or DataChannelState.Faulted)
                    {
                        m_pending.RemoveAt(i--);
                        continue;
                    }
                    if (Matches(parameters, channel.Parameters))
                    {
                        m_pending.RemoveAt(i);
                        return channel;
                    }
                }
                if (m_acceptEnded)
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                waiter = new PendingAccept(parameters.ResourceUri, parameters.Protocol);
                m_waiters.Add(waiter);
                m_acceptLoop ??= Task.Run(AcceptLoopAsync, CancellationToken.None);
            }
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                lock (m_gate)
                {
                    if (m_waiters.Remove(waiter))
                    {
                        waiter.Completion.TrySetCanceled(cancellationToken);
                    }
                }
            });
            return await waiter.Completion.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Routes actual ready flows without creating a second payload or receive queue.
        /// </summary>
        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!m_lifetimeToken.IsCancellationRequested)
                {
                    DataChannel? channel = await m_context.AcceptAsync(m_lifetimeToken).ConfigureAwait(false);
                    if (channel is null)
                    {
                        break;
                    }
                    DataChannelParametersDataType parameters = channel.Parameters;
                    DataChannelResult rejection = DataChannelResult.Good;
                    if (parameters.Protocol is not (DataChannelProfiles.UadpPayload or DataChannelProfiles.JsonPayload))
                    {
                        rejection = DataChannelResult.Unsupported;
                    }
                    lock (m_gate)
                    {
                        if (m_disposed)
                        {
                            rejection = DataChannelResult.Closed;
                        }
                        if (rejection == DataChannelResult.Good)
                        {
                            m_pending.RemoveAll(static pending =>
                                pending.State is DataChannelState.Closed or DataChannelState.Faulted);
                            PendingAccept? waiter = m_waiters.Find(item =>
                                item.ResourceUri == parameters.ResourceUri && item.Protocol == parameters.Protocol);
                            if (waiter is not null)
                            {
                                m_waiters.Remove(waiter);
                                waiter.Completion.TrySetResult(channel);
                            }
                            else if (m_pending.Count < m_maximumPendingFlows)
                            {
                                m_pending.Add(channel);
                            }
                            else
                            {
                                rejection = DataChannelResult.WouldBlock;
                            }
                        }
                    }
                    if (rejection != DataChannelResult.Good)
                    {
                        await channel.ResetAsync(rejection, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (m_lifetimeToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                CompleteAccepts(exception);
                return;
            }
            CompleteAccepts(new DataChannelException(DataChannelResult.Closed));
        }

        /// <summary>
        /// Completes all bounded waiters when the accepted-flow source ends.
        /// </summary>
        private void CompleteAccepts(Exception exception)
        {
            lock (m_gate)
            {
                m_acceptEnded = true;
                foreach (PendingAccept waiter in m_waiters)
                {
                    waiter.Completion.TrySetException(exception);
                }
                m_waiters.Clear();
            }
        }

        /// <summary>
        /// Matches routing identities without URI normalization or publisher-ID substitution.
        /// </summary>
        private static bool Matches(DataChannelParametersDataType requested, DataChannelParametersDataType actual)
        {
            return requested.ResourceUri == actual.ResourceUri && requested.Protocol == actual.Protocol;
        }

        /// <summary>
        /// Represents one cancellable, bounded acquisition.
        /// </summary>
        private sealed class PendingAccept
        {
            /// <summary>
            /// Creates an exact resource/protocol route.
            /// </summary>
            public PendingAccept(string? resourceUri, string? protocol)
            {
                ResourceUri = resourceUri ?? throw new ArgumentNullException(nameof(resourceUri));
                Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            }

            /// <summary>
            /// Gets the exact resource.
            /// </summary>
            public string ResourceUri { get; }

            /// <summary>
            /// Gets the exact payload protocol.
            /// </summary>
            public string Protocol { get; }

            /// <summary>
            /// Gets the asynchronous ownership transfer.
            /// </summary>
            public TaskCompletionSource<DataChannel> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly DataChannelContext m_context;
        private readonly bool m_openLocally;
        private readonly int m_maximumPendingFlows;
        private readonly IDataChannelPubSubGrantProvider? m_grantProvider;
        private readonly CancellationTokenSource m_lifetime;
        private readonly CancellationToken m_lifetimeToken;
        private readonly Lock m_gate = new();
        private readonly List<DataChannel> m_pending = [];
        private readonly List<PendingAccept> m_waiters = [];
        private Task? m_acceptLoop;
        private TaskCompletionSource<bool>? m_drained;
        private int m_acquisitions;
        private bool m_acceptEnded;
        private bool m_disposed;
    }
}
