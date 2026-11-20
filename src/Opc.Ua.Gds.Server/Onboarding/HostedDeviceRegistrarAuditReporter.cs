// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Routes injected registrar audit calls to the restricted reporter bound by its live model host.
    /// Calls before model initialization or after shutdown are rejected, never discarded.
    /// One adapter belongs to one registrar host at a time.
    /// </summary>
    public sealed class HostedDeviceRegistrarAuditReporter : IDeviceRegistrarAuditReporter
    {
        /// <summary>
        /// Creates an unbound adapter without resolving server-owned nodes during container construction.
        /// </summary>
        public HostedDeviceRegistrarAuditReporter()
        {
        }

        /// <inheritdoc/>
        public ValueTask ReportIdentityAcceptedAsync(
            ISystemContext context,
            OnboardingIdentitySelection selection,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            return GetReporter(cancellationToken).ReportIdentityAcceptedAsync(context, selection, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ReportSoftwareStatusAsync(
            ISystemContext context,
            string productInstanceUri,
            bool status,
            string softwareRevision,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            return GetReporter(cancellationToken).ReportSoftwareStatusAsync(
                context, productInstanceUri, status, softwareRevision, cancellationToken);
        }

        /// <summary>
        /// Attaches only after the factory has initialized the model and restricted its event permissions.
        /// A second live host cannot replace the first host's reporter.
        /// </summary>
        internal void Attach(IDeviceRegistrarAuditReporter reporter)
        {
            if (reporter == null)
            {
                throw new ArgumentNullException(nameof(reporter));
            }
            if (ReferenceEquals(reporter, this))
            {
                throw new ArgumentException("The audit adapter cannot forward to itself.", nameof(reporter));
            }
            if (Interlocked.CompareExchange(ref m_reporter, reporter, null) != null)
            {
                throw new InvalidOperationException("The audit adapter is already bound to a registrar host.");
            }
        }

        /// <summary>
        /// Removes only the reporter owned by the shutting-down model; a stale owner cannot detach another host.
        /// </summary>
        internal void Detach(IDeviceRegistrarAuditReporter reporter)
        {
            if (reporter == null)
            {
                throw new ArgumentNullException(nameof(reporter));
            }
            Interlocked.CompareExchange(ref m_reporter, null, reporter);
        }

        /// <summary>
        /// Requires a live host without resolving or constructing a replacement server implicitly.
        /// </summary>
        private IDeviceRegistrarAuditReporter GetReporter(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Volatile.Read(ref m_reporter) ??
                throw new ServiceResultException(StatusCodes.BadServerHalted, "The registrar audit model is not active.");
        }

        /// <summary>The reporter borrowed from the current model host.</summary>
        private IDeviceRegistrarAuditReporter? m_reporter;
    }
}
