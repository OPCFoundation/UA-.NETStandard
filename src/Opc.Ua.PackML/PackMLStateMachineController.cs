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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PackML
{
    /// <summary>
    /// The commands of the OPC 30050 PackML state machines.
    /// </summary>
    public enum PackMLCommand
    {
        /// <summary>Base state machine: Cleared to Aborting.</summary>
        Abort,

        /// <summary>Base state machine: Aborted to Cleared.</summary>
        Clear,

        /// <summary>Machine state machine: Running to Stopping.</summary>
        Stop,

        /// <summary>
        /// Machine state machine: Stopped to Running; execute state machine:
        /// Complete to Resetting.
        /// </summary>
        Reset,

        /// <summary>Execute state machine: Idle to Starting.</summary>
        Start,

        /// <summary>Execute state machine: to Holding.</summary>
        Hold,

        /// <summary>Execute state machine: Held to Unholding.</summary>
        Unhold,

        /// <summary>Execute state machine: Execute to Suspending.</summary>
        Suspend,

        /// <summary>Execute state machine: Suspended to Unsuspending.</summary>
        Unsuspend,

        /// <summary>Execute state machine: Execute to Completing.</summary>
        ToComplete
    }

    /// <summary>
    /// The state a <see cref="PackMLStateMachineController"/> starts in.
    /// </summary>
    public enum PackMLInitialState
    {
        /// <summary>Aborted: the machine has to be cleared first.</summary>
        Aborted,

        /// <summary>Cleared/Stopped: the machine has to be reset first.</summary>
        Stopped,

        /// <summary>Cleared/Running/Idle: the machine is ready to start.</summary>
        Idle
    }

    /// <summary>
    /// Reports a change of the innermost active PackML state.
    /// </summary>
    public sealed class PackMLStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the event arguments.
        /// </summary>
        /// <param name="previousState">The previous state number.</param>
        /// <param name="currentState">The new state number.</param>
        public PackMLStateChangedEventArgs(uint previousState, uint currentState)
        {
            PreviousState = previousState;
            CurrentState = currentState;
        }

        /// <summary>
        /// Gets the previous <see cref="PackMLStateNumbers"/> value.
        /// </summary>
        public uint PreviousState { get; }

        /// <summary>
        /// Gets the new <see cref="PackMLStateNumbers"/> value.
        /// </summary>
        public uint CurrentState { get; }
    }

    /// <summary>
    /// Drives an OPC 30050 PackML base state machine instance together with
    /// its nested machine and execute state machines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three generated state machines each know their own states and
    /// transitions (see <c>PackMLStateMachineTables.cs</c>); what no single
    /// machine knows is the nesting. <c>MachineState</c> is the sub-state
    /// machine of the base machine's Cleared state and <c>ExecuteState</c> the
    /// sub-state machine of the machine state machine's Running state. The
    /// controller activates a sub-state machine in its initial state when the
    /// parent enters the hosting state, and deactivates it - an empty
    /// <c>CurrentState</c> with <c>Bad_StateNotActive</c>, as OPC 10000-16
    /// prescribes - when the parent leaves it.
    /// </para>
    /// <para>
    /// The PackML methods (Abort, Clear, Stop, Reset, Start, Hold, Unhold,
    /// Suspend, Unsuspend, ToComplete) are bound to the matching causes, and
    /// their <c>Executable</c> attribute follows the current state. The
    /// acting states (Aborting, Clearing, Stopping, Resetting, Starting,
    /// Completing, Holding, Unholding, Suspending, Unsuspending) are left
    /// either automatically - the default, for equipment that has nothing to
    /// wait for - or by <see cref="CompleteActingState"/> once the equipment
    /// reports the state complete.
    /// </para>
    /// </remarks>
    public sealed class PackMLStateMachineController
    {
        /// <summary>
        /// Creates a controller for a materialised PackML base state machine.
        /// </summary>
        /// <param name="context">The system context of the owning node manager.</param>
        /// <param name="stateMachine">The base state machine instance.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// The instance lacks its mandatory machine or execute state machine.
        /// </exception>
        public PackMLStateMachineController(
            ISystemContext context,
            PackMLBaseStateMachineState stateMachine)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            m_machine = stateMachine.MachineState ?? throw new ArgumentException(
                "The PackML base state machine has no MachineState.",
                nameof(stateMachine));
            m_execute = m_machine.ExecuteState ?? throw new ArgumentException(
                "The PackML machine state machine has no ExecuteState.",
                nameof(stateMachine));
            m_namespaceIndex = (ushort)context.NamespaceUris.GetIndexOrAppend(Namespaces.PackML);
            BindMethods();
        }

        /// <summary>
        /// Gets the base state machine this controller drives.
        /// </summary>
        public PackMLBaseStateMachineState StateMachine { get; }

        /// <summary>
        /// Gets or sets whether acting states are completed as soon as they
        /// are entered. Defaults to <see langword="true"/>.
        /// </summary>
        public bool AutoCompleteActingStates
        {
            get
            {
                lock (m_lock)
                {
                    return m_autoCompleteActingStates;
                }
            }
            set
            {
                lock (m_lock)
                {
                    ThrowIfNotificationPending();
                    m_autoCompleteActingStates = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets a guard consulted before every command, from a method
        /// call or from <see cref="Execute"/>. A bad result rejects the
        /// command with that result.
        /// </summary>
        /// <remarks>
        /// The guard is called outside the controller's lock, so it may take
        /// its time; whether the command is still allowed is checked again
        /// once it returns.
        /// </remarks>
        public Func<PackMLCommand, ServiceResult>? CommandGuard { get; set; }

        /// <summary>
        /// Gets the parameters passed with the last accepted Start command.
        /// </summary>
        public ArrayOf<PackMLDescriptorDataType> StartParameters
        {
            get
            {
                lock (m_lock)
                {
                    return m_startParameters;
                }
            }
        }

        /// <summary>
        /// Raised after the innermost active state changed.
        /// </summary>
        /// <remarks>
        /// The event is raised after the controller's lock is released, so a
        /// handler may call back into the controller from any thread. Changes
        /// are reported in the order they happened; one that happens while a
        /// handler runs - also one the handler causes - is reported after the
        /// handler returns.
        /// </remarks>
        public event EventHandler<PackMLStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Gets the <see cref="PackMLStateNumbers"/> value of the innermost
        /// active state.
        /// </summary>
        public uint CurrentState
        {
            get
            {
                lock (m_lock)
                {
                    return CurrentStateCore();
                }
            }
        }

        /// <summary>
        /// Gets the active host-defined mode key.
        /// </summary>
        public uint CurrentMode
        {
            get
            {
                lock (m_lock)
                {
                    return m_currentMode;
                }
            }
        }

        /// <summary>
        /// Gets the state numbers supported in the active mode.
        /// </summary>
        public ArrayOf<uint> SupportedStates
        {
            get
            {
                lock (m_lock)
                {
                    uint[] states = m_supportedStates.ToArray();
                    Array.Sort(states);
                    return ArrayOf.Wrapped(states);
                }
            }
        }

        /// <summary>
        /// Sets the supported state numbers for the active mode.
        /// Transitions with an unsupported source or destination state are
        /// removed from the state machines' <c>AvailableTransitions</c>.
        /// </summary>
        /// <param name="stateNumbers">The supported PackML state numbers.</param>
        /// <returns>
        /// <c>Good</c>, or <c>BadInvalidArgument</c> for an unknown state
        /// number, or <c>BadInvalidState</c> if the active state would be
        /// excluded.
        /// </returns>
        public ServiceResult SetSupportedStates(ArrayOf<uint> stateNumbers)
        {
            if (!TryCreateSupportedStates(stateNumbers, out HashSet<uint> states))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The supported PackML states contain an unknown or duplicate state number.");
            }

            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                if (!AllActiveStatesSupported(states))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The supported PackML states exclude an active state.");
                }
                m_modeStates[m_currentMode] = states;
                m_supportedStates = states;
                RefreshAvailability();
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Sets the supported state numbers for a PackML mode.
        /// </summary>
        /// <param name="mode">The host-defined mode key.</param>
        /// <param name="stateNumbers">The supported PackML state numbers.</param>
        /// <returns>
        /// <c>Good</c>, or <c>BadInvalidArgument</c> for an unknown state
        /// number, or <c>BadInvalidState</c> if the active state would
        /// be excluded.
        /// </returns>
        public ServiceResult SetSupportedStates(uint mode, ArrayOf<uint> stateNumbers)
        {
            if (!TryCreateSupportedStates(stateNumbers, out HashSet<uint> states))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The PackML mode or supported state numbers are invalid.");
            }

            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                if (mode == m_currentMode && !AllActiveStatesSupported(states))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The supported PackML states exclude an active state.");
                }
                m_modeStates[mode] = states;
                if (mode == m_currentMode)
                {
                    m_supportedStates = states;
                    RefreshAvailability();
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Activates a previously configured PackML mode.
        /// </summary>
        /// <param name="mode">The host-defined mode key.</param>
        /// <returns>
        /// <c>Good</c>, or <c>BadInvalidState</c> if the mode excludes an
        /// active state. Unconfigured modes support all PackML states.
        /// </returns>
        public ServiceResult SetMode(uint mode)
        {
            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                HashSet<uint> states = m_modeStates.TryGetValue(mode, out HashSet<uint>? configured)
                    ? configured
                    : new HashSet<uint>(s_allStateNumbers);
                if (!AllActiveStatesSupported(states))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "The selected PackML mode excludes an active state.");
                }
                m_currentMode = mode;
                m_supportedStates = states;
                RefreshAvailability();
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the active state and mode permit a command.
        /// This check does not invoke <see cref="CommandGuard"/>; execution
        /// rechecks state and mode after any external asynchronous work.
        /// </summary>
        /// <param name="command">The command to check.</param>
        /// <returns>
        /// <see langword="true"/> when the command is permitted by the active
        /// state machine configuration.
        /// </returns>
        public bool CanExecute(PackMLCommand command)
        {
            lock (m_lock)
            {
                return Resolve(command) != null;
            }
        }

        /// <summary>
        /// Configures the active mode's supported states and initializes the
        /// state machines in one operation.
        /// </summary>
        /// <param name="states">The supported PackML state numbers.</param>
        /// <param name="initialState">The state to initialize.</param>
        /// <exception cref="ArgumentException">
        /// The state list is invalid or does not include the requested initial
        /// state.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The initial state value is not defined.
        /// </exception>
        public void ConfigureStates(ArrayOf<uint> states, PackMLInitialState initialState)
        {
            lock (m_lock)
            {
                ThrowIfNotificationPending();
                ConfigureStatesCore(states, initialState, notify: true);
            }
            RaisePendingChanges();
        }

        /// <summary>
        /// Configures the active mode's supported states and initializes the
        /// state machines, then awaits the state-change notification of the
        /// node and its children.
        /// </summary>
        /// <remarks>
        /// Cancellation is observed before the state machines change. An
        /// applied change is always published, so the notification ignores
        /// the token. Callers that need every intermediate value sampled
        /// serialize their asynchronous calls.
        /// </remarks>
        /// <param name="states">The supported PackML state numbers.</param>
        /// <param name="initialState">The state to initialize.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ArgumentException">
        /// The state list is invalid or does not include the requested initial
        /// state.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The initial state value is not defined.
        /// </exception>
        public async ValueTask ConfigureStatesAsync(
            ArrayOf<uint> states,
            PackMLInitialState initialState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BeginAsyncWriteAsync(cancellationToken).ConfigureAwait(false);
            m_asyncOwner.Value = true;
            try
            {
                await RunStateOperationAsync(async token =>
                {
                    token.ThrowIfCancellationRequested();
                    ConfigureStatesCore(states, initialState, notify: false);
                    await PublishChangesAsync(changed: true).ConfigureAwait(false);
                    return ServiceResult.Good;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_asyncOwner.Value = false;
                EndAsyncWrite();
            }
        }

        /// <summary>
        /// Validates and applies a mode's supported states and initial state.
        /// </summary>
        private void ConfigureStatesCore(
            ArrayOf<uint> states,
            PackMLInitialState initialState,
            bool notify)
        {
            if (!TryCreateSupportedStates(states, out HashSet<uint> supportedStates))
            {
                throw new ArgumentException(
                    "The supported PackML states contain an unknown or duplicate state number.",
                    nameof(states));
            }

            lock (m_lock)
            {
                uint[] initialStates = InitialStateNumbers(initialState);
                if (initialStates.Any(state => !supportedStates.Contains(state)))
                {
                    throw new ArgumentException(
                        "The initial state is excluded by the configured supported states.",
                        nameof(initialState));
                }
                m_modeStates[m_currentMode] = supportedStates;
                m_supportedStates = supportedStates;
                InitializeCore(initialState, notify);
            }
        }

        /// <summary>
        /// Puts the three state machines into a consistent initial state
        /// without reporting transition events, and publishes the
        /// <c>AvailableStates</c> and <c>AvailableTransitions</c> of each.
        /// </summary>
        /// <param name="initialState">The state to start in.</param>
        /// <exception cref="ArgumentException">
        /// The initial state is excluded by the active mode's supported states.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The initial state value is not defined.
        /// </exception>
        public void Initialize(PackMLInitialState initialState = PackMLInitialState.Stopped)
        {
            lock (m_lock)
            {
                ThrowIfNotificationPending();
                InitializeCore(initialState, notify: true);
            }
            RaisePendingChanges();
        }

        /// <summary>
        /// Initializes all nested states while the controller owns serialization.
        /// Without <paramref name="notify"/> the caller publishes the change
        /// masks after releasing the lock.
        /// </summary>
        private void InitializeCore(PackMLInitialState initialState, bool notify)
        {
            uint[] initialStates = InitialStateNumbers(initialState);
            if (initialStates.Any(state => !m_supportedStates.Contains(state)))
            {
                throw new ArgumentException(
                    "The initial state is excluded by the active mode's supported states.",
                    nameof(initialState));
            }
            uint previous = CurrentStateCore();
            PublishAvailable();
            switch (initialState)
            {
                case PackMLInitialState.Aborted:
                    Enter(StateMachine, Objects.PackMLBaseStateMachineType_Aborted);
                    Deactivate(m_machine);
                    Deactivate(m_execute);
                    break;
                case PackMLInitialState.Idle:
                    Enter(StateMachine, Objects.PackMLBaseStateMachineType_Cleared);
                    Enter(m_machine, Objects.PackMLMachineStateMachineType_Running);
                    Enter(m_execute, Objects.PackMLExecuteStateMachineType_Idle);
                    break;
                case PackMLInitialState.Stopped:
                    Enter(StateMachine, Objects.PackMLBaseStateMachineType_Cleared);
                    Enter(m_machine, Objects.PackMLMachineStateMachineType_Stopped);
                    Deactivate(m_execute);
                    break;
            }
            UpdateExecutable();
            if (notify)
            {
                StateMachine.ClearChangeMasks(m_context, includeChildren: true);
            }
            QueueChanged(previous);
        }

        /// <summary>
        /// Gets the active state and parent states required by an initial state.
        /// </summary>
        private static uint[] InitialStateNumbers(PackMLInitialState initialState)
        {
            return initialState switch
            {
                PackMLInitialState.Aborted => [PackMLStateNumbers.Aborted],
                PackMLInitialState.Stopped => [PackMLStateNumbers.Cleared, PackMLStateNumbers.Stopped],
                PackMLInitialState.Idle =>
                [
                    PackMLStateNumbers.Cleared,
                    PackMLStateNumbers.Running,
                    PackMLStateNumbers.Idle
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(initialState))
            };
        }

        /// <summary>
        /// Executes a PackML command, exactly as the matching method would.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="startParameters">
        /// The parameters of a <see cref="PackMLCommand.Start"/>; ignored
        /// otherwise.
        /// </param>
        /// <returns>
        /// <c>Good</c>, or <c>Bad_InvalidState</c> when the command is not
        /// allowed in the current state, or the guard's result.
        /// </returns>
        public ServiceResult Execute(
            PackMLCommand command,
            ArrayOf<PackMLDescriptorDataType> startParameters = default)
        {
            return ExecuteGuarded(command, null, startParameters);
        }

        /// <summary>
        /// Executes a PackML command and awaits the state-change notification
        /// of the state machines.
        /// </summary>
        /// <remarks>
        /// The command follows the same guard and state rules as
        /// <see cref="Execute"/>. The cause runs through the native
        /// <c>DoCauseAsync</c> and <c>DoTransitionAsync</c> counterparts, so a machine's cause
        /// mapping and <c>OnCheckUserPermission</c> handler apply as in
        /// <see cref="Execute"/>, and its transition events are
        /// reported with <see cref="NodeState.ReportEventAsync"/> before the
        /// next transition. A machine subclass that customizes
        /// <c>DoCause</c> or <c>DoTransition</c> must override their async
        /// counterparts for this method; a synchronous-only override is used by
        /// <see cref="Execute"/> and the Methods, not here. Cancellation is
        /// observed before the state machines change. An applied change is
        /// always published, so the notification ignores the token. Callers
        /// are serialized through notification delivery. Overlapping synchronous
        /// writes return BadInvalidState; void setters throw ServiceResultException.
        /// Notification callbacks must not await another operation on this controller.
        /// </remarks>
        /// <param name="command">The command.</param>
        /// <param name="startParameters">
        /// The parameters of a <see cref="PackMLCommand.Start"/>; ignored
        /// otherwise.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// <c>Good</c>, or <c>Bad_InvalidState</c> when the command is not
        /// allowed in the current state, or the guard's result.
        /// </returns>
        public async ValueTask<ServiceResult> ExecuteAsync(
            PackMLCommand command,
            ArrayOf<PackMLDescriptorDataType> startParameters = default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BeginAsyncWriteAsync(cancellationToken).ConfigureAwait(false);
            m_asyncOwner.Value = true;
            try
            {
                return await RunStateOperationAsync(async token =>
                {
                    (FiniteStateMachineState Machine, uint Cause)? target;
                    uint previous;
                    lock (m_lock)
                    {
                        target = Resolve(command);
                        if (target == null)
                        {
                            return NotAllowed(command);
                        }
                        previous = CurrentStateCore();
                    }
                    ServiceResult guard = Guard(command);
                    if (ServiceResult.IsBad(guard))
                    {
                        return guard;
                    }
                    token.ThrowIfCancellationRequested();
                    ServiceResult result = await target.Value.Machine.DoCauseAsync(
                        m_context, null, target.Value.Cause, default, [], token).ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }
                    lock (m_lock)
                    {
                        if (command == PackMLCommand.Start)
                        {
                            m_startParameters = startParameters;
                        }
                        OnEntered(target.Value.Machine);
                    }
                    await SettleAsync(previous).ConfigureAwait(false);
                    return ServiceResult.Good;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_asyncOwner.Value = false;
                EndAsyncWrite();
            }
        }

        /// <summary>
        /// Completes the current acting state (the ISA-TR88 "state complete"
        /// event), moving on to the state that follows it.
        /// </summary>
        /// <returns>
        /// <c>Good</c>, <c>Bad_InvalidState</c> when no acting state is
        /// active, or the result the completion transition failed with - for
        /// example the veto of an <c>OnBeforeTransition</c> handler.
        /// </returns>
        public ServiceResult CompleteActingState()
        {
            ServiceResult result;
            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                result = CompleteActingStateCore();
            }
            RaisePendingChanges();
            return result;
        }

        /// <summary>
        /// Completes the current acting state and awaits the state-change
        /// notification of the state machines.
        /// </summary>
        /// <remarks>
        /// The completion transitions run through <c>DoTransitionAsync</c>,
        /// and their events are reported with
        /// <see cref="NodeState.ReportEventAsync"/> after the lock is
        /// released. Cancellation is observed before the state machines
        /// change. An applied change is always published, so the notification
        /// ignores the token. Synchronous writes are rejected while notifications
        /// are pending; callbacks must not await reentrant controller operations.
        /// </remarks>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// <c>Good</c>, <c>Bad_InvalidState</c> when no acting state is
        /// active, or the result the completion transition failed with.
        /// </returns>
        public async ValueTask<ServiceResult> CompleteActingStateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BeginAsyncWriteAsync(cancellationToken).ConfigureAwait(false);
            m_asyncOwner.Value = true;
            try
            {
                return await RunStateOperationAsync(async token =>
                {
                    token.ThrowIfCancellationRequested();
                    uint previous;
                    lock (m_lock)
                    {
                        previous = CurrentStateCore();
                    }
                    ServiceResult? result = await TryCompleteActingStateAsync().ConfigureAwait(false);
                    if (result == null)
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidState,
                            "The PackML state machine is not in an acting state.");
                    }
                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }
                    await SettleAsync(previous).ConfigureAwait(false);
                    return ServiceResult.Good;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_asyncOwner.Value = false;
                EndAsyncWrite();
            }
        }

        /// <summary>
        /// Serializes asynchronous writes and reserves synchronous admission
        /// before the first mutation.
        /// </summary>
        private async ValueTask BeginAsyncWriteAsync(CancellationToken cancellationToken)
        {
            if (m_asyncOwner.Value)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "A PackML callback cannot await a reentrant controller write.");
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task pending;
                lock (m_lock)
                {
                    if (!m_asyncOperationActive)
                    {
                        m_asyncOperationActive = true;
                        m_asyncWrite = true;
                        return;
                    }
                    pending = m_asyncOperationCompleted.Task;
                }
                await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reopens synchronous admission after all notifications finish.
        /// </summary>
        private void EndAsyncWrite()
        {
            TaskCompletionSource<bool> completed;
            lock (m_lock)
            {
                m_asyncWrite = false;
                m_asyncOperationActive = false;
                completed = m_asyncOperationCompleted;
                m_asyncOperationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            completed.TrySetResult(true);
        }

        /// <summary>
        /// Returns the explicit rejection for an overlapping synchronous write.
        /// </summary>
        private static ServiceResult NotificationPending()
        {
            return ServiceResult.Create(
                StatusCodes.BadInvalidState,
                "An asynchronous PackML state notification is pending.");
        }

        /// <summary>
        /// Rejects a void synchronous setter while publication is pending.
        /// </summary>
        private void ThrowIfNotificationPending()
        {
            if (m_asyncWrite)
            {
                throw new ServiceResultException(NotificationPending());
            }
        }

        /// <summary>
        /// Reserves the complete machine hierarchy until publication finishes,
        /// then reopens write admission before delivering controller events.
        /// </summary>
        private async ValueTask<ServiceResult> RunStateOperationAsync(
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            CancellationToken cancellationToken)
        {
            Exception? notificationError = null;
            ServiceResult result = ServiceResult.Good;
            try
            {
                result = await StateMachine.ExecuteWithStateAdmissionAsync(
                    m_context, operation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                notificationError = e;
            }
            lock (m_lock)
            {
                m_asyncWrite = false;
            }
            try
            {
                RaisePendingChanges();
            }
            catch (Exception handlerError) when (notificationError != null)
            {
                throw new AggregateException(
                    "Publishing the PackML state machines and raising StateChanged both failed.",
                    notificationError,
                    handlerError);
            }
            if (notificationError != null)
            {
                ExceptionDispatchInfo.Capture(notificationError).Throw();
            }
            return result;
        }

        /// <summary>
        /// Completes the innermost acting state under the lock.
        /// </summary>
        private ServiceResult CompleteActingStateCore()
        {
            lock (m_lock)
            {
                uint previous = CurrentStateCore();
                if (!TryCompleteActingState(out ServiceResult? failure))
                {
                    return failure ??
                        ServiceResult.Create(
                            StatusCodes.BadInvalidState,
                            "The PackML state machine is not in an acting state.");
                }
                Settle(previous);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Publishes live change masks, then raises the queued
        /// <see cref="StateChanged"/> events.
        /// </summary>
        /// <remarks>
        /// A rejected operation changed no state, so, as on the synchronous
        /// path, it publishes no change masks of the state machines. When the
        /// notification fails and this call also drains the queue and a
        /// <see cref="StateChanged"/> handler fails, an
        /// <see cref="AggregateException"/> carries both failures. A
        /// concurrent command that drains the queue first receives the
        /// handler failure instead.
        /// </remarks>
        /// <param name="changed">Whether the operation changed the state machines.</param>
        private async ValueTask PublishChangesAsync(bool changed)
        {
            if (changed)
            {
                await StateMachine.PublishStateChangesAsync(
                    m_context, includeChildren: true, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Consults the <see cref="CommandGuard"/> and then executes the
        /// command, checking again that it is still allowed.
        /// </summary>
        /// <remarks>
        /// The guard is application code - an equipment interlock - that may
        /// take its time or call back into the controller, so it runs outside
        /// the lock; the lock is held only while the state is read and while
        /// the command is applied.
        /// </remarks>
        private ServiceResult ExecuteGuarded(
            PackMLCommand command,
            MethodState? causeMethod,
            ArrayOf<PackMLDescriptorDataType> startParameters)
        {
            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                if (Resolve(command) == null)
                {
                    return NotAllowed(command);
                }
            }
            ServiceResult guard = Guard(command);
            if (ServiceResult.IsBad(guard))
            {
                return guard;
            }
            ServiceResult result;
            lock (m_lock)
            {
                if (m_asyncWrite)
                {
                    return NotificationPending();
                }
                result = ExecuteCore(command, causeMethod, startParameters);
            }
            RaisePendingChanges();
            return result;
        }

        private ServiceResult Guard(PackMLCommand command)
        {
            Func<PackMLCommand, ServiceResult>? guard = CommandGuard;
            return guard == null ? ServiceResult.Good : guard(command);
        }

        private ServiceResult NotAllowed(PackMLCommand command)
        {
            return ServiceResult.Create(
                StatusCodes.BadInvalidState,
                "The PackML command {0} is not allowed in state {1}.",
                command,
                CurrentStateCore());
        }

        /// <summary>
        /// Applies a command under the lock.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="causeMethod">The called Method, if any.</param>
        /// <param name="startParameters">The parameters of a Start.</param>
        private ServiceResult ExecuteCore(
            PackMLCommand command,
            MethodState? causeMethod,
            ArrayOf<PackMLDescriptorDataType> startParameters)
        {
            (FiniteStateMachineState machine, uint causeId)? target = Resolve(command);
            if (target == null)
            {
                return NotAllowed(command);
            }

            uint previous = CurrentStateCore();
            (FiniteStateMachineState machine, uint causeId) = target.Value;
            ServiceResult result = machine.DoCause(m_context, causeMethod, causeId, default, []);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            if (command == PackMLCommand.Start)
            {
                m_startParameters = startParameters;
            }
            OnEntered(machine);
            Settle(previous);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Finds the machine and cause a command applies to in the current
        /// state, or <see langword="null"/> when it does not apply.
        /// </summary>
        private (FiniteStateMachineState, uint)? Resolve(PackMLCommand command)
        {
            (FiniteStateMachineState machine, uint causeId)[] candidates = command switch
            {
                PackMLCommand.Abort => [(StateMachine, Methods.PackMLBaseStateMachineType_Abort)],
                PackMLCommand.Clear => [(StateMachine, Methods.PackMLBaseStateMachineType_Clear)],
                PackMLCommand.Stop => [(m_machine, Methods.PackMLMachineStateMachineType_Stop)],
                PackMLCommand.Reset =>
                [
                    (m_machine, Methods.PackMLMachineStateMachineType_Reset),
                    (m_execute, Methods.PackMLExecuteStateMachineType_Reset)
                ],
                PackMLCommand.Start => [(m_execute, Methods.PackMLExecuteStateMachineType_Start)],
                PackMLCommand.Hold => [(m_execute, Methods.PackMLExecuteStateMachineType_Hold)],
                PackMLCommand.Unhold => [(m_execute, Methods.PackMLExecuteStateMachineType_Unhold)],
                PackMLCommand.Suspend => [(m_execute, Methods.PackMLExecuteStateMachineType_Suspend)],
                PackMLCommand.Unsuspend => [(m_execute, Methods.PackMLExecuteStateMachineType_Unsuspend)],
                PackMLCommand.ToComplete => [(m_execute, Methods.PackMLExecuteStateMachineType_ToComplete)],
                _ => []
            };
            foreach ((FiniteStateMachineState machine, uint causeId) in candidates)
            {
                if (IsPermittedCause(machine, causeId))
                {
                    return (machine, causeId);
                }
            }
            return null;
        }

        /// <summary>
        /// Applies the nesting rules after <paramref name="machine"/> entered
        /// a new state.
        /// </summary>
        private void OnEntered(FiniteStateMachineState machine)
        {
            uint state = StateOf(machine);
            if (ReferenceEquals(machine, StateMachine))
            {
                if (state == Objects.PackMLBaseStateMachineType_Cleared)
                {
                    if (IsStateSupported(Objects.PackMLMachineStateMachineType_Clearing))
                    {
                        // Entering Cleared starts the machine state machine in
                        // Clearing, which is itself an acting state.
                        Enter(m_machine, Objects.PackMLMachineStateMachineType_Clearing);
                    }
                    else
                    {
                        Deactivate(m_machine);
                    }
                    Deactivate(m_execute);
                }
                else
                {
                    Deactivate(m_machine);
                    Deactivate(m_execute);
                }
            }
            else if (ReferenceEquals(machine, m_machine))
            {
                if (state == Objects.PackMLMachineStateMachineType_Running &&
                    IsStateSupported(Objects.PackMLExecuteStateMachineType_Resetting))
                {
                    Enter(m_execute, Objects.PackMLExecuteStateMachineType_Resetting);
                }
                else
                {
                    Deactivate(m_execute);
                }
            }
        }

        /// <summary>
        /// Leaves acting states when configured to, then refreshes the method
        /// executability and queues the change for reporting.
        /// </summary>
        private void Settle(uint previous)
        {
            if (AutoCompleteActingStates)
            {
                // Each completion can enter a nested acting state (Cleared
                // starts in Clearing, Running in Resetting), so completion
                // repeats until the machine rests. The chain is bounded by the
                // number of acting states.
                for (int ii = 0; ii < 16 && TryCompleteActingState(out _); ii++)
                {
                }
            }
            UpdateExecutable();
            StateMachine.ClearChangeMasks(m_context, includeChildren: true);
            QueueChanged(previous);
        }

        /// <summary>
        /// Completes and publishes each acting-state transition before the
        /// next mutation, then publishes the final composite state.
        /// </summary>
        private async ValueTask SettleAsync(uint previous)
        {
            if (AutoCompleteActingStates)
            {
                for (int ii = 0; ii < 16; ii++)
                {
                    ServiceResult? result = await TryCompleteActingStateAsync().ConfigureAwait(false);
                    if (result == null || ServiceResult.IsBad(result))
                    {
                        break;
                    }
                }
            }
            lock (m_lock)
            {
                UpdateExecutable();
                QueueChanged(previous);
            }
            await PublishChangesAsync(changed: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes a completion transition before applying nested entry rules.
        /// </summary>
        private async ValueTask<ServiceResult?> TryCompleteActingStateAsync()
        {
            FiniteStateMachineState? target = null;
            uint transition = 0;
            lock (m_lock)
            {
                foreach (FiniteStateMachineState machine in InnermostFirst())
                {
                    if (!IsActive(machine))
                    {
                        continue;
                    }
                    transition = CompletionOf(StateOf(machine));
                    if (transition == 0 || !IsTransitionSupported(transition))
                    {
                        return null;
                    }
                    target = machine;
                    break;
                }
            }
            if (target == null)
            {
                return null;
            }
            ServiceResult result = await target.DoTransitionAsync(
                m_context, transition, 0, default, [], CancellationToken.None).ConfigureAwait(false);
            if (ServiceResult.IsGood(result))
            {
                lock (m_lock)
                {
                    OnEntered(target);
                }
            }
            return result;
        }

        /// <summary>
        /// Performs the completion transition of the innermost active acting
        /// state, if there is one.
        /// </summary>
        /// <param name="failure">
        /// The result the transition failed with, or <see langword="null"/>
        /// when no acting state is active.
        /// </param>
        private bool TryCompleteActingState(out ServiceResult? failure)
        {
            failure = null;
            foreach (FiniteStateMachineState machine in InnermostFirst())
            {
                if (!IsActive(machine))
                {
                    continue;
                }
                uint transitionId = CompletionOf(StateOf(machine));
                if (transitionId == 0 || !IsTransitionSupported(transitionId))
                {
                    // The innermost active machine is at rest; an outer
                    // machine's acting state cannot be active beneath it.
                    return false;
                }
                ServiceResult result = machine.DoTransition(m_context, transitionId, 0, default, []);
                if (ServiceResult.IsBad(result))
                {
                    failure = result;
                    return false;
                }
                OnEntered(machine);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The three machines from the innermost (execute) to the outermost
        /// (base), the order in which an active state is looked up.
        /// </summary>
        private FiniteStateMachineState[] InnermostFirst()
        {
            return [m_execute, m_machine, StateMachine];
        }

        private static uint CompletionOf(uint state)
        {
            return state switch
            {
                Objects.PackMLBaseStateMachineType_Aborting =>
                    Objects.PackMLBaseStateMachineType_AbortingToAborted,
                Objects.PackMLMachineStateMachineType_Clearing =>
                    Objects.PackMLMachineStateMachineType_ClearingToStopped,
                Objects.PackMLMachineStateMachineType_Stopping =>
                    Objects.PackMLMachineStateMachineType_StoppingToStopped,
                Objects.PackMLExecuteStateMachineType_Resetting =>
                    Objects.PackMLExecuteStateMachineType_ResettingToIdle,
                Objects.PackMLExecuteStateMachineType_Starting =>
                    Objects.PackMLExecuteStateMachineType_StartingToExecute,
                Objects.PackMLExecuteStateMachineType_Completing =>
                    Objects.PackMLExecuteStateMachineType_CompletingToComplete,
                Objects.PackMLExecuteStateMachineType_Holding =>
                    Objects.PackMLExecuteStateMachineType_HoldingToHeld,
                Objects.PackMLExecuteStateMachineType_Unholding =>
                    Objects.PackMLExecuteStateMachineType_UnholdingToExecute,
                Objects.PackMLExecuteStateMachineType_Suspending =>
                    Objects.PackMLExecuteStateMachineType_SuspendingToSuspended,
                Objects.PackMLExecuteStateMachineType_Unsuspending =>
                    Objects.PackMLExecuteStateMachineType_UnsuspendingToExecute,
                _ => 0
            };
        }

        private void BindMethods()
        {
            Bind(StateMachine.Abort, PackMLCommand.Abort);
            Bind(StateMachine.Clear, PackMLCommand.Clear);
            Bind(m_machine.Stop, PackMLCommand.Stop);
            BindCause(m_machine, m_machine.Reset, Methods.PackMLMachineStateMachineType_Reset);
            BindCause(m_execute, m_execute.Reset, Methods.PackMLExecuteStateMachineType_Reset);
            Bind(m_execute.Hold, PackMLCommand.Hold);
            Bind(m_execute.Unhold, PackMLCommand.Unhold);
            Bind(m_execute.Suspend, PackMLCommand.Suspend);
            Bind(m_execute.Unsuspend, PackMLCommand.Unsuspend);
            Bind(m_execute.ToComplete, PackMLCommand.ToComplete);
            if (m_execute.Start is StartMethodState start)
            {
                start.OnCall = (context, method, objectId, parameters) =>
                    ExecuteGuarded(PackMLCommand.Start, method, parameters);
            }
        }

        private void Bind(MethodState? method, PackMLCommand command)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (context, calledMethod, objectId, inputs, outputs) =>
                ExecuteGuarded(command, calledMethod, default);
        }

        /// <summary>
        /// Binds a Reset method to its own machine. Reset exists on both the
        /// machine and the execute state machine and means a different
        /// transition on each, so the method must not be resolved by command.
        /// </summary>
        private void BindCause(
            FiniteStateMachineState machine,
            MethodState? method,
            uint causeId)
        {
            if (method == null)
            {
                return;
            }
            method.OnCallMethod2 = (context, calledMethod, objectId, inputs, outputs) =>
            {
                // The guard runs outside the lock, as in ExecuteGuarded.
                lock (m_lock)
                {
                    if (m_asyncWrite)
                    {
                        return NotificationPending();
                    }
                    if (!IsPermittedCause(machine, causeId))
                    {
                        return ResetNotAllowed();
                    }
                }
                ServiceResult guard = Guard(PackMLCommand.Reset);
                if (ServiceResult.IsBad(guard))
                {
                    return guard;
                }
                lock (m_lock)
                {
                    if (m_asyncWrite)
                    {
                        return NotificationPending();
                    }
                    if (!IsPermittedCause(machine, causeId))
                    {
                        return ResetNotAllowed();
                    }
                    uint previous = CurrentStateCore();
                    ServiceResult result = machine.DoCause(m_context, calledMethod, causeId, default, []);
                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }
                    OnEntered(machine);
                    Settle(previous);
                }
                RaisePendingChanges();
                return ServiceResult.Good;
            };
        }

        private ServiceResult ResetNotAllowed()
        {
            return ServiceResult.Create(
                StatusCodes.BadInvalidState,
                "Reset is not allowed in state {0}.",
                CurrentStateCore());
        }

        /// <summary>
        /// Refreshes every PackML method's Executable attribute from whether
        /// its cause is permitted in the current state.
        /// </summary>
        private void UpdateExecutable()
        {
            SetExecutable(StateMachine, StateMachine.Abort, Methods.PackMLBaseStateMachineType_Abort);
            SetExecutable(StateMachine, StateMachine.Clear, Methods.PackMLBaseStateMachineType_Clear);
            SetExecutable(m_machine, m_machine.Stop, Methods.PackMLMachineStateMachineType_Stop);
            SetExecutable(m_machine, m_machine.Reset, Methods.PackMLMachineStateMachineType_Reset);
            SetExecutable(m_execute, m_execute.Reset, Methods.PackMLExecuteStateMachineType_Reset);
            SetExecutable(m_execute, m_execute.Start, Methods.PackMLExecuteStateMachineType_Start);
            SetExecutable(m_execute, m_execute.Hold, Methods.PackMLExecuteStateMachineType_Hold);
            SetExecutable(m_execute, m_execute.Unhold, Methods.PackMLExecuteStateMachineType_Unhold);
            SetExecutable(m_execute, m_execute.Suspend, Methods.PackMLExecuteStateMachineType_Suspend);
            SetExecutable(m_execute, m_execute.Unsuspend, Methods.PackMLExecuteStateMachineType_Unsuspend);
            SetExecutable(m_execute, m_execute.ToComplete, Methods.PackMLExecuteStateMachineType_ToComplete);
        }

        private void SetExecutable(FiniteStateMachineState machine, MethodState? method, uint causeId)
        {
            if (method == null)
            {
                return;
            }
            bool executable = IsPermittedCause(machine, causeId);
            method.Executable = executable;
            method.UserExecutable = executable;
        }

        private void PublishAvailable()
        {
            SetAvailableStates(
                StateMachine.AvailableStates,
                Objects.PackMLBaseStateMachineType_Aborting,
                Objects.PackMLBaseStateMachineType_Aborted,
                Objects.PackMLBaseStateMachineType_Cleared);
            SetAvailableTransitions(
                StateMachine.AvailableTransitions,
                Objects.PackMLBaseStateMachineType_AbortedToCleared,
                Objects.PackMLBaseStateMachineType_AbortingToAborted,
                Objects.PackMLBaseStateMachineType_ClearedToAborting);
            SetAvailableStates(
                m_machine.AvailableStates,
                Objects.PackMLMachineStateMachineType_Clearing,
                Objects.PackMLMachineStateMachineType_Stopped,
                Objects.PackMLMachineStateMachineType_Running,
                Objects.PackMLMachineStateMachineType_Stopping);
            SetAvailableTransitions(
                m_machine.AvailableTransitions,
                Objects.PackMLMachineStateMachineType_ClearingToStopped,
                Objects.PackMLMachineStateMachineType_StoppedToRunning,
                Objects.PackMLMachineStateMachineType_RunningToStopping,
                Objects.PackMLMachineStateMachineType_StoppingToStopped);
            SetAvailableStates(
                m_execute.AvailableStates,
                Objects.PackMLExecuteStateMachineType_Resetting,
                Objects.PackMLExecuteStateMachineType_Idle,
                Objects.PackMLExecuteStateMachineType_Starting,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_Completing,
                Objects.PackMLExecuteStateMachineType_Complete,
                Objects.PackMLExecuteStateMachineType_Holding,
                Objects.PackMLExecuteStateMachineType_Held,
                Objects.PackMLExecuteStateMachineType_Unholding,
                Objects.PackMLExecuteStateMachineType_Suspending,
                Objects.PackMLExecuteStateMachineType_Suspended,
                Objects.PackMLExecuteStateMachineType_Unsuspending);
            SetAvailableTransitions(
                m_execute.AvailableTransitions,
                Objects.PackMLExecuteStateMachineType_ResettingToIdle,
                Objects.PackMLExecuteStateMachineType_IdleToStarting,
                Objects.PackMLExecuteStateMachineType_StartingToExecute,
                Objects.PackMLExecuteStateMachineType_ExecuteToSuspending,
                Objects.PackMLExecuteStateMachineType_SuspendingToSuspended,
                Objects.PackMLExecuteStateMachineType_UnsuspendingToExecute,
                Objects.PackMLExecuteStateMachineType_ExecuteToHolding,
                Objects.PackMLExecuteStateMachineType_HoldingToHeld,
                Objects.PackMLExecuteStateMachineType_HeldToUnholding,
                Objects.PackMLExecuteStateMachineType_UnholdingToExecute,
                Objects.PackMLExecuteStateMachineType_ExecuteToCompleting,
                Objects.PackMLExecuteStateMachineType_CompletingToComplete,
                Objects.PackMLExecuteStateMachineType_CompleteToResetting,
                Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending,
                Objects.PackMLExecuteStateMachineType_StartingToHolding,
                Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding,
                Objects.PackMLExecuteStateMachineType_SuspendedToHolding,
                Objects.PackMLExecuteStateMachineType_SuspendingToHolding,
                Objects.PackMLExecuteStateMachineType_UnholdingToHolding);
        }

        /// <summary>
        /// Publishes only states included in the active mode's subset.
        /// </summary>
        private void SetAvailableStates(BaseDataVariableState<ArrayOf<NodeId>>? variable, params uint[] ids)
        {
            if (variable == null)
            {
                return;
            }
            var nodeIds = new List<NodeId>(ids.Length);
            foreach (uint id in ids)
            {
                if (IsStateSupported(id))
                {
                    nodeIds.Add(new NodeId(id, m_namespaceIndex));
                }
            }
            variable.Value = nodeIds.ToArrayOf();
        }

        /// <summary>
        /// Publishes transitions whose source and destination states are supported.
        /// </summary>
        private void SetAvailableTransitions(
            BaseDataVariableState<ArrayOf<NodeId>>? variable,
            params uint[] ids)
        {
            if (variable == null)
            {
                return;
            }
            var nodeIds = new List<NodeId>(ids.Length);
            foreach (uint id in ids)
            {
                if (IsTransitionSupported(id))
                {
                    nodeIds.Add(new NodeId(id, m_namespaceIndex));
                }
            }
            variable.Value = nodeIds.ToArrayOf();
        }

        /// <summary>
        /// Checks the active graph and nested entry states before enabling a cause.
        /// </summary>
        private bool IsPermittedCause(FiniteStateMachineState machine, uint causeId)
        {
            if (!IsActive(machine) ||
                !machine.IsCausePermitted(m_context, causeId, false) ||
                !IsCauseTransitionSupported(machine, causeId))
            {
                return false;
            }

            if (ReferenceEquals(machine, StateMachine) &&
                causeId == Methods.PackMLBaseStateMachineType_Clear)
            {
                return IsStateSupported(Objects.PackMLMachineStateMachineType_Clearing);
            }
            return !ReferenceEquals(machine, m_machine) ||
                causeId != Methods.PackMLMachineStateMachineType_Reset ||
                IsStateSupported(Objects.PackMLExecuteStateMachineType_Resetting);
        }

        /// <summary>
        /// Finds a supported outgoing transition for a cause on its owning machine.
        /// </summary>
        private bool IsCauseTransitionSupported(FiniteStateMachineState machine, uint causeId)
        {
            return SupportedCauseTransition(machine, causeId) != 0;
        }

        /// <summary>
        /// Gets the supported transition a cause performs from the owning
        /// machine's current state, or zero.
        /// </summary>
        private uint SupportedCauseTransition(FiniteStateMachineState machine, uint causeId)
        {
            uint state = StateOf(machine);
            foreach (uint transitionId in CauseTransitions(causeId))
            {
                (uint fromState, _) = TransitionEndpoints(transitionId);
                if (fromState == state && IsTransitionSupported(transitionId))
                {
                    return transitionId;
                }
            }
            return 0;
        }

        /// <summary>
        /// Maps a method identifier to its published PackML transitions.
        /// </summary>
        private static uint[] CauseTransitions(uint causeId)
        {
            return causeId switch
            {
                Methods.PackMLBaseStateMachineType_Abort =>
                    [Objects.PackMLBaseStateMachineType_ClearedToAborting],
                Methods.PackMLBaseStateMachineType_Clear =>
                    [Objects.PackMLBaseStateMachineType_AbortedToCleared],
                Methods.PackMLMachineStateMachineType_Stop =>
                    [Objects.PackMLMachineStateMachineType_RunningToStopping],
                Methods.PackMLMachineStateMachineType_Reset =>
                    [Objects.PackMLMachineStateMachineType_StoppedToRunning],
                Methods.PackMLExecuteStateMachineType_Reset =>
                    [Objects.PackMLExecuteStateMachineType_CompleteToResetting],
                Methods.PackMLExecuteStateMachineType_Start =>
                    [Objects.PackMLExecuteStateMachineType_IdleToStarting],
                Methods.PackMLExecuteStateMachineType_Hold =>
                [
                    Objects.PackMLExecuteStateMachineType_ExecuteToHolding,
                    Objects.PackMLExecuteStateMachineType_StartingToHolding,
                    Objects.PackMLExecuteStateMachineType_SuspendingToHolding,
                    Objects.PackMLExecuteStateMachineType_SuspendedToHolding,
                    Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding,
                    Objects.PackMLExecuteStateMachineType_UnholdingToHolding
                ],
                Methods.PackMLExecuteStateMachineType_Unhold =>
                    [Objects.PackMLExecuteStateMachineType_HeldToUnholding],
                Methods.PackMLExecuteStateMachineType_Suspend =>
                    [Objects.PackMLExecuteStateMachineType_ExecuteToSuspending],
                Methods.PackMLExecuteStateMachineType_Unsuspend =>
                    [Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending],
                Methods.PackMLExecuteStateMachineType_ToComplete =>
                    [Objects.PackMLExecuteStateMachineType_ExecuteToCompleting],
                _ => []
            };
        }

        /// <summary>
        /// Tests a model state identifier against the configured state numbers.
        /// </summary>
        private bool IsStateSupported(uint stateId)
        {
            return m_supportedStates.Contains(PackMLStateNumbers.FromStateId(stateId));
        }

        /// <summary>
        /// Requires both ends of a transition in the configured state subset.
        /// </summary>
        private bool IsTransitionSupported(uint transitionId)
        {
            (uint fromState, uint toState) = TransitionEndpoints(transitionId);
            return fromState != 0 &&
                m_supportedStates.Contains(PackMLStateNumbers.FromStateId(fromState)) &&
                m_supportedStates.Contains(PackMLStateNumbers.FromStateId(toState));
        }

        /// <summary>
        /// Gets the model state identifiers at each end of a PackML transition.
        /// </summary>
        private static (uint FromState, uint ToState) TransitionEndpoints(uint transitionId)
        {
            return transitionId switch
            {
                Objects.PackMLBaseStateMachineType_AbortedToCleared =>
                    (Objects.PackMLBaseStateMachineType_Aborted, Objects.PackMLBaseStateMachineType_Cleared),
                Objects.PackMLBaseStateMachineType_AbortingToAborted =>
                    (Objects.PackMLBaseStateMachineType_Aborting, Objects.PackMLBaseStateMachineType_Aborted),
                Objects.PackMLBaseStateMachineType_ClearedToAborting =>
                    (Objects.PackMLBaseStateMachineType_Cleared, Objects.PackMLBaseStateMachineType_Aborting),
                Objects.PackMLMachineStateMachineType_ClearingToStopped =>
                    (Objects.PackMLMachineStateMachineType_Clearing, Objects.PackMLMachineStateMachineType_Stopped),
                Objects.PackMLMachineStateMachineType_StoppedToRunning =>
                    (Objects.PackMLMachineStateMachineType_Stopped, Objects.PackMLMachineStateMachineType_Running),
                Objects.PackMLMachineStateMachineType_RunningToStopping =>
                    (Objects.PackMLMachineStateMachineType_Running, Objects.PackMLMachineStateMachineType_Stopping),
                Objects.PackMLMachineStateMachineType_StoppingToStopped =>
                    (Objects.PackMLMachineStateMachineType_Stopping, Objects.PackMLMachineStateMachineType_Stopped),
                Objects.PackMLExecuteStateMachineType_ResettingToIdle =>
                    (Objects.PackMLExecuteStateMachineType_Resetting, Objects.PackMLExecuteStateMachineType_Idle),
                Objects.PackMLExecuteStateMachineType_IdleToStarting =>
                    (Objects.PackMLExecuteStateMachineType_Idle, Objects.PackMLExecuteStateMachineType_Starting),
                Objects.PackMLExecuteStateMachineType_StartingToExecute =>
                    (Objects.PackMLExecuteStateMachineType_Starting, Objects.PackMLExecuteStateMachineType_Execute),
                Objects.PackMLExecuteStateMachineType_ExecuteToSuspending =>
                    (Objects.PackMLExecuteStateMachineType_Execute, Objects.PackMLExecuteStateMachineType_Suspending),
                Objects.PackMLExecuteStateMachineType_SuspendingToSuspended =>
                    (Objects.PackMLExecuteStateMachineType_Suspending, Objects.PackMLExecuteStateMachineType_Suspended),
                Objects.PackMLExecuteStateMachineType_UnsuspendingToExecute =>
                    (Objects.PackMLExecuteStateMachineType_Unsuspending, Objects.PackMLExecuteStateMachineType_Execute),
                Objects.PackMLExecuteStateMachineType_ExecuteToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Execute, Objects.PackMLExecuteStateMachineType_Holding),
                Objects.PackMLExecuteStateMachineType_HoldingToHeld =>
                    (Objects.PackMLExecuteStateMachineType_Holding, Objects.PackMLExecuteStateMachineType_Held),
                Objects.PackMLExecuteStateMachineType_HeldToUnholding =>
                    (Objects.PackMLExecuteStateMachineType_Held, Objects.PackMLExecuteStateMachineType_Unholding),
                Objects.PackMLExecuteStateMachineType_UnholdingToExecute =>
                    (Objects.PackMLExecuteStateMachineType_Unholding, Objects.PackMLExecuteStateMachineType_Execute),
                Objects.PackMLExecuteStateMachineType_ExecuteToCompleting =>
                    (Objects.PackMLExecuteStateMachineType_Execute, Objects.PackMLExecuteStateMachineType_Completing),
                Objects.PackMLExecuteStateMachineType_CompletingToComplete =>
                    (Objects.PackMLExecuteStateMachineType_Completing, Objects.PackMLExecuteStateMachineType_Complete),
                Objects.PackMLExecuteStateMachineType_CompleteToResetting =>
                    (Objects.PackMLExecuteStateMachineType_Complete, Objects.PackMLExecuteStateMachineType_Resetting),
                Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending =>
                    (Objects.PackMLExecuteStateMachineType_Suspended,
                        Objects.PackMLExecuteStateMachineType_Unsuspending),
                Objects.PackMLExecuteStateMachineType_StartingToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Starting, Objects.PackMLExecuteStateMachineType_Holding),
                Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Unsuspending, Objects.PackMLExecuteStateMachineType_Holding),
                Objects.PackMLExecuteStateMachineType_SuspendedToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Suspended, Objects.PackMLExecuteStateMachineType_Holding),
                Objects.PackMLExecuteStateMachineType_SuspendingToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Suspending, Objects.PackMLExecuteStateMachineType_Holding),
                Objects.PackMLExecuteStateMachineType_UnholdingToHolding =>
                    (Objects.PackMLExecuteStateMachineType_Unholding, Objects.PackMLExecuteStateMachineType_Holding),
                _ => (0, 0)
            };
        }

        /// <summary>
        /// Checks that replacing a subset would preserve every active nested state.
        /// </summary>
        private bool AllActiveStatesSupported(HashSet<uint> states)
        {
            return InnermostFirst().All(machine =>
            {
                uint stateId = StateOf(machine);
                return stateId == 0 || states.Contains(PackMLStateNumbers.FromStateId(stateId));
            });
        }

        /// <summary>
        /// Copies unique standard state numbers into a detached subset.
        /// </summary>
        private static bool TryCreateSupportedStates(ArrayOf<uint> stateNumbers, out HashSet<uint> states)
        {
            states = new HashSet<uint>();
            foreach (uint state in stateNumbers.Span)
            {
                if (!s_allStateNumbers.Contains(state) || !states.Add(state))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Refreshes published availability and method executability after a subset change.
        /// </summary>
        private void RefreshAvailability()
        {
            PublishAvailable();
            UpdateExecutable();
            StateMachine.ClearChangeMasks(m_context, includeChildren: true);
        }

        private void Enter(FiniteStateMachineState machine, uint stateId)
        {
            machine.SetState(m_context, stateId);
            if (machine.CurrentState != null)
            {
                machine.CurrentState.StatusCode = StatusCodes.Good;
            }
        }

        private static void Deactivate(FiniteStateMachineState machine)
        {
            if (machine.CurrentState == null)
            {
                return;
            }
            machine.CurrentState.Value = LocalizedText.Null;
            machine.CurrentState.StatusCode = StatusCodes.BadStateNotActive;
            if (machine.CurrentState.Id != null)
            {
                machine.CurrentState.Id.Value = NodeId.Null;
            }
        }

        private static bool IsActive(FiniteStateMachineState machine)
        {
            return machine.CurrentState != null &&
                StatusCode.IsGood(machine.CurrentState.StatusCode) &&
                machine.CurrentState.Id != null &&
                !machine.CurrentState.Id.Value.IsNull;
        }

        private uint StateOf(FiniteStateMachineState machine)
        {
            if (!IsActive(machine))
            {
                return 0;
            }
            return machine.GetStateId(machine.CurrentState!.Id!.Value);
        }

        private uint CurrentStateCore()
        {
            foreach (FiniteStateMachineState machine in InnermostFirst())
            {
                uint state = StateOf(machine);
                if (state != 0)
                {
                    return PackMLStateNumbers.FromStateId(state);
                }
            }
            return 0;
        }

        /// <summary>
        /// Queues a change of the innermost active state for
        /// <see cref="RaisePendingChanges"/>. Called under the lock.
        /// </summary>
        private void QueueChanged(uint previous)
        {
            uint current = CurrentStateCore();
            if (current != previous)
            {
                m_pendingChanges.Enqueue(new PackMLStateChangedEventArgs(previous, current));
            }
        }

        /// <summary>
        /// Raises the queued <see cref="StateChanged"/> events once the lock
        /// is released. One caller at a time drains the queue, so the events
        /// keep the order of the changes even when several threads - or a
        /// handler - change the state meanwhile.
        /// </summary>
        private void RaisePendingChanges()
        {
            lock (m_lock)
            {
                if (m_raisingChanges || m_pendingChanges.Count == 0)
                {
                    return;
                }
                m_raisingChanges = true;
            }

            bool drained = false;
            try
            {
                while (true)
                {
                    PackMLStateChangedEventArgs change;
                    lock (m_lock)
                    {
                        if (m_pendingChanges.Count == 0)
                        {
                            m_raisingChanges = false;
                            drained = true;
                            return;
                        }
                        change = m_pendingChanges.Dequeue();
                    }
                    StateChanged?.Invoke(this, change);
                }
            }
            finally
            {
                if (!drained)
                {
                    // A handler threw; the next change raises what is left.
                    lock (m_lock)
                    {
                        m_raisingChanges = false;
                    }
                }
            }
        }

        private readonly ISystemContext m_context;
        private readonly PackMLMachineStateMachineState m_machine;
        private readonly PackMLExecuteStateMachineState m_execute;
        private readonly ushort m_namespaceIndex;
        private readonly Lock m_lock = new();

        /// <summary>Signals completion of the current asynchronous composite operation.</summary>
        private TaskCompletionSource<bool> m_asyncOperationCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether an asynchronous composite operation owns serialization.</summary>
        private bool m_asyncOperationActive;

        /// <summary>Identifies reentry from an asynchronous operation's callback context.</summary>
        private readonly AsyncLocal<bool> m_asyncOwner = new();

        /// <summary>Whether an asynchronous operation owns synchronous write admission.</summary>
        private bool m_asyncWrite;

        /// <summary>Whether acting states complete immediately after entry.</summary>
        private bool m_autoCompleteActingStates = true;
        private readonly Queue<PackMLStateChangedEventArgs> m_pendingChanges = new();

        /// <summary>The supported state subset for each configured host-defined mode.</summary>
        private readonly Dictionary<uint, HashSet<uint>> m_modeStates = new();

        /// <summary>The state numbers supported in the active mode.</summary>
        private HashSet<uint> m_supportedStates = new(s_allStateNumbers);

        /// <summary>The active host-defined mode key.</summary>
        private uint m_currentMode = 1;

        private ArrayOf<PackMLDescriptorDataType> m_startParameters;
        private bool m_raisingChanges;

        /// <summary>The standard PackML state numbers, including nested parent states.</summary>
        private static readonly uint[] s_allStateNumbers =
        [
            PackMLStateNumbers.Clearing,
            PackMLStateNumbers.Stopped,
            PackMLStateNumbers.Starting,
            PackMLStateNumbers.Idle,
            PackMLStateNumbers.Suspended,
            PackMLStateNumbers.Execute,
            PackMLStateNumbers.Stopping,
            PackMLStateNumbers.Aborting,
            PackMLStateNumbers.Aborted,
            PackMLStateNumbers.Holding,
            PackMLStateNumbers.Held,
            PackMLStateNumbers.Unholding,
            PackMLStateNumbers.Suspending,
            PackMLStateNumbers.Unsuspending,
            PackMLStateNumbers.Resetting,
            PackMLStateNumbers.Completing,
            PackMLStateNumbers.Complete,
            PackMLStateNumbers.Running,
            PackMLStateNumbers.Cleared
        ];
    }
}
