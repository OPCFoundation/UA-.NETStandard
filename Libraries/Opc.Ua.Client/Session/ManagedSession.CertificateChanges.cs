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
    /// and surfaces every observed change through the
    /// <see cref="ApplicationCertificateChanged"/> event so applications
    /// can implement custom rotation policies. When
    /// <see cref="ManagedSession.DisableAutoReconnectOnCertificateChange"/> is
    /// <c>false</c> (the default) the manager additionally reloads the
    /// instance certificate and triggers a reconnect on
    /// <see cref="CertificateChangeKind.ApplicationCertificateUpdated"/>
    /// so the new client cert takes effect immediately instead of
    /// waiting for the next SecurityToken renewal.
    /// <see cref="CertificateChangeKind.TrustListUpdated"/> and
    /// <see cref="CertificateChangeKind.CrlUpdated"/> intentionally
    /// do NOT trigger an automatic reconnect — see
    /// <see cref="OnCertificateChange"/> for the rationale.
    /// </summary>
    public partial class ManagedSession
    {
        /// <summary>
        /// When <c>false</c> (the default), the managed session
        /// automatically calls
        /// <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        /// and triggers a reconnect when the application's
        /// <see cref="CertificateManager"/> publishes an
        /// <see cref="CertificateChangeKind.ApplicationCertificateUpdated"/>
        /// event, so the new client cert takes effect within milliseconds
        /// rather than at the next SecurityToken renewal (OPC UA Part 4
        /// §5.5.2). Trust-list and CRL changes do NOT trigger an
        /// automatic reconnect — applications that want to honour them
        /// immediately should subscribe to
        /// <see cref="ApplicationCertificateChanged"/> and call
        /// <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        /// + <see cref="ISession.ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>
        /// themselves after evaluating whether the server certificate
        /// is still valid against the new trust state. Set this property
        /// to <c>true</c> to opt out of all automatic reconnects. The
        /// <see cref="ApplicationCertificateChanged"/> event still fires
        /// for diagnostics regardless of this setting.
        /// </summary>
        public bool DisableAutoReconnectOnCertificateChange { get; set; }

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

            if (DisableAutoReconnectOnCertificateChange)
            {
                return;
            }

            // Auto-reconnect is scoped to ApplicationCertificateUpdated
            // only. Triggering a reconnect on every TrustListUpdated /
            // CrlUpdated is too aggressive in real deployments: a new
            // server onboarded into a shared client/server fleet, or a
            // batch trust-list refresh, would force every session in
            // the process to reconnect even when the server certificate
            // is still valid against the updated trust state.
            //
            // TODO: cheap re-validation of the cached remote server
            // cert against the new trust state (and reconnect ONLY when
            // the result actually changes) is tracked under the
            // event-driven certificate validation workitem also needed
            // for the "support 500 sessions" use case. Applications
            // that want immediate re-validation today can subscribe to
            // ApplicationCertificateChanged and call ReconnectAsync
            // themselves.
            if (evt.Kind != CertificateChangeKind.ApplicationCertificateUpdated)
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

                // Concurrency note:
                //   * Session.ReloadInstanceCertificateAsync takes the
                //     same m_reconnectLock used by ReconnectAsync, so
                //     it serialises behind any in-flight reconnect /
                //     activate and the reload completes before the next
                //     reconnect starts.
                //   * StateMachine.TriggerReconnect locks the state-
                //     machine lock and is idempotent if the connection
                //     is already in the Reconnecting state.
                // The worst case is one extra reconnect attempt after a
                // racy ApplicationCertificateUpdated event, which is
                // benign — the next attempt picks up the new cert.
                //
                // The inner session must re-load its private key from
                // the registry BEFORE the reconnect so the new
                // ActivateSession is signed with the rotated
                // certificate.
                await session.ReloadInstanceCertificateAsync()
                    .ConfigureAwait(false);

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
        /// <see cref="DisableAutoReconnectOnCertificateChange"/>. Subscribers
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
