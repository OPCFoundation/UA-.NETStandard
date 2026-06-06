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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Automatic certificate-change handling for
    /// <see cref="ManagedSession"/>. Subscribes to the application's
    /// <see cref="ICertificateLifecycle.CertificateChanges"/> stream
    /// (resolved via <see cref="ApplicationConfiguration.CertificateManager"/>)
    /// and reacts to the three scenarios called out in issue #3160:
    /// own client-certificate renewal, trust-list add/remove, and CRL
    /// add/remove. When
    /// <see cref="ManagedSession.AutoReconnectOnCertificateChange"/> is
    /// <c>true</c> the manager triggers a reconnect so the new state
    /// takes effect immediately instead of waiting for the next
    /// SecurityToken renewal.
    /// </summary>
    public partial class ManagedSession
    {
        /// <summary>
        /// When <c>true</c>, the managed session automatically calls
        /// <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        /// and triggers a reconnect when the application's
        /// <see cref="CertificateManager"/> publishes an
        /// <see cref="CertificateChangeKind.ApplicationCertificateUpdated"/>,
        /// <see cref="CertificateChangeKind.TrustListUpdated"/> or
        /// <see cref="CertificateChangeKind.CrlUpdated"/> event so the
        /// new state takes effect within milliseconds rather than at the
        /// next SecurityToken renewal (OPC UA Part 4 §5.5.2).
        /// Default <c>false</c> for backwards compatibility — users who
        /// explicitly want manual control (auditing, idempotency) can
        /// leave it disabled and call
        /// <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        /// + <see cref="ISession.ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>
        /// themselves.
        /// </summary>
        public bool AutoReconnectOnCertificateChange { get; set; }

        /// <summary>
        /// Subscribes to
        /// <see cref="ICertificateLifecycle.CertificateChanges"/> on the
        /// application's <see cref="CertificateManager"/>. Safe to call
        /// when no certificate manager is configured (no-op). Disposed
        /// by <see cref="UnsubscribeCertificateChanges"/>.
        /// </summary>
        private void SubscribeCertificateChanges()
        {
            ICertificateLifecycle? lifecycle = m_configuration.CertificateManager;
            if (lifecycle == null)
            {
                return;
            }

            m_certificateChangeSubscription?.Dispose();
            m_certificateChangeSubscription = lifecycle.CertificateChanges.Subscribe(
                new CertificateChangeObserver(this));
        }

        /// <summary>
        /// Unsubscribes from
        /// <see cref="ICertificateLifecycle.CertificateChanges"/>. Safe
        /// to call when no subscription is active.
        /// </summary>
        private void UnsubscribeCertificateChanges()
        {
            IDisposable? subscription = Interlocked.Exchange(
                ref m_certificateChangeSubscription, null);
            subscription?.Dispose();
        }

        /// <summary>
        /// Handler invoked from the <see cref="CertificateChangeObserver"/>.
        /// Dispatches a one-shot background task to avoid blocking the
        /// notifying thread (which can be the certificate-manager IO
        /// thread) and to serialise reload + reconnect via the inner
        /// session's reconnect lock.
        /// </summary>
        private void OnCertificateChange(CertificateChangeEvent evt)
        {
            if (m_disposed != 0)
            {
                return;
            }

            switch (evt.Kind)
            {
                case CertificateChangeKind.ApplicationCertificateUpdated:
                case CertificateChangeKind.TrustListUpdated:
                case CertificateChangeKind.CrlUpdated:
                    break;
                default:
                    // CertificateRejected / CertificateExpiring do not
                    // require session-level action.
                    return;
            }

            ApplicationCertificateChanged?.Invoke(this, evt);

            if (!AutoReconnectOnCertificateChange)
            {
                return;
            }

            // Fire and forget — the reconnect serialiser owns its own
            // synchronisation; we just need to kick it off without
            // blocking the certificate-manager dispatcher.
            _ = Task.Run(() => HandleCertificateChangeAsync(evt));
        }

        private async Task HandleCertificateChangeAsync(CertificateChangeEvent evt)
        {
            try
            {
                Session? session = m_session;
                if (session == null || m_disposed != 0)
                {
                    return;
                }

                // For own-certificate rotations, the inner session must
                // re-load its private key from the registry BEFORE the
                // reconnect so the new ActivateSession is signed with
                // the rotated certificate. Trust-list / CRL changes do
                // not require a per-session reload — the validation
                // cores are shared via the certificate manager and
                // already invalidated by NotifyTrustListChanged.
                if (evt.Kind == CertificateChangeKind.ApplicationCertificateUpdated)
                {
                    await session.ReloadInstanceCertificateAsync()
                        .ConfigureAwait(false);
                }

                StateMachine.TriggerReconnect();

                m_logger.LogInformation(
                    "ManagedSession: requested reconnect after {Kind} on {TrustList}.",
                    evt.Kind,
                    evt.TrustList);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "ManagedSession: failed to react to certificate change {Kind}.",
                    evt.Kind);
            }
        }

        /// <summary>
        /// Raised on every observed
        /// <see cref="CertificateChangeEvent"/> regardless of
        /// <see cref="AutoReconnectOnCertificateChange"/>. Subscribers
        /// can use this for diagnostics or to implement custom
        /// rotation policies.
        /// </summary>
        public event EventHandler<CertificateChangeEvent>? ApplicationCertificateChanged;

        // CA2213 false-positive: Interlocked.Exchange in
        // UnsubscribeCertificateChanges hides the dispose call from the
        // analyzer. Disposed in both Dispose paths.
#pragma warning disable CA2213
        private IDisposable? m_certificateChangeSubscription;
#pragma warning restore CA2213

        private sealed class CertificateChangeObserver : IObserver<CertificateChangeEvent>
        {
            private readonly ManagedSession m_owner;

            public CertificateChangeObserver(ManagedSession owner)
            {
                m_owner = owner;
            }

            public void OnNext(CertificateChangeEvent value)
            {
                m_owner.OnCertificateChange(value);
            }

            public void OnError(Exception error)
            {
                m_owner.m_logger.LogWarning(
                    error,
                    "ManagedSession: certificate change stream errored.");
            }

            public void OnCompleted()
            {
            }
        }
    }
}
