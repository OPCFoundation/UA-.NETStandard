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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel for the ITransportChannel interface.
    /// Implements the UA-SC security and UA Binary encoding.
    /// The byte transport layer requires an IUaSCByteTransportFactory implementation.
    /// </summary>
    public class UaSCUaBinaryTransportChannel : ITransportChannel, ISecureChannel, IServerRetryAfterHintProvider
    {
        private const int kChannelCloseDefault = 1_000;

        /// <summary>
        /// Create a transport channel from a byte transport factory.
        /// </summary>
        /// <param name="transportFactory">The byte transport factory.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        public UaSCUaBinaryTransportChannel(
            IUaSCByteTransportFactory transportFactory,
            ITelemetryContext telemetry)
            : this(
                transportFactory,
                telemetry,
                timeProvider: null,
                DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Create a transport channel from a byte transport factory.
        /// </summary>
        /// <param name="transportFactory">The byte transport factory.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="timeProvider">Time provider to use for timers and durations.</param>
        public UaSCUaBinaryTransportChannel(
            IUaSCByteTransportFactory transportFactory,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
            : this(
                transportFactory,
                telemetry,
                timeProvider,
                DefaultBufferManagerFactory.Instance)
        {
        }

        /// <summary>
        /// Creates a transport channel with explicit time and buffer-manager providers.
        /// </summary>
        /// <param name="transportFactory">The byte transport factory.</param>
        /// <param name="telemetry">Telemetry context to use.</param>
        /// <param name="timeProvider">Time provider to use for timers and durations.</param>
        /// <param name="bufferManagerFactory">Factory used to create channel buffer managers.</param>
        public UaSCUaBinaryTransportChannel(
            IUaSCByteTransportFactory transportFactory,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider,
            IBufferManagerFactory bufferManagerFactory)
        {
            m_transportFactory = transportFactory;
            m_telemetry = telemetry;
            m_logger = m_telemetry.CreateLogger<UaSCUaBinaryTransportChannel>();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_bufferManagerFactory = bufferManagerFactory ??
                throw new ArgumentNullException(nameof(bufferManagerFactory));
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
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                m_connecting.Wait();
                try
                {
                    m_channel?.Dispose();
                    m_channel = null;
                }
                finally
                {
                    m_connecting.Release();
                    m_connecting.Dispose();
                }

                DisposeSettingsCertificates();
            }
        }

        /// <summary>
        /// Returns the channel's underlying byte transport (TCP, WebSocket,
        /// etc.) if connected, or <c>null</c> otherwise. Useful for advanced
        /// scenarios — typical consumers should use <see cref="ITransportChannel"/>.
        /// </summary>
        public IUaSCByteTransport? Transport => m_channel?.Transport;

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add => m_OnTokenActivated += value;
            remove => m_OnTokenActivated -= value;
        }

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures =>
            m_channel?.Transport?.Features ?? TransportChannelFeatures.None;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => m_settings?.Description ?? throw BadNotConnected();

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_settings?.Configuration ?? throw BadNotConnected();

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => m_quotas?.MessageContext ?? throw BadNotConnected();

        /// <inheritdoc/>
        public ChannelToken CurrentToken => m_channel?.CurrentToken ?? new();

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => m_channel?.ChannelThumbprint ?? [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate => m_channel?.ClientChannelCertificate ?? [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate => m_channel?.ServerChannelCertificate ?? [];

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            return OpenInternalAsync(url, null, settings, ct);
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            return OpenInternalAsync(connection.EndpointUrl, connection, settings, ct);
        }

        /// <inheritdoc/>
        public async ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UaSCUaBinaryTransportChannel));
            }
            if (m_url == null)
            {
                throw BadNotConnected();
            }
            UaSCUaBinaryClientChannel? previousChannel = null;
            await m_connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_logger.LogInformation(
                    "TransportChannel RECONNECT: Reconnecting to {Url}.",
                    m_url);

                // the new channel must be connected first because WinSock
                // will reuse sockets and this can result in messages sent
                // over the old socket arriving as messages on the new socket.
                // if this happens the new channel is shutdown because of a
                // security violation. Therefore close the previous channel
                // in the finally block after the new channel is connected.
                previousChannel = m_channel;
                m_channel = null;
                UaSCUaBinaryClientChannel newChannel = CreateChannel(m_telemetry, connection);

                // Connect the new channel
                await newChannel.ConnectAsync(
                    m_url,
                    OperationTimeout,
                    ct).ConfigureAwait(false);

                m_channel = newChannel;
                m_logger.LogInformation(
                    "TransportChannel RECONNECT: Reconnected to {Url}.",
                    m_url);
            }
            finally
            {
                m_connecting.Release(); // Give access again to the new channel

                // close previous channel.
                if (previousChannel != null)
                {
                    m_logger.LogDebug(
                       "TransportChannel RECONNECT: Closing old channel to {Url}.",
                       m_url);
                    try
                    {
                        await previousChannel.CloseAsync(
                            kChannelCloseDefault,
                            default).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        // do nothing.
                        m_logger.LogDebug(
                            e,
                            "Exception while closing old channel during Reconnect.");
                    }
                    previousChannel.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken ct)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UaSCUaBinaryTransportChannel));
            }
            await m_connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                UaSCUaBinaryClientChannel? channel = m_channel;
                // Indicate we are closed
                m_channel = null;
                m_url = null;

                if (channel != null)
                {
                    try
                    {
                        await channel.CloseAsync(kChannelCloseDefault, ct) // TODO: Configurable
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Ignoring error during close of channel.");
                    }
                    finally
                    {
                        try
                        {
                            channel.Dispose();
                        }
                        finally
                        {
                            DisposeSettingsCertificates();
                        }
                    }
                }
                else
                {
                    DisposeSettingsCertificates();
                }
            }
            finally
            {
                m_connecting.Release();
            }
        }

        private void DisposeSettingsCertificates()
        {
            if (m_settings == null)
            {
                return;
            }

            m_settings.ServerCertificate?.Dispose();
            m_settings.ServerCertificate = null;
            m_settings.ClientCertificate?.Dispose();
            m_settings.ClientCertificate = null;
            m_settings.ClientCertificateChain?.Dispose();
            m_settings.ClientCertificateChain = null;
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct)
        {
            // A UA-TCP server retry-after hint arrives on an Error message that faults the
            // channel; it is captured out-of-band by OnInnerChannelStateChanged. This send
            // path therefore stays a lean pass-through with no per-request try/catch (and no
            // async state machine), keeping the hot path allocation- and overhead-free.
            UaSCUaBinaryClientChannel? channel = m_channel;
            if (channel != null)
            {
                return channel.SendRequestAsync(request, OperationTimeout, ct);
            }
            return SendRequestInternalAsync(request, ct);
        }

        /// <summary>
        /// Wait under on the connection lock until connect or close completes
        /// and send or throw then.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<IServiceResponse> SendRequestInternalAsync(
            IServiceRequest request,
            CancellationToken ct)
        {
            if (m_disposed)
            {
                // right now this can happen if Dispose is called
                // Before channel users (e.g. keep alive requests)
                // are cancelled and stopped.
                // TODO: Areas like this should be fixed eventually.
                throw BadNotConnected();
                // throw new ObjectDisposedException(nameof(UaSCUaBinaryTransportChannel));
            }
            UaSCUaBinaryClientChannel? channel;
            await m_connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Get under lock
                // Channel was closed
                channel = m_channel ?? throw BadNotConnected();
            }
            finally
            {
                m_connecting.Release();
            }
            return await channel.SendRequestAsync(
                request,
                OperationTimeout,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Create and connect the channel. Reset to non open state in case
        /// of failure
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        private async ValueTask OpenInternalAsync(
            Uri endpointUrl,
            ITransportWaitingConnection? connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UaSCUaBinaryTransportChannel));
            }
            UaSCUaBinaryClientChannel? channel;
            await m_connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_channel != null || m_url != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTcpInternalError,
                        "Channel is already open.");
                }
                SaveSettings(endpointUrl, settings);
                channel = CreateChannel(m_telemetry, connection);
                m_channel = channel;
            }
            finally
            {
                m_connecting.Release();
            }

            //
            // We do not hold the lock while connecting to let send requests get
            // queued to the channel. The inner channel allows queueing during
            // state connecting, which will then be played as soon as connect
            // completes. Therefore it is safe to now call SendRequestAsync with
            // the caveat that these requests may then fail if connect fails.
            //
            try
            {
                await channel.ConnectAsync(m_url!, OperationTimeout, ct).ConfigureAwait(false);
            }
            catch
            {
                await m_connecting.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    Debug.Assert(m_channel == channel,
                        "Should have thrown if m_channel was null");
                    // Reset as not opened to allow OpenAsync again.
                    m_channel = null;
                    m_url = null;
                    channel.Dispose();
                    DisposeSettingsCertificates();
                    throw;
                }
                finally
                {
                    m_connecting.Release();
                }
            }
        }

        /// <summary>
        /// Saves the settings so the channel can be opened later.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="settings">The settings.</param>
        /// <exception cref="ArgumentException"></exception>
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            // save the settings.
            m_url = url;
            m_settings = settings;

            EndpointConfiguration configuration = settings.Configuration
                ?? throw new ArgumentException("EndpointConfiguration is required.", nameof(settings));
            IEncodeableFactory factory = settings.Factory
                ?? throw new ArgumentException("Factory is required.", nameof(settings));

            OperationTimeout = configuration.OperationTimeout;
            if (OperationTimeout <= 0)
            {
                throw new ArgumentException("Timeout must be greater than 0");
            }

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, factory)
            {
                MaxArrayLength = configuration.MaxArrayLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(
                        configuration.MaxMessageSize),
                MaxStringLength = configuration.MaxStringLength,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = settings.NamespaceUris ?? new NamespaceTable(),
                ServerUris = new StringTable()
            })
            {
                MaxBufferSize = configuration.MaxBufferSize,
                MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(
                    configuration.MaxMessageSize),
                ChannelLifetime = configuration.ChannelLifetime,
                SecurityTokenLifetime = configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator
            };

            // create the buffer manager.
            m_bufferManager = new BufferManager(
                m_bufferManagerFactory.Create(
                    "Client",
                    configuration.MaxBufferSize,
                    m_telemetry));

            // notify derived classes - e.g. WSS - that settings have been
            // bound so they can push channel-level options (TLS cert validator,
            // client TLS certificate) into the byte-transport factory before
            // the channel attempts to connect.
            OnSettingsSaved(settings, m_quotas);
        }

        /// <summary>
        /// Hook called by the channel immediately after its settings and
        /// quotas have been bound, but before any connect attempt. The
        /// default implementation is a no-op; derived classes override to
        /// push channel-level options into the byte-transport factory. For
        /// example, the WSS channel uses this to propagate the OPC UA
        /// certificate validator and the client TLS certificate into
        /// <c>ClientWebSocketOptions.RemoteCertificateValidationCallback</c>
        /// and <c>ClientWebSocketOptions.ClientCertificates</c>.
        /// </summary>
        /// <param name="settings">The bound transport channel settings.</param>
        /// <param name="quotas">The bound channel quotas.</param>
        protected virtual void OnSettingsSaved(
            TransportChannelSettings settings,
            ChannelQuotas quotas)
        {
        }

        /// <summary>
        /// Opens the channel before sending the request.
        /// </summary>
        /// <param name="telemetry">Telemetry context</param>
        /// <param name="connection">A reverse connection, null otherwise.</param>
        /// <exception cref="ServiceResultException"></exception>
        private UaSCUaBinaryClientChannel CreateChannel(
            ITelemetryContext telemetry,
            ITransportWaitingConnection? connection = null)
        {
            if (m_url == null ||
                m_settings == null ||
                m_quotas == null ||
                m_bufferManager == null)
            {
                throw BadNotConnected();
            }

            IUaSCByteTransport? transport = null;
            if (connection != null)
            {
                transport = connection.Handle as IUaSCByteTransport
                    ?? throw ServiceResultException.Unexpected(
                        "Waiting Connection Handle is not of type IUaSCByteTransport.");
            }

            string id = Guid.NewGuid().ToString();

            // Hand the channel its own owning references to the shared
            // certificates. The inner channel disposes ClientCertificate,
            // ClientCertificateChain and ServerCertificate when it is
            // disposed, while m_settings retains its own owning references
            // (released by DisposeSettingsCertificates). A transport channel
            // can create several inner channels over its lifetime (connect
            // retry, reconnect, failover); without a per-channel AddRef the
            // first channel's disposal would release the single shared
            // reference and invalidate the certificate handle still used by
            // the surviving channel - surfacing as a CryptographicException
            // ("m_safeCertContext is an invalid handle") on the next signing.
            Certificate? clientCertificate = m_settings.ClientCertificate?.AddRef();
            CertificateCollection? clientCertificateChain =
                m_settings.ClientCertificateChain?.AddRef();
            Certificate? serverCertificate = m_settings.ServerCertificate?.AddRef();
            UaSCUaBinaryClientChannel channel;
            try
            {
                channel = new UaSCUaBinaryClientChannel(
                    id,
                    m_bufferManager,
                    m_transportFactory,
                    m_quotas,
                    clientCertificate,
                    clientCertificateChain,
                    serverCertificate,
                    m_settings.Description,
                    telemetry,
                    m_timeProvider);
            }
            catch
            {
                // The channel never took ownership; release the references
                // we added above so they are not leaked.
                clientCertificate?.Dispose();
                clientCertificateChain?.Dispose();
                serverCertificate?.Dispose();
                throw;
            }

            // use transport for reverse connections, ignore otherwise
            if (transport != null)
            {
                channel.Transport = transport;
                channel.StartReceiveLoop();
                channel.ReverseSocket = true;
            }

            // Register the token changed event handler with the internal channel
            channel.TokenActivatedCallback =
                (current, previous) => m_OnTokenActivated?.Invoke(
                    this,
                    current,
                    previous);
            channel.SetStateChangedCallback(OnInnerChannelStateChanged);
            return channel;
        }

        TimeSpan? IServerRetryAfterHintProvider.ConsumeServerRetryAfterHint()
        {
            long ticks = Interlocked.Exchange(ref m_serverRetryAfterHintTicks, 0);
            return ticks > 0 ? TimeSpan.FromTicks(ticks) : null;
        }

        private void OnInnerChannelStateChanged(
            UaSCUaBinaryChannel channel,
            TcpChannelState state,
            ServiceResult error)
        {
            _ = channel;

            if (state != TcpChannelState.Faulted)
            {
                return;
            }

            StoreServerRetryAfterHint(error);
        }

        private void StoreServerRetryAfterHint(ServiceResult? error)
        {
            TimeSpan? serverRetryAfter = RetryAfterHint.ParseServerBusyRetryAfter(error);
            if (!serverRetryAfter.HasValue)
            {
                return;
            }

            Interlocked.Exchange(ref m_serverRetryAfterHintTicks, serverRetryAfter.Value.Ticks);
            m_logger.LogInformation(
                "TransportChannel: received UA-TCP server retry-after hint {Delay} ms.",
                serverRetryAfter.Value.TotalMilliseconds);
        }

        private ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "{0} not open.",
                nameof(UaSCUaBinaryTransportChannel));
        }

        private readonly SemaphoreSlim m_connecting = new(1, 1);
        private readonly ILogger m_logger;
        private Uri? m_url;
        private TransportChannelSettings? m_settings;
        private ChannelQuotas? m_quotas;
        private BufferManager? m_bufferManager;
        private UaSCUaBinaryClientChannel? m_channel;
        private bool m_disposed;
        private event ChannelTokenActivatedEventHandler? m_OnTokenActivated;
        private readonly IUaSCByteTransportFactory m_transportFactory;
        private readonly IBufferManagerFactory m_bufferManagerFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private long m_serverRetryAfterHintTicks;
    }
}
