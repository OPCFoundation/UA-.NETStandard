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
            if (disposing)
            {
                UaSCUaBinaryClientChannel? channel = Interlocked.Exchange(ref m_channel, null);
                Utils.SilentDispose(channel);
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
            => m_settings?.Description ?? throw NotOpen();

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_settings?.Configuration ?? throw NotOpen();

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => m_quotas?.MessageContext ?? throw NotOpen();

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
            SaveSettings(url, settings);
            Interlocked.Exchange(ref m_channel, CreateChannel(m_telemetry));
            return OpenAsync(ct);
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            SaveSettings(connection.EndpointUrl, settings);
            Interlocked.Exchange(ref m_channel, CreateChannel(m_telemetry, connection));
            return OpenAsync(ct);
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(ITransportWaitingConnection? connection, CancellationToken ct)
        {
            m_logger.LogInformation("TransportChannel RECONNECT: Reconnecting to {Url}.", m_url);

            lock (m_lock)
            {
                // the new channel must be created first because WinSock will reuse sockets and this
                // can result in messages sent over the old socket arriving as messages on the new socket.
                // if this happens the new channel is shutdown because of a security violation.
                UaSCUaBinaryClientChannel? channel = Interlocked.Exchange(ref m_channel, null);

                try
                {
                    UaSCUaBinaryClientChannel newChannel = CreateChannel(m_telemetry, connection);

                    // reconnect.
                    Interlocked.Exchange(ref m_channel, newChannel);

                    // begin connect operation.
                    IAsyncResult result = newChannel.BeginConnect(
                        m_url,
                        OperationTimeout,
                        null,
                        null);
                    newChannel.EndConnect(result);
                }
                finally
                {
                    // close existing channel.
                    if (channel != null)
                    {
                        try
                        {
                            channel.Close(kChannelCloseDefault);
                        }
                        catch (Exception e)
                        {
                            // do nothing.
                            m_logger.LogTrace(e, "Ignoring exception while closing transport channel during Reconnect.");
                        }
                        finally
                        {
                            channel.Dispose();
                        }
                    }
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public Task CloseAsync(CancellationToken ct)
        {
            UaSCUaBinaryClientChannel? channel = Interlocked.Exchange(ref m_channel, null);
            if (channel != null)
            {
                return channel.CloseAsync(kChannelCloseDefault, ct);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct)
        {
            UaSCUaBinaryClientChannel? channel = m_channel;

            if (channel == null)
            {
                channel = CreateChannel(m_telemetry);
                UaSCUaBinaryClientChannel? currentChannel = Interlocked.CompareExchange(
                    ref m_channel,
                    channel,
                    null);
                if (currentChannel != null)
                {
                    Utils.SilentDispose(channel);
                    channel = currentChannel;
                }
            }

            IAsyncResult operation = channel.BeginSendRequest(request, OperationTimeout, null, null);
            return EndSendRequestAsync(operation, ct);
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginSendRequest call.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public Task<IServiceResponse> EndSendRequestAsync(IAsyncResult result, CancellationToken ct)
        {
            UaSCUaBinaryClientChannel channel =
                m_channel
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel has been closed.");

            return channel.EndSendRequestAsync(result, ct);
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
        /// Open the channel
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask OpenAsync(CancellationToken ct)
        {
            await Task.Factory.FromAsync(
                BeginOpen,
                EndOpen,
                null).ConfigureAwait(false);

            IAsyncResult BeginOpen(AsyncCallback callback, object? callbackData)
            {
                lock (m_lock)
                {
                    // create the channel.
                    UaSCUaBinaryClientChannel newChannel = CreateChannel(m_telemetry);
                    Interlocked.Exchange(ref m_channel, newChannel);

                    // begin connect operation.
                    return newChannel.BeginConnect(m_url, OperationTimeout, callback, callbackData);
                }
            }

            void EndOpen(IAsyncResult result)
            {
                ct.ThrowIfCancellationRequested();
                m_channel?.EndConnect(result);
            }
        }

        /// <summary>
        /// Opens the channel before sending the request.
        /// </summary>
        /// <param name="telemetry">Telemetry context</param>
        /// <param name="connection">A reverse connection, null otherwise.</param>
        /// <exception cref="ArgumentException"></exception>
        private UaSCUaBinaryClientChannel CreateChannel(
            ITelemetryContext telemetry,
            ITransportWaitingConnection? connection = null)
        {
            IMessageSocket? socket = null;
            if (connection != null)
            {
                socket = connection.Handle as IMessageSocket;
                if (socket == null)
                {
                    throw new ArgumentException("Connection Handle is not of type IMessageSocket.");
                }
            }

            // create the channel.
            var channel = new UaSCUaBinaryClientChannel(
                Guid.NewGuid().ToString(),
                m_bufferManager,
                m_messageSocketFactory,
                m_quotas,
                m_settings?.ClientCertificate,
                m_settings?.ClientCertificateChain,
                m_settings?.ServerCertificate,
                m_settings?.Description,
                telemetry);

            // use socket for reverse connections, ignore otherwise
            if (socket != null)
            {
                channel.Socket = socket;
                channel.Socket.ChangeSink(channel);
                channel.ReverseSocket = true;
            }

            // Register the token changed event handler with the internal channel
            channel.OnTokenActivated = (current, previous) => m_OnTokenActivated?.Invoke(
                this,
                current,
                previous);
            return channel;
        }

        private ServiceResultException NotOpen()
        {
            return ServiceResultException.Unexpected("{0} not open.", nameof(UaSCUaBinaryTransportChannel));
        }

        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private Uri? m_url;
        private TransportChannelSettings? m_settings;
        private ChannelQuotas? m_quotas;
        private BufferManager? m_bufferManager;
        private UaSCUaBinaryClientChannel? m_channel;
        private event ChannelTokenActivatedEventHandler? m_OnTokenActivated;
        private readonly IMessageSocketFactory m_messageSocketFactory;
        private readonly ITelemetryContext m_telemetry;
    }
}
