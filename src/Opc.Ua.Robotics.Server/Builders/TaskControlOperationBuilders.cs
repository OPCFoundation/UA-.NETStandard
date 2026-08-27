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
    internal sealed class TaskControlOperationBuilder :
        RoboticsOperationBuilder<TaskControlOperationState, TaskControlStateMachineState>,
        ITaskControlOperationBuilder
    {
        private static readonly ArrayOf<FiniteStateMachineEntry> s_states =
        [
            new(
                TaskControlStateMachineTypeIds.StateIds.Idle,
                TaskControlStateMachineTypeIds.StateNumbers.Idle,
                "Idle"),
            new(
                TaskControlStateMachineTypeIds.StateIds.Ready,
                TaskControlStateMachineTypeIds.StateNumbers.Ready,
                "Ready"),
            new(
                TaskControlStateMachineTypeIds.StateIds.Executing,
                TaskControlStateMachineTypeIds.StateNumbers.Executing,
                "Executing")
        ];

        private static readonly ArrayOf<FiniteStateMachineEntry> s_transitions =
        [
            new(
                TaskControlStateMachineTypeIds.TransitionIds.IdleToIdle,
                TaskControlStateMachineTypeIds.TransitionNumbers.IdleToIdle,
                "IdleToIdle"),
            new(
                TaskControlStateMachineTypeIds.TransitionIds.IdleToReady,
                TaskControlStateMachineTypeIds.TransitionNumbers.IdleToReady,
                "IdleToReady"),
            new(
                TaskControlStateMachineTypeIds.TransitionIds.ReadyToIdle,
                TaskControlStateMachineTypeIds.TransitionNumbers.ReadyToIdle,
                "ReadyToIdle"),
            new(
                TaskControlStateMachineTypeIds.TransitionIds.ReadyToExecuting,
                TaskControlStateMachineTypeIds.TransitionNumbers.ReadyToExecuting,
                "ReadyToExecuting"),
            new(
                TaskControlStateMachineTypeIds.TransitionIds.ExecutingToReady,
                TaskControlStateMachineTypeIds.TransitionNumbers.ExecutingToReady,
                "ExecutingToReady"),
            new(
                TaskControlStateMachineTypeIds.TransitionIds.ExecutingToIdle,
                TaskControlStateMachineTypeIds.TransitionNumbers.ExecutingToIdle,
                "ExecutingToIdle")
        ];

        public TaskControlOperationBuilder(RoboticsBuildScope scope, TaskControlOperationState state)
            : base(
                scope,
                state,
                state.TaskControlStateMachine ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Generated TaskControlStateMachine is missing below '{0}'.",
                        state.BrowseName),
                s_states,
                s_transitions)
        {
        }

        public ITaskControlOperationBuilder OnStart(
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
                    TaskControlStateMachineTypeIds.TransitionIds.ReadyToExecuting,
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

        public ITaskControlOperationBuilder OnStop(
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

        public ITaskControlOperationBuilder OnLoadByName(
            Func<string, CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddLoadByName(Scope.Context);
            Machine.LoadByName!.OnCallAsync = async (_, _, _, name, cancellationToken) =>
            {
                RoboticsProgramResult result = await InvokeProgramAsync(
                    RoboticsOperationState.Idle,
                    RoboticsOperationState.Ready,
                    TaskControlStateMachineTypeIds.TransitionIds.IdleToReady,
                    ct => handler(name, ct),
                    cancellationToken).ConfigureAwait(false);
                return new LoadByNameMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder OnLoadByNodeId(
            Func<NodeId, CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddLoadByNodeId(Scope.Context);
            Machine.LoadByNodeId!.OnCallAsync = async (_, _, _, id, cancellationToken) =>
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(id, Scope.Context.NamespaceUris);
                RoboticsProgramResult result = await InvokeProgramAsync(
                    RoboticsOperationState.Idle,
                    RoboticsOperationState.Ready,
                    TaskControlStateMachineTypeIds.TransitionIds.IdleToReady,
                    ct => handler(nodeId, ct),
                    cancellationToken).ConfigureAwait(false);
                return new LoadByNodeIdMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder OnUnloadByName(
            Func<string, CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddUnloadByName(Scope.Context);
            Machine.UnloadByName!.OnCallAsync = async (_, _, _, name, cancellationToken) =>
            {
                RoboticsProgramResult result = await InvokeProgramAsync(
                    RoboticsOperationState.Ready,
                    RoboticsOperationState.Idle,
                    TaskControlStateMachineTypeIds.TransitionIds.ReadyToIdle,
                    ct => handler(name, ct),
                    cancellationToken).ConfigureAwait(false);
                return new UnloadByNameMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder OnUnloadByNodeId(
            Func<NodeId, CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddUnloadByNodeId(Scope.Context);
            Machine.UnloadByNodeId!.OnCallAsync = async (_, _, _, id, cancellationToken) =>
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(id, Scope.Context.NamespaceUris);
                RoboticsProgramResult result = await InvokeProgramAsync(
                    RoboticsOperationState.Ready,
                    RoboticsOperationState.Idle,
                    TaskControlStateMachineTypeIds.TransitionIds.ReadyToIdle,
                    ct => handler(nodeId, ct),
                    cancellationToken).ConfigureAwait(false);
                return new UnloadByNodeIdMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder OnUnloadProgram(
            Func<CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddUnloadProgram(Scope.Context);
            Machine.UnloadProgram!.OnCallAsync = async (_, _, _, cancellationToken) =>
            {
                RoboticsProgramResult result = await InvokeProgramAsync(
                    RoboticsOperationState.Ready,
                    RoboticsOperationState.Idle,
                    TaskControlStateMachineTypeIds.TransitionIds.ReadyToIdle,
                    handler,
                    cancellationToken).ConfigureAwait(false);
                return new UnloadProgramMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder OnResetToProgramStart(
            Func<CancellationToken, ValueTask<RoboticsProgramResult>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Scope.EnsureMutable();
            Machine.AddReadySubstateMachine(Scope.Context);
            Machine.ReadySubstateMachine!.AddResetToProgramStart(Scope.Context);
            // The parent machine completed its create lifecycle in the
            // builder constructor, so a child added afterwards has to
            // complete its own — a state machine resolves the namespace
            // qualifying its element NodeIds in OnAfterCreate.
            Machine.ReadySubstateMachine.CreateAsPredefinedNode(Scope.Context);
            Machine.ReadySubstateMachine.ResetToProgramStart!.OnCallAsync = async (_, _, _, cancellationToken) =>
            {
                RoboticsProgramResult result = await handler(cancellationToken).ConfigureAwait(false);
                return new ResetToProgramStartMethodStateResult
                {
                    ServiceResult = result.ServiceResult,
                    Status = result.Status
                };
            };
            return this;
        }

        public ITaskControlOperationBuilder WithMotionDevicesUnderControl(
            ArrayOf<NodeId> motionDevices)
        {
            Scope.EnsureMutable();
            State.AddMotionDevicesUnderControl(Scope.Context);
            RoboticsBuilderUtilities.SetValue(State.MotionDevicesUnderControl!, motionDevices);
            return this;
        }

        public ITaskControlOperationBuilder OnTransition(
            Func<RoboticsOperationTransition, CancellationToken, ValueTask> handler)
        {
            SetTransitionHandler(handler);
            return this;
        }

        protected override uint StateId(RoboticsOperationState state)
        {
            return state switch
            {
                RoboticsOperationState.Idle => TaskControlStateMachineTypeIds.StateIds.Idle,
                RoboticsOperationState.Ready => TaskControlStateMachineTypeIds.StateIds.Ready,
                RoboticsOperationState.Executing => TaskControlStateMachineTypeIds.StateIds.Executing,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };
        }

        protected override RoboticsOperationState StateFromId(uint stateId)
        {
            return stateId switch
            {
                TaskControlStateMachineTypeIds.StateIds.Idle => RoboticsOperationState.Idle,
                TaskControlStateMachineTypeIds.StateIds.Ready => RoboticsOperationState.Ready,
                TaskControlStateMachineTypeIds.StateIds.Executing => RoboticsOperationState.Executing,
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
                    TaskControlStateMachineTypeIds.TransitionIds.ReadyToExecuting,
                (RoboticsOperationState.Executing, RoboticsOperationState.Ready) =>
                    TaskControlStateMachineTypeIds.TransitionIds.ExecutingToReady,
                _ => throw new ArgumentOutOfRangeException(nameof(toState))
            };
        }
    }
}
