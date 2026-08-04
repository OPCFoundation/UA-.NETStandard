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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public sealed class IntentControllerHost : IDisposable
    {
        private const uint StateReady = 1;
        private const uint StateRunning = 2;
        private const uint StateSuspended = 3;
        private const uint StateHalted = 4;

        private readonly Lock m_lock = new();
        private readonly IntentControllerState m_controller;
        private readonly IIntentExecutor m_executor;
        private readonly IntentControllerHostOptions m_options;
        private readonly Func<NodeState, CancellationToken, ValueTask> m_addNode;
        private readonly Func<NodeState, CancellationToken, ValueTask>? m_removeNode;
        private readonly Dictionary<string, IntentEntry> m_intents = [];
        private readonly Dictionary<string, MissionEntry> m_missions = [];
        private readonly LinkedList<IntentEntry> m_queue = new();
        private readonly Dictionary<NodeId, IntentCapabilityDataType> m_capabilities = [];
        private NamespaceTable m_namespaceUris = new();
        private readonly Dictionary<string, ChannelEntry> m_channels = [];
        private SafetyStatus m_safety = SafetyStatus.Nominal;
        private FolderState? m_intentsFolder;
        private FolderState? m_missionsFolder;
        private readonly SemaphoreSlim m_pump = new(0);
        private readonly CancellationTokenSource m_shutdown = new();

        private IntentEntry? m_current;
        private Task? m_pumpTask;
        private long m_nextId;
        private bool m_paused;
        private bool m_disposed;

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
        /// Starts the execution pump and wires the controller's Methods.
        /// </summary>
        public void Start(ISystemContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_namespaceUris = context.NamespaceUris;
            m_intentsFolder = EnsureFolder(context, m_controller.Intents, BrowseNames.Intents);
            if (m_options.MissionsSupported)
            {
                m_missionsFolder = EnsureFolder(context, m_controller.Missions, BrowseNames.Missions);
            }
            ResolveCapabilities(context);
            if (m_options.RealTimeChannelsSupported)
            {
                CreateChannels(context);
            }
            PublishSafetyLocked(context);
            WireMethods(context);
            PublishControllerState(context);
            m_pumpTask = Task.Run(() => PumpAsync(context, m_shutdown.Token));
        }

        // ---------------------------------------------------------------- admission

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
            return SubmitCore(context, sessionId, intent, missionId, forceNewId: false);
        }

        private IntentAdmission SubmitCore(
            ISystemContext context,
            NodeId? sessionId,
            IntentDataType? intent,
            string missionId,
            bool forceNewId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (intent == null)
            {
                return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                    "No intent was supplied.");
            }

            // Everything below depends on state another thread can change: the safety
            // status, the control owner, the channel leases and the capability table.
            // Admission is decided and acted on under one acquisition, because a check
            // that releases the lock before it acts is a check the world can invalidate
            // in between - a stop asserted in that window would otherwise admit work
            // the Server had already been told to refuse.
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
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
                if (intent is MotionIntentDataType && AnyChannelHeldLocked() &&
                    !m_options.ArbitratesWithRealTimeChannel)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.CapabilityNotSupported,
                        "A real-time channel lease is held and this Server does not "
                        + "arbitrate between the two command sources.");
                }
                if (ExceedsSafeSpeedLocked(intent))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.SafetyLimitExceeded,
                        FormattableString.Invariant(
                            $"The requested speed exceeds the enforced safe limit of {m_safety.SafeSpeedLimit} m/s."));
                }

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

                // Last, and deliberately so: a caller that holds no authority must not
                // learn from the answer whether its parameters would have been valid.
                Check validation = IntentValidation.Validate(intent, m_options);
                if (!validation.Ok)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        validation.Message ?? "The intent is not valid.");
                }

                string id = forceNewId ? string.Empty : intent.IntentId ?? string.Empty;
                if (string.IsNullOrEmpty(id))
                {
                    id = FormattableString.Invariant(
                        $"intent-{Interlocked.Increment(ref m_nextId)}");
                }
                else if (m_intents.TryGetValue(id, out IntentEntry? existing) &&
                         !IntentOutcome.IsTerminal(existing.State))
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        $"IntentId '{id}' is already outstanding.");
                }

                if (intent.BufferMode != BufferModeEnum.Aborting &&
                    m_queue.Count >= m_options.MaxQueueDepth)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.QueueFull,
                        "The queue is at MaxQueueDepth.");
                }

                var entry = new IntentEntry(id, intent, missionId);
                m_intents[id] = entry;
                CreateOperationNode(context, entry);

                if (intent.BufferMode == BufferModeEnum.Aborting)
                {
                    // Everything already queued is superseded, and whatever is
                    // executing is asked to stop. The aborted work terminates as
                    // Cancelled with Superseded, which is what tells a client the
                    // difference between "you cancelled it" and "you replaced it".
                    SupersedeQueuedLocked(context);
                    m_current?.RequestCancel(IntentFailureEnum.Superseded);
                }

                m_queue.AddLast(entry);
                SetExecutionStateLocked(context, entry, ExecutionStateEnum.Accepted);
                RenumberQueueLocked(context);
                m_pump.Release();
                return IntentAdmission.Admitted(id, entry.Node!.NodeId);
            }
        }

        // ------------------------------------------------------------- cancellation

        /// <summary>
        /// Asks the Server to end an intent early.
        /// </summary>
        /// <remarks>
        /// This is NOT the OPC UA Cancel Service, which discards a pending service
        /// response and leaves the robot moving. The Server may refuse: some motions
        /// cannot be abandoned part-way without leaving the cell in a worse state than
        /// completing them.
        /// </remarks>
        public bool CancelIntent(ISystemContext context, NodeId? sessionId, string intentId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
                {
                    return false;
                }
                if (!m_intents.TryGetValue(intentId ?? string.Empty, out IntentEntry? entry) ||
                    IntentOutcome.IsTerminal(entry.State))
                {
                    return false;
                }
                return CancelLocked(context, entry, IntentFailureEnum.None);
            }
        }

        /// <summary>
        /// Asks the Server to end every outstanding intent and mission.
        /// </summary>
        public uint CancelAll(ISystemContext context, NodeId? sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
                {
                    return 0;
                }
                uint count = 0;
                foreach (MissionEntry mission in m_missions.Values.ToList())
                {
                    if (!IntentOutcome.IsTerminal(mission.State))
                    {
                        FinishMissionLocked(context, mission, ExecutionStateEnum.Cancelled);
                    }
                }
                foreach (IntentEntry entry in m_intents.Values.ToList())
                {
                    if (!IntentOutcome.IsTerminal(entry.State) &&
                        CancelLocked(context, entry, IntentFailureEnum.None))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        private bool CancelLocked(ISystemContext context, IntentEntry entry, IntentFailureEnum reason)
        {
            if (entry == m_current)
            {
                if (!m_executor.CanCancel(entry.Execution!))
                {
                    return false;
                }
                SetExecutionStateLocked(context, entry, ExecutionStateEnum.Cancelling);
                entry.RequestCancel(reason);
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

        private void SupersedeQueuedLocked(ISystemContext context)
        {
            while (m_queue.First is { } node)
            {
                m_queue.RemoveFirst();
                CompleteLocked(context, node.Value, new IntentOutcome
                {
                    State = ExecutionStateEnum.Cancelled,
                    Failure = IntentFailureEnum.Superseded,
                    Message = "Replaced by an Aborting submission."
                });
            }
        }

        // -------------------------------------------------------- pause and resume

        /// <summary>
        /// Suspends execution, retaining position.
        /// </summary>
        public bool Pause(ISystemContext context, NodeId? sessionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
                {
                    return false;
                }
                if (m_paused)
                {
                    return true;
                }
                m_paused = true;
                if (m_current is { } cur && cur.State == ExecutionStateEnum.Executing)
                {
                    SetExecutionStateLocked(context, cur, ExecutionStateEnum.Suspended);
                }
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
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
                {
                    return false;
                }
                if (!m_paused)
                {
                    return true;
                }
                m_paused = false;
                if (m_current is { } cur && cur.State == ExecutionStateEnum.Suspended)
                {
                    SetExecutionStateLocked(context, cur, ExecutionStateEnum.Executing);
                }
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
            IntentDataType? intent;
            string missionId;
            lock (m_lock)
            {
                if (!m_intents.TryGetValue(intentId ?? string.Empty, out IntentEntry? entry) ||
                    entry.State != ExecutionStateEnum.Retriable)
                {
                    return IntentAdmission.Refused(IntentFailureEnum.ParameterInvalid,
                        "No intent with that identifier terminated Retriable.");
                }
                intent = entry.Intent;
                missionId = entry.MissionId;
            }

            return SubmitCore(context, sessionId, intent!, missionId, forceNewId: true);
        }

        // ------------------------------------------------------------------ authority

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
            lock (m_lock)
            {
                if (ControlOwner == null || ControlOwner == sessionId)
                {
                    ControlOwner = sessionId;
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
            lock (m_lock)
            {
                if (ControlOwner == sessionId)
                {
                    ControlOwner = null;
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
            lock (m_lock)
            {
                m_safety = status;
                PublishSafetyLocked(context);
                PublishControllerState(context);
            }
        }

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
        /// Releases authority held by a Session that has closed.
        /// </summary>
        /// <remarks>
        /// Without this a crashed client locks the robot for good.
        /// </remarks>
        public void OnSessionClosed(ISystemContext context, NodeId sessionId)
        {
            ReleaseControl(context, sessionId);
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

        private static bool Permits<T>(ArrayOf<T> modes, T value) where T : struct, Enum
        {
            if (modes.IsNull || modes.IsEmpty)
            {
                return true;
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

        // ----------------------------------------------------------------- the pump

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
                        m_current = next;
                        SetExecutionStateLocked(context, next, ExecutionStateEnum.Executing);
                        RenumberQueueLocked(context);
                        PublishControllerState(context);
                    }
                    await RunOneAsync(context, next!).ConfigureAwait(false);
                }
            }
        }

        private async Task RunOneAsync(ISystemContext context, IntentEntry entry)
        {
            var progress = new ProgressSink(this, context, entry);
            entry.Execution = new IntentExecution(entry.IntentId, entry.Intent, progress)
            {
                MissionId = entry.MissionId
            };

            IntentOutcome outcome;
            try
            {
                outcome = await m_executor
                    .ExecuteAsync(entry.Execution, entry.CancellationToken)
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
            catch (Exception ex)                                   // the intent, not the pump
            {
                outcome = IntentOutcome.Fail(IntentFailureEnum.Other, ex.Message);
            }
#pragma warning restore CA1031

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

            lock (m_lock)
            {
                CompleteLocked(context, entry, outcome);
                m_current = null;
                AdvanceMissionLocked(context, entry, outcome);
                PublishControllerState(context);
            }
        }

        // ------------------------------------------------------- real-time channels

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
            if (!m_options.RealTimeChannelsSupported)
            {
                return RealTimeLease.Refused("This Server brokers no real-time channels.");
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
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
            lock (m_lock)
            {
                if (!m_channels.TryGetValue(channelId ?? string.Empty, out ChannelEntry? channel) ||
                    !channel.Leased || channel.Holder != sessionId)
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
        /// Whether any channel lease is currently held.
        /// </summary>
        /// <remarks>
        /// Clause 6.9 forbids accepting motion intents alongside a held channel unless
        /// the host can genuinely arbitrate: two things commanding one robot with no
        /// arbitration is the failure that rule exists to prevent.
        /// </remarks>
        private bool AnyChannelHeldLocked()
        {
            foreach (ChannelEntry channel in m_channels.Values)
            {
                if (channel.Leased && channel.Expiry > DateTime.UtcNow)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Materialises the declared channels so a client can browse and read them
        /// before it asks for a lease.
        /// </summary>
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

        // ------------------------------------------------------------------ missions

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
            if (!m_options.MissionsSupported)
            {
                return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                    "This Server does not implement missions.");
            }
            if (mission == null || mission.Steps.IsNull || mission.Steps.IsEmpty)
            {
                return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                    "A mission must carry at least one step.");
            }

            Check ordering = MissionRules.ValidateSteps(mission.Steps);
            if (!ordering.Ok)
            {
                return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                    ordering.Message ?? "The mission steps are not valid.");
            }
            Check graph = MissionRules.ValidateTransitions(mission.Steps, mission.Transitions);
            if (!graph.Ok)
            {
                return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                    graph.Message ?? "The mission graph is not valid.");
            }

            // Decided under the same acquisition that acts on it, for the reason given
            // in SubmitCore: authority and mode can both change between a check that
            // released the lock and the admission that follows it.
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
                {
                    return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                        "The calling Session does not hold command authority.");
                }
                if (!IsSubmissionPermittedInMode())
                {
                    return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                        "Missions are accepted only in Automatic or AutomaticExternal mode.");
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
                    return MissionAdmission.Refused(MissionUpdateResultEnum.Rejected,
                        $"MissionId '{id}' is already outstanding.");
                }

                var entry = new MissionEntry(id, mission);
                m_missions[id] = entry;
                CreateMissionNode(context, entry);
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
            if (!m_options.MissionHorizonSupported)
            {
                return new MissionUpdateOutcome(MissionUpdateResultEnum.Rejected,
                    "This Server does not implement horizon updates.");
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
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

                entry.Mission.Steps = steps;
                entry.Mission.MissionUpdateId = missionUpdateId;
                PublishMissionLocked(context, entry);
                return new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, null);
            }
        }

        /// <summary>
        /// Ends a mission and every intent belonging to it.
        /// </summary>
        public bool CancelMission(ISystemContext context, NodeId? sessionId, string missionId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            lock (m_lock)
            {
                if (m_options.RequireControlAuthority && ControlOwner != sessionId)
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
                        !IntentOutcome.IsTerminal(intent.State))
                    {
                        CancelLocked(context, intent, IntentFailureEnum.None);
                    }
                }
                FinishMissionLocked(context, entry, ExecutionStateEnum.Cancelled);
                return true;
            }
        }

        private void StartNextStepLocked(ISystemContext context, MissionEntry mission, NodeId? sessionId)
        {
            MissionStepDataType? step = MissionRules.NextPending(mission.Mission.Steps, mission.NextIndex);
            if (step == null)
            {
                FinishMissionLocked(context, mission, ExecutionStateEnum.Succeeded);
                return;
            }

            mission.CurrentStepId = step.StepId ?? string.Empty;
            IntentAdmission admission =
                SubmitCore(context, sessionId, step.Intent, mission.MissionId, forceNewId: true);
            if (!admission.Accepted)
            {
                FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                return;
            }
            mission.CurrentIntentId = admission.IntentId;
            PublishMissionLocked(context, mission);
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
                    FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                    return;
                }
                mission.RetriesUsed = 0;
                if (!AdvanceToNextStepLocked(context, mission))
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

            ApplyErrorPolicyLocked(context, mission);
        }

        /// <summary>
        /// Chooses the step that follows one that succeeded.
        /// </summary>
        /// <remarks>
        /// Where the mission carries a step graph and this host evaluates it, the graph
        /// decides; otherwise the steps run in order, which is what a mission without
        /// transitions has always done.
        /// </remarks>
        private bool AdvanceToNextStepLocked(ISystemContext context, MissionEntry mission)
        {
            ArrayOf<MissionTransitionDataType> transitions = mission.Mission.Transitions;
            bool graphed = m_options.MissionBranchingSupported &&
                !transitions.IsNull && !transitions.IsEmpty;

            if (graphed)
            {
                MissionTransitionDataType? edge = MissionRules.SelectTransition(
                    transitions, mission.CurrentStepId, m_options.EvaluateCondition);
                if (edge == null)
                {
                    return false;
                }
                int next = MissionRules.IndexOfStep(mission.Mission.Steps, edge.ToStepId ?? string.Empty);
                if (next < 0)
                {
                    return false;
                }
                mission.NextIndex = next;
            }
            else
            {
                mission.NextIndex++;
            }
            StartNextStepLocked(context, mission, ControlOwner);
            return !IntentOutcome.IsTerminal(mission.State);
        }

        /// <summary>
        /// Applies a failed step's error policy, per clause 7.4.
        /// </summary>
        private void ApplyErrorPolicyLocked(ISystemContext context, MissionEntry mission)
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
                    FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                    return;
                case ErrorPolicyEnum.Skip:
                    mission.RetriesUsed = 0;
                    if (!AdvanceToNextStepLocked(context, mission))
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
                        FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                        return;
                    }
                    mission.RetriesUsed = 0;
                    mission.Compensating = policy == ErrorPolicyEnum.Compensate;
                    mission.NextIndex = target;
                    StartNextStepLocked(context, mission, ControlOwner);
                    return;
                default:
                    FinishMissionLocked(context, mission, ExecutionStateEnum.Failed);
                    return;
            }
        }

        private void FinishMissionLocked(
            ISystemContext context, MissionEntry mission, ExecutionStateEnum state)
        {
            SetMissionStateLocked(context, mission, state);
            mission.CurrentStepId = string.Empty;
            PublishMissionLocked(context, mission);
        }

        // ------------------------------------------------------------ node plumbing

        private void CreateOperationNode(ISystemContext context, IntentEntry entry)
        {
            FolderState folder = m_intentsFolder
                ?? throw new InvalidOperationException("The controller has no Intents folder.");
            var node = new IntentOperationState(folder)
            {
                NodeId = ChildNodeId(folder.NodeId, entry.IntentId),
                BrowseName = new QualifiedName(entry.IntentId, folder.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(entry.IntentId),
                SymbolicName = entry.IntentId,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.IntentOperationType, context.NamespaceUris),
                EventNotifier = global::Opc.Ua.EventNotifiers.SubscribeToEvents
            };
            node.Create(context, node.NodeId, node.BrowseName, node.DisplayName, false);
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

            EnsureFinalResultData(context, node);
            entry.Node = node;
            AddNode(node);
        }

        /// <summary>
        /// Part 10 declares FinalResultData Optional, so it is not materialised by
        /// default. This host always produces a result and clause 6.7 says a Part 10
        /// client must find it here, so the object is created rather than skipped.
        /// </summary>
        private static void EnsureFinalResultData(ISystemContext context, IntentOperationState node)
        {
            if (node.FinalResultData != null)
            {
                return;
            }
            const string browseName = "FinalResultData";
            var final = new BaseObjectState(node)
            {
                NodeId = ChildNodeId(node.NodeId, browseName),
                BrowseName = new QualifiedName(browseName, node.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                SymbolicName = browseName,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            node.AddChild(final);
            final.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, node.NodeId);
            node.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, false, final.NodeId);
            node.FinalResultData = final;
            _ = context;
        }

        private void CreateMissionNode(ISystemContext context, MissionEntry entry)
        {
            FolderState folder = m_missionsFolder
                ?? throw new InvalidOperationException("The controller has no Missions folder.");
            var node = new MissionObjectState(folder)
            {
                NodeId = ChildNodeId(folder.NodeId, entry.MissionId),
                BrowseName = new QualifiedName(entry.MissionId, folder.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(entry.MissionId),
                SymbolicName = entry.MissionId,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.MissionType, context.NamespaceUris),
                EventNotifier = global::Opc.Ua.EventNotifiers.SubscribeToEvents
            };
            node.Create(context, node.NodeId, node.BrowseName, node.DisplayName, false);
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
            entry.State = state;
            if (entry.Node is not { } node)
            {
                return;
            }
            SetValue(node.ExecutionState, state);
            node.SetState(context, MapToProgramState(state));
            node.ClearChangeMasks(context, true);
            if (IntentOutcome.IsTerminal(state))
            {
                PruneTerminalOperationsLocked();
            }
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
            if (keep == 0 || m_removeNode is null)
            {
                return;
            }
            List<KeyValuePair<string, IntentEntry>> terminal = m_intents
                .Where(kv => IntentOutcome.IsTerminal(kv.Value.State) &&
                             !ReferenceEquals(kv.Value, m_current))
                .OrderBy(kv => kv.Value.StartTime)
                .ToList();
            for (int i = 0; i < terminal.Count - (int)keep; i++)
            {
                KeyValuePair<string, IntentEntry> victim = terminal[i];
                if (victim.Value.Node is { } stale)
                {
                    RemoveNode(stale);
                }
                m_intents.Remove(victim.Key);
                victim.Value.Dispose();
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

        /// <summary>
        /// Raised when a per-invocation node could not be retired.
        /// </summary>
        public event EventHandler<IntentNodeAddFailure>? NodeRemoveFailed;

        private void SetMissionStateLocked(
            ISystemContext context, MissionEntry entry, ExecutionStateEnum state)
        {
            entry.State = state;
            if (entry.Node is not { } node)
            {
                return;
            }
            SetValue(node.ExecutionState, state);
            node.SetState(context, MapToProgramState(state));
            node.ClearChangeMasks(context, true);
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

        private void CompleteLocked(ISystemContext context, IntentEntry entry, IntentOutcome outcome)
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
                PublishFinalResult(context, node, result);
                node.ClearChangeMasks(context, true);
            }
            SetExecutionStateLocked(context, entry, outcome.State);
        }

        /// <summary>
        /// Places the result under the inherited FinalResultData object as well, so a
        /// client written against Part 10 finds it where Part 10 says it will be.
        /// </summary>
        private static void PublishFinalResult(
            ISystemContext context, IntentOperationState node, IntentResultDataType result)
        {
            BaseObjectState? final = node.FinalResultData;
            if (final == null)
            {
                return;
            }
            var browseName = new QualifiedName(
                nameof(IntentOperationState.Result), node.BrowseName.NamespaceIndex);
            var value = new Variant(new ExtensionObject(result));
            if (final.FindChild(context, browseName) is BaseDataVariableState existing)
            {
                existing.Value = value;
                existing.ClearChangeMasks(context, false);
                return;
            }
            var variable = new BaseDataVariableState(final)
            {
                NodeId = ChildNodeId(final.NodeId, browseName.Name ?? "Result"),
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name ?? "Result"),
                SymbolicName = browseName.Name ?? "Result",
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType,
                DataType = ExpandedNodeId.ToNodeId(
                    DataTypeIds.IntentResultDataType, context.NamespaceUris),
                ValueRank = global::Opc.Ua.ValueRanks.Scalar,
                Value = value
            };
            final.AddChild(variable);
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
            SetValue(m_controller.Ready,
                IsSubmissionPermittedInMode() && !m_paused && m_safety.PermitsSubmission);
            SetValue(m_controller.ControlOwner, ControlOwner ?? global::Opc.Ua.NodeId.Null);
            SetValue(m_controller.MaxQueueDepth, m_options.MaxQueueDepth);
            SetValue(m_controller.ActiveIntent, m_current?.Node?.NodeId ?? NodeId.Null);
            m_controller.ClearChangeMasks(context, true);
        }

        // PropertyState<T> and BaseDataVariableState<T> derive from different
        // non-generic bases, so there is no one generic type to constrain on.
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
            var folder = new FolderState(m_controller)
            {
                NodeId = ChildNodeId(m_controller.NodeId, browseName),
                BrowseName = new QualifiedName(browseName, m_controller.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(browseName),
                SymbolicName = browseName,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent,
                TypeDefinitionId = global::Opc.Ua.ObjectTypeIds.FolderType,
                EventNotifier = global::Opc.Ua.EventNotifiers.None
            };
            m_controller.AddChild(folder);
            folder.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, true, m_controller.NodeId);
            m_controller.AddReference(global::Opc.Ua.ReferenceTypeIds.HasComponent, false, folder.NodeId);
            AddNode(folder);
            return folder;
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

        /// <summary>
        /// Raised when a per-invocation node could not be published.
        /// </summary>
        public event EventHandler<IntentNodeAddFailure>? NodeAddFailed;

        private static NodeId ChildNodeId(NodeId parent, string name)
        {
            return new NodeId($"{parent.IdentifierAsString}_{name}", parent.NamespaceIndex);
        }

        private void WireMethods(ISystemContext context)
        {
            if (m_controller.RequestControl is { } requestControl)
            {
                requestControl.OnCallAsync = (ctx, method, objectId, ct) =>
                {
                    bool granted = RequestControl(context, SessionOf(ctx), out NodeId? owner);
                    return new ValueTask<RequestControlMethodStateResult>(new RequestControlMethodStateResult
                    {
                        Granted = granted,
                        CurrentOwner = owner ?? global::Opc.Ua.NodeId.Null
                    });
                };
            }
            if (m_controller.SubmitIntent is { } submit)
            {
                submit.OnCallAsync = (ctx, method, objectId, intent, ct) =>
                {
                    IntentAdmission admission = SubmitIntent(context, SessionOf(ctx), intent);
                    if (!admission.Accepted)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadUserAccessDenied, admission.Message ?? "Refused.");
                    }
                    return new ValueTask<SubmitIntentMethodStateResult>(new SubmitIntentMethodStateResult
                    {
                        IntentId = admission.IntentId,
                        Operation = admission.Operation
                    });
                };
            }
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

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_shutdown.Cancel();
            try
            {
                m_pumpTask?.Wait(TimeSpan.FromSeconds(5));
            }
