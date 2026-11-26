// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
        /// The endpoint of a known redundancy member, or <see langword="null"/> to fail over
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

            bool configured = target != null;
            target ??= await SelectFailoverTargetAsync(handler, ct).ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadNothingToDo, "The redundancy handler selected no failover target.");
            string peerUri = GetFailoverPeerUri(target, configured);
            await RunRequestedFailoverAsync(target, peerUri, ct).ConfigureAwait(false);
        }

        private async Task<ConfiguredEndpoint?> SelectFailoverTargetAsync(
            IServerRedundancyHandler handler,
            CancellationToken ct)
        {
            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
            await RefreshRedundancyInfoBestEffortAsync(ct, resolveCachedEndpoints: true).ConfigureAwait(false);
            ServerRedundancyInfo? info = m_redundancyInfo;
            return SelectEligibleFailoverTarget(handler, info);
        }

        /// <summary>
        /// Runs a failover to <paramref name="target"/> in the Failover state of
        /// the state machine and waits until the session is connected again.
        /// </summary>
        private async Task RunRequestedFailoverAsync(ConfiguredEndpoint target, string peerUri, CancellationToken ct)
        {
            var outcome = new TaskCompletionSource<ServiceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!StateMachine.RequestFailover(
                (budget, token) => FailoverToRequestedTargetAsync(target, peerUri, budget, token),
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
            string peerUri,
            IRetryBudget budget,
            CancellationToken ct)
        {
            try
            {
                m_logger.ManagedSessionFailingOverEndpoint(target.EndpointUrl);
                ServiceResult result = await SwitchToRedundantServerAsync(
                    target, peerUri, budget, retirePreviousSession: true, ct).ConfigureAwait(false);
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
            ConfiguredEndpoint current = ConfiguredEndpoint;
            byte? currentLevel = await ReadActiveServiceLevelAsync(ct).ConfigureAwait(false);
            if (currentLevel is not byte level || ServiceLevels.IsHealthy(level))
            {
                // An unreadable active level is not a degradation signal. Normal reconnect owns channel faults.
                return;
            }

            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
            info = m_redundancyInfo;
            if (info == null || info.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return;
            }

            await RefreshRedundancyInfoBestEffortAsync(ct, resolveCachedEndpoints: true).ConfigureAwait(false);
            byte? latestLevel = await ReadActiveServiceLevelAsync(ct).ConfigureAwait(false);
            if (StateMachine.State != ConnectionState.Connected || !ReferenceEquals(current, ConfiguredEndpoint) ||
                (latestLevel == null && !ServiceLevels.IsMaintenance(level)))
            {
                return;
            }
            info = WithActiveServiceLevel(
                m_redundancyInfo ?? info,
                latestLevel ?? level);
            if (info.ServiceLevelAccessible && ServiceLevels.IsHealthy(info.ServiceLevel))
            {
                return;
            }
            ServerFailoverDecision decision = handler.ShouldFailover(ForPeerSelection(info), current);
            ConfiguredEndpoint? target = decision.IsFailoverWarranted
                ? SelectEligibleFailoverTarget(handler, info, proactive: true)
                : null;
            if (target == null)
            {
                if (IsCurrentMemberInMaintenance(info))
                {
                    SuspendForMaintenance(info);
                }
                return;
            }

            m_logger.ManagedSessionServiceLevelFailover(info.ServiceLevel, decision.Reason);
            await RunRequestedFailoverAsync(target, GetFailoverPeerUri(target, configured: false), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the <c>ServiceLevel</c> of the Server in use.
        /// </summary>
        private async Task<byte?> ReadActiveServiceLevelAsync(CancellationToken ct)
        {
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(
                m_redundancyRefreshTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                DataValue value = await this.ReadValueAsync(VariableIds.Server_ServiceLevel, linked.Token)
                    .ConfigureAwait(false);
                return StatusCode.IsGood(value.StatusCode) &&
                    value.WrappedValue.TryGetValue(out byte level) ? level : null;
            }
            catch (Exception exception) when (exception is ServiceResultException ||
                (exception is OperationCanceledException && !ct.IsCancellationRequested))
            {
                return null;
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
            string peerUri,
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
                session.EnableTokenReuseFailover =
                    m_enableTokenReuseFailover && m_redundancyInfo?.Mode == RedundancySupport.HotAndMirrored;
                try
                {
                    await RecreateInPlaceAndRebindAsync(
                            session,
                            target,
                            budget,
                            ct,
                            retirePreviousSession,
                            peerUri)
                        .ConfigureAwait(false);
                }
                finally
                {
                    session.EnableTokenReuseFailover = m_enableTokenReuseFailover;
                }
            }

            m_reconnectPolicy.Reset();
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks the initial server's health before admitting application services.
        /// </summary>
        private async Task SelectInitialRedundantServerAsync(CancellationToken ct)
        {
            ServerRedundancyInfo? info = m_redundancyInfo;
            if (info == null ||
                m_redundancyHandler is not IServerRedundancyHandler handler ||
                info.Mode is RedundancySupport.None or RedundancySupport.Transparent ||
                (info.ServiceLevelAccessible && ServiceLevels.IsHealthy(info.ServiceLevel)))
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
            }

            byte? latestLevel = await ReadActiveServiceLevelAsync(ct).ConfigureAwait(false);
            info = WithActiveServiceLevel(
                m_redundancyInfo ?? info,
                latestLevel ?? (IsCurrentMemberInMaintenance(info) ? info.ServiceLevel : null));
            ConfiguredEndpoint? target = handler.ShouldFailover(ForPeerSelection(info), ConfiguredEndpoint)
                .IsFailoverWarranted ? SelectEligibleFailoverTarget(handler, info, proactive: true) : null;
            if (target == null)
            {
                if (m_monitorServiceLevel && IsCurrentMemberInMaintenance(info))
                {
                    throw new ServiceResultException(SuspendForMaintenance(info));
                }
                return;
            }

            ServiceResult result = await SwitchToRedundantServerAsync(
                    target, GetFailoverPeerUri(target, configured: false),
                    CreateRecoveryBudget(), retirePreviousSession: true, ct)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
            await RefreshRedundancyInfoBestEffortAsync(ct).ConfigureAwait(false);
        }

        private ConfiguredEndpoint? SelectEligibleFailoverTarget(
            IServerRedundancyHandler handler,
            ServerRedundancyInfo? info,
            bool proactive = false)
        {
            if (info == null || info.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return null;
            }

            bool requiresHealthyPeer = proactive &&
                info.ServiceLevelAccessible && ServiceLevels.IsDegraded(info.ServiceLevel);
            ServerRedundancyInfo selection = ForPeerSelection(info, requiresHealthyPeer);
            ConfiguredEndpoint? target = handler.SelectFailoverTarget(selection, ConfiguredEndpoint);
            return target != null && (!m_probeMemberServiceLevel ||
                selection.RedundantServers.Contains(server => ReferenceEquals(server.Endpoint, target)))
                ? target
                : null;
        }

        private ServerRedundancyInfo ForPeerSelection(ServerRedundancyInfo info, bool requiresHealthyPeer = false)
        {
            return !m_probeMemberServiceLevel ? info : CopySnapshot(
                info,
                info.ServiceLevel,
                info.ServiceLevelAccessible,
                info.RedundantServers.ToList().Where(server =>
                    server.ServiceLevelProbed && server.ServiceLevelKnown && server.ResolutionError == null &&
                    server.ServerState == ServerState.Running &&
                    (requiresHealthyPeer
                        ? ServiceLevels.IsHealthy(server.ServiceLevel)
                        : ServiceLevels.IsOperational(server.ServiceLevel)))
                    .ToArray());
        }

        private bool IsCurrentMemberInMaintenance(ServerRedundancyInfo? info)
        {
            if (info is { ServiceLevelAccessible: true })
            {
                return info.Mode is not (RedundancySupport.None or RedundancySupport.Transparent) &&
                    ServiceLevels.IsMaintenance(info.ServiceLevel);
            }
            return Volatile.Read(ref m_suspendedMaintenanceUri) is string uri &&
                string.Equals(uri, m_session?.RedundantPeerUri, StringComparison.Ordinal);
        }

        private ServiceResult SuspendForMaintenance(ServerRedundancyInfo? info)
        {
            DateTime retryAfter = DefaultServerRedundancyHandler.GetMaintenanceRetryTime(
                info?.EstimatedReturnTime ?? DateTime.MinValue, m_timeProvider.GetUtcNow().UtcDateTime);
            var failure = new ServiceResult(
                StatusCodes.BadServerHalted,
                new LocalizedText("The only available redundancy member is in maintenance."));
            if (StateMachine.SuspendRecoveryUntil(retryAfter, failure))
            {
                Volatile.Write(ref m_suspendedMaintenanceUri, m_session?.RedundantPeerUri);
            }
            return failure;
        }

        private static ServerRedundancyInfo WithActiveServiceLevel(ServerRedundancyInfo info, byte? level)
        {
            return CopySnapshot(info, level ?? ServiceLevels.NoData, level.HasValue, info.RedundantServers);
        }

        private static ServerRedundancyInfo CopySnapshot(
            ServerRedundancyInfo info, byte level, bool levelKnown, ArrayOf<RedundantServer> servers)
        {
            return new ServerRedundancyInfo
            {
                Mode = info.Mode,
                RedundantServers = servers,
                ServiceLevel = level,
                ServiceLevelAccessible = levelKnown,
                ServiceLevelSubrange = ServiceLevels.GetSubrange(level),
                EstimatedReturnTime = info.EstimatedReturnTime,
                CurrentServerId = info.CurrentServerId,
                ReadStatuses = info.ReadStatuses
            };
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
            int count = Math.Min(snapshot.RedundantServers.Count, m_maxProbedMembers);
            var servers = new RedundantServer[count];
            var workers = new Task[Math.Min(count, m_maxConcurrentPeerProbes)];
            int next = -1;
            for (int ii = 0; ii < workers.Length; ii++)
            {
                workers[ii] = ProbeNextAsync();
            }
            await Task.WhenAll(workers).ConfigureAwait(false);
            // A member whose probe waited for a connection in vain is no longer selectable.
            return ApplyMemberConnections(
                CopySnapshot(snapshot, snapshot.ServiceLevel, snapshot.ServiceLevelAccessible, servers), current);

            async Task ProbeNextAsync()
            {
                int index;
                while ((index = Interlocked.Increment(ref next)) < count)
                {
                    ct.ThrowIfCancellationRequested();
                    servers[index] = await ProbeMemberAsync(
                        snapshot.RedundantServers[index], current, currentUri, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task<RedundantServer> ProbeMemberAsync(
            RedundantServer server,
            ConfiguredEndpoint current,
            string? currentUri,
            CancellationToken ct)
        {
            ConfiguredEndpoint? endpoint = server.Endpoint;
            if (endpoint == null || server.ResolutionError != null ||
                string.IsNullOrEmpty(server.ServerUri) ||
                string.Equals(server.ServerUri, currentUri, StringComparison.Ordinal))
            {
                return server;
            }

            // Probes use a separate session without weakening channel security.
            ServiceResultException? mismatch = GetSecurityConfigurationMismatch(
                current.Description, endpoint.Description);
            if (mismatch != null)
            {
                m_logger.ManagedSessionMemberServiceLevelProbeFailed(mismatch, endpoint.EndpointUrl);
                return WithProbeError(server, mismatch.Result);
            }

            using CancellationTokenSource timeout =
                m_timeProvider.CreateCancellationTokenSource(m_peerProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            ISession? probe = null;
            try
            {
                probe = await CreateProbeSessionAsync(
                    server.ServerUri,
                    DetachEndpoint(endpoint),
                    linked.Token).ConfigureAwait(false);
                (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) = await probe.ReadValuesAsync(
                    [VariableIds.Server_ServiceLevel, VariableIds.Server_ServerStatus_State],
                    linked.Token).ConfigureAwait(false);
                if (values.Count != 2 || errors.Count != 2)
                {
                    return WithProbeError(server, new ServiceResult(StatusCodes.BadUnknownResponse));
                }
                for (int ii = 0; ii < values.Count; ii++)
                {
                    if (!StatusCode.IsGood(errors[ii].StatusCode) || !StatusCode.IsGood(values[ii].StatusCode))
                    {
                        return WithProbeError(server, !StatusCode.IsGood(errors[ii].StatusCode)
                            ? errors[ii]
                            : new ServiceResult(values[ii].StatusCode));
                    }
                }
                if (!values[0].WrappedValue.TryGetValue(out byte level) ||
                    !values[1].WrappedValue.TryGetValue(out ServerState state))
                {
                    return WithProbeError(server, new ServiceResult(StatusCodes.BadTypeMismatch));
                }
                return new RedundantServer
                {
                    ServerUri = server.ServerUri,
                    ServiceLevel = level,
                    ServiceLevelKnown = true,
                    ServiceLevelProbed = true,
                    ServerState = state,
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
                using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(m_peerProbeTimeout);
                int closeTimeout = Math.Max(1, (int)m_peerProbeTimeout.TotalMilliseconds);
                await probe.CloseAsync(closeTimeout, true, timeout.Token).ConfigureAwait(false);
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
                ServiceLevel = ServiceLevels.NoData,
                ServiceLevelKnown = false,
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
            CancellationToken ct)
        {
            ServerPeerIdentity.ValidateEndpoint(endpoint, serverUri);
            ITransportWaitingConnection? connection = m_memberConnections == null
                ? null
                : await WaitForRecoveryConnectionAsync(endpoint, null, switching: true, ct).ConfigureAwait(false);

            try
            {
                IUserIdentity? configuredProbeIdentity = m_probeIdentity;
                IUserIdentity? activeIdentity = m_session?.Identity ?? m_identity;
                if (configuredProbeIdentity is { TokenType: not UserTokenType.Anonymous } &&
                    (ReferenceEquals(configuredProbeIdentity, activeIdentity) ||
                        (activeIdentity != null && configuredProbeIdentity.TokenType == activeIdentity.TokenType &&
                            string.Equals(configuredProbeIdentity.DisplayName, activeIdentity.DisplayName,
                                StringComparison.Ordinal))))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadIdentityTokenInvalid,
                        "The probe identity must differ from the active identity.");
                }

                IUserIdentity? probeIdentity = configuredProbeIdentity == null
                    ? null
                    : new UserIdentity(configuredProbeIdentity.TokenHandler);
                return await CreatePeerBoundSessionAsync(
                    endpoint, connection, serverUri, probeIdentity, ct, probeOnly: true)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (connection != null)
                {
                    CloseUnusedConnection(connection);
                }
                throw;
            }
        }

        private Task<Session> CreatePeerBoundSessionAsync(
            ConfiguredEndpoint endpoint,
            ITransportWaitingConnection? connection,
            string expectedUri,
            IUserIdentity? identity,
            CancellationToken ct,
            bool probeOnly = false)
        {
            ServerPeerIdentity.ValidateEndpoint(endpoint, expectedUri);
            return OpenActivatedSessionAsync(
                endpoint,
                connection,
                async (session, token) =>
                {
                    session.BindRedundantPeerIdentity(expectedUri);
                    IUserIdentity sessionIdentity = identity ?? new UserIdentity();
                    if (probeOnly)
                    {
                        await session.OpenForRedundancyProbeAsync(
                            m_sessionName, m_sessionTimeout, sessionIdentity, m_checkDomain, token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await session.OpenAsync(
                            m_sessionName, m_sessionTimeout, sessionIdentity,
                            m_preferredLocales, m_checkDomain, token).ConfigureAwait(false);
                    }
                },
                ct,
                useReverseConnectManager: !probeOnly,
                allowDirectCustomFactory: true);
        }

        private string GetFailoverPeerUri(ConfiguredEndpoint target, bool configured)
        {
            if (m_redundancyInfo?.Mode == RedundancySupport.Transparent &&
                m_session?.RedundantPeerUri is string sharedUri)
            {
                return sharedUri;
            }

            string? claimedUri = target.Description.Server?.ApplicationUri;
            string? expectedUri = null;
            if (m_redundancyInfo is { Mode: not (RedundancySupport.None or RedundancySupport.Transparent) } info)
            {
                foreach (RedundantServer member in info.RedundantServers)
                {
                    if (ReferenceEquals(member.Endpoint, target) ||
                        (configured && string.Equals(claimedUri, member.ServerUri, StringComparison.Ordinal)))
                    {
                        if (expectedUri != null &&
                            !string.Equals(expectedUri, member.ServerUri, StringComparison.Ordinal))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadCertificateUriInvalid,
                                "The endpoint refers to more than one redundancy member.");
                        }
                        expectedUri = member.ServerUri;
                    }
                }
            }

            return expectedUri ??
                (m_session?.RedundantPeerUri is string currentUri &&
                    string.Equals(claimedUri, currentUri, StringComparison.Ordinal)
                        ? currentUri
                        : null) ??
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUriInvalid,
                    "The endpoint does not identify a known redundancy member.");
        }

        private static string RequirePeerUri(string? expectedUri)
        {
            return expectedUri ??
                throw new ServiceResultException(
                    StatusCodes.BadCertificateUriInvalid,
                    "Redundancy requires an independently configured server application URI.");
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
                    ServiceLevel = ServiceLevels.NoData,
                    ServiceLevelKnown = false,
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
        private readonly bool m_monitorServiceLevel;
        private readonly int m_maxConcurrentPeerProbes;
        private readonly int m_maxProbedMembers;
        private readonly IUserIdentity? m_probeIdentity;
        private string? m_suspendedMaintenanceUri;

        private RetryBudget CreateRecoveryBudget()
        {
            return new RetryBudget(
                m_maxTotalReconnectTime == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : m_maxTotalReconnectTime,
                m_timeProvider);
        }
    }
}
