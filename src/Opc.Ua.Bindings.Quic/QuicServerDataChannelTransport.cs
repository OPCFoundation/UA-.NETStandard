/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Bridges <see cref="StandardServer"/> data-channel Services to the
    /// QUIC transport instance that owns the request's SecureChannel.
    /// </summary>
    public sealed class QuicServerDataChannelTransport : IServerDataChannelTransport
    {
        /// <inheritdoc/>
        public bool TryGetManager(
            SecureChannelContext secureChannelContext,
            DataChannelServerCapabilities capabilities,
            ITelemetryContext telemetry,
            out DataChannelManager manager,
            out uint maxFrameSize,
            out bool isReliable)
        {
            if (secureChannelContext == null)
            {
                throw new ArgumentNullException(nameof(secureChannelContext));
            }

            if (!string.Equals(
                secureChannelContext.EndpointDescription?.TransportProfileUri,
                Profiles.UaQuicTransport,
                StringComparison.Ordinal))
            {
                manager = null!;
                maxFrameSize = 0;
                isReliable = true;
                return false;
            }

            if (!s_bindings.TryGetValue(secureChannelContext.SecureChannelId, out Binding? binding))
            {
                manager = null!;
                maxFrameSize = 0;
                isReliable = true;
                return false;
            }

            // Part 6 errata §7.6.1: the TLS server shall not accept
            // OpenDataChannel on a SecureChannel whose connection completed
            // without a TLS client certificate, because there is then
            // nothing to bind the TLS peer to the OPC UA peer and the
            // TransportSecured profile rests entirely on that binding. The
            // connection itself is allowed to complete so the Discovery
            // Services stay reachable on a SecurityPolicy None channel;
            // this is where the absence becomes fatal. Refusing rather than
            // falling back to a Service-only transport is deliberate - a
            // silent downgrade is exactly the failure this binding exists
            // to prevent.
            if (binding.Transport.PeerCertificate == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The opc.quic connection completed without a TLS client certificate, " +
                        "so the TLS peer cannot be bound to the OPC UA peer.");
            }

            DataChannelManager bound = binding.EnsureManager(() =>
            {
                var transport = new QuicDataChannelTransport(
                    binding.Transport,
                    binding.Transport.BufferManager,
                    telemetry)
                {
                    SecureChannelId = ParseChannelId(secureChannelContext.SecureChannelId),
                    MaxFrameBodySize = Math.Max(
                        0,
                        binding.Transport.ReceiveBufferSize - MessageHeaderSize)
                };

                var created = new DataChannelManager(
                    transport,
                    isServer: true,
                    telemetry,
                    capabilities.MaxDataChannels,
                    capabilities.MaxCreditPerChannel);

                transport.Manager = created;
                return (created, transport);
            });

            manager = bound;
            maxFrameSize = (uint)DataChannelFrameCodec.MaxPayload(
                DataChannelFramingMode.Quic,
                binding.Transport.ReceiveBufferSize,
                footerSize: 0,
                withDeadline: false);
            isReliable = true;
            return true;
        }

        /// <inheritdoc/>
        public ValueTask<ulong> AllocateServerStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            Binding binding = GetBinding(secureChannelContext);
            QuicDataChannelTransport transport = GetDataTransport(binding);
            return transport.OpenChannelStreamAsync(channelId, direction, isOpcUaServer: true, ct);
        }

        /// <inheritdoc/>
        public ValueTask BindClientStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            Binding binding = GetBinding(secureChannelContext);
            QuicDataChannelTransport transport = GetDataTransport(binding);

            // §7.4 requires the transportChannelId to be validated before the
            // channel is bound, and forbids echoing a value that was not
            // validated. Running this inline is what lets a refusal surface as
            // the Service result rather than being lost in a discarded task.
            transport.ValidateAndReserveChannel(
                channelId,
                streamId,
                direction,
                isOpcUaServer: true);

            // Only the wait for the client's stream to materialize is
            // deferred: a peer-initiated QUIC stream is observable only once
            // the peer writes to it, and a Client normally writes only after
            // it has the OpenDataChannel response, so awaiting it here would
            // deadlock the exchange.
            transport.BeginInboundBind(channelId, streamId);
            return default;
        }

        /// <inheritdoc/>
        public void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason)
        {
            if (secureChannelContext != null &&
                s_bindings.TryGetValue(secureChannelContext.SecureChannelId, out Binding? binding))
            {
                // The contract is to tear down the data channels, not the
                // connection: the SecureChannel, the Session and the Service
                // traffic on the same QUIC connection are unaffected by a
                // channel fault (§5.11, "a failed stream is not a failed
                // connection").
                binding.TryGetManager()?.AbortAll(reason);
            }
        }

        internal static void BindSecureChannel(
            string secureChannelId,
            QuicMultiplexedTransport transport)
        {
            s_bindings[secureChannelId] = new Binding(transport);
        }

        internal static void UnbindSecureChannel(
            string secureChannelId,
            QuicMultiplexedTransport transport)
        {
            if (s_bindings.TryGetValue(secureChannelId, out Binding? binding) &&
                ReferenceEquals(binding.Transport, transport))
            {
                s_bindings.TryRemove(secureChannelId, out _);

                // Part 6 errata §5.13: a lost transport faults every data
                // channel on it, from any state. No audit close event is
                // raised for opc.quic, so dropping the binding without this
                // would leave the peer's channels reported as Open forever
                // and their sources never told they had ended.
                binding.TryGetManager()?.AbortAll(StatusCodes.BadConnectionClosed);
            }
        }

        private static Binding GetBinding(SecureChannelContext secureChannelContext)
        {
            if (secureChannelContext == null)
            {
                throw new ArgumentNullException(nameof(secureChannelContext));
            }

            if (s_bindings.TryGetValue(secureChannelContext.SecureChannelId, out Binding? binding))
            {
                return binding;
            }

            throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
        }

        private static QuicDataChannelTransport GetDataTransport(Binding binding)
        {
            return binding.RequireDataTransport();
        }

        private static uint ParseChannelId(string secureChannelId)
        {
            int separator = secureChannelId.LastIndexOf('-');
            ReadOnlySpan<char> id = separator >= 0
                ? secureChannelId.AsSpan(separator + 1)
                : secureChannelId.AsSpan();
            return uint.TryParse(id, out uint parsed) ? parsed : 0;
        }

        private sealed class Binding(QuicMultiplexedTransport transport)
        {
            public QuicMultiplexedTransport Transport { get; } = transport;

            /// <summary>
            /// Creates the engine and its transport once, and returns the one
            /// already in place to every later caller.
            /// </summary>
            /// <param name="create">Builds the pair on first use.</param>
            public DataChannelManager EnsureManager(
                Func<(DataChannelManager Manager, QuicDataChannelTransport Transport)> create)
            {
                lock (m_syncRoot)
                {
                    if (m_manager == null)
                    {
                        (m_manager, m_dataTransport) = create();
                    }

                    return m_manager;
                }
            }

            /// <summary>
            /// The data channel transport for this SecureChannel, which only
            /// exists once a channel has been opened on it.
            /// </summary>
            public QuicDataChannelTransport RequireDataTransport()
            {
                lock (m_syncRoot)
                {
                    return m_dataTransport
                        ?? throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }

            /// <summary>
            /// The engine for this SecureChannel, or <c>null</c> when no
            /// channel has been opened on it.
            /// </summary>
            public DataChannelManager? TryGetManager()
            {
                lock (m_syncRoot)
                {
                    return m_manager;
                }
            }

            private readonly Lock m_syncRoot = new();
            private DataChannelManager? m_manager;
            private QuicDataChannelTransport? m_dataTransport;
        }

        private const int MessageHeaderSize = 12;

        private static readonly ConcurrentDictionary<string, Binding> s_bindings = new();
    }
}
