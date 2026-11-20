// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// Stub to avoid null references
    /// </summary>
    internal sealed class NullChannel : ITransportChannel, ISecureChannel
    {
        /// <inheritdoc/>
        public ChannelToken CurrentToken
           => throw Unexpected(nameof(CurrentToken));

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures
            => throw Unexpected(nameof(SupportedFeatures));

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => throw Unexpected(nameof(EndpointDescription));

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => throw Unexpected(nameof(EndpointConfiguration));

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => throw Unexpected(nameof(MessageContext));

        /// <inheritdoc/>
        public byte[] ChannelThumbprint
            => throw Unexpected(nameof(ChannelThumbprint));

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate
            => throw Unexpected(nameof(ClientChannelCertificate));

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate
            => throw Unexpected(nameof(ServerChannelCertificate));

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add => throw Unexpected(nameof(OnTokenActivated));
            remove => throw Unexpected(nameof(OnTokenActivated));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Do nothing
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct)
        {
            throw Unexpected(nameof(CloseAsync));
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            throw Unexpected(nameof(OpenAsync));
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            throw Unexpected(nameof(OpenAsync));
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct)
        {
            throw Unexpected(nameof(ReconnectAsync));
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request,
            CancellationToken ct)
        {
            throw Unexpected(nameof(SendRequestAsync));
        }

        /// <summary>
        /// Throw exception
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static ServiceResultException Unexpected(string name)
        {
#if DEBUG_CHECK
            System.Diagnostics.Debug.Fail(name + " called in unexpected state");
#endif
            return ServiceResultException.Unexpected($"{name} called in unexpected state");
        }
    }
}
