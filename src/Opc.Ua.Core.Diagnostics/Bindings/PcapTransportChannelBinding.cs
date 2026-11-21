// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// <see cref="ITransportChannelFactory"/> decorator that produces TCP
    /// transport channels whose underlying byte transport is wrapped by a
    /// <see cref="CapturingByteTransportFactory"/>. Registering this
    /// binding via <c>AddPcap</c> (or
    /// <c>PcapBindings.Install(ITransportBindingRegistry)</c>) installs
    /// the capture hook for every <see cref="ITransportChannel"/> created
    /// through <c>ClientChannelManager</c>.
    /// </summary>
    /// <remarks>
    /// Wrapping is unconditional once the binding is installed; the
    /// runtime cost of "binding installed, no session recording" is
    /// effectively a single volatile read returning <c>null</c> on the
    /// channel's hot path. When the binding is NOT installed the original
    /// <see cref="TcpTransportChannel"/> is invoked directly with zero
    /// indirection.
    /// </remarks>
    public sealed class PcapTransportChannelBinding : ITransportChannelFactory
    {
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;

        /// <summary>
        /// Constructs a Pcap channel binding bound to the supplied
        /// registry.
        /// </summary>
        public PcapTransportChannelBinding(
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcTcp;

        /// <inheritdoc/>
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            var innerFactory = new TcpByteTransportFactory(telemetry);
            var capturingFactory = new CapturingByteTransportFactory(
                innerFactory,
                m_registry,
                m_loggerFactory ?? telemetry.LoggerFactory);
            var channel = new UaSCUaBinaryTransportChannel(capturingFactory, telemetry);

            // Forward token-activated events to the active observer so an
            // offline decoder receives the derived key material alongside
            // the wire bytes. Subscribing here once per channel is
            // cheaper than subscribing at every send/receive site.
            ((ISecureChannel)channel).OnTokenActivated += OnChannelTokenActivated;
            return channel;
        }

        private void OnChannelTokenActivated(
            ITransportChannel channel,
            ChannelToken? currentToken,
            ChannelToken? previousToken)
        {
            if (currentToken is null)
            {
                return;
            }
            IFrameCaptureSink? observer = m_registry.CurrentObserver;
            if (observer is null)
            {
                return;
            }
            try
            {
                observer.OnTokenActivated(currentToken.ChannelId, currentToken, previousToken);
            }
            catch
            {
                // best-effort observer; never break the channel handshake.
            }
        }
    }
}
