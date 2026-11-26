// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Receives the audit events a server reports while its Auditing flag is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>AuditEvents.Report*</c> helper builds the event and hands it to
    /// <see cref="IAuditEventServer.ReportAuditEvent"/>; the server forwards it to its sink. The
    /// default sink, <see cref="AddressSpaceAuditEventSink"/>, publishes the event to subscribed
    /// clients through the Server object, as OPC 10000-5 §6.4 requires.
    /// </para>
    /// <para>
    /// Register additional sinks (for example a SIEM forwarder) in the service container: they
    /// receive every event after it is published in the address space. To replace the address
    /// space publication, override <c>StandardServer.CreateAuditEventSink</c>.
    /// </para>
    /// <para>
    /// A sink runs on the thread that reports the event, often inside a service call, so it must
    /// return quickly and must not throw.
    /// </para>
    /// </remarks>
    public interface IAuditEventSink
    {
        /// <summary>
        /// Receives one audit event.
        /// </summary>
        /// <param name="context">The context the event was raised in.</param>
        /// <param name="e">The audit event. Do not change it.</param>
        void ReportAuditEvent(ISystemContext context, AuditEventState e);
    }

    /// <summary>
    /// The default <see cref="IAuditEventSink"/>: publishes the audit event in the address space
    /// of the server, so subscribed clients receive it.
    /// </summary>
    public sealed class AddressSpaceAuditEventSink : IAuditEventSink
    {
        /// <summary>
        /// Creates the sink for <paramref name="server"/>.
        /// </summary>
        /// <param name="server">The server that publishes the events.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
        public AddressSpaceAuditEventSink(ServerInternalData server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <inheritdoc/>
        public void ReportAuditEvent(ISystemContext context, AuditEventState e)
        {
            m_server.PublishAuditEvent(context, e);
        }

        private readonly ServerInternalData m_server;
    }

    /// <summary>
    /// Forwards each audit event to several sinks in order. A sink that throws is logged and
    /// does not stop the sinks after it.
    /// </summary>
    public sealed class CompositeAuditEventSink : IAuditEventSink
    {
        /// <summary>
        /// Creates the composite sink.
        /// </summary>
        /// <param name="sinks">The sinks, in the order they receive each event.</param>
        /// <param name="telemetry">The telemetry used to log a failing sink.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sinks"/> is <c>null</c>.</exception>
        public CompositeAuditEventSink(IEnumerable<IAuditEventSink> sinks, ITelemetryContext? telemetry = null)
        {
            if (sinks == null)
            {
                throw new ArgumentNullException(nameof(sinks));
            }
            var list = new List<IAuditEventSink>();
            foreach (IAuditEventSink sink in sinks)
            {
                if (sink != null)
                {
                    list.Add(sink);
                }
            }
            m_sinks = list.ToArray();
            m_logger = telemetry?.CreateLogger<CompositeAuditEventSink>();
        }

        /// <summary>
        /// Returns <paramref name="first"/> when <paramref name="additional"/> is empty; otherwise a
        /// composite that forwards to <paramref name="first"/> and then to each additional sink.
        /// </summary>
        /// <param name="first">The sink that receives each event first (normally the address space).</param>
        /// <param name="additional">Further sinks, for example those registered in a service container.</param>
        /// <param name="telemetry">The telemetry used to log a failing sink.</param>
        /// <exception cref="ArgumentNullException"><paramref name="first"/> is <c>null</c>.</exception>
        public static IAuditEventSink Combine(
            IAuditEventSink first,
            IEnumerable<IAuditEventSink>? additional,
            ITelemetryContext? telemetry = null)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }
            var sinks = new List<IAuditEventSink> { first };
            if (additional != null)
            {
                sinks.AddRange(additional);
            }
            return sinks.Count == 1 ? first : new CompositeAuditEventSink(sinks, telemetry);
        }

        /// <summary>
        /// The sinks, in the order they receive each event.
        /// </summary>
        public IReadOnlyList<IAuditEventSink> Sinks => m_sinks;

        /// <inheritdoc/>
        public void ReportAuditEvent(ISystemContext context, AuditEventState e)
        {
            foreach (IAuditEventSink sink in m_sinks)
            {
                try
                {
                    sink.ReportAuditEvent(context, e);
                }
                catch (Exception ex)
                {
                    m_logger?.LogError(ex, "Audit event sink {Sink} failed.", sink.GetType().Name);
                }
            }
        }

        private readonly IAuditEventSink[] m_sinks;
        private readonly ILogger? m_logger;
    }
}
