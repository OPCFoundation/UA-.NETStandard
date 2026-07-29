#if NET9_0_OR_GREATER
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

            lock (binding.SyncRoot)
            {
                if (binding.Manager == null)
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

                    binding.DataTransport = transport;
                    binding.Manager = new DataChannelManager(
                        transport,
                        isServer: true,
                        telemetry,
                        capabilities.MaxDataChannels,
                        capabilities.MaxCreditPerChannel);
                    transport.Manager = binding.Manager;
                }

                manager = binding.Manager;
                maxFrameSize = (uint)DataChannelFrameCodec.MaxPayload(
                    DataChannelFramingMode.Quic,
                    binding.Transport.ReceiveBufferSize,
                    footerSize: 0,
                    withDeadline: false);
                isReliable = true;
                return true;
            }
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
            _ = transport.BindChannelAsync(
                channelId,
                streamId,
                direction,
                isOpcUaServer: true,
                ct).AsTask();
            return default;
        }

        /// <inheritdoc/>
        public void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason)
        {
            if (secureChannelContext != null &&
                s_bindings.TryGetValue(secureChannelContext.SecureChannelId, out Binding? binding))
            {
                binding.Transport.Close();
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
            lock (binding.SyncRoot)
            {
                if (binding.DataTransport == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }

                return binding.DataTransport;
            }
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

            public object SyncRoot { get; } = new();

            public QuicDataChannelTransport? DataTransport { get; set; }

            public DataChannelManager? Manager { get; set; }
        }

        private const int MessageHeaderSize = 12;

        private static readonly ConcurrentDictionary<string, Binding> s_bindings = new();
    }
}

#endif
