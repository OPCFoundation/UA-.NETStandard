// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Establishes the managed capsule-based WebTransport UASC baseline over public .NET QUIC APIs.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class ManagedWebTransportUascTransport
    {
        /// <summary>
        /// Establishes a managed HTTP/3 capsule session and its stream-zero UACP/UASC byte transport.
        /// </summary>
        /// <remarks>
        /// TLS options must come from the host's certificate-store policy. TLS authenticates the server;
        /// the ordinary UASC channel separately authenticates applications and enforces SignAndEncrypt.
        /// </remarks>
        public async ValueTask<ManagedWebTransportUascConnection> ConnectAsync(
            Uri endpoint,
            SslClientAuthenticationOptions tls,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(tls);
            Http3CapsuleSession session = await ManagedHttp3WebTransportClient.ConnectAsync(
                endpoint,
                tls,
                origin: null,
                MaximumHeaderBytes,
                MaximumFramePayloadBytes,
                MaximumCapsulePayloadBytes,
                EstablishmentTimeout,
                cancellationToken).ConfigureAwait(false);
            try
            {
                WebTransportCapsuleByteStream stream = await WebTransportCapsuleByteStream.CreateAsync(
                    session,
                    MaximumCapsulePayloadBytes,
                    InitialReceiveWindowBytes,
                    cancellationToken).ConfigureAwait(false);
                return new ManagedWebTransportUascConnection(session, stream, endpoint, isInitiator: true);
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Registers the managed client transport without creating TLS credentials or advertising endpoints.
        /// </summary>
        private const int MaximumHeaderBytes = 65536;
        private const int MaximumFramePayloadBytes = 1048576;
        private const int MaximumCapsulePayloadBytes = 1048576;
        private const int InitialReceiveWindowBytes = 1048576;
        private static readonly TimeSpan EstablishmentTimeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Registers the managed WebTransport UASC client binding.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public static class ManagedWebTransportUascBuilderExtensions
    {
        /// <summary>
        /// Adds the managed client transport without creating TLS credentials or advertising endpoints.
        /// </summary>
        public static IOpcUaBuilder AddManagedWebTransportUasc(this IOpcUaBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton<ManagedWebTransportUascTransport>();
            builder.Services.TryAddSingleton<ManagedWebTransportUascServer>();
            return builder;
        }
    }

    /// <summary>
    /// Owns one managed HTTP/3 capsule session and its UACP/UASC stream-zero mapping.
    /// </summary>
#if NET8_0
    [RequiresPreviewFeatures("System.Net.Quic is a preview API on .NET 8.")]
#endif
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    public sealed class ManagedWebTransportUascConnection :
        IUaSCByteTransportFactory, ITransportChannelFactory, IUaSCTransportProfile, IAsyncDisposable
    {
        internal ManagedWebTransportUascConnection(
            Http3CapsuleSession session,
            WebTransportCapsuleByteStream stream,
            Uri endpoint,
            bool isInitiator)
        {
            m_session = session;
            m_stream = stream;
            EndpointUrl = endpoint;
            m_isInitiator = isInitiator;
        }

        /// <summary>
        /// Gets the exact OPC UA endpoint used for the UACP HEL and channel profile checks.
        /// </summary>
        public Uri EndpointUrl { get; }

        /// <inheritdoc/>
        public string Implementation => "Managed-WebTransport-UASC";

        /// <inheritdoc/>
        public string UriScheme => "https";

        /// <inheritdoc/>
        public string TransportProfileUri => DataChannelProfiles.UascWebTransport;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!m_isInitiator)
            {
                throw new InvalidOperationException("An accepted WebTransport baseline cannot initiate a channel.");
            }
            return new BaselineChannel(this, telemetry);
        }

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(bufferManager);
            ArgumentNullException.ThrowIfNull(telemetry);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (receiveBufferSize <= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
            if (m_isInitiator)
            {
                return new UascStreamByteTransportFactory(
                    ClaimStreamAsync, Implementation, TransportProfileUri)
                    .Create(bufferManager, receiveBufferSize, telemetry);
            }
            return new UascStreamByteTransport(
                ClaimStream(EndpointUrl, cancellationToken: default),
                bufferManager,
                receiveBufferSize,
                Implementation);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await m_stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private ValueTask<Stream> ClaimStreamAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Stream>(ClaimStream(endpoint, cancellationToken));
        }

        private WebTransportCapsuleByteStream ClaimStream(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!EndpointUrl.Equals(endpoint) || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0)
            {
                throw new ServiceResultException(StatusCodes.BadTcpEndpointUrlInvalid);
            }
            if (Interlocked.Exchange(ref m_streamClaimed, 1) != 0)
            {
                throw new InvalidOperationException("Each WebTransport UASC baseline can be claimed only once.");
            }
            return m_stream;
        }

        private sealed class BaselineChannel(
            ManagedWebTransportUascConnection owner,
            ITelemetryContext telemetry)
            : UaSCUaBinaryTransportChannel(owner, telemetry)
        {
            protected override void OnSettingsSaved(TransportChannelSettings settings, ChannelQuotas quotas)
            {
                if (settings.Description?.TransportProfileUri != owner.TransportProfileUri ||
                    settings.Description.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    !Uri.TryCreate(settings.Description.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    !owner.EndpointUrl.Equals(endpoint))
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed,
                        "The managed WebTransport baseline requires its exact endpoint, profile and SignAndEncrypt UASC.");
                }
            }
        }

        private readonly Http3CapsuleSession m_session;
        private readonly WebTransportCapsuleByteStream m_stream;
        private readonly bool m_isInitiator;
        private int m_streamClaimed;
        private int m_disposed;
    }
}
#endif
