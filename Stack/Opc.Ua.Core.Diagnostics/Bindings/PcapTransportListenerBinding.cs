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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// <see cref="ITransportListenerFactory"/> decorator that produces
    /// <c>opc.tcp</c> transport listeners whose accepted server channels have
    /// their underlying byte transport wrapped by a
    /// <see cref="CapturingByteTransport"/>. This is the server-side
    /// counterpart to <see cref="PcapTransportChannelBinding"/>: installing it
    /// via <c>AddPcap</c> (or
    /// <c>PcapBindings.InstallServer(ITransportBindingRegistry)</c>) makes
    /// every inbound client→server channel capture-aware.
    /// </summary>
    /// <remarks>
    /// The decorator inherits <see cref="TcpServiceHost.CreateServiceHostAsync"/>
    /// so the server's service-host wiring is unchanged; it overrides only
    /// <see cref="Create"/> to wrap the inner factory's listener. Recording is
    /// gated by the shared <see cref="IChannelCaptureRegistry"/>: when no
    /// session is recording the accepted transport's hot path is a single
    /// volatile read returning <c>null</c>.
    /// </remarks>
    public sealed class PcapTransportListenerBinding : TcpServiceHost
    {
        private readonly ITransportListenerFactory m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;

        /// <summary>
        /// Constructs a Pcap listener binding that decorates the supplied
        /// inner <see cref="ITransportListenerFactory"/> and shares the
        /// supplied <see cref="IChannelCaptureRegistry"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="inner"/> or <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public PcapTransportListenerBinding(
            ITransportListenerFactory inner,
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);
            m_inner = inner;
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public override string UriScheme => m_inner.UriScheme;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            ITransportListener inner = m_inner.Create(telemetry);
            return new CapturingTransportListener(
                inner,
                m_registry,
                m_loggerFactory ?? telemetry.LoggerFactory);
        }
    }

    /// <summary>
    /// <see cref="ITransportListener"/> decorator that forwards every member
    /// to an inner listener but augments <see cref="OpenAsync"/> to install
    /// the capture seam
    /// (<see cref="TransportListenerSettings.AcceptedTransportDecorator"/> and
    /// <see cref="TransportListenerSettings.OnAcceptedChannel"/>) so accepted
    /// server channels are wrapped by a <see cref="CapturingByteTransport"/>
    /// and their token activations are forwarded to the active observer.
    /// </summary>
    internal sealed class CapturingTransportListener : ITransportListener
    {
        private readonly ITransportListener m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;

        public CapturingTransportListener(
            ITransportListener inner,
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);
            m_inner = inner;
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public string ListenerId => m_inner.ListenerId;

        /// <inheritdoc/>
        public string UriScheme => m_inner.UriScheme;

        /// <inheritdoc/>
        public event ConnectionWaitingHandlerAsync ConnectionWaiting
        {
            add => m_inner.ConnectionWaiting += value;
            remove => m_inner.ConnectionWaiting -= value;
        }

        /// <inheritdoc/>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged
        {
            add => m_inner.ConnectionStatusChanged += value;
            remove => m_inner.ConnectionStatusChanged -= value;
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.AcceptedTransportDecorator = WrapTransport;
            settings.OnAcceptedChannel = SubscribeChannel;
            return m_inner.OpenAsync(baseAddress, settings, callback, ct);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
            => m_inner.CloseAsync(ct);

        /// <inheritdoc/>
        public void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates)
            => m_inner.CertificateUpdate(validator, serverCertificates);

        /// <inheritdoc/>
        public void CreateReverseConnection(Uri url, int timeout)
            => m_inner.CreateReverseConnection(url, timeout);

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
            => m_inner.UpdateChannelLastActiveTime(globalChannelId);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_inner.DisposeAsync();

        private CapturingByteTransport WrapTransport(IUaSCByteTransport inner)
            => new(inner, m_registry, m_loggerFactory);

        private void SubscribeChannel(TcpListenerChannel channel)
        {
            channel.OnTokenActivated += OnChannelTokenActivated;
        }

        private void OnChannelTokenActivated(
            TcpListenerChannel channel,
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
