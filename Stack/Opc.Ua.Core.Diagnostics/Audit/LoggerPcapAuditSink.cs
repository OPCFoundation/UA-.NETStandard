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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

            m_logger.LogWarning(
                "Pcap audit event {Kind} session={SessionId} resource={ResourcePath} endpoint={RemoteEndpoint}",
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
}
