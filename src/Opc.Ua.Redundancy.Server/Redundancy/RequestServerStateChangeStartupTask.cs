// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Wires the standard <c>Server.RequestServerStateChange</c> method for Maintenance-driven Failover
    /// (OPC 10000-4 §6.6.5, OPC 10000-5 §9.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An accepted request updates <c>ServerStatus.State</c>, <c>SecondsTillShutdown</c> and
    /// <c>ShutdownReason</c> through <see cref="IServerInternal.UpdateServerStatus"/>, so a
    /// Read of the <c>ServerStatus</c> variable (or its children) returns the new values.
    /// </para>
    /// <para>
    /// Every defined <see cref="ServerState"/> except <see cref="ServerState.Unknown"/> may be
    /// requested; <see cref="ServerState.Unknown"/> and undefined values are rejected with
    /// <c>Bad_InvalidState</c>. The server's request gate treats every state other than
    /// <see cref="ServerState.Running"/> and <see cref="ServerState.NoConfiguration"/> (and
    /// <see cref="ServerState.Shutdown"/> while <c>SecondsTillShutdown</c> is still above zero)
    /// as halted, so further service requests - including a later call of this method - are
    /// rejected with <c>Bad_ServerHalted</c> once such a state is published. Returning to
    /// <see cref="ServerState.Running"/> from there is up to the host application.
    /// </para>
    /// <para>
    /// <c>SecondsTillShutdown</c> is honoured only for <see cref="ServerState.Shutdown"/>; it
    /// counts down once per second and the server stops serving requests when it reaches zero.
    /// The process itself is not stopped. For any other state it is published as zero.
    /// <c>EstimatedReturnTime</c> is set to null (<see cref="DateTimeUtc.MinValue"/>) when the
    /// requested state is <see cref="ServerState.Running"/>.
    /// </para>
    /// <para>
    /// This task cannot restart the server process, so a request with <c>restart</c> set is
    /// rejected with <c>Bad_NotSupported</c> instead of being reported as performed.
    /// </para>
    /// </remarks>
    public sealed class RequestServerStateChangeStartupTask : IServerStartupTask, IDisposable
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="options">The method wiring options.</param>
        /// <param name="serviceLevelController">Optional service-level controller.</param>
        public RequestServerStateChangeStartupTask(
            RequestServerStateChangeOptions options,
            IServiceLevelController? serviceLevelController = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_serviceLevelController = serviceLevelController;
            m_timeProvider = options.TimeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Creates the task with default options.
        /// </summary>
        /// <param name="serviceLevelController">Optional service-level controller.</param>
        public RequestServerStateChangeStartupTask(IServiceLevelController? serviceLevelController = null)
            : this(new RequestServerStateChangeOptions(), serviceLevelController)
        {
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ServerObjectState? serverObject = server.ServerObject;
            if (serverObject == null)
            {
                return default;
            }

            m_server = server;
            RequestServerStateChangeMethodState? requestServerStateChange =
                server.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange) ??
                serverObject.RequestServerStateChange;
            requestServerStateChange?.OnCall = OnRequestServerStateChange;

            return default;
        }

        /// <summary>
        /// Stops a pending <c>SecondsTillShutdown</c> countdown.
        /// </summary>
        public void Dispose()
        {
            lock (m_countdownLock)
            {
                m_disposed = true;
                StopCountdown();
            }
        }

        private ServiceResult OnRequestServerStateChange(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ServerState state,
            DateTimeUtc estimatedReturnTime,
            uint secondsTillShutdown,
            LocalizedText reason,
            bool restart)
        {
            try
            {
                ValidateAdminAccess(context);

                if (!IsRequestableState(state))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The requested server state is not accepted.");
                }

                if (restart)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadNotSupported,
                        "The server cannot restart itself.");
                }

                IServerContext? server = m_server;
                ServerObjectState? serverObject = server?.ServerObject;
                if (server == null || serverObject == null)
                {
                    return new ServiceResult(StatusCodes.BadServerHalted);
                }

                // OPC 10000-5 §9.4: secondsTillShutdown is ignored unless the state is Shutdown,
                // and estimatedReturnTime is ignored (published as null) for Running.
                if (state != ServerState.Shutdown)
                {
                    secondsTillShutdown = 0;
                }
                if (state == ServerState.Running)
                {
                    estimatedReturnTime = DateTimeUtc.MinValue;
                    reason = LocalizedText.Null;
                }

                byte serviceLevel = SelectServiceLevel(state);
                ApplyServiceLevel(serverObject, server.DefaultSystemContext, serviceLevel);
                ApplyEstimatedReturnTime(serverObject, server.DefaultSystemContext, estimatedReturnTime);

                lock (m_countdownLock)
                {
                    if (m_disposed)
                    {
                        return new ServiceResult(StatusCodes.BadServerHalted);
                    }

                    StopCountdown();
                    ApplyServerStatus(server, serverObject, state, secondsTillShutdown, reason);
                    if (secondsTillShutdown > 0)
                    {
                        StartCountdown(server, serverObject, secondsTillShutdown, reason);
                    }
                }

                return ServiceResult.Good;
            }
            catch (ServiceResultException sre)
            {
                return sre.Result;
            }
        }

        /// <summary>
        /// Whether a Client may request the state. <see cref="ServerState.Unknown"/> describes a
        /// server whose state cannot be determined and is never a state to move to.
        /// </summary>
        private static bool IsRequestableState(ServerState state)
        {
            return state switch
            {
                ServerState.Running or
                ServerState.Failed or
                ServerState.NoConfiguration or
                ServerState.Suspended or
                ServerState.Shutdown or
                ServerState.Test or
                ServerState.CommunicationFault => true,
                _ => false
            };
        }

        private void ValidateAdminAccess(ISystemContext context)
        {
            if (m_options.AdminAccessValidator != null)
            {
                m_options.AdminAccessValidator(context);
                return;
            }

            IConfigurationNodeManager? configurationNodeManager = m_server?.FindNodeManagers<IConfigurationNodeManager>().FirstOrDefault() ??
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied,
                    "A configuration node manager is required to validate administrator access.");

            configurationNodeManager.HasApplicationSecureAdminAccess(context);
        }

        private byte SelectServiceLevel(ServerState state)
        {
            if (m_options.ServiceLevelSelector != null)
            {
                return m_options.ServiceLevelSelector(state);
            }

            return state switch
            {
                ServerState.Running => ServiceLevels.Maximum,
                ServerState.Shutdown => ServiceLevels.Maintenance,
                ServerState.Suspended => ServiceLevels.Maintenance,
                _ => ServiceLevels.NoData
            };
        }

        private void ApplyServiceLevel(ServerObjectState serverObject, ISystemContext context, byte serviceLevel)
        {
            if (m_serviceLevelController != null)
            {
                m_serviceLevelController.SetServiceLevel(serviceLevel);
                return;
            }

            if (serverObject.ServiceLevel != null)
            {
                serverObject.ServiceLevel.Value = serviceLevel;
                serverObject.ServiceLevel.ClearChangeMasks(context, false);
            }
        }

        private static void ApplyEstimatedReturnTime(
            ServerObjectState serverObject,
            ISystemContext context,
            DateTimeUtc estimatedReturnTime)
        {
            if (serverObject.EstimatedReturnTime != null)
            {
                serverObject.EstimatedReturnTime.Value = estimatedReturnTime;
                serverObject.EstimatedReturnTime.ClearChangeMasks(context, false);
            }
        }

        /// <summary>
        /// Publishes the status through the server's status object, which backs every Read of
        /// <c>ServerStatus</c> and its children; the variable nodes are kept in step so
        /// subscriptions see the change. A server context that is not an
        /// <see cref="IServerInternal"/> only has the variable nodes to write to.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="serverObject">The Server Object.</param>
        /// <param name="state">The state to publish.</param>
        /// <param name="secondsTillShutdown">The seconds until shutdown.</param>
        /// <param name="reason">The shutdown reason.</param>
        /// <param name="requiredState">
        /// When set, the status is only written while the current state equals it; the check
        /// and the write happen under the same status lock.
        /// </param>
        /// <returns><c>false</c> when <paramref name="requiredState"/> did not match.</returns>
        private static bool ApplyServerStatus(
            IServerContext server,
            ServerObjectState serverObject,
            ServerState state,
            uint secondsTillShutdown,
            LocalizedText reason,
            ServerState? requiredState = null)
        {
            ISystemContext context = server.DefaultSystemContext;
            if (server is IServerInternal serverInternal)
            {
                bool applied = false;
                serverInternal.UpdateServerStatus(status =>
                {
                    if (requiredState != null && status.Value.State != requiredState.Value)
                    {
                        return;
                    }
                    applied = true;
                    status.Value.State = state;
                    status.Value.SecondsTillShutdown = secondsTillShutdown;
                    status.Value.ShutdownReason = reason;
                    UpdateVariables(status.Variable ?? serverObject.ServerStatus, context, state,
                        secondsTillShutdown, reason);
                });
                return applied;
            }

            if (requiredState != null && server.CurrentState != requiredState.Value)
            {
                return false;
            }
            UpdateVariables(serverObject.ServerStatus, context, state, secondsTillShutdown, reason);
            return true;
        }

        private static void UpdateVariables(
            ServerStatusState? variable,
            ISystemContext context,
            ServerState state,
            uint secondsTillShutdown,
            LocalizedText reason)
        {
            if (variable == null)
            {
                return;
            }

            variable.State?.Value = state;
            variable.SecondsTillShutdown?.Value = secondsTillShutdown;
            variable.ShutdownReason?.Value = reason;
            variable.ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Starts publishing the remaining seconds once per second. Must be called with
        /// <see cref="m_countdownLock"/> held.
        /// </summary>
        private void StartCountdown(
            IServerContext server,
            ServerObjectState serverObject,
            uint secondsTillShutdown,
            LocalizedText reason)
        {
            long generation = ++m_countdownGeneration;
            long deadline = m_timeProvider.GetTimestamp() +
                (long)(secondsTillShutdown * (double)m_timeProvider.TimestampFrequency);
            m_countdownTimer = m_timeProvider.CreateTimer(
                _ => OnCountdownTick(server, serverObject, generation, deadline, reason),
                null,
                kCountdownPeriod,
                kCountdownPeriod);
        }

        /// <summary>
        /// Stops the countdown. Must be called with <see cref="m_countdownLock"/> held.
        /// </summary>
        private void StopCountdown()
        {
            m_countdownGeneration++;
            m_countdownTimer?.Dispose();
            m_countdownTimer = null;
        }

        private void OnCountdownTick(
            IServerContext server,
            ServerObjectState serverObject,
            long generation,
            long deadline,
            LocalizedText reason)
        {
            lock (m_countdownLock)
            {
                if (m_disposed || generation != m_countdownGeneration)
                {
                    return;
                }

                TimeSpan remaining = m_timeProvider.GetElapsedTime(m_timeProvider.GetTimestamp(), deadline);
                uint secondsTillShutdown = remaining <= TimeSpan.Zero
                    ? 0
                    : (uint)Math.Ceiling(remaining.TotalSeconds);
                if (!ApplyServerStatus(
                    server,
                    serverObject,
                    ServerState.Shutdown,
                    secondsTillShutdown,
                    reason,
                    requiredState: ServerState.Shutdown))
                {
                    // the state was changed by someone else; the countdown no longer applies.
                    StopCountdown();
                    return;
                }

                if (secondsTillShutdown == 0)
                {
                    StopCountdown();
                }
            }
        }

        private static readonly TimeSpan kCountdownPeriod = TimeSpan.FromSeconds(1);
        private readonly RequestServerStateChangeOptions m_options;
        private readonly IServiceLevelController? m_serviceLevelController;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_countdownLock = new();
        private IServerContext? m_server;
        private ITimer? m_countdownTimer;
        private long m_countdownGeneration;
        private bool m_disposed;
    }
}
