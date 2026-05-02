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

#nullable enable

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
        private readonly CertificateChangeSubject m_subject;
        private readonly Func<IReadOnlyList<CertificateEntry>> m_getCertificates;
        private readonly TimeSpan m_expiryThreshold;
        private readonly Timer m_timer;
        private readonly ILogger m_logger;
        private readonly HashSet<string> m_alreadyNotified = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateLifecycleMonitor"/> class.
        /// </summary>
        /// <param name="subject">
        /// The change subject used to emit certificate change events.
        /// </param>
        /// <param name="getCertificates">
        /// A delegate that returns the current application certificates.
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
        public CertificateLifecycleMonitor(
            CertificateChangeSubject subject,
            Func<IReadOnlyList<CertificateEntry>> getCertificates,
            TimeSpan expiryThreshold,
            TimeSpan checkInterval,
            ITelemetryContext telemetry)
        {
            m_subject = subject ?? throw new ArgumentNullException(nameof(subject));
            m_getCertificates = getCertificates ?? throw new ArgumentNullException(nameof(getCertificates));
            m_expiryThreshold = expiryThreshold;
            m_logger = telemetry.CreateLogger<CertificateLifecycleMonitor>();

            m_timer = new Timer(CheckExpiry, null, TimeSpan.Zero, checkInterval);
        }

        private void CheckExpiry(object? state)
        {
            try
            {
                foreach (CertificateEntry entry in m_getCertificates())
                {
                    if (entry.IsNearExpiry(m_expiryThreshold) &&
                        m_alreadyNotified.Add(entry.Certificate.Thumbprint))
                    {
                        m_logger.LogWarning(
                            "Certificate {Thumbprint} expires at {NotAfter}.",
                            entry.Certificate.Thumbprint,
                            entry.NotAfter);

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
                m_logger.LogDebug(ex, "Error checking certificate expiry.");
            }
        }

        /// <summary>
        /// Resets notifications so already-notified certificates can be
        /// re-checked (e.g., after a certificate update).
        /// </summary>
        public void Reset()
        {
            m_alreadyNotified.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_timer.Dispose();
        }
    }
}
