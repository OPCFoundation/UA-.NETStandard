/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel for the ITransportChannel interface.
    /// Implements the UA-SC security and UA Binary encoding.
    /// The socket layer requires a IMessageSocketFactory implementation.
    /// </summary>
    public class UaSCUaBinaryTransportChannel : ITransportChannel, ISecureChannel,
        IMessageSocketChannel
    {
        private const int kChannelCloseDefault = 1_000;

        /// <summary>
        /// Create a transport channel from a message socket factory.
        /// </summary>
        /// <param name="messageSocketFactory">The message socket factory.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        public UaSCUaBinaryTransportChannel(
            IMessageSocketFactory messageSocketFactory,
            ITelemetryContext telemetry)
        {
            m_messageSocketFactory = messageSocketFactory;
            m_telemetry = telemetry;
            m_logger = m_telemetry.CreateLogger<UaSCUaBinaryTransportChannel>();
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
            }
        }

        /// <summary>
        /// Returns the channel's underlying message socket if connected / available.
        /// </summary>
        public IMessageSocket? Socket => m_channel?.Socket;

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add => m_OnTokenActivated += value;
            remove => m_OnTokenActivated -= value;
        }

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures =>
            Socket?.MessageSocketFeatures ?? TransportChannelFeatures.None;

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
        public ChannelToken? CurrentToken => m_channel?.CurrentToken;

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
                    Utils.SilentDispose(previousChannel);
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
                }
            }
            finally
            {
                m_connecting.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct)
        {
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
                channel = m_channel;
                if (channel == null)
                {
                    // Channel was closed
                    throw BadNotConnected();
                }
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

            OperationTimeout = settings.Configuration.OperationTimeout;
            if (OperationTimeout <= 0)
            {
                throw new ArgumentException("Timeout must be greater than 0");
            }

            // initialize the quotas.
            EndpointConfiguration configuration = m_settings.Configuration;
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry)
            {
                MaxArrayLength = configuration.MaxArrayLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxMessageSize = TcpMessageLimits.AlignRoundMaxMessageSize(
                        configuration.MaxMessageSize),
                MaxStringLength = configuration.MaxStringLength,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = m_settings.NamespaceUris,
                ServerUris = new StringTable(),
                Factory = m_settings.Factory
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
                "Client",
                settings.Configuration.MaxBufferSize,
                m_telemetry);
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

            IMessageSocket? socket = null;
            if (connection != null)
            {
                socket = connection.Handle as IMessageSocket;
                if (socket == null)
                {
                    throw ServiceResultException.Unexpected(
                        "Waiting Connection Handle is not of type IMessageSocket.");
                }
            }

            // create the channel.
            var channel = new UaSCUaBinaryClientChannel(
                Guid.NewGuid().ToString(),
                m_bufferManager,
                m_messageSocketFactory,
                m_quotas,
                m_settings.ClientCertificate,
                m_settings.ClientCertificateChain,
                m_settings.ServerCertificate,
                m_settings.Description,
                telemetry);

            // use socket for reverse connections, ignore otherwise
            if (socket != null)
            {
                channel.Socket = socket;
                channel.Socket.ChangeSink(channel);
                channel.ReverseSocket = true;
            }

            // Register the token changed event handler with the internal channel
            channel.OnTokenActivated =
                (current, previous) => m_OnTokenActivated?.Invoke(
                    this,
                    current,
                    previous);
            return channel;
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
        private readonly IMessageSocketFactory m_messageSocketFactory;
        private readonly ITelemetryContext m_telemetry;
    }
}
