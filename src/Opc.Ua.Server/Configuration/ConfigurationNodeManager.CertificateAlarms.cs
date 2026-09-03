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
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Certificate-group alarm surface of <see cref="ConfigurationNodeManager"/>
    /// (OPC 10000-12 §7.8.3): the <see cref="IConfigurationNodeManager"/> start/stop
    /// members and the registration of the per-group <c>CertificateExpired</c> and
    /// <c>TrustListOutOfDate</c> alarm subtrees in this manager's address space. The
    /// evaluation logic lives in <see cref="CertificateAlarmScheduler"/>; code in this
    /// file reads <c>m_certificateGroups</c> but never mutates it.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <summary>
        /// Gets the scheduler that refreshes and evaluates the certificate-group
        /// alarms. Exposed for testing.
        /// </summary>
        internal CertificateAlarmScheduler AlarmScheduler => m_alarmScheduler;

        /// <inheritdoc/>
        public void StartAlarmMonitoring(TimeSpan interval)
        {
            m_alarmScheduler.Start(SystemContext, interval);
        }

        /// <inheritdoc/>
        public void StopAlarmMonitoring()
        {
            m_alarmScheduler.Stop();
        }

        /// <summary>
        /// Creates the optional per-group <c>CertificateExpired</c> and
        /// <c>TrustListOutOfDate</c> alarm instances (OPC 10000-12 §7.8.3),
        /// registers them with the node manager and as event sources, and
        /// initializes their condition state without emitting any event.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="externalReferences">
        /// References from standard certificate-group nodes owned by another node manager.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async ValueTask CreateCertificateAlarmsAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                CertificateGroupState? node = certGroup.Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    m_alarmScheduler.CreateMonitor(context, certGroup);

                    // Register the new alarm subtrees and wire them as event
                    // sources so their transition events reach subscriptions
                    // and ConditionRefresh.
                    if (node.CertificateExpired != null)
                    {
                        await AddPredefinedNodeAsync(
                                context,
                                node.CertificateExpired,
                                externalReferences,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await AddRootNotifierAsync(node.CertificateExpired, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (node.TrustListOutOfDate != null)
                    {
                        await AddPredefinedNodeAsync(
                                context,
                                node.TrustListOutOfDate,
                                externalReferences,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await AddRootNotifierAsync(node.TrustListOutOfDate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToCreateCertificateAlarms(ex, certGroup.BrowseName);
                }
            }
        }
    }
}
