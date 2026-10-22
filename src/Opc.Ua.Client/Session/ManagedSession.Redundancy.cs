/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client side failover across the members of a non-transparent redundant
    /// Server set (OPC 10000-4 §6.6.2).
    /// </summary>
    public partial class ManagedSession
    {
        /// <summary>
        /// Fails over now to another member of the non-transparent redundant
        /// Server set, with the same in-place recreate as a failover after a
        /// lost connection: subscriptions are transferred or recreated on the
        /// target and, with token reuse enabled, a HotAndMirrored target
        /// reactivates the existing session. <see cref="ConnectionStateChanged"/>
        /// reports <see cref="ConnectionState.Failover"/> and then
        /// <see cref="ConnectionState.Connected"/>.
        /// </summary>
        /// <remarks>
        /// When the failover fails the session transitions to
        /// <see cref="ConnectionState.Reconnecting"/> and the reconnect policy
        /// restores it. Cancelling <paramref name="ct"/> stops waiting; a
        /// failover in progress completes.
        /// </remarks>
        /// <param name="target">
        /// The endpoint to fail over to, or <see langword="null"/> to fail over
        /// to the target the redundancy handler selects from a refreshed
        /// redundancy snapshot.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when no redundancy handler
        /// is configured, <see cref="StatusCodes.BadInvalidState"/> when the
        /// session is not connected, <see cref="StatusCodes.BadNothingToDo"/>
        /// when the handler selects no target, or the error of the failed
        /// failover.
        /// </exception>
        public async Task FailoverAsync(ConfiguredEndpoint? target = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IServerRedundancyHandler handler = m_redundancyHandler ?? throw new ServiceResultException(
                StatusCodes.BadNotSupported, "A failover requires a server redundancy handler.");
            if (!StateMachine.IsConnected)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The managed session can only fail over while it is connected.");
            }

            target ??= await SelectFailoverTargetAsync(handler, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadNothingToDo, "The redundancy handler selected no failover target.");
            await RunRequestedFailoverAsync(target, ct).ConfigureAwait(false);
        }

        private async Task<ConfiguredEndpoint?> SelectFailoverTargetAsync(
            IServerRedundancyHandler handler,
            CancellationToken ct)
        {
            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
            await RefreshRedundancyInfoBestEffortAsync(ct, resolveCachedEndpoints: true).ConfigureAwait(false);
            ServerRedundancyInfo? info = m_redundancyInfo;
            return info == null ? null : handler.SelectFailoverTarget(info, ConfiguredEndpoint);
        }

        /// <summary>
        /// Runs a failover to <paramref name="target"/> in the Failover state of
        /// the state machine and waits until the session is connected again.
        /// </summary>
        private async Task RunRequestedFailoverAsync(ConfiguredEndpoint target, CancellationToken ct)
        {
            var outcome = new TaskCompletionSource<ServiceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!StateMachine.RequestFailover((budget, token) =>
                FailoverToRequestedTargetAsync(target, outcome, budget, token)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The managed session can only fail over while it is connected.");
            }

            using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task closed = StateMachine.WaitForClosedAsync(waiting.Token).AsTask();
                try
                {
                    if (await Task.WhenAny(outcome.Task, closed).ConfigureAwait(false) != outcome.Task ||
                        outcome.Task.IsCanceled)
                    {
                        ct.ThrowIfCancellationRequested();
                        throw new ServiceResultException(
                            StatusCodes.BadSessionClosed, "The managed session was closed during the failover.");
                    }
                }
                finally
                {
                    // Releases the close wait, which otherwise lasts until the session closes.
                    await waiting.CancelAsync().ConfigureAwait(false);
                }
            }

            ServiceResult result = await outcome.Task.ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
            await StateMachine.WaitForConnectedAsync(ct).ConfigureAwait(false);
        }

        private async Task<ServiceResult> FailoverToRequestedTargetAsync(
            ConfiguredEndpoint target,
            TaskCompletionSource<ServiceResult> outcome,
            IRetryBudget budget,
            CancellationToken ct)
        {
            ServiceResult result;
            try
            {
                m_logger.ManagedSessionFailingOverEndpoint(target.EndpointUrl);
                result = await SwitchToRedundantServerAsync(target, budget, ct).ConfigureAwait(false);
                if (ServiceResult.IsGood(result))
                {
                    await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
                    m_logger.ManagedSessionFailoverComplete();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcome.TrySetCanceled(ct);
                throw;
            }
            catch (Exception exception)
            {
                if (m_redundancyHandler is IServerRedundancyEndpointCache cache)
                {
                    cache.InvalidateEndpoint(target);
                }
                m_logger.ManagedSessionFailoverFailed(exception);
                result = ToAttemptFailure(exception);
            }
            outcome.TrySetResult(result);
            return result;
        }

        /// <summary>
        /// Switches the session to <paramref name="target"/> by recreating it in
        /// place, so subscriptions are transferred or recreated on the target.
        /// </summary>
        private async Task<ServiceResult> SwitchToRedundantServerAsync(
            ConfiguredEndpoint target,
            IRetryBudget budget,
            CancellationToken ct)
        {
            using (await RecoveryWriterLockAsync(ct)
                .ConfigureAwait(false))
            {
                Session? session = m_session;
                if (session == null)
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidState);
                }

                // Recreate in place: the inner Session reference is
                // preserved across failover so that the V2
                // SubscriptionEngine, SubscriptionManager, and any
                // external holders of InnerSession all continue to
                // operate on the same Session object. Session
                // internals re-run CreateSession+ActivateSession
                // against the new endpoint and drive subscription
                // recreate/transfer for both unamanged templates and
                // the new engine.
                using IDisposable recovery = session.DeferSubscriptionRecovery();
                await RecreateInPlaceAndRebindAsync(
                        session,
                        target,
                        budget,
                        ct)
                    .ConfigureAwait(false);
            }

            m_reconnectPolicy.Reset();
            return ServiceResult.Good;
        }

        /// <summary>
        /// After the initial connect, switches to the member of the redundant
        /// Server set with the highest probed <c>ServiceLevel</c> when it is
        /// higher than the <c>ServiceLevel</c> of the Server connected to.
        /// </summary>
        private async Task SelectInitialRedundantServerAsync(CancellationToken ct)
        {
            ServerRedundancyInfo? info = m_redundancyInfo;
            if (info == null || info.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return;
            }

            try
            {
                await RefreshRedundancyInfoBestEffortAsync(ct, resolveCachedEndpoints: true)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                !(exception is OperationCanceledException && ct.IsCancellationRequested))
            {
                m_logger.ManagedSessionRedundancyDiscoveryFailed(exception);
                return;
            }

            info = m_redundancyInfo;
            RedundantServer? best = info == null ? null : SelectHigherServiceLevelMember(info, ConfiguredEndpoint);
            if (best?.Endpoint == null)
            {
                return;
            }

            m_logger.ManagedSessionSelectingRedundantServer(
                best.Endpoint.EndpointUrl,
                best.ServiceLevel,
                info!.ServiceLevel);
            ServiceResult result = await SwitchToRedundantServerAsync(best.Endpoint, CreateRecoveryBudget(), ct)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Selects the running member with a read <c>ServiceLevel</c> that is
        /// higher than the <c>ServiceLevel</c> of the Server in use.
        /// </summary>
        private static RedundantServer? SelectHigherServiceLevelMember(
            ServerRedundancyInfo info,
            ConfiguredEndpoint current)
        {
            string? currentUri = current.Description?.Server?.ApplicationUri;
            byte currentLevel = info.ServiceLevelAccessible ? info.ServiceLevel : ServiceLevels.Maintenance;
            RedundantServer? best = null;
            for (int ii = 0; ii < info.RedundantServers.Count; ii++)
            {
                RedundantServer server = info.RedundantServers[ii];
                if (server.Endpoint != null &&
                    server.ServiceLevelKnown &&
                    server.ResolutionError == null &&
                    server.ServerState == ServerState.Running &&
                    server.ServiceLevel > currentLevel &&
                    !string.Equals(server.ServerUri, currentUri, StringComparison.Ordinal) &&
                    (best == null || server.ServiceLevel > best.ServiceLevel))
                {
                    best = server;
                }
            }
            return best;
        }

        /// <summary>
        /// Reads <c>ServiceLevel</c> and <c>ServerStatus.State</c> of every
        /// other member with a resolved endpoint.
        /// </summary>
        private async Task<ServerRedundancyInfo> ProbeMemberServiceLevelsAsync(
            ServerRedundancyInfo snapshot,
            ConfiguredEndpoint current,
            CancellationToken ct)
        {
            if (snapshot.Mode is RedundancySupport.None or RedundancySupport.Transparent ||
                snapshot.RedundantServers.Count == 0)
            {
                return snapshot;
            }

            string? currentUri = current.Description?.Server?.ApplicationUri;
            var probes = new Task<RedundantServer>[snapshot.RedundantServers.Count];
            for (int ii = 0; ii < probes.Length; ii++)
            {
                probes[ii] = ProbeMemberAsync(snapshot.RedundantServers[ii], currentUri, ct);
            }
            RedundantServer[] servers = await Task.WhenAll(probes).ConfigureAwait(false);
            return new ServerRedundancyInfo
            {
                Mode = snapshot.Mode,
                RedundantServers = servers,
                ServiceLevel = snapshot.ServiceLevel,
                ServiceLevelAccessible = snapshot.ServiceLevelAccessible,
                ServiceLevelSubrange = snapshot.ServiceLevelSubrange,
                EstimatedReturnTime = snapshot.EstimatedReturnTime,
                CurrentServerId = snapshot.CurrentServerId,
                ReadStatuses = snapshot.ReadStatuses
            };
        }

        private async Task<RedundantServer> ProbeMemberAsync(
            RedundantServer server,
            string? currentUri,
            CancellationToken ct)
        {
            ConfiguredEndpoint? endpoint = server.Endpoint;
            if (endpoint == null ||
                string.IsNullOrEmpty(server.ServerUri) ||
                string.Equals(server.ServerUri, currentUri, StringComparison.Ordinal))
            {
                return server;
            }

            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(m_peerProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            ISession? probe = null;
            try
            {
                probe = await SessionFactory.CreateAsync(
                    m_configuration,
                    endpoint,
                    false,
                    m_checkDomain,
                    m_sessionName,
                    m_sessionTimeout,
                    m_session?.Identity ?? m_identity,
                    m_preferredLocales,
                    linked.Token).ConfigureAwait(false);
                (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) = await probe.ReadValuesAsync(
                    [VariableIds.Server_ServiceLevel, VariableIds.Server_ServerStatus_State],
                    linked.Token).ConfigureAwait(false);
                if (StatusCode.IsBad(errors[0].StatusCode) || StatusCode.IsBad(values[0].StatusCode))
                {
                    return WithProbeError(server, StatusCode.IsBad(errors[0].StatusCode)
                        ? errors[0]
                        : new ServiceResult(values[0].StatusCode));
                }
                return new RedundantServer
                {
                    ServerUri = server.ServerUri,
                    ServiceLevel = values[0].GetValue<byte>(0),
                    ServiceLevelKnown = true,
                    ServerState = StatusCode.IsGood(errors[1].StatusCode) && StatusCode.IsGood(values[1].StatusCode)
                        ? (ServerState)values[1].GetValue((int)server.ServerState)
                        : server.ServerState,
                    Endpoint = endpoint
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ServiceResultException or TimeoutException
                or OperationCanceledException or IOException or SocketException)
            {
                m_logger.ManagedSessionMemberServiceLevelProbeFailed(exception, endpoint.EndpointUrl);
                return WithProbeError(server, exception switch
                {
                    ServiceResultException sre => sre.Result,
                    IOException or SocketException => new ServiceResult(exception),
                    _ => new ServiceResult(StatusCodes.BadTimeout, new LocalizedText(exception.Message))
                });
            }
            finally
            {
                if (probe != null)
                {
                    await CloseProbeAsync(probe).ConfigureAwait(false);
                }
            }
        }

        private async Task CloseProbeAsync(ISession probe)
        {
            try
            {
                int closeTimeout = m_peerProbeTimeout == Timeout.InfiniteTimeSpan
                    ? 0
                    : (int)Math.Min(m_peerProbeTimeout.TotalMilliseconds, int.MaxValue);
                await probe.CloseAsync(closeTimeout, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ManagedSessionSessionCloseFailed(exception);
            }
            finally
            {
                probe.Dispose();
            }
        }

        private static RedundantServer WithProbeError(RedundantServer server, ServiceResult error)
        {
            return new RedundantServer
            {
                ServerUri = server.ServerUri,
                ServiceLevel = server.ServiceLevel,
                ServiceLevelKnown = server.ServiceLevelKnown,
                ServerState = server.ServerState,
                Endpoint = server.Endpoint,
                ResolutionError = error
            };
        }

        private RetryBudget CreateRecoveryBudget()
        {
            return new RetryBudget(
                m_maxTotalReconnectTime == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : m_maxTotalReconnectTime,
                m_timeProvider);
        }
    }
}
