// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Capturing decorator for an <see cref="IPubSubTransportFactory"/>. Every
    /// transport it creates is wrapped in a <see cref="CapturingPubSubTransport"/>
    /// so an active capture session taps the traffic without the underlying
    /// UDP / MQTT transports knowing about capture.
    /// </summary>
    public sealed class CapturingPubSubTransportFactory : IPubSubTransportFactory
    {
        /// <summary>
        /// Initializes a new <see cref="CapturingPubSubTransportFactory"/>.
        /// </summary>
        /// <param name="inner">The wrapped transport factory.</param>
        /// <param name="registry">The shared capture registry.</param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        public CapturingPubSubTransportFactory(
            IPubSubTransportFactory inner,
            IPubSubCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(registry);
            m_inner = inner;
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_inner.TransportProfileUri;

        /// <inheritdoc/>
        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            IPubSubTransport inner = m_inner.Create(connection, telemetry, timeProvider);
            return new CapturingPubSubTransport(
                inner,
                m_registry,
                timeProvider,
                m_loggerFactory?.CreateLogger<CapturingPubSubTransport>());
        }

        private readonly IPubSubTransportFactory m_inner;
        private readonly IPubSubCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;
    }
}
