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
using System.Threading;

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
        public bool AutoCompleteActingStates { get; set; } = true;

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
        /// Puts the three state machines into a consistent initial state
        /// without reporting transition events, and publishes the
        /// <c>AvailableStates</c> and <c>AvailableTransitions</c> of each.
        /// </summary>
        /// <param name="initialState">The state to start in.</param>
        public void Initialize(PackMLInitialState initialState = PackMLInitialState.Stopped)
        {
            lock (m_lock)
            {
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
                    default:
                        Enter(StateMachine, Objects.PackMLBaseStateMachineType_Cleared);
                        Enter(m_machine, Objects.PackMLMachineStateMachineType_Stopped);
                        Deactivate(m_execute);
                        break;
                }
                UpdateExecutable();
                StateMachine.ClearChangeMasks(m_context, includeChildren: true);
                QueueChanged(previous);
            }
            RaisePendingChanges();
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
            RaisePendingChanges();
            return ServiceResult.Good;
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
                if (IsActive(machine) && machine.IsCausePermitted(m_context, causeId, false))
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
                    // Entering Cleared starts the machine state machine in
                    // Clearing, which is itself an acting state.
                    Enter(m_machine, Objects.PackMLMachineStateMachineType_Clearing);
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
                if (state == Objects.PackMLMachineStateMachineType_Running)
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
                if (transitionId == 0)
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
                    if (!IsActive(machine) || !machine.IsCausePermitted(m_context, causeId, false))
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
                    if (!IsActive(machine) || !machine.IsCausePermitted(m_context, causeId, false))
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
            bool executable = IsActive(machine) && machine.IsCausePermitted(m_context, causeId, false);
            method.Executable = executable;
            method.UserExecutable = executable;
        }

        private void PublishAvailable()
        {
            SetAvailable(
                StateMachine.AvailableStates,
                Objects.PackMLBaseStateMachineType_Aborting,
                Objects.PackMLBaseStateMachineType_Aborted,
                Objects.PackMLBaseStateMachineType_Cleared);
            SetAvailable(
                StateMachine.AvailableTransitions,
                Objects.PackMLBaseStateMachineType_AbortedToCleared,
                Objects.PackMLBaseStateMachineType_AbortingToAborted,
                Objects.PackMLBaseStateMachineType_ClearedToAborting);
            SetAvailable(
                m_machine.AvailableStates,
                Objects.PackMLMachineStateMachineType_Clearing,
                Objects.PackMLMachineStateMachineType_Stopped,
                Objects.PackMLMachineStateMachineType_Running,
                Objects.PackMLMachineStateMachineType_Stopping);
            SetAvailable(
                m_machine.AvailableTransitions,
                Objects.PackMLMachineStateMachineType_ClearingToStopped,
                Objects.PackMLMachineStateMachineType_StoppedToRunning,
                Objects.PackMLMachineStateMachineType_RunningToStopping,
                Objects.PackMLMachineStateMachineType_StoppingToStopped);
            SetAvailable(
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
            SetAvailable(
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

        private void SetAvailable(BaseDataVariableState<ArrayOf<NodeId>>? variable, params uint[] ids)
        {
            if (variable == null)
            {
                return;
            }
            var nodeIds = new List<NodeId>(ids.Length);
            foreach (uint id in ids)
            {
                nodeIds.Add(new NodeId(id, m_namespaceIndex));
            }
            variable.Value = nodeIds.ToArrayOf();
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
        private readonly Queue<PackMLStateChangedEventArgs> m_pendingChanges = new();
        private ArrayOf<PackMLDescriptorDataType> m_startParameters;
        private bool m_raisingChanges;
    }
}
