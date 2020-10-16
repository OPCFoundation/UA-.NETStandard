/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Reflection;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class ProgramStateMachineState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            UpdateStateVariable(context, Objects.ProgramStateMachineType_Ready, CurrentState);
            UpdateTransitionVariable(context, 0, LastTransition);
        }
        #endregion
        
        #region Overriden Members
        /// <summary>
        /// The table of states belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] StateTable
        {
            get { return s_StateTable; }
        }

        /// <summary>
        /// A table of valid states.
        /// </summary>
        private ElementInfo[] s_StateTable = new ElementInfo[]
        {
            new ElementInfo(Objects.ProgramStateMachineType_Ready, BrowseNames.Ready, 1),
            new ElementInfo(Objects.ProgramStateMachineType_Running, BrowseNames.Running, 2),
            new ElementInfo(Objects.ProgramStateMachineType_Suspended, BrowseNames.Suspended, 3),
            new ElementInfo(Objects.ProgramStateMachineType_Halted, BrowseNames.Halted, 4)
        };
        
        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected override ElementInfo[] TransitionTable
        {
            get { return s_TransitionTable; }
        }

        /// <summary>
        /// A table of valid transitions.
        /// </summary>
        private ElementInfo[] s_TransitionTable = new ElementInfo[]
        {
            new ElementInfo(Objects.ProgramStateMachineType_HaltedToReady, BrowseNames.HaltedToReady, 1),
            new ElementInfo(Objects.ProgramStateMachineType_ReadyToRunning, BrowseNames.ReadyToRunning, 2),
            new ElementInfo(Objects.ProgramStateMachineType_RunningToHalted, BrowseNames.RunningToHalted, 3),
            new ElementInfo(Objects.ProgramStateMachineType_RunningToReady, BrowseNames.RunningToReady, 4),
            new ElementInfo(Objects.ProgramStateMachineType_RunningToSuspended, BrowseNames.RunningToSuspended, 5),
            new ElementInfo(Objects.ProgramStateMachineType_SuspendedToRunning, BrowseNames.SuspendedToRunning, 6),
            new ElementInfo(Objects.ProgramStateMachineType_SuspendedToHalted, BrowseNames.SuspendedToHalted, 7),
            new ElementInfo(Objects.ProgramStateMachineType_SuspendedToReady, BrowseNames.SuspendedToReady, 8),
            new ElementInfo(Objects.ProgramStateMachineType_ReadyToHalted, BrowseNames.ReadyToHalted, 9)
        };
        
        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected override uint[,] TransitionMappings
        {
            get { return s_TransitionMappings; }
        }

        /// <summary>
        /// A table of the to and from states for the transitions.
        /// </summary>
        private uint[,] s_TransitionMappings = new uint[,]
        {
            { Objects.ProgramStateMachineType_HaltedToReady, Objects.ProgramStateMachineType_Halted, Objects.ProgramStateMachineType_Ready, 1 },
            { Objects.ProgramStateMachineType_ReadyToRunning, Objects.ProgramStateMachineType_Ready, Objects.ProgramStateMachineType_Running, 1 },
            { Objects.ProgramStateMachineType_RunningToHalted, Objects.ProgramStateMachineType_Running, Objects.ProgramStateMachineType_Halted, 1 },
            { Objects.ProgramStateMachineType_RunningToReady, Objects.ProgramStateMachineType_Running, Objects.ProgramStateMachineType_Ready, 1 },
            { Objects.ProgramStateMachineType_RunningToSuspended, Objects.ProgramStateMachineType_Running, Objects.ProgramStateMachineType_Suspended, 1 },
            { Objects.ProgramStateMachineType_SuspendedToRunning, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_Running, 1 },
            { Objects.ProgramStateMachineType_SuspendedToHalted, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_Halted, 1 },
            { Objects.ProgramStateMachineType_SuspendedToReady, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_Ready, 1 },
            { Objects.ProgramStateMachineType_ReadyToHalted, Objects.ProgramStateMachineType_Ready, Objects.ProgramStateMachineType_Halted, 1 }
        };
        
        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected override uint[,] CauseMappings
        {
            get { return s_CauseMappings; }
        }
        
        /// <summary>
        /// A table of transitions for the available causes.
        /// </summary>
        private uint[,] s_CauseMappings = new uint[,]
        {
            { Methods.ProgramStateMachineType_Reset, Objects.ProgramStateMachineType_Halted, Objects.ProgramStateMachineType_HaltedToReady },
            { Methods.ProgramStateMachineType_Start, Objects.ProgramStateMachineType_Ready, Objects.ProgramStateMachineType_ReadyToRunning },
            { Methods.ProgramStateMachineType_Suspend,Objects.ProgramStateMachineType_Running,  Objects.ProgramStateMachineType_RunningToSuspended },
            { Methods.ProgramStateMachineType_Reset, Objects.ProgramStateMachineType_Running, Objects.ProgramStateMachineType_RunningToReady },
            { Methods.ProgramStateMachineType_Halt, Objects.ProgramStateMachineType_Running, Objects.ProgramStateMachineType_RunningToHalted },
            { Methods.ProgramStateMachineType_Resume, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_SuspendedToRunning },
            { Methods.ProgramStateMachineType_Reset, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_SuspendedToReady },
            { Methods.ProgramStateMachineType_Halt, Objects.ProgramStateMachineType_Suspended, Objects.ProgramStateMachineType_SuspendedToHalted }
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
            uint causeId,
            AuditUpdateStateEventState e,
            ServiceResult result)
        {            
            base.UpdateAuditEvent(
                context,
                causeMethod,
                causeId,
                e,
                result);

            // update program specific event fields.
            if (ServiceResult.IsGood(result))
            {
                ProgramTransitionAuditEventState e2 = e as ProgramTransitionAuditEventState;

                if (e2 != null)
                {
                    e2.SetChildValue(context, BrowseNames.Transition, LastTransition, false);
                }
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
        #endregion

        #region Protected Methods
        #region Start Cause Handlers
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
            return DoCause(context, method, Methods.ProgramStateMachineType_Start, inputArguments, outputArguments);
        }
        #endregion
        
        #region Suspend Cause Handlers
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
            return DoCause(context, method, Methods.ProgramStateMachineType_Suspend, inputArguments, outputArguments);
        }
        #endregion
        
        #region Resume Cause Handlers
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
            return DoCause(context, method, Methods.ProgramStateMachineType_Resume, inputArguments, outputArguments);
        }
        #endregion
        
        #region Halt Cause Handlers
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
            return DoCause(context, method, Methods.ProgramStateMachineType_Halt, inputArguments, outputArguments);
        }
        #endregion
        
        #region Reset Cause Handlers
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
            return DoCause(context, method, Methods.ProgramStateMachineType_Reset, inputArguments, outputArguments);
        }
        #endregion
        #endregion
    }
}
