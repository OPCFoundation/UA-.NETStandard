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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Carries each complete, already encoded NetworkMessage unchanged on an independently authorized Core flow.
    /// Part 14 encoding, security, chunking and metadata remain in the existing PubSub runtime.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelPubSubTransport : IPubSubTransport, IPubSubEncodingTransport
    {
        /// <summary>
        /// Creates an unopened transport. The flow provider remains owned by its application or DI container.
        /// </summary>
        public DataChannelPubSubTransport(
            DataChannelPubSubFlowRequest request,
            IDataChannelPubSubFlowProvider flowProvider,
            TimeProvider timeProvider)
        {
            m_request = request ?? throw new ArgumentNullException(nameof(request));
            m_flowProvider = flowProvider ?? throw new ArgumentNullException(nameof(flowProvider));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            TransportProfileUri = request.Connection.TransportProfileUri ??
                throw new ArgumentException("The connection transport profile is missing.", nameof(request));
            m_parameters = request.Parameters;
        }

        /// <inheritdoc/>
        public string TransportProfileUri { get; }

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => m_request.Direction;

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (m_gate)
                {
                    return m_connected;
                }
            }
        }

        /// <summary>
        /// Gets the borrowed current flow for revisions, orderly directional end and diagnostics,
        /// or null before opening and after closing. The transport retains ownership of the flow.
        /// Session-authorized revisions still require the corresponding model-control authorization.
        /// </summary>
        public DataChannel? Channel
        {
            get
            {
                lock (m_gate)
                {
                    return m_flow?.Channel;
                }
            }
        }

        /// <summary>
        /// Gets the confirmed payload protocol after opening, or the configured protocol before opening.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public string PayloadProtocol
        {
            get
            {
                lock (m_gate)
                {
                    return m_parameters.Protocol ??
                        throw new InvalidOperationException("The flow payload protocol is missing.");
                }
            }
        }

        /// <inheritdoc/>
        public string EncodingProfileUri => DataChannelPubSubConfiguration.GetEncodingProfile(PayloadProtocol);

        /// <inheritdoc/>
        public uint MaxNetworkMessageSize
        {
            get
            {
                lock (m_gate)
                {
                    return m_flow?.Channel.Parameters.MaxMessageSize ?? m_parameters.MaxMessageSize;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            _ = TryEnterLifecycle(throwIfDisposed: true, out _);
            try
            {
                await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExitLifecycle();
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            if (!TryEnterLifecycle(throwIfDisposed: false, out Task disposing))
            {
                await disposing.ConfigureAwait(false);
                return;
            }
            try
            {
                await CloseCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                ExitLifecycle();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (DataChannel channel, CancellationToken lifetime) = GetFlow();
            if ((Direction & PubSubTransportDirection.Send) == 0)
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            if (payload.Length > channel.Parameters.MaxMessageSize)
            {
                throw new DataChannelException(DataChannelResult.TooLarge);
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            DataChannelResult result = await channel.SendAsync(
                new ByteString(payload), cancellationToken: linked.Token).ConfigureAwait(false);
            if (result != DataChannelResult.Good)
            {
                if (channel.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    SetConnected(channel, false, ToStatusCode(result));
                }
                linked.Token.ThrowIfCancellationRequested();
                throw new DataChannelException(result);
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((Direction & PubSubTransportDirection.Receive) == 0)
            {
                yield break;
            }
            if (Interlocked.CompareExchange(ref m_receiving, 1, 0) != 0)
            {
                throw new InvalidOperationException("A PubSub flow has one receive consumer.");
            }
            try
            {
                (DataChannel channel, CancellationToken lifetime) = GetFlow();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                while (true)
                {
                    using DataChannelMessage? message = await ReadAsync(channel, linked.Token).ConfigureAwait(false);
                    if (message is null)
                    {
                        yield break;
                    }
                    // The lease spans the yield: decoding, metadata routing and sinks finish before credit returns.
                    yield return new PubSubTransportFrame(
                        message.Payload.Memory, null, m_timeProvider.GetUtcNow().UtcDateTime);
                }
            }
            finally
            {
                Volatile.Write(ref m_receiving, 0);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool>? completion = null;
            Task disposing;
            Task drained = Task.CompletedTask;
            lock (m_gate)
            {
                if (m_disposeTask is null)
                {
                    m_disposed = true;
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposeTask = completion.Task;
                    if (m_lifecycleUsers != 0)
                    {
                        m_lifecycleDrained = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        drained = m_lifecycleDrained.Task;
                    }
                }
                disposing = m_disposeTask;
            }
            if (completion is not null)
            {
                try
                {
                    try
                    {
                        await CloseCoreAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await drained.ConfigureAwait(false);
                        m_opening?.Dispose();
                        m_lifecycle.Dispose();
                    }
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
            await disposing.ConfigureAwait(false);
        }

        /// <summary>
        /// Tracks admitted lifecycle callers so disposal can release its gate only after their cleanup completes.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private bool TryEnterLifecycle(bool throwIfDisposed, out Task disposing)
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    if (throwIfDisposed)
                    {
                        throw new ObjectDisposedException(nameof(DataChannelPubSubTransport));
                    }
                    disposing = m_disposeTask ??
                        throw new InvalidOperationException("A disposed transport has no disposal operation.");
                    return false;
                }
                m_lifecycleUsers++;
                disposing = Task.CompletedTask;
                return true;
            }
        }

        /// <summary>
        /// Releases an admitted lifecycle call without exposing synchronization to callers.
        /// </summary>
        private void ExitLifecycle()
        {
            lock (m_gate)
            {
                if (--m_lifecycleUsers == 0)
                {
                    m_lifecycleDrained?.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Acquires and validates one actual flow while allowing close to cancel a pending establishment.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask OpenCoreAsync(CancellationToken cancellationToken)
        {
            await m_lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DataChannelPubSubFlow? previous;
                CancellationTokenSource? previousLifetime;
                CancellationTokenSource opening;
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        throw new ObjectDisposedException(nameof(DataChannelPubSubTransport));
                    }
                    if (m_closeRequests != 0)
                    {
                        throw new DataChannelException(DataChannelResult.Closed);
                    }
                    if (m_connected)
                    {
                        return;
                    }
                    previous = m_flow;
                    previousLifetime = m_lifetime;
                    m_flow = null;
                    m_lifetime = null;
                    opening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    m_opening = opening;
                }
                try
                {
                    if (previous is not null)
                    {
                        previous.Channel.StateChanged -= OnFlowStateChanged;
                        await previous.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    previousLifetime?.Dispose();
                }
                DataChannelPubSubFlow acquired = await m_flowProvider.OpenAsync(m_request, opening.Token)
                    .ConfigureAwait(false);
                try
                {
                    opening.Token.ThrowIfCancellationRequested();
                    ValidateFlow(acquired);
                    lock (m_gate)
                    {
                        opening.Token.ThrowIfCancellationRequested();
                        m_parameters = acquired.Channel.Parameters;
                        m_lifetime = new CancellationTokenSource();
                        m_flow = acquired;
                        m_receiveEnded = false;
                        acquired.Channel.StateChanged += OnFlowStateChanged;
                    }
                    SetConnected(acquired.Channel, true, StatusCodes.Good);
                }
                catch
                {
                    acquired.Channel.StateChanged -= OnFlowStateChanged;
                    lock (m_gate)
                    {
                        if (ReferenceEquals(m_flow, acquired))
                        {
                            m_flow = null;
                            m_connected = false;
                            m_lifetime?.Dispose();
                            m_lifetime = null;
                        }
                    }
                    await acquired.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_opening?.Dispose();
                    m_opening = null;
                }
                m_lifecycle.Release();
            }
        }

        /// <summary>
        /// Cancels establishment/receive, closes only this flow and awaits the complete cleanup.
        /// </summary>
        private async ValueTask CloseCoreAsync()
        {
            CancellationTokenSource? opening;
            CancellationTokenSource? lifetime;
            lock (m_gate)
            {
                m_closeRequests++;
                opening = m_opening;
                lifetime = m_lifetime;
            }
            Cancel(opening);
            Cancel(lifetime);
            await m_lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                DataChannelPubSubFlow? flow;
                bool wasConnected;
                lock (m_gate)
                {
                    flow = m_flow;
                    m_flow = null;
                    lifetime = m_lifetime;
                    m_lifetime = null;
                    wasConnected = m_connected;
                    m_connected = false;
                }
                try
                {
                    if (flow is not null)
                    {
                        flow.Channel.StateChanged -= OnFlowStateChanged;
                        await flow.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    lifetime?.Dispose();
                    if (wasConnected)
                    {
                        StateChanged?.Invoke(this, new PubSubTransportStateChangedEventArgs(
                            false, StatusCodes.Good, "The PubSub flow was closed."));
                    }
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_closeRequests--;
                }
                m_lifecycle.Release();
            }
        }

        /// <summary>
        /// Checks the actual immutable descriptor and admitted bounds before any publisher or reader is enabled.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void ValidateFlow(DataChannelPubSubFlow flow)
        {
            DataChannelParametersDataType expected = m_request.Parameters;
            DataChannelParametersDataType actual = flow.Channel.Parameters;
            if (flow.CarrierKind != m_request.CarrierKind ||
                flow.Channel.State != DataChannelState.Open ||
                actual.ResourceUri != expected.ResourceUri ||
                actual.Protocol != expected.Protocol ||
                actual.ContentType != expected.ContentType ||
                actual.Direction != expected.Direction ||
                actual.Delivery != expected.Delivery ||
                actual.Protection != expected.Protection ||
                actual.AllowQueueExpiry != expected.AllowQueueExpiry ||
                actual.Priority != expected.Priority ||
                actual.MaxMessageSize > expected.MaxMessageSize ||
                actual.MaxReceiveBytes > expected.MaxReceiveBytes ||
                (expected.MaxRate != 0 && (actual.MaxRate == 0 || actual.MaxRate > expected.MaxRate)))
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            DataChannelPubSubConfiguration.ValidateParameters(actual, flow.CarrierKind);
            bool canSend = actual.Direction == DataChannelDirection.Duplex ||
                actual.Direction == DataChannelDirection.OpenerToAcceptor == flow.Channel.IsLocalOpener;
            bool canReceive = actual.Direction == DataChannelDirection.Duplex ||
                actual.Direction == DataChannelDirection.AcceptorToOpener == flow.Channel.IsLocalOpener;
            if (((Direction & PubSubTransportDirection.Send) != 0 && !canSend) ||
                ((Direction & PubSubTransportDirection.Receive) != 0 && !canReceive))
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
        }

        /// <summary>
        /// Gets a flow and cancellation snapshot without exposing synchronization.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="DataChannelException"></exception>
        private (DataChannel Channel, CancellationToken Lifetime) GetFlow()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(DataChannelPubSubTransport));
                }
                if (!m_connected || m_flow is null || m_lifetime is null)
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                return (m_flow.Channel, m_lifetime.Token);
            }
        }

        /// <summary>
        /// Reports remote EOF or failure while leaving message ownership with the consumer.
        /// </summary>
        private async ValueTask<DataChannelMessage?> ReadAsync(DataChannel channel, CancellationToken cancellationToken)
        {
            try
            {
                DataChannelMessage? message = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    lock (m_gate)
                    {
                        if (ReferenceEquals(m_flow?.Channel, channel))
                        {
                            m_receiveEnded = true;
                        }
                    }
                    DataChannelStatusDataType status = channel.GetStatus();
                    if (status.State is DataChannelState.Closed or DataChannelState.Faulted)
                    {
                        SetConnected(channel, false, ToStatusCode(status.Result));
                    }
                }
                return message;
            }
            catch (DataChannelException exception)
            {
                SetConnected(channel, false, ToStatusCode(exception.Result));
                throw;
            }
        }

        /// <summary>
        /// Observes flow closure even on publisher-only connections without reading an unauthorized direction.
        /// </summary>
        private void OnFlowStateChanged(DataChannelStateChangedEventArgs args)
        {
            if (args.Status.State is DataChannelState.Closed or DataChannelState.Faulted)
            {
                if (args.Status.State == DataChannelState.Closed &&
                    args.Status.Result == DataChannelResult.Good &&
                    (Direction & PubSubTransportDirection.Receive) != 0)
                {
                    lock (m_gate)
                    {
                        if (ReferenceEquals(m_flow?.Channel, args.Channel) && !m_receiveEnded)
                        {
                            // Core END may precede delivery of owned/queued messages. EOF is the drain boundary.
                            return;
                        }
                    }
                }
                SetConnected(args.Channel, false, ToStatusCode(args.Status.Result));
            }
        }

        /// <summary>
        /// Publishes actual connection-state transitions outside synchronization.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void SetConnected(DataChannel channel, bool connected, StatusCode status)
        {
            lock (m_gate)
            {
                if (!ReferenceEquals(m_flow?.Channel, channel) || m_connected == connected)
                {
                    return;
                }
                if (connected && channel.State != DataChannelState.Open)
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                m_connected = connected;
            }
            StateChanged?.Invoke(this, new PubSubTransportStateChangedEventArgs(
                connected, status, connected ? "The PubSub flow is ready." : "The PubSub flow is unavailable."));
        }

        /// <summary>
        /// Cancels a concurrent open/read without racing disposal of its token source.
        /// </summary>
        private static void Cancel(CancellationTokenSource? source)
        {
            try
            {
                source?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Maps flow outcomes to existing PubSub diagnostic status codes without adding wire status values.
        /// </summary>
        private static StatusCode ToStatusCode(DataChannelResult result)
        {
            return result switch
            {
                DataChannelResult.Good => StatusCodes.Good,
                DataChannelResult.Unsupported => StatusCodes.BadNotSupported,
                DataChannelResult.InvalidArgument => StatusCodes.BadInvalidArgument,
                DataChannelResult.InvalidState => StatusCodes.BadInvalidState,
                DataChannelResult.NotAuthorized => StatusCodes.BadUserAccessDenied,
                DataChannelResult.SecurityFailure => StatusCodes.BadSecurityChecksFailed,
                DataChannelResult.TooLarge => StatusCodes.BadEncodingLimitsExceeded,
                DataChannelResult.Timeout => StatusCodes.BadTimeout,
                DataChannelResult.WouldBlock => StatusCodes.BadResourceUnavailable,
                _ => StatusCodes.BadConnectionClosed
            };
        }

        private readonly DataChannelPubSubFlowRequest m_request;
        private readonly IDataChannelPubSubFlowProvider m_flowProvider;
        private readonly TimeProvider m_timeProvider;
        private readonly SemaphoreSlim m_lifecycle = new(1, 1);
        private readonly Lock m_gate = new();
        private DataChannelParametersDataType m_parameters;
        private DataChannelPubSubFlow? m_flow;
        private CancellationTokenSource? m_opening;
        private CancellationTokenSource? m_lifetime;
        private Task? m_disposeTask;
        private TaskCompletionSource<bool>? m_lifecycleDrained;
        private int m_lifecycleUsers;
        private int m_receiving;
        private int m_closeRequests;
        private bool m_connected;
        private bool m_receiveEnded;
        private bool m_disposed;
    }
}
