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
using System.Threading.Channels;
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
    /// <see cref="DisableAutoReconnectOnCertificateChange"/> is
    /// <c>false</c> (the default) the manager additionally reloads the
    /// instance certificate and triggers a reconnect on
    /// <see cref="CertificateChangeKind.ApplicationCertificateUpdated"/>,
    /// and re-runs server-certificate validation on
    /// <see cref="CertificateChangeKind.TrustListUpdated"/> /
    /// <see cref="CertificateChangeKind.CrlUpdated"/> so the session
    /// reconnects only when the cached server cert is no longer
    /// trusted under the new state. See
    /// <see cref="RunRevalidationLoopAsync"/> for the debouncing /
    /// single-in-flight semantics.
    /// </summary>
    public partial class ManagedSession
    {
        /// <summary>
        /// When <c>false</c> (the default), the managed session
        /// automatically reacts to events published by the application's
        /// <see cref="CertificateManager"/>:
        /// <list type="bullet">
        ///   <item>
        ///     <see cref="CertificateChangeKind.ApplicationCertificateUpdated"/>
        ///     — call
        ///     <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        ///     and trigger a reconnect so the new client cert takes
        ///     effect immediately rather than at the next SecurityToken
        ///     renewal (OPC UA Part 4 §5.5.2).
        ///   </item>
        ///   <item>
        ///     <see cref="CertificateChangeKind.TrustListUpdated"/> or
        ///     <see cref="CertificateChangeKind.CrlUpdated"/> — debounce
        ///     a burst of events, re-run
        ///     <see cref="ICertificateValidatorEx.ValidateAsync(Certificate, TrustListIdentifier, CancellationToken)"/>
        ///     against the cached server certificate, and reconnect
        ///     ONLY when the result flips from valid to invalid. The
        ///     debounce keeps batch trust-list updates (e.g. a new
        ///     server onboarded into a shared fleet) from forcing every
        ///     session in the process to reconnect.
        ///   </item>
        /// </list>
        /// Set this property to <c>true</c> to opt out of all automatic
        /// reconnects — applications that require manual control
        /// (auditing, idempotency) can subscribe to
        /// <see cref="ApplicationCertificateChanged"/> and call
        /// <see cref="ISession.ReloadInstanceCertificateAsync(CancellationToken)"/>
        /// + <see cref="ISession.ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>
        /// themselves. The <see cref="ApplicationCertificateChanged"/>
        /// event still fires for diagnostics regardless of this setting.
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
            StartRevalidationLoop();
        }

        /// <summary>
        /// Unsubscribes from
        /// <see cref="ICertificateLifecycle.CertificateChanges"/> and
        /// cancels the background revalidation loop. Safe to call when
        /// no subscription is active. Synchronous callers (the sync
        /// <see cref="Dispose(bool)"/> path) cannot
        /// await the loop's exit;
        /// <see cref="StopRevalidationLoopAsync"/> handles the
        /// asynchronous wait used by <c>DisposeAsync</c>.
        /// </summary>
        private void UnsubscribeCertificateChanges()
        {
            IDisposable? subscription = Interlocked.Exchange(
                ref m_certificateChangeSubscription, null);
            subscription?.Dispose();
            CancelRevalidationLoop();
        }

        /// <summary>
        /// Handler invoked from the <see cref="CertificateChangeObserver"/>
        /// on the certificate-manager dispatcher thread. Must NOT block
        /// or allocate per-event work — the heavy lifting runs on the
        /// long-lived <see cref="RunRevalidationLoopAsync"/> loop, woken
        /// via <see cref="SignalRevalidation"/>.
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

            switch (evt.Kind)
            {
                case CertificateChangeKind.ApplicationCertificateUpdated:
                    // Own-certificate rotation always needs reload +
                    // reconnect — no validation can save it. Fire-and-
                    // forget on the thread pool; the inner session's
                    // reconnect lock serialises with any in-flight
                    // ReconnectAsync (see HandleApplicationCertificateUpdatedAsync).
                    _ = Task.Run(() => HandleApplicationCertificateUpdatedAsync(evt));
                    break;
                case CertificateChangeKind.TrustListUpdated:
                case CertificateChangeKind.CrlUpdated:
                    // Wake the persistent revalidation loop. No Task is
                    // allocated here — SignalRevalidation collapses
                    // bursts via a bounded channel of capacity 1 with
                    // BoundedChannelFullMode.DropWrite.
                    SignalRevalidation();
                    break;
            }
        }

        private async Task HandleApplicationCertificateUpdatedAsync(CertificateChangeEvent evt)
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
                // The worst case on a racy event is one extra reconnect
                // attempt — benign, the next attempt picks up the new
                // cert.
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
        /// Posts a wake-up to <see cref="RunRevalidationLoopAsync"/>.
        /// Allocation-free in the steady state — when a signal is
        /// already pending the bounded channel silently drops the
        /// duplicate, giving AutoResetEvent-like coalescing semantics.
        /// </summary>
        private void SignalRevalidation()
        {
            m_revalidationSignal.Writer.TryWrite(0);
        }

        /// <summary>
        /// Starts the single per-session revalidation loop. Idempotent:
        /// callers may invoke from <see cref="SubscribeCertificateChanges"/>
        /// even if the loop was previously started — the existing loop
        /// is cancelled and replaced. The pattern mirrors
        /// <see cref="StartIdentityRefreshLoop"/>.
        /// </summary>
        private void StartRevalidationLoop()
        {
            var cts = new CancellationTokenSource();
            Task task = RunRevalidationLoopAsync(cts.Token);

            CancellationTokenSource? previousCts;
            lock (m_revalidationLock)
            {
                previousCts = m_revalidationCancellation;
                m_revalidationCancellation = cts;
                m_revalidationTask = task;
            }
            previousCts?.Cancel();
            previousCts?.Dispose();
        }

        /// <summary>
        /// Cancels the revalidation loop without awaiting its exit.
        /// Used by the synchronous <see cref="Dispose(bool)"/>
        /// path which cannot await asynchronously. Mirrors
        /// <see cref="CancelIdentityRefreshLoop"/>.
        /// </summary>
        private void CancelRevalidationLoop()
        {
            CancellationTokenSource? cts;
            lock (m_revalidationLock)
            {
                cts = m_revalidationCancellation;
                m_revalidationCancellation = null;
                m_revalidationTask = null;
            }
            cts?.Cancel();
        }

        /// <summary>
        /// Cancels the revalidation loop and awaits its exit. Used by
        /// <see cref="DisposeAsync"/> to guarantee the
        /// loop has released the certificate manager and any in-flight
        /// validation completed before the manager is torn down.
        /// </summary>
        private async Task StopRevalidationLoopAsync()
        {
            CancellationTokenSource? cts;
            Task? task;
            lock (m_revalidationLock)
            {
                cts = m_revalidationCancellation;
                task = m_revalidationTask;
                m_revalidationCancellation = null;
                m_revalidationTask = null;
            }

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        /// <summary>
        /// Long-lived loop that drains <see cref="m_revalidationSignal"/>
        /// and re-runs server-certificate validation whenever the
        /// application's <see cref="CertificateManager"/> publishes a
        /// <see cref="CertificateChangeKind.TrustListUpdated"/> or
        /// <see cref="CertificateChangeKind.CrlUpdated"/> event.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The loop owns the at-most-one-in-flight property — only this
        /// task ever calls <see cref="RevalidateServerCertificateAsync"/>,
        /// and it does so serially. Bursts of signals collapse to a
        /// single validation: <see cref="m_revalidationSignal"/> is a
        /// bounded channel of capacity 1 with
        /// <see cref="BoundedChannelFullMode.DropWrite"/>, so duplicate
        /// signals received while one is pending are silently dropped.
        /// </para>
        /// <para>
        /// After the signal is read, the loop waits a short debounce
        /// window via <see cref="m_timeProvider"/> so a burst of events
        /// (e.g. a batch trust-list refresh) coalesces to one validation
        /// on the final state. Signals that arrive during the debounce
        /// or during the validation itself re-fill the channel and are
        /// picked up on the next iteration, so the final state is always
        /// honoured exactly once after the burst settles.
        /// </para>
        /// </remarks>
        private async Task RunRevalidationLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await m_revalidationSignal.Reader.ReadAsync(ct)
                        .ConfigureAwait(false);

                    await m_timeProvider.Delay(s_revalidationDebounce, ct)
                        .ConfigureAwait(false);

                    // Drain any signals that arrived during the debounce
                    // so the next ReadAsync only fires for events that
                    // post-date this validation.
                    while (m_revalidationSignal.Reader.TryRead(out _))
                    {
                    }

                    try
                    {
                        await RevalidateServerCertificateAsync(ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(
                            ex,
                            "ManagedSession: server-certificate revalidation failed; will retry on next change.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Loop cancellation from Dispose / Unsubscribe — exit cleanly.
            }
        }

        /// <summary>
        /// Re-runs <see cref="ICertificateValidatorEx.ValidateAsync(Certificate, TrustListIdentifier, CancellationToken)"/>
        /// against the cached server certificate currently advertised by
        /// the session's configured endpoint and triggers a reconnect
        /// when the result flips from valid to invalid.
        /// </summary>
        /// <remarks>
        /// Virtual to give unit tests a seam without standing up a full
        /// connected session. The default implementation pulls the cert
        /// from <c>ConfiguredEndpoint.Description.ServerCertificate</c>
        /// (the cert the channel was negotiated against), early-returns
        /// when no cert is cached (e.g. a <c>SecurityPolicy.None</c>
        /// channel), validates against the default Peers trust list, and
        /// calls <see cref="ConnectionStateMachine.TriggerReconnect"/>
        /// when the result is no longer valid.
        /// </remarks>
        internal virtual async Task RevalidateServerCertificateAsync(CancellationToken ct)
        {
            Session? session = m_session;
            if (session == null || m_disposed != 0)
            {
                return;
            }

            ConfiguredEndpoint? endpoint = session.ConfiguredEndpoint;
            ByteString serverCertBlob = endpoint?.Description?.ServerCertificate ?? default;
            if (serverCertBlob.IsEmpty)
            {
                // SecurityPolicy.None or pre-discovery — nothing to validate.
                return;
            }

            ICertificateValidatorEx? validator = m_configuration.CertificateManager;
            if (validator == null)
            {
                return;
            }

            byte[] rawData = serverCertBlob.ToArray();
            using var serverCert = Certificate.FromRawData(rawData);

            CertificateValidationResult result;
            try
            {
                result = await validator.ValidateAsync(serverCert, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "ManagedSession: ValidateAsync threw on cached server certificate {Thumbprint}; treating as invalid.",
                    serverCert.Thumbprint);
                StateMachine.TriggerReconnect();
                return;
            }

            if (result.IsValid)
            {
                m_logger.LogDebug(
                    "ManagedSession: cached server certificate {Thumbprint} still trusted after trust/CRL update — no reconnect needed.",
                    serverCert.Thumbprint);
                return;
            }

            m_logger.LogInformation(
                "ManagedSession: cached server certificate {Thumbprint} no longer trusted after trust/CRL update ({Status}); triggering reconnect.",
                serverCert.Thumbprint,
                result.StatusCode);
            StateMachine.TriggerReconnect();
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
        /// <summary>
        /// CTS owned by StartRevalidationLoop / disposed by
        /// StopRevalidationLoopAsync. CancelRevalidationLoop (sync) only
        /// cancels — the CTS is freed by the next StartRevalidationLoop
        /// or by StopRevalidationLoopAsync.
        /// </summary>
        private CancellationTokenSource? m_revalidationCancellation;
#pragma warning restore CA2213

        private readonly Lock m_revalidationLock = new();
        private Task? m_revalidationTask;

        /// <summary>
        /// Bounded channel of capacity 1 with
        /// <see cref="BoundedChannelFullMode.DropWrite"/> — duplicate
        /// signals received while one is already pending are silently
        /// dropped, which gives AutoResetEvent-like semantics without
        /// per-event allocation.
        /// </summary>
        private readonly Channel<int> m_revalidationSignal
            = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private static readonly TimeSpan s_revalidationDebounce
            = TimeSpan.FromMilliseconds(250);

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
