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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal sealed class RoboticsUserBuilder :
        RoboticsNodeBuilder<UserState>,
        IRoboticsUserBuilder
    {
        public RoboticsUserBuilder(RoboticsBuildScope scope, UserState state)
            : base(scope, state)
        {
        }

        public IRoboticsUserBuilder WithLevel(string level)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            Scope.EnsureMutable();
            RoboticsBuilderUtilities.SetValue(State.Level!, level);
            return this;
        }

        public IRoboticsUserBuilder WithName(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Scope.EnsureMutable();
            State.AddName(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.Name!, name);
            return this;
        }
    }

    internal abstract class RoboticsOperationBuilder<TState, TMachine> :
        RoboticsNodeBuilder<TState>
        where TState : BaseObjectState
        where TMachine : OperationStateMachineState
    {
        private readonly FiniteStateMachineDispatcher m_dispatcher;

        /// <summary>
        /// Serialises transitions on this operation: 0 when idle, 1 while a
        /// transition is in flight. The guard, the application handler and the
        /// commit have to be one atomic step, because the handler may run for
        /// as long as the physical motion it starts. Without this a second
        /// caller passes the same guard and a robot can be commanded to start
        /// and stand down at once, leaving the published CurrentState
        /// describing whichever commit happened to land last.
        /// </summary>
        private int m_transitionInFlight;

        private Func<RoboticsOperationTransition, CancellationToken, ValueTask>? m_transition;
        private short m_transitionReason;

        protected RoboticsOperationBuilder(
            RoboticsBuildScope scope,
            TState state,
            TMachine machine,
            ArrayOf<FiniteStateMachineEntry> states,
            ArrayOf<FiniteStateMachineEntry> transitions)
            : base(scope, state)
        {
            Machine = machine;
            m_dispatcher = new FiniteStateMachineDispatcher(
                (ushort)scope.Context.NamespaceUris.GetIndex(Namespaces.Robotics),
                states,
                transitions);
            // The generated CreateInstanceOf factory builds the node graph
            // and assigns per-instance NodeIds but leaves OnBeforeCreate /
            // OnAfterCreate to the caller. A state machine resolves the
            // namespace qualifying its element NodeIds in OnAfterCreate, so
            // the create lifecycle has to complete before the initial state
            // is written.
            Machine.CreateAsPredefinedNode(scope.Context);
            m_dispatcher.InitializeToInitialState(
                Machine,
                StateId(RoboticsOperationState.Idle),
                scope.Context);
            SetLastTransitionReason();
        }

        internal TMachine Machine { get; }

        protected void SetInitialState(RoboticsOperationState state)
        {
            Scope.EnsureMutable();
            m_dispatcher.InitializeToInitialState(Machine, StateId(state), Scope.Context);
            SetLastTransitionReason();
        }

        protected void SetTransitionHandler(
            Func<RoboticsOperationTransition, CancellationToken, ValueTask> handler)
        {
            m_transition = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        protected void SetTransitionReason(short reason)
        {
            Scope.EnsureMutable();
            m_transitionReason = reason;
            SetLastTransitionReason();
        }

        protected RoboticsOperationContext CreateContext(RoboticsOperationState currentState)
        {
            return new RoboticsOperationContext
            {
                OperationNodeId = State.NodeId,
                StateMachineNodeId = Machine.NodeId,
                CurrentState = currentState
            };
        }

        protected async ValueTask<ServiceResult> InvokeOperationAsync(
            RoboticsOperationState fromState,
            RoboticsOperationState toState,
            uint transitionId,
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler,
            CancellationToken cancellationToken)
        {
            if (!TryBeginTransition())
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }

            try
            {
                if (!IsCurrentState(fromState))
                {
                    return new ServiceResult(StatusCodes.BadInvalidState);
                }

                ServiceResult result = await handler(CreateContext(fromState), cancellationToken)
                    .ConfigureAwait(false);
                result ??= ServiceResult.Good;
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                await MoveAsync(fromState, toState, transitionId, cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }
            finally
            {
                EndTransition();
            }
        }

        protected async ValueTask<ServiceResult> InvokeStopAsync(
            long stopMode,
            Func<RoboticsStopRequest, CancellationToken, ValueTask<ServiceResult>> handler,
            CancellationToken cancellationToken)
        {
            if (!TryBeginTransition())
            {
                return new ServiceResult(StatusCodes.BadInvalidState);
            }

            try
            {
                if (!IsCurrentState(RoboticsOperationState.Executing))
                {
                    return new ServiceResult(StatusCodes.BadInvalidState);
                }

                var request = new RoboticsStopRequest
                {
                    Context = CreateContext(RoboticsOperationState.Executing),
                    StopMode = (RoboticsStopMode)(short)stopMode
                };
                ServiceResult result = await handler(request, cancellationToken).ConfigureAwait(false);
                result ??= ServiceResult.Good;
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                await MoveAsync(
                    RoboticsOperationState.Executing,
                    RoboticsOperationState.Ready,
                    TransitionId(RoboticsOperationState.Executing, RoboticsOperationState.Ready),
                    cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                EndTransition();
            }
        }

        protected async ValueTask<RoboticsProgramResult> InvokeProgramAsync(
            RoboticsOperationState fromState,
            RoboticsOperationState toState,
            uint transitionId,
            Func<CancellationToken, ValueTask<RoboticsProgramResult>> handler,
            CancellationToken cancellationToken)
        {
            if (!TryBeginTransition())
            {
                return BadInvalidStateProgramResult();
            }

            try
            {
                if (!IsCurrentState(fromState))
                {
                    return BadInvalidStateProgramResult();
                }

                RoboticsProgramResult result = await handler(cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(result.ServiceResult))
                {
                    return result;
                }

                await MoveAsync(fromState, toState, transitionId, cancellationToken)
                    .ConfigureAwait(false);
                return result;
            }
            finally
            {
                EndTransition();
            }
        }

        /// <summary>
        /// Claims the operation for one transition, without waiting.
        /// </summary>
        /// <remarks>
        /// A caller that arrives while a transition is in flight is rejected
        /// rather than queued: these methods actuate a physical machine, so
        /// blocking would let a command land long after the operator issued it.
        /// </remarks>
        /// <returns>
        /// <c>true</c> when the caller owns the transition and must call
        /// <see cref="EndTransition"/>.
        /// </returns>
        private bool TryBeginTransition()
        {
            return Interlocked.CompareExchange(ref m_transitionInFlight, 1, 0) == 0;
        }

        private void EndTransition()
        {
            Interlocked.Exchange(ref m_transitionInFlight, 0);
        }

        protected RoboticsProgramResult BadInvalidStateProgramResult()
        {
            return new RoboticsProgramResult
            {
                ServiceResult = new ServiceResult(StatusCodes.BadInvalidState)
            };
        }

        protected static int StatusFrom(ServiceResult result)
        {
            return ServiceResult.IsGood(result) ? 0 : (int)result.StatusCode.Code;
        }

        protected abstract uint StateId(RoboticsOperationState state);

        protected abstract RoboticsOperationState StateFromId(uint stateId);

        protected abstract uint TransitionId(
            RoboticsOperationState fromState,
            RoboticsOperationState toState);

        private bool IsCurrentState(RoboticsOperationState state)
        {
            return m_dispatcher.TryGetCurrentState(Machine, out uint stateId) &&
                StateFromId(stateId) == state;
        }

        private async ValueTask MoveAsync(
            RoboticsOperationState fromState,
            RoboticsOperationState toState,
            uint transitionId,
            CancellationToken cancellationToken)
        {
            m_dispatcher.Move(Machine, StateId(toState), transitionId, Scope.Context);
            SetLastTransitionReason();
            if (m_transition != null)
            {
                await m_transition(
                    new RoboticsOperationTransition
                    {
                        FromState = fromState,
                        ToState = toState,
                        TransitionId = new NodeId(
                            transitionId,
                            (ushort)Scope.Context.NamespaceUris.GetIndex(Namespaces.Robotics)),
                        Reason = m_transitionReason
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private void SetLastTransitionReason()
        {
            if (Machine.LastTransitionReason is null)
            {
                return;
            }

            Machine.LastTransitionReason.Value = m_transitionReason;
            Machine.LastTransitionReason.ClearChangeMasks(Scope.Context, includeChildren: true);
        }
    }

    internal sealed partial class SystemOperationBuilder :
        RoboticsOperationBuilder<SystemOperationState, SystemOperationStateMachineState>,
        ISystemOperationBuilder
    {
        private static readonly ArrayOf<FiniteStateMachineEntry> s_states =
        [
            new(
                SystemOperationStateMachineTypeIds.StateIds.Idle,
                SystemOperationStateMachineTypeIds.StateNumbers.Idle,
                "Idle"),
            new(
                SystemOperationStateMachineTypeIds.StateIds.Ready,
                SystemOperationStateMachineTypeIds.StateNumbers.Ready,
                "Ready"),
            new(
                SystemOperationStateMachineTypeIds.StateIds.Executing,
                SystemOperationStateMachineTypeIds.StateNumbers.Executing,
                "Executing")
        ];

        private static readonly ArrayOf<FiniteStateMachineEntry> s_transitions =
        [
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.IdleToIdle,
                SystemOperationStateMachineTypeIds.TransitionNumbers.IdleToIdle,
                "IdleToIdle"),
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.IdleToReady,
                SystemOperationStateMachineTypeIds.TransitionNumbers.IdleToReady,
                "IdleToReady"),
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.ReadyToIdle,
                SystemOperationStateMachineTypeIds.TransitionNumbers.ReadyToIdle,
                "ReadyToIdle"),
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.ReadyToExecuting,
                SystemOperationStateMachineTypeIds.TransitionNumbers.ReadyToExecuting,
                "ReadyToExecuting"),
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.ExecutingToReady,
                SystemOperationStateMachineTypeIds.TransitionNumbers.ExecutingToReady,
                "ExecutingToReady"),
            new(
                SystemOperationStateMachineTypeIds.TransitionIds.ExecutingToIdle,
                SystemOperationStateMachineTypeIds.TransitionNumbers.ExecutingToIdle,
                "ExecutingToIdle")
        ];

        public SystemOperationBuilder(RoboticsBuildScope scope, SystemOperationState state)
            : base(
                scope,
                state,
                state.SystemOperationStateMachine ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Generated SystemOperationStateMachine is missing below '{0}'.",
                        state.BrowseName),
                s_states,
                s_transitions)
        {
        }

        public ISystemOperationBuilder WithInitialState(RoboticsOperationState state)
        {
            SetInitialState(state);
            return this;
        }

        public ISystemOperationBuilder OnGetReady(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddGetReady(Scope.Context);
            Machine.GetReady!.OnCallAsync = async (_, _, _, cancellationToken) =>
            {
                ServiceResult result = await InvokeOperationAsync(
                    RoboticsOperationState.Idle,
                    RoboticsOperationState.Ready,
                    SystemOperationStateMachineTypeIds.TransitionIds.IdleToReady,
                    handler,
                    cancellationToken).ConfigureAwait(false);
                return new GetReadyMethodStateResult
                {
                    ServiceResult = result,
                    Status = StatusFrom(result)
                };
            };
            return this;
        }

        public ISystemOperationBuilder OnStart(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddStart(Scope.Context);
            Machine.Start!.OnCallAsync = async (_, _, _, cancellationToken) =>
            {
                ServiceResult result = await InvokeOperationAsync(
                    RoboticsOperationState.Ready,
                    RoboticsOperationState.Executing,
                    SystemOperationStateMachineTypeIds.TransitionIds.ReadyToExecuting,
                    handler,
                    cancellationToken).ConfigureAwait(false);
                return new StartMethodStateResult
                {
                    ServiceResult = result,
                    Status = StatusFrom(result)
                };
            };
            return this;
        }

        public ISystemOperationBuilder OnStop(
            Func<RoboticsStopRequest, CancellationToken, ValueTask<ServiceResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddStop(Scope.Context);
            Machine.Stop!.OnCallAsync = async (_, _, _, stopMode, cancellationToken) =>
            {
                ServiceResult result = await InvokeStopAsync(stopMode, handler, cancellationToken)
                    .ConfigureAwait(false);
                return new StopMethodStateResult
                {
                    ServiceResult = result,
                    Status = StatusFrom(result)
                };
            };
            return this;
        }

        public ISystemOperationBuilder OnStandDown(
            Func<RoboticsOperationContext, CancellationToken, ValueTask<ServiceResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddStandDown(Scope.Context);
            Machine.StandDown!.OnCallAsync = async (_, _, _, cancellationToken) =>
            {
                ServiceResult result = await InvokeOperationAsync(
                    RoboticsOperationState.Ready,
                    RoboticsOperationState.Idle,
                    SystemOperationStateMachineTypeIds.TransitionIds.ReadyToIdle,
                    handler,
                    cancellationToken).ConfigureAwait(false);
                return new StandDownMethodStateResult
                {
                    ServiceResult = result,
                    Status = StatusFrom(result)
                };
            };
            return this;
        }

        public ISystemOperationBuilder WithStopModes(
            ArrayOf<RoboticsStopMode> modes,
            RoboticsStopMode defaultMode)
        {
            if (!modes.Contains(defaultMode))
            {
                throw new ArgumentException(
                    "The default stop mode must be included in the possible stop modes.",
                    nameof(defaultMode));
            }

            Scope.EnsureMutable();
            Machine.AddPossibleStopModes(Scope.Context);
            Machine.AddConfiguredDefaultStopMode(Scope.Context);
            var values = new EnumValueType[modes.Count];
            for (int ii = 0; ii < modes.Count; ii++)
            {
                RoboticsStopMode mode = modes[ii];
                values[ii] = new EnumValueType
                {
                    Value = (short)mode,
                    DisplayName = new LocalizedText(mode.ToString())
                };
            }

            RoboticsBuilderUtilities.SetValue(Machine.PossibleStopModes!, new ArrayOf<EnumValueType>(values));
            RoboticsBuilderUtilities.SetValue(Machine.ConfiguredDefaultStopMode!, (short)defaultMode);
            return this;
        }

        public ISystemOperationBuilder OnTransition(
            Func<RoboticsOperationTransition, CancellationToken, ValueTask> handler)
        {
            SetTransitionHandler(handler);
            return this;
        }

        public ISystemOperationBuilder WithTransitionReason(short reason)
        {
            SetTransitionReason(reason);
            return this;
        }

        protected override uint StateId(RoboticsOperationState state)
        {
            return state switch
            {
                RoboticsOperationState.Idle => SystemOperationStateMachineTypeIds.StateIds.Idle,
                RoboticsOperationState.Ready => SystemOperationStateMachineTypeIds.StateIds.Ready,
                RoboticsOperationState.Executing => SystemOperationStateMachineTypeIds.StateIds.Executing,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };
        }

        protected override RoboticsOperationState StateFromId(uint stateId)
        {
            return stateId switch
            {
                SystemOperationStateMachineTypeIds.StateIds.Idle => RoboticsOperationState.Idle,
                SystemOperationStateMachineTypeIds.StateIds.Ready => RoboticsOperationState.Ready,
                SystemOperationStateMachineTypeIds.StateIds.Executing => RoboticsOperationState.Executing,
                _ => throw new ArgumentOutOfRangeException(nameof(stateId))
            };
        }

        protected override uint TransitionId(
            RoboticsOperationState fromState,
            RoboticsOperationState toState)
        {
            return (fromState, toState) switch
            {
                (RoboticsOperationState.Ready, RoboticsOperationState.Executing) =>
                    SystemOperationStateMachineTypeIds.TransitionIds.ReadyToExecuting,
                (RoboticsOperationState.Executing, RoboticsOperationState.Ready) =>
                    SystemOperationStateMachineTypeIds.TransitionIds.ExecutingToReady,
                _ => throw new ArgumentOutOfRangeException(nameof(toState))
            };
        }
    }
}
