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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Commit/rollback surface of <see cref="ConfigurationNodeManager"/>: <c>ApplyChanges</c> and
    /// <c>CancelChanges</c> (OPC 10000-12 §7.10.9), the deferred grace-period apply and channel cut,
    /// <c>ResetToServerDefaults</c>, and the shutdown drains. Code in this file owns
    /// <c>m_pendingApplyChangesTask</c>, <c>m_pendingResetTask</c> and <c>m_pendingApplyChangesLock</c>,
    /// schedules work on <c>m_backgroundWork</c> under <c>m_shutdownCts</c>, and disposes
    /// <see cref="PendingCertificateRotation"/> payloads.
    /// </summary>
    public partial class ConfigurationNodeManager
    {
        /// <inheritdoc/>
        public Task DrainPendingApplyChangesAsync(CancellationToken cancellationToken = default)
        {
            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingApplyChangesTask;
            }

            if (pending == null || pending.IsCompleted)
            {
                return Task.CompletedTask;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return pending;
            }

            return pending.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Awaits completion of any pending deferred <c>ResetToServerDefaults</c>
        /// work scheduled by a recent Method call. Returns immediately when no
        /// reset is in flight. Used by tests and tightly-coupled hosts to
        /// deterministically wait for the reset to run.
        /// </summary>
        internal Task DrainPendingResetAsync(CancellationToken cancellationToken = default)
        {
            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingResetTask;
            }

            if (pending.IsCompleted)
            {
                return Task.CompletedTask;
            }

            return cancellationToken.CanBeCanceled ? pending.WaitAsync(cancellationToken) : pending;
        }

        /// <summary>
        /// Implements the Optional OPC 10000-12 §7.10.13
        /// <c>ResetToServerDefaults</c> Method: it resets the application
        /// security configuration to its default state. The Method requires an
        /// authenticated SecureChannel and the SecurityAdmin Role, is rejected
        /// while another Session owns an active PushManagement transaction, and
        /// returns its response before the actual reset runs. After the
        /// response, the server advertises the pending shutdown
        /// (<c>ServerState</c> = Shutdown, <c>ShutdownReason</c>,
        /// <c>SecondsTillShutdown</c>), waits the configured grace period so the
        /// Client can receive this response, and then invokes the injected
        /// <see cref="IServerConfigurationResetProvider"/>.
        /// </summary>
        private ValueTask<ServiceResult> ResetToServerDefaultsAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            // §7.10.13: authenticated SecureChannel + SecurityAdmin Role.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            if (m_serverConfigurationOptions.ResetProvider == null)
            {
                return new ValueTask<ServiceResult>(
                    (ServiceResult)StatusCodes.BadNotSupported);
            }

            // A reset invalidates the whole configuration, so it must not race
            // an in-flight PushManagement transaction owned by another Session.
            NodeId sessionId = GetSessionId(context);
            try
            {
                m_coordinator.ValidateSessionCanParticipate(sessionId);
            }
            catch (ServiceResultException ex)
            {
                return new ValueTask<ServiceResult>((ServiceResult)ex.StatusCode);
            }

            m_logger.ResetToServerDefaultsRequested(sessionId);

            ScheduleDeferredReset();

            // §7.10.13: the response is returned before the reset/shutdown runs.
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Advertises the pending shutdown and, after the configured grace
        /// period has elapsed so the <c>ResetToServerDefaults</c> response can
        /// be received, invokes the reset provider. Honors the shutdown
        /// cancellation token so a server shutdown that races the reset
        /// abandons it cleanly. The completion is exposed via
        /// <see cref="DrainPendingResetAsync"/> for deterministic testing.
        /// </summary>
        private void ScheduleDeferredReset()
        {
            IServerConfigurationResetProvider? resetProvider = m_serverConfigurationOptions.ResetProvider;
            if (resetProvider == null)
            {
                return;
            }

            CancellationToken shutdownToken;
            try
            {
                shutdownToken = m_shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (shutdownToken.IsCancellationRequested)
            {
                return;
            }

            TimeSpan delay = m_serverConfigurationOptions.ResetShutdownDelay;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_pendingApplyChangesLock)
            {
                m_pendingResetTask = completion.Task;
            }

            m_backgroundWork.Run("DeferredResetToServerDefaults", async _ =>
            {
                try
                {
                    AdvertisePendingShutdown(delay);

                    try
                    {
                        await m_timeProvider.Delay(delay, shutdownToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    if (shutdownToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    await resetProvider.ResetToServerDefaultsAsync(shutdownToken).ConfigureAwait(false);
                    completion.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.ResetToServerDefaultsFailed(ex);
                    completion.TrySetException(ex);
                }
            });
        }

        /// <summary>
        /// Sets <c>ServerState</c> to <see cref="ServerState.Shutdown"/> and
        /// advertises the <c>ShutdownReason</c> and <c>SecondsTillShutdown</c>
        /// per OPC 10000-12 §7.10.13, tolerating a server whose status object is
        /// not available.
        /// </summary>
        private void AdvertisePendingShutdown(TimeSpan delay)
        {
            try
            {
                uint secondsTillShutdown = (uint)Math.Ceiling(Math.Max(0, delay.TotalSeconds));
                var reason = new LocalizedText(
                    "en-US",
                    "The server is resetting to its default configuration. " +
                    "Existing credentials may no longer be valid after the restart.");

                Server.UpdateServerStatus(status =>
                {
                    status.Value.State = ServerState.Shutdown;
                    status.Value.ShutdownReason = reason;
                    status.Value.SecondsTillShutdown = secondsTillShutdown;

                    ServerStatusState? variable = status.Variable;
                    if (variable != null)
                    {
                        if (variable.State != null)
                        {
                            variable.State.Value = ServerState.Shutdown;
                        }
                        if (variable.ShutdownReason != null)
                        {
                            variable.ShutdownReason.Value = reason;
                        }
                        if (variable.SecondsTillShutdown != null)
                        {
                            variable.SecondsTillShutdown.Value = secondsTillShutdown;
                        }
                        variable.ClearChangeMasks(Server.DefaultSystemContext, true);
                    }
                });
            }
            catch (Exception ex)
            {
                m_logger.FailedToAdvertisePendingShutdown(ex);
            }
        }

        /// <summary>
        /// Commits the active PushManagement transaction (OPC UA Part 12
        /// §7.10.2). Runs every staged certificate/TrustList operation's
        /// commit in request order (reverse-compensating on failure),
        /// updates <c>TransactionDiagnostics</c>, and — once the commit
        /// succeeds — schedules the post-response SecureChannel
        /// renegotiation for every rotated certificate (§7.10.9).
        /// </summary>
        private async ValueTask<ServiceResult> ApplyChangesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            // §7.10.9: ApplyChanges requires an authenticated SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            NodeId sessionId = GetSessionId(context);

            // A fresh collector for this call alone, flowed to
            // RegisterPendingRotation via the ambient async call chain
            // (see m_activeRotationCollector) rather than a shared field:
            // a concurrent/duplicate ApplyChanges call that finds no
            // active transaction owned by its Session short-circuits
            // through the coordinator without ever running a commit, so
            // it always observes its OWN empty collector and can never
            // drain or dispose the rotations produced by this call.
            var rotations = new List<PendingCertificateRotation>();

            // An apply-local collector for the exact certificate groups and
            // TrustLists this call commits, filled by the coordinator while
            // this Session still owns the transaction. This is the §7.10.9
            // counterpart of the rotation collector above: it must never be
            // re-derived from a fresh coordinator snapshot after ApplyChanges
            // returns, because ownership is released before the coordinator
            // returns and another Session may already have staged a new
            // transaction whose (uncommitted) targets a snapshot would report.
            var committedEffects = new PushConfigurationApplyEffects();
            ServiceResult result;
            try
            {
                m_activeRotationCollector.Value = rotations;
                result = await m_coordinator.ApplyChangesAsync(sessionId, committedEffects, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_activeRotationCollector.Value = null;
            }

            UpdateTransactionDiagnostics(context);

            if (!ServiceResult.IsGood(result))
            {
                // The transaction failed and was reverse-compensated;
                // nothing actually rotated, so any provisionally-recorded
                // rotations must be discarded rather than scheduled.
                foreach (PendingCertificateRotation rotation in rotations)
                {
                    rotation.OldCertificate?.Dispose();
                }
                return result;
            }

            // §7.10.9: identify the TrustLists whose committed changes must
            // force affected SecureChannels to renegotiate (application/HTTPS
            // groups) or invalidate certificate-based user identities (user
            // token group). Only TrustLists actually committed by THIS
            // transaction are considered - taken from the apply-local
            // collector, not a fresh coordinator snapshot that may already
            // represent another Session's active transaction - so unaffected
            // channels/Sessions are never disturbed.
            List<TrustListChangeEffect> trustListEffects =
                BuildTrustListEffects(committedEffects.TrustLists);

            if (rotations.Count > 0 || trustListEffects.Count > 0)
            {
                // Schedule the deferred apply: wait a short grace period for
                // the method response to be flushed, then re-sync the
                // certificate manager from disk (for rotations), force-close
                // every SecureChannel that was negotiated against the rotated
                // certificate(s), force channels with a now-untrusted peer
                // certificate to renegotiate, and close Sessions whose
                // certificate user identity is no longer valid. The
                // completion handle is exposed via DrainPendingApplyChangesAsync
                // so tests and hosts can deterministically await the effects
                // rather than racing the delay.
                ScheduleDeferredApplyChanges(rotations, trustListEffects);
            }

            // OPC 10000-12 §7.8.3: a committed TrustList change updates the
            // TrustList's LastUpdateTime synchronously here (even when no
            // deferred §7.10.9 effects are scheduled), so refresh the alarm
            // values now. A certificate rotation additionally re-evaluates
            // once the deferred reload has completed.
            try
            {
                m_alarmScheduler.UpdateAndEvaluate(context, emitEvents: m_alarmScheduler.IsActive);
            }
            catch (Exception alarmEx)
            {
                m_logger.CertificateAlarmReevaluationAfterCommitFailed(alarmEx);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Cancels (discards, without applying) the active PushManagement
        /// transaction owned by the calling Session (OPC UA Part 12
        /// §7.10.2/§7.10.11).
        /// </summary>
        private ValueTask<ServiceResult> CancelChangesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            // §7.10.2/§7.10.11: CancelChanges requires an authenticated
            // SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            NodeId sessionId = GetSessionId(context);
            ServiceResult result = m_coordinator.CancelChanges(sessionId);
            UpdateTransactionDiagnostics(context);
            return new ValueTask<ServiceResult>(result);
        }

        /// <summary>
        /// Schedules the post-response fan-out for both the server
        /// certificate rotation channel-cuts and the §7.10.9 TrustList
        /// effects. Chains onto any already-running deferred apply so
        /// concurrent calls to <see cref="ApplyChangesAsync"/> run
        /// sequentially.
        /// </summary>
        private void ScheduleDeferredApplyChanges(
            List<PendingCertificateRotation> rotations,
            List<TrustListChangeEffect> trustListEffects)
        {
            CancellationToken shutdownToken;
            try
            {
                shutdownToken = m_shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                // The manager is being disposed; do not schedule new deferred
                // work. Release the captured rotations so nothing leaks.
                DisposeRotations(rotations);
                return;
            }

            if (shutdownToken.IsCancellationRequested)
            {
                // Shutdown already signalled: skip the post-response effects
                // entirely (they would only run against listeners/managers
                // being torn down) and release the captured rotations.
                DisposeRotations(rotations);
                return;
            }

            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task previous;
            lock (m_pendingApplyChangesLock)
            {
                previous = m_pendingApplyChangesTask;
                m_pendingApplyChangesTask = completion.Task;
            }

            m_backgroundWork.Run("DeferredApplyChanges", async _ =>
            {
                try
                {
                    // Wait for any earlier deferred apply to finish to
                    // preserve ordering.
                    if (previous != null)
                    {
                        try
                        {
                            await previous.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Errors on the previous task are already
                            // logged; do not propagate to the new one.
                        }
                    }

                    TimeSpan gracePeriod = ApplyChangesGracePeriod;
                    if (gracePeriod < TimeSpan.Zero)
                    {
                        gracePeriod = TimeSpan.Zero;
                    }

                    m_logger.ApplyChangesScheduled(gracePeriod.TotalMilliseconds);

                    // Give the client a chance to receive the
                    // ApplyChanges response before cutting its channel.
                    // OPC UA Part 12 §7.10.9 requires the response is
                    // returned first; without a transport-level
                    // "response flushed" hook this grace period is the
                    // pragmatic compromise. The grace period itself is
                    // configurable via ApplyChangesGracePeriod so hosts
                    // running over high-latency links can tune it.
                    // TODO: implement a transport-level
                    // "response-flushed" callback so this can be
                    // deterministic without relying on a fixed delay.
                    try
                    {
                        await m_timeProvider.Delay(gracePeriod, shutdownToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Server shutting down during the grace period: skip
                        // the post-response effects entirely so they never run
                        // against listeners/managers that are about to be
                        // disposed. The finally below releases the rotations.
                        completion.TrySetResult(null);
                        return;
                    }

                    if (shutdownToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    m_logger.ApplyChangesRunning();

                    // Reload the certificate manager only when a server
                    // application certificate actually rotated. A TrustList-
                    // only change does not touch the server's own
                    // certificates, and the validator's directory-backed
                    // trust stores refresh themselves, so an app-cert reload
                    // here would be needless work.
                    if (rotations.Count > 0 && m_configuration.CertificateManager != null)
                    {
                        // Deliberately not cancellable: a rotation that has begun
                        // must finish updating the configuration, otherwise the
                        // server is left advertising a certificate it no longer has.
                        await m_configuration.CertificateManager.UpdateAsync(
                                m_configuration.SecurityConfiguration,
                                m_configuration.ApplicationUri,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    // Force-close affected SecureChannels on every
                    // transport listener that opted into
                    // ITransportListenerCertificateRotation.
                    IReadOnlyList<ITransportListener> listeners
                        = (Server as ITransportListenerRegistryProvider)?.TransportListeners
                            ?? [];

                    int totalCut = 0;
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        if (rotation.OldCertificate == null)
                        {
                            continue;
                        }

                        foreach (ITransportListener listener in listeners)
                        {
                            if (listener is not ITransportListenerCertificateRotation rotator)
                            {
                                continue;
                            }

                            try
                            {
                                IReadOnlyList<string> closed
                                    = await rotator.CloseChannelsForCertificateAsync(
                                            rotation.OldCertificate,
                                            CancellationToken.None)
                                        .ConfigureAwait(false);
                                totalCut += closed.Count;
                            }
                            catch (Exception ex)
                            {
                                m_logger.ListenerFailedToCloseChannels(
                                    ex,
                                    listener.ListenerId,
                                    rotation.CertificateType);
                            }
                        }
                    }

                    m_logger.ApplyChangesCompleted(totalCut);

                    // §7.10.9 TrustList effects: force channels whose peer
                    // certificate is no longer trusted to renegotiate and
                    // close Sessions (plus Subscriptions) whose certificate
                    // user identity is no longer valid. Unaffected channels
                    // and Sessions are left untouched.
                    if (trustListEffects.Count > 0)
                    {
                        // The push TrustList writes certificates and CRLs
                        // directly to the stores, so the certificate manager
                        // must be told the trust material changed: dropping
                        // its cached validation cores (including the
                        // validated-certificate fast path) BEFORE the effect
                        // re-validation below guarantees the sweep observes
                        // the committed stores rather than a stale snapshot.
                        // The staged operations do not record whether
                        // certificates or CRLs changed, so both flags are
                        // raised conservatively.
                        NotifyTrustMaterialChanged(trustListEffects);

                        // Deliberately not cancellable: the committed change's
                        // post-response effects must run to completion once
                        // started (shutdown is handled by the checks above).
                        await ApplyTrustListEffectsAsync(
                                trustListEffects,
                                listeners,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    // OPC 10000-12 §7.8.3: the committed certificate/TrustList
                    // change may clear (or raise) the CertificateExpired /
                    // TrustListOutOfDate alarms, so re-evaluate now that the new
                    // certificate has been reloaded from disk.
                    try
                    {
                        m_alarmScheduler.UpdateAndEvaluate(SystemContext, emitEvents: m_alarmScheduler.IsActive);
                    }
                    catch (Exception alarmEx)
                    {
                        m_logger.CertificateAlarmReevaluationFailed(alarmEx);
                    }

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.ApplyChangesUpdateFailed(ex);
                    completion.TrySetException(ex);
                }
                finally
                {
                    DisposeRotations(rotations);
                }
            });
        }

        /// <summary>
        /// Disposes the captured old-certificate references of every pending
        /// rotation, tolerating a <see langword="null"/> reference.
        /// </summary>
        private static void DisposeRotations(List<PendingCertificateRotation> rotations)
        {
            foreach (PendingCertificateRotation rotation in rotations)
            {
                rotation.OldCertificate?.Dispose();
            }
        }

        /// <summary>
        /// Captured payload for a single certificate-group rotation
        /// scheduled by <see cref="ApplyChangesAsync"/>. The deferred apply
        /// task owns the contained <see cref="Certificate"/> reference
        /// and disposes it once the channel-cut completes.
        /// </summary>
        private sealed class PendingCertificateRotation
        {
            public Certificate? OldCertificate { get; set; }
            public NodeId CertificateType { get; set; }
        }
    }
}
