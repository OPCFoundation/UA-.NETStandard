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

using System.Collections.Generic;

namespace Opc.Ua
{
    public partial class ProgramStateMachineState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ProgramStateMachineType_Ready, CurrentState);
            UpdateTransitionVariable(context, 0, LastTransition);
        }

        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] StateTable => s_stateTable;

        /// <summary>
        /// A table of valid states.
        /// </summary>
        private static readonly ElementInfo[] s_stateTable =
        [
            new(Objects.ProgramStateMachineType_Ready, BrowseNames.Ready, 1),
            new(Objects.ProgramStateMachineType_Running, BrowseNames.Running, 2),
            new(Objects.ProgramStateMachineType_Suspended, BrowseNames.Suspended, 3),
            new(Objects.ProgramStateMachineType_Halted, BrowseNames.Halted, 4)
        ];

        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] TransitionTable => s_transitionTable;

        /// <summary>
        /// A table of valid transitions.
        /// </summary>
        private static readonly ElementInfo[] s_transitionTable =
        [
            new(Objects.ProgramStateMachineType_HaltedToReady, BrowseNames.HaltedToReady, 1),
            new(Objects.ProgramStateMachineType_ReadyToRunning, BrowseNames.ReadyToRunning, 2),
            new(Objects.ProgramStateMachineType_RunningToHalted, BrowseNames.RunningToHalted, 3),
            new(Objects.ProgramStateMachineType_RunningToReady, BrowseNames.RunningToReady, 4),
            new(
                Objects.ProgramStateMachineType_RunningToSuspended,
                BrowseNames.RunningToSuspended,
                5),
            new(
                Objects.ProgramStateMachineType_SuspendedToRunning,
                BrowseNames.SuspendedToRunning,
                6),
            new(
                Objects.ProgramStateMachineType_SuspendedToHalted,
                BrowseNames.SuspendedToHalted,
                7),
            new(Objects.ProgramStateMachineType_SuspendedToReady, BrowseNames.SuspendedToReady, 8),
            new(Objects.ProgramStateMachineType_ReadyToHalted, BrowseNames.ReadyToHalted, 9)
        ];

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected override uint[,] TransitionMappings => s_transitionMappings;

        /// <summary>
        /// A table of the to and from states for the transitions.
        /// </summary>
        private static readonly uint[,] s_transitionMappings = new uint[,]
        {
            {
                Objects.ProgramStateMachineType_HaltedToReady,
                Objects.ProgramStateMachineType_Halted,
                Objects.ProgramStateMachineType_Ready,
                1
            },
            {
                Objects.ProgramStateMachineType_ReadyToRunning,
                Objects.ProgramStateMachineType_Ready,
                Objects.ProgramStateMachineType_Running,
                1
            },
            {
                Objects.ProgramStateMachineType_RunningToHalted,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Halted,
                1
            },
            {
                Objects.ProgramStateMachineType_RunningToReady,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Ready,
                1
            },
            {
                Objects.ProgramStateMachineType_RunningToSuspended,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_Suspended,
                1
            },
            {
                Objects.ProgramStateMachineType_SuspendedToRunning,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_Running,
                1
            },
            {
                Objects.ProgramStateMachineType_SuspendedToHalted,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_Halted,
                1
            },
            {
                Objects.ProgramStateMachineType_SuspendedToReady,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_Ready,
                1
            },
            {
                Objects.ProgramStateMachineType_ReadyToHalted,
                Objects.ProgramStateMachineType_Ready,
                Objects.ProgramStateMachineType_Halted,
                1
            }
        };

        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected override uint[,] CauseMappings => m_causeMappings;

        /// <summary>
        /// A table of transitions for the available causes.
        /// </summary>
        private readonly uint[,] m_causeMappings = new uint[,]
        {
            {
                Methods.ProgramStateMachineType_Reset,
                Objects.ProgramStateMachineType_Halted,
                Objects.ProgramStateMachineType_HaltedToReady
            },
            {
                Methods.ProgramStateMachineType_Start,
                Objects.ProgramStateMachineType_Ready,
                Objects.ProgramStateMachineType_ReadyToRunning
            },
            {
                Methods.ProgramStateMachineType_Suspend,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_RunningToSuspended
            },
            {
                Methods.ProgramStateMachineType_Halt,
                Objects.ProgramStateMachineType_Running,
                Objects.ProgramStateMachineType_RunningToHalted
            },
            {
                Methods.ProgramStateMachineType_Resume,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_SuspendedToRunning
            },
            {
                Methods.ProgramStateMachineType_Reset,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_SuspendedToReady
            },
            {
                Methods.ProgramStateMachineType_Halt,
                Objects.ProgramStateMachineType_Suspended,
                Objects.ProgramStateMachineType_SuspendedToHalted
            },
            {
                Methods.ProgramStateMachineType_Halt,
                Objects.ProgramStateMachineType_Ready,
                Objects.ProgramStateMachineType_ReadyToHalted
            }
        };

        /// <summary>
        /// Creates an instance of an audit event.
        /// </summary>
        protected override AuditUpdateStateEventState CreateAuditEvent(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId)
        {
            return new ProgramTransitionAuditEventState(null);
        }

        /// <summary>
        /// Updates an audit event after the method is invoked.
        /// </summary>
        protected override void UpdateAuditEvent(
            ISystemContext context,
            MethodState causeMethod,
            IList<object> inputArguments,
            uint causeId,
            AuditUpdateStateEventState e,
            ServiceResult result)
        {
            base.UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);

            // update program specific event fields.
            if (ServiceResult.IsGood(result) && e is ProgramTransitionAuditEventState e2)
            {
                e2.SetChildValue(context, BrowseNames.Transition, LastTransition, false);
            }
        }

        /// <summary>
        /// Creates an instance of an transition event.
        /// </summary>
        protected override TransitionEventState CreateTransitionEvent(
            ISystemContext context,
            uint transitionId,
            uint causeId)
        {
            if (TransitionHasEffect(context, transitionId))
            {
                return new ProgramTransitionEventState(null);
            }

            return null;
        }

        /// <summary>
        /// Checks whether the start method is executable.
        /// </summary>
        protected ServiceResult IsStartExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Start, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the start method is executable by the current user.
        /// </summary>
        protected ServiceResult IsStartUserExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Start, true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the start method.
        /// </summary>
        protected virtual ServiceResult OnStart(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return DoCause(
                context,
                method,
                Methods.ProgramStateMachineType_Start,
                inputArguments,
                outputArguments);
        }

        /// <summary>
        /// Checks whether the suspend method is executable.
        /// </summary>
        protected ServiceResult IsSuspendExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Suspend, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the suspend method is executable by the current user.
        /// </summary>
        protected ServiceResult IsSuspendUserExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Suspend, true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the suspend method.
        /// </summary>
        protected virtual ServiceResult OnSuspend(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return DoCause(
                context,
                method,
                Methods.ProgramStateMachineType_Suspend,
                inputArguments,
                outputArguments);
        }

        /// <summary>
        /// Checks whether the resume method is executable.
        /// </summary>
        protected ServiceResult IsResumeExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Resume, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the resume method is executable by the current user.
        /// </summary>
        protected ServiceResult IsResumeUserExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Resume, true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the resume method.
        /// </summary>
        protected virtual ServiceResult OnResume(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return DoCause(
                context,
                method,
                Methods.ProgramStateMachineType_Resume,
                inputArguments,
                outputArguments);
        }

        /// <summary>
        /// Checks whether the halt method is executable.
        /// </summary>
        protected ServiceResult IsHaltExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Halt, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the halt method is executable by the current user.
        /// </summary>
        protected ServiceResult IsHaltUserExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Halt, true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the halt method.
        /// </summary>
        protected virtual ServiceResult OnHalt(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return DoCause(
                context,
                method,
                Methods.ProgramStateMachineType_Halt,
                inputArguments,
                outputArguments);
        }

        /// <summary>
        /// Checks whether the reset method is executable.
        /// </summary>
        protected ServiceResult IsResetExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Reset, false);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Checks whether the reset method is executable by the current user.
        /// </summary>
        protected ServiceResult IsResetUserExecutable(
            ISystemContext context,
            NodeState node,
            ref bool value)
        {
            value = IsCausePermitted(context, Methods.ProgramStateMachineType_Reset, true);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the reset method.
        /// </summary>
        protected virtual ServiceResult OnReset(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return DoCause(
                context,
                method,
                Methods.ProgramStateMachineType_Reset,
                inputArguments,
                outputArguments);
        }
    }
}