#pragma warning disable CA1031 // shutdown must not throw
            catch (Exception)
            {
            }
#pragma warning restore CA1031
            m_shutdown.Dispose();
            m_pump.Dispose();
            foreach (IntentEntry entry in m_intents.Values)
            {
                entry.Dispose();
            }
        }

        private sealed class ProgressSink(
            IntentControllerHost host, ISystemContext context, IntentEntry entry) : IIntentProgress
        {
            public void ReportProgress(double fraction)
            {
                if (entry.Node is { } node)
                {
                    SetValue(node.Progress, fraction);
                    node.Progress?.ClearChangeMasks(context, false);
                }
                _ = host;
            }

            public void ReportPose(Pose3DDataType pose)
            {
                if (entry.Node is { } node)
                {
                    SetValue(node.CurrentPose, pose);
                    node.CurrentPose?.ClearChangeMasks(context, false);
                }
            }
        }

        private sealed class IntentEntry(string intentId, IntentDataType intent, string missionId)
            : IDisposable
        {
            private readonly CancellationTokenSource m_cts = new();

            public string IntentId { get; } = intentId;
            public IntentDataType Intent { get; } = intent;
            public string MissionId { get; } = missionId;
            public IntentOperationState? Node { get; set; }
            public IntentExecution? Execution { get; set; }
            public ExecutionStateEnum State { get; set; } = ExecutionStateEnum.Accepted;
            public IntentResultDataType? Result { get; set; }
            public DateTime StartTime { get; } = DateTime.UtcNow;
            public bool CancelRequested { get; private set; }
            public IntentFailureEnum CancelReason { get; private set; } = IntentFailureEnum.None;
            public CancellationToken CancellationToken => m_cts.Token;

            public void RequestCancel(IntentFailureEnum reason)
            {
                CancelRequested = true;
                CancelReason = reason;
                if (!m_cts.IsCancellationRequested)
                {
                    m_cts.Cancel();
                }
            }

            public void Dispose()
            {
                m_cts.Dispose();
            }
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

        private sealed class MissionEntry(string missionId, MissionDataType mission)
        {
            public string MissionId { get; } = missionId;
            public MissionDataType Mission { get; } = mission;
            public MissionObjectState? Node { get; set; }
            public ExecutionStateEnum State { get; set; } = ExecutionStateEnum.Accepted;
            public int NextIndex { get; set; }
            public uint RetriesUsed { get; set; }
            public bool Compensating { get; set; }
            public string CurrentStepId { get; set; } = string.Empty;
            public string CurrentIntentId { get; set; } = string.Empty;
        }
    }
}
