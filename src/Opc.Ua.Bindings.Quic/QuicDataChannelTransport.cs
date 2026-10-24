// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Direct and injectable native QUIC flow endpoint factory. It does not register an implicit UASC transport.
    /// </summary>
    public sealed class QuicDataChannelTransport
    {
        /// <summary>
        /// Creates a transport using an explicit backend, certificate policy and common context factory.
        /// The injected providers are borrowed; returned listeners/connections are caller-owned.
        /// </summary>
        public QuicDataChannelTransport(
            INativeQuicBackend backend,
            INativeQuicCredentialProvider credentialProvider,
            IDataChannelContextFactory contextFactory,
            QuicDataChannelOptions? options = null,
            TimeProvider? timeProvider = null,
            DataChannelApplicationBudget? applicationBudget = null)
        {
            m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
            m_credentials = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            m_contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            options ??= new QuicDataChannelOptions();
            m_options = options with
            {
                ApplicationBudget = options.ApplicationBudget ?? applicationBudget ?? new DataChannelApplicationBudget()
            };
            m_options.Validate();
            m_clock = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Connects with real mutual TLS and returns a native data context after HELLO, without opening any flow.
        /// </summary>
        public async ValueTask<QuicDataChannelConnection> ConnectAsync(
            Uri endpointUrl,
            CancellationToken cancellationToken = default)
        {
            string address = GetAddress(endpointUrl, false);
            DataChannelApplicationBudget.DataChannelCarrierReservation? reservation =
                QuicDataChannelConnection.ReserveNativeCapacity(m_options);
            NativeQuicConnection? connection = null;
            try
            {
                connection = await NativeQuicConnection.ConnectAsync(
                    m_backend, m_credentials, address, DataChannelProfiles.QuicAlpn, endpointUrl.DnsSafeHost,
                    m_options.Native, m_clock, cancellationToken).ConfigureAwait(false);
                QuicDataChannelConnection established = await QuicDataChannelConnection.EstablishReservedAsync(
                    connection, m_contextFactory, endpointUrl, m_options, m_clock, reservation, cancellationToken)
                    .ConfigureAwait(false);
                reservation = null;
                return established;
            }
            catch
            {
                if (connection != null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                reservation?.Dispose();
            }
        }

        /// <summary>
        /// Listens on an explicitly selected numeric opc.quic endpoint. Port zero requests an ephemeral port.
        /// No endpoint is advertised until this returns a real listening native backend.
        /// </summary>
        public async ValueTask<QuicDataChannelListener> ListenAsync(
            Uri endpointUrl,
            CancellationToken cancellationToken = default)
        {
            string address = GetAddress(endpointUrl, true);
            NativeQuicListener listener = await NativeQuicListener.ListenAsync(
                m_backend, m_credentials, address, DataChannelProfiles.QuicAlpn,
                m_options.Native, m_clock, cancellationToken).ConfigureAwait(false);
            return new QuicDataChannelListener(listener, m_contextFactory, m_options, m_clock);
        }

        /// <summary>
        /// Validates the native-only URL without accepting or silently translating a legacy/foreign transport.
        /// </summary>
        private static string GetAddress(Uri endpoint, bool listen)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "opc.quic" ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment) || endpoint.AbsolutePath is not ("" or "/") ||
                (listen && !IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out _)))
            {
                throw new ArgumentException("An explicit opc.quic authority without path or credentials is required.",
                    nameof(endpoint));
            }
            int port = endpoint.Port < 0 ? 4840 : endpoint.Port;
            if (!listen && port == 0)
            {
                throw new ArgumentException("A connector requires a nonzero UDP port.", nameof(endpoint));
            }
            string host = endpoint.DnsSafeHost;
            return host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";
        }

        private readonly INativeQuicBackend m_backend;
        private readonly INativeQuicCredentialProvider m_credentials;
        private readonly IDataChannelContextFactory m_contextFactory;
        private readonly QuicDataChannelOptions m_options;
        private readonly TimeProvider m_clock;
    }

    /// <summary>
    /// Registers opt-in native flow factories without registering Service transports or advertising endpoints.
    /// </summary>
    public static class QuicDataChannelBuilderExtensions
    {
        /// <summary>
        /// Adds the QUIC flow factory. Register INativeQuicBackend, INativeQuicCredentialProvider and
        /// IDataChannelContextFactory explicitly; the latter may optionally host model/control services.
        /// </summary>
        public static IOpcUaBuilder AddQuicDataChannels(
            this IOpcUaBuilder builder,
            QuicDataChannelOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            options ??= new QuicDataChannelOptions();
            options.Validate();
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<DataChannelApplicationBudget>();
            builder.Services.TryAddSingleton<QuicDataChannelTransport>();
            return builder;
        }

        /// <summary>
        /// Registers the reusable native certificate-manager bridge without selecting a flow or HTTP/3 protocol.
        /// </summary>
        public static IOpcUaBuilder AddNativeQuicCertificates(
            this IOpcUaBuilder builder,
            NativeQuicCertificateOptions options)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(options);
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<INativeQuicCredentialProvider, NativeQuicCertificateProvider>();
            return builder;
        }
    }
}
