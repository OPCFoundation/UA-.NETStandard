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
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Bindings
{
    /// <summary>
    /// <see cref="ITransportChannelFactory"/> decorator that produces TCP
    /// transport channels whose underlying socket is wrapped by a
    /// <see cref="CapturingMessageSocketFactory"/>. Registering this
    /// binding via <c>AddPcap</c> (or
    /// <c>TransportBindings.Channels.SetBinding</c>) installs the capture
    /// hook for every <see cref="ITransportChannel"/> created through
    /// <see cref="ClientChannelManager"/>.
    /// </summary>
    /// <remarks>
    /// Wrapping is unconditional once the binding is installed; the
    /// runtime cost of "binding installed, no session recording" is
    /// effectively a single volatile read returning <c>null</c> on the
    /// channel's hot path. When the binding is NOT installed the original
    /// <see cref="TcpTransportChannelFactory"/> is invoked directly with
    /// zero indirection.
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

            var innerSocketFactory = new TcpMessageSocketFactory(telemetry);
            var capturingFactory = new CapturingMessageSocketFactory(
                innerSocketFactory,
                m_registry,
                m_loggerFactory ?? telemetry.LoggerFactory);
            var channel = new TcpTransportChannel(telemetry, capturingFactory);

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
