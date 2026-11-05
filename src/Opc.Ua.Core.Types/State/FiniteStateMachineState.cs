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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Implements mapped states, transitions, and cause callbacks for a finite state machine.
    /// </summary>
    public partial class FiniteStateMachineState
    {
        /// <summary>
        /// Consume the telemetry context
        /// </summary>
        protected override void Initialize(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<FiniteStateMachineState>();
            base.Initialize(telemetry);
        }

        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            // cache the namespace index used to qualify the element browse names and node ids.
            int index = context.NamespaceUris.GetIndex(ElementNamespaceUri);

            if (index >= 0)
            {
                ElementNamespaceIndex = (ushort)index;
            }
        }

        /// <summary>
        /// Stores information about a statemachine element.
        /// </summary>
        protected sealed class ElementInfo
        {
            /// <summary>
            /// Creates a new instance of the object.
            /// </summary>
            public ElementInfo(uint id, string name, uint number)
            {
                Id = id;
                Name = name;
                Number = number;
            }

            /// <summary>
            /// The node id for the element.
            /// </summary>
            public uint Id { get; }

            /// <summary>
            /// The browse name of the element.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// A number assigned to the element.
            /// </summary>
            public uint Number { get; }
        }

        /// <summary>
        /// The namespace index used to qualify the element browse names and node ids.
        /// </summary>
        protected ushort ElementNamespaceIndex { get; set; }

        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected virtual string ElementNamespaceUri => Namespaces.OpcUa;

        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected virtual ElementInfo[]? StateTable => null;

        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected virtual ElementInfo[]? TransitionTable => null;

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected virtual uint[,]? TransitionMappings => null;

        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected virtual uint[,]? CauseMappings => null;

        /// <summary>
        /// The last state that the machine was in.
        /// </summary>
        protected FiniteStateVariableState? LastState { get; set; }

        /// <summary>
        /// Maps a numeric state id onto the NodeId of the
        /// <c>StateType</c> node representing that state.
        /// </summary>
        /// <remarks>
        /// The default follows the OPC UA convention of qualifying the
        /// numeric element id with <see cref="ElementNamespaceIndex"/>.
        /// Subclasses that materialize per-instance state nodes (whose
        /// NodeIds therefore cannot be derived from the state id alone)
        /// override this together with <see cref="GetStateId"/> so that
        /// <c>CurrentState/Id</c> resolves to a node that actually
        /// exists in the address space.
        /// </remarks>
        /// <param name="stateId">The numeric state id. Zero denotes
        /// "no state" and maps to <see cref="NodeId.Null"/>.</param>
        public virtual NodeId GetStateNodeId(uint stateId)
        {
            return stateId == 0 ? NodeId.Null : new NodeId(stateId, ElementNamespaceIndex);
        }

        /// <summary>
        /// The inverse of <see cref="GetStateNodeId"/>. Returns 0 when
        /// <paramref name="stateNodeId"/> does not identify a state of
        /// this machine.
        /// </summary>
        public virtual uint GetStateId(NodeId stateNodeId)
        {
            if (stateNodeId.IsNull ||
                ElementNamespaceIndex != stateNodeId.NamespaceIndex ||
                !stateNodeId.TryGetValue(out uint numericId))
            {
                return 0;
            }

            return numericId;
        }

        /// <summary>
        /// Maps a numeric transition id onto the NodeId of the
        /// <c>TransitionType</c> node representing that transition. See
        /// <see cref="GetStateNodeId"/> for the rationale.
        /// </summary>
        /// <param name="transitionId">The numeric transition id. Zero
        /// denotes "no transition" and maps to <see cref="NodeId.Null"/>.</param>
        public virtual NodeId GetTransitionNodeId(uint transitionId)
        {
            return transitionId == 0
                ? NodeId.Null
                : new NodeId(transitionId, ElementNamespaceIndex);
        }

        /// <summary>
        /// The inverse of <see cref="GetTransitionNodeId"/>. Returns 0
        /// when <paramref name="transitionNodeId"/> does not identify a
        /// transition of this machine.
        /// </summary>
        public virtual uint GetTransitionId(NodeId transitionNodeId)
        {
            if (transitionNodeId.IsNull ||
                ElementNamespaceIndex != transitionNodeId.NamespaceIndex ||
                !transitionNodeId.TryGetValue(out uint numericId))
            {
                return 0;
            }

            return numericId;
        }

        /// <summary>
        /// Returns the current state id of the state machine.
        /// </summary>
        protected uint GetCurrentStateId()
        {
            if (CurrentState == null ||
                CurrentState.Id == null ||
                CurrentState.Value.IsNullOrEmpty)
            {
                return 0;
            }

            return GetStateId(CurrentState.Id.Value);
        }

        /// <summary>
        /// Returns the new state for the specified transition. Returns 0 if the transition is not allowed.
        /// </summary>
        protected virtual uint GetNewStateForTransition(ISystemContext context, uint transitionId)
        {
            uint currentState = GetCurrentStateId();

            if (currentState == 0)
            {
                return 0;
            }

            uint[,]? transitionMappings = TransitionMappings;

            if (transitionMappings == null)
            {
                return 0;
            }

            int length = transitionMappings.GetLength(0);

            for (int ii = 0; ii < length; ii++)
            {
                if (transitionMappings[ii, 0] == transitionId &&
                    transitionMappings[ii, 1] == currentState)
                {
                    return transitionMappings[ii, 2];
                }
            }

            return 0;
        }

        /// <summary>
        /// Checks if the transition has an effect.
        /// </summary>
        protected virtual bool TransitionHasEffect(ISystemContext context, uint transitionId)
        {
            uint[,]? transitionMappings = TransitionMappings;

            if (transitionMappings == null)
            {
                return false;
            }

            int length = transitionMappings.GetLength(0);

            for (int ii = 0; ii < length; ii++)
            {
                if (transitionMappings[ii, 0] == transitionId)
                {
                    return transitionMappings[ii, 3] != 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the transition for the cause given the current state.
        /// </summary>
        protected virtual uint GetTransitionForCause(ISystemContext context, uint causeId)
        {
            uint currentState = GetCurrentStateId();

            if (currentState == 0)
            {
                return 0;
            }

            uint[,]? causeMappings = CauseMappings;

            if (causeMappings == null)
            {
                return 0;
            }

            int length = causeMappings.GetLength(0);

            for (int ii = 0; ii < length; ii++)
            {
                if (causeMappings[ii, 0] == causeId && causeMappings[ii, 1] == currentState)
                {
                    return causeMappings[ii, 2];
                }
            }

            return 0;
        }

        /// <summary>
        /// Returns the transition from the current state to the target state.
        /// </summary>
        protected virtual uint GetTransitionToState(ISystemContext context, uint targetStateId)
        {
            uint currentState = GetCurrentStateId();

            if (currentState == 0)
            {
                return 0;
            }

            uint[,]? transitionMappings = TransitionMappings;

            if (transitionMappings == null)
            {
                return 0;
            }

            int length = transitionMappings.GetLength(0);

            for (int ii = 0; ii < length; ii++)
            {
                if (transitionMappings[ii, 1] == currentState &&
                    transitionMappings[ii, 2] == targetStateId)
                {
                    return transitionMappings[ii, 0];
                }
            }

            return 0;
        }

        /// <summary>
        /// Updates the current state variable.
        /// </summary>
        protected void UpdateStateVariable(
            ISystemContext context,
            uint stateId,
            FiniteStateVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }

            if (stateId == 0)
            {
                variable.Value = default;
                variable.Id?.Value = default;

                variable.Number?.Value = 0;

                return;
            }

            ElementInfo[]? stateTable = StateTable;

            if (stateTable == null)
            {
                return;
            }

            for (int ii = 0; ii < stateTable.Length; ii++)
            {
                ElementInfo state = stateTable[ii];

                if (state.Id == stateId)
                {
                    variable.Value = new LocalizedText(state.Name);
                    variable.Id?.Value = GetStateNodeId(state.Id);

                    variable.Number?.Value = state.Number;

                    return;
                }
            }
        }

        /// <summary>
        /// Updates the last transition variable.
        /// </summary>
        protected void UpdateTransitionVariable(
            ISystemContext context,
            uint transitionId,
            FiniteTransitionVariableState? variable)
        {
            if (variable == null)
            {
                return;
            }

            if (transitionId == 0)
            {
                variable.Value = default;
                variable.Id?.Value = default;

                variable.TransitionTime?.Value = DateTime.MinValue;

                variable.Number?.Value = 0;

                return;
            }

            ElementInfo[]? transitionTable = TransitionTable;

            if (transitionTable == null)
            {
                return;
            }

            for (int ii = 0; ii < transitionTable.Length; ii++)
            {
                ElementInfo transition = transitionTable[ii];

                if (transition.Id == transitionId)
                {
                    variable.Value = new LocalizedText(transition.Name);
                    variable.Id?.Value = GetTransitionNodeId(transition.Id);

                    variable.TransitionTime?.Value = DateTime.UtcNow;

                    variable.Number?.Value = transition.Number;

                    return;
                }
            }
        }

        /// <summary>
        /// Raised to check whether the current user is allowed to execute the command.
        /// </summary>
        public StateMachineTransitionHandler? OnCheckUserPermission;

        /// <summary>
        /// Raised before a transition occurs.
        /// </summary>
        public StateMachineTransitionHandler? OnBeforeTransition;

        /// <summary>
        /// Raises after a transition occurs. Errors are ignored.
        /// </summary>
        public StateMachineTransitionHandler? OnAfterTransition;

        /// <summary>
        /// Replaces the synchronous post-transition callback on the native asynchronous path when supplied.
        /// </summary>
        public StateMachineTransitionAsyncHandler? OnAfterTransitionAsync;

        /// <summary>
        /// If true transition events will not be produced by the state machine.
        /// </summary>
        public bool SuppressTransitionEvents { get; set; }

        /// <summary>
        /// Creates transition-local callbacks that retain the captured source and destination states.
        /// </summary>
        internal StateMachineTransitionCallbackFactory? TransitionCallbackFactory { get; set; }

        /// <summary>
        /// Creates native asynchronous post-transition callbacks without invoking synchronous coordinators.
        /// </summary>
        internal StateMachineAsyncTransitionCallbackFactory? AsyncTransitionCallbackFactory { get; set; }

        /// <summary>
        /// Synchronizes attached child machines after an owner's initial state selection.
        /// </summary>
        internal event Action<ISystemContext, uint>? InitialStateSynchronization;

        /// <summary>
        /// Synchronizes attached children through native asynchronous notification delivery.
        /// </summary>
        internal event Func<ISystemContext, uint, ValueTask>? InitialStateSynchronizationAsync;

        /// <summary>
        /// Marks attached children for initial-state entry on the next activation of a new Program run.
        /// </summary>
        internal event Action? SubStateResetting;

        /// <summary>
        /// Requests child-state reset without acquiring child-machine synchronization.
        /// </summary>
        internal void ResetSubStates()
        {
            SubStateResetting?.Invoke();
        }

        /// <summary>
        /// Applies the selected initial state to attached child-machine behavior without reporting a transition.
        /// </summary>
        internal void SynchronizeInitialState(ISystemContext context, uint stateId)
        {
            InitialStateSynchronization?.Invoke(context, stateId);
        }

        /// <summary>
        /// Gets the revision used to reject work armed before the current state was entered.
        /// </summary>
        internal long StateRevision => Interlocked.Read(ref m_stateRevision);

        /// <summary>
        /// Gets whether native delivery currently excludes synchronous state mutations.
        /// </summary>
        internal bool IsAsyncTransitionActive
        {
            get
            {
                lock (m_transitionLock)
                {
                    return m_asyncTransitionActive;
                }
            }
        }

        /// <summary>
        /// Invokes the callback function if it has been specified.
        /// </summary>
        protected ServiceResult InvokeCallback(
            StateMachineTransitionHandler? callback,
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments)
        {
            if (callback != null)
            {
                try
                {
                    return callback(
                        context,
                        this,
                        transitionId,
                        causeId,
                        inputArguments,
                        outputArguments);
                }
                catch (Exception e)
                {
                    return new ServiceResult(e);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks if the cause is permitted given the current state and returns the associated transition.
        /// </summary>
        public virtual bool IsCausePermitted(
            ISystemContext context,
            uint causeId,
            bool checkUserAccessRights)
        {
            if (CurrentState?.StatusCode == StatusCodes.BadStateNotActive)
            {
                return false;
            }
            uint transitionId = GetTransitionForCause(context, causeId);

            if (transitionId == 0)
            {
                return false;
            }

            if (checkUserAccessRights)
            {
                ServiceResult result = InvokeCallback(
                    OnCheckUserPermission,
                    context,
                    this,
                    transitionId,
                    causeId,
                    default,
                    null);

                if (ServiceResult.IsBad(result))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Forces the machine into the specified state.
        /// </summary>
        public virtual void SetState(ISystemContext context, uint newState)
        {
            lock (m_transitionLock)
            {
                if (m_asyncTransitionActive && !CanSetCompositeState())
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                uint transitionId = GetTransitionToState(context, newState);
                UpdateStateVariable(context, newState, CurrentState);
                UpdateTransitionVariable(context, transitionId, LastTransition);
                m_stateRevision++;
            }
        }

        /// <summary>
        /// Checks a model's state table without changing the current state.
        /// </summary>
        internal bool IsKnownState(uint stateId)
        {
            ElementInfo[]? states = StateTable;
            return states != null && Array.Exists(states, state => state.Id == stateId);
        }

        /// <summary>
        /// Applies Part 16 active-state quality to a nested machine and its state properties.
        /// </summary>
        internal void SetActive(ISystemContext context, bool active)
        {
            lock (m_transitionLock)
            {
                if (m_asyncTransitionActive)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                StatusCode status = active ? StatusCodes.Good : StatusCodes.BadStateNotActive;
                ApplyStateQuality(context, CurrentState, status);
                ApplyStateQuality(context, LastTransition, status);
            }
        }

        /// <summary>
        /// Applies nested-machine activation and optional state selection under one asynchronous admission.
        /// </summary>
        internal async ValueTask SetStateAndActiveAsync(
            ISystemContext context,
            uint? stateId,
            bool active,
            CancellationToken cancellationToken = default)
        {
            AsyncTransitionAdmission? admission = await EnterAsyncTransitionAsync(
                cancellationToken, allowCompositeNested: true)
                .ConfigureAwait(false);
            if (admission == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            m_asyncAdmission.Value = admission;
            var failures = new List<Exception>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (stateId.HasValue)
                    {
                        uint transition = GetTransitionToState(context, stateId.Value);
                        UpdateStateVariable(context, stateId.Value, CurrentState);
                        UpdateTransitionVariable(context, transition, LastTransition);
                        m_stateRevision++;
                    }
                    StatusCode status = active ? StatusCodes.Good : StatusCodes.BadStateNotActive;
                    ApplyStateQuality(context, CurrentState, status, publish: false);
                    ApplyStateQuality(context, LastTransition, status, publish: false);
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                }
                Func<ISystemContext, uint, ValueTask>? synchronizers = InitialStateSynchronizationAsync;
                if (synchronizers != null)
                {
                    uint current = active ? GetCurrentStateId() : 0;
                    foreach (Delegate synchronizer in synchronizers.GetInvocationList())
                    {
                        try
                        {
                            await ((Func<ISystemContext, uint, ValueTask>)synchronizer)(context, current)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddFailure(failures, ex);
                        }
                    }
                }
                await PublishChangeMasksAsync(context, this, failures).ConfigureAwait(false);
                ThrowFailures(failures);
            }
            finally
            {
                ExitAsyncTransition(admission);
            }
        }

        /// <summary>
        /// Publishes composite state changes while rejecting direct state-machine writes until delivery finishes.
        /// </summary>
        internal async ValueTask PublishStateChangesAsync(
            ISystemContext context,
            bool includeChildren,
            CancellationToken cancellationToken = default)
        {
            AsyncTransitionAdmission? admission = await EnterAsyncTransitionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (admission == null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            m_asyncAdmission.Value = admission;
            var failures = new List<Exception>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (includeChildren)
                {
                    await PublishChangeMasksAsync(context, this, failures).ConfigureAwait(false);
                }
                else
                {
                    await ClearChangeMasksAsync(context, false, CancellationToken.None).ConfigureAwait(false);
                }
                ThrowFailures(failures);
            }
            finally
            {
                ExitAsyncTransition(admission);
            }
        }

        /// <summary>
        /// Reserves this machine and every descendant for one composite operation without exposing admission management.
        /// </summary>
        internal async ValueTask<ServiceResult> ExecuteWithStateAdmissionAsync(
            ISystemContext context,
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (m_asyncAdmission.Value is { Active: true })
            {
                return StatusCodes.BadInvalidState;
            }
            AsyncTransitionAdmission? admission = await EnterAsyncTransitionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (admission == null)
            {
                return StatusCodes.BadInvalidState;
            }
            var admissions = new List<(FiniteStateMachineState Machine, AsyncTransitionAdmission Admission)>();
            var composite = new CompositeStateAdmission();
            admission.Composite = composite;
            m_asyncAdmission.Value = admission;
            try
            {
                ReserveDescendantAdmissions(context, this, admissions, composite);
                cancellationToken.ThrowIfCancellationRequested();
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                for (int ii = admissions.Count - 1; ii >= 0; ii--)
                {
                    (FiniteStateMachineState machine, AsyncTransitionAdmission childAdmission) = admissions[ii];
                    machine.ReleaseAsyncTransition(childAdmission);
                }
                ReleaseAsyncTransition(admission);
            }
        }

        /// <summary>
        /// Allows only the composite owner's state assignments between native notification intervals.
        /// </summary>
        private bool CanSetCompositeState()
        {
            return m_asyncAdmission.Value is { Active: true, Composite: not null } admission &&
                Volatile.Read(ref admission.Composite.OperationDepth) == 0;
        }

        private static void ApplyStateQuality(
            ISystemContext context, BaseVariableState? variable, StatusCode status, bool publish = true)
        {
            if (variable is null)
            {
                return;
            }
            variable.StatusCode = status;
            var children = new List<BaseInstanceState>();
            variable.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState childVariable)
                {
                    childVariable.StatusCode = status;
                }
            }
            if (publish)
            {
                variable.ClearChangeMasks(context, true);
            }
        }

        /// <summary>
        /// Invokes the specified cause.
        /// </summary>
        public virtual ServiceResult DoCause(
            ISystemContext context,
            MethodState? causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return ExecuteSynchronousTransition(
                () => DoCauseCore(context, causeMethod, causeId, inputArguments, outputArguments));
        }

        /// <summary>
        /// Invokes the specified cause and awaits the publication of its
        /// events and change notifications with
        /// <see cref="NodeState.ReportEventAsync"/> and
        /// <see cref="NodeState.ClearChangeMasksAsync"/>.
        /// </summary>
        /// <remarks>
        /// Uses the protected cause mapping and virtual <see cref="DoTransitionAsync"/>,
        /// not synchronous cause or transition overrides. Subclasses must customize the
        /// asynchronous counterparts separately. Synchronous state changes are refused
        /// while this invocation owns admission. Callback self-reentrancy is refused
        /// with BadInvalidState instead of awaiting this invocation. Cancellation is
        /// observed before mutation; committed notifications finish without cancellation.
        /// Legacy synchronous transition callbacks and completion observers still run
        /// synchronously; they must not perform asynchronous notification work.
        /// </remarks>
        public virtual async ValueTask<ServiceResult> DoCauseAsync(
            ISystemContext context,
            MethodState? causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (outputArguments == null)
            {
                throw new ArgumentNullException(nameof(outputArguments));
            }
            AsyncTransitionAdmission? admission = await EnterAsyncTransitionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (admission == null)
            {
                return StatusCodes.BadInvalidState;
            }
            m_asyncAdmission.Value = admission;
            var failures = new List<Exception>();
            ServiceResult result = ServiceResult.Good;
            bool transitioned = false;
            try
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint transitionId = GetTransitionForCause(context, causeId);
                    result = transitionId == 0
                        ? StatusCodes.BadNotSupported
                        : OnCheckUserPermission?.Invoke(
                            context, this, transitionId, causeId, inputArguments, outputArguments)
                            ?? ServiceResult.Good;
                    if (ServiceResult.IsGood(result))
                    {
                        admission.ForwardingTransition = true;
                        long revision = StateRevision;
                        try
                        {
                            result = await DoTransitionAsync(
                                context, transitionId, causeId, inputArguments, outputArguments, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            transitioned = StateRevision != revision;
                            admission.ForwardingTransition = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                    result = ServiceResult.Create(
                        ex, StatusCodes.BadUnexpectedError, "Unexpected error processing a state machine cause.");
                }

                if (AreEventsMonitored && causeMethod != null)
                {
                    try
                    {
                        AuditUpdateStateEventState e = CreateAuditEvent(context, causeMethod, causeId);
                        UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);
                        await ReportEventAsync(context, e, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AddFailure(failures, ex);
                    }
                    if (m_causeId != causeId && transitioned)
                    {
                        try
                        {
                            await ReportAuditProgramTransitionEventAsync(
                                context, causeMethod, causeId, inputArguments, result, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddFailure(failures, ex);
                        }
                        m_causeId = causeId;
                    }
                }
                ThrowFailures(failures);
                return result;
            }
            finally
            {
                ExitAsyncTransition(admission);
            }
        }

        /// <summary>
        /// Checks a cause's permissions, performs its transition, and reports applicable audit events.
        /// </summary>
        private ServiceResult DoCauseCore(
            ISystemContext context,
            MethodState? causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult? result = null;

            try
            {
                // get the transition.
                uint transitionId = GetTransitionForCause(context, causeId);

                if (transitionId == 0)
                {
                    // assign the result so the audit event reports the failure (Part 5 6.4.3).
                    return result = StatusCodes.BadNotSupported;
                }

                // check access rights.
                result = InvokeCallback(
                    OnCheckUserPermission,
                    context,
                    this,
                    transitionId,
                    causeId,
                    inputArguments,
                    outputArguments);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                // do the transition.
                result = DoTransition(context, transitionId, causeId, inputArguments, outputArguments);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                // report any changes to state machine.
                ClearChangeMasks(context, true);
            }
            catch (Exception ex)
            {
                // a failed transition must not be audited as successful.
                result = ServiceResult.Create(
                    ex,
                    StatusCodes.BadUnexpectedError,
                    "Unexpected error processing a state machine cause.");
                throw;
            }
            finally
            {
                // report the event.
                if (AreEventsMonitored && causeMethod != null)
                {
                    AuditUpdateStateEventState e = CreateAuditEvent(context, causeMethod, causeId);
                    UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);
                    ReportEvent(context, e);

                    // only a performed transition is reported as a program transition.
                    if (m_causeId != causeId && ServiceResult.IsGood(result))
                    {
                        ReportAuditProgramTransitionEvent(
                            context,
                            causeMethod,
                            causeId,
                            inputArguments,
                            result);
                        m_causeId = causeId;
                    }
                }
            }

            return result ?? ServiceResult.Good;
        }

        /// <summary>
        /// Creates an instance of an audit event.
        /// </summary>
        protected virtual AuditUpdateStateEventState CreateAuditEvent(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId)
        {
            return new AuditUpdateStateEventState(null);
        }

        /// <summary>
        /// Updates an audit event after the method is invoked.
        /// </summary>
        protected virtual void UpdateAuditEvent(
            ISystemContext context,
            MethodState causeMethod,
            ArrayOf<Variant> inputArguments,
            uint causeId,
            AuditUpdateStateEventState e,
            ServiceResult? result)
        {
            var info = new TranslationInfo(
                "StateTransition",
                "en-US",
                "The {0} method called was on the {1} state machine.",
                causeMethod.DisplayName,
                GetDisplayPath(3, '.'));

            e.Initialize(
                context,
                this,
                EventSeverity.Medium,
                new LocalizedText(info),
                ServiceResult.IsGood(result),
                DateTime.UtcNow);

            e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
            e.SetChildValue(
                context,
                BrowseNames.SourceName,
                $"Method/{causeMethod.BrowseName.Name}",
                false);
            e.SetChildValue(context, BrowseNames.LocalTime, TimeZoneDataType.Local, false);

            // AuditUpdateMethodStateEventType properties
            e.SetChildValue(context, BrowseNames.MethodId, causeMethod.NodeId, false);
            e.SetChildValue(context, BrowseNames.InputArguments, inputArguments, false);

            // AuditUpdateStateEventType properties
            e.SetChildValue(context, BrowseNames.OldStateId, LastState, false);
            e.SetChildValue(context, BrowseNames.NewStateId, CurrentState, false);
        }

        /// <summary>
        /// Reports AuditProgramTransition event
        /// </summary>
        protected virtual void ReportAuditProgramTransitionEvent(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            ServiceResult? result)
        {
            try
            {
                ReportEvent(
                    context,
                    CreateAuditProgramTransitionEvent(context, causeMethod, causeId, inputArguments, result));
            }
            catch (Exception ex)
            {
                m_logger.ReportAuditProgramTransitionError(ex);
            }
        }

        /// <summary>
        /// Reports the program audit event using native asynchronous notification.
        /// </summary>
        /// <remarks>
        /// Asynchronous causes use this counterpart rather than the synchronous
        /// reporting override. Failures are retained by the cause invocation.
        /// </remarks>
        protected virtual ValueTask ReportAuditProgramTransitionEventAsync(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            ServiceResult? result,
            CancellationToken cancellationToken = default)
        {
            return ReportEventAsync(
                context,
                CreateAuditProgramTransitionEvent(context, causeMethod, causeId, inputArguments, result),
                cancellationToken);
        }

        private AuditProgramTransitionEventState CreateAuditProgramTransitionEvent(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            ServiceResult? result)
        {
            var e = new AuditProgramTransitionEventState(null);

            UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);

            e.SetChildValue(
                context,
                BrowseNames.TransitionNumber,
                LastTransition?.Number?.Value ?? 0,
                false);
            return e;
        }

        /// <summary>
        /// Updates the state machine to reflect the successful processing of a method.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="causeId">The cause id.</param>
        public virtual void CauseProcessingCompleted(ISystemContext context, uint causeId)
        {
            lock (m_transitionLock)
            {
                if (m_asyncTransitionActive)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
                CompleteCauseCore(context, causeId);
            }
        }

        /// <summary>
        /// Publishes the completed cause's state and transition, advancing the state revision.
        /// </summary>
        private void CompleteCauseCore(ISystemContext context, uint causeId)
        {
            // get the transition.
            uint transitionId = GetTransitionForCause(context, causeId);

            if (transitionId == 0)
            {
                return;
            }

            // get the new state.
            uint newState = GetNewStateForTransition(context, transitionId);

            if (newState == 0)
            {
                return;
            }

            // save the last state.
            LastState = CoreUtils.Clone(CurrentState);

            // update state and transition variables.
            UpdateStateVariable(context, newState, CurrentState);
            UpdateTransitionVariable(context, transitionId, LastTransition);
            m_stateRevision++;
        }

        /// <summary>
        /// Causes the specified transition to occur.
        /// </summary>
        public virtual ServiceResult DoTransition(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return ExecuteSynchronousTransition(
                () => DoTransitionCore(context, transitionId, causeId, inputArguments, outputArguments));
        }

        /// <summary>
        /// Causes the specified transition to occur and awaits the
        /// publication of its transition event and live state changes with
        /// <see cref="NodeState.ReportEventAsync"/> and <see cref="NodeState.ClearChangeMasksAsync"/>.
        /// </summary>
        /// <remarks>
        /// The state remains live and unchanged until notification delivery completes.
        /// Concurrent asynchronous invocations wait for admission; synchronous writes
        /// and callback self-reentrancy return BadInvalidState. Synchronous overrides
        /// are not invoked. Legacy callbacks still run synchronously and must not
        /// publish asynchronous work themselves. Cancellation is checked before
        /// mutation. After mutation every node notification is attempted, failures
        /// are retained together, and the state is not rolled back. NodeState's
        /// per-node sink short-circuiting is unchanged; only observed failures are retained.
        /// </remarks>
        public virtual async ValueTask<ServiceResult> DoTransitionAsync(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (outputArguments == null)
            {
                throw new ArgumentNullException(nameof(outputArguments));
            }
            AsyncTransitionAdmission? admission = m_asyncAdmission.Value;
            bool forwarded = admission is { Active: true, ForwardingTransition: true };
            if (forwarded)
            {
                admission!.ForwardingTransition = false;
            }
            else
            {
                admission = await EnterAsyncTransitionAsync(cancellationToken).ConfigureAwait(false);
                if (admission == null)
                {
                    return StatusCodes.BadInvalidState;
                }
                m_asyncAdmission.Value = admission;
            }
            var failures = new List<Exception>();
            bool mutated = false;
            TransitionEventState? transitionEvent = null;
            try
            {
                ServiceResult result;
                PreparedStateMachineTransition prepared;
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_transitionLock)
                {
                    result = PrepareTransitionCore(
                        context, transitionId, causeId, inputArguments, outputArguments, out prepared,
                        nativeAsync: true);
                }
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                try
                {
                    lock (m_transitionLock)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (m_stateRevision != prepared.Revision || GetCurrentStateId() != prepared.FromState)
                        {
                            return StatusCodes.BadInvalidState;
                        }
                        LastState = CoreUtils.Clone(CurrentState);
                        // Treat a partially failed variable update as committed for notification delivery.
                        mutated = true;
                        UpdateStateVariable(context, prepared.ToState, CurrentState);
                        UpdateTransitionVariable(context, prepared.TransitionId, LastTransition);
                        m_stateRevision++;
                    }
                    if (AreEventsMonitored && !SuppressTransitionEvents)
                    {
                        transitionEvent = CreateTransitionEvent(context, prepared.TransitionId, prepared.CauseId);
                        if (transitionEvent != null)
                        {
                            UpdateTransitionEvent(context, prepared.TransitionId, prepared.CauseId, transitionEvent);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                }
                if (mutated)
                {
                    try
                    {
                        if (prepared.AfterAsync != null)
                        {
                            result = await prepared.AfterAsync(
                                context, this, prepared.TransitionId, prepared.CauseId,
                                prepared.InputArguments, prepared.OutputArguments, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            result = prepared.After?.Invoke(
                                context, this, prepared.TransitionId, prepared.CauseId,
                                prepared.InputArguments, prepared.OutputArguments) ?? ServiceResult.Good;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddFailure(failures, ex);
                    }
                    if (transitionEvent != null)
                    {
                        try
                        {
                            await ReportEventAsync(context, transitionEvent, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            AddFailure(failures, ex);
                        }
                    }
                    try
                    {
                        await DispatchTransitionCompletionsAsync(failures).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AddFailure(failures, ex);
                    }
                    await PublishChangeMasksAsync(context, this, failures).ConfigureAwait(false);
                }
                ThrowFailures(failures);
                return result;
            }
            finally
            {
                if (!forwarded)
                {
                    ExitAsyncTransition(admission!);
                }
            }
        }

        /// <summary>
        /// Visits live children in NodeState order, retaining failures without skipping later nodes.
        /// </summary>
        private static async ValueTask PublishChangeMasksAsync(
            ISystemContext context,
            NodeState node,
            List<Exception> failures)
        {
            var admissions = new List<(FiniteStateMachineState Machine, AsyncTransitionAdmission Admission)>();
            try
            {
                ReserveDescendantAdmissions(context, node, admissions);
                await PublishNodeChangeMasksAsync(context, node, failures).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddFailure(failures, ex);
            }
            finally
            {
                for (int ii = admissions.Count - 1; ii >= 0; ii--)
                {
                    (FiniteStateMachineState machine, AsyncTransitionAdmission admission) = admissions[ii];
                    machine.ExitAsyncTransition(admission);
                }
            }
        }

        /// <summary>
        /// Reserves every descendant machine before any recursive sink is invoked.
        /// </summary>
        private static void ReserveDescendantAdmissions(
            ISystemContext context,
            NodeState node,
            List<(FiniteStateMachineState Machine, AsyncTransitionAdmission Admission)> admissions,
            CompositeStateAdmission? composite = null)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is FiniteStateMachineState machine)
                {
                    if (machine.m_asyncAdmission.Value is { Active: true, Composite: not null })
                    {
                        ReserveDescendantAdmissions(context, child, admissions, composite);
                        continue;
                    }
                    AsyncTransitionAdmission admission;
                    lock (machine.m_transitionLock)
                    {
                        if (machine.m_asyncTransitionActive ||
                            machine.m_synchronousAdmissions != 0 || machine.m_dispatchingCompletions)
                        {
                            // Waiting here can deadlock an already-admitted child's callback against its parent.
                            throw new ServiceResultException(StatusCodes.BadInvalidState);
                        }
                        machine.m_asyncTransitionActive = true;
                        admission = new AsyncTransitionAdmission { Composite = composite };
                    }
                    machine.m_asyncAdmission.Value = admission;
                    admissions.Add((machine, admission));
                }
                ReserveDescendantAdmissions(context, child, admissions, composite);
            }
        }

        /// <summary>
        /// Delivers children before their owner while the entire machine subtree retains admission.
        /// </summary>
        private static async ValueTask PublishNodeChangeMasksAsync(
            ISystemContext context,
            NodeState node,
            List<Exception> failures)
        {
            try
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                foreach (BaseInstanceState child in children)
                {
                    await PublishNodeChangeMasksAsync(context, child, failures).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AddFailure(failures, ex);
            }
            try
            {
                await node.ClearChangeMasksAsync(context, false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AddFailure(failures, ex);
            }
        }

        /// <summary>
        /// Retains individual failures from nested asynchronous operations in delivery order.
        /// </summary>
        private static void AddFailure(List<Exception> failures, Exception failure)
        {
            if (failure is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    AddFailure(failures, inner);
                }
            }
            else
            {
                failures.Add(failure);
            }
        }

        /// <summary>
        /// Preserves a single original exception or reports every retained failure.
        /// </summary>
        private static void ThrowFailures(List<Exception> failures)
        {
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Count > 1)
            {
                throw new AggregateException("State machine processing or notification delivery failed.", failures);
            }
        }

        /// <summary>
        /// Preserves legacy synchronous reentrancy while excluding awaited asynchronous notifications.
        /// </summary>
        private ServiceResult ExecuteSynchronousTransition(Func<ServiceResult> operation)
        {
            lock (m_transitionLock)
            {
                if (m_asyncTransitionActive)
                {
                    return StatusCodes.BadInvalidState;
                }
                m_synchronousAdmissions++;
            }
            try
            {
                lock (m_transitionLock)
                {
                    return operation();
                }
            }
            finally
            {
                try
                {
                    DispatchTransitionCompletions();
                }
                finally
                {
                    lock (m_transitionLock)
                    {
                        m_synchronousAdmissions--;
                        SignalAdmissionChanged();
                    }
                }
            }
        }

        /// <summary>
        /// Waits without holding a synchronous lock and refuses callback self-reentrancy.
        /// </summary>
        private async ValueTask<AsyncTransitionAdmission?> EnterAsyncTransitionAsync(
            CancellationToken cancellationToken,
            bool allowCompositeNested = false)
        {
            if (m_asyncAdmission.Value is { Active: true, Composite: not null } owned)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_transitionLock)
                {
                    if (!owned.Active)
                    {
                        return null;
                    }
                    if (allowCompositeNested)
                    {
                        Interlocked.Increment(ref owned.Composite.OperationDepth);
                    }
                    else if (Interlocked.CompareExchange(ref owned.Composite.OperationDepth, 1, 0) != 0)
                    {
                        return null;
                    }
                    return owned;
                }
            }
            if (m_asyncAdmission.Value is { Active: true } || m_transitionLock.IsHeldByCurrentThread)
            {
                return null;
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task changed;
                lock (m_transitionLock)
                {
                    if (!m_asyncTransitionActive && m_synchronousAdmissions == 0 && !m_dispatchingCompletions)
                    {
                        m_asyncTransitionActive = true;
                        return new AsyncTransitionAdmission();
                    }
                    changed = m_admissionChanged.Task;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases logical admission only after the live transition's notifications have completed.
        /// </summary>
        private void ExitAsyncTransition(AsyncTransitionAdmission admission)
        {
            if (admission.Composite != null)
            {
                Interlocked.Decrement(ref admission.Composite.OperationDepth);
                return;
            }
            ReleaseAsyncTransition(admission);
        }

        /// <summary>
        /// Releases a native or composite reservation after all of its operation scopes have finished.
        /// </summary>
        private void ReleaseAsyncTransition(AsyncTransitionAdmission admission)
        {
            admission.Active = false;
            m_asyncAdmission.Value = null;
            lock (m_transitionLock)
            {
                m_asyncTransitionActive = false;
                SignalAdmissionChanged();
            }
        }

        /// <summary>
        /// Wakes asynchronous admission waiters while mutation metadata is protected.
        /// </summary>
        private void SignalAdmissionChanged()
        {
            TaskCompletionSource<bool> changed = m_admissionChanged;
            m_admissionChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult(true);
        }

        /// <summary>
        /// Executes an armed transition only while its source state, revision, and timer are still current.
        /// </summary>
        internal ServiceResult TryTimedTransition(
            ISystemContext context,
            uint fromState,
            long stateRevision,
            uint transitionId,
            uint causeId,
            Func<bool> isCurrent)
        {
            return ExecuteSynchronousTransition(() =>
            {
                if (m_stateRevision != stateRevision || GetCurrentStateId() != fromState || !isCurrent())
                {
                    return StatusCodes.BadInvalidState;
                }
                return transitionId == 0
                    ? DoCause(context, null, causeId, default, [])
                    : DoTransition(context, transitionId, causeId, default, []);
            });
        }

        /// <summary>
        /// Checks a transition before asynchronous work without retaining a synchronization primitive.
        /// </summary>
        internal ServiceResult PrepareTransition(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            out PreparedStateMachineTransition prepared)
        {
            PreparedStateMachineTransition captured = default;
            ServiceResult result = ExecuteSynchronousTransition(
                () => PrepareTransitionCore(
                    context, transitionId, causeId, inputArguments, outputArguments, out captured));
            prepared = captured;
            return result;
        }

        /// <summary>
        /// Commits previously validated work only while its captured state revision is current.
        /// </summary>
        internal ServiceResult CommitTransition(
            ISystemContext context,
            PreparedStateMachineTransition prepared,
            Action committed)
        {
            return ExecuteSynchronousTransition(
                () => CommitTransitionCore(context, prepared, committed, suppressEvents: true));
        }

        /// <summary>
        /// Coalesces related-node synchronization until the outer transition invocation completes.
        /// </summary>
        internal void ScheduleTransitionCompletion(NodeId nodeId, Action completion)
        {
            lock (m_transitionLock)
            {
                if (!m_transitionCompletions.ContainsKey(nodeId))
                {
                    m_transitionCompletionOrder.Enqueue(nodeId);
                }
                m_transitionCompletions[nodeId] = completion;
            }
        }

        /// <summary>
        /// Queues one lifecycle invocation after related-node synchronization, outside the transition lock.
        /// </summary>
        internal void ScheduleTransitionObserver(Action observer)
        {
            lock (m_transitionLock)
            {
                m_transitionObserverQueue.Enqueue(observer);
            }
        }

        /// <summary>
        /// Schedules native child synchronization before asynchronous lifecycle observers.
        /// </summary>
        internal void ScheduleAsyncTransitionCompletion(NodeId nodeId, Func<ValueTask> completion)
        {
            if (!m_asyncTransitionCompletions.ContainsKey(nodeId))
            {
                m_asyncTransitionCompletionOrder.Enqueue(nodeId);
            }
            m_asyncTransitionCompletions[nodeId] = completion;
        }

        /// <summary>
        /// Schedules an awaited lifecycle observer after native child synchronization.
        /// </summary>
        internal void ScheduleAsyncTransitionObserver(Func<ValueTask> observer)
        {
            m_asyncTransitionObservers.Enqueue(observer);
        }

        /// <summary>
        /// Attempts each asynchronous completion and observer, retaining every observed failure.
        /// </summary>
        private async ValueTask DispatchTransitionCompletionsAsync(List<Exception> failures)
        {
            while (m_asyncTransitionCompletionOrder.Count != 0 || m_asyncTransitionObservers.Count != 0)
            {
                Func<ValueTask> completion;
                if (m_asyncTransitionCompletionOrder.Count != 0)
                {
                    NodeId nodeId = m_asyncTransitionCompletionOrder.Dequeue();
                    completion = m_asyncTransitionCompletions[nodeId];
                    m_asyncTransitionCompletions.Remove(nodeId);
                }
                else
                {
                    completion = m_asyncTransitionObservers.Dequeue();
                }
                try
                {
                    await completion().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddFailure(failures, ex);
                }
            }
        }

        private void DispatchTransitionCompletions()
        {
            if (m_transitionLock.IsHeldByCurrentThread)
            {
                return;
            }
            lock (m_transitionLock)
            {
                if (m_dispatchingCompletions ||
                    (m_transitionCompletionOrder.Count == 0 && m_transitionObserverQueue.Count == 0))
                {
                    return;
                }
                m_dispatchingCompletions = true;
            }

            bool drained = false;
            try
            {
                while (true)
                {
                    Action completion;
                    lock (m_transitionLock)
                    {
                        if (m_transitionCompletionOrder.Count != 0)
                        {
                            NodeId nodeId = m_transitionCompletionOrder.Dequeue();
                            completion = m_transitionCompletions[nodeId];
                            m_transitionCompletions.Remove(nodeId);
                        }
                        else if (m_transitionObserverQueue.Count != 0)
                        {
                            completion = m_transitionObserverQueue.Dequeue();
                        }
                        else
                        {
                            m_dispatchingCompletions = false;
                            drained = true;
                            return;
                        }
                    }
                    completion();
                }
            }
            finally
            {
                if (!drained)
                {
                    lock (m_transitionLock)
                    {
                        m_dispatchingCompletions = false;
                    }
                }
            }
        }

        /// <summary>
        /// Runs transition-local callbacks and commits the new state only if the captured revision remains current.
        /// </summary>
        private ServiceResult DoTransitionCore(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult result = PrepareTransitionCore(
                context, transitionId, causeId, inputArguments, outputArguments,
                out PreparedStateMachineTransition prepared);
            return ServiceResult.IsBad(result)
                ? result
                : CommitTransitionCore(context, prepared, null, suppressEvents: false);
        }

        private ServiceResult PrepareTransitionCore(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            out PreparedStateMachineTransition prepared,
            bool nativeAsync = false)
        {
            prepared = default;
            if (CurrentState?.StatusCode == StatusCodes.BadStateNotActive)
            {
                return StatusCodes.BadStateNotActive;
            }
            long revision = m_stateRevision;
            uint fromState = GetCurrentStateId();
            // check for valid transition.
            uint newState = GetNewStateForTransition(context, transitionId);

            if (newState == 0)
            {
                return StatusCodes.BadNotSupported;
            }

            // check the cause permissions.
            if (causeId != 0 && !IsCausePermitted(context, causeId, true))
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // do any pre-transition processing.
            StateMachineTransitionHandler? before;
            StateMachineTransitionHandler? after = OnAfterTransition;
            StateMachineTransitionAsyncHandler? afterAsync = nativeAsync ? OnAfterTransitionAsync : null;
            if (nativeAsync && AsyncTransitionCallbackFactory != null)
            {
                (before, afterAsync) = AsyncTransitionCallbackFactory(
                    fromState, newState, OnBeforeTransition, OnAfterTransition, OnAfterTransitionAsync);
                after = null;
            }
            else
            {
                (before, after) =
                    TransitionCallbackFactory?.Invoke(fromState, newState, OnBeforeTransition, OnAfterTransition)
                    ?? (OnBeforeTransition, OnAfterTransition);
            }
            ServiceResult result = nativeAsync
                ? before?.Invoke(
                    context, this, transitionId, causeId, inputArguments, outputArguments) ?? ServiceResult.Good
                : InvokeCallback(
                    before, context, this, transitionId, causeId, inputArguments, outputArguments);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            if (m_stateRevision != revision || GetCurrentStateId() != fromState)
            {
                return StatusCodes.BadInvalidState;
            }

            prepared = new PreparedStateMachineTransition(
                this, revision, fromState, newState, transitionId, causeId,
                inputArguments, outputArguments, after, afterAsync);
            return ServiceResult.Good;
        }

        private ServiceResult CommitTransitionCore(
            ISystemContext context,
            PreparedStateMachineTransition prepared,
            Action? committed,
            bool suppressEvents)
        {
            if (!ReferenceEquals(prepared.Machine, this) ||
                m_stateRevision != prepared.Revision || GetCurrentStateId() != prepared.FromState)
            {
                return StatusCodes.BadInvalidState;
            }

            // save the last state.
            LastState = CoreUtils.Clone(CurrentState);

            // update state and transition variables.
            UpdateStateVariable(context, prepared.ToState, CurrentState);
            UpdateTransitionVariable(context, prepared.TransitionId, LastTransition);
            m_stateRevision++;
            committed?.Invoke();

            TransitionEventState? transitionEvent = null;
            if (AreEventsMonitored && !SuppressTransitionEvents && !suppressEvents)
            {
                transitionEvent = CreateTransitionEvent(context, prepared.TransitionId, prepared.CauseId);
                if (transitionEvent != null)
                {
                    UpdateTransitionEvent(context, prepared.TransitionId, prepared.CauseId, transitionEvent);
                }
            }

            // do any post-transition processing.
            InvokeCallback(
                prepared.After,
                context,
                this,
                prepared.TransitionId,
                prepared.CauseId,
                prepared.InputArguments,
                prepared.OutputArguments);

            // report the event.
            if (transitionEvent != null)
            {
                ReportEvent(context, transitionEvent);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates an instance of an transition event.
        /// </summary>
        protected virtual TransitionEventState? CreateTransitionEvent(
            ISystemContext context,
            uint transitionId,
            uint causeId)
        {
            if (TransitionHasEffect(context, transitionId))
            {
                return new TransitionEventState(null);
            }

            return null;
        }

        /// <summary>
        /// Updates a transition event after the transition is complete.
        /// </summary>
        protected virtual void UpdateTransitionEvent(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            TransitionEventState e)
        {
            var info = new TranslationInfo(
                "StateTransition",
                "en-US",
                "The {0} state machine moved to the {1} state.",
                GetDisplayPath(3, '.'),
                CurrentState?.Value!); // params object[] element type is non-nullable; null is acceptable as a format arg

            e.Initialize(context, this, EventSeverity.Medium, new LocalizedText(info));

            e.SetChildValue(context, BrowseNames.FromState, LastState, false);
            e.SetChildValue(context, BrowseNames.ToState, CurrentState, false);
            e.SetChildValue(context, BrowseNames.Transition, LastTransition, false);
        }

        private uint m_causeId;

        /// <summary>
        /// Serializes state changes and the checks that reject superseded transitions.
        /// </summary>
        private readonly Lock m_transitionLock = new();

        /// <summary>
        /// Identifies callbacks executing within one asynchronous invocation.
        /// </summary>
        private readonly AsyncLocal<AsyncTransitionAdmission?> m_asyncAdmission = new();

        /// <summary>
        /// Signals completion of a logical admission without retaining a lock across awaits.
        /// </summary>
        private TaskCompletionSource<bool> m_admissionChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Excludes synchronous mutations until asynchronous notification delivery finishes.
        /// </summary>
        private bool m_asyncTransitionActive;

        /// <summary>
        /// Keeps admission closed through synchronous outer-invocation completion dispatch.
        /// </summary>
        private int m_synchronousAdmissions;
        private readonly Dictionary<NodeId, Action> m_transitionCompletions = [];
        private readonly Queue<NodeId> m_transitionCompletionOrder = [];
        private readonly Queue<Action> m_transitionObserverQueue = [];

        /// <summary>
        /// Native completion work owned exclusively by the current asynchronous admission.
        /// </summary>
        private readonly Dictionary<NodeId, Func<ValueTask>> m_asyncTransitionCompletions = [];

        /// <summary>
        /// Preserves native child-completion ordering.
        /// </summary>
        private readonly Queue<NodeId> m_asyncTransitionCompletionOrder = [];

        /// <summary>
        /// Preserves native lifecycle-observer ordering after child synchronization.
        /// </summary>
        private readonly Queue<Func<ValueTask>> m_asyncTransitionObservers = [];
        private bool m_dispatchingCompletions;

        /// <summary>
        /// Advances on every state update so delayed work cannot act on a later entry into the same state.
        /// </summary>
        private long m_stateRevision;
        private ILogger m_logger = LoggerUtils.Null.Logger;

        /// <summary>
        /// Tracks logical ownership and the single internal cause-to-transition forwarding call.
        /// </summary>
        private sealed class AsyncTransitionAdmission
        {
            /// <summary>
            /// Indicates that notifications belonging to this invocation are still being delivered.
            /// </summary>
            public bool Active { get; set; } = true;

            /// <summary>
            /// Authorizes only the cause's direct call into its virtual asynchronous transition.
            /// </summary>
            public bool ForwardingTransition { get; set; }

            /// <summary>
            /// Shares operation depth across all machines reserved by one composite owner.
            /// </summary>
            public CompositeStateAdmission? Composite { get; set; }
        }

        /// <summary>
        /// Distinguishes owned sequential operations from callback reentrancy throughout a composite subtree.
        /// </summary>
        private sealed class CompositeStateAdmission
        {
            /// <summary>
            /// Counts native operations, including trusted internal child synchronization.
            /// </summary>
            public int OperationDepth;
        }
    }

    /// <summary>
    /// An immutable transition authorization bound to one state-machine revision.
    /// </summary>
    internal readonly record struct PreparedStateMachineTransition(
        FiniteStateMachineState Machine,
        long Revision,
        uint FromState,
        uint ToState,
        uint TransitionId,
        uint CauseId,
        ArrayOf<Variant> InputArguments,
        List<Variant> OutputArguments,
        StateMachineTransitionHandler? After,
        StateMachineTransitionAsyncHandler? AfterAsync = null);

    /// <summary>
    /// A delegate used to receive notifications when a state machine transition occurs.
    /// </summary>
    public delegate ServiceResult StateMachineTransitionHandler(
        ISystemContext context,
        StateMachineState machine,
        uint transitionId,
        uint causeId,
        ArrayOf<Variant> inputArguments,
        List<Variant>? outputArguments);

    /// <summary>
    /// Creates before and after callbacks bound to one transition's source and destination states.
    /// </summary>
    internal delegate (StateMachineTransitionHandler? Before, StateMachineTransitionHandler? After)
        StateMachineTransitionCallbackFactory(
            uint fromState,
            uint toState,
            StateMachineTransitionHandler? before,
            StateMachineTransitionHandler? after);

    /// <summary>
    /// Awaits a transition-local coordinator while logical admission remains owned.
    /// </summary>
    public delegate ValueTask<ServiceResult> StateMachineTransitionAsyncHandler(
        ISystemContext context,
        StateMachineState machine,
        uint transitionId,
        uint causeId,
        ArrayOf<Variant> inputArguments,
        List<Variant>? outputArguments,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates native callbacks without routing through synchronous builder completion or observer dispatch.
    /// </summary>
    internal delegate (StateMachineTransitionHandler? Before, StateMachineTransitionAsyncHandler? After)
        StateMachineAsyncTransitionCallbackFactory(
            uint fromState,
            uint toState,
            StateMachineTransitionHandler? before,
            StateMachineTransitionHandler? after,
            StateMachineTransitionAsyncHandler? afterAsync);

    /// <summary>
    /// Source-generated log messages for <see cref="FiniteStateMachineState"/>.
    /// </summary>
    internal static partial class FiniteStateMachineStateLog
    {
        [LoggerMessage(EventId = CoreTypesEventIds.FiniteStateMachineState + 0, Level = LogLevel.Error,
            Message = "Error while reporting AuditProgramTransitionEvent event.")]
        public static partial void ReportAuditProgramTransitionError(this ILogger logger, Exception exception);
    }
}
