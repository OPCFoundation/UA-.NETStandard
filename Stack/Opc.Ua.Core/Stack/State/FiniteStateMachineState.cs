/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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

namespace Opc.Ua
{
    public partial class FiniteStateMachineState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

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
        protected virtual ElementInfo[] StateTable => null;

        /// <summary>
        /// The table of transitions belonging to the state machine.
        /// </summary>
        protected virtual ElementInfo[] TransitionTable => null;

        /// <summary>
        /// The mapping between transitions and their from and to states.
        /// </summary>
        protected virtual uint[,] TransitionMappings => null;

        /// <summary>
        /// The mapping between causes, the current state and a transition.
        /// </summary>
        protected virtual uint[,] CauseMappings => null;

        /// <summary>
        /// The last state that the machine was in.
        /// </summary>
        protected FiniteStateVariableState LastState { get; set; }

        /// <summary>
        /// Returns the current state of for the state machine.
        /// </summary>
        protected uint GetCurrentStateId()
        {
            if (CurrentState == null || CurrentState.Id == null || CurrentState.Value == null)
            {
                return 0;
            }

            NodeId value = CurrentState.Id.Value;

            if (ElementNamespaceIndex != value.NamespaceIndex || value.IdType != IdType.Numeric)
            {
                return 0;
            }

            return (uint)value.Identifier;
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

            uint[,] transitionMappings = TransitionMappings;

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
            uint[,] transitionMappings = TransitionMappings;

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

            uint[,] causeMappings = CauseMappings;

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

            uint[,] transitionMappings = TransitionMappings;

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
            FiniteStateVariableState variable)
        {
            if (variable == null)
            {
                return;
            }

            if (stateId == 0)
            {
                variable.Value = null;
                variable.Id.Value = null;

                if (variable.Number != null)
                {
                    variable.Number.Value = 0;
                }

                return;
            }

            ElementInfo[] stateTable = StateTable;

            if (stateTable == null)
            {
                return;
            }

            for (int ii = 0; ii < stateTable.Length; ii++)
            {
                ElementInfo state = stateTable[ii];

                if (state.Id == stateId)
                {
                    variable.Value = state.Name;
                    variable.Id.Value = new NodeId(state.Id, ElementNamespaceIndex);

                    if (variable.Number != null)
                    {
                        variable.Number.Value = state.Number;
                    }

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
            FiniteTransitionVariableState variable)
        {
            if (variable == null)
            {
                return;
            }

            if (transitionId == 0)
            {
                variable.Value = null;
                variable.Id.Value = null;

                if (variable.TransitionTime != null)
                {
                    variable.TransitionTime.Value = DateTime.MinValue;
                }

                if (variable.Number != null)
                {
                    variable.Number.Value = 0;
                }

                return;
            }

            ElementInfo[] transitionTable = TransitionTable;

            if (transitionTable == null)
            {
                return;
            }

            for (int ii = 0; ii < transitionTable.Length; ii++)
            {
                ElementInfo transition = transitionTable[ii];

                if (transition.Id == transitionId)
                {
                    variable.Value = transition.Name;
                    variable.Id.Value = new NodeId(transition.Id, ElementNamespaceIndex);

                    if (variable.TransitionTime != null)
                    {
                        variable.TransitionTime.Value = DateTime.UtcNow;
                    }

                    if (variable.Number != null)
                    {
                        variable.Number.Value = transition.Number;
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Raised to check whether the current user is allowed to execute the command.
        /// </summary>
        public StateMachineTransitionHandler OnCheckUserPermission;

        /// <summary>
        /// Raised before a transition occurs.
        /// </summary>
        public StateMachineTransitionHandler OnBeforeTransition;

        /// <summary>
        /// Raises after a transition occurs. Errors are ignored.
        /// </summary>
        public StateMachineTransitionHandler OnAfterTransition;

        /// <summary>
        /// If true transition events will not be produced by the state machine.
        /// </summary>
        public bool SuppressTransitionEvents { get; set; }

        /// <summary>
        /// Invokes the callback function if it has been specified.
        /// </summary>
        protected ServiceResult InvokeCallback(
            StateMachineTransitionHandler callback,
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            IList<object> inputArguments,
            IList<object> outputArguments)
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
                    null,
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
            uint transitionId = GetTransitionToState(context, newState);

            UpdateStateVariable(context, newState, CurrentState);
            UpdateTransitionVariable(context, transitionId, LastTransition);
        }

        /// <summary>
        /// Invokes the specified cause.
        /// </summary>
        public virtual ServiceResult DoCause(
            ISystemContext context,
            MethodState causeMethod,
            uint causeId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult result = null;

            try
            {
                // get the transition.
                uint transitionId = GetTransitionForCause(context, causeId);

                if (transitionId == 0)
                {
                    return StatusCodes.BadNotSupported;
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
                result = DoTransition(
                    context,
                    transitionId,
                    causeId,
                    inputArguments,
                    outputArguments);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                // report any changes to state machine.
                ClearChangeMasks(context, true);
            }
            finally
            {
                // report the event.
                if (AreEventsMonitored)
                {
                    AuditUpdateStateEventState e = CreateAuditEvent(context, causeMethod, causeId);
                    UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);
                    ReportEvent(context, e);

                    if (m_causeId != causeId)
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

            return result;
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
            IList<object> inputArguments,
            uint causeId,
            AuditUpdateStateEventState e,
            ServiceResult result)
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
            e.SetChildValue(context, BrowseNames.LocalTime, Utils.GetTimeZoneInfo(), false);

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
            IList<object> inputArguments,
            ServiceResult result)
        {
            try
            {
                var e = new AuditProgramTransitionEventState(null);

                UpdateAuditEvent(context, causeMethod, inputArguments, causeId, e, result);

                e.SetChildValue(
                    context,
                    BrowseNames.TransitionNumber,
                    LastTransition.Number.Value,
                    false);

                ReportEvent(context, e);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Error while reporting AuditProgramTransitionEvent event.");
            }
        }

        /// <summary>
        /// Updates the state machine to reflect the successful processing of a method.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="causeId">The cause id.</param>
        public virtual void CauseProcessingCompleted(ISystemContext context, uint causeId)
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
            (LastState ??= new FiniteStateVariableState(this)).SetChildValue(
                context,
                null,
                CurrentState,
                false);

            // update state and transition variables.
            UpdateStateVariable(context, newState, CurrentState);
            UpdateTransitionVariable(context, transitionId, LastTransition);
        }

        /// <summary>
        /// Causes the specified transition to occur.
        /// </summary>
        public virtual ServiceResult DoTransition(
            ISystemContext context,
            uint transitionId,
            uint causeId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
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
            ServiceResult result = InvokeCallback(
                OnBeforeTransition,
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

            // save the last state.
            (LastState ??= new FiniteStateVariableState(this)).SetChildValue(
                context,
                null,
                CurrentState,
                false);

            // update state and transition variables.
            UpdateStateVariable(context, newState, CurrentState);
            UpdateTransitionVariable(context, transitionId, LastTransition);

            // do any post-transition processing.
            InvokeCallback(
                OnAfterTransition,
                context,
                this,
                transitionId,
                causeId,
                inputArguments,
                outputArguments);

            // report the event.
            if (AreEventsMonitored && !SuppressTransitionEvents)
            {
                TransitionEventState e = CreateTransitionEvent(context, transitionId, causeId);

                if (e != null)
                {
                    UpdateTransitionEvent(context, transitionId, causeId, e);
                    ReportEvent(context, e);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates an instance of an transition event.
        /// </summary>
        protected virtual TransitionEventState CreateTransitionEvent(
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
                CurrentState.Value);

            e.Initialize(context, this, EventSeverity.Medium, new LocalizedText(info));

            e.SetChildValue(context, BrowseNames.FromState, LastState, false);
            e.SetChildValue(context, BrowseNames.ToState, CurrentState, false);
            e.SetChildValue(context, BrowseNames.Transition, LastTransition, false);
        }

        private uint m_causeId;
    }

    /// <summary>
    /// A delegate used to receive notifications when a state machine transition occurs.
    /// </summary>
    public delegate ServiceResult StateMachineTransitionHandler(
        ISystemContext context,
        StateMachineState machine,
        uint transitionId,
        uint causeId,
        IList<object> inputArguments,
        IList<object> outputArguments);
}
