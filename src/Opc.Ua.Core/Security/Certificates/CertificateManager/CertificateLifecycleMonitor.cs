/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
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
        /// A delegate that returns a snapshot of the current application
        /// certificates. It must hand back a collection this monitor owns and
        /// disposes: the timer callback runs while other threads add, replace
        /// and dispose entries, so enumerating the live list would throw and
        /// could read an entry that has just been released.
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

        /// <summary>
        /// Inspects an owned certificate snapshot and emits expiry notices only for the current monitor generation.
        /// </summary>
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

        /// <summary>
        /// Serializes notification bookkeeping with monitor reset and disposal.
        /// </summary>
        private readonly Lock m_lock = new();

        /// <summary>
        /// Invalidates callbacks that started before the latest reset or disposal.
        /// </summary>
        private long m_generation;

        /// <summary>
        /// Prevents further expiry notifications after the monitor is disposed.
        /// </summary>
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
            [ApplicationIdentity] string? thumbprint,
            DateTime notAfter);

        [LoggerMessage(EventId = CoreEventIds.CertificateLifecycleMonitor + 1, Level = LogLevel.Debug,
            Message = "Error checking certificate expiry.")]
        public static partial void CertLifecycleLog1(
            this ILogger logger,
            global::System.Exception? exception);
    }

}
