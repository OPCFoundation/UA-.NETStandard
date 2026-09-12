/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Periodically checks application certificates for upcoming expiry
    /// and emits <see cref="CertificateChangeKind.CertificateExpiring"/> events.
    /// </summary>
    internal sealed class CertificateLifecycleMonitor : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CertificateLifecycleMonitor"/> class.
        /// </summary>
        /// <param name="subject">
        /// The change subject used to emit certificate change events.
        /// </param>
        /// <param name="getCertificates">
        /// A delegate that returns an owned snapshot of the current application certificates.
        /// </param>
        /// <param name="expiryThreshold">
        /// The time span before expiry at which a warning is emitted.
        /// </param>
        /// <param name="checkInterval">
        /// How often to check for expiring certificates.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used for logging.
        /// </param>
        /// <param name="timeProvider">
        /// The <see cref="TimeProvider"/> used for timer scheduling and
        /// expiry comparison. Defaults to <see cref="TimeProvider.System"/>.
        /// </param>
        public CertificateLifecycleMonitor(
            CertificateChangeSubject subject,
            Func<CertificateEntryCollection> getCertificates,
            TimeSpan expiryThreshold,
            TimeSpan checkInterval,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_subject = subject ?? throw new ArgumentNullException(nameof(subject));
            m_getCertificates = getCertificates ?? throw new ArgumentNullException(nameof(getCertificates));
            m_expiryThreshold = expiryThreshold;
            m_logger = telemetry.CreateLogger<CertificateLifecycleMonitor>();
            m_timeProvider = timeProvider ?? TimeProvider.System;

            m_timer = m_timeProvider.CreateTimer(CheckExpiry, null, TimeSpan.Zero, checkInterval);
        }

        private void CheckExpiry(object? state)
        {
            long generation;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                generation = m_generation;
            }
            try
            {
                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                using CertificateEntryCollection certificates = m_getCertificates();
                foreach (CertificateEntry entry in certificates)
                {
                    bool notify;
                    lock (m_lock)
                    {
                        if (m_disposed || generation != m_generation)
                        {
                            return;
                        }
                        notify = now.Add(m_expiryThreshold) >= entry.NotAfter.ToUniversalTime() &&
                            m_alreadyNotified.Add(entry.Certificate.Thumbprint);
                    }
                    if (notify)
                    {
                        if (m_logger.IsEnabled(LogLevel.Warning))
                        {
                            m_logger.CertLifecycleLog0(
                                entry.Certificate.Thumbprint,
                                entry.NotAfter);
                        }

                        m_subject.Notify(new CertificateChangeEvent(
                            CertificateChangeKind.CertificateExpiring,
                            TrustListIdentifier.Peers,
                            entry.CertificateType,
                            entry.Certificate,
                            null,
                            null));
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.CertLifecycleLog1(ex);
            }
        }

        /// <summary>
        /// Resets notifications so already-notified certificates can be
        /// re-checked (e.g., after a certificate update).
        /// </summary>
        public void Reset()
        {
            lock (m_lock)
            {
                if (!m_disposed)
                {
                    m_generation++;
                    m_alreadyNotified.Clear();
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_generation++;
            }
            m_timer.Dispose();
        }

        private readonly CertificateChangeSubject m_subject;
        private readonly Func<CertificateEntryCollection> m_getCertificates;
        private readonly TimeSpan m_expiryThreshold;
        private readonly TimeProvider m_timeProvider;
        private readonly ITimer m_timer;
        private readonly ILogger m_logger;
        private readonly HashSet<string> m_alreadyNotified = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock m_lock = new();
        private long m_generation;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for CertificateLifecycleMonitor.
    /// </summary>
    internal static partial class CertificateLifecycleMonitorLog
    {
        [LoggerMessage(EventId = CoreEventIds.CertificateLifecycleMonitor + 0, Level = LogLevel.Warning,
            Message = "Certificate {Thumbprint} expires at {NotAfter}.")]
        public static partial void CertLifecycleLog0(
            this ILogger logger,
            string? thumbprint,
            DateTime notAfter);

        [LoggerMessage(EventId = CoreEventIds.CertificateLifecycleMonitor + 1, Level = LogLevel.Debug,
            Message = "Error checking certificate expiry.")]
        public static partial void CertLifecycleLog1(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
