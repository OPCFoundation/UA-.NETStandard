// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Bindings
{
    /// <summary>
    /// <see cref="IUaSCByteTransportFactory"/> decorator that wraps every
    /// transport produced by an inner factory in a
    /// <see cref="CapturingByteTransport"/>. Installed once at binding
    /// registration time; every channel created through the binding gets
    /// its transport transparently capture-enabled.
    /// </summary>
    public sealed class CapturingByteTransportFactory : IUaSCByteTransportFactory
    {
        private readonly IUaSCByteTransportFactory m_inner;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;

        /// <summary>
        /// Constructs a capturing factory.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="inner"/> or <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public CapturingByteTransportFactory(
            IUaSCByteTransportFactory inner,
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
        public string Implementation => m_inner.Implementation + "+pcap";

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            IUaSCByteTransport inner = m_inner.Create(bufferManager, receiveBufferSize, telemetry);
            return new CapturingByteTransport(inner, m_registry, m_loggerFactory);
        }
    }
}
