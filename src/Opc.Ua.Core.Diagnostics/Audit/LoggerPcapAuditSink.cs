// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

namespace Opc.Ua.Pcap.Audit
{
    /// <summary>
    /// Default audit sink that writes Pcap audit events to the configured logger.
    /// </summary>
    public sealed class LoggerPcapAuditSink : IPcapAuditSink
    {
        private static readonly TimeSpan s_frameCapturedInterval = TimeSpan.FromMinutes(1);

        private static readonly ConcurrentDictionary<string, DateTimeOffset> s_lastFrameCapturedAudit = new(
            StringComparer.OrdinalIgnoreCase);

        private static readonly Lock s_frameCapturedRateLimitLock = new();

        private readonly ILogger<LoggerPcapAuditSink> m_logger;

        /// <summary>
        /// Constructs a logger-backed Pcap audit sink.
        /// </summary>
        public LoggerPcapAuditSink(ILogger<LoggerPcapAuditSink> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public ValueTask OnEventAsync(PcapAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(auditEvent);
            cancellationToken.ThrowIfCancellationRequested();

            if (!ShouldLog(auditEvent))
            {
                return ValueTask.CompletedTask;
            }

            m_logger.PcapAuditEvent(
                auditEvent.Kind,
                auditEvent.SessionId,
                auditEvent.ResourcePath,
                auditEvent.RemoteEndpoint);
            return ValueTask.CompletedTask;
        }

        private static bool ShouldLog(PcapAuditEvent auditEvent)
        {
            if (auditEvent.Kind != PcapAuditEventKind.FrameCaptured)
            {
                return true;
            }

            string key = auditEvent.SessionId ?? string.Empty;
            lock (s_frameCapturedRateLimitLock)
            {
                if (s_lastFrameCapturedAudit.TryGetValue(key, out DateTimeOffset lastAudit) &&
                    auditEvent.Timestamp - lastAudit < s_frameCapturedInterval)
                {
                    return false;
                }

                s_lastFrameCapturedAudit[key] = auditEvent.Timestamp;
                return true;
            }

        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="LoggerPcapAuditSink"/>.
    /// </summary>
    internal static partial class LoggerPcapAuditSinkLog
    {
        [LoggerMessage(EventId = CoreDiagnosticsEventIds.LoggerPcapAuditSink + 0, Level = LogLevel.Warning,
            Message = "Pcap audit event {Kind} session={SessionId} resource={ResourcePath} endpoint={RemoteEndpoint}")]
        public static partial void PcapAuditEvent(
            this ILogger logger,
            PcapAuditEventKind kind,
            string? sessionId,
            [FilePath] string? resourcePath,
            [NetworkAddress] string? remoteEndpoint);
    }

}
