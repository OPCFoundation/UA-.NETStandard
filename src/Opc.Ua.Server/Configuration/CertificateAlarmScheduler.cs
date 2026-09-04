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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Drives the per-group <c>CertificateExpired</c> and
    /// <c>TrustListOutOfDate</c> alarms of the ServerConfiguration object
    /// (OPC 10000-12 §7.8.3): publishes the current certificate/TrustList
    /// state onto the alarm inputs of every registered
    /// <see cref="CertificateGroupAlarmMonitor"/> and evaluates them, both on
    /// demand (address-space creation, <c>ApplyChanges</c>) and periodically
    /// from a timer once monitoring is started. The owning node manager
    /// instantiates and registers the alarm nodes and hands the finished
    /// monitor to <see cref="Add"/>.
    /// </summary>
    /// <remarks>
    /// Refresh and evaluation are serialized on one lock so the node
    /// mutations and event reporting driven from the timer, from commits
    /// and from startup/shutdown never overlap. The lock only guards
    /// synchronous work, so it never spans an await. <see cref="Dispose"/>
    /// is terminal: a later <see cref="Start"/> does nothing.
    /// </remarks>
    internal sealed class CertificateAlarmScheduler : IDisposable
    {
        /// <summary>
        /// Creates a scheduler.
        /// </summary>
        /// <param name="timeProvider">The time provider for the periodic timer.</param>
        /// <param name="logger">The logger.</param>
        public CertificateAlarmScheduler(TimeProvider timeProvider, ILogger logger)
        {
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a value indicating whether alarm monitoring is currently
        /// active, i.e. transition events may be reported.
        /// </summary>
        public bool IsActive => m_active;

        /// <summary>
        /// Gets the alarm monitors registered for the server's certificate
        /// groups.
        /// </summary>
        public IReadOnlyList<CertificateGroupAlarmMonitor> Monitors
        {
            get
            {
                lock (m_lock)
                {
                    return m_monitors.ConvertAll(entry => entry.Monitor);
                }
            }
        }

        /// <summary>
        /// Registers a monitor whose alarm nodes are already part of the
        /// address space, so it takes part in every refresh and evaluation
        /// from now on.
        /// </summary>
        /// <param name="monitor">The monitor evaluating the group's alarms.</param>
        /// <param name="group">The certificate group the monitor observes.</param>
        public void Add(CertificateGroupAlarmMonitor monitor, ServerCertificateGroup group)
        {
            if (monitor == null)
            {
                throw new ArgumentNullException(nameof(monitor));
            }

            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            lock (m_lock)
            {
                m_monitors.Add(new MonitorEntry(monitor, group));
            }
        }

        /// <summary>
        /// Starts periodic evaluation: performs an immediate evaluation with
        /// events enabled and then arms a timer that re-evaluates every
        /// <paramref name="interval"/>. Calling it while running, or after
        /// <see cref="Dispose"/>, is a no-op.
        /// </summary>
        /// <param name="context">The system context used for evaluation.</param>
        /// <param name="interval">The evaluation interval.</param>
        public void Start(ISystemContext context, TimeSpan interval)
        {
            lock (m_lock)
            {
                if (m_disposed || m_timer != null)
                {
                    return;
                }

                // The subscription/event infrastructure is ready once this is
                // called (see StandardServer.OnServerStarted), so transition
                // events may now be reported. Clear any prior stopped state so a
                // restart after Stop resumes evaluation.
                m_stopped = false;
                m_active = true;
            }

            // Perform an immediate evaluation so an already-expired certificate
            // or a stale TrustList is signalled without waiting a full interval.
            // This is done outside the lock (UpdateAndEvaluate takes it
            // itself) because System.Threading.Lock is not reentrant.
            try
            {
                UpdateAndEvaluate(context, emitEvents: true);
            }
            catch (Exception ex)
            {
                m_logger.InitialCertificateAlarmEvaluationFailed(ex);
            }

            lock (m_lock)
            {
                // A concurrent Stop may have run during the initial
                // evaluation; do not arm the periodic timer in that case.
                if (m_stopped || m_timer != null)
                {
                    return;
                }

                m_timer = m_timeProvider.CreateTimer(
                    _ =>
                    {
                        try
                        {
                            UpdateAndEvaluate(context, emitEvents: true);
                        }
                        catch (Exception ex)
                        {
                            m_logger.AlarmEvaluationTickFailed(ex);
                        }
                    },
                    null,
                    interval,
                    interval);
            }
        }

        /// <summary>
        /// Stops periodic evaluation and suppresses any evaluation still
        /// queued behind the lock, so nodes that may be getting torn down are
        /// no longer mutated. <see cref="Start"/> may be called again.
        /// </summary>
        public void Stop()
        {
            // Prevent further evaluations, then serialize with any in-flight one:
            // setting the stopped flag under the evaluation lock waits for a
            // running evaluation to finish and guarantees any evaluation still
            // queued behind the lock returns without mutating nodes that may be
            // getting torn down. The timer is disposed outside the lock so its
            // disposal can never deadlock against a callback that is blocked
            // waiting for the same lock.
            ITimer? timer;
            lock (m_lock)
            {
                m_active = false;
                m_stopped = true;
                timer = m_timer;
                m_timer = null;
            }

            timer?.Dispose();
        }

        /// <summary>
        /// Refreshes the alarm inputs from the current certificate/TrustList
        /// state and then evaluates every certificate-group alarm, driving the
        /// standard active/inactive state transitions per OPC 10000-12 §7.8.3.
        /// Does nothing once <see cref="Stop"/> has been called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="emitEvents">Whether transition events may be reported.</param>
        public void UpdateAndEvaluate(ISystemContext context, bool emitEvents)
        {
            // Serialize the entire refresh + evaluation path so the NodeState
            // mutations and event reporting driven from the periodic timer,
            // ApplyChanges commits, startup and shutdown never overlap. The lock
            // only ever guards fully synchronous work (RefreshInputs +
            // Evaluate perform no awaits), so it never spans an
            // await and never introduces sync-over-async.
            lock (m_lock)
            {
                // Once monitoring has been stopped (shutdown/dispose) the
                // address-space nodes may be getting torn down: a timer tick
                // that was already in flight when Stop ran blocks here until
                // Stop releases the lock, then observes the stopped flag and
                // returns without mutating any disposed nodes.
                if (m_stopped)
                {
                    return;
                }

                RefreshInputs(context);

                foreach (MonitorEntry entry in m_monitors)
                {
                    entry.Monitor.Evaluate(context, emitEvents);
                }
            }
        }

        /// <summary>
        /// Stops monitoring for good; a later <see cref="Start"/> is ignored.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_disposed = true;
            }

            Stop();
        }

        /// <summary>
        /// Publishes the current certificate and TrustList state onto the
        /// alarm inputs (expiration date/certificate/type, trust-list id and
        /// last-update time) without emitting any event.
        /// </summary>
        /// <param name="context">The system context.</param>
        private void RefreshInputs(ISystemContext context)
        {
            foreach (MonitorEntry entry in m_monitors)
            {
                CertificateGroupAlarmMonitor monitor = entry.Monitor;
                ServerCertificateGroup certGroup = entry.Group;
                CertificateGroupState? node = certGroup.Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    DateTime earliest = DateTime.MaxValue;
                    ByteString certificate = default;
                    NodeId certificateType = NodeId.Null;

                    foreach (CertificateIdentifier certIdent in certGroup.ApplicationCertificates)
                    {
                        if (certIdent.RawData == null || certIdent.RawData.Length == 0)
                        {
                            continue;
                        }

                        try
                        {
                            using Certificate cert = Certificate.FromRawData(certIdent.RawData);
                            if (cert.NotAfter < earliest)
                            {
                                earliest = cert.NotAfter;
                                certificate = ByteString.From(certIdent.RawData);
                                certificateType = certIdent.CertificateType;
                            }
                        }
                        catch (Exception ex)
                        {
                            m_logger.SkippingUnreadableCertificate(ex, certGroup.BrowseName);
                        }
                    }

                    monitor.SetCertificateExpiration(
                        context,
                        earliest == DateTime.MaxValue ? null : earliest,
                        certificate,
                        certificateType);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRefreshCertificateExpiredAlarm(ex, certGroup.BrowseName);
                }

                try
                {
                    NodeId trustListId = node.TrustList?.NodeId ?? NodeId.Null;
                    var lastUpdate = (DateTime)(node.TrustList?.LastUpdateTime?.Value
                        ?? (DateTimeUtc)DateTime.MinValue);
                    double updateFrequency = monitor.TrustListOutOfDate?.UpdateFrequency?.Value ?? 0;

                    monitor.SetTrustListStatus(context, trustListId, lastUpdate, updateFrequency);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRefreshTrustListOutOfDateAlarm(ex, certGroup.BrowseName);
                }
            }
        }

        /// <summary>
        /// Explicitly binds a <see cref="CertificateGroupAlarmMonitor"/> to the
        /// <see cref="ServerCertificateGroup"/> whose certificate/TrustList state
        /// it evaluates, so the monitor list and the certificate-group list
        /// never need to be index-aligned (groups without a node get no
        /// monitor).
        /// </summary>
        /// <param name="Monitor">The alarm monitor.</param>
        /// <param name="Group">The certificate group it evaluates.</param>
        private sealed record MonitorEntry(
            CertificateGroupAlarmMonitor Monitor,
            ServerCertificateGroup Group);

        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly List<MonitorEntry> m_monitors = [];
        private readonly Lock m_lock = new();
        private ITimer? m_timer;
        private bool m_active;
        private bool m_stopped;
        private bool m_disposed;
    }

    internal static partial class CertificateAlarmSchedulerLog
    {
        [LoggerMessage(EventId = ServerEventIds.CertificateAlarmScheduler + 0, Level = LogLevel.Warning,
            Message = "Initial certificate-alarm evaluation failed.")]
        public static partial void InitialCertificateAlarmEvaluationFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.CertificateAlarmScheduler + 1, Level = LogLevel.Warning,
            Message = "Alarm evaluation tick failed.")]
        public static partial void AlarmEvaluationTickFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.CertificateAlarmScheduler + 2, Level = LogLevel.Debug,
            Message = "Skipping unreadable certificate in group {Group}.")]
        public static partial void SkippingUnreadableCertificate(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.CertificateAlarmScheduler + 3, Level = LogLevel.Warning,
            Message = "Failed to refresh CertificateExpired alarm inputs for group {Group}.")]
        public static partial void FailedToRefreshCertificateExpiredAlarm(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.CertificateAlarmScheduler + 4, Level = LogLevel.Warning,
            Message = "Failed to refresh TrustListOutOfDate alarm inputs for group {Group}.")]
        public static partial void FailedToRefreshTrustListOutOfDateAlarm(
            this ILogger logger,
            Exception ex,
            string group);
    }
}
