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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Hosts ordinary UASC Services on explicitly supplied native lanes without opening any socket.
    /// Accepting connections and routing unrelated resources remain the native host's responsibility.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelClientServerListener : ITransportListener
    {
        /// <summary>
        /// Creates a virtual listener for one explicitly selected native carrier.
        /// </summary>
        public DataChannelClientServerListener(
            DataChannelCarrierKind carrierKind,
            ITelemetryContext telemetry,
            DataChannelClientServerOptions? options = null,
            IBufferManagerFactory? bufferManagerFactory = null,
            TimeProvider? timeProvider = null,
            IDataChannelAcceptedFlowSource? source = null)
        {
            if (carrierKind is not (DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport))
            {
                throw new ArgumentOutOfRangeException(nameof(carrierKind));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            CarrierKind = carrierKind;
            m_options = options ?? new DataChannelClientServerOptions();
            m_options.Validate();
            m_listener = new TcpTransportListener(
                telemetry, timeProvider, bufferManagerFactory ?? DefaultBufferManagerFactory.Instance);
            m_source = source;
        }

        /// <summary>
        /// Gets the selected native carrier. Its connection is not owned by this listener.
        /// </summary>
        public DataChannelCarrierKind CarrierKind { get; }

        /// <inheritdoc/>
        public string UriScheme => CarrierKind == DataChannelCarrierKind.Quic ? "opc.quic" : "opc.wt";

        /// <inheritdoc/>
        public string ListenerId => m_listener.ListenerId;

        /// <summary>
        /// Gets the virtual ordinary Service endpoint selected by UACP HEL.
        /// It does not replace the native transport's connection/CONNECT URL.
        /// </summary>
        public Uri EndpointUrl => m_listener.EndpointUrl;

        /// <inheritdoc/>
        public event ConnectionWaitingHandlerAsync ConnectionWaiting
        {
            add => m_listener.ConnectionWaiting += value;
            remove => m_listener.ConnectionWaiting -= value;
        }

        /// <inheritdoc/>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged
        {
            add => m_listener.ConnectionStatusChanged += value;
            remove => m_listener.ConnectionStatusChanged -= value;
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }
            if (baseAddress == null)
            {
                throw new ArgumentNullException(nameof(baseAddress));
            }
            ct.ThrowIfCancellationRequested();
            if (settings.DataChannels != null)
            {
                throw new ArgumentException(
                    "A native Client/Server lane cannot host another Data Channel context.", nameof(settings));
            }
            if (baseAddress.Scheme != UriScheme ||
                settings.ReverseConnectListener ||
                settings.Configuration == null ||
                settings.Factory == null ||
                settings.ServerCertificates == null ||
                settings.CertificateValidator == null ||
                settings.Descriptions == null ||
                settings.Descriptions.Count == 0)
            {
                throw new ArgumentException("Complete ordinary UASC listener settings and a native URL are required.",
                    nameof(settings));
            }
            foreach (EndpointDescription endpoint in settings.Descriptions)
            {
                DataChannelClientServerSecurity.ValidateEndpoint(baseAddress, endpoint);
            }
            var configuration = (EndpointConfiguration)settings.Configuration.Clone();
            configuration.MaxBufferSize = Math.Min(configuration.MaxBufferSize, m_options.MaxChunkSize);
            if (configuration.MaxBufferSize < TcpMessageLimits.MinBufferSize ||
                configuration.MaxMessageSize <= 0 ||
                configuration.OperationTimeout <= 0 ||
                configuration.ChannelLifetime <= 0)
            {
                throw new ArgumentException("Finite ordinary UASC limits are required.", nameof(settings));
            }
            var snapshot = new TransportListenerSettings
            {
                Configuration = configuration,
                Descriptions = settings.Descriptions.ConvertAll(endpoint => (EndpointDescription)endpoint.Clone()),
                ServerCertificates = settings.ServerCertificates,
                CertificateValidator = settings.CertificateValidator,
                SecurityPolicyRegistry = settings.SecurityPolicyRegistry,
                NamespaceUris = settings.NamespaceUris,
                Factory = settings.Factory,
                MaxChannelCount = settings.MaxChannelCount > 0
                    ? Math.Min(settings.MaxChannelCount, m_options.MaxLanes) : m_options.MaxLanes,
                ConnectionRateLimiter = settings.ConnectionRateLimiter,
                ChunkReassemblyBudget = settings.ChunkReassemblyBudget,
                SessionBindingProvider = settings.SessionBindingProvider,
                ResourceIsolationProvider = settings.ResourceIsolationProvider,
                HandshakeTimeout = settings.HandshakeTimeout,
                AcceptedTransportDecorator = settings.AcceptedTransportDecorator,
                OnAcceptedChannel = settings.OnAcceptedChannel
            };
            lock (m_gate)
            {
                if (m_settings != null || m_closing)
                {
                    throw new InvalidOperationException("The native lane listener cannot be reopened.");
                }
                m_listener.OpenExternal(baseAddress, snapshot, callback);
                m_settings = snapshot;
                if (m_source != null)
                {
                    m_acceptLoop = AcceptRoutedLanesAsync(m_source);
                }
            }
            return default;
        }

        /// <summary>
        /// Transfers an already routed reserved flow into the ordinary UASC listener.
        /// Ownership of this flow transfers on entry, including failed or cancelled acceptance.
        /// No other flow or native connection is consumed or closed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="flow"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public async ValueTask<DataChannelClientServerLane> AcceptAsync(
            DataChannel flow, CancellationToken cancellationToken = default)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }
            var factory = new DataChannelClientServerByteTransportFactory(flow.Context, flow);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                DataChannelClientServerSecurity.ValidateContext(flow.Context);
                if (flow.Context.CarrierKind != CarrierKind)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                }
                DataChannelClientServerLane lane;
                lock (m_gate)
                {
                    TransportListenerSettings settings = m_settings ??
                        throw new InvalidOperationException("Open the lane listener before accepting flows.");
                    if (m_closing)
                    {
                        throw new ObjectDisposedException(nameof(DataChannelClientServerListener));
                    }
                    m_lanes.RemoveWhere(lane =>
                    {
                        if (!lane.Completion.IsCompleted)
                        {
                            return false;
                        }
                        lane.CloseChannel();
                        return true;
                    });
                    if (m_lanes.Count >= settings.MaxChannelCount)
                    {
                        throw new ServiceResultException(StatusCodes.BadTcpNotEnoughResources);
                    }
                    DataChannelPeerIdentity identity = flow.Context.Identity.Peer;
                    foreach (EndpointDescription endpoint in settings.Descriptions!)
                    {
                        if (endpoint.Server?.ApplicationUri != identity.LocalApplicationUri)
                        {
                            throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
                        }
                        using CertificateEntry certificate = settings.ServerCertificates!
                            .AcquireApplicationCertificateBySecurityPolicy(endpoint.SecurityPolicyUri!) ??
                            throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                        DataChannelClientServerSecurity.ValidateIdentity(
                            certificate.Certificate, identity.LocalApplicationUri, identity.LocalKeyThumbprint);
                    }
                    int maximum = Math.Min(settings.Configuration!.MaxBufferSize,
                        checked((int)flow.Parameters.MaxMessageSize));
                    if (maximum < TcpMessageLimits.MinBufferSize)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTcpNotEnoughResources, "The admitted flow is too small for UACP.");
                    }
                    TcpListenerChannel channel = m_listener.AcceptTransport(
                        factory, new DataChannelClientServerSecurity(flow.Context, settings.CertificateValidator!),
                        maximum);
                    lane = new DataChannelClientServerLane(flow, m_listener, channel, factory.Transport!);
                    m_lanes.Add(lane);
                }
                _ = ObserveClosureAsync(lane);
                return lane;
            }
            catch
            {
                await factory.CloseAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Accepts exactly this lane from a shared resource router, never directly from its contexts.
        /// The source, optional model and all unrelated resources remain independently owned.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <c>null</c>.</exception>
        public async ValueTask<DataChannelClientServerLane?> AcceptAsync(
            IDataChannelAcceptedFlowSource source, CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            DataChannel? flow = await source.AcceptAsync(
                DataChannelProfiles.ClientServerResource, DataChannelProfiles.OpaquePayload, CarrierKind,
                cancellationToken).ConfigureAwait(false);
            return flow == null ? null : await AcceptAsync(flow, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void CertificateUpdate(ICertificateValidatorEx validator, ICertificateRegistry serverCertificates)
        {
            m_listener.CertificateUpdate(validator, serverCertificates);
            lock (m_gate)
            {
                if (m_settings != null)
                {
                    m_settings.CertificateValidator = validator;
                    m_settings.ServerCertificates = serverCertificates;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            m_listener.UpdateChannelLastActiveTime(globalChannelId);
        }

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            throw new NotSupportedException("Native connection roles are selected by the explicit native transport.");
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            bool start;
            lock (m_gate)
            {
                start = !m_closing;
                m_closing = true;
            }
            if (start)
            {
                _ = CloseCoreAsync();
            }
            await m_completion.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return CloseAsync();
        }

        /// <summary>
        /// Observes per-flow errors and prevents closed lanes from retaining listener capacity.
        /// </summary>
        private async Task ObserveClosureAsync(DataChannelClientServerLane lane)
        {
            try
            {
                await lane.Completion.ConfigureAwait(false);
            }
            catch (Exception) when (lane.Completion.IsFaulted || lane.Completion.IsCanceled)
            {
                // The byte adapter reports failure; the lane's Completion retains it for its owner.
            }
            finally
            {
                try
                {
                    lane.CloseChannel();
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_lanes.Remove(lane);
                    }
                }
            }
        }

        /// <summary>
        /// Consumes only the reserved resource route supplied by the application.
        /// Rejection of one lane never steals or closes another resource's flow.
        /// </summary>
        private async Task AcceptRoutedLanesAsync(IDataChannelAcceptedFlowSource source)
        {
            try
            {
                while (!m_stopping.IsCancellationRequested)
                {
                    DataChannel? flow = await source.AcceptAsync(
                        DataChannelProfiles.ClientServerResource, DataChannelProfiles.OpaquePayload, CarrierKind,
                        m_stopping.Token).ConfigureAwait(false);
                    if (flow == null)
                    {
                        return;
                    }
                    try
                    {
                        await AcceptAsync(flow, m_stopping.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is
                        ServiceResultException or ArgumentException or DataChannelException)
                    {
                        // Admission already reset this rejected lane; keep serving the other authorized lanes.
                    }
                }
            }
            catch (OperationCanceledException) when (m_stopping.IsCancellationRequested)
            {
                // The borrowed source and its other resource routes remain alive.
            }
            catch (ObjectDisposedException) when (m_stopping.IsCancellationRequested)
            {
                // Closing can win after routing has returned a flow but before it is attached.
            }
        }

        /// <summary>
        /// Stops UASC first and joins every owned native flow without disposing any context.
        /// </summary>
        private async Task CloseCoreAsync()
        {
            try
            {
                try
                {
                    await m_stopping.CancelAsync().ConfigureAwait(false);
                    await m_acceptLoop.ConfigureAwait(false);
                }
                finally
                {
                    DataChannelClientServerLane[] lanes;
                    lock (m_gate)
                    {
                        lanes = [.. m_lanes];
                    }
                    try
                    {
                        await m_listener.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await Task.WhenAll(lanes.Select(lane => lane.DisposeAsync().AsTask())).ConfigureAwait(false);
                    }
                }
                m_completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_completion.TrySetException(exception);
            }
            finally
            {
                m_stopping.Dispose();
            }
        }

        private readonly Lock m_gate = new();
        private readonly HashSet<DataChannelClientServerLane> m_lanes = [];
        private readonly TcpTransportListener m_listener;
        private readonly DataChannelClientServerOptions m_options;
        private readonly IDataChannelAcceptedFlowSource? m_source;
        private readonly CancellationTokenSource m_stopping = new();
        private readonly TaskCompletionSource<bool> m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task m_acceptLoop = Task.CompletedTask;
        private TransportListenerSettings? m_settings;
        private bool m_closing;
    }

    /// <summary>
    /// Owns one accepted UASC lane. Closing it never closes the shared native context.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelClientServerLane : IAsyncDisposable
    {
        /// <summary>
        /// Records the ordinary channel and the transport whose release must be joined.
        /// </summary>
        internal DataChannelClientServerLane(
            DataChannel flow,
            TcpTransportListener owner,
            TcpListenerChannel channel,
            DataChannelClientServerByteTransport transport)
        {
            Flow = flow;
            m_owner = owner;
            m_channel = channel;
            m_transport = transport;
        }

        /// <summary>
        /// Gets the reserved flow for diagnostics, not independent sends or reads.
        /// </summary>
        public DataChannel Flow { get; }

        /// <summary>
        /// Gets the inner channel's globally qualified identifier.
        /// </summary>
        public string SecureChannelId => m_channel.GlobalChannelId;

        /// <summary>
        /// Gets completion of per-flow native release after UASC closure.
        /// </summary>
        public Task Completion => m_transport.Completion;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            CloseChannel();
            await m_transport.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases ordinary listener admission as well as the transport's native-flow reservation.
        /// </summary>
        internal void CloseChannel()
        {
            m_owner.CloseExternalChannel(m_channel);
        }

        private readonly TcpTransportListener m_owner;
        private readonly TcpListenerChannel m_channel;
        private readonly DataChannelClientServerByteTransport m_transport;
    }
}
