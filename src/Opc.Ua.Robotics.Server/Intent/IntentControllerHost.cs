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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.RobotIntent.Server
{
    /// <summary>
    /// The execution engine behind one <see cref="IntentControllerState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of OPC UA - Robot Intent that the .NET stack previously had no
    /// answer for. An OPC UA Call cannot stay open for the length of a real motion -
    /// OPC 10000-4 discards a method result when the Session ends "independent of the
    /// task actually performed at the Server" - so submission returns a handle and the
    /// work is tracked on a Part 10 program instance the client watches.
    /// </para>
    /// <para>
    /// Intents execute SERIALLY here. That satisfies every BlockingMode constraint by
    /// construction: the specification forbids beginning a Single or Hard intent while
    /// another executes and merely permits None and Soft to overlap, so a serial host
    /// is conformant. Parallelism would be an optimisation, not a correction.
    /// </para>
    /// </remarks>
    public sealed class IntentControllerHost : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Creates a host over an already-materialised controller node.
        /// </summary>
        /// <param name="controller">The controller node this host drives.</param>
        /// <param name="executor">The application code that moves the robot.</param>
        /// <param name="addNode">Adds a per-invocation node to the address space.</param>
        /// <param name="options">Host options; defaults are used when null.</param>
        /// <param name="removeNode">Removes a node again, when the host can delete.</param>
        public IntentControllerHost(
            IntentControllerState controller,
            IIntentExecutor executor,
            Func<NodeState, CancellationToken, ValueTask> addNode,
            IntentControllerHostOptions? options = null,
            Func<NodeState, CancellationToken, ValueTask>? removeNode = null)
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_addNode = addNode ?? throw new ArgumentNullException(nameof(addNode));
            m_options = options ?? new IntentControllerHostOptions();
            m_removeNode = removeNode;
        }

        /// <summary>
        /// The controller node this host drives.
        /// </summary>
        public IntentControllerState Controller => m_controller;

        /// <summary>
        /// The Session that currently holds command authority, or null.
        /// </summary>
        public NodeId? ControlOwner { get; private set; }

        /// <summary>
        /// The safety state the host is refusing against.
        /// </summary>
        public SafetyStatus SafetyState
        {
            get
            {
                lock (m_lock)
                {
                    return m_safety;
                }
            }
        }

        /// <summary>
        /// Raised when a per-invocation node could not be retired.
        /// </summary>
        public event EventHandler<IntentNodeAddFailure>? NodeRemoveFailed;

        /// <summary>
        /// Raised when a per-invocation node could not be published.
        /// </summary>
        public event EventHandler<IntentNodeAddFailure>? NodeAddFailed;

        internal bool IsShutdownDeferred => Volatile.Read(ref m_shutdownDeferred) != 0;

        internal bool ResourcesDisposed => Volatile.Read(ref m_resourcesDisposed) != 0;

        internal Func<string, CancellationToken, ValueTask>? BeforeExecuteAsync { get; set; }

        internal Func<string, ValueTask>? BeforeDisposeCancelAsync { get; set; }

        /// <summary>
        /// Starts the execution pump and wires the controller's Methods.
        /// </summary>
        public void Start(ISystemContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_pumpTask != null)
            {
                if (context is global::Opc.Ua.Server.ServerSystemContext startedContext &&
                    startedContext.Server.SessionManager is { } startedSessionManager)
                {
                    AttachSessionManager(context, startedSessionManager);
                }
                return;
            }
            m_logger = context.Telemetry.CreateLogger<IntentControllerHost>();
            m_namespaceUris = context.NamespaceUris;
            m_intentsFolder = EnsureFolder(context, m_controller.Intents, BrowseNames.Intents);
            if (m_options.MissionsSupported)
            {
                m_missionsFolder = EnsureFolder(context, m_controller.Missions, BrowseNames.Missions);
            }
            ResolveCapabilities(context);
            BuildReferenceIndexes(context);
            if (m_options.RealTimeChannelsSupported)
            {
                CreateChannels(context);
            }
            PublishSafetyLocked(context);
            WireMethods(context);
            WireVariableReads(context);
            if (context is global::Opc.Ua.Server.ServerSystemContext serverContext &&
                serverContext.Server.SessionManager is { } sessionManager)
            {
                AttachSessionManager(context, sessionManager);
            }
            PublishControllerState(context);
            m_pumpTask = Task.Run(() => PumpAsync(context, m_shutdown.Token));
        }

        private void WireVariableReads(ISystemContext context)
        {
            if (m_options.SafetyStatusReader == null || m_controller.Ready == null)
            {
                return;
            }
            m_controller.Ready.OnReadValueAsync = async (
                readContext,
                variable,
                indexRange,
                dataEncoding,
                cancellationToken) =>
            {
                await RefreshSafetyStateAsync(context, cancellationToken).ConfigureAwait(false);
                Variant value;
                lock (m_lock)
                {
                    value = new Variant(IsReadyLocked());
                }
                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    readContext,
                    indexRange,
                    dataEncoding,
                    ref value);
                if (ServiceResult.IsBad(result))
                {
                    return new AttributeReadResult(result.StatusCode, value, result.StatusCode, DateTimeUtc.Now);
                }
                return new AttributeReadResult(StatusCodes.Good, value, StatusCodes.Good, DateTimeUtc.Now);
            };
        }

        /// <summary>
        /// Admits one intent, per OPC UA - Robot Intent clause 6.2.
        /// </summary>
        /// <remarks>
        /// The order of the checks is normative and is preserved here, because a
        /// caller that lacks authority must be told that rather than being told its
        /// parameters are wrong. A refusal creates no operation instance and moves
        /// nothing.
        /// </remarks>
        public IntentAdmission SubmitIntent(
            ISystemContext context,
            NodeId? sessionId,
            IntentDataType? intent,
            string missionId = "")
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_options.SafetyStatusReader != null)
            {
                return IntentAdmission.Refused(
                    IntentFailureEnum.NotPermittedInMode,
                    "A current safety snapshot is required before admission.");
            }
            return SubmitCore(
                context, sessionId, ClientNameOf(context), intent, missionId);
        }

        /// <summary>
        /// Admits one intent after refreshing the safety status, per OPC UA - Robot Intent clause 10.4.
        /// </summary>
        public async ValueTask<IntentAdmission> SubmitIntentAsync(
            ISystemContext context,
            NodeId? sessionId,
            IntentDataType? intent,
            string missionId = "",
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (intent != null && !m_disposed)
            {
                await RefreshSafetyStateAsync(context, cancellationToken).ConfigureAwait(false);
            }
            return SubmitCore(
                context, sessionId, ClientNameOf(context), intent, missionId);
        }

        /// <summary>
        /// Asks the Server to end an intent early.
        /// </summary>
        /// <remarks>
        /// This is NOT the OPC UA Cancel Service, which discards a pending service
        /// response and leaves the robot moving. The Server may refuse: some motions
        /// cannot be abandoned part-way without leaving the cell in a worse state than
        /// completing them.
        /// </remarks>
        public bool CancelIntent(
            ISystemContext context,
            NodeId? sessionId,
            string intentId,
            StopModeEnum stopMode = StopModeEnum.OnPath)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return false;
            }
            IntentExecution? execution = null;
            IntentEntry? activeEntry = null;
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (!m_intents.TryGetValue(intentId ?? string.Empty, out IntentEntry? entry) ||
                    IntentOutcome.IsTerminal(entry.State))
                {
                    return false;
                }
                if (!CapabilityPermitsCancelLocked(entry.Intent))
                {
                    return false;
                }
                if (entry == m_current)
                {
                    execution = entry.Execution;
                    activeEntry = entry;
                }
            }
            if (execution != null && !m_executor.CanCancel(execution))
            {
                return false;
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (!m_intents.TryGetValue(intentId ?? string.Empty, out IntentEntry? entry) ||
                    IntentOutcome.IsTerminal(entry.State) ||
                    !CapabilityPermitsCancelLocked(entry.Intent) ||
                    (activeEntry != null && !ReferenceEquals(entry, activeEntry)))
                {
                    return false;
                }
                return CancelLocked(context, entry, IntentFailureEnum.None, stopMode);
            }
        }

        /// <summary>
        /// Asks the Server to end every outstanding intent and mission.
        /// </summary>
        public uint CancelAll(
            ISystemContext context,
            NodeId? sessionId,
            StopModeEnum stopMode = StopModeEnum.OnPath)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return 0;
            }
            IntentEntry? activeEntry;
            IntentExecution? execution;
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return 0;
                }
                activeEntry = m_current;
                execution = activeEntry != null &&
                    !IntentOutcome.IsTerminal(activeEntry.State) &&
                    CapabilityPermitsCancelLocked(activeEntry.Intent)
                    ? activeEntry.Execution
                    : null;
            }
            bool activeCanCancel = execution == null || m_executor.CanCancel(execution);
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return 0;
                }
                uint count = 0;
                foreach (IntentEntry entry in m_intents.Values.ToList())
                {
                    if (!IntentOutcome.IsTerminal(entry.State) &&
                        CapabilityPermitsCancelLocked(entry.Intent) &&
                        (!ReferenceEquals(entry, activeEntry) || activeCanCancel) &&
                        CancelLocked(context, entry, IntentFailureEnum.None, stopMode))
                    {
                        count++;
                    }
                }
                foreach (MissionEntry mission in m_missions.Values.ToList())
                {
                    if (!IntentOutcome.IsTerminal(mission.State) &&
                        MissionCancellationAcceptedLocked(mission))
                    {
                        FinishMissionLocked(context, mission, ExecutionStateEnum.Cancelled);
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// Pauses queue dispatch. The executing intent keeps running because the
        /// executor interface has no pause acknowledgement channel.
        /// </summary>
        public bool Pause(ISystemContext context, NodeId? sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return false;
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (m_paused)
                {
                    return true;
                }
                m_paused = true;
                PublishControllerState(context);
                return true;
            }
        }

        /// <summary>
        /// Continues execution suspended by <see cref="Pause"/>.
        /// </summary>
        public bool Resume(ISystemContext context, NodeId? sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return false;
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (!m_paused)
                {
                    return false;
                }
                m_paused = false;
                PublishControllerState(context);
                m_pump.Release();
                return true;
            }
        }

        /// <summary>
        /// Re-attempts an intent that terminated Retriable.
        /// </summary>
        /// <remarks>
        /// The new attempt is a NEW operation instance. The original stays where it is,
        /// terminal, with its own result, so the history of what was tried survives.
        /// </remarks>
        public IntentAdmission Retry(ISystemContext context, NodeId? sessionId, string intentId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return IntentAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                    "The intent controller host is shutting down.");
            }
            IntentDataType? intent;
            string missionId;
            string retryIntentId;
            string baseIntentId;
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ControlNotOwned,
                        "The calling Session does not hold command authority.");
                }
                if (!IsSubmissionPermittedInMode())
                {
                    return IntentAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                        "Retry is accepted only in Automatic or AutomaticExternal mode.");
                }
                if (!m_intents.TryGetValue(intentId ?? string.Empty, out IntentEntry? entry))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        "No intent with that identifier terminated Retriable.");
                }
                if (!CapabilityPermitsRetryLocked(entry.Intent))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        "The declared capability does not support Retry for this intent type.");
                }
                if (entry.State != ExecutionStateEnum.Retriable)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        "No intent with that identifier terminated Retriable.");
                }
                intent = entry.Intent;
                missionId = entry.MissionId;
                baseIntentId = entry.BaseIntentId;
                retryIntentId = NextRetryIntentIdLocked(baseIntentId);
            }

            IntentDataType retryIntent = CloneIntent(intent!);
            retryIntent.IntentId = retryIntentId;
            return SubmitCore(
                context,
                sessionId,
                ClientNameOf(context),
                retryIntent,
                missionId,
                retryIntentId,
                baseIntentId);
        }

        /// <summary>
        /// Takes command authority.
        /// </summary>
        /// <remarks>
        /// This arbitrates between OPC UA clients so two of them cannot interleave
        /// motion. It is NOT the single point of control that ISO 10218-2 requires,
        /// which concerns remote command against local manual control and is enforced
        /// by safety-rated means outside this interface.
        /// </remarks>
        public bool RequestControl(ISystemContext context, NodeId? sessionId, out NodeId? owner)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (sessionId is not { IsNull: false } || m_disposed)
            {
                owner = ControlOwner;
                return false;
            }
            lock (m_lock)
            {
                if (ControlOwner == null || ControlOwner == sessionId)
                {
                    ControlOwner = sessionId;
                    m_logger?.AuthorityGranted(sessionId ?? NodeId.Null);
                    PublishControllerState(context);
                    owner = ControlOwner;
                    return true;
                }
                owner = ControlOwner;
                return false;
            }
        }

        /// <summary>
        /// Gives up command authority. Outstanding intents are unaffected.
        /// </summary>
        public void ReleaseControl(ISystemContext context, NodeId? sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return;
            }
            lock (m_lock)
            {
                if (HasAuthority(sessionId))
                {
                    ControlOwner = null;
                    m_logger?.AuthorityReleased(sessionId ?? NodeId.Null);
                    PublishControllerState(context);
                }
            }
        }

        /// <summary>
        /// Reports what the safety system is enforcing, and publishes it.
        /// </summary>
        /// <remarks>
        /// The application calls this; the host does not infer safety state. Admission
        /// then refuses on the same values a client can read, so the refusal is
        /// explainable from the address space rather than from Server-internal state.
        /// </remarks>
        public void UpdateSafetyState(ISystemContext context, SafetyStatus status)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }
            if (m_disposed)
            {
                return;
            }
            lock (m_lock)
            {
                m_safety = status;
                PublishSafetyLocked(context);
                PublishControllerState(context);
            }
        }

        /// <summary>
        /// Releases authority held by a Session that has closed.
        /// </summary>
        /// <remarks>
        /// Without this a crashed client locks the robot for good.
        /// </remarks>
        public void OnSessionClosed(ISystemContext context, NodeId sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return;
            }
            lock (m_lock)
            {
                if (HasAuthority(sessionId))
                {
                    ControlOwner = null;
                }
                ReleaseChannelsLocked(context, sessionId);
                PublishControllerState(context);
            }
        }

        /// <summary>
        /// Subscribes this host to the Server session lifetime notifications.
        /// </summary>
        public void AttachSessionManager(
            ISystemContext context, global::Opc.Ua.Server.ISessionManager sessionManager)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (sessionManager == null)
            {
                throw new ArgumentNullException(nameof(sessionManager));
            }
            if (m_disposed)
            {
                return;
            }
            if (m_sessionManager != null)
            {
                m_sessionManager.SessionClosing -= m_sessionClosingHandler;
            }
            m_sessionClosingHandler = (session, reason) => OnSessionClosed(context, session.Id);
            m_sessionManager = sessionManager;
            sessionManager.SessionClosing += m_sessionClosingHandler;
        }

        /// <summary>
        /// Takes a lease on a brokered real-time channel.
        /// </summary>
        /// <remarks>
        /// This hands over what a client needs in order to connect and nothing else.
        /// The samples travel on that channel; clause 4.3 explains why they cannot
        /// travel here. A lease that is not renewed lapses, so a client that dies does
        /// not hold the channel for good - the same reasoning as command authority.
        /// </remarks>
        public RealTimeLease OpenRealTimeChannel(
            ISystemContext context, NodeId? sessionId, string channelId, double requestedLeaseMs)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed || !m_options.RealTimeChannelsSupported)
            {
                return RealTimeLease.Refused("This Server brokers no real-time channels.");
            }
            if (sessionId is not { IsNull: false })
            {
                return RealTimeLease.Refused("A Session is required to hold a real-time channel lease.");
            }
            lock (m_lock)
            {
                ReapExpiredChannelsLocked(context);
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return RealTimeLease.Refused(
                        "The calling Session does not hold command authority.");
                }
                if (!m_channels.TryGetValue(channelId ?? string.Empty, out ChannelEntry? channel))
                {
                    return RealTimeLease.Refused("No channel with that identifier is offered.");
                }
                if (!channel.Available)
                {
                    return RealTimeLease.Refused("The channel is not available.");
                }
                if (channel.RequiredMode != m_options.OperationalMode)
                {
                    return RealTimeLease.Refused(
                        $"The channel requires {channel.RequiredMode} mode.");
                }
                bool held = channel.Leased && channel.Expiry > DateTime.UtcNow;
                if (held && channel.Holder != sessionId)
                {
                    return RealTimeLease.Refused("Another Session holds the lease.");
                }

                double lease = requestedLeaseMs > 0
                    ? Math.Min(requestedLeaseMs, m_options.MaxChannelLeaseMs)
                    : m_options.MaxChannelLeaseMs;
                channel.Holder = sessionId;
                channel.Leased = true;
                channel.Expiry = DateTime.UtcNow.AddMilliseconds(lease);
                PublishChannelLocked(context, channel);
                m_logger?.RealTimeLeaseGranted(channel.ChannelId, sessionId ?? NodeId.Null, channel.Expiry);
                return new RealTimeLease
                {
                    Granted = true,
                    EndpointUrl = channel.EndpointUrl,
                    PayloadDescriptor = channel.PayloadDescriptor,
                    Expiry = channel.Expiry
                };
            }
        }

        /// <summary>
        /// Gives up a lease on a brokered channel.
        /// </summary>
        public bool CloseRealTimeChannel(ISystemContext context, NodeId? sessionId, string channelId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return false;
            }
            lock (m_lock)
            {
                ReapExpiredChannelsLocked(context);
                if (!m_channels.TryGetValue(channelId ?? string.Empty, out ChannelEntry? channel) ||
                    !channel.Leased ||
                    channel.Holder != sessionId)
                {
                    return false;
                }
                channel.Holder = null;
                channel.Leased = false;
                channel.Expiry = DateTime.MinValue;
                PublishChannelLocked(context, channel);
                return true;
            }
        }

        /// <summary>
        /// Submits an ordered sequence of intents tracked as one unit.
        /// </summary>
        public MissionAdmission SubmitMission(
            ISystemContext context,
            NodeId? sessionId,
            MissionDataType? mission)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_options.SafetyStatusReader != null)
            {
                return MissionAdmission.Refused(
                    IntentFailureEnum.NotPermittedInMode,
                    "A current safety snapshot is required before admission.");
            }
            return SubmitMissionCore(context, sessionId, mission);
        }

        /// <summary>
        /// Submits a mission after refreshing the safety status, per OPC UA - Robot Intent clause 10.4.
        /// </summary>
        public async ValueTask<MissionAdmission> SubmitMissionAsync(
            ISystemContext context,
            NodeId? sessionId,
            MissionDataType? mission,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!m_disposed)
            {
                await RefreshSafetyStateAsync(context, cancellationToken).ConfigureAwait(false);
            }
            return SubmitMissionCore(context, sessionId, mission);
        }

        private MissionAdmission SubmitMissionCore(
            ISystemContext context,
            NodeId? sessionId,
            MissionDataType? mission)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return MissionAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                    "The intent controller host is shutting down.");
            }

            // Decided under the same acquisition that acts on it, for the reason given
            // in SubmitCore: authority and mode can both change between a check that
            // released the lock and the admission that follows it.
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ControlNotOwned,
                        "The calling Session does not hold command authority.");
                }
                if (!IsSubmissionPermittedInMode())
                {
                    return MissionAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                        "Missions are accepted only in Automatic or AutomaticExternal mode.");
                }
                if (!m_safety.PermitsSubmission)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                        m_safety.SafetyControllerOk
                            ? "A stop is asserted."
                            : "The safety controller reports a fault.");
                }
                if (!m_options.MissionsSupported)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        "This Server does not implement missions.");
                }
                if (mission == null || mission.Steps.IsNull || mission.Steps.IsEmpty)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        "A mission must carry at least one step.");
                }
                if (MissionExceedsSafeSpeedLocked(mission))
                {
                    return MissionAdmission.Refused(IntentFailureEnum.SafetyLimitExceeded,
                        FormattableString.Invariant(
                            $"The requested speed exceeds the enforced safe limit of {m_safety.SafeSpeedLimit} m/s."));
                }

                Check capability = ValidateMissionCapabilitiesLocked(context, mission);
                if (!capability.Ok)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        capability.Message ?? "The mission requires an unsupported capability.");
                }
                Check ordering = MissionRules.ValidateSteps(mission.Steps);
                if (!ordering.Ok)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        ordering.Message ?? "The mission steps are not valid.");
                }
                Check graph = MissionRules.ValidateTransitions(mission.Steps, mission.Transitions);
                if (!graph.Ok)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        graph.Message ?? "The mission graph is not valid.");
                }
                Check parameters = ValidateMissionParametersLocked(mission);
                if (!parameters.Ok)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        parameters.Message ?? "The mission contains an invalid intent.");
                }

                string id = mission.MissionId ?? string.Empty;
                if (string.IsNullOrEmpty(id))
                {
                    id = FormattableString.Invariant(
                        $"mission-{Interlocked.Increment(ref m_nextId)}");
                }
                else if (m_missions.TryGetValue(id, out MissionEntry? existing) &&
                    !IntentOutcome.IsTerminal(existing.State))
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        $"MissionId '{id}' is already outstanding.");
                }

                Check intentIds = PreflightStepIntentIdsLocked(mission, id);
                if (!intentIds.Ok)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        intentIds.Message ?? "A step IntentId is invalid.");
                }

                if (mission.Steps[0]?.Intent is { BufferMode: not BufferModeEnum.Aborting } &&
                    m_queue.Count >= m_options.MaxQueueDepth)
                {
                    return MissionAdmission.Refused(IntentFailureEnum.QueueFull,
                        "The queue is at MaxQueueDepth.");
                }

                var entry = new MissionEntry(id, mission, Interlocked.Increment(ref m_nextId));
                m_missions[id] = entry;
                CreateMissionNode(context, entry);
                m_missionHistory.Add(entry.Node!.NodeId, entry);
                SetMissionStateLocked(context, entry, ExecutionStateEnum.Executing);
                StartNextStepLocked(context, entry, sessionId);
                return MissionAdmission.Admitted(id, entry.Node!.NodeId);
            }
        }

        /// <summary>
        /// Replaces the horizon of a mission already submitted.
        /// </summary>
        /// <remarks>
        /// The base is untouchable. It has been committed and may already have
        /// executed, so an update that would alter a released step is refused rather
        /// than partly applied, and the whole update is applied atomically.
        /// </remarks>
        public MissionUpdateOutcome UpdateMission(
            ISystemContext context,
            NodeId? sessionId,
            string missionId,
            uint missionUpdateId,
            ArrayOf<MissionStepDataType> steps)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                    "The intent controller host is shutting down.");
            }
            if (!m_options.MissionHorizonSupported)
            {
                return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                    "This Server does not implement horizon updates.");
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                        "The calling Session does not hold command authority.");
                }
                if (!m_missions.TryGetValue(missionId ?? string.Empty, out MissionEntry? entry))
                {
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.UnknownMission,
                        "No mission with that identifier is held.");
                }
                if (missionUpdateId <= entry.Mission.MissionUpdateId)
                {
                    // Two updates that crossed in flight: the later one wins and the
                    // earlier is rejected rather than applied out of order.
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.Outdated,
                        "MissionUpdateId must be greater than the mission's current value.");
                }

                uint released = MissionRules.ReleasedCount(entry.Mission.Steps);
                Check conflict = MissionRules.ValidateBasePreserved(entry.Mission.Steps, steps);
                if (!conflict.Ok)
                {
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.BaseConflict,
                        conflict.Message ?? "The update would alter a released step.");
                }
                Check ordering = MissionRules.ValidateSteps(steps);
                if (!ordering.Ok)
                {
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                        ordering.Message ?? "The replacement steps are not valid.");
                }
                Check graph = MissionRules.ValidateTransitions(steps, entry.Mission.Transitions);
                if (!graph.Ok)
                {
                    return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                        graph.Message ?? "The mission graph is not valid.");
                }

                var reservedIds = new HashSet<string>(StringComparer.Ordinal);
                for (int ii = 0; ii < released; ii++)
                {
                    string baseId = entry.GetBaseIntentId(entry.Mission.Steps[ii], ii);
                    if (!string.IsNullOrEmpty(baseId))
                    {
                        reservedIds.Add(baseId);
                    }
                }
                Check intentIds = PreflightStepIntentIdsLocked(
                    steps,
                    entry.MissionId,
                    (int)released,
                    reservedIds);
                if (!intentIds.Ok)
                {
                    return new MissionUpdateOutcome(
                        MissionUpdateResultEnum.Rejected,
                        intentIds.Message ?? "A horizon step IntentId is invalid.");
                }

                var merged = new List<MissionStepDataType>(steps.Count);
                for (int ii = 0; ii < released; ii++)
                {
                    merged.Add(entry.Mission.Steps[ii]);
                }
                for (int ii = (int)released; ii < steps.Count; ii++)
                {
                    merged.Add(steps[ii]);
                }
                entry.ReplaceSteps([.. merged], (int)released);
                entry.Mission.MissionUpdateId = missionUpdateId;
                PublishMissionLocked(context, entry);
                return new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, null);
            }
        }

        /// <summary>
        /// Ends a mission and every intent belonging to it.
        /// </summary>
        public bool CancelMission(
            ISystemContext context,
            NodeId? sessionId,
            string missionId,
            StopModeEnum stopMode = StopModeEnum.OnPath)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_disposed)
            {
                return false;
            }
            IntentEntry? activeEntry;
            IntentExecution? execution;
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (!m_missions.TryGetValue(missionId ?? string.Empty, out MissionEntry? entry) ||
                    IntentOutcome.IsTerminal(entry.State))
                {
                    return false;
                }
                activeEntry = m_current;
                execution = activeEntry != null &&
                    activeEntry.MissionId == entry.MissionId &&
                    !IntentOutcome.IsTerminal(activeEntry.State) &&
                    CapabilityPermitsCancelLocked(activeEntry.Intent)
                    ? activeEntry.Execution
                    : null;
            }
            bool activeCanCancel = execution == null || m_executor.CanCancel(execution);
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return false;
                }
                if (!m_missions.TryGetValue(missionId ?? string.Empty, out MissionEntry? entry) ||
                    IntentOutcome.IsTerminal(entry.State))
                {
                    return false;
                }
                foreach (IntentEntry intent in m_intents.Values.ToList())
                {
                    if (intent.MissionId == entry.MissionId &&
                        !IntentOutcome.IsTerminal(intent.State) &&
                        (!CapabilityPermitsCancelLocked(intent.Intent) ||
                            (ReferenceEquals(intent, activeEntry) && !activeCanCancel)))
                    {
                        return false;
                    }
                }
                foreach (IntentEntry intent in m_intents.Values.ToList())
                {
                    if (intent.MissionId == entry.MissionId &&
                        !IntentOutcome.IsTerminal(intent.State))
                    {
                        CancelLocked(context, intent, IntentFailureEnum.None, stopMode);
                    }
                }
                FinishMissionLocked(context, entry, ExecutionStateEnum.Cancelled);
                return true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            UnhookSessionManager();
            m_shutdown.Cancel();
            IntentEntry[] entries = SnapshotIntents();
            foreach (IntentEntry entry in entries)
            {
                entry.RequestCancel(IntentFailureEnum.Other, StopModeEnum.QuickStop);
            }
            if (m_pumpTask == null || m_pumpTask.IsCompleted)
            {
                DisposeResources();
                return;
            }
            DeferDisposeResources(m_pumpTask);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// The clause 6.3 table, in code. A pairing not listed there is not legal, so
        /// this mapping is total and has no default arm that guesses.
        /// </summary>
        internal static uint MapToProgramState(ExecutionStateEnum state)
        {
            return state switch
            {
                ExecutionStateEnum.Accepted => StateReady,
                ExecutionStateEnum.Queued => StateReady,
                ExecutionStateEnum.Executing => StateRunning,
                ExecutionStateEnum.Cancelling => StateRunning,
                ExecutionStateEnum.Suspended => StateSuspended,
                ExecutionStateEnum.Succeeded => StateHalted,
                ExecutionStateEnum.Failed => StateHalted,
                ExecutionStateEnum.Cancelled => StateHalted,
                ExecutionStateEnum.Retriable => StateHalted,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state,
                    "ExecutionStateEnum has no clause 6.3 pairing.")
            };
        }

        private IntentAdmission SubmitCore(
            ISystemContext context,
            NodeId? sessionId,
            string clientName,
            IntentDataType? intent,
            string missionId,
            string? admittedIntentId = null,
            string? baseIntentId = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (intent == null)
            {
                var refused = IntentAdmission.Refused(
                    IntentFailureEnum.ParameterInvalid, "No intent was supplied.");
                m_logger?.IntentAdmissionRefused(string.Empty, refused.Failure, refused.Message ?? string.Empty);
                return refused;
            }
            if (m_disposed)
            {
                return IntentAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                    "The intent controller host is shutting down.");
            }

            // Everything below depends on state another thread can change: the safety
            // status, the control owner, the channel leases and the capability table.
            // Admission is decided and acted on under one acquisition, because a check
            // that releases the lock before it acts is a check the world can invalidate
            // in between - a stop asserted in that window would otherwise admit work
            // the Server had already been told to refuse.
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && !HasAuthority(sessionId))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ControlNotOwned,
                        "The calling Session does not hold command authority.");
                }
                if (!IsSubmissionPermittedInMode())
                {
                    return IntentAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                        "Intents are accepted only in Automatic or AutomaticExternal mode.");
                }
                if (!m_safety.PermitsSubmission)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.NotPermittedInMode,
                        m_safety.SafetyControllerOk
                            ? "A stop is asserted."
                            : "The safety controller reports a fault.");
                }
                if (intent is MotionIntentDataType &&
                    AnyChannelHeldLocked(context) &&
                    !m_options.ArbitratesWithRealTimeChannel)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        "A real-time channel lease is held and this Server does not " +
                        "arbitrate between the two command sources.");
                }
                if (ExceedsSafeSpeedLocked(intent))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.SafetyLimitExceeded,
                        FormattableString.Invariant(
                            $"The requested speed exceeds the enforced safe limit of {m_safety.SafeSpeedLimit} m/s."));
                }

                ClampMotionConstraints(intent);

                IntentCapabilityDataType? capability = FindCapabilityLocked(intent);
                if (capability == null)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        $"This Server does not accept {intent.GetType().Name}.");
                }
                if (!Permits(capability.SupportedBufferModes, intent.BufferMode))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        $"BufferMode {intent.BufferMode} is not accepted for this intent type.");
                }
                if (!Permits(capability.SupportedBlockingModes, intent.BlockingMode))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        $"BlockingMode {intent.BlockingMode} is not accepted for this intent type.");
                }
                if (ReferencesUnsupportedFastenJoint(intent))
                {
                    return IntentAdmission.Refused(
                        IntentFailureEnum.CapabilityNotSupported,
                        UnsupportedFastenJointMessage);
                }

                // Last, and deliberately so: a caller that holds no authority must not
                // learn from the answer whether its parameters would have been valid.
                Check validation = IntentValidation.Validate(intent, m_options);
                if (!validation.Ok)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        validation.Message ?? "The intent is not valid.");
                }
                Check scope = ValidateScopedReferencesLocked(intent);
                if (!scope.Ok)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        scope.Message ?? "The intent references an invalid node.");
                }

                string id = admittedIntentId ?? intent.IntentId ?? string.Empty;
                if (string.IsNullOrEmpty(id))
                {
                    id = FormattableString.Invariant(
                        $"intent-{Interlocked.Increment(ref m_nextId)}");
                }
                else if (m_intents.ContainsKey(id))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        $"IntentId '{id}' is already retained by another operation.");
                }

                if (intent.BufferMode != BufferModeEnum.Aborting &&
                    m_queue.Count >= m_options.MaxQueueDepth)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.QueueFull,
                        "The queue is at MaxQueueDepth.");
                }

                var entry = new IntentEntry(
                    id,
                    baseIntentId ?? id,
                    intent,
                    missionId,
                    Interlocked.Increment(ref m_nextAdmissionSequence))
                {
                    CreateSessionId = sessionId ?? NodeId.Null,
                    CreateClientName = clientName,
                    InvocationCreationTime = DateTime.UtcNow
                };
                m_intents[id] = entry;
                CreateOperationNode(context, entry);
                InitializeProgramDiagnostic(context, entry, "SubmitIntent", sessionId);
                m_logger?.IntentAdmitted(id, intent.GetType().Name);

                if (intent.BufferMode == BufferModeEnum.Aborting)
                {
                    SupersedeQueuedLocked(context, SupersededStopMode);
                    if (m_current is { } current &&
                        !IntentOutcome.IsTerminal(current.State))
                    {
                        SetExecutionStateLocked(context, current, ExecutionStateEnum.Cancelling);
                        current.RequestCancel(IntentFailureEnum.Superseded, SupersededStopMode);
                    }
                }
                m_queue.AddLast(entry);
                SetExecutionStateLocked(context, entry, ExecutionStateEnum.Accepted);
                RenumberQueueLocked(context);
                m_pump.Release();
                return IntentAdmission.Admitted(id, entry.Node!.NodeId);
            }
        }

        private bool CancelLocked(
            ISystemContext context,
            IntentEntry entry,
            IntentFailureEnum reason,
            StopModeEnum stopMode)
        {
            if (entry == m_current)
            {
                SetExecutionStateLocked(context, entry, ExecutionStateEnum.Cancelling);
                entry.RequestCancel(reason, stopMode);
                return true;
            }

            // Queued work has not started, so there is nothing to bring to a
            // controlled end: it goes straight to the terminal state.
            m_queue.Remove(entry);
            CompleteLocked(context, entry, new IntentOutcome
            {
                State = ExecutionStateEnum.Cancelled,
                Failure = reason
            });
            RenumberQueueLocked(context);
            return true;
        }

        private void SupersedeQueuedLocked(ISystemContext context, StopModeEnum stopMode)
        {
            while (m_queue.First is { } node)
            {
                node.Value.AcceptedStopMode = stopMode;
                m_queue.RemoveFirst();
                CompleteLocked(context, node.Value, new IntentOutcome
                {
                    State = ExecutionStateEnum.Cancelled,
                    Failure = IntentFailureEnum.Superseded,
                    Message = "Replaced by an Aborting submission."
                });
            }
        }

        private bool HasAuthority(NodeId? sessionId)
        {
            return ControlOwner != null &&
                sessionId is { IsNull: false } &&
                ControlOwner == sessionId;
        }

        /// <summary>
        /// Whether the intent asks to move faster than the safety system permits.
        /// </summary>
        /// <remarks>
        /// Only an explicit Cartesian speed can be compared: a speed FRACTION is of a
        /// configured maximum the host does not know, so it is left to the robot, which
        /// does. Refusing what cannot be judged would reject legitimate work.
        /// </remarks>
        private bool ExceedsSafeSpeedLocked(IntentDataType intent)
        {
            if (!m_safety.SafeSpeedLimitActive || m_safety.SafeSpeedLimit <= 0)
            {
                return false;
            }
            return intent is MotionIntentDataType motion &&
                motion.Constraints is { } constraints &&
                constraints.CartesianSpeed > m_safety.SafeSpeedLimit;
        }

        private void PublishSafetyLocked(ISystemContext context)
        {
            if (m_controller.SafetyState is not { } node)
            {
                return;
            }
            SetValue(node.ActiveFunction, m_safety.ActiveFunction);
            SetValue(node.EmergencyStopActive, m_safety.EmergencyStopActive);
            SetValue(node.ProtectiveStopActive, m_safety.ProtectiveStopActive);
            SetValue(node.SafeSpeedLimitActive, m_safety.SafeSpeedLimitActive);
            SetValue(node.SafeSpeedLimit, m_safety.SafeSpeedLimit);
            SetValue(node.SafetyControllerOk, m_safety.SafetyControllerOk);
            SetValue(node.LastStopReason, new LocalizedText(m_safety.LastStopReason ?? string.Empty));
            node.ClearChangeMasks(context, true);
        }

        private bool IsSubmissionPermittedInMode()
        {
            OperationalModeEnum mode = m_options.OperationalMode;
            return mode is OperationalModeEnum.Automatic or OperationalModeEnum.AutomaticExternal;
        }

        /// <summary>
        /// Resolves the declared capabilities against the Server's namespace table and
        /// publishes them, so the declaration a client reads is the one the host
        /// enforces rather than a parallel description of it.
        /// </summary>
        private void ResolveCapabilities(ISystemContext context)
        {
            m_capabilities.Clear();
            var published = new List<IntentCapabilityDataType>(m_options.Capabilities.Count);
            foreach (DeclaredCapability declared in m_options.Capabilities)
            {
                IntentCapabilityDataType resolved = declared.Resolve(context.NamespaceUris);
                if (resolved.IntentType.IsNull)
                {
                    continue;
                }
                m_capabilities[resolved.IntentType] = resolved;
                published.Add(resolved);
            }

            if (m_controller.Capabilities is { } capabilities)
            {
                SetValue(capabilities.SupportedIntents, new ArrayOf<IntentCapabilityDataType>(published.ToArray()));
                SetValue(capabilities.MissionsSupported, m_options.MissionsSupported);
                SetValue(capabilities.MissionHorizonSupported, m_options.MissionHorizonSupported);
                SetValue(capabilities.BlendingSupported, m_options.BlendingSupported);
                SetValue(capabilities.AxisCount, m_options.AxisCount);
                SetValue(capabilities.TrajectorySupported, m_options.TrajectorySupported);
                SetValue(capabilities.ForceControlSupported, m_options.ForceControlSupported);
                SetValue(capabilities.RealTimeChannelsSupported,
                    m_options.RealTimeChannelsSupported);
                SetValue(capabilities.MissionBranchingSupported,
                    m_options.MissionBranchingSupported);
                SetValue(capabilities.MaxTrajectoryPoints, m_options.MaxTrajectoryPoints);
                capabilities.ClearChangeMasks(context, true);
            }
        }

        private IntentCapabilityDataType? FindCapabilityLocked(IntentDataType intent)
        {
            NodeId? typeId = ExpandedNodeId.ToNodeId(intent.TypeId, m_namespaceUris);
            if (typeId is not { } resolved || resolved.IsNull)
            {
                return null;
            }
            return m_capabilities.TryGetValue(resolved, out IntentCapabilityDataType? capability)
                ? capability
                : null;
        }

        private bool CapabilityPermitsCancelLocked(IntentDataType intent)
        {
            return FindCapabilityLocked(intent)?.CancelSupported != false;
        }

        private bool CapabilityPermitsRetryLocked(IntentDataType intent)
        {
            return FindCapabilityLocked(intent)?.RetrySupported == true;
        }

        private static bool Permits<T>(ArrayOf<T> modes, T value) where T : struct, Enum
        {
            if (modes.IsNull || modes.IsEmpty)
            {
                return false;
            }
            for (int ii = 0; ii < modes.Count; ii++)
            {
                if (EqualityComparer<T>.Default.Equals(modes[ii], value))
                {
                    return true;
                }
            }
            return false;
        }

        private Check ValidateMissionCapabilitiesLocked(ISystemContext context, MissionDataType mission)
        {
            if (!mission.Transitions.IsNull)
            {
                for (int ii = 0; ii < mission.Transitions.Count; ii++)
                {
                    MissionTransitionDataType transition = mission.Transitions[ii];
                    if (transition?.DivergenceKind == DivergenceKindEnum.Parallel)
                    {
                        return Check.Fail(
                            "Parallel mission divergence is not supported by this serial host.");
                    }
                }
            }
            for (int ii = 0; ii < mission.Steps.Count; ii++)
            {
                IntentDataType? intent = mission.Steps[ii]?.Intent;
                if (intent == null)
                {
                    continue;
                }
                if (intent is MotionIntentDataType &&
                    AnyChannelHeldLocked(context) &&
                    !m_options.ArbitratesWithRealTimeChannel)
                {
                    return Check.Fail(
                        "A real-time channel lease is held and this Server does not " +
                        "arbitrate between the two command sources.");
                }
                IntentCapabilityDataType? capability = FindCapabilityLocked(intent);
                if (capability == null)
                {
                    return Check.Fail($"This Server does not accept {intent.GetType().Name}.");
                }
                if (!Permits(capability.SupportedBufferModes, intent.BufferMode))
                {
                    return Check.Fail($"BufferMode {intent.BufferMode} is not accepted for this intent type.");
                }
                if (!Permits(capability.SupportedBlockingModes, intent.BlockingMode))
                {
                    return Check.Fail($"BlockingMode {intent.BlockingMode} is not accepted for this intent type.");
                }
                if (ReferencesUnsupportedFastenJoint(intent))
                {
                    return Check.Fail(UnsupportedFastenJointMessage);
                }
            }
            return Check.Pass;
        }

        private bool MissionExceedsSafeSpeedLocked(MissionDataType mission)
        {
            for (int ii = 0; ii < mission.Steps.Count; ii++)
            {
                if (mission.Steps[ii]?.Intent is { } intent && ExceedsSafeSpeedLocked(intent))
                {
                    return true;
                }
            }
            return false;
        }

        private Check ValidateMissionParametersLocked(MissionDataType mission)
        {
            for (int ii = 0; ii < mission.Steps.Count; ii++)
            {
                IntentDataType? intent = mission.Steps[ii]?.Intent;
                if (intent == null)
                {
                    continue;
                }
                ClampMotionConstraints(intent);
                Check validation = IntentValidation.Validate(intent, m_options);
                if (!validation.Ok)
                {
                    return validation;
                }
                Check scope = ValidateScopedReferencesLocked(intent);
                if (!scope.Ok)
                {
                    return scope;
                }
            }
            return Check.Pass;
        }

        private bool MissionCancellationAcceptedLocked(MissionEntry mission)
        {
            foreach (IntentEntry intent in m_intents.Values)
            {
                if (intent.MissionId == mission.MissionId &&
                    !IntentOutcome.IsTerminal(intent.State) &&
                    !intent.CancelRequested)
                {
                    return false;
                }
            }
            return true;
        }

        private void ClampMotionConstraints(IntentDataType intent)
        {
            if (intent is not MotionIntentDataType motion || motion.Constraints == null)
            {
                return;
            }
            MotionConstraintsDataType constraints = motion.Constraints;
            constraints.SpeedFraction = ClampCeiling(constraints.SpeedFraction, m_options.MaxSpeedFraction);
            constraints.CartesianSpeed = ClampCeiling(constraints.CartesianSpeed, m_options.MaxCartesianSpeed);
            constraints.CartesianAcceleration = ClampCeiling(
                constraints.CartesianAcceleration, m_options.MaxCartesianAcceleration);
            constraints.Jerk = ClampCeiling(constraints.Jerk, m_options.MaxJerk);
        }

        private static double ClampCeiling(double value, double ceiling)
        {
            return ceiling > 0 && value > ceiling ? ceiling : value;
        }

        private Check ValidateScopedReferencesLocked(IntentDataType intent)
        {
            if (intent is MotionIntentDataType motion)
            {
                Check toolFrame = RequireNode(motion.ToolFrame, m_toolFrames, false, "ToolFrame");
                if (!toolFrame.Ok)
                {
                    return toolFrame;
                }
                if (motion is ForceIntentDataType force)
                {
                    Check frameId = RequireFrameId(force.FrameId, nameof(force.FrameId));
                    if (!frameId.Ok)
                    {
                        return frameId;
                    }
                }
            }

            if (intent is ProcessIntentDataType process)
            {
                Check processProgram = RequireNode(
                    process.ProcessProgram, m_programs, false, nameof(process.ProcessProgram));
                if (!processProgram.Ok)
                {
                    return processProgram;
                }
            }

            Check[] checks = intent switch
            {
                PickIntentDataType pick =>
                [
                    RequireNode(pick.Source, m_locations, true, nameof(pick.Source)),
                    RequireNode(pick.Tool, m_tools, false, nameof(pick.Tool))
                ],
                PlaceIntentDataType place =>
                [
                    RequireNode(place.Destination, m_locations, true, nameof(place.Destination)),
                    RequireNode(place.Tool, m_tools, false, nameof(place.Tool))
                ],
                PalletiseIntentDataType palletise =>
                [
                    RequireNode(palletise.Pattern, m_locations, false, nameof(palletise.Pattern))
                ],
                FastenIntentDataType fasten when !fasten.Joint.IsNull =>
                [
                    Check.Fail(UnsupportedFastenJointMessage)
                ],
                ToolChangeIntentDataType toolChange =>
                [
                    RequireNode(toolChange.Tool, m_tools, false, nameof(toolChange.Tool)),
                    RequireNode(toolChange.DockStation, m_locations, false, nameof(toolChange.DockStation))
                ],
                GraspIntentDataType grasp =>
                [
                    RequireNode(grasp.Tool, m_tools, false, nameof(grasp.Tool))
                ],
                ReleaseIntentDataType release =>
                [
                    RequireNode(release.Tool, m_tools, false, nameof(release.Tool))
                ],
                SetOutputIntentDataType output => [ValidateOutput(output)],
                CallProgramIntentDataType program =>
                [
                    RequireNode(program.Program, m_programs, true, nameof(program.Program))
                ],
                WaitIntentDataType wait =>
                [
                    RequireNode(wait.Signal, m_waitSignals, false, nameof(wait.Signal))
                ],
                _ => []
            };
            return FirstFailureOrPass(checks);
        }

        private static bool ReferencesUnsupportedFastenJoint(IntentDataType intent)
        {
            return intent is FastenIntentDataType fasten && !fasten.Joint.IsNull;
        }

        private static Check FirstFailureOrPass(IEnumerable<Check> checks)
        {
            foreach (Check check in checks)
            {
                if (!check.Ok)
                {
                    return check;
                }
            }
            return Check.Pass;
        }

        private Check ValidateOutput(SetOutputIntentDataType output)
        {
            Check node = RequireNode(output.Output, m_outputs, true, nameof(output.Output));
            if (!node.Ok)
            {
                return node;
            }
            if (!m_outputDataTypes.TryGetValue(output.Output, out NodeId dataType) ||
                dataType.IsNull ||
                output.Value.TypeInfo.IsUnknown ||
                !VariantMatchesDataType(output.Value, dataType))
            {
                return Check.Fail("SetOutput.Value does not match the OutputSignal Value DataType.");
            }
            return Check.Pass;
        }

        private static bool VariantMatchesDataType(Variant value, NodeId dataType)
        {
            BuiltInType builtInType = value.TypeInfo.BuiltInType;
            if (dataType == global::Opc.Ua.DataTypeIds.Boolean)
            {
                return builtInType == BuiltInType.Boolean;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.String)
            {
                return builtInType == BuiltInType.String;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.Double)
            {
                return builtInType == BuiltInType.Double;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.Float)
            {
                return builtInType == BuiltInType.Float;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.Int32)
            {
                return builtInType == BuiltInType.Int32;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.UInt32)
            {
                return builtInType == BuiltInType.UInt32;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.Int16)
            {
                return builtInType == BuiltInType.Int16;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.UInt16)
            {
                return builtInType == BuiltInType.UInt16;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.Byte)
            {
                return builtInType == BuiltInType.Byte;
            }
            if (dataType == global::Opc.Ua.DataTypeIds.SByte)
            {
                return builtInType == BuiltInType.SByte;
            }
            return true;
        }

        private Check RequireFrameId(string? frameId, string name)
        {
            string effective = frameId ?? string.Empty;
            return effective.Length == 0 || m_frameIds.Contains(effective)
                ? Check.Pass
                : Check.Fail($"{name} names an unknown CoordinateFrame FrameId.");
        }

        private static Check RequireNode(
            NodeId nodeId, HashSet<NodeId> index, bool required, string name)
        {
            if (nodeId.IsNull)
            {
                return required ? Check.Fail($"{name} is required.") : Check.Pass;
            }
            return index.Contains(nodeId)
                ? Check.Pass
                : Check.Fail($"{name} does not resolve to the required node type under this controller.");
        }

        private async Task PumpAsync(ISystemContext context, CancellationToken shutdown)
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    await m_pump.WaitAsync(shutdown).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                while (!shutdown.IsCancellationRequested)
                {
                    IntentEntry? next;
                    lock (m_lock)
                    {
                        if (m_paused || m_queue.First == null)
                        {
                            m_current = null;
                            PublishControllerState(context);
                            break;
                        }
                        next = m_queue.First.Value;
                        m_queue.RemoveFirst();
                        var progress = new ProgressSink(this, context, next);
                        next.Execution = new IntentExecution(
                            next.IntentId,
                            next.Intent,
                            progress,
                            m_controller.NodeId,
                            m_controller.BrowseName.Name ?? string.Empty)
                        {
                            MissionId = next.MissionId
                        };
                        if (next.CancelRequested)
                        {
                            next.Execution.AcceptCancellation(next.AcceptedStopMode);
                        }
                        m_current = next;
                        SetExecutionStateLocked(context, next, ExecutionStateEnum.Executing);
                        RenumberQueueLocked(context);
                        PublishControllerState(context);
                    }
#pragma warning disable CA1031 // the pump must remain observable even if host completion faults
                    try
                    {
                        await RunOneAsync(context, next!).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger?.IntentPumpFault(ex);
                        lock (m_lock)
                        {
                            m_pumpFaulted = true;
                            PublishControllerState(context);
                        }
                        return;
                    }
#pragma warning restore CA1031
                }
            }
        }

        private async Task RunOneAsync(ISystemContext context, IntentEntry entry)
        {
            IntentOutcome outcome;
            try
            {
                if (BeforeExecuteAsync != null)
                {
                    await BeforeExecuteAsync(entry.IntentId, entry.CancellationToken).ConfigureAwait(false);
                }
                outcome = await m_executor
                    .ExecuteAsync(entry.Execution!, entry.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                outcome = new IntentOutcome
                {
                    State = ExecutionStateEnum.Cancelled,
                    Failure = entry.CancelReason
                };
            }
#pragma warning disable CA1031 // an executor is application code; a fault must terminate
            catch (Exception ex)
            {
                m_logger?.IntentExecutorFault(ex, entry.IntentId);
                outcome = IntentOutcome.Fail(IntentFailureEnum.Other, ex.Message);
            }
#pragma warning restore CA1031

            if (entry.ToleranceFailure != null && outcome.State == ExecutionStateEnum.Succeeded)
            {
                outcome = entry.ToleranceFailure;
            }

            if (entry.CancelRequested && outcome.State != ExecutionStateEnum.Failed)
            {
                // A cancel was accepted, so the outcome is Cancelled however the
                // executor chose to return. An executor that failed on the way out
                // keeps its failure, which is more informative.
                outcome = outcome with
                {
                    State = ExecutionStateEnum.Cancelled,
                    Failure = outcome.Failure == IntentFailureEnum.None
                        ? entry.CancelReason
                        : outcome.Failure
                };
            }

#pragma warning disable CA1031 // completion includes application mission callbacks; keep the pump alive
            try
            {
                lock (m_lock)
                {
                    entry.ExecutionCompleted = true;
                    if (!IntentOutcome.IsTerminal(entry.State))
                    {
                        CompleteLocked(context, entry, outcome);
                    }
                    m_current = null;
                    PruneTerminalOperationsLocked();
                    PublishControllerState(context);
                }
            }
            catch (Exception ex)
            {
                m_logger?.IntentPumpFault(ex);
                lock (m_lock)
                {
                    m_pumpFaulted = true;
                    if (!IntentOutcome.IsTerminal(entry.State))
                    {
                        CompleteLocked(context, entry, IntentOutcome.Fail(IntentFailureEnum.Other, ex.Message));
                    }
                    m_current = null;
                    PublishControllerState(context);
                }
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Whether any channel lease is currently held.
        /// </summary>
        /// <remarks>
        /// Clause 6.9 forbids accepting motion intents alongside a held channel unless
        /// the host can genuinely arbitrate: two things commanding one robot with no
        /// arbitration is the failure that rule exists to prevent.
        /// </remarks>
        private bool AnyChannelHeldLocked(ISystemContext context)
        {
            ReapExpiredChannelsLocked(context);
            foreach (ChannelEntry channel in m_channels.Values)
            {
                if (channel.Leased)
                {
                    return true;
                }
            }
            return false;
        }

        private void ReapExpiredChannelsLocked(ISystemContext context)
        {
            DateTime now = DateTime.UtcNow;
            foreach (ChannelEntry channel in m_channels.Values)
            {
                if (channel.Leased && channel.Expiry <= now)
                {
                    channel.Holder = null;
                    channel.Leased = false;
                    channel.Expiry = DateTime.MinValue;
                    PublishChannelLocked(context, channel);
                    m_logger?.RealTimeLeaseExpired(channel.ChannelId);
                }
            }
        }

        private void ReleaseChannelsLocked(ISystemContext context, NodeId? sessionId)
        {
            foreach (ChannelEntry channel in m_channels.Values)
            {
                if (channel.Leased && channel.Holder == sessionId)
                {
                    channel.Holder = null;
                    channel.Leased = false;
                    channel.Expiry = DateTime.MinValue;
                    PublishChannelLocked(context, channel);
                }
            }
        }

        /// <summary>
        /// Materialises the declared channels so a client can browse and read them
        /// before it asks for a lease.
        /// </summary>
        private void BuildReferenceIndexes(ISystemContext context)
        {
            m_locations.Clear();
            m_tools.Clear();
            m_frames.Clear();
            m_toolFrames.Clear();
            m_outputs.Clear();
            m_waitSignals.Clear();
            m_programs.Clear();
            m_outputDataTypes.Clear();
            m_frameIds.Clear();
            IndexFolder<LocationState>(context, m_controller.Locations, m_locations);
            IndexFolder<ToolState>(context, m_controller.Tools, m_tools);
            IndexFolder<ProgramState>(context, m_controller.Programs, m_programs);
            IndexFrames(context, m_controller.Frames);
            IndexOutputs(context, m_controller.Outputs);
        }

        private void IndexFolder<T>(ISystemContext context, NodeState? folder, HashSet<NodeId> index)
            where T : BaseInstanceState
        {
            if (folder == null)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is T && !child.NodeId.IsNull)
                {
                    index.Add(child.NodeId);
                }
                IndexFolder<T>(context, child, index);
            }
        }

        private void IndexFrames(ISystemContext context, NodeState? folder)
        {
            if (folder == null)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is CoordinateFrameState frame)
                {
                    m_frames.Add(frame.NodeId);
                    if (frame.Role?.Value == FrameRoleEnum.Tool)
                    {
                        m_toolFrames.Add(frame.NodeId);
                    }
                    if (!string.IsNullOrEmpty(frame.FrameId?.Value))
                    {
                        m_frameIds.Add(frame.FrameId.Value);
                    }
                }
                IndexFrames(context, child);
            }
        }

        private void IndexOutputs(ISystemContext context, NodeState? folder)
        {
            if (folder == null)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is OutputSignalState output)
                {
                    m_outputs.Add(output.NodeId);
                    m_waitSignals.Add(output.NodeId);
                    if (output.Value != null && !output.Value.DataType.IsNull)
                    {
                        m_outputDataTypes[output.NodeId] = output.Value.DataType;
                    }
                    continue;
                }
                else if (child is BaseDataVariableState variable &&
                    variable.DataType == global::Opc.Ua.DataTypeIds.Boolean)
                {
                    m_waitSignals.Add(variable.NodeId);
                }
                IndexOutputs(context, child);
            }
        }

        private void CreateChannels(ISystemContext context)
        {
            FolderState folder = EnsureFolder(
                context, m_controller.RealTimeChannels, BrowseNames.RealTimeChannels);
            foreach (DeclaredChannel declared in m_options.Channels)
            {
                var node = new RealTimeChannelState(folder)
                {
                    NodeId = ChildNodeId(folder.NodeId, declared.ChannelId),
                    BrowseName = new QualifiedName(
                        declared.ChannelId, folder.BrowseName.NamespaceIndex),
                    DisplayName = new LocalizedText(declared.ChannelId),
                    SymbolicName = declared.ChannelId,
                    ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = ExpandedNodeId.ToNodeId(
                        ObjectTypeIds.RealTimeChannelType, context.NamespaceUris)
                };
                node.Create(context, node.NodeId, node.BrowseName, node.DisplayName, false);
                node.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, folder.NodeId);
                folder.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, false, node.NodeId);

                SetValue(node.ChannelId, declared.ChannelId);
                SetValue(node.Transport, declared.Transport);
                SetValue(node.EndpointUrl, declared.EndpointUrl);
                SetValue(node.Initiator, declared.Initiator);
                SetValue(node.NominalRate, declared.NominalRate);
                SetValue(node.PayloadDescriptor, declared.PayloadDescriptor);
                SetValue(node.RequiredMode, declared.RequiredMode);

                var entry = new ChannelEntry
                {
                    ChannelId = declared.ChannelId,
                    EndpointUrl = declared.EndpointUrl,
                    PayloadDescriptor = declared.PayloadDescriptor,
                    RequiredMode = declared.RequiredMode,
                    Node = node
                };
                m_channels[declared.ChannelId] = entry;
                PublishChannelLocked(context, entry);
                AddNode(node);
            }
        }

        private void PublishChannelLocked(ISystemContext context, ChannelEntry channel)
        {
            if (channel.Node is not { } node)
            {
                return;
            }
            SetValue(node.LeaseHolder, channel.Holder ?? global::Opc.Ua.NodeId.Null);
            SetValue(node.LeaseExpiry, channel.Expiry);
            SetValue(node.Available, channel.Available);
            node.ClearChangeMasks(context, true);
        }

        private Check PreflightStepIntentIdsLocked(MissionDataType mission, string missionId)
        {
            return PreflightStepIntentIdsLocked(
                mission.Steps,
                missionId,
                startIndex: 0,
                reservedIds: null);
        }

        private Check PreflightStepIntentIdsLocked(
            ArrayOf<MissionStepDataType> steps,
            string missionId,
            int startIndex,
            HashSet<string>? reservedIds)
        {
            var assigned = reservedIds == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(reservedIds, StringComparer.Ordinal);
            var generatedIds = new string[steps.Count];

            for (int ii = startIndex; ii < steps.Count; ii++)
            {
                MissionStepDataType step = steps[ii];
                IntentDataType? intent = step?.Intent;
                if (intent == null)
                {
                    continue;
                }

                string suppliedId = intent.IntentId ?? string.Empty;
                if (!string.IsNullOrEmpty(suppliedId))
                {
                    if (string.IsNullOrWhiteSpace(suppliedId))
                    {
                        return Check.Fail(
                            $"Step '{step!.StepId}' has a whitespace-only IntentId.");
                    }
                    if (!assigned.Add(suppliedId))
                    {
                        return Check.Fail(
                            $"IntentId '{suppliedId}' appears on more than one step.");
                    }
                    if (m_intents.ContainsKey(suppliedId))
                    {
                        return Check.Fail(
                            $"IntentId '{suppliedId}' collides with a retained operation.");
                    }
                }
                else
                {
                    string stepId = step!.StepId ?? string.Empty;
                    string generatedId = string.IsNullOrEmpty(stepId)
                        ? FormattableString.Invariant($"{missionId}/step-{ii}")
                        : FormattableString.Invariant($"{missionId}/{stepId}");
                    if (assigned.Contains(generatedId))
                    {
                        return Check.Fail(
                            $"Generated IntentId '{generatedId}' appears on more than one step.");
                    }
                    generatedId = NextAvailableGeneratedIntentIdLocked(generatedId, assigned);
                    assigned.Add(generatedId);
                    generatedIds[ii] = generatedId;
                }
            }

            for (int ii = startIndex; ii < steps.Count; ii++)
            {
                if (!string.IsNullOrEmpty(generatedIds[ii]) &&
                    steps[ii]?.Intent is { } generatedIntent)
                {
                    generatedIntent.IntentId = generatedIds[ii];
                }
            }

            return Check.Pass;
        }

        private string NextAvailableGeneratedIntentIdLocked(
            string baseIntentId,
            HashSet<string> assigned)
        {
            if (!assigned.Contains(baseIntentId) && !m_intents.ContainsKey(baseIntentId))
            {
                return baseIntentId;
            }

            for (int run = 2; ; run++)
            {
                string candidate = FormattableString.Invariant($"{baseIntentId}#run-{run}");
                if (!assigned.Contains(candidate) && !m_intents.ContainsKey(candidate))
                {
                    return candidate;
                }
            }
        }

        private string NextRetryIntentIdLocked(string baseIntentId)
        {
            for (int attempt = 2; ; attempt++)
            {
                string candidate = FormattableString.Invariant($"{baseIntentId}#attempt-{attempt}");
                if (!m_intents.ContainsKey(candidate))
                {
                    return candidate;
                }
            }
        }

        private static IntentDataType CloneIntent(IntentDataType intent)
        {
            if (intent.Clone() is IntentDataType clone)
            {
                return clone;
            }
            throw new InvalidOperationException(
                $"Intent type '{intent.GetType().FullName}' did not clone as {nameof(IntentDataType)}.");
        }

        private MissionAdvanceResult StartNextStepLocked(
            ISystemContext context,
            MissionEntry mission,
            NodeId? sessionId)
        {
            MissionStepDataType? step =
                MissionRules.NextPending(mission.Mission.Steps, mission.NextIndex);
            if (step == null)
            {
                FinishMissionLocked(context, mission, ExecutionStateEnum.Succeeded);
                return MissionAdvanceResult.Exhausted;
            }

            mission.CurrentStepId = step.StepId ?? string.Empty;
            IntentDataType? stepIntent = step.Intent;
            if (stepIntent == null)
            {
                FinishMissionLocked(
                    context,
                    mission,
                    ExecutionStateEnum.Failed,
                    IntentFailureEnum.ParameterInvalid,
                    $"Mission step '{mission.CurrentStepId}' has no intent.");
                return MissionAdvanceResult.Refused;
            }

            string baseId = mission.GetBaseIntentId(step, mission.NextIndex);
            int attempt = mission.IncrementAttempt(mission.CurrentStepId);
            string admittedId = attempt == 1
                ? baseId
                : FormattableString.Invariant($"{baseId}#attempt-{attempt}");
            mission.CurrentIntentId = admittedId;
            stepIntent.IntentId = admittedId;
            IntentDataType admittedIntent = CloneIntent(stepIntent);

            IntentAdmission admission = SubmitCore(
                context,
                sessionId,
                ClientNameOf(context),
                admittedIntent,
                mission.MissionId,
                admittedId,
                baseId);
            if (!admission.Accepted)
            {
                FinishMissionLocked(
                    context,
                    mission,
                    ExecutionStateEnum.Failed,
                    admission.Failure,
                    admission.Message ?? "A mission step was refused.");
                return MissionAdvanceResult.Refused;
            }

            mission.CurrentIntentId = admission.IntentId;
            stepIntent.IntentId = admission.IntentId;
            if (m_intents.TryGetValue(admission.IntentId, out IntentEntry? entry))
            {
                MissionRules.SetStatus(
                    mission.Mission.Steps,
                    mission.NextIndex,
                    entry.State,
                    entry.Node?.NodeId);
            }
            PublishMissionLocked(context, mission);
            return MissionAdvanceResult.Started;
        }

        private void AdvanceMissionLocked(ISystemContext context, IntentEntry entry, IntentOutcome outcome)
        {
            if (string.IsNullOrEmpty(entry.MissionId) ||
                !m_missions.TryGetValue(entry.MissionId, out MissionEntry? mission) ||
                IntentOutcome.IsTerminal(mission.State))
            {
                return;
            }
            if (mission.CurrentIntentId != entry.IntentId)
            {
                return;
            }

            MissionRules.SetStatus(mission.Mission.Steps, mission.NextIndex, outcome.State,
                entry.Node?.NodeId);

            if (outcome.State == ExecutionStateEnum.Succeeded)
            {
                if (mission.Compensating)
                {
                    // The compensation ran; the mission still ends, because that is
                    // what distinguishes Compensate from Fallback.
                    FinishMissionLocked(
                        context, mission, ExecutionStateEnum.Failed,
                        IntentFailureEnum.Other,
                        "Compensation completed; the mission is still failed.");
                    return;
                }
                mission.RetriesUsed = 0;
                if (AdvanceToNextStepLocked(context, mission) == MissionAdvanceResult.Exhausted)
                {
                    FinishMissionLocked(context, mission, ExecutionStateEnum.Succeeded);
                }
                return;
            }

            if (outcome.State == ExecutionStateEnum.Cancelled)
            {
                FinishMissionLocked(context, mission, ExecutionStateEnum.Cancelled);
                return;
            }

            ApplyErrorPolicyLocked(context, mission, outcome);
        }

        /// <summary>
        /// Chooses the step that follows one that succeeded.
        /// </summary>
        /// <remarks>
        /// Where the mission carries a step graph and this host evaluates it, the graph
        /// decides; otherwise the steps run in order, which is what a mission without
        /// transitions has always done.
        /// </remarks>
        private MissionAdvanceResult AdvanceToNextStepLocked(ISystemContext context, MissionEntry mission)
        {
            ArrayOf<MissionTransitionDataType> transitions = mission.Mission.Transitions;
            bool graphed = m_options.MissionBranchingSupported &&
                !transitions.IsNull &&
                !transitions.IsEmpty;

            if (graphed)
            {
                string fromStepId = mission.CurrentStepId;
                MissionTransitionSelection selection = SelectTransitionUnlocked(transitions, fromStepId);
                if (IntentOutcome.IsTerminal(mission.State) ||
                    !string.Equals(mission.CurrentStepId, fromStepId, StringComparison.Ordinal))
                {
                    return MissionAdvanceResult.Refused;
                }
                if (selection.Transition == null)
                {
                    if (selection.HasOutgoingTransitions)
                    {
                        FinishMissionLocked(
                            context,
                            mission,
                            ExecutionStateEnum.Failed,
                            IntentFailureEnum.NoTransition);
                    }
                    else
                    {
                        FinishMissionLocked(context, mission, ExecutionStateEnum.Succeeded);
                    }
                    return MissionAdvanceResult.Refused;
                }
                int next = MissionRules.IndexOfStep(
                    mission.Mission.Steps,
                    selection.Transition.ToStepId ?? string.Empty);
                if (next < 0)
                {
                    FinishMissionLocked(
                        context,
                        mission,
                        ExecutionStateEnum.Failed,
                        IntentFailureEnum.NoTransition);
                    return MissionAdvanceResult.Refused;
                }
                mission.NextIndex = next;
            }
            else
            {
                mission.NextIndex++;
            }
            return StartNextStepLocked(context, mission, ControlOwner);
        }

        private MissionTransitionSelection SelectTransitionUnlocked(
            ArrayOf<MissionTransitionDataType> transitions,
            string fromStepId)
        {
            m_lock.Exit();
            try
            {
                return MissionRules.SelectTransition(transitions, fromStepId, m_options.EvaluateCondition);
            }
            finally
            {
                m_lock.Enter();
            }
        }

        /// <summary>
        /// Applies a failed step's error policy, per clause 7.4.
        /// </summary>
        private void ApplyErrorPolicyLocked(
            ISystemContext context,
            MissionEntry mission,
            IntentOutcome stepOutcome)
        {
            MissionStepDataType? step =
                MissionRules.NextPending(mission.Mission.Steps, mission.NextIndex);
            ErrorPolicyEnum policy = step?.ErrorPolicy ?? ErrorPolicyEnum.Abort;

            switch (policy)
            {
                case ErrorPolicyEnum.Retry:
                    if (mission.RetriesUsed < m_options.MaxStepRetries)
                    {
                        mission.RetriesUsed++;
                        StartNextStepLocked(context, mission, ControlOwner);
                        return;
                    }
                    FinishMissionLocked(
                        context, mission, ExecutionStateEnum.Failed,
                        stepOutcome.Failure,
                        stepOutcome.Message ?? "Retries exhausted.");
                    return;
                case ErrorPolicyEnum.Skip:
                    mission.RetriesUsed = 0;
                    if (AdvanceToNextStepLocked(context, mission) == MissionAdvanceResult.Exhausted)
                    {
                        FinishMissionLocked(context, mission, ExecutionStateEnum.Succeeded);
                    }
                    return;
                case ErrorPolicyEnum.Fallback:
                case ErrorPolicyEnum.Compensate:
                    int target = MissionRules.IndexOfStep(
                        mission.Mission.Steps, step?.FallbackStepId ?? string.Empty);
                    if (target < 0)
                    {
                        FinishMissionLocked(
                            context, mission, ExecutionStateEnum.Failed,
                            stepOutcome.Failure,
                            stepOutcome.Message ?? "Fallback step not found.");
                        return;
                    }
                    mission.RetriesUsed = 0;
                    mission.Compensating = policy == ErrorPolicyEnum.Compensate;
                    mission.NextIndex = target;
                    StartNextStepLocked(context, mission, ControlOwner);
                    return;
                default:
                    FinishMissionLocked(
                        context, mission, ExecutionStateEnum.Failed,
                        stepOutcome.Failure,
                        stepOutcome.Message ?? "The step failed.");
                    return;
            }
        }

        private void FinishMissionLocked(
            ISystemContext context,
            MissionEntry mission,
            ExecutionStateEnum state,
            IntentFailureEnum failure = IntentFailureEnum.None,
            string failureMessage = "")
        {
            if (IntentOutcome.IsTerminal(mission.State))
            {
                return;
            }
            if (state == ExecutionStateEnum.Failed && failure == IntentFailureEnum.None)
            {
                failure = IntentFailureEnum.Other;
                if (string.IsNullOrWhiteSpace(failureMessage))
                {
                    failureMessage = "The mission failed without a specific failure classification.";
                }
            }
            mission.Failure = state == ExecutionStateEnum.Failed ? failure : IntentFailureEnum.None;
            mission.FailureMessage = state == ExecutionStateEnum.Failed
                ? failureMessage ?? string.Empty
                : string.Empty;
            if (!string.IsNullOrEmpty(mission.CurrentStepId) &&
                mission.NextIndex >= 0 &&
                mission.NextIndex < mission.Mission.Steps.Count &&
                mission.Mission.Steps[mission.NextIndex] is { } currentStep &&
                !IntentOutcome.IsTerminal(currentStep.Status))
            {
                NodeId? operation = m_intents.TryGetValue(mission.CurrentIntentId, out IntentEntry? entry)
                    ? entry.Node?.NodeId
                    : null;
                MissionRules.SetStatus(mission.Mission.Steps, mission.NextIndex, state, operation);
            }
            mission.CurrentStepId = string.Empty;
            mission.CurrentIntentId = string.Empty;
            PublishMissionFinalResultLocked(context, mission);
            PublishMissionLocked(context, mission);
            SetMissionStateLocked(context, mission, state);
            PruneTerminalMissionsLocked();
        }

        private void CreateOperationNode(ISystemContext context, IntentEntry entry)
        {
            FolderState folder = m_intentsFolder
                ?? throw new InvalidOperationException("The controller has no Intents folder.");
            var node = new IntentOperationState(folder)
            {
                NodeId = ChildNodeId(folder.NodeId, entry.OperationNodeName),
                BrowseName = new QualifiedName(entry.IntentId, m_controller.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(entry.IntentId),
                SymbolicName = entry.IntentId,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.IntentOperationType, context.NamespaceUris),
                EventNotifier = global::Opc.Ua.EventNotifiers.SubscribeToEvents
            };
            node.Create(context, node.NodeId, node.BrowseName, node.DisplayName, false);
            node.AddProgress(context);
            node.AddQueuePosition(context);
            node.AddCurrentPose(context);
            EnsureFinalResultVariable(context, node).Value = Variant.Null;
            node.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, folder.NodeId);
            folder.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, false, node.NodeId);

            SetValue(node.IntentId, entry.IntentId);
            SetValue(node.Intent, entry.Intent);
            SetValue(node.MissionId, entry.MissionId);
            SetValue(node.Progress, -1.0);
            SetValue(node.QueuePosition, (uint)0);
            SetValue(node.Deletable, true);
            SetValue(node.AutoDelete, false);
            SetValue(node.RecycleCount, 0);
            node.SetState(context, StateReady);

            entry.Node = node;
            AddNode(node);
        }

        private void CreateMissionNode(ISystemContext context, MissionEntry entry)
        {
            FolderState folder = m_missionsFolder
                ?? throw new InvalidOperationException("The controller has no Missions folder.");
            var node = new MissionObjectState(folder)
            {
                NodeId = ChildNodeId(folder.NodeId, entry.MissionNodeName),
                BrowseName = new QualifiedName(entry.MissionId, m_controller.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(entry.MissionId),
                SymbolicName = entry.MissionId,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.MissionType, context.NamespaceUris),
                EventNotifier = global::Opc.Ua.EventNotifiers.SubscribeToEvents
            };
            node.Create(context, node.NodeId, node.BrowseName, node.DisplayName, false);
            EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentResultDataType.Failure),
                DataTypeIds.IntentFailureEnum).Value = Variant.From((int)IntentFailureEnum.None);
            EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentResultDataType.Message),
                global::Opc.Ua.DataTypeIds.LocalizedText).Value = Variant.From(LocalizedText.Null);
            node.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, folder.NodeId);
            folder.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, false, node.NodeId);

            SetValue(node.MissionId, entry.MissionId);
            SetValue(node.Deletable, true);
            SetValue(node.AutoDelete, false);
            SetValue(node.RecycleCount, 0);
            node.SetState(context, StateReady);

            entry.Node = node;
            AddNode(node);
            PublishMissionLocked(context, entry);
        }

        private void SetExecutionStateLocked(
            ISystemContext context, IntentEntry entry, ExecutionStateEnum state)
        {
            ExecutionStateEnum previous = entry.State;
            entry.State = state;
            PublishExecutionStateLocked(context, entry, previous, state);
            UpdateMissionStepStateLocked(context, entry, state);
        }

        private void UpdateMissionStepStateLocked(
            ISystemContext context,
            IntentEntry entry,
            ExecutionStateEnum state)
        {
            if (string.IsNullOrEmpty(entry.MissionId) ||
                !m_missions.TryGetValue(entry.MissionId, out MissionEntry? mission) ||
                mission.CurrentIntentId != entry.IntentId)
            {
                return;
            }
            MissionRules.SetStatus(
                mission.Mission.Steps,
                mission.NextIndex,
                state,
                entry.Node?.NodeId);
            PublishMissionLocked(context, mission);
        }

        private void PublishExecutionStateLocked(
            ISystemContext context,
            IntentEntry entry,
            ExecutionStateEnum previous,
            ExecutionStateEnum state)
        {
            if (entry.Node is not { } node)
            {
                return;
            }
            SetValue(node.ExecutionState, state);
            DriveProgramState(context, node, previous, state);
            UpdateProgramDiagnosticTransition(context, node, DateTime.UtcNow);
            node.ClearChangeMasks(context, true);
            m_logger?.IntentStateTransition(entry.IntentId, previous, state);
            if (IntentOutcome.IsTerminal(state))
            {
                PruneTerminalOperationsLocked();
            }
        }

        private static void DriveProgramState(
            ISystemContext context,
            ProgramStateMachineState node,
            ExecutionStateEnum previous,
            ExecutionStateEnum state)
        {
            uint from = MapToProgramState(previous);
            uint to = MapToProgramState(state);
            if (from == to)
            {
                node.SetState(context, to);
                return;
            }
            uint transition = ProgramTransition(from, to);
            if (transition == 0 || ServiceResult.IsBad(node.DoTransition(context, transition, 0, [], [])))
            {
                node.SetState(context, to);
            }
        }

        private static uint ProgramTransition(uint from, uint to)
        {
            if (from == StateHalted && to == StateReady)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_HaltedToReady;
            }
            if (from == StateReady && to == StateRunning)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_ReadyToRunning;
            }
            if (from == StateRunning && to == StateHalted)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_RunningToHalted;
            }
            if (from == StateRunning && to == StateReady)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_RunningToReady;
            }
            if (from == StateRunning && to == StateSuspended)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_RunningToSuspended;
            }
            if (from == StateSuspended && to == StateRunning)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToRunning;
            }
            if (from == StateSuspended && to == StateHalted)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToHalted;
            }
            if (from == StateSuspended && to == StateReady)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToReady;
            }
            if (from == StateReady && to == StateHalted)
            {
                return global::Opc.Ua.Objects.ProgramStateMachineType_ReadyToHalted;
            }
            return 0;
        }

        /// <summary>
        /// Drops the oldest terminal operations once more than
        /// <see cref="IntentControllerHostOptions.RetainedTerminalOperations"/> have
        /// accrued, so a controller that runs for months does not accumulate an
        /// operation node for every intent it has ever been given.
        /// <para>
        /// Only terminal operations are considered: one still queued, executing or
        /// suspended is going to change again and a client watching it would lose the
        /// rest of the story. Removal is best effort - a host that cannot remove the
        /// node keeps the entry rather than leaving a dictionary that disagrees with
        /// the address space.
        /// </para>
        /// </summary>
        private void PruneTerminalOperationsLocked()
        {
            uint keep = m_options.RetainedTerminalOperations;
            if (keep == 0)
            {
                return;
            }
            var terminal = m_intents
                .Where(kv => IntentOutcome.IsTerminal(kv.Value.State) &&
                    !ReferenceEquals(kv.Value, m_current) &&
                    (kv.Value.Execution == null || kv.Value.ExecutionCompleted))
                .OrderBy(kv => kv.Value.AdmissionSequence)
                .ToList();
            for (int i = 0; i < terminal.Count - (int)keep; i++)
            {
                KeyValuePair<string, IntentEntry> victim = terminal[i];
                if (m_removeNode != null && victim.Value.Node is { } stale)
                {
                    RemoveNode(stale);
                }
                m_intents.Remove(victim.Key);
                victim.Value.Dispose();
            }
        }

        private void PruneTerminalMissionsLocked()
        {
            uint keep = m_options.RetainedTerminalMissions;
            if (keep == 0)
            {
                return;
            }
            var terminal = m_missionHistory
                .Where(kv => IntentOutcome.IsTerminal(kv.Value.State))
                .OrderBy(kv => kv.Value.AdmissionSequence)
                .ToList();
            for (int i = 0; i < terminal.Count - (int)keep; i++)
            {
                KeyValuePair<NodeId, MissionEntry> victim = terminal[i];
                if (m_removeNode != null && victim.Value.Node is { } stale)
                {
                    RemoveNode(stale);
                }
                m_missionHistory.Remove(victim.Key);
                if (m_missions.TryGetValue(victim.Value.MissionId, out MissionEntry? current) &&
                    ReferenceEquals(current, victim.Value))
                {
                    m_missions.Remove(victim.Value.MissionId);
                }
            }
        }

        /// <summary>
        /// Retires a per-invocation node. Mirrors <see cref="AddNode"/>: the removal
        /// usually completes synchronously, and when it does not the task is observed
        /// so a failure surfaces rather than leaving a node nothing will ever clean up.
        /// </summary>
        private void RemoveNode(NodeState node)
        {
            ValueTask task = m_removeNode!(node, CancellationToken.None);
            if (task.IsCompletedSuccessfully)
            {
                return;
            }
            _ = task.AsTask().ContinueWith(
                t => NodeRemoveFailed?.Invoke(this, new IntentNodeAddFailure(node, t.Exception!)),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void SetMissionStateLocked(
            ISystemContext context, MissionEntry entry, ExecutionStateEnum state)
        {
            ExecutionStateEnum previous = entry.State;
            entry.State = state;
            if (entry.Node is not { } node)
            {
                return;
            }
            SetValue(node.ExecutionState, state);
            DriveProgramState(context, node, previous, state);
            UpdateProgramDiagnosticTransition(context, node, DateTime.UtcNow);
            node.ClearChangeMasks(context, true);
        }

        private void CompleteLocked(ISystemContext context, IntentEntry entry, IntentOutcome outcome)
        {
            if (!IntentOutcome.IsTerminal(outcome.State))
            {
                outcome = IntentOutcome.Fail(
                    IntentFailureEnum.Other,
                    $"Executor returned non-terminal outcome {outcome.State}.");
            }
            else if (outcome.State == ExecutionStateEnum.Failed &&
                outcome.Failure == IntentFailureEnum.None)
            {
                outcome = outcome with
                {
                    Failure = IntentFailureEnum.Other,
                    Message = string.IsNullOrWhiteSpace(outcome.Message)
                        ? "Executor reported failure without a failure classification."
                        : outcome.Message
                };
            }

            ExecutionStateEnum previous = entry.State;
            entry.State = outcome.State;
            PublishTerminalResultLocked(context, entry, outcome);
            PublishExecutionStateLocked(context, entry, previous, entry.State);
#pragma warning disable CA1031 // mission callbacks are application code; keep operation result immutable
            try
            {
                AdvanceMissionLocked(context, entry, outcome);
            }
            catch (Exception ex)
            {
                m_logger?.IntentPumpFault(ex);
                if (!string.IsNullOrEmpty(entry.MissionId) &&
                    m_missions.TryGetValue(entry.MissionId, out MissionEntry? mission) &&
                    !IntentOutcome.IsTerminal(mission.State))
                {
                    FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                }
            }
#pragma warning restore CA1031
        }

        private void PublishTerminalResultLocked(
            ISystemContext context,
            IntentEntry entry,
            IntentOutcome outcome)
        {
            var result = new IntentResultDataType
            {
                IntentId = entry.IntentId,
                State = outcome.State,
                Failure = outcome.Failure,
                Message = new LocalizedText(outcome.Message ?? string.Empty),
                HasAchievedPose = outcome.AchievedPose != null,
                AchievedPose = outcome.AchievedPose ?? new Pose3DDataType(),
                StartTime = entry.StartTime,
                EndTime = DateTime.UtcNow,
                Outputs = outcome.Outputs
            };
            entry.Result = result;
            // The result is published BEFORE the state goes terminal. A client watching
            // the state machine acts the moment it sees a terminal state, and would
            // otherwise read a result that is not there yet.
            if (entry.Node is { } node)
            {
                SetValue(node.Result, result);
                SetValue(node.QueuePosition, (uint)0);
                node.Result?.ClearChangeMasks(context, true);
                node.QueuePosition?.ClearChangeMasks(context, true);
                PublishFinalResult(context, node, result);
                node.ClearChangeMasks(context, true);
            }
        }

        /// <summary>
        /// Places the result under the inherited FinalResultData object as well, so a
        /// client written against Part 10 finds it where Part 10 says it will be.
        /// </summary>
        private static void PublishFinalResult(
            ISystemContext context, IntentOperationState node, IntentResultDataType result)
        {
            var value = new Variant(new ExtensionObject(result));
            BaseDataVariableState existing = EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentOperationState.Result),
                DataTypeIds.IntentResultDataType);
            existing.Value = value;
            existing.ClearChangeMasks(context, false);
        }

        private static void PublishMissionFinalResultLocked(ISystemContext context, MissionEntry entry)
        {
            if (entry.Node is not { } node)
            {
                return;
            }
            BaseDataVariableState failure = EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentResultDataType.Failure),
                DataTypeIds.IntentFailureEnum);
            failure.Value = Variant.From((int)entry.Failure);
            failure.ClearChangeMasks(context, false);
            BaseDataVariableState message = EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentResultDataType.Message),
                global::Opc.Ua.DataTypeIds.LocalizedText);
            message.Value = Variant.From(new LocalizedText(entry.FailureMessage));
            message.ClearChangeMasks(context, false);
            node.ClearChangeMasks(context, true);
        }

        private static BaseDataVariableState EnsureFinalResultVariable(
            ISystemContext context,
            ProgramStateMachineState node,
            string browseNameText,
            ExpandedNodeId dataType)
        {
            BaseObjectState final = node.FinalResultData
                ?? node.CreateOrReplaceFinalResultData(context, null);
            var browseName = new QualifiedName(browseNameText, node.BrowseName.NamespaceIndex);
            if (final.FindChild(context, browseName) is BaseDataVariableState existing)
            {
                return existing;
            }
            var variable = new BaseDataVariableState(final)
            {
                NodeId = ChildNodeId(final.NodeId, browseName.Name ?? "Result"),
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name ?? "Result"),
                SymbolicName = browseName.Name ?? "Result",
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType,
                DataType = ExpandedNodeId.ToNodeId(dataType, context.NamespaceUris),
                ValueRank = global::Opc.Ua.ValueRanks.Scalar
            };
            final.AddChild(variable);
            return variable;
        }

        private static BaseDataVariableState EnsureFinalResultVariable(
            ISystemContext context, IntentOperationState node)
        {
            return EnsureFinalResultVariable(
                context,
                node,
                nameof(IntentOperationState.Result),
                DataTypeIds.IntentResultDataType);
        }

        private void RenumberQueueLocked(ISystemContext context)
        {
            uint position = 1;
            foreach (IntentEntry entry in m_queue)
            {
                if (entry.State != ExecutionStateEnum.Queued)
                {
                    SetExecutionStateLocked(context, entry, ExecutionStateEnum.Queued);
                }
                if (entry.Node is { } node)
                {
                    SetValue(node.QueuePosition, position);
                    node.ClearChangeMasks(context, false);
                }
                position++;
            }
        }

        private void PublishMissionLocked(ISystemContext context, MissionEntry entry)
        {
            if (entry.Node is not { } node)
            {
                return;
            }
            SetValue(node.Mission, entry.Mission);
            SetValue(node.MissionUpdateId, entry.Mission.MissionUpdateId);
            SetValue(node.CurrentStepId, entry.CurrentStepId);
            SetValue(node.ReleasedStepCount, MissionRules.ReleasedCount(entry.Mission.Steps));
            node.ClearChangeMasks(context, true);
        }

        private void PublishControllerState(ISystemContext context)
        {
            SetValue(m_controller.OperationalMode, m_options.OperationalMode);
            SetValue(m_controller.Ready, IsReadyLocked());
            SetValue(m_controller.ControlOwner, ControlOwner ?? global::Opc.Ua.NodeId.Null);
            SetValue(m_controller.MaxQueueDepth, m_options.MaxQueueDepth);
            SetValue(m_controller.ActiveIntent, m_current?.Node?.NodeId ?? NodeId.Null);
            SetActiveMission(context, ActiveMissionNodeId());
            m_controller.ClearChangeMasks(context, true);
        }

        private bool IsReadyLocked()
        {
            return IsSubmissionPermittedInMode() &&
                !m_paused &&
                !m_pumpFaulted &&
                m_safety.PermitsSubmission;
        }

        private async ValueTask RefreshSafetyStateAsync(
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            if (m_options.SafetyStatusReader == null)
            {
                return;
            }
            SafetyStatus status = await m_options.SafetyStatusReader(cancellationToken).ConfigureAwait(false);
            UpdateSafetyState(context, status);
        }

        private NodeId ActiveMissionNodeId()
        {
            if (m_current == null ||
                string.IsNullOrEmpty(m_current.MissionId) ||
                !m_missions.TryGetValue(m_current.MissionId, out MissionEntry? mission))
            {
                return NodeId.Null;
            }
            return mission.Node?.NodeId ?? NodeId.Null;
        }

        private void SetActiveMission(ISystemContext context, NodeId value)
        {
            var browseName = new QualifiedName("ActiveMission", m_controller.BrowseName.NamespaceIndex);
            if (m_controller.FindChild(context, browseName) is BaseDataVariableState<NodeId> typed)
            {
                SetValue(typed, value);
                typed.ClearChangeMasks(context, false);
                return;
            }
            if (m_controller.FindChild(context, browseName) is PropertyState<NodeId> property)
            {
                SetValue(property, value);
                property.ClearChangeMasks(context, false);
                return;
            }
            if (m_controller.FindChild(context, browseName) is BaseDataVariableState variable)
            {
                variable.Value = value;
                variable.ClearChangeMasks(context, false);
            }
        }

        /// <summary>
        /// PropertyState&lt;T&gt; and BaseDataVariableState&lt;T&gt; derive from different
        /// non-generic bases, so there is no one generic type to constrain on.
        /// </summary>
        /// <typeparam name="T">
        /// The value type carried by the variable.
        /// </typeparam>
        /// <param name="variable">
        /// The variable to write, or null when the variable is absent.
        /// </param>
        /// <param name="value">
        /// The value to write.
        /// </param>
        private static void SetValue<T>(PropertyState<T>? variable, T value)
        {
            if (variable != null)
            {
                variable.Value = value;
            }
        }

        private static void SetValue<T>(BaseDataVariableState<T>? variable, T value)
        {
            if (variable != null)
            {
                variable.Value = value;
            }
        }

        /// <summary>
        /// Returns the declared folder, creating it when the type declared it Optional
        /// and it was not materialised. A Server that implements a facet exposes its
        /// optional members; leaving them absent would make the facet unclaimable.
        /// </summary>
        private FolderState EnsureFolder(ISystemContext context, FolderState? declared, string browseName)
        {
            if (declared != null)
            {
                return declared;
            }
            ushort riNs = RobotIntentNamespaceIndex(context);
            var folder = new FolderState(m_controller)
            {
                NodeId = ChildNodeId(m_controller.NodeId, browseName),
                BrowseName = new QualifiedName(browseName, riNs),
                DisplayName = new LocalizedText(browseName),
                SymbolicName = browseName,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.FolderType,
                EventNotifier = global::Opc.Ua.EventNotifiers.None
            };
            m_controller.AddChild(folder);
            folder.AddReference(
                global::Opc.Ua.ReferenceTypeIds.HasComponent, true, m_controller.NodeId);
            m_controller.AddReference(
                global::Opc.Ua.ReferenceTypeIds.HasComponent, false, folder.NodeId);
            AddNode(folder);
            return folder;
        }

        private static ushort RobotIntentNamespaceIndex(ISystemContext context)
        {
            int idx = context.NamespaceUris.GetIndex(Namespaces.RobotIntent);
            return idx < 0 ? (ushort)0 : (ushort)idx;
        }

        /// <summary>
        /// Publishes a per-invocation node. The add usually completes synchronously;
        /// when it does not, the task is observed so a failure surfaces instead of
        /// leaving a node the client can never browse and no word about why.
        /// </summary>
        private void AddNode(NodeState node)
        {
            ValueTask task = m_addNode(node, CancellationToken.None);
            if (task.IsCompletedSuccessfully)
            {
                return;
            }
            _ = task.AsTask().ContinueWith(
                t => NodeAddFailed?.Invoke(this, new IntentNodeAddFailure(node, t.Exception!)),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static NodeId ChildNodeId(NodeId parent, string name)
        {
            return new NodeId($"{parent.IdentifierAsString}_{name}", parent.NamespaceIndex);
        }

        private void WireMethods(ISystemContext context)
        {
            SetMethodExecutable(m_controller.RequestControl);
            SetMethodExecutable(m_controller.ReleaseControl);
            SetMethodExecutable(m_controller.SubmitIntent);
            SetMethodExecutable(m_controller.CancelIntent);
            SetMethodExecutable(m_controller.CancelAll);
            SetMethodExecutable(m_controller.Pause);
            SetMethodExecutable(m_controller.Resume);
            SetMethodExecutable(m_controller.Retry);
            SetMethodExecutable(m_controller.SubmitMission);
            SetMethodExecutable(m_controller.UpdateMission);
            SetMethodExecutable(m_controller.CancelMission);
            SetMethodExecutable(m_controller.OpenRealTimeChannel);
            SetMethodExecutable(m_controller.CloseRealTimeChannel);

            if (m_controller.RequestControl is { } requestControl)
            {
                requestControl.OnCallAsync = (ctx, method, objectId, ct) =>
                {
                    bool granted = RequestControl(context, SessionOf(ctx), out NodeId? owner);
                    return new ValueTask<RequestControlMethodStateResult>(new RequestControlMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Granted = granted,
                        CurrentOwner = owner ?? global::Opc.Ua.NodeId.Null
                    });
                };
            }
            if (m_controller.ReleaseControl is { } releaseControl)
            {
                releaseControl.OnCallMethod2Async = (ctx, method, objectId, inputArguments, outputArguments, ct) =>
                {
                    ReleaseControl(context, SessionOf(ctx));
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                };
            }
            if (m_controller.SubmitIntent is { } submit)
            {
                submit.OnCallAsync = async (ctx, method, objectId, intent, ct) =>
                {
                    await RefreshSafetyStateAsync(context, ct).ConfigureAwait(false);
                    IntentAdmission admission = SubmitCore(
                        context, SessionOf(ctx), ClientNameOf(ctx), intent, string.Empty);
                    return ToSubmitIntentResult(admission);
                };
            }
            if (m_controller.CancelIntent is { } cancelIntent)
            {
                cancelIntent.OnCallAsync = (ctx, method, objectId, intentId, stopMode, ct) =>
                    new ValueTask<CancelIntentMethodStateResult>(new CancelIntentMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = CancelIntent(context, SessionOf(ctx), intentId, stopMode)
                    });
            }
            if (m_controller.CancelAll is { } cancelAll)
            {
                cancelAll.OnCallAsync = (ctx, method, objectId, stopMode, ct) =>
                    new ValueTask<CancelAllMethodStateResult>(new CancelAllMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Cancelled = CancelAll(context, SessionOf(ctx), stopMode)
                    });
            }
            if (m_controller.Pause is { } pause)
            {
                pause.OnCallAsync = (ctx, method, objectId, ct) =>
                    new ValueTask<PauseMethodStateResult>(new PauseMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = Pause(context, SessionOf(ctx))
                    });
            }
            if (m_controller.Resume is { } resume)
            {
                resume.OnCallAsync = (ctx, method, objectId, ct) =>
                    new ValueTask<ResumeMethodStateResult>(new ResumeMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = Resume(context, SessionOf(ctx))
                    });
            }
            if (m_controller.Retry is { } retry)
            {
                retry.OnCallAsync = (ctx, method, objectId, intentId, ct) =>
                {
                    IntentAdmission admission = Retry(context, SessionOf(ctx), intentId);
                    return new ValueTask<RetryMethodStateResult>(new RetryMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = admission.Accepted,
                        Operation = admission.Operation,
                        Failure = admission.Failure,
                        Message = new LocalizedText(admission.Message ?? string.Empty)
                    });
                };
            }
            if (m_controller.SubmitMission is { } submitMission)
            {
                submitMission.OnCallAsync = async (ctx, method, objectId, mission, ct) =>
                {
                    MissionAdmission admission = await SubmitMissionAsync(
                        context,
                        SessionOf(ctx),
                        mission,
                        ct).ConfigureAwait(false);
                    return new SubmitMissionMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = admission.Accepted,
                        MissionId = admission.MissionId,
                        Operation = admission.Operation,
                        Failure = admission.Failure,
                        Message = new LocalizedText(admission.Message ?? string.Empty)
                    };
                };
            }
            if (m_controller.UpdateMission is { } updateMission)
            {
                updateMission.OnCallAsync = (ctx, method, objectId, missionId, updateId, steps, ct) =>
                {
                    MissionUpdateOutcome outcome = UpdateMission(context, SessionOf(ctx), missionId, updateId, steps);
                    return new ValueTask<UpdateMissionMethodStateResult>(new UpdateMissionMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Result = outcome.Result,
                        Message = new LocalizedText(outcome.Message ?? string.Empty)
                    });
                };
            }
            if (m_controller.CancelMission is { } cancelMission)
            {
                cancelMission.OnCallAsync = (ctx, method, objectId, missionId, stopMode, ct) =>
                    new ValueTask<CancelMissionMethodStateResult>(new CancelMissionMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Accepted = CancelMission(context, SessionOf(ctx), missionId, stopMode)
                    });
            }
            if (m_controller.OpenRealTimeChannel is { } openChannel)
            {
                openChannel.OnCallAsync = (ctx, method, objectId, channelId, requestedLease, ct) =>
                {
                    RealTimeLease lease = OpenRealTimeChannel(
                        context, SessionOf(ctx), channelId, requestedLease);
                    return new ValueTask<OpenRealTimeChannelMethodStateResult>(
                        new OpenRealTimeChannelMethodStateResult
                        {
                            ServiceResult = ServiceResult.Good,
                            Granted = lease.Granted,
                            EndpointUrl = lease.EndpointUrl,
                            PayloadDescriptor = lease.PayloadDescriptor,
                            LeaseExpiry = lease.Expiry,
                            Message = new LocalizedText(lease.Message ?? string.Empty)
                        });
                };
            }
            if (m_controller.CloseRealTimeChannel is { } closeChannel)
            {
                closeChannel.OnCallAsync = (ctx, method, objectId, channelId, ct) =>
                    new ValueTask<CloseRealTimeChannelMethodStateResult>(
                        new CloseRealTimeChannelMethodStateResult
                        {
                            ServiceResult = ServiceResult.Good,
                            Released = CloseRealTimeChannel(context, SessionOf(ctx), channelId)
                        });
            }
        }

        private static SubmitIntentMethodStateResult ToSubmitIntentResult(IntentAdmission admission)
        {
            return new SubmitIntentMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Accepted = admission.Accepted,
                IntentId = admission.IntentId,
                Operation = admission.Operation,
                Failure = admission.Failure,
                Message = new LocalizedText(admission.Message ?? string.Empty)
            };
        }

        private static void SetMethodExecutable(MethodState? method)
        {
            if (method != null)
            {
                method.Executable = true;
                method.UserExecutable = true;
            }
        }

        private void InitializeProgramDiagnostic(
            ISystemContext context,
            IntentEntry entry,
            string methodName,
            NodeId? sessionId)
        {
            if (entry.Node is not { } node)
            {
                return;
            }
            ProgramDiagnostic2State? diagnostic = node.ProgramDiagnostic;
            if (diagnostic == null)
            {
                node.AddProgramDiagnostic(context);
                diagnostic = node.ProgramDiagnostic;
            }
            if (diagnostic == null)
            {
                return;
            }
            SetValue(diagnostic.CreateSessionId, entry.CreateSessionId);
            SetValue(diagnostic.CreateClientName, entry.CreateClientName);
            SetValue(diagnostic.InvocationCreationTime, entry.InvocationCreationTime);
            UpdateProgramDiagnosticMethod(
                context,
                node,
                methodName,
                sessionId ?? NodeId.Null,
                ArrayOf.Create([new Variant(new ExtensionObject(entry.Intent))]),
                [],
                StatusCodes.Good);
            diagnostic.ClearChangeMasks(context, true);
        }

        private static void UpdateProgramDiagnosticTransition(
            ISystemContext context,
            ProgramStateMachineState node,
            DateTime transitionTime)
        {
            if (node.ProgramDiagnostic is { } diagnostic)
            {
                SetValue(diagnostic.LastTransitionTime, transitionTime);
                diagnostic.ClearChangeMasks(context, true);
            }
        }

        private static void UpdateProgramDiagnosticMethod(
            ISystemContext context,
            ProgramStateMachineState node,
            string methodName,
            NodeId sessionId,
            ArrayOf<Variant> inputValues,
            ArrayOf<Variant> outputValues,
            StatusCode statusCode)
        {
            if (node.ProgramDiagnostic is not { } diagnostic)
            {
                return;
            }
            SetValue(diagnostic.LastMethodCall, methodName);
            SetValue(diagnostic.LastMethodSessionId, sessionId);
            SetValue(diagnostic.LastMethodInputArguments, []);
            SetValue(diagnostic.LastMethodOutputArguments, []);
            SetValue(diagnostic.LastMethodInputValues, inputValues);
            SetValue(diagnostic.LastMethodOutputValues, outputValues);
            SetValue(diagnostic.LastMethodCallTime, DateTime.UtcNow);
            SetValue(diagnostic.LastMethodReturnStatus, statusCode);
            diagnostic.ClearChangeMasks(context, true);
        }

        private void ApplyTrajectoryDeviation(
            ISystemContext context,
            IntentEntry entry,
            double pathPositionDeviation,
            double goalPositionDeviation,
            double elapsedMilliseconds,
            bool final)
        {
            if (entry.Intent is not TrajectoryIntentDataType trajectory ||
                trajectory.Points.IsNull ||
                trajectory.Points.IsEmpty)
            {
                return;
            }
            double pathTolerance = EffectiveTolerance(
                trajectory.PathTolerance?.Position ?? 0, m_options.DefaultPathTolerance);
            double goalTolerance = EffectiveTolerance(
                trajectory.GoalTolerance?.Position ?? 0, m_options.DefaultGoalTolerance);
            double goalTimeTolerance = EffectiveTolerance(
                trajectory.GoalTimeTolerance, m_options.DefaultGoalTimeTolerance);
            double finalTime = trajectory.Points[^1].TimeFromStart;
            if (finalTime > 0)
            {
                entry.Execution?.Progress.ReportProgress(Math.Min(1.0, elapsedMilliseconds / finalTime));
            }
            if (!final && pathTolerance > 0 && pathPositionDeviation > pathTolerance)
            {
                entry.ToleranceFailure ??= IntentOutcome.Fail(
                    IntentFailureEnum.Kinematics,
                    "Trajectory path tolerance was exceeded.") with
                {
                    Outputs = ToleranceOutputs(pathTolerance, goalTolerance, goalTimeTolerance)
                };
                return;
            }
            if (final && goalTolerance > 0 && goalPositionDeviation > goalTolerance)
            {
                entry.ToleranceFailure ??= IntentOutcome.Fail(
                    IntentFailureEnum.Kinematics,
                    "Trajectory goal tolerance was exceeded.") with
                {
                    Outputs = ToleranceOutputs(pathTolerance, goalTolerance, goalTimeTolerance)
                };
                return;
            }
            if (final && goalTimeTolerance > 0 && elapsedMilliseconds > finalTime + goalTimeTolerance)
            {
                entry.ToleranceFailure ??= IntentOutcome.Fail(
                    IntentFailureEnum.Timeout,
                    "Trajectory goal time tolerance was exceeded.") with
                {
                    Outputs = ToleranceOutputs(pathTolerance, goalTolerance, goalTimeTolerance)
                };
            }
            _ = context;
        }

        private void CompleteAtBlendPoint(
            ISystemContext context,
            IntentEntry entry,
            Pose3DDataType pose)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(entry, m_current) || IntentOutcome.IsTerminal(entry.State))
                {
                    return;
                }
                CompleteLocked(context, entry, IntentOutcome.SucceededAt(pose));
            }
        }

        private static ArrayOf<KeyValuePair> ToleranceOutputs(
            double pathTolerance,
            double goalTolerance,
            double goalTimeTolerance)
        {
            return ArrayOf.Create([
                new KeyValuePair { Key = new QualifiedName("PathTolerance"), Value = new Variant(pathTolerance) },
                new KeyValuePair { Key = new QualifiedName("GoalTolerance"), Value = new Variant(goalTolerance) },
                new KeyValuePair
                {
                    Key = new QualifiedName("GoalTimeTolerance"),
                    Value = new Variant(goalTimeTolerance)
                }
            ]);
        }

        private static double EffectiveTolerance(double requested, double fallback)
        {
            return requested > 0 ? requested : fallback;
        }

        private static string ClientNameOf(ISystemContext? context)
        {
            if (context is SessionSystemContext
                {
                    OperationContext: Opc.Ua.Server.OperationContext operation
                } &&
                operation.Session != null)
            {
                return operation.Session.SessionName;
            }
            return string.Empty;
        }

        /// <summary>
        /// The Session behind a Method call, which is what command authority is held
        /// by. A context without one is an internal call, and holds no authority.
        /// </summary>
        private static NodeId? SessionOf(ISystemContext? context)
        {
            if (context is SessionSystemContext
                {
                    OperationContext: Opc.Ua.Server.OperationContext operation
                })
            {
                return operation.SessionId;
            }
            return null;
        }

        private void UnhookSessionManager()
        {
            if (m_sessionManager != null && m_sessionClosingHandler != null)
            {
                m_sessionManager.SessionClosing -= m_sessionClosingHandler;
            }
            m_sessionManager = null;
            m_sessionClosingHandler = null;
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref m_resourcesDisposed, 1) != 0)
            {
                return;
            }
            Volatile.Write(ref m_shutdownDeferred, 0);
            m_shutdown.Cancel();
            m_shutdown.Dispose();
            m_pump.Dispose();
            IntentEntry[] entries = SnapshotIntents();
            foreach (IntentEntry entry in entries)
            {
                entry.Dispose();
            }
        }

        private async ValueTask DisposeAsyncCore()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            UnhookSessionManager();
            Task? pumpTask = m_pumpTask;
            m_shutdown.Cancel();
            IntentEntry[] entries = SnapshotIntents();
            await CancelEntriesForDisposeAsync(entries).ConfigureAwait(false);
            if (pumpTask != null)
            {
                try
                {
                    if (!await WaitForPumpShutdownAsync(pumpTask).ConfigureAwait(false))
                    {
                        DeferDisposeResources(pumpTask);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }
            DisposeResources();
        }

        private async ValueTask CancelEntriesForDisposeAsync(IntentEntry[] entries)
        {
            foreach (IntentEntry entry in entries)
            {
                if (BeforeDisposeCancelAsync != null)
                {
                    await BeforeDisposeCancelAsync(entry.IntentId).ConfigureAwait(false);
                }
                entry.RequestCancel(IntentFailureEnum.Other, StopModeEnum.QuickStop);
            }
        }

        private IntentEntry[] SnapshotIntents()
        {
            lock (m_lock)
            {
                return [.. m_intents.Values];
            }
        }

        private void DeferDisposeResources(Task pumpTask)
        {
            Volatile.Write(ref m_shutdownDeferred, 1);
            _ = pumpTask.ContinueWith(
                static (_, state) => ((IntentControllerHost)state!).DisposeResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task<bool> WaitForPumpShutdownAsync(Task pumpTask)
        {
            double timeoutMs = m_options.ExecutorShutdownTimeoutMs;
            if (timeoutMs <= 0)
            {
                return pumpTask.IsCompleted;
            }
            Task delay = Task.Delay(TimeSpan.FromMilliseconds(timeoutMs));
            Task completed = await Task.WhenAny(pumpTask, delay).ConfigureAwait(false);
            if (!ReferenceEquals(completed, pumpTask))
            {
                m_logger?.IntentControllerShutdownTimedOut(timeoutMs);
                return false;
            }
            await pumpTask.ConfigureAwait(false);
            return true;
        }

        private const uint StateReady = 1;
        private const uint StateRunning = 2;
        private const uint StateSuspended = 3;
        private const uint StateHalted = 4;
        private const StopModeEnum SupersededStopMode = StopModeEnum.QuickStop;
        private const string UnsupportedFastenJointMessage =
            "FastenIntent.Joint references from OPC 40450/40451 are not supported by this controller model; " +
            "omit Joint and provide the fastening parameters directly.";

        private readonly Lock m_lock = new();
        private readonly IntentControllerState m_controller;
        private readonly IIntentExecutor m_executor;
        private readonly IntentControllerHostOptions m_options;
        private readonly Func<NodeState, CancellationToken, ValueTask> m_addNode;
        private readonly Func<NodeState, CancellationToken, ValueTask>? m_removeNode;
        private readonly Dictionary<string, IntentEntry> m_intents = [];
        private readonly Dictionary<string, MissionEntry> m_missions = [];
        private readonly Dictionary<NodeId, MissionEntry> m_missionHistory = [];
        private readonly LinkedList<IntentEntry> m_queue = new();
        private readonly Dictionary<NodeId, IntentCapabilityDataType> m_capabilities = [];
        private NamespaceTable m_namespaceUris = new();
        private readonly Dictionary<string, ChannelEntry> m_channels = [];
        private SafetyStatus m_safety = SafetyStatus.Nominal;
        private readonly HashSet<NodeId> m_locations = [];
        private readonly HashSet<NodeId> m_tools = [];
        private readonly HashSet<NodeId> m_frames = [];
        private readonly HashSet<NodeId> m_toolFrames = [];
        private readonly HashSet<NodeId> m_outputs = [];
        private readonly HashSet<NodeId> m_waitSignals = [];
        private readonly HashSet<NodeId> m_programs = [];
        private readonly Dictionary<NodeId, NodeId> m_outputDataTypes = [];
        private readonly HashSet<string> m_frameIds = new(StringComparer.Ordinal);
        private FolderState? m_intentsFolder;
        private FolderState? m_missionsFolder;
        private readonly SemaphoreSlim m_pump = new(0);
        private readonly CancellationTokenSource m_shutdown = new();
        private ILogger? m_logger;

        private IntentEntry? m_current;
        private Task? m_pumpTask;
        private long m_nextId;
        private long m_nextAdmissionSequence;
        private bool m_paused;
        private bool m_pumpFaulted;
        private global::Opc.Ua.Server.ISessionManager? m_sessionManager;
        private global::Opc.Ua.Server.SessionEventHandler? m_sessionClosingHandler;
        private bool m_disposed;
        private int m_shutdownDeferred;
        private int m_resourcesDisposed;

        private enum MissionAdvanceResult
        {
            Started,
            Exhausted,
            Refused
        }

        private sealed class ProgressSink(
            IntentControllerHost host,
            ISystemContext context,
            IntentEntry entry) : IIntentProgress, IIntentBlendProgress
        {
            public void ReportProgress(double fraction)
            {
                lock (host.m_lock)
                {
                    if (entry.Node is { } node)
                    {
                        SetValue(node.Progress, fraction);
                        node.Progress?.ClearChangeMasks(context, false);
                    }
                }
            }

            public void ReportPose(Pose3DDataType pose)
            {
                lock (host.m_lock)
                {
                    entry.LastPose = pose;
                    if (entry.Node is { } node)
                    {
                        SetValue(node.CurrentPose, pose);
                        node.CurrentPose?.ClearChangeMasks(context, false);
                    }
                }
            }

            public void ReportTrajectoryDeviation(
                double pathPositionDeviation,
                double goalPositionDeviation,
                double elapsedMilliseconds,
                bool final)
            {
                host.ApplyTrajectoryDeviation(
                    context,
                    entry,
                    pathPositionDeviation,
                    goalPositionDeviation,
                    elapsedMilliseconds,
                    final);
            }

            public void ReportBlendBegin(Pose3DDataType pose)
            {
                entry.LastPose = pose;
                host.CompleteAtBlendPoint(context, entry, pose);
            }
        }

        private sealed class IntentEntry(
            string intentId,
            string baseIntentId,
            IntentDataType intent,
            string missionId,
            long admissionSequence)
            : IDisposable
        {
            public string IntentId { get; } = intentId;
            public string BaseIntentId { get; } = baseIntentId;
            public string OperationNodeName { get; } = $"{intentId}-{Guid.NewGuid():N}";
            public IntentDataType Intent { get; } = intent;
            public string MissionId { get; } = missionId;
            public long AdmissionSequence { get; } = admissionSequence;
            public IntentOperationState? Node { get; set; }
            public IntentExecution? Execution { get; set; }
            public ExecutionStateEnum State { get; set; } = ExecutionStateEnum.Accepted;
            public IntentResultDataType? Result { get; set; }
            public DateTime StartTime { get; } = DateTime.UtcNow;
            public NodeId CreateSessionId { get; init; } = NodeId.Null;
            public string CreateClientName { get; init; } = string.Empty;
            public DateTime InvocationCreationTime { get; init; } = DateTime.UtcNow;
            public IntentOutcome? ToleranceFailure { get; set; }
            public Pose3DDataType? LastPose { get; set; }
            public bool ExecutionCompleted { get; set; }
            public bool CancelRequested { get; private set; }
            public IntentFailureEnum CancelReason { get; private set; } = IntentFailureEnum.None;
            public StopModeEnum AcceptedStopMode { get; set; } = StopModeEnum.OnPath;
            public CancellationToken CancellationToken => m_cts.Token;

            public void RequestCancel(IntentFailureEnum reason, StopModeEnum stopMode)
            {
                CancelRequested = true;
                CancelReason = reason;
                AcceptedStopMode = stopMode;
                Execution?.AcceptCancellation(stopMode);
                try
                {
                    if (!m_cts.IsCancellationRequested)
                    {
                        m_cts.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void Dispose()
            {
                m_cts.Dispose();
            }

            private readonly CancellationTokenSource m_cts = new();
        }

        private sealed class ChannelEntry
        {
            public string ChannelId { get; init; } = string.Empty;
            public string EndpointUrl { get; init; } = string.Empty;
            public string PayloadDescriptor { get; init; } = string.Empty;
            public OperationalModeEnum RequiredMode { get; init; }
            public bool Available { get; set; } = true;
            public RealTimeChannelState? Node { get; set; }
            public NodeId? Holder { get; set; }
            public bool Leased { get; set; }
            public DateTime Expiry { get; set; } = DateTime.MinValue;
        }

        private sealed class MissionEntry(string missionId, MissionDataType mission, long sequence)
        {
            public string MissionId { get; } = missionId;
            public string MissionNodeName { get; } = $"{missionId}-{Guid.NewGuid():N}";
            public MissionDataType Mission { get; } = mission;
            public long AdmissionSequence { get; } = sequence;
            public MissionObjectState? Node { get; set; }
            public ExecutionStateEnum State { get; set; } = ExecutionStateEnum.Accepted;
            public IntentFailureEnum Failure { get; set; }
            public string FailureMessage { get; set; } = string.Empty;
            public int NextIndex { get; set; }
            public uint RetriesUsed { get; set; }
            public bool Compensating { get; set; }
            public string CurrentStepId { get; set; } = string.Empty;
            public string CurrentIntentId { get; set; } = string.Empty;
            public Dictionary<string, int> StepAttempts { get; } =
                new(StringComparer.Ordinal);
            public Dictionary<string, string> StepBaseIntentIds { get; } =
                CreateStepBaseIntentIds(mission.Steps);

            public int IncrementAttempt(string stepId)
            {
                if (!StepAttempts.TryGetValue(stepId, out int count))
                {
                    count = 0;
                }
                count++;
                StepAttempts[stepId] = count;
                return count;
            }

            public string GetBaseIntentId(MissionStepDataType step, int index)
            {
                string stepId = step.StepId ?? string.Empty;
                if (StepBaseIntentIds.TryGetValue(stepId, out string? intentId) &&
                    !string.IsNullOrEmpty(intentId))
                {
                    return intentId;
                }
                intentId = step.Intent?.IntentId ?? string.Empty;
                if (string.IsNullOrEmpty(intentId))
                {
                    intentId = string.IsNullOrEmpty(stepId)
                        ? FormattableString.Invariant($"{MissionId}/step-{index}")
                        : FormattableString.Invariant($"{MissionId}/{stepId}");
                    if (step.Intent != null)
                    {
                        step.Intent.IntentId = intentId;
                    }
                }
                StepBaseIntentIds[stepId] = intentId;
                return intentId;
            }

            public void ReplaceSteps(ArrayOf<MissionStepDataType> steps, int preservedCount)
            {
                for (int ii = preservedCount; ii < Mission.Steps.Count; ii++)
                {
                    string oldStepId = Mission.Steps[ii].StepId ?? string.Empty;
                    StepAttempts.Remove(oldStepId);
                    StepBaseIntentIds.Remove(oldStepId);
                }

                Mission.Steps = steps;
                for (int ii = preservedCount; ii < steps.Count; ii++)
                {
                    MissionStepDataType step = steps[ii];
                    string stepId = step.StepId ?? string.Empty;
                    StepAttempts.Remove(stepId);
                    StepBaseIntentIds[stepId] = step.Intent?.IntentId ?? string.Empty;
                }
            }

            private static Dictionary<string, string> CreateStepBaseIntentIds(
                ArrayOf<MissionStepDataType> steps)
            {
                var ids = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int ii = 0; ii < steps.Count; ii++)
                {
                    MissionStepDataType step = steps[ii];
                    ids[step.StepId ?? string.Empty] = step.Intent?.IntentId ?? string.Empty;
                }
                return ids;
            }
        }
    }

    internal static partial class IntentControllerHostLog
    {
        [LoggerMessage(EventId = RobotIntentServerEventIds.Admission, Level = LogLevel.Information,
            Message = "Intent {IntentId} admitted as {IntentType}.")]
        public static partial void IntentAdmitted(this ILogger logger, string intentId, string intentType);

        [LoggerMessage(EventId = RobotIntentServerEventIds.AdmissionRefused, Level = LogLevel.Information,
            Message = "Intent {IntentId} refused with {Failure}: {Message}")]
        public static partial void IntentAdmissionRefused(
            this ILogger logger, string intentId, IntentFailureEnum failure, string message);

        [LoggerMessage(EventId = RobotIntentServerEventIds.StateTransition, Level = LogLevel.Debug,
            Message = "Intent {IntentId} transitioned from {PreviousState} to {NewState}.")]
        public static partial void IntentStateTransition(
            this ILogger logger, string intentId, ExecutionStateEnum previousState, ExecutionStateEnum newState);

        [LoggerMessage(EventId = RobotIntentServerEventIds.ExecutorFault, Level = LogLevel.Error,
            Message = "Intent executor faulted for {IntentId}.")]
        public static partial void IntentExecutorFault(this ILogger logger, Exception exception, string intentId);

        [LoggerMessage(EventId = RobotIntentServerEventIds.NodeAddRemove, Level = LogLevel.Warning,
            Message = "Intent node operation failed for {NodeId}.")]
        public static partial void IntentNodeOperationFailed(this ILogger logger, Exception exception, NodeId nodeId);

        [LoggerMessage(EventId = RobotIntentServerEventIds.AuthorityGranted, Level = LogLevel.Information,
            Message = "Command authority granted to {SessionId}.")]
        public static partial void AuthorityGranted(this ILogger logger, NodeId sessionId);

        [LoggerMessage(EventId = RobotIntentServerEventIds.AuthorityReleased, Level = LogLevel.Information,
            Message = "Command authority released from {SessionId}.")]
        public static partial void AuthorityReleased(this ILogger logger, NodeId sessionId);

        [LoggerMessage(EventId = RobotIntentServerEventIds.LeaseGranted, Level = LogLevel.Information,
            Message = "Real-time channel {ChannelId} leased by {SessionId} until {Expiry}.")]
        public static partial void RealTimeLeaseGranted(
            this ILogger logger, string channelId, NodeId sessionId, DateTime expiry);

        [LoggerMessage(EventId = RobotIntentServerEventIds.LeaseExpired, Level = LogLevel.Information,
            Message = "Real-time channel {ChannelId} lease expired.")]
        public static partial void RealTimeLeaseExpired(this ILogger logger, string channelId);

        [LoggerMessage(EventId = RobotIntentServerEventIds.ShutdownTimedOut, Level = LogLevel.Warning,
            Message = "Intent controller shutdown timed out after {TimeoutMilliseconds} ms.")]
        public static partial void IntentControllerShutdownTimedOut(
            this ILogger logger, double timeoutMilliseconds);

        [LoggerMessage(EventId = RobotIntentServerEventIds.PumpFault, Level = LogLevel.Error,
            Message = "Intent controller pump faulted.")]
        public static partial void IntentPumpFault(this ILogger logger, Exception exception);
    }
}
