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
using System.Collections.Concurrent;
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
            if (!StateMachine.RequestFailover(
                (budget, token) => FailoverToRequestedTargetAsync(target, budget, token),
                outcome))
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
            IRetryBudget budget,
            CancellationToken ct)
        {
            try
            {
                m_logger.ManagedSessionFailingOverEndpoint(target.EndpointUrl);
                ServiceResult result = await SwitchToRedundantServerAsync(
                    target, budget, retirePreviousSession: true, ct).ConfigureAwait(false);
                if (ServiceResult.IsGood(result))
                {
                    await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
                    m_logger.ManagedSessionFailoverComplete();
                }
                return result;
            }
            catch (Exception exception) when (
                !(exception is OperationCanceledException && ct.IsCancellationRequested))
            {
                if (m_redundancyHandler is IServerRedundancyEndpointCache cache)
                {
                    cache.InvalidateEndpoint(target);
                }
                m_logger.ManagedSessionFailoverFailed(exception);
                return ToAttemptFailure(exception);
            }
        }

        /// <summary>
        /// Reads the <c>ServiceLevel</c> of the Server in use at
        /// <paramref name="interval"/> and fails over when the redundancy
        /// handler decides that a failover is warranted.
        /// </summary>
        private async ValueTask MonitorServiceLevelAsync(TimeSpan interval, CancellationToken shutdown)
        {
            while (StateMachine.State != ConnectionState.Closed)
            {
                await m_timeProvider.Delay(interval, shutdown).ConfigureAwait(false);
                if (StateMachine.State != ConnectionState.Connected)
                {
                    continue;
                }
                try
                {
                    await FailOverOnServiceLevelAsync(shutdown).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    m_logger.ManagedSessionServiceLevelMonitoringFailed(exception);
                }
            }
        }

        private async Task FailOverOnServiceLevelAsync(CancellationToken ct)
        {
            IServerRedundancyHandler? handler = m_redundancyHandler;
            if (handler == null)
            {
                return;
            }
            ServerRedundancyInfo? info = m_redundancyInfo;
            if (info == null)
            {
                await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
                info = m_redundancyInfo;
            }
            if (info == null ||
                info.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return;
            }

            // A Healthy Server is never left (OPC 10000-4 §6.6.2.4.3), so only its
            // ServiceLevel is read; the redundant set is refreshed, resolved and
            // probed once the Server in use is no longer Healthy.
            if (await IsServiceLevelHealthyAsync(ct).ConfigureAwait(false))
            {
                return;
            }

            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
            info = m_redundancyInfo;
            if (info == null ||
                info.Mode is RedundancySupport.None or RedundancySupport.Transparent ||
                (info.ServiceLevelAccessible && ServiceLevels.IsHealthy(info.ServiceLevel)))
            {
                return;
            }

            await RefreshRedundancyInfoBestEffortAsync(ct, resolveCachedEndpoints: true).ConfigureAwait(false);
            info = m_redundancyInfo!;
            ConfiguredEndpoint current = ConfiguredEndpoint;
            ServerFailoverDecision decision = handler.ShouldFailover(info, current);
            ConfiguredEndpoint? target = decision.IsFailoverWarranted
                ? handler.SelectFailoverTarget(info, current)
                : null;
            if (target == null || StateMachine.State != ConnectionState.Connected)
            {
                return;
            }

            m_logger.ManagedSessionServiceLevelFailover(info.ServiceLevel, decision.Reason);
            await RunRequestedFailoverAsync(target, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the <c>ServiceLevel</c> of the Server in use.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the read succeeds and the value is in the
        /// Healthy subrange.
        /// </returns>
        private async Task<bool> IsServiceLevelHealthyAsync(CancellationToken ct)
        {
            try
            {
                DataValue value = await this.ReadValueAsync(VariableIds.Server_ServiceLevel, ct)
                    .ConfigureAwait(false);
                return StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetValue(out byte level) &&
                    ServiceLevels.IsHealthy(level);
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        /// <summary>
        /// Switches the session to <paramref name="target"/> by recreating it in
        /// place, so subscriptions are transferred or recreated on the target.
        /// With <paramref name="retirePreviousSession"/>, a session newly created
        /// on the target closes the previous session on the Server in use.
        /// </summary>
        private async Task<ServiceResult> SwitchToRedundantServerAsync(
            ConfiguredEndpoint target,
            IRetryBudget budget,
            bool retirePreviousSession,
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
                        ct,
                        retirePreviousSession)
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
            ServiceResult result = await SwitchToRedundantServerAsync(
                    best.Endpoint, CreateRecoveryBudget(), retirePreviousSession: true, ct)
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
                probes[ii] = ProbeMemberAsync(snapshot.RedundantServers[ii], current, currentUri, ct);
            }
            RedundantServer[] servers = await Task.WhenAll(probes).ConfigureAwait(false);
            // A member whose probe waited for a connection in vain is no longer selectable.
            return ApplyMemberConnections(new ServerRedundancyInfo
            {
                Mode = snapshot.Mode,
                RedundantServers = servers,
                ServiceLevel = snapshot.ServiceLevel,
                ServiceLevelAccessible = snapshot.ServiceLevelAccessible,
                ServiceLevelSubrange = snapshot.ServiceLevelSubrange,
                EstimatedReturnTime = snapshot.EstimatedReturnTime,
                CurrentServerId = snapshot.CurrentServerId,
                ReadStatuses = snapshot.ReadStatuses
            }, current);
        }

        private async Task<RedundantServer> ProbeMemberAsync(
            RedundantServer server,
            ConfiguredEndpoint current,
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

            // The probe authenticates with the identity in use, so it must not
            // weaken the security the session is bound to.
            ServiceResultException? mismatch = GetSecurityConfigurationMismatch(
                current.Description, endpoint.Description);
            if (mismatch != null)
            {
                m_logger.ManagedSessionMemberServiceLevelProbeFailed(mismatch, endpoint.EndpointUrl);
                return WithProbeError(server, mismatch.Result);
            }

            IUserIdentity? identity = m_session?.Identity ?? m_identity;
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(m_peerProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            ISession? probe = null;
            try
            {
                probe = await CreateProbeSessionAsync(
                    server.ServerUri,
                    DetachEndpoint(endpoint),
                    identity == null ? null : new UserIdentity(identity.TokenHandler),
                    linked.Token).ConfigureAwait(false);
                (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) = await probe.ReadValuesAsync(
                    [VariableIds.Server_ServiceLevel, VariableIds.Server_ServerStatus_State],
                    linked.Token).ConfigureAwait(false);
                for (int ii = 0; ii < values.Count; ii++)
                {
                    if (StatusCode.IsBad(errors[ii].StatusCode) || StatusCode.IsBad(values[ii].StatusCode))
                    {
                        return WithProbeError(server, StatusCode.IsBad(errors[ii].StatusCode)
                            ? errors[ii]
                            : new ServiceResult(values[ii].StatusCode));
                    }
                }
                return new RedundantServer
                {
                    ServerUri = server.ServerUri,
                    ServiceLevel = values[0].GetValue<byte>(0),
                    ServiceLevelKnown = true,
                    ServerState = (ServerState)values[1].GetValue((int)server.ServerState),
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
                if (exception is TimeoutException or OperationCanceledException)
                {
                    // The member opened no connection within the probe timeout.
                    m_connectingMembers.TryRemove(server.ServerUri, out _);
                }
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

        /// <summary>
        /// Creates the short session that reads a member's <c>ServiceLevel</c>,
        /// over one of the member's reverse connections when
        /// <see cref="ServerRedundancyOptions.MemberConnections"/> is set.
        /// </summary>
        private async Task<ISession> CreateProbeSessionAsync(
            string serverUri,
            ConfiguredEndpoint endpoint,
            IUserIdentity? identity,
            CancellationToken ct)
        {
            ITransportWaitingConnection? connection = m_memberConnections == null
                ? null
                : await WaitForRecoveryConnectionAsync(endpoint, null, switching: true, ct).ConfigureAwait(false);
            if (connection == null)
            {
                return await SessionFactory.CreateAsync(
                    m_configuration,
                    endpoint,
                    false,
                    m_checkDomain,
                    m_sessionName,
                    m_sessionTimeout,
                    identity,
                    m_preferredLocales,
                    ct).ConfigureAwait(false);
            }

            try
            {
                return await SessionFactory.CreateAsync(
                    m_configuration,
                    connection,
                    endpoint,
                    false,
                    m_checkDomain,
                    m_sessionName,
                    m_sessionTimeout,
                    identity,
                    m_preferredLocales,
                    ct).ConfigureAwait(false);
            }
            catch
            {
                CloseUnusedConnection(connection);
                throw;
            }
        }

        /// <summary>
        /// Removes the endpoint of every other member that has no connection
        /// available from <see cref="ServerRedundancyOptions.MemberConnections"/>
        /// and was not seen with one before, and reports the member as not
        /// connected, so it is neither probed nor selected. A member seen with
        /// a connection stays selectable while it keeps opening connections,
        /// because the probe and the failover take its connections.
        /// </summary>
        private ServerRedundancyInfo ApplyMemberConnections(
            ServerRedundancyInfo snapshot,
            ConfiguredEndpoint current)
        {
            IReverseConnectionAvailability? connections = m_memberConnections;
            if (connections == null ||
                snapshot.Mode is RedundancySupport.None or RedundancySupport.Transparent ||
                snapshot.RedundantServers.Count == 0)
            {
                return snapshot;
            }

            string? currentUri = current.Description?.Server?.ApplicationUri;
            var servers = new RedundantServer[snapshot.RedundantServers.Count];
            bool changed = false;
            for (int ii = 0; ii < servers.Length; ii++)
            {
                RedundantServer server = snapshot.RedundantServers[ii];
                servers[ii] = server;
                if (string.IsNullOrEmpty(server.ServerUri) ||
                    IsInitialServer(server.ServerUri) ||
                    string.Equals(server.ServerUri, currentUri, StringComparison.Ordinal))
                {
                    continue;
                }
                if (SelectMemberEndpointUrl(connections, server.ServerUri, current) != null)
                {
                    continue;
                }
                servers[ii] = new RedundantServer
                {
                    ServerUri = server.ServerUri,
                    ServiceLevel = server.ServiceLevel,
                    ServiceLevelKnown = server.ServiceLevelKnown,
                    ServerState = server.ServerState,
                    ResolutionError = ReverseConnectRedundantServerEndpointResolver.NotConnected().Result
                };
                changed = true;
            }
            if (!changed)
            {
                return snapshot;
            }
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

        /// <summary>
        /// Selects the endpoint url an available connection of the member
        /// announces and remembers it, or returns the url remembered for a
        /// member seen with a connection before, which a source keyed by the
        /// announced url still serves when no connection is held at the moment.
        /// </summary>
        private Uri? SelectMemberEndpointUrl(
            IReverseConnectionAvailability connections,
            string serverUri,
            ConfiguredEndpoint endpoint)
        {
            Uri? url = ReverseConnectRedundantServerEndpointResolver.SelectAvailableEndpointUrl(
                connections, serverUri, endpoint);
            if (url != null)
            {
                m_connectingMembers[serverUri] = url;
                return url;
            }
            return m_connectingMembers.TryGetValue(serverUri, out Uri? announced) ? announced : null;
        }

        /// <summary>
        /// Whether <paramref name="serverUri"/> is the Server the session was
        /// first connected to, or that Server is not known yet.
        /// </summary>
        private bool IsInitialServer(string serverUri)
        {
            string? initial = m_initialServerUri;
            return string.IsNullOrEmpty(initial) || string.Equals(initial, serverUri, StringComparison.Ordinal);
        }

        /// <summary>
        /// The ServerUris of members seen with an available connection and the
        /// endpoint url their connections announced, until a probe waits for one
        /// of their connections in vain.
        /// </summary>
        private readonly ConcurrentDictionary<string, Uri> m_connectingMembers = new(StringComparer.Ordinal);

        private RetryBudget CreateRecoveryBudget()
        {
            return new RetryBudget(
                m_maxTotalReconnectTime == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : m_maxTotalReconnectTime,
                m_timeProvider);
        }
    }
}
